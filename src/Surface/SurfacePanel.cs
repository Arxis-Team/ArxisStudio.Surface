using System;
using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Surface;

/// <summary>
/// Панель холста ядра: ставит каждый <see cref="SurfaceItem"/> в его <see cref="SurfaceItem.Location"/>.
/// </summary>
/// <remarks>
/// Панель верхнего уровня у <see cref="SurfaceView"/> без дизайнера интерфейса. Координаты
/// панели — мировые: она лежит в слое, к которому уже применена трансформация viewport.
/// <para>
/// Ребёнок меряется бесконечностью и получает свой желаемый размер — холст не
/// ограничивает элементы, как не ограничивает их бесконечная поверхность. Ребёнок, не
/// являющийся <see cref="SurfaceItem"/>, стоит в начале координат.
/// </para>
/// <para>
/// Дизайнер интерфейса пользуется своей панелью: его формы стоят в координатах
/// <c>Layout.X/Y</c>, которые он синхронизирует с <see cref="SurfaceItem.Location"/>.
/// </para>
/// </remarks>
public class SurfacePanel : Panel
{
    /// <summary>
    /// Идентификатор свойства области, занятой элементами.
    /// </summary>
    public static readonly StyledProperty<Rect> ExtentProperty =
        AvaloniaProperty.Register<SurfacePanel, Rect>(nameof(Extent));

    static SurfacePanel()
    {
        // Сдвиг элемента меняет и его место, и занятую область.
        AffectsParentMeasure<SurfacePanel>(SurfaceItem.LocationProperty);
        AffectsParentArrange<SurfacePanel>(SurfaceItem.LocationProperty);
    }

    /// <summary>
    /// Получает область в мировых координатах, которую занимают элементы.
    /// </summary>
    /// <remarks>
    /// Пересчитывается на каждом измерении. Её читает <see cref="SurfaceView.ItemsExtent"/>,
    /// а через него — вписывание в окно.
    /// </remarks>
    public Rect Extent
    {
        get => GetValue(ExtentProperty);
        set => SetValue(ExtentProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
        Rect? extent = null;

        foreach (var child in Children)
        {
            child.Measure(infinite);

            var size = child.DesiredSize;
            if (size.Width <= 0 || size.Height <= 0)
                continue;

            var bounds = new Rect(LocationOf(child), size);
            extent = extent is { } current ? current.Union(bounds) : bounds;
        }

        var occupied = extent ?? default;
        SetCurrentValue(ExtentProperty, occupied);

        // Как и панель дизайнера интерфейса: в холсте панель меряют бесконечностью, и тогда
        // её размер — дальний угол содержимого; данное конечное место она занимает целиком.
        return new Size(
            double.IsPositiveInfinity(availableSize.Width) ? Math.Max(0, occupied.Right) : availableSize.Width,
            double.IsPositiveInfinity(availableSize.Height) ? Math.Max(0, occupied.Bottom) : availableSize.Height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
            child.Arrange(new Rect(LocationOf(child), child.DesiredSize));

        return finalSize;
    }

    private static Point LocationOf(Control child)
    {
        if (child is not SurfaceItem item)
            return default;

        var location = item.Location;
        return double.IsNaN(location.X) || double.IsNaN(location.Y) ? default : location;
    }
}
