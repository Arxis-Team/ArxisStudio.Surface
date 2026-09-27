using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

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
/// Сдвиг элемента переставляет его одного (ADR 0007): мера ребёнка от положения не зависит,
/// и перемерять и переставлять всех ради одного значило бы платить за каждый кадр
/// перетаскивания размером всего холста. Полная расстановка — после прохода меры, то есть
/// когда сменился чей-то размер или состав детей.
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

    // Сдвинутые с прошлой расстановки: только их расстановка и ставит заново.
    private readonly HashSet<SurfaceItem> _moved = new();
    private bool _arrangeAll = true;
    private bool _extentStale;

    static SurfacePanel()
    {
        SurfaceItem.LocationProperty.Changed.AddClassHandler<SurfaceItem>((item, e) =>
        {
            if (item.GetVisualParent() is SurfacePanel panel)
                panel.OnChildMoved(item, e.OldValue is Point old ? old : default);
        });
    }

    /// <summary>
    /// Получает область в мировых координатах, которую занимают элементы.
    /// </summary>
    /// <remarks>
    /// Её читает <see cref="SurfaceView.ItemsExtent"/>, а через него — вписывание в окно. Сдвиг
    /// элемента растит её объединением; сжаться она может, только если с края ушёл элемент,
    /// который его и задавал, — тогда она пересчитывается целиком в ближайшей расстановке.
    /// </remarks>
    public Rect Extent
    {
        get => GetValue(ExtentProperty);
        set => SetValue(ExtentProperty, value);
    }

    /// <summary>
    /// Сколько раз панель меряла детей — для стенда стоимости.
    /// </summary>
    internal int MeasuredChildren { get; private set; }

    /// <summary>
    /// Сколько раз панель расставляла детей — для стенда стоимости.
    /// </summary>
    internal int ArrangedChildren { get; private set; }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
        foreach (var child in Children)
        {
            MeasuredChildren++;
            child.Measure(infinite);
        }

        // После меры — сменился ли чей-то размер или состав детей — расставляются все, охват
        // заново: состав детей панель меняет только проходом меры.
        _arrangeAll = true;
        var occupied = ComputeExtent();
        _extentStale = false;
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
        if (_arrangeAll)
        {
            foreach (var child in Children)
                ArrangeChild(child);

            _arrangeAll = false;
        }
        else
        {
            foreach (var item in _moved)
            {
                // Ушедший из панели после сдвига ставить уже некуда.
                if (ReferenceEquals(item.GetVisualParent(), this))
                    ArrangeChild(item);
            }
        }

        _moved.Clear();

        if (_extentStale)
        {
            _extentStale = false;
            SetCurrentValue(ExtentProperty, ComputeExtent());
        }

        return finalSize;
    }

    private void ArrangeChild(Control child)
    {
        ArrangedChildren++;
        child.Arrange(new Rect(LocationOf(child), child.DesiredSize));
    }

    private void OnChildMoved(SurfaceItem item, Point oldLocation)
    {
        if (!_arrangeAll)
            _moved.Add(item);

        var size = item.DesiredSize;
        if (!_extentStale && size.Width > 0 && size.Height > 0)
        {
            var extent = Extent;
            var before = new Rect(Normalize(oldLocation), size);
            var after = new Rect(LocationOf(item), size);

            // Прежний прямоугольник на краю — край мог уйти вместе с ним: пересчёт целиком.
            if (extent.Width <= 0 || extent.Height <= 0 || TouchesEdge(before, extent))
                _extentStale = true;
            else if (!extent.Contains(after))
                SetCurrentValue(ExtentProperty, extent.Union(after));
        }

        InvalidateArrange();
    }

    private Rect ComputeExtent()
    {
        Rect? extent = null;
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (size.Width <= 0 || size.Height <= 0)
                continue;

            var bounds = new Rect(LocationOf(child), size);
            extent = extent is { } current ? current.Union(bounds) : bounds;
        }

        return extent ?? default;
    }

    private static bool TouchesEdge(Rect bounds, Rect extent) =>
        bounds.X <= extent.X || bounds.Y <= extent.Y || bounds.Right >= extent.Right || bounds.Bottom >= extent.Bottom;

    private static Point LocationOf(Control child) =>
        child is SurfaceItem item ? Normalize(item.Location) : default;

    private static Point Normalize(Point location) =>
        double.IsNaN(location.X) || double.IsNaN(location.Y) ? default : location;
}
