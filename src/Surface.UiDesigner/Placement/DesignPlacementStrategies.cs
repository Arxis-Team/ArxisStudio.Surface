using Avalonia;
using Avalonia.Controls;
using ArxisStudio.Controls;
using DesignLayout = ArxisStudio.Surface.UiDesigner.Layout;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Surface.UiDesigner.Placement;

/// <summary>
/// Позиционирование в <see cref="AbsolutePanel"/> и в <see cref="DesignSurface"/>.
/// </summary>
/// <remarks>
/// Обслуживает и контрол без родителя: для него design-координаты — единственное,
/// что вообще определяет положение, поэтому семантика та же.
/// </remarks>
internal sealed class AbsolutePlacementStrategy : IDesignPlacementStrategy
{
    public static readonly AbsolutePlacementStrategy Instance = new();

    private AbsolutePlacementStrategy()
    {
    }

    /// <inheritdoc />
    public string Name => "Absolute";

    /// <inheritdoc />
    public DesignMoveSemantics MoveSemantics => DesignMoveSemantics.Reposition;

    /// <inheritdoc />
    public Point GetPosition(Control target, DesignEditor editor)
    {
        if (target is DesignEditorItem item)
            return item.Location;

        if (editor.TryGetDesignBounds(target, out var bounds))
            return bounds.Position;

        return new Point(DesignLayout.GetDesignX(target), DesignLayout.GetDesignY(target));
    }

    /// <inheritdoc />
    public void SetPosition(Control target, Point designPosition, DesignEditor editor)
    {
        if (target is DesignEditorItem item)
        {
            item.Location = designPosition;
            return;
        }

        DesignEditor.EnsureTracked(target);
        DesignLayout.SetDesignX(target, designPosition.X);
        DesignLayout.SetDesignY(target, designPosition.Y);
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
internal sealed class CanvasPlacementStrategy : IDesignPlacementStrategy
{
    public static readonly CanvasPlacementStrategy Instance = new();

    private CanvasPlacementStrategy()
    {
    }

    /// <inheritdoc />
    public string Name => "Canvas";

    /// <inheritdoc />
    public DesignMoveSemantics MoveSemantics => DesignMoveSemantics.Reposition;

    /// <inheritdoc />
    public Point GetPosition(Control target, DesignEditor editor)
    {
        if (editor.TryGetDesignBounds(target, out var bounds))
            return bounds.Position;

        return new Point(DesignLayout.GetDesignX(target), DesignLayout.GetDesignY(target));
    }

    /// <inheritdoc />
    public void SetPosition(Control target, Point designPosition, DesignEditor editor)
    {
        // Design-координаты считаются от поверхности дизайна, а Canvas.Left/Top —
        // от самого Canvas, поэтому позицию нужно перевести в его пространство.
        var current = GetPosition(target, editor);
        var delta = designPosition - current;

        var left = Canvas.GetLeft(target);
        var top = Canvas.GetTop(target);

        Canvas.SetLeft(target, (double.IsNaN(left) ? 0d : left) + delta.X);
        Canvas.SetTop(target, (double.IsNaN(top) ? 0d : top) + delta.Y);

        DesignEditor.EnsureTracked(target);
    }
}

/// <summary>
/// Раскладка, которая сама расставляет детей потоком: перестановка осмыслена, позиция — нет.
/// </summary>
internal sealed class StackPlacementStrategy : IDesignPlacementStrategy
{
    public static readonly StackPlacementStrategy Instance = new();

    private StackPlacementStrategy()
    {
    }

    /// <inheritdoc />
    public string Name => "Stack";

    /// <inheritdoc />
    public DesignMoveSemantics MoveSemantics => DesignMoveSemantics.Reorder;

    /// <inheritdoc />
    public Point GetPosition(Control target, DesignEditor editor)
        => editor.TryGetDesignBounds(target, out var bounds) ? bounds.Position : default;

    /// <inheritdoc />
    public void SetPosition(Control target, Point designPosition, DesignEditor editor)
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
internal sealed class FixedPlacementStrategy : IDesignPlacementStrategy
{
    public static readonly FixedPlacementStrategy Grid = new("Grid");
    public static readonly FixedPlacementStrategy Dock = new("Dock");
    public static readonly FixedPlacementStrategy ContentHost = new("ContentHost");

    private FixedPlacementStrategy(string name) => Name = name;

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public DesignMoveSemantics MoveSemantics => DesignMoveSemantics.None;

    /// <inheritdoc />
    public Point GetPosition(Control target, DesignEditor editor)
        => editor.TryGetDesignBounds(target, out var bounds) ? bounds.Position : default;

    /// <inheritdoc />
    public void SetPosition(Control target, Point designPosition, DesignEditor editor)
    {
    }
}
