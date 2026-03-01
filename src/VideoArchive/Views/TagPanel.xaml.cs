using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VideoArchive.Models;
using VideoArchive.Services;
using VideoArchive.ViewModels;

namespace VideoArchive.Views;

/// <summary>
/// Inline tag-toggling panel for the currently loaded video.
/// Replaces the need to open a modal dialog while using the player.
/// </summary>
public sealed partial class TagPanel : UserControl
{
    private VideoPlayerViewModel PlayerVm { get; }
    private Video? _boundVideo;

    public TagPanel()
    {
        PlayerVm = App.Services.GetRequiredService<VideoPlayerViewModel>();
        this.InitializeComponent();

        PlayerVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VideoPlayerViewModel.CurrentVideo))
                RefreshTags();
        };
    }

    private async void RefreshTags()
    {
        var video = PlayerVm.CurrentVideo;
        _boundVideo = video;

        TagList.Children.Clear();

        if (video is null)
        {
            NoVideoHint.Visibility = Visibility.Visible;
            TagScroller.Visibility = Visibility.Collapsed;
            return;
        }

        NoVideoHint.Visibility = Visibility.Collapsed;
        TagScroller.Visibility = Visibility.Visible;

        using var scope = App.Services.CreateScope();
        var tagService = scope.ServiceProvider.GetRequiredService<ITagService>();
        var allTags = await tagService.GetAllAsync();

        if (allTags.Count == 0)
        {
            TagList.Children.Add(new TextBlock
            {
                Text = "No tags created yet.\nUse Settings → Manage Tags to add some.",
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        var currentTagIds = new HashSet<int>(video.VideoTags.Select(vt => vt.TagId));

        foreach (var tag in allTags)
        {
            var cb = new CheckBox
            {
                Content = tag.Name,
                IsChecked = currentTagIds.Contains(tag.Id),
                Tag = tag,
            };
            cb.Checked += TagCheckBox_Changed;
            cb.Unchecked += TagCheckBox_Changed;
            TagList.Children.Add(cb);
        }
    }

    private async void TagCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb || cb.Tag is not Tag tag || _boundVideo is null)
            return;

        using var scope = App.Services.CreateScope();
        var tagService = scope.ServiceProvider.GetRequiredService<ITagService>();

        if (cb.IsChecked == true)
        {
            await tagService.AddTagToVideoAsync(_boundVideo.Id, tag.Id);
            if (!_boundVideo.VideoTags.Any(vt => vt.TagId == tag.Id))
            {
                _boundVideo.VideoTags.Add(new VideoTag
                {
                    VideoId = _boundVideo.Id,
                    TagId = tag.Id,
                    Tag = tag,
                    Video = _boundVideo,
                });
            }
        }
        else
        {
            await tagService.RemoveTagFromVideoAsync(_boundVideo.Id, tag.Id);
            var toRemove = _boundVideo.VideoTags.FirstOrDefault(vt => vt.TagId == tag.Id);
            if (toRemove is not null)
                _boundVideo.VideoTags.Remove(toRemove);
        }
    }
}
