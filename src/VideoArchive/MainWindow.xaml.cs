using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using VideoArchive.Helpers;
using VideoArchive.Services;
using VideoArchive.ViewModels;
using Windows.Graphics;

namespace VideoArchive;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    private readonly ISettingsService _settings;

    public MainWindow()
    {
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        _settings = App.Services.GetRequiredService<ISettingsService>();
        this.InitializeComponent();

        // Restore window size and position
        RestoreWindowPlacement();

        // Save window placement on close
        this.Closed += MainWindow_Closed;

        // React to view mode and navigation changes
        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsGalleryView))
                SearchPanelControl.ScrollToSelected();

            if (e.PropertyName == nameof(MainViewModel.ActivePlaybackView))
                UpdatePlaybackView();

            if (e.PropertyName == nameof(MainViewModel.ActivePanel))
                UpdateActivePanel();
        };

        // Select Search & Playback by default
        NavView.SelectedItem = NavView.MenuItems[0];

        // Initial panel state
        UpdateActivePanel();
        UpdatePlaybackView();

        // Load existing videos from DB on startup (one-shot)
        bool _loaded = false;
        this.Activated += async (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            await ViewModel.LoadVideosCommand.ExecuteAsync(null);
        };
    }

    private void RestoreWindowPlacement()
    {
        var width = (int)_settings.WindowWidth;
        var height = (int)_settings.WindowHeight;
        var left = (int)_settings.WindowLeft;
        var top = (int)_settings.WindowTop;

        if (width > 100 && height > 100)
        {
            var appWindow = this.AppWindow;
            appWindow.MoveAndResize(new RectInt32(left, top, width, height));
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        var appWindow = this.AppWindow;
        _settings.WindowWidth = appWindow.Size.Width;
        _settings.WindowHeight = appWindow.Size.Height;
        _settings.WindowLeft = appWindow.Position.X;
        _settings.WindowTop = appWindow.Position.Y;
    }

    /// <summary>
    /// Stops the player panel timer before LibVLC resources are disposed.
    /// </summary>
    public void ShutdownPlayer() => PlayerPanelControl.Shutdown();

    /// <summary>Hide the native video overlay (call before showing a dialog).</summary>
    public void HideVideoOverlay() => PlayerPanelControl.HideOverlay();

    /// <summary>Show the native video overlay (call after a dialog closes).</summary>
    public void ShowVideoOverlay() => PlayerPanelControl.ShowOverlay();

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string tag)
        {
            ViewModel.ActivePanel = tag switch
            {
                "TagManagement" => MainPanel.TagManagement,
                "Settings"      => MainPanel.Settings,
                _               => MainPanel.SearchPlayback,
            };
        }
    }

    private void UpdateActivePanel()
    {
        var isSearch = ViewModel.ActivePanel == MainPanel.SearchPlayback;
        var isTagMgmt = ViewModel.ActivePanel == MainPanel.TagManagement;
        var isSettings = ViewModel.ActivePanel == MainPanel.Settings;

        SearchPlaybackHost.Visibility = isSearch ? Visibility.Visible : Visibility.Collapsed;
        TagsPanelControl.Visibility  = isTagMgmt ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanelControl.Visibility = isSettings ? Visibility.Visible : Visibility.Collapsed;

        // TabsPanel only appears in Search & Playback
        TabsPanelControl.Visibility = isSearch ? Visibility.Visible : Visibility.Collapsed;

        // Hide the native video window when leaving Search & Playback
        if (!isSearch)
            PlayerPanelControl.HideOverlay();
        else
            UpdatePlaybackView();
    }

    private void UpdatePlaybackView()
    {
        var isPlayer = ViewModel.ActivePlaybackView == PlaybackView.PlayerView;

        SearchPanelControl.Visibility  = isPlayer ? Visibility.Collapsed : Visibility.Visible;
        PlayerPanelControl.Visibility  = isPlayer ? Visibility.Visible   : Visibility.Collapsed;

        if (isPlayer)
            PlayerPanelControl.ShowOverlay();
        else
            PlayerPanelControl.HideOverlay();
    }

}

