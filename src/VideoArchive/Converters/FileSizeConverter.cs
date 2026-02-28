using Microsoft.UI.Xaml.Data;

namespace VideoArchive.Converters;

public class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is long bytes)
        {
            return bytes switch
            {
                >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
                >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
                >= 1024 => $"{bytes / 1024.0:F0} KB",
                _ => $"{bytes} B"
            };
        }
        return "—";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
