using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VideoArchive.Helpers;
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
        EmptyPanel.Visibility = hasVideos ? Visibility.Collapsed : Visibility.Visible;
        VideosGrid.Visibility = hasVideos ? Visibility.Visible : Visibility.Collapsed;
    }

    private void VideosGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.SelectedVideos = VideosGrid.SelectedItems.OfType<Video>().ToList();
        ViewModel.SelectedVideo = VideosGrid.SelectedItem as Video;
    }

    private void VideosGrid_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        // Walk up the visual tree to find the DataGridRow under the pointer
        var element = e.OriginalSource as DependencyObject;
        while (element is not null)
        {
            if (element is DataGridRow row)
            {
                if (row.DataContext is Video video && !VideosGrid.SelectedItems.Contains(video))
                    VideosGrid.SelectedItem = video;
                break;
            }
            element = VisualTreeHelper.GetParent(element);
        }

        DetailPlayMenuItem.IsEnabled = VideosGrid.SelectedItems.Count == 1;
    }

    /// <summary>
    /// Syncs the DataGrid selection to ViewModel.SelectedVideo and scrolls it into view.
    /// Call when switching to details view.
    /// </summary>
    public void ScrollToSelected()
    {
        if (ViewModel.SelectedVideo is null) return;

        VideosGrid.SelectedItem = ViewModel.SelectedVideo;
        VideosGrid.ScrollIntoView(ViewModel.SelectedVideo, null);
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
            ViewModel.NavigateToPlayerCommand.Execute(ViewModel.SelectedVideo);
    }

    private async void RemoveMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var toRemove = ViewModel.SelectedVideos.ToList();
        if (toRemove.Count == 0) return;

        var content = toRemove.Count == 1
            ? $"Remove \"{toRemove[0].Title}\" from the library?\nThe file will not be deleted from disk."
            : $"Remove {toRemove.Count} videos from the library?\nFiles will not be deleted from disk.";

        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = "Remove from Library",
            Content = content,
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await DialogHelper.ShowWithOverlayHiddenAsync(dialog) == ContentDialogResult.Primary)
        {
            using var scope = App.Services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IVideoRepository>();
            foreach (var video in toRemove)
            {
                await repo.DeleteAsync(video.Id);
                ViewModel.Videos.Remove(video);
            }
            ViewModel.SelectedVideos = [];
            ViewModel.SelectedVideo = null;
        }
    }
}
