using Microsoft.UI.Xaml.Data;

namespace VideoArchive.Converters;

/// <summary>
/// Joins tag names from a Video's VideoTags collection into a comma-separated string.
/// </summary>
public class TagsJoinConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is ICollection<VideoArchive.Models.VideoTag> videoTags && videoTags.Count > 0)
        {
            return string.Join(", ", videoTags.Select(vt => vt.Tag?.Name).Where(n => n is not null));
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
