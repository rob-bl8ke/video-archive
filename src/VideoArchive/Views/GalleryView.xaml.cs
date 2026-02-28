using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VideoArchive.ViewModels;

namespace VideoArchive.Views;

public sealed partial class GalleryView : UserControl
{
    private MainViewModel ViewModel { get; }

    public GalleryView()
    {
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        this.InitializeComponent();

        // Bind the repeater to the ViewModel's Videos collection
        VideoRepeater.ItemsSource = ViewModel.Videos;

        // Show/hide empty state
        ViewModel.Videos.CollectionChanged += (_, _) => UpdateEmptyState();
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        var hasVideos = ViewModel.Videos.Count > 0;
        EmptyText.Visibility = hasVideos ? Visibility.Collapsed : Visibility.Visible;
        GalleryScroller.Visibility = hasVideos ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Card_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is int videoId)
        {
            var video = ViewModel.Videos.FirstOrDefault(v => v.Id == videoId);
            if (video is not null)
            {
                ViewModel.SelectedVideo = video;
            }
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
}
