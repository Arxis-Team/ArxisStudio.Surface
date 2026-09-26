using Avalonia.Controls;

namespace ArxisStudio.Surface.UiDesigner.Placement;

/// <summary>
/// То, что физически умеет родительская раскладка: двигать можно только там, где
/// позицию ребёнка задаёт редактор.
/// </summary>
/// <remarks>
/// Раскладка задаёт потолок, а не пожелание: в <c>StackPanel</c> позицию ребёнка задаёт
/// панель, и перемещение там ничего бы не сделало. Размер раскладка не ограничивает —
/// явный <c>Width</c>/<c>Height</c> honours любая панель (<c>LayoutHonourProbeTests</c>).
/// </remarks>
internal sealed class PlacementMovePolicy : ISurfaceInteractionPolicy
{
    public static readonly PlacementMovePolicy Instance = new();

    private PlacementMovePolicy()
    {
    }

    public MovePolicy GetMovePolicy(Control target)
        => DesignPlacementResolver.Resolve(target).MoveSemantics == DesignMoveSemantics.Reposition
            ? MovePolicy.Both
            : MovePolicy.None;

    public ResizePolicy GetResizePolicy(Control target) => ResizePolicy.All;
}
