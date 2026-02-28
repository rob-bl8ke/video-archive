using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VideoArchive.Models;
using VideoArchive.Services;

namespace VideoArchive.Views;

/// <summary>
/// Programmatic ContentDialog for applying tags to one or more videos.
/// Shows a checkbox list of all tags; checked = tag is applied.
/// </summary>
public static class ApplyTagsDialog
{
    /// <summary>
    /// Show dialog to apply/remove tags for the given video.
    /// Returns true if any changes were made.
    /// </summary>
    public static async Task<bool> ShowAsync(XamlRoot xamlRoot, Video video)
    {
        using var scope = App.Services.CreateScope();
        var tagService = scope.ServiceProvider.GetRequiredService<ITagService>();

        var allTags = await tagService.GetAllAsync();
        if (allTags.Count == 0)
        {
            var noTagsDialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = "No Tags",
                Content = "No tags have been created yet. Use \"Manage Tags\" to create tags first.",
                CloseButtonText = "OK",
            };
            await noTagsDialog.ShowAsync();
            return false;
        }

        var currentTagIds = new HashSet<int>(video.VideoTags.Select(vt => vt.TagId));

        // Build checkbox list
        var checkBoxes = new List<(CheckBox cb, Tag tag)>();
        var panel = new StackPanel { Spacing = 6, MinWidth = 280 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Tags for: {video.Title}",
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            Margin = new Thickness(0, 0, 0, 8),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 280,
        });

        foreach (var tag in allTags)
        {
            var cb = new CheckBox
            {
                Content = tag.Name,
                IsChecked = currentTagIds.Contains(tag.Id),
                Tag = tag.Id,
            };
            checkBoxes.Add((cb, tag));
            panel.Children.Add(cb);
        }

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Apply Tags",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return false;

        // Apply changes
        bool changed = false;
        foreach (var (cb, tag) in checkBoxes)
        {
            var wasChecked = currentTagIds.Contains(tag.Id);
            var isChecked = cb.IsChecked == true;

            if (isChecked && !wasChecked)
            {
                await tagService.AddTagToVideoAsync(video.Id, tag.Id);
                video.VideoTags.Add(new VideoTag { VideoId = video.Id, TagId = tag.Id, Tag = tag, Video = video });
                changed = true;
            }
            else if (!isChecked && wasChecked)
            {
                await tagService.RemoveTagFromVideoAsync(video.Id, tag.Id);
                var toRemove = video.VideoTags.FirstOrDefault(vt => vt.TagId == tag.Id);
                if (toRemove is not null)
                    video.VideoTags.Remove(toRemove);
                changed = true;
            }
        }

        // Write tags to file container via TagLib#
        if (changed)
        {
            WriteTagsToContainer(video);
        }

        return changed;
    }

    /// <summary>
    /// Writes the current tag names to the video container's Comment field as semicolon-delimited string.
    /// </summary>
    private static void WriteTagsToContainer(Video video)
    {
        try
        {
            using var tagFile = TagLib.File.Create(video.FilePath);
            var tagNames = video.VideoTags
                .Select(vt => vt.Tag?.Name)
                .Where(n => !string.IsNullOrEmpty(n));
            tagFile.Tag.Comment = string.Join(";", tagNames);
            tagFile.Save();
        }
        catch
        {
            // File may be read-only or corrupted — skip silently
        }
    }
}
