using Avalonia;
using Avalonia.Controls;
using SurfaceLayout = ArxisStudio.Surface.UiDesigner.Layout;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Surface.UiDesigner.Placement;

/// <summary>
/// Позиционирование в <see cref="AbsolutePanel"/> и в <see cref="UiDesignerPanel"/>.
/// </summary>
/// <remarks>
/// Обслуживает и контрол без родителя: для него координаты поверхности — единственное,
/// что вообще определяет положение, поэтому семантика та же.
/// </remarks>
internal sealed class AbsolutePlacementStrategy : ISurfacePlacementStrategy
{
    public static readonly AbsolutePlacementStrategy Instance = new();

    private AbsolutePlacementStrategy()
    {
    }

    /// <inheritdoc />
    public string Name => "Absolute";

    /// <inheritdoc />
    public SurfaceMoveSemantics MoveSemantics => SurfaceMoveSemantics.Reposition;

    /// <inheritdoc />
    public Point GetPosition(Control target, UiDesignerView editor)
    {
        if (target is UiDesignerItem item)
            return item.Location;

        if (editor.TryGetTargetBounds(target, out var bounds))
            return bounds.Position;

        return new Point(SurfaceLayout.GetSurfaceX(target), SurfaceLayout.GetSurfaceY(target));
    }

    /// <inheritdoc />
    public void SetPosition(Control target, Point surfacePosition, UiDesignerView editor)
    {
        if (target is UiDesignerItem item)
        {
            item.SetCurrentValue(SurfaceItem.LocationProperty, surfacePosition);
            return;
        }

        UiDesignerView.EnsureTracked(target);
        SurfaceLayout.SetSurfaceX(target, surfacePosition.X);
        SurfaceLayout.SetSurfaceY(target, surfacePosition.Y);
    }
}

/// <summary>
/// Позиционирование в <see cref="Canvas"/>.
/// </summary>
/// <remarks>
/// <see cref="Canvas"/> не читает <c>Layout.X</c>/<c>Y</c> — ему нужны собственные
/// <c>Canvas.Left</c>/<c>Canvas.Top</c>. Раньше он проходил гейт перетаскивания
/// наравне с <see cref="AbsolutePanel"/> и после этого молча ничего не делал.
/// </remarks>
internal sealed class CanvasPlacementStrategy : ISurfacePlacementStrategy
{
    public static readonly CanvasPlacementStrategy Instance = new();

    private CanvasPlacementStrategy()
    {
    }

    /// <inheritdoc />
    public string Name => "Canvas";

    /// <inheritdoc />
    public SurfaceMoveSemantics MoveSemantics => SurfaceMoveSemantics.Reposition;

    /// <inheritdoc />
    public Point GetPosition(Control target, UiDesignerView editor)
    {
        if (editor.TryGetTargetBounds(target, out var bounds))
            return bounds.Position;

        return new Point(SurfaceLayout.GetSurfaceX(target), SurfaceLayout.GetSurfaceY(target));
    }

    /// <inheritdoc />
    public void SetPosition(Control target, Point surfacePosition, UiDesignerView editor)
    {
        // Координаты поверхности считаются от поверхности дизайна, а Canvas.Left/Top —
        // от самого Canvas, поэтому позицию нужно перевести в его пространство.
        var current = GetPosition(target, editor);
        var delta = surfacePosition - current;

        var left = Canvas.GetLeft(target);
        var top = Canvas.GetTop(target);

        Canvas.SetLeft(target, (double.IsNaN(left) ? 0d : left) + delta.X);
        Canvas.SetTop(target, (double.IsNaN(top) ? 0d : top) + delta.Y);

        UiDesignerView.EnsureTracked(target);
    }
}

/// <summary>
/// Раскладка, которая сама расставляет детей потоком: перестановка осмыслена, позиция — нет.
/// </summary>
internal sealed class StackPlacementStrategy : ISurfacePlacementStrategy
{
    public static readonly StackPlacementStrategy Instance = new();

    private StackPlacementStrategy()
    {
    }

    /// <inheritdoc />
    public string Name => "Stack";

    /// <inheritdoc />
    public SurfaceMoveSemantics MoveSemantics => SurfaceMoveSemantics.Reorder;

    /// <inheritdoc />
    public Point GetPosition(Control target, UiDesignerView editor)
        => editor.TryGetTargetBounds(target, out var bounds) ? bounds.Position : default;

    /// <inheritdoc />
    public void SetPosition(Control target, Point surfacePosition, UiDesignerView editor)
    {
        // Осознанно ничего: позицией распоряжается панель. Запись сюда и была
        // тем молчаливым no-op, ради устранения которого появились стратегии.
    }
}

/// <summary>
/// Раскладка, которая полностью определяет положение ребёнка.
/// </summary>
/// <remarks>
/// <see cref="Grid"/>, <see cref="DockPanel"/> и контент-хосты вроде
/// <see cref="Border"/>. Позиция в них задаётся не координатами, а собственными
/// присоединёнными свойствами раскладки, и правка их — отдельная задача.
/// </remarks>
internal sealed class FixedPlacementStrategy : ISurfacePlacementStrategy
{
    public static readonly FixedPlacementStrategy Grid = new("Grid");
    public static readonly FixedPlacementStrategy Dock = new("Dock");
    public static readonly FixedPlacementStrategy ContentHost = new("ContentHost");

    private FixedPlacementStrategy(string name) => Name = name;

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public SurfaceMoveSemantics MoveSemantics => SurfaceMoveSemantics.None;

    /// <inheritdoc />
    public Point GetPosition(Control target, UiDesignerView editor)
        => editor.TryGetTargetBounds(target, out var bounds) ? bounds.Position : default;

    /// <inheritdoc />
    public void SetPosition(Control target, Point surfacePosition, UiDesignerView editor)
    {
    }
}
