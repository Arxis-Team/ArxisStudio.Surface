using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Surface.UiDesigner.Placement;

/// <summary>
/// Геометрия дизайнера интерфейса: позицию ребёнка знает и принимает его панель.
/// </summary>
/// <remarks>
/// Стратегия размещения выводится из родительской раскладки (<see cref="SurfacePlacementResolver"/>).
/// Запись позиции там, где ею владеет раскладка, отсекается на шве ядра — поэтому в
/// контракт изменений не попадает перемещение, которого не произошло.
/// </remarks>
internal sealed class SurfacePlacementGeometry : ISurfaceGeometry
{
    private readonly UiDesignerView _editor;

    public SurfacePlacementGeometry(UiDesignerView editor) => _editor = editor;

    public Point GetPosition(Control target)
        => SurfacePlacementResolver.Resolve(target).GetPosition(target, _editor);

    public bool CanSetPosition(Control target)
        => SurfacePlacementResolver.Resolve(target).MoveSemantics == SurfaceMoveSemantics.Reposition;

    public void SetPosition(Control target, Point position)
        => SurfacePlacementResolver.Resolve(target).SetPosition(target, position, _editor);

    public bool TryGetBounds(Control target, out Rect bounds)
        => _editor.TryGetTargetBounds(target, out bounds);
}
