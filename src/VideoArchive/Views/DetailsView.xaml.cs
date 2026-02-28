using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VideoArchive.Models;
using VideoArchive.Services;
using VideoArchive.ViewModels;

namespace VideoArchive.Views;

public sealed partial class DetailsView : UserControl
{
    private MainViewModel ViewModel { get; }

    public DetailsView()
    {
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        this.InitializeComponent();

        VideosGrid.ItemsSource = ViewModel.Videos;

        ViewModel.Videos.CollectionChanged += (_, _) => UpdateEmptyState();
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        var hasVideos = ViewModel.Videos.Count > 0;
        EmptyText.Visibility = hasVideos ? Visibility.Collapsed : Visibility.Visible;
        VideosGrid.Visibility = hasVideos ? Visibility.Visible : Visibility.Collapsed;
    }

    private void VideosGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VideosGrid.SelectedItem is Video video)
        {
            ViewModel.SelectedVideo = video;
        }
    }

    private void VideosGrid_Sorting(object sender, DataGridColumnEventArgs e)
    {
        var tag = e.Column.Tag as string;
        if (string.IsNullOrEmpty(tag)) return;

        // Determine sort direction
        var direction = e.Column.SortDirection;
        var ascending = direction != DataGridSortDirection.Ascending;

        // Clear other column sort indicators
        foreach (var col in VideosGrid.Columns)
        {
            if (col != e.Column) col.SortDirection = null;
        }
        e.Column.SortDirection = ascending
            ? DataGridSortDirection.Ascending
            : DataGridSortDirection.Descending;

        // Sort the collection
        var sorted = tag switch
        {
            "Title" => ascending
                ? ViewModel.Videos.OrderBy(v => v.Title)
                : ViewModel.Videos.OrderByDescending(v => v.Title),
            "Duration" => ascending
                ? ViewModel.Videos.OrderBy(v => v.Duration)
                : ViewModel.Videos.OrderByDescending(v => v.Duration),
            "Format" => ascending
                ? ViewModel.Videos.OrderBy(v => v.Format)
                : ViewModel.Videos.OrderByDescending(v => v.Format),
            "Resolution" => ascending
                ? ViewModel.Videos.OrderBy(v => v.Resolution)
                : ViewModel.Videos.OrderByDescending(v => v.Resolution),
            "DateAdded" => ascending
                ? ViewModel.Videos.OrderBy(v => v.DateAdded)
                : ViewModel.Videos.OrderByDescending(v => v.DateAdded),
            "FileSize" => ascending
                ? ViewModel.Videos.OrderBy(v => v.FileSize)
                : ViewModel.Videos.OrderByDescending(v => v.FileSize),
            _ => (IOrderedEnumerable<Video>)ViewModel.Videos.OrderBy(v => v.Title),
        };

        var list = sorted.ToList();
        ViewModel.Videos.Clear();
        foreach (var v in list)
        {
            ViewModel.Videos.Add(v);
        }
    }

    private void PlayMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedVideo is not null)
        {
            ViewModel.IsPlayerVisible = true;
            // Find the PlayerPanel and start playback
            var mainWindow = VideoArchive.Views.WindowHelper.ActiveWindows.FirstOrDefault();
            if (mainWindow?.Content is Grid rootGrid)
            {
                var playerPanel = FindPlayerPanel(rootGrid);
                playerPanel?.PlayVideo(ViewModel.SelectedVideo);
            }
        }
    }

    private static PlayerPanel? FindPlayerPanel(DependencyObject parent)
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is PlayerPanel panel) return panel;
            var result = FindPlayerPanel(child);
            if (result is not null) return result;
        }
        return null;
    }

    private async void AddTagsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedVideo is null) return;
        await ApplyTagsDialog.ShowAsync(this.XamlRoot, ViewModel.SelectedVideo);
    }

    private async void RemoveMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedVideo is null) return;

        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = "Remove from Library",
            Content = $"Remove \"{ViewModel.SelectedVideo.Title}\" from the library?\nThe file will not be deleted from disk.",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            using var scope = App.Services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IVideoRepository>();
            await repo.DeleteAsync(ViewModel.SelectedVideo.Id);
            ViewModel.Videos.Remove(ViewModel.SelectedVideo);
            ViewModel.SelectedVideo = null;
        }
    }
}
