using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Surface.UiDesigner.Placement;

/// <summary>
/// Геометрия дизайнера форм: позицию ребёнка знает и принимает его панель.
/// </summary>
/// <remarks>
/// Стратегия размещения выводится из родительской раскладки (<see cref="DesignPlacementResolver"/>).
/// Запись позиции там, где ею владеет раскладка, отсекается на шве ядра — поэтому в
/// контракт изменений не попадает перемещение, которого не произошло.
/// </remarks>
internal sealed class DesignPlacementGeometry : ISurfaceGeometry
{
    private readonly DesignEditor _editor;

    public DesignPlacementGeometry(DesignEditor editor) => _editor = editor;

    public Point GetPosition(Control target)
        => DesignPlacementResolver.Resolve(target).GetPosition(target, _editor);

    public bool CanSetPosition(Control target)
        => DesignPlacementResolver.Resolve(target).MoveSemantics == DesignMoveSemantics.Reposition;

    public void SetPosition(Control target, Point position)
        => DesignPlacementResolver.Resolve(target).SetPosition(target, position, _editor);
}
