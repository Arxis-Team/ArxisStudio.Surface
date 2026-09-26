using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ArxisStudio.Surface;

/// <summary>
/// Переводит масштаб viewport в обратную трансформацию для оверлеев.
/// </summary>
/// <remarks>
/// Оверлеи лежат внутри слоя, к которому применён <c>ViewportTransform</c>, и без
/// обратного масштаба росли бы вместе с содержимым: ручки выделения раздувались бы
/// на приближении, а линия индикатора толстела.
/// <para>
/// Привязка идёт к <c>ViewportZoom</c>, а не к <c>ViewportTransform</c>, и это
/// принципиально. Прежний конвертер брал матрицу из <c>TransformGroup.Children[0]</c> —
/// то есть зависел от порядка элементов в группе, о чём приходилось помнить, — и
/// перевычислялся только при смене <b>идентичности</b> привязанного значения.
/// Трансформации редактор мутирует на месте, идентичность при этом не меняется,
/// поэтому <c>UpdateTransforms</c> был вынужден пересобирать обе группы заново
/// на каждом кадре панорамирования. Число меняется само, и оба костыля отпадают.
/// </para>
/// </remarks>
internal sealed class InverseScaleConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double zoom || !(zoom > 0) || double.IsInfinity(zoom))
            return new ScaleTransform(1, 1);

        var inverse = 1 / zoom;
        return new ScaleTransform(inverse, inverse);
    }

    /// <summary>
    /// Обратное преобразование не поддерживается: привязка односторонняя.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}
