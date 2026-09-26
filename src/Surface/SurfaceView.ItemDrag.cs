using Avalonia;
using ArxisStudio.Surface.States;

namespace ArxisStudio.Surface;

// Перетаскивание контейнеров: единица редактирования и перенос остального выделения.
// Часть SurfaceView; общее описание типа — в SurfaceView.cs.
public partial class SurfaceView
{
    /// <summary>
    /// Запрещает перетаскивать текущее выделение, хотя каждый его элемент двигать можно.
    /// </summary>
    /// <remarks>
    /// Ядро такого правила не знает. Дизайнер форм запрещает тащить группу вложенных
    /// target'ов, среди которых есть заблокированный: смешанная группа не двигается вовсе.
    /// </remarks>
    private protected virtual bool BlocksSelectionDrag() => false;

    /// <summary>
    /// Начало перетаскивания контейнера: жест принят или отклонён.
    /// </summary>
    /// <remarks>
    /// Принятый жест открывает единицу редактирования — по ней состояние перетаскивания и
    /// узнаёт, что его приняли (<see cref="HasActiveEdit"/>). Жила эта точка у дизайнера
    /// форм, и голая поверхность не открывала единицы никогда: элемент на ней не двигался
    /// вовсе. Все проверки идут до <see cref="BeginEdit"/>, а не после.
    /// </remarks>
    private void OnItemsDragStarted(DragStartedEventArgs e)
    {
        _groupDragOperation = null;
        e.Handled = true;

        if (IsSelecting || CurrentState is EditorPanningState)
            return;

        var items = SelectedItems;
        if (e.Source is not SurfaceItem sourceContainer || items == null || items.Count == 0)
            return;

        if (BlocksSelectionDrag())
            return;

        var sourceTarget = ResolveInteractionTarget(sourceContainer);
        if (GetEffectiveMovePolicy(sourceTarget) == MovePolicy.None)
            return;

        BeginEdit(DesignEditKind.Move);
        _groupDragOperation = GroupDragOperation.TryCreate(this, sourceContainer, sourceTarget);
    }

    /// <summary>
    /// Кадр перетаскивания: источник уже сдвинут своим состоянием, остальное выделение
    /// едет на ту же применённую дельту.
    /// </summary>
    private void OnItemsDragDelta(DragDeltaEventArgs e)
    {
        if (IsSelecting || CurrentState is EditorPanningState)
            return;

        var items = SelectedItems;
        if (items == null || items.Count == 0)
            return;

        var source = e.Source as SurfaceItem;
        if (source != null)
        {
            if (BlocksSelectionDrag() || GetEffectiveMovePolicy(ResolveInteractionTarget(source)) == MovePolicy.None)
            {
                e.Handled = true;
                return;
            }
        }

        var delta = new Vector(e.HorizontalChange, e.VerticalChange);

        if (_groupDragOperation != null && source != null && _groupDragOperation.CanHandle(source))
        {
            _groupDragOperation.Update(this, delta);
            e.Handled = true;
            RefreshSelectionOverlay();
            return;
        }

        foreach (var item in items)
        {
            var container = ContainerFromItem(item) as SurfaceItem ?? item as SurfaceItem;
            if (container == null || !container.IsDraggable || ReferenceEquals(container, source))
                continue;

            var target = ResolveInteractionTarget(container);
            SetDesignPosition(target, GetDesignPosition(target) + ApplyMovePolicy(target, delta));
        }

        e.Handled = true;
        RefreshSelectionOverlay();
    }

    private void OnItemsDragCompleted(DragCompletedEventArgs e)
    {
        _groupDragOperation?.Complete(this);
        _groupDragOperation = null;
        CommitEdit();
        e.Handled = true;
    }
}
