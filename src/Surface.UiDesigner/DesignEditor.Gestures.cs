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
using ArxisStudio.Controls;
using ArxisStudio.Surface.Editing;
using ArxisStudio.Surface.UiDesigner.Placement;
using ArxisStudio.States;
using ArxisStudio.Surface;

namespace ArxisStudio.Surface.UiDesigner;

// Ввод: машина состояний редактора, указатель, клавиатура, drag и resize.
// Часть DesignEditor; общее описание типа — в DesignEditor.cs.
public partial class DesignEditor
{
    private readonly Stack<EditorState> _states = new();

    /// <summary>
    /// Получает текущее активное состояние редактора.
    /// </summary>
    internal EditorState CurrentState => _states.Count > 0 ? _states.Peek() : null!;

    /// <summary>
    /// Помещает новое состояние в стек и вызывает его инициализацию.
    /// </summary>
    /// <param name="state">Состояние, которое должно стать активным.</param>
    internal void PushState(EditorState state)
    {
        var previous = _states.Count > 0 ? _states.Peek() : null;
        _states.Push(state);
        state.Enter(previous);
    }

    /// <summary>
    /// Завершает текущее состояние и возвращается к предыдущему, если стек содержит более одного состояния.
    /// </summary>
    internal void PopState()
    {
        if (_states.Count > 1)
        {
            var current = _states.Pop();
            current.Exit();
        }
    }

    // --- Input Handling ---

    /// <summary>
    /// Обрабатывает нажатие указателя и маршрутизирует его в active state редактора.
    /// </summary>
    /// <param name="e">Аргументы указателя.</param>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _lastMousePosition = e.GetPosition(this);
        LastInputModifiers = e.KeyModifiers;

