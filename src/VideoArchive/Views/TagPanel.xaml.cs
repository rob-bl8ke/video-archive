using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VideoArchive.Models;
using VideoArchive.Services;
using VideoArchive.ViewModels;

namespace VideoArchive.Views;

/// <summary>
/// Inline tag-toggling panel for the currently selected video(s).
/// Supports single and multi-selection with live apply.
/// Partial tags (present on only some selected videos) are shown as indeterminate;
/// clicking them normalises the selection to all-having-it.
/// </summary>
public sealed partial class TagPanel : UserControl
{
    private MainViewModel MainVm { get; }
    private List<Video>? _boundVideos;

    public TagPanel()
    {
        MainVm = App.Services.GetRequiredService<MainViewModel>();
        this.InitializeComponent();

        MainVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedVideos))
                RefreshTags();
        };
    }

    private async void RefreshTags()
    {
        var videos = MainVm.SelectedVideos;
        _boundVideos = videos;

        TagList.Children.Clear();

        if (videos.Count == 0)
        {
            NoVideoHint.Visibility = Visibility.Visible;
            SelectionSubtitle.Visibility = Visibility.Collapsed;
            TagScroller.Visibility = Visibility.Collapsed;
            return;
        }

        NoVideoHint.Visibility = Visibility.Collapsed;
        SelectionSubtitle.Text = videos.Count == 1
            ? videos[0].Title
            : $"{videos.Count} videos selected";
        SelectionSubtitle.Visibility = Visibility.Visible;
        TagScroller.Visibility = Visibility.Visible;

        using var scope = App.Services.CreateScope();
        var tagService = scope.ServiceProvider.GetRequiredService<ITagService>();
        var allTags = await tagService.GetAllAsync();

        // Guard: bail out if the selection changed while we were awaiting
        if (!ReferenceEquals(_boundVideos, videos)) return;

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

        foreach (var tag in allTags)
        {
            var countWithTag = videos.Count(v => v.VideoTags.Any(vt => vt.TagId == tag.Id));
            var isPartial = countWithTag > 0 && countWithTag < videos.Count;

            var cb = new CheckBox
            {
                Content = tag.Name,
                IsThreeState = isPartial,
                IsChecked = countWithTag == videos.Count ? true
                          : countWithTag == 0           ? false
                          :                              (bool?)null,
                Tag = tag,
            };

            if (isPartial)
            {
                // WinUI3 three-state cycle: Unchecked→Checked→Indeterminate→Unchecked.
                // Clicking an indeterminate checkbox fires Unchecked.
                // We intercept that first Unchecked-from-indeterminate to mean "add to all"
                // and then switch to normal two-state toggling.
                cb.Unchecked += (s, e) =>
                {
                    if (s is not CheckBox c || c.Tag is not Tag t) return;
                    if (c.IsThreeState)
                    {
                        c.IsThreeState = false;
                        c.IsChecked = true; // fires Checked → adds to all
                        return;
                    }
                    _ = ApplyTagChangeAsync(t, false);
                };
                cb.Checked += (s, e) =>
                {
                    if (s is CheckBox c && c.Tag is Tag t) _ = ApplyTagChangeAsync(t, true);
                };
            }
            else
            {
                cb.Checked   += (s, e) => { if (s is CheckBox c && c.Tag is Tag t) _ = ApplyTagChangeAsync(t, true); };
                cb.Unchecked += (s, e) => { if (s is CheckBox c && c.Tag is Tag t) _ = ApplyTagChangeAsync(t, false); };
            }

            TagList.Children.Add(cb);
        }
    }

    private async Task ApplyTagChangeAsync(Tag tag, bool add)
    {
        var videos = _boundVideos;
        if (videos is null || !ReferenceEquals(videos, MainVm.SelectedVideos)) return;

        using var scope = App.Services.CreateScope();
        var tagService = scope.ServiceProvider.GetRequiredService<ITagService>();

        foreach (var video in videos)
        {
            var hasTag = video.VideoTags.Any(vt => vt.TagId == tag.Id);
            if (add && !hasTag)
            {
                await tagService.AddTagToVideoAsync(video.Id, tag.Id);
                video.VideoTags.Add(new VideoTag { VideoId = video.Id, TagId = tag.Id, Tag = tag, Video = video });
            }
            else if (!add && hasTag)
            {
                await tagService.RemoveTagFromVideoAsync(video.Id, tag.Id);
                var toRemove = video.VideoTags.FirstOrDefault(vt => vt.TagId == tag.Id);
                if (toRemove is not null)
                    video.VideoTags.Remove(toRemove);
            }
        }
    }
}

