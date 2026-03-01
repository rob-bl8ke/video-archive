using Microsoft.UI.Xaml.Data;

namespace VideoArchive.Converters;

/// <summary>
/// Converts a <see cref="TimeSpan"/> to a string with tenths-of-a-second precision.
/// Used in the segment panel where sub-second accuracy is valuable.
/// Format: h:mm:ss.f  or  m:ss.f
/// </summary>
public class PreciseDurationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is TimeSpan ts)
        {
            return ts.TotalHours >= 1
                ? ts.ToString(@"h\:mm\:ss\.f")
                : ts.ToString(@"m\:ss\.f");
        }
        return "--:--.0";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
