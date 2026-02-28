using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using VideoArchive.Data;
using VideoArchive.Helpers;
using VideoArchive.Models;
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

        // React to view mode changes
        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsGalleryView))
            {
                UpdateViewVisibility();
                // Keep toggle buttons in sync
                DetailsToggle.IsChecked = !ViewModel.IsGalleryView;
            }
            if (e.PropertyName == nameof(MainViewModel.IsPlayerVisible))
            {
                // Resize the player row when the panel toggles
                PlayerRow.Height = ViewModel.IsPlayerVisible
                    ? new GridLength(2, GridUnitType.Star)
                    : new GridLength(0);
            }
        };

        // Disable library/tag toolbar buttons while video is playing
        var playerVm = App.Services.GetRequiredService<VideoPlayerViewModel>();
        playerVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VideoPlayerViewModel.CanInteract))
            {
                AddFolderButton.IsEnabled = playerVm.CanInteract;
                RefreshButton.IsEnabled = playerVm.CanInteract;
                ManageTagsButton.IsEnabled = playerVm.CanInteract;
            }
        };

        // Select Library nav item by default
        NavView.SelectedItem = NavView.MenuItems[0];
        DetailsToggle.IsChecked = !ViewModel.IsGalleryView;
        UpdateViewVisibility();

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
            var isLibrary = tag == "Library";
            SettingsPageControl.Visibility = isLibrary ? Visibility.Collapsed : Visibility.Visible;
            GalleryViewControl.Visibility = isLibrary && ViewModel.IsGalleryView ? Visibility.Visible : Visibility.Collapsed;
            DetailsViewControl.Visibility = isLibrary && !ViewModel.IsGalleryView ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void DetailsToggle_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsGalleryView = false;
        UpdateViewVisibility();
    }

    private void UpdateViewVisibility()
    {
        var isLibrary = SettingsPageControl.Visibility == Visibility.Collapsed;
        if (isLibrary)
        {
            GalleryViewControl.Visibility = ViewModel.IsGalleryView ? Visibility.Visible : Visibility.Collapsed;
            DetailsViewControl.Visibility = ViewModel.IsGalleryView ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private async void AddFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.VideosLibrary;
        picker.FileTypeFilter.Add("*");

        // Initialize with window handle (required for unpackaged apps)
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        using var scope = App.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<VideoArchiveContext>();

        // Skip if folder is already registered
        if (await context.LibraryFolders.AnyAsync(f => f.Path == folder.Path))
            return;

        context.LibraryFolders.Add(new LibraryFolder { Path = folder.Path });
        await context.SaveChangesAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        // Check for library folders
        bool hasFolders;
        using (var scope = App.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<VideoArchiveContext>();
            hasFolders = await context.LibraryFolders.AnyAsync(f => f.IsActive);
        }

        if (!hasFolders)
        {
            var noFolderDialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "No Library Folders",
                Content = "No library folders configured. Use the \"Add Library Folder\" button to add a video folder first.",
                CloseButtonText = "OK",
            };
            await DialogHelper.ShowWithOverlayHiddenAsync(noFolderDialog);
            return;
        }

        // Build progress UI
        var cts = new CancellationTokenSource();
        var progressText = new TextBlock { Text = "Preparing scan..." };
        var progressBar = new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 12, 0, 0) };
        var panel = new StackPanel();
        panel.Children.Add(progressText);
        panel.Children.Add(progressBar);

        var progressDialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Scanning Library",
            Content = panel,
            CloseButtonText = "Cancel",
        };
        progressDialog.CloseButtonClick += (_, _) => cts.Cancel();

        var progress = new Progress<(int current, int total)>(p =>
        {
            progressBar.IsIndeterminate = false;
            progressBar.Maximum = p.total;
            progressBar.Value = p.current;
            progressText.Text = $"Processing: {p.current} / {p.total} videos";
        });

        var scanner = App.Services.GetRequiredService<ILibraryScanner>();

        // Show dialog and run scan concurrently
        HideVideoOverlay();
        _ = progressDialog.ShowAsync();

        ScanResult? result = null;
        try
        {
            result = await Task.Run(() => scanner.ScanAsync(progress, cts.Token));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            progressDialog.Hide();
            var errorDialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Scan Error",
                Content = $"An error occurred during scanning:\n{ex.Message}",
                CloseButtonText = "OK",
            };
            await DialogHelper.ShowWithOverlayHiddenAsync(errorDialog);
            ShowVideoOverlay();
            return;
        }

        progressDialog.Hide();

        // Reload video list
        await ViewModel.LoadVideosCommand.ExecuteAsync(null);

        // Show scan summary
        if (result is not null)
        {
            var summary = $"Added {result.NewVideos} video(s), removed {result.RemovedVideos}.";
            if (result.Errors > 0)
                summary += $"\n{result.Errors} file(s) could not be read (corrupted or unsupported format).";

            var summaryDialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Scan Complete",
                Content = summary,
                CloseButtonText = "OK",
            };
            await DialogHelper.ShowWithOverlayHiddenAsync(summaryDialog);
        }

        ShowVideoOverlay();
    }

    private async void ManageTagsButton_Click(object sender, RoutedEventArgs e)
    {
        await VideoArchive.Views.TagManagerDialog.ShowAsync(Content.XamlRoot);
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ViewModel.SearchText = sender.Text;
        }
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        ViewModel.SearchText = args.QueryText ?? string.Empty;
    }
}
