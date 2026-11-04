using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Zenote.App.Converters;

public sealed class BytesToBitmapConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is byte[] bytes ? new Bitmap(new MemoryStream(bytes)) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
