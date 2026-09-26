using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Surface;

internal sealed class GroupDragOperation
    : IInteractionOperation
{
    private readonly IReadOnlyList<GroupDragTarget> _targets;
    private Vector _accumulatedDelta;

    private GroupDragOperation(
        SurfaceItem sourceContainer,
        Control sourceTarget,
        IReadOnlyList<GroupDragTarget> targets,
        Rect frame,
        Vector frameOffset)
    {
        SourceContainer = sourceContainer;
        SourceTarget = sourceTarget;
        _targets = targets;
        _accumulatedDelta = Vector.Zero;
        Frame = frame;
        FrameOffset = frameOffset;
    }

    public SurfaceItem SourceContainer { get; }
    public Control SourceTarget { get; }

    /// <summary>
    /// Рамка группы на момент начала жеста, в design-координатах.
    /// </summary>
    /// <remarks>
    /// Снимается один раз: внутри жеста группа двигается целиком, и её размер
    /// не меняется. К ней и идёт притяжение — как у группового resize и как
    /// выглядит происходящее на экране.
    /// </remarks>
    public Rect Frame { get; }

    /// <summary>
    /// Смещение левого верхнего угла рамки от позиции источника.
    /// </summary>
    /// <remarks>
    /// Им позиция рамки переводится в позицию источника и обратно: жест ведёт
    /// источник, а притягивается рамка.
    /// </remarks>
    public Vector FrameOffset { get; }

    public static GroupDragOperation? TryCreate(SurfaceView editor, SurfaceItem sourceContainer, Control sourceTarget)
    {
        var targets = new List<GroupDragTarget>();
        var items = editor.SelectedItems;
        if (items == null || items.Count == 0)
            return null;

        foreach (var item in items)
        {
            var container = editor.ContainerFromItem(item) as SurfaceItem;
            if (container == null && item is SurfaceItem directItem)
                container = directItem;

            if (container == null || !container.IsDraggable)
                continue;

            foreach (var target in editor.ResolveSelectionTargets(container))
            {
                if (ReferenceEquals(container, sourceContainer) && ReferenceEquals(target, sourceTarget))
                    continue;
                if (editor.GetEffectiveMovePolicy(target) == MovePolicy.None)
                    continue;

                targets.Add(new GroupDragTarget(target, editor.GetDesignPosition(target)));
            }
        }

        if (targets.Count == 0)
            return null;

        if (!editor.Geometry.TryGetBounds(sourceTarget, out var frame))
            return null;

        var sourceOrigin = frame.Position;
        for (var i = 0; i < targets.Count; i++)
        {
            if (editor.Geometry.TryGetBounds(targets[i].Target, out var bounds))
                frame = frame.Union(bounds);
        }

        return new GroupDragOperation(sourceContainer, sourceTarget, targets, frame, frame.Position - sourceOrigin);
    }

    public bool CanHandle(SurfaceItem sourceContainer)
    {
        return ReferenceEquals(sourceContainer, SourceContainer);
    }

    public void Update(SurfaceView editor, Vector frameDelta)
    {
        _accumulatedDelta += frameDelta;

        for (var i = 0; i < _targets.Count; i++)
        {
            var snapshot = _targets[i];
            var filteredDelta = editor.ApplyMovePolicy(snapshot.Target, _accumulatedDelta);
            editor.SetDesignPosition(snapshot.Target, snapshot.InitialPosition + filteredDelta);
        }
    }

    public void Complete(SurfaceView editor)
    {
    }
}

internal readonly struct GroupDragTarget
{
    public GroupDragTarget(Control target, Point initialPosition)
    {
        Target = target;
        InitialPosition = initialPosition;
    }

    public Control Target { get; }
    public Point InitialPosition { get; }
}
