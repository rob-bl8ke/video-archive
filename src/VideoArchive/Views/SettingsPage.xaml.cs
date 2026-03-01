using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VideoArchive.Data;
using VideoArchive.Helpers;
using VideoArchive.Models;
using VideoArchive.Services;
using VideoArchive.ViewModels;

namespace VideoArchive.Views;

public sealed partial class SettingsPage : UserControl
{
    private SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        this.InitializeComponent();

        FoldersList.ItemsSource = ViewModel.Folders;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.Folders))
                FoldersList.ItemsSource = ViewModel.Folders;
            if (e.PropertyName == nameof(SettingsViewModel.StatusText))
                StatusText.Text = ViewModel.StatusText;
        };

        this.Loaded += async (_, _) =>
        {
            await ViewModel.LoadFoldersCommand.ExecuteAsync(null);

            // Load segment settings
            var settings = App.Services.GetRequiredService<ISettingsService>();
            DefaultDurationBox.Value = settings.DefaultSegmentDurationSeconds;
            MinDurationBox.Value = settings.MinSegmentDurationSeconds;

            // Set theme combo to current value
            if (this.XamlRoot?.Content is FrameworkElement rootElement)
            {
                var theme = rootElement.RequestedTheme;
                ThemeCombo.SelectedIndex = theme switch
                {
                    ElementTheme.Light => 1,
                    ElementTheme.Dark => 2,
                    _ => 0,
                };
            }
            else
            {
                ThemeCombo.SelectedIndex = 0;
            }
        };
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.VideosLibrary;
        picker.FileTypeFilter.Add("*");

        var mainWindow = WindowHelper.ActiveWindows.FirstOrDefault();
        if (mainWindow is null) return;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        using var scope = App.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<VideoArchiveContext>();

        if (await context.LibraryFolders.AnyAsync(f => f.Path == folder.Path))
        {
            ViewModel.StatusText = "Folder already exists.";
            return;
        }

        var entity = new LibraryFolder { Path = folder.Path };
        context.LibraryFolders.Add(entity);
        await context.SaveChangesAsync();
        ViewModel.Folders.Add(entity);
        ViewModel.StatusText = $"Added: {folder.Path}";
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is LibraryFolder folder)
            ViewModel.RemoveFolderCommand.Execute(folder);
    }

    private async void RefreshLibrary_Click(object sender, RoutedEventArgs e)
    {
        bool hasFolders;
        using (var scope = App.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<VideoArchiveContext>();
            hasFolders = await context.LibraryFolders.AnyAsync(f => f.IsActive);
        }

        if (!hasFolders)
        {
            ViewModel.StatusText = "No active library folders. Add a folder first.";
            return;
        }

        var cts = new CancellationTokenSource();
        var progressText = new TextBlock { Text = "Preparing scan..." };
        var progressBar = new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 12, 0, 0) };
        var panel = new StackPanel();
        panel.Children.Add(progressText);
        panel.Children.Add(progressBar);

        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = "Scanning Library",
            Content = panel,
            CloseButtonText = "Cancel",
        };
        dialog.CloseButtonClick += (_, _) => cts.Cancel();

        var progress = new Progress<(int current, int total)>(p =>
        {
            progressBar.IsIndeterminate = false;
            progressBar.Maximum = p.total;
            progressBar.Value = p.current;
            progressText.Text = $"Processing: {p.current} / {p.total} videos";
        });

        var scanner = App.Services.GetRequiredService<ILibraryScanner>();
        DialogHelper.HideOverlay();
        _ = dialog.ShowAsync();

        try
        {
            var result = await Task.Run(() => scanner.ScanAsync(progress, cts.Token));
            var summary = $"Scan complete — {result.NewVideos} added, {result.RemovedVideos} removed.";
            if (result.Errors > 0)
                summary += $" {result.Errors} file(s) had errors.";
            ViewModel.StatusText = summary;
        }
        catch (OperationCanceledException)
        {
            ViewModel.StatusText = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = $"Scan error: {ex.Message}";
        }

        dialog.Hide();
        DialogHelper.ShowOverlay();
        await ViewModel.LoadFoldersCommand.ExecuteAsync(null);

        // Reload video list in the shared MainViewModel so Library views update
        var mainVm = App.Services.GetRequiredService<MainViewModel>();
        await mainVm.LoadVideosCommand.ExecuteAsync(null);
    }

    private void CleanThumbnails_Click(object sender, RoutedEventArgs e)
    {
        var thumbService = App.Services.GetRequiredService<IThumbnailService>();
        thumbService.CleanOrphaned();
        ViewModel.StatusText = "Orphaned thumbnails cleaned.";
    }

    private async void RebuildThumbnails_Click(object sender, RoutedEventArgs e)
    {
        var cts = new CancellationTokenSource();
        var progressText = new TextBlock { Text = "Preparing..." };
        var progressBar = new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 12, 0, 0) };
        var panel = new StackPanel();
        panel.Children.Add(progressText);
        panel.Children.Add(progressBar);

        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = "Rebuilding Thumbnails",
            Content = panel,
            CloseButtonText = "Cancel",
        };
        dialog.CloseButtonClick += (_, _) => cts.Cancel();

        var progress = new Progress<(int current, int total)>(p =>
        {
            progressBar.IsIndeterminate = false;
            progressBar.Maximum = p.total;
            progressBar.Value = p.current;
            progressText.Text = $"Generating: {p.current} / {p.total}";
        });

        var thumbService = App.Services.GetRequiredService<IThumbnailService>();
        DialogHelper.HideOverlay();
        _ = dialog.ShowAsync();

        try
        {
            await Task.Run(() => thumbService.RebuildAllAsync(progress, cts.Token));
            ViewModel.StatusText = "All thumbnails rebuilt.";
        }
        catch (OperationCanceledException)
        {
            ViewModel.StatusText = "Thumbnail rebuild cancelled.";
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = $"Rebuild error: {ex.Message}";
        }

        dialog.Hide();
        DialogHelper.ShowOverlay();
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            var theme = tag switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };

            if (this.XamlRoot?.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = theme;
            }
        }
    }

    private async void ManageTags_Click(object sender, RoutedEventArgs e)
    {
        await TagManagerDialog.ShowAsync(this.XamlRoot);
    }

    private void DefaultDurationBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (double.IsNaN(args.NewValue)) return;
        var settings = App.Services.GetRequiredService<ISettingsService>();
        settings.DefaultSegmentDurationSeconds = (int)args.NewValue;
    }

    private void MinDurationBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (double.IsNaN(args.NewValue)) return;
        var settings = App.Services.GetRequiredService<ISettingsService>();
        settings.MinSegmentDurationSeconds = (int)args.NewValue;
    }
}
