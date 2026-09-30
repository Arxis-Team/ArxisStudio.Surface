using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace ArxisStudio.Surface.UiDesigner;

/// <summary>
/// Скругление окна с заголовком: одно на двоих, верх — заголовку, низ — форме.
/// </summary>
/// <remarks>
/// Окно с заголовком — один прямоугольник из двух частей, и радиус у него один:
/// <c>CornerRadius</c> элемента. Заголовок берёт из него верхние углы, форма под ним — нижние, а
/// сходятся они прямой линией. Второго радиуса — ресурса для заголовка — нет намеренно: два числа
/// расходятся, и тогда дуга заголовка не совпадает с дугой формы.
/// </remarks>
internal sealed class FormCornerRadiusConverter : IValueConverter
{
    /// <summary>Верхние углы элемента, нижние прямые: заголовок окна.</summary>
    public static readonly FormCornerRadiusConverter TitleBar = new(top: true);

    /// <summary>Нижние углы элемента, верхние прямые: форма под заголовком.</summary>
    public static readonly FormCornerRadiusConverter UnderTitleBar = new(top: false);

    private readonly bool _top;

    private FormCornerRadiusConverter(bool top) => _top = top;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not CornerRadius radius)
            return AvaloniaProperty.UnsetValue;

        return _top
            ? new CornerRadius(radius.TopLeft, radius.TopRight, 0, 0)
            : new CornerRadius(0, 0, radius.BottomRight, radius.BottomLeft);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
