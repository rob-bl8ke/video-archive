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

public sealed partial class SearchPanel : UserControl
{
    public MainViewModel ViewModel { get; }

    public SearchPanel()
    {
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        this.InitializeComponent();

        UpdateViewVisibility();
        DetailsToggle.IsChecked = !ViewModel.IsGalleryView;

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsGalleryView))
            {
                UpdateViewVisibility();
                DetailsToggle.IsChecked = !ViewModel.IsGalleryView;
            }
        };
    }

    private void UpdateViewVisibility()
    {
        GalleryViewControl.Visibility = ViewModel.IsGalleryView ? Visibility.Visible : Visibility.Collapsed;
        DetailsViewControl.Visibility = ViewModel.IsGalleryView ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Scrolls the active sub-view to the currently selected video.
    /// </summary>
    public void ScrollToSelected()
    {
        if (ViewModel.IsGalleryView)
            GalleryViewControl.ScrollToSelected();
        else
            DetailsViewControl.ScrollToSelected();
    }

    private void DetailsToggle_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsGalleryView = false;
        DetailsToggle.IsChecked = true;
        GalleryToggle.IsChecked = false;
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            ViewModel.SearchText = sender.Text;
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        ViewModel.SearchText = args.QueryText ?? string.Empty;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
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
                XamlRoot = this.XamlRoot,
                Title = "No Library Folders",
                Content = "No library folders configured. Go to Settings to add a video folder first.",
                CloseButtonText = "OK",
            };
            await DialogHelper.ShowWithOverlayHiddenAsync(noFolderDialog);
            return;
        }

        var cts = new CancellationTokenSource();
        var progressText = new TextBlock { Text = "Preparing scan..." };
        var progressBar = new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 12, 0, 0) };
        var panel = new StackPanel();
        panel.Children.Add(progressText);
        panel.Children.Add(progressBar);

        var progressDialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
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
        DialogHelper.HideOverlay();
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
                XamlRoot = this.XamlRoot,
                Title = "Scan Error",
                Content = $"An error occurred during scanning:\n{ex.Message}",
                CloseButtonText = "OK",
            };
            await DialogHelper.ShowWithOverlayHiddenAsync(errorDialog);
            DialogHelper.ShowOverlay();
            return;
        }

        progressDialog.Hide();
        await ViewModel.LoadVideosCommand.ExecuteAsync(null);

        if (result is not null)
        {
            var summary = $"Added {result.NewVideos} video(s), removed {result.RemovedVideos}.";
            if (result.Errors > 0)
                summary += $"\n{result.Errors} file(s) could not be read (corrupted or unsupported format).";

            var summaryDialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "Scan Complete",
                Content = summary,
                CloseButtonText = "OK",
            };
            await DialogHelper.ShowWithOverlayHiddenAsync(summaryDialog);
        }

        DialogHelper.ShowOverlay();
    }
}
