using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace ArxisStudio.Surface.Editing;

/// <summary>
/// Переводит размер ручки в отступ, центрирующий её на краю выделения.
/// </summary>
/// <remarks>
/// Ручка сидит на границе выделения, значит наружу должна торчать ровно половина её
/// размера — отсюда <c>-size/2</c>.
/// <para>
/// До этого отступ был литералом <c>-4</c>, подобранным под размер по умолчанию 8.
/// Размер при этом задавался ресурсом <c>Surface.SelectionAdorner.HandleSize</c>
/// и объявлен настраиваемым: переопределившему его ручки молча съезжали с края
/// на половину разницы, и заметить это можно было только глазами.
/// </para>
/// </remarks>
internal sealed class HandleMarginConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double size || double.IsNaN(size) || double.IsInfinity(size))
            return new Thickness(0);

        return new Thickness(-size / 2);
    }

    /// <summary>
    /// Обратное преобразование не поддерживается: привязка односторонняя.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}
