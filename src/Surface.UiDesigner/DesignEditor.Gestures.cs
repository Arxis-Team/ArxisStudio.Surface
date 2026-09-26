using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DesignLayout = ArxisStudio.Surface.UiDesigner.Layout;
using DesignInteraction = ArxisStudio.Surface.Editing.DesignInteraction;
using ArxisStudio.Surface.Editing;
using ArxisStudio.Surface.UiDesigner.Placement;
using ArxisStudio.Surface;
using ArxisStudio.Surface.States;
using ArxisStudio.Surface.UiDesigner.States;

namespace ArxisStudio.Surface.UiDesigner;

// Ввод: машина состояний редактора, указатель, клавиатура, drag и resize.
// Часть DesignEditor; общее описание типа — в DesignEditor.cs.
public partial class DesignEditor
{
    /// <inheritdoc />
    /// <remarks>
    /// Порядок решений прежний, и он значим.
    /// <list type="number">
    /// <item><description>
    /// Пользовательский запрет сильнее любой раскладки — в том числе сильнее перестановки.
    /// </description></item>
    /// <item><description>
    /// Спрашивается не родитель контейнера, а фактическая цель жеста: двигаться будет
    /// именно она, и решает её собственная раскладка.
    /// </description></item>
    /// <item><description>
    /// Перестановку выполняет приложение, и без подписчика она не произойдёт: жест тогда
    /// не начинается вовсе — вести точку вставки за курсором, зная, что на отпускании
    /// ничего не будет, то же самое, что предлагать заблокированное перемещение.
    /// </description></item>
    /// <item><description>
    /// Смешанная группа заблокированных и свободных вложенных target'ов не двигается вовсе.
    /// </description></item>
    /// </list>
    /// </remarks>
    internal override ItemDragPlan PlanItemDrag(SurfaceItem container, Control moveTarget)
    {
        if (DesignInteraction.GetMovePolicy(moveTarget) == ArxisStudio.Surface.MovePolicy.None)
            return ItemDragPlan.Refuse;

        var semantics = GetPlacementStrategy(moveTarget).MoveSemantics;
        if (semantics == DesignMoveSemantics.None)
            return ItemDragPlan.Refuse;

        if (semantics == DesignMoveSemantics.Reorder)
        {
            return CanRequestReorder
                ? new ItemDragPlan(ItemDragKind.Custom, static (item, start) => new ItemReorderingState((DesignEditorItem)item, start))
                : ItemDragPlan.Refuse;
        }

        if (ShouldBlockNestedGroupDrag())
            return ItemDragPlan.Refuse with { MarkHandled = true };

        return ItemDragPlan.Drag;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Правый клик сначала переводит выделение, если щёлкнули мимо него, и только потом
    /// спрашивает контекст: меню относится к тому, что выбрано.
    /// </remarks>
    private protected override bool OnContextPointerPressed(PointerPressedEventArgs e, Point position)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            return false;

        RetargetSelectionForContext(position, e.KeyModifiers);
        RequestContextSafe(DesignEditorContextSource.Pointer, position, e.KeyModifiers);
        return true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Брошенный групповой жест закрывается здесь же. Иначе и операция, и открытая
    /// единица редактирования переживают его: правка ушла бы мимо отмены, а следующий
    /// жест достался бы чужой операции. Единица именно фиксируется, а не затирается —
    /// геометрия к этому моменту уже применена, и поздняя запись лучше потерянной.
    /// </remarks>
    private protected override void OnGestureAbandoned()
    {
        if (_groupResizeOperation != null)
            CompleteGroupResize();
    }

    // --- Drag & Drop ---

    /// <inheritdoc />
    /// <remarks>
    /// Смешанная группа вложенных target'ов — заблокированные вместе со свободными — не
    /// двигается вовсе: сдвинуть половину значило бы разорвать то, что выбрано вместе.
    /// </remarks>
    private protected override bool BlocksSelectionDrag() => ShouldBlockNestedGroupDrag();

    private void OnItemsResizeDelta(ResizeDeltaEventArgs e)
    {
        UpdateSelectionOverlayState();
        e.Handled = false;
    }

    private void OnSelectionResizeStarted(object? sender, ResizeStartedEventArgs e)
    {
        if (_primarySelectionItem == null || _primarySelectionControl == null || !HasSingleSelection)
            return;

        if (!IsResizeAllowed(_primarySelectionControl, e.Direction))
            return;

        // До PushState: ItemResizingState.Enter уже фиксирует текущий размер.
        BeginEdit(DesignEditKind.Resize);
        _primarySelectionItem.PushState(new ItemResizingState(_primarySelectionItem, _primarySelectionControl, e.Direction));
        _primarySelectionItem.OnResizeStarted(e.Vector);
        e.Handled = true;
    }

    private void OnSelectionResizeDelta(object? sender, ResizeDeltaEventArgs e)
    {
        if (_primarySelectionItem == null || _primarySelectionControl == null || _primarySelectionItem.CurrentState is not ItemResizingState)
            return;
        if (!IsResizeAllowed(_primarySelectionControl, e.Direction))
            return;

        var worldDelta = NormalizeResizeDelta(e.Delta);
        var normalizedArgs = new ResizeDeltaEventArgs(worldDelta, e.Direction, SelectionAdorner.ResizeDeltaEvent)
        {
            Source = e.Source
        };

        // Событие контейнера поднимает само состояние, уже применив геометрию. Второй
        // раз здесь его не поднимать: хост получал каждое движение дважды.
        _primarySelectionItem.CurrentState.OnResizeDelta(normalizedArgs);
        UpdateSelectionOverlayState();
        e.Handled = true;

        TrackResizeAutoPan(() => OnSelectionResizeDelta(
            sender,
            new ResizeDeltaEventArgs(AutoPanScreenShift(), e.Direction, SelectionAdorner.ResizeDeltaEvent) { Source = e.Source }));
    }

    private void OnSelectionResizeCompleted(object? sender, VectorEventArgs e)
    {
        if (_primarySelectionItem == null || _primarySelectionControl == null || _primarySelectionItem.CurrentState is not ItemResizingState)
            return;

        StopAutoPan();
        _primarySelectionItem.PopState();
        _primarySelectionItem.OnResizeCompleted(e.Vector);
        UpdateSelectionOverlayState();
        CommitEdit();
        e.Handled = true;
    }

    private void OnSecondarySelectionResizeStarted(object? sender, SelectionAdornerResizeStartedEventArgs e)
    {
        var container = e.AdornerInfo.Container as DesignEditorItem;
        var target = e.AdornerInfo.Target;

        if (container == null || target == null || !HasMultipleNestedSelection)
            return;

        // Рамка кластера-группы тянет весь его состав, а не первого участника:
        // группа и снаружи, и внутри жеста остаётся одним элементом.
        if (e.AdornerInfo.Members is { Count: > 1 } members)
        {
            BeginEdit(DesignEditKind.Resize);
            if (!TryCreateGroupResizeOperation(members, e.AdornerInfo.Bounds, e.Direction, out var clusterOperation))
            {
                CancelEdit();
                return;
            }

            _groupResizeOperation = clusterOperation;

            if (clusterOperation?.SourceTarget is { } clusterSource)
                BeginSnapGuides(clusterSource);

            e.Handled = true;
            return;
        }

        if (!IsResizeAllowed(target, e.Direction))
            return;

        BeginEdit(DesignEditKind.Resize);
        container.PushState(new ItemResizingState(container, target, e.Direction));
        container.OnResizeStarted(e.Vector);
        e.Handled = true;
    }

    private void OnSecondarySelectionResizeDelta(object? sender, SelectionAdornerResizeDeltaEventArgs e)
    {
        // Жест кластера ведёт групповая операция — та же, что у рамки всего выделения.
        // Условие здесь то же, что и на входе в жест: ветвиться по самому полю операции
        // нельзя, иначе брошенная операция перехватила бы следующий пожатийный resize
        // одиночного контрола.
        if (e.AdornerInfo.Members is { Count: > 1 })
        {
            if (_groupResizeOperation == null)
                return;

            UpdateInteractionOperation(_groupResizeOperation, NormalizeResizeDelta(e.Delta));
            UpdateSelectionOverlayState();
            e.Handled = true;
            TrackSecondaryResizeAutoPan(sender, e);
            return;
        }

        var container = e.AdornerInfo.Container as DesignEditorItem;
        var target = e.AdornerInfo.Target;

        if (container == null || target == null || container.CurrentState is not ItemResizingState)
            return;
        if (!IsResizeAllowed(target, e.Direction))
            return;

        var worldDelta = NormalizeResizeDelta(e.Delta);
        var normalizedArgs = new ResizeDeltaEventArgs(worldDelta, e.Direction, SelectionAdorner.ResizeDeltaEvent)
        {
            Source = e.Source
        };

        // Событие контейнера поднимает само состояние, уже применив геометрию. Второй
        // раз здесь его не поднимать: хост получал каждое движение дважды.
        container.CurrentState.OnResizeDelta(normalizedArgs);
        UpdateSelectionOverlayState();
        e.Handled = true;
        TrackSecondaryResizeAutoPan(sender, e);
    }

    private void TrackSecondaryResizeAutoPan(object? sender, SelectionAdornerResizeDeltaEventArgs e) =>
        TrackResizeAutoPan(() => OnSecondarySelectionResizeDelta(
            sender,
            new SelectionAdornerResizeDeltaEventArgs(e.AdornerInfo, AutoPanScreenShift(), e.Direction, e.RoutedEvent!) { Source = e.Source }));

    private void OnSecondarySelectionResizeCompleted(object? sender, SelectionAdornerResizeCompletedEventArgs e)
    {
        // Жест кластера ведёт групповая операция — та же, что у рамки всего выделения.
        if (e.AdornerInfo.Members is { Count: > 1 })
        {
            if (_groupResizeOperation == null)
                return;

            CompleteGroupResize();
            e.Handled = true;
            return;
        }

        var container = e.AdornerInfo.Container as DesignEditorItem;
        var target = e.AdornerInfo.Target;

        if (container == null || target == null || container.CurrentState is not ItemResizingState)
            return;

        StopAutoPan();
        container.PopState();
        container.OnResizeCompleted(e.Vector);
        UpdateSelectionOverlayState();
        CommitEdit();
        e.Handled = true;
    }

    private void OnGroupSelectionResizeStarted(object? sender, ResizeStartedEventArgs e)
    {
        // Жест принимает та же рамка, которую показывает шаблон: и группа контейнеров,
        // и design-time группа. Условие обязано совпадать с ShowsGroupFrame, иначе
        // ручки видны и берутся мышью, а жест молча не начинается.
        if (!ShowsGroupFrame)
            return;

        // TryCreateGroupResizeOperation фиксирует текущие размеры target'ов,
        // поэтому открывать единицу редактирования нужно до него — и отменять,
        // если операция так и не создалась.
        BeginEdit(DesignEditKind.Resize);
        if (!TryCreateGroupResizeOperation(e.Direction, out var operation))
        {
            CancelEdit();
            return;
        }

        _groupResizeOperation = operation;

        // У группового resize нет состояния контейнера, поэтому снимок соседей
        // берётся здесь. Исключается всё выделение целиком, а не один target.
        if (operation?.SourceTarget is { } guideSource)
            BeginSnapGuides(guideSource);

        e.Handled = true;
    }

    private void OnGroupSelectionResizeDelta(object? sender, ResizeDeltaEventArgs e)
    {
        if (_groupResizeOperation == null)
            return;

        UpdateInteractionOperation(_groupResizeOperation, NormalizeResizeDelta(e.Delta));

        UpdateSelectionOverlayState();
        e.Handled = true;

        TrackResizeAutoPan(() => OnGroupSelectionResizeDelta(
            sender,
            new ResizeDeltaEventArgs(AutoPanScreenShift(), e.Direction, e.RoutedEvent!) { Source = e.Source }));
    }

    /// <summary>
    /// Автопрокрутка у края для изменения размера — тем же механизмом, что у перетаскивания.
    /// </summary>
    /// <remarks>
    /// Ручка сообщает о движении, только пока движется указатель; у края он стоит, а холст
    /// едет. Поэтому шаг автопрокрутки повторяет обработчик сам — с тем же направлением и
    /// сдвигом холста вместо движения указателя. Сам размер от этой дельты не зависит: жест
    /// считает от снимка указателя, который шаг автопрокрутки уже пересчитал.
    /// </remarks>
    private void TrackResizeAutoPan(Action reapply) => TrackAutoPan(LastPointerScreen, _ => reapply());

    // Сдвиг холста в экранных единицах: обработчики ручек переводят дельту в мировые
    // делением на масштаб, как дельту самой ручки.
    private Vector AutoPanScreenShift() => LastAutoPanShift * ViewportZoom;

    private Vector NormalizeResizeDelta(Vector delta)
    {
        var zoom = Math.Max(0.0001, ViewportZoom);
        return delta / zoom;
    }

    private void OnGroupSelectionResizeCompleted(object? sender, VectorEventArgs e)
    {
        if (_groupResizeOperation == null)
            return;

        CompleteGroupResize();
        e.Handled = true;
    }

    /// <summary>
    /// Заканчивает групповое масштабирование: применяет накопленное и закрывает единицу.
    /// </summary>
    /// <remarks>
    /// Точка одна на три входа — рамку всего выделения, рамку кластера и потерю захвата.
    /// Разложить её по местам значило бы получить жест, который где-то закрывается, а
    /// где-то нет.
    /// </remarks>
    private void CompleteGroupResize()
    {
        StopAutoPan();
        CompleteInteractionOperation(ref _groupResizeOperation);
        EndSnapGuides();
        UpdateSelectionOverlayState();
        CommitEdit();
    }

    private bool TryCreateGroupResizeOperation(ResizeDirection direction, out GroupResizeOperation? operation)
    {
        operation = null;

        if (!TryGetSelectedDesignBounds(out var selectionBounds, out var selectedCount, out _, out _, out _, out _, out _, out _)
            || selectedCount <= 1)
        {
            return false;
        }

        var controls = new List<Control>();
        var items = SelectedItems;
        if (items == null)
            return false;

        foreach (var item in items)
        {
            var container = ContainerFromItem(item) as DesignEditorItem;
            if (container == null && item is DesignEditorItem directItem)
                container = directItem;

            if (container == null)
                continue;

            controls.AddRange(ResolveSelectionTargets(container));
        }

        return TryCreateGroupResizeOperation(controls, selectionBounds, direction, out operation);
    }

    /// <summary>
    /// Собирает групповое масштабирование по явному составу.
    /// </summary>
    /// <remarks>
    /// Состав приходит параметром, потому что тянуть можно не только всё выделение:
    /// у выбранных рядом группы и контрола ручки рамки группы обязаны масштабировать
    /// её одну. Правило прежнее — жест не начинается, если хоть одному участнику
    /// политика запрещает эту сторону.
    /// </remarks>
    private bool TryCreateGroupResizeOperation(
        IReadOnlyList<Control> controls,
        Rect frame,
        ResizeDirection direction,
        out GroupResizeOperation? operation)
    {
        operation = null;

        var targets = new List<GroupResizeTarget>(controls.Count);
        foreach (var target in controls)
        {
            if (!IsResizeAllowed(target, direction))
                return false;

            if (!TryGetDesignBounds(target, out var bounds))
                continue;

            SetDesignSize(target, GetDesignSize(target));

            targets.Add(new GroupResizeTarget(target, bounds));
        }

        if (targets.Count <= 1)
            return false;

        operation = new GroupResizeOperation(
            direction,
            frame,
            targets,
            // Предел не поднимается выше рамки, которую тянут: см. ItemResizingState.
            Math.Min(
                Math.Max(0.0, InteractionOptions.ResizeMinSize),
                Math.Min(frame.Width, frame.Height)),
            PointerSample);

        return true;
    }

    private void UpdateInteractionOperation(IInteractionOperation operation, Vector worldDelta)
    {
        operation.Update(this, worldDelta);
    }

    private void CompleteInteractionOperation<TOperation>(ref TOperation? operation)
        where TOperation : class, IInteractionOperation
    {
        operation?.Complete(this);
        operation = null;
    }
}