        // Focusable сам по себе фокус не даёт: без него клавиатурные жесты
        // до редактора не доходят. Проверка IsKeyboardFocusWithin не даёт
        // отобрать фокус у вложенного редактируемого контрола.
        if (!IsKeyboardFocusWithin)
            Focus();

        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            RetargetSelectionForContext(_lastMousePosition, e.KeyModifiers);
            RequestContextSafe(DesignEditorContextSource.Pointer, _lastMousePosition, e.KeyModifiers);
            e.Handled = true;
            return;
        }

        CurrentState.OnPointerPressed(e);

        if (!e.Handled) base.OnPointerPressed(e);
    }

    /// <summary>
    /// Обрабатывает нажатие клавиши: смещение выделения, снятие выделения,
    /// выбор всего и запрос удаления.
    /// </summary>
    /// <param name="e">Аргументы клавиатуры.</param>
    /// <remarks>
    /// Уже обработанные нажатия пропускаются: если фокус во вложенном редактируемом
    /// контроле, стрелки и Delete принадлежат ему, а не редактору.
    /// </remarks>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
            return;

        // История разбирается до switch: сочетание задаётся целиком и настраивается,
        // поэтому конкретная клавиша заранее не известна и ветвиться по ней нельзя.
        if (TryHandleHistoryKey(e))
            return;

        switch (e.Key)
        {
            case Key.Left:
            case Key.Right:
            case Key.Up:
            case Key.Down:
                e.Handled = TryNudgeSelection(e.Key, e.KeyModifiers);
                break;

            case Key.Escape:
                e.Handled = TryClearSelection();
                break;

            case Key.Delete:
            case Key.Back:
                e.Handled = TryRequestDelete();
                break;

            case Key.A when ShouldUseContainerInteraction(e.KeyModifiers):
                e.Handled = TrySelectAll();
                break;
        }
    }

    /// <summary>
    /// Сочетания клавиш, принятые на этой платформе.
    /// </summary>
    /// <remarks>
    /// Отмена — не политика редактора, а соглашение системы: на macOS это Cmd, а не
    /// Ctrl, и повтор там пишется Cmd + Shift + Z. Спрашивать платформу дешевле, чем
    /// заводить свои свойства и потом объяснять, почему они не совпадают с остальным
    /// приложением.
    /// </remarks>
    /// <summary>
    /// Разбирает нажатие как отмену или повтор.
    /// </summary>
    /// <param name="e">Аргументы нажатия.</param>
    /// <returns><see langword="true"/>, если нажатие обработано.</returns>
    private bool TryHandleHistoryKey(KeyEventArgs e)
    {
        if (Matches(InputGestures.UndoGestures ?? HotkeyConfiguration?.Undo, e))
        {
            e.Handled = TryRequestHistory(UndoRequested);
            return e.Handled;
        }

        if (Matches(InputGestures.RedoGestures ?? HotkeyConfiguration?.Redo, e))
        {
            e.Handled = TryRequestHistory(RedoRequested);
            return e.Handled;
        }

        return false;
    }

    private PlatformHotkeyConfiguration? HotkeyConfiguration => this.GetPlatformSettings()?.HotkeyConfiguration;

    private static bool Matches(IReadOnlyList<KeyGesture>? gestures, KeyEventArgs e)
    {
        if (gestures == null)
            return false;

        for (var i = 0; i < gestures.Count; i++)
        {
            if (gestures[i].Matches(e))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Поднимает запрос к истории правок.
    /// </summary>
    /// <param name="handler">Подписчики запроса.</param>
    /// <returns><see langword="true"/>, если запрос выполнен.</returns>
    /// <remarks>
    /// Обход останавливается на первом выполнившем — по той же причине, что у удаления
    /// и перестановки: второй обработчик отменял бы уже не то, о чём его спросили.
    /// </remarks>
    private bool TryRequestHistory(EventHandler<DesignEditorHistoryRequestedEventArgs>? handler)
    {
        if (handler == null)
            return false;

        var args = new DesignEditorHistoryRequestedEventArgs();
        foreach (var invocation in handler.GetInvocationList())
        {
            ((EventHandler<DesignEditorHistoryRequestedEventArgs>)invocation)(this, args);

            if (args.Handled)
                break;
        }

        return args.Handled;
    }

    private bool TryNudgeSelection(Key key, KeyModifiers modifiers)
    {
        var targets = SelectedDesignTargets;
        if (targets.Count == 0)
            return false;

        var step = MatchesModifiers(modifiers, InputGestures.LargeNudgeModifiers)
            && InputGestures.LargeNudgeModifiers != KeyModifiers.None
                ? InteractionOptions.LargeNudgeStep
                : InteractionOptions.NudgeStep;

        var delta = key switch
        {
            Key.Left => new Vector(-step, 0),
            Key.Right => new Vector(step, 0),
            Key.Up => new Vector(0, -step),
            Key.Down => new Vector(0, step),
            _ => default
        };

        if (delta.X == 0 && delta.Y == 0)
            return false;

        // Одно нажатие — одна единица редактирования, как и одно перетаскивание.
        BeginEdit(DesignEditKind.Move);

        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i].Target;
            var filtered = ApplyMovePolicy(target, delta);
            if (filtered.X == 0 && filtered.Y == 0)
                continue;

            SetDesignPosition(target, GetDesignPosition(target) + filtered);
        }

        CommitEdit();
        UpdateSelectionOverlayState();
        return true;
    }

    private bool TryClearSelection()
    {
        if (SelectedDesignTargets.Count == 0)
            return false;

        Selection.Clear();
        _selectedTargets.Clear();
        UpdateSelectionOverlayState();
        return true;
    }

    private bool TrySelectAll()
    {
        if (ItemCount == 0)
            return false;

        using (Selection.BatchUpdate())
        {
            Selection.Clear();
            Selection.SelectAll();
        }

        // Выбор всего работает на уровне контейнеров: это единица документа.
        _selectedTargets.Clear();
        if (Presenter?.Panel != null)
        {
            foreach (var child in Presenter.Panel.Children)
            {
                if (child is DesignEditorItem container)
                    AddSelectedTarget(container);
            }
        }

        UpdateSelectionOverlayState();
        return true;
    }

    private bool TryRequestDelete()
    {
        var targets = SelectedDesignTargets;
        if (targets.Count == 0)
            return false;

        var handler = DeleteRequested;
        if (handler == null)
            return false;

        var args = new DesignEditorDeleteRequestedEventArgs(targets);

        // Обработчики обходятся по одному, и первый же выполнивший удаление
        // останавливает обход. Список targets снят до правки, поэтому следующему
        // он описывал бы выделение, которого уже нет. Ровно это было исправлено
        // для ReorderRequested и не было исправлено здесь.
        foreach (var invocation in handler.GetInvocationList())
        {
            ((EventHandler<DesignEditorDeleteRequestedEventArgs>)invocation)(this, args);

            if (args.Handled)
                break;
        }

        return args.Handled;
    }

    /// <summary>
    /// Обрабатывает перемещение указателя и обновляет последнюю известную позицию курсора.
    /// </summary>
    /// <param name="e">Аргументы указателя.</param>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        _lastMousePosition = e.GetPosition(this);
        CurrentState.OnPointerMoved(e);
        base.OnPointerMoved(e);
    }

    /// <summary>
    /// Обрабатывает отпускание указателя и завершает текущее interaction-состояние при необходимости.
    /// </summary>
    /// <param name="e">Аргументы указателя.</param>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        CurrentState.OnPointerReleased(e);
        base.OnPointerReleased(e);
    }

    /// <summary>
    /// Разбирает стек состояний, если редактор потерял захват указателя.
    /// </summary>
    /// <param name="e">Аргументы потери захвата.</param>
    /// <remarks>
    /// Рамка выделения и панорамирование выходят только через отпускание. Отпускание
    /// доходит почти всегда, но захват можно и потерять — его забирает другой элемент,
    /// захваченный уходит из дерева, платформа отбирает сама. Тогда состояние остаётся
    /// на стеке, и это не косметика: брошенная рамка держит <see cref="IsSelecting"/>,
    /// а по нему <c>OnItemsDragStarted</c> отклоняет следующее перетаскивание — при том
    /// что контейнер об отказе не узнаёт и продолжает писать геометрию каждый кадр
    /// с закрытой единицей редактирования. Правка уходила мимо undo.
    /// <para>
    /// Тот же приём, что у контейнера (<c>DesignEditorItem.OnPointerCaptureLost</c>):
    /// разобрать стек до базового состояния, дав каждому выйти своим <c>Exit</c>.
    /// </para>
    /// </remarks>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        while (_states.Count > 1)
            PopState();

        // Брошенный групповой жест закрывается здесь же. Иначе и операция, и открытая
        // единица редактирования переживают его: правка ушла бы мимо отмены, а следующий
        // жест достался бы чужой операции. Единица именно фиксируется, а не затирается —
        // геометрия к этому моменту уже применена, и поздняя запись лучше потерянной.
        if (_groupResizeOperation != null)
            CompleteGroupResize();
    }

    /// <summary>
    /// Решает, набирает ли рамка контейнеры целиком.
    /// </summary>
    /// <param name="viewportPoint">Точка нажатия в координатах редактора.</param>
    /// <param name="modifiers">Модификаторы на момент нажатия.</param>
    /// <remarks>
    /// Рамка, начатая на пустом холсте, набирает формы, а не их содержимое: снаружи
    /// контейнеров выбирать содержимое не за что — пользователь видит формы и обводит
    /// формы. Начатая внутри формы — работает в её пределах, как и раньше.
    /// <para>
    /// <see cref="DesignEditorInputGestures.ContainerInteractionModifiers"/> остаётся
    /// способом потребовать контейнеров и изнутри формы.
    /// </para>
    /// <para>
    /// Спрашивается один раз, на входе в жест: режим рамки не должен меняться посреди
    /// протяжки — в отличие от её владельца, который пересчитывается каждый кадр.
    /// </para>
    /// </remarks>
    internal bool ShouldUseContainerMarquee(Point viewportPoint, KeyModifiers modifiers)
    {
        if (ShouldUseContainerInteraction(modifiers))
            return true;

        return FindContainerAtWorldPoint(GetWorldPosition(viewportPoint)) == null;
    }

    /// <summary>
    /// Обрабатывает колесо мыши и делегирует управление активному состоянию редактора.
    /// </summary>
    /// <param name="e">Аргументы колесика мыши.</param>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (e.Handled) return;

        // Помечать обработанным можно только то, что действительно потребили.
        // Безусловный Handled съедал колесо и тогда, когда зум не сработал —
        // заданы ZoomModifiers, но не нажаты, — и внешний ScrollViewer,
        // внутри которого лежит редактор, переставал прокручиваться вовсе.
        e.Handled = CurrentState.OnPointerWheelChanged(e);
    }

    // --- Drag & Drop ---

    private void OnItemsDragStarted(DragStartedEventArgs e)
    {
        _groupDragOperation = null;

        if (IsSelecting || CurrentState is EditorPanningState)
        {
            e.Handled = true;
            return;
        }

        var sourceContainer = e.Source as DesignEditorItem;
        var items = SelectedItems;
        if (sourceContainer == null || items == null || items.Count == 0)
        {
            e.Handled = true;
            return;
        }

        var selectionCapabilities = GetSelectionInteractionCapabilities();
        if (ShouldBlockNestedGroupDrag(selectionCapabilities))
        {
            e.Handled = true;
            return;
        }

        var sourceTarget = ResolveInteractionTarget(sourceContainer);
        var sourceMovePolicy = GetEffectiveMovePolicy(sourceTarget);
        if (sourceMovePolicy == ArxisStudio.Surface.MovePolicy.None)
        {
            e.Handled = true;
            return;
        }

        // Все проверки пройдены — жест состоится, открываем единицу редактирования.
        BeginEdit(DesignEditKind.Move);
        _groupDragOperation = GroupDragOperation.TryCreate(this, sourceContainer, sourceTarget);

        e.Handled = true;
    }

    private void OnItemsDragDelta(DragDeltaEventArgs e)
    {
        if (IsSelecting || CurrentState is EditorPanningState) return;

        var items = SelectedItems;
        if (items == null || items.Count == 0) return;
        var source = e.Source as DesignEditorItem;
        if (source != null)
        {
            if (ShouldBlockNestedGroupDrag(GetSelectionInteractionCapabilities()))
            {
                e.Handled = true;
                return;
            }

            var sourceTarget = ResolveInteractionTarget(source);
            if (GetEffectiveMovePolicy(sourceTarget) == ArxisStudio.Surface.MovePolicy.None)
            {
                e.Handled = true;
                return;
            }
        }

        if (_groupDragOperation != null &&
            e.Source is DesignEditorItem sourceContainer &&
            _groupDragOperation.CanHandle(sourceContainer))
        {
            UpdateInteractionOperation(_groupDragOperation, new Vector(e.HorizontalChange, e.VerticalChange));

            e.Handled = true;
            UpdateSelectionOverlayState();
            return;
        }

        var delta = new Vector(e.HorizontalChange, e.VerticalChange);

        foreach (var item in items)
        {
            var container = ContainerFromItem(item) as DesignEditorItem;
            if (container == null && item is DesignEditorItem directItem)
                container = directItem;

            if (container == null || !container.IsDraggable || ReferenceEquals(container, source))
                continue;

            var target = ResolveInteractionTarget(container);
            var position = GetDesignPosition(target);
            var filteredDelta = ApplyMovePolicy(target, delta);
            SetDesignPosition(target, position + filteredDelta);
        }
        e.Handled = true;
        UpdateSelectionOverlayState();
    }

    private void OnItemsDragCompleted(DragCompletedEventArgs e)
    {
        CompleteInteractionOperation(ref _groupDragOperation);
        CommitEdit();
        e.Handled = true;
    }

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

        _primarySelectionItem.CurrentState.OnResizeDelta(normalizedArgs);
        _primarySelectionItem.OnResizeDelta(new ResizeDeltaEventArgs(worldDelta, e.Direction, DesignEditorItem.ResizeDeltaEvent));
        UpdateSelectionOverlayState();
        e.Handled = true;
    }

    private void OnSelectionResizeCompleted(object? sender, VectorEventArgs e)
    {
        if (_primarySelectionItem == null || _primarySelectionControl == null || _primarySelectionItem.CurrentState is not ItemResizingState)
            return;

        _primarySelectionItem.PopState();
        _primarySelectionItem.OnResizeCompleted(e.Vector);
        UpdateSelectionOverlayState();
        CommitEdit();
        e.Handled = true;
    }

    private void OnSecondarySelectionResizeStarted(object? sender, SelectionAdornerResizeStartedEventArgs e)
    {
        var container = e.AdornerInfo.Container;
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
            return;
        }

        var container = e.AdornerInfo.Container;
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

        container.CurrentState.OnResizeDelta(normalizedArgs);
        container.OnResizeDelta(new ResizeDeltaEventArgs(worldDelta, e.Direction, DesignEditorItem.ResizeDeltaEvent));
        UpdateSelectionOverlayState();
        e.Handled = true;
    }

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

        var container = e.AdornerInfo.Container;
        var target = e.AdornerInfo.Target;

        if (container == null || target == null || container.CurrentState is not ItemResizingState)
            return;

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
    }

    private PointerSample? _pointerSample;

    /// <summary>
    /// Последнее движение указателя: чьё, куда и каким по счёту.
    /// </summary>
    /// <remarks>
    /// Изменение размера обязано считать поправку от указателя, а не от того, успела ли
    /// ручка переехать. <c>Thumb.DragDelta</c> меряет смещение относительно самой ручки,
    /// и приращённым оно бывает лишь пока между двумя движениями проходит layout. Стоит
    /// указателю обогнать раскладку — а на занятом UI-потоке это обычное дело, — и каждая
    /// дельта несёт всё расстояние заново; сложенные, они растят размер квадратично.
    /// <para>
    /// Одного положения мало, и это выяснилось замером. Во-первых, снимок переживает
    /// жест: хост, поднявший события resize сам, попадал на «указатель не двигался»
    /// и получал нулевую поправку вместо своей дельты — жест переставал работать молча.
    /// Во-вторых, движение приходит от любого устройства: перо, зависшее над холстом,
    /// пока мышь тянет ручку, увело бы край к перу. Поэтому снимок несёт и устройство,
    /// и номер движения, а пользоваться им можно, только если <b>это же</b> устройство
    /// двинулось <b>после</b> начала жеста.
    /// </para>
    /// </remarks>
    internal PointerSample? PointerSample => _pointerSample;

    private int _pointerMoveCount;

    private void OnTrackPointer(object? sender, PointerEventArgs e)
    {
        _pointerSample = new PointerSample(
            e.Pointer.Id,
            GetWorldPosition(e.GetPosition(this)),
            ++_pointerMoveCount);
    }

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
        CompleteInteractionOperation(ref _groupResizeOperation);
        EndSnapGuides();
        UpdateSelectionOverlayState();
        CommitEdit();
    }

    /// <summary>
    /// Определяет, должен ли контейнер уступить нажатие рамке выделения.
    /// </summary>
    /// <param name="container">Контейнер, получивший нажатие. Может быть вложенным.</param>
    /// <param name="viewportPoint">Точка нажатия в координатах редактора.</param>
    /// <param name="modifiers">Модификаторы ввода.</param>
    /// <remarks>
    /// Решение принимает редактор, а не состояние контейнера: политика ввода живёт
    /// в <see cref="SurfaceView.InputGestures"/>, и контейнеру знать о ней незачем. Уступив жест,
    /// контейнер не захватывает указатель и не помечает событие обработанным,
    /// поэтому нажатие всплывает до редактора обычным маршрутом.
    /// <para>
    /// Контейнер удерживает жест, если нажат <see cref="DesignEditorInputGestures.ContainerInteractionModifiers"/>,
    /// если контейнер уже выбран целиком, либо если под точкой есть design target.
    /// </para>
    /// </remarks>
    internal bool ShouldDeferPressToMarquee(DesignEditorItem container, Point viewportPoint, KeyModifiers modifiers)
    {
        if (InputGestures.ContainerEmptyAreaDrag != ContainerEmptyAreaDragGesture.Marquee)
            return false;

        if (ShouldUseContainerInteraction(modifiers))
            return false;

        // Уже выбранный контейнер перетаскивается без модификаторов —
        // иначе его нельзя было бы двигать мышью вовсе.
        if (_selectedTargets.Contains(container))
            return false;

        var worldPoint = GetWorldPosition(viewportPoint);
        return !TryResolveSelectionTargetAtPoint(container, worldPoint, out _);
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
