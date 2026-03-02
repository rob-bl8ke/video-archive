using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VideoArchive.Models;
using VideoArchive.ViewModels;

namespace VideoArchive.Views;

public sealed partial class GalleryView : UserControl
{
    private MainViewModel ViewModel { get; }
    private GridViewItem? _previousSelectedContainer;
    private static readonly SolidColorBrush SelectedBorderBrush = new(Colors.CornflowerBlue);
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    public GalleryView()
    {
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        this.InitializeComponent();

        // Bind the GridView to the ViewModel's Videos collection
        VideoGrid.ItemsSource = ViewModel.Videos;

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
        // Clear previous selection highlight
        if (_previousSelectedContainer is not null)
        {
            _previousSelectedContainer.BorderBrush = TransparentBrush;
            _previousSelectedContainer = null;
        }

        if (VideoGrid.SelectedItem is Video video)
        {
            ViewModel.SelectedVideo = video;

            // Apply selection highlight to the container
            if (VideoGrid.ContainerFromItem(video) is GridViewItem container)
            {
                container.BorderBrush = SelectedBorderBrush;
                _previousSelectedContainer = container;
            }
        }
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
            if (_previousSelectedContainer is not null)
                _previousSelectedContainer.BorderBrush = TransparentBrush;

            if (VideoGrid.ContainerFromItem(ViewModel.SelectedVideo) is GridViewItem container)
            {
                container.BorderBrush = SelectedBorderBrush;
                _previousSelectedContainer = container;
            }
        });
    }
}
