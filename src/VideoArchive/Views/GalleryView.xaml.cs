using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VideoArchive.Helpers;
using VideoArchive.Models;
using VideoArchive.Services;
using VideoArchive.ViewModels;

namespace VideoArchive.Views;

public sealed partial class GalleryView : UserControl
{
    private MainViewModel ViewModel { get; }
    private static readonly SolidColorBrush SelectedBorderBrush = new(Colors.CornflowerBlue);
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    public GalleryView()
    {
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        this.InitializeComponent();

        // Bind the GridView to the ViewModel's Videos collection
        VideoGrid.ItemsSource = ViewModel.Videos;

        // Re-apply selection borders when containers are recycled (virtualization)
        VideoGrid.ContainerContentChanging += VideoGrid_ContainerContentChanging;

        // Show/hide empty state
        ViewModel.Videos.CollectionChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateEmptyState();
            });
        };
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        var hasVideos = ViewModel.Videos.Count > 0;
        EmptyPanel.Visibility = hasVideos ? Visibility.Collapsed : Visibility.Visible;
        VideoGrid.Visibility = hasVideos ? Visibility.Visible : Visibility.Collapsed;
    }

    private void VideoGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Clear border on items leaving the selection
        foreach (var item in e.RemovedItems.OfType<Video>())
        {
            if (VideoGrid.ContainerFromItem(item) is GridViewItem container)
                container.BorderBrush = TransparentBrush;
        }

        // Apply border on items entering the selection
        foreach (var item in e.AddedItems.OfType<Video>())
        {
            if (VideoGrid.ContainerFromItem(item) is GridViewItem container)
                container.BorderBrush = SelectedBorderBrush;
        }

        // Sync ViewModel
        ViewModel.SelectedVideos = VideoGrid.SelectedItems.OfType<Video>().ToList();
        ViewModel.SelectedVideo = VideoGrid.SelectedItems.OfType<Video>().LastOrDefault();
    }

    private void VideoGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is not GridViewItem container) return;
        var isSelected = args.Item is Video v && VideoGrid.SelectedItems.Contains(v);
        container.BorderBrush = isSelected ? SelectedBorderBrush : TransparentBrush;
    }

    private void VideoGrid_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        // Walk up the visual tree to find the GridViewItem under the pointer
        var element = e.OriginalSource as DependencyObject;
        while (element is not null and not GridViewItem)
            element = VisualTreeHelper.GetParent(element);

        if (element is GridViewItem { Content: Video video })
        {
            // Replace selection only if the right-clicked item is not already selected
            if (!VideoGrid.SelectedItems.Contains(video))
                VideoGrid.SelectedItem = video;
        }

        // Play only makes sense for a single selection
        GalleryPlayMenuItem.IsEnabled = VideoGrid.SelectedItems.Count == 1;
    }

    private void VideoGrid_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel.SelectedVideo is not null)
            ViewModel.NavigateToPlayerCommand.Execute(ViewModel.SelectedVideo);
    }

    private void PlayMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedVideo is not null)
            ViewModel.NavigateToPlayerCommand.Execute(ViewModel.SelectedVideo);
    }

    private async void GalleryRemoveMenuItem_Click(object sender, RoutedEventArgs e)
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

    /// <summary>
    /// Helper for x:Bind — returns Visible when ThumbnailPath is null/empty or file doesn't exist.
    /// </summary>
    public static Visibility NoThumbnailVisibility(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return Visibility.Visible;
        return Visibility.Collapsed;
    }

    /// <summary>
    /// Syncs the GridView selection to ViewModel.SelectedVideo and scrolls it into view.
    /// Call when switching to gallery view.
    /// </summary>
    public void ScrollToSelected()
    {
        if (ViewModel.SelectedVideo is null) return;

        VideoGrid.SelectedItem = ViewModel.SelectedVideo;
        VideoGrid.ScrollIntoView(ViewModel.SelectedVideo);

        // Re-apply the selection border after layout
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (VideoGrid.ContainerFromItem(ViewModel.SelectedVideo) is GridViewItem container)
                container.BorderBrush = SelectedBorderBrush;
        });
    }
}
