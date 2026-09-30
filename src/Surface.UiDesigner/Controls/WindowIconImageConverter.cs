using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace ArxisStudio.Surface.UiDesigner;

/// <summary>
/// Значок окна — картинкой для рамки элемента формы.
/// </summary>
/// <remarks>
/// <see cref="WindowIcon"/> — значок для системы, а не для показа: рисовать его нечем, он умеет только
/// записать себя в поток. Картинка из него одна на значок и живёт столько же, сколько он. Значок, который
/// записать не удалось, рамку не роняет — она остаётся без значка.
/// </remarks>
internal sealed class WindowIconImageConverter : IValueConverter
{
    public static readonly WindowIconImageConverter Instance = new();

    private static readonly ConditionalWeakTable<WindowIcon, Bitmap> Images = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not WindowIcon icon)
            return null;

        if (Images.TryGetValue(icon, out var image))
            return image;

        try
        {
            using var stream = new MemoryStream();
            icon.Save(stream);
            stream.Position = 0;
            image = new Bitmap(stream);
        }
        catch (Exception)
        {
            return null;
        }

        Images.AddOrUpdate(icon, image);
        return image;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
