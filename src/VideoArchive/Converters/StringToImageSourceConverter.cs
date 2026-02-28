using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace VideoArchive.Converters;

public class StringToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string path && !string.IsNullOrEmpty(path) && File.Exists(path))
        {
            var bitmap = new BitmapImage();
            bitmap.UriSource = new Uri(path);
            return bitmap;
        }

        // Return null — the Image control will show nothing; 
        // XAML handles placeholder via a fallback in the template
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
