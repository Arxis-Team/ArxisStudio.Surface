using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ArxisStudio.Surface.Editing;

namespace ArxisStudio.Surface.UiDesigner;

// Привязка к сетке и направляющие выравнивания: служба слоя редактирования
// (SnapService) и то, как дизайнер форм её подключает.
// Часть DesignEditor; общее описание типа — в DesignEditor.cs.
public partial class DesignEditor
{
    /// <summary>
    /// Идентификатор свойства линий выравнивания, найденных во время жеста.
    /// </summary>
    public static readonly AttachedProperty<IReadOnlyList<DesignSnapGuide>> SnapGuidesProperty =
        SurfaceSnapping.SnapGuidesProperty.AddOwner<DesignEditor>();

    /// <summary>
    /// Идентификатор свойства подсказок о равных интервалах.
    /// </summary>
    public static readonly AttachedProperty<IReadOnlyList<DesignSpacingHint>> SpacingHintsProperty =
        SurfaceSnapping.SpacingHintsProperty.AddOwner<DesignEditor>();

    private SnapService? _snap;

    private SnapService Snap => _snap ??= new SnapService(this, CollectUserGuideNeighbours);

    /// <summary>
    /// Получает линии выравнивания, найденные во время жеста.
    /// </summary>
    public IReadOnlyList<DesignSnapGuide> SnapGuides => GetValue(SnapGuidesProperty);

    /// <summary>
    /// Получает подсказки о равных интервалах, найденные во время жеста.
    /// </summary>
    public IReadOnlyList<DesignSpacingHint> SpacingHints => GetValue(SpacingHintsProperty);

    internal bool ShouldSnap(KeyModifiers modifiers) => Snap.ShouldSnap(modifiers);

    internal double ResolveSnapStep() => Snap.ResolveStep();

    internal double SnapCoordinate(double value) => Snap.SnapCoordinate(value);

    internal void BeginSnapGuides(Control movingTarget) => Snap.Begin(movingTarget);

    internal void EndSnapGuides() => Snap.End();

    /// <summary>
    /// Возвращает позицию перетаскиваемого target'а с учётом направляющих и сетки.
    /// </summary>
    /// <remarks>
    /// Группа притягивается рамкой выделения, а не тем элементом, за который её
    /// схватили: так уже устроен групповой resize, и так же выглядит происходящее
    /// на экране — пользователь ведёт рамку, её и надо ставить на место.
    /// <para>
    /// Отображение стоит снаружи всего разрешения, а не внутри ветки направляющих:
    /// правило одно на всю привязку, и сетка обязана ставить на узел ту же рамку.
    /// </para>
    /// </remarks>
    internal Point ResolveDragPosition(Control target, Point proposed, KeyModifiers modifiers)
    {
        if (_groupDragOperation is { } group && ReferenceEquals(target, group.SourceTarget))
        {
            var frame = Snap.ResolveOrigin(proposed + group.FrameOffset, group.Frame.Size, modifiers);
            return frame - group.FrameOffset;
        }

        return Snap.ResolveOrigin(proposed, GetDesignSize(target), modifiers);
    }

    internal bool CanSnapResizeEdge(KeyModifiers modifiers) => Snap.CanSnapEdge(modifiers);

    internal double ResolveResizeEdge(double edge, Rect proposed, bool xAxis, bool farEdge, KeyModifiers modifiers)
        => Snap.ResolveEdge(edge, proposed, xAxis, farEdge, modifiers);

    internal void PublishResizeGuides(Rect bounds) => Snap.PublishApplied(bounds);

    private IEnumerable<Rect> CollectUserGuideNeighbours()
    {
        var neighbours = new List<Rect>();
        AddUserGuideNeighbours(neighbours);
        return neighbours;
    }
}
