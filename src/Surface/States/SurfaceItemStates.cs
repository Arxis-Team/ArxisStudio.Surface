using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace ArxisStudio.Surface.States;

/// <summary>
/// Базовый класс состояния элемента дизайнера.
/// </summary>
internal abstract class SurfaceItemState
{
    /// <summary>
    /// Получает контейнер, которому принадлежит состояние.
    /// </summary>
    protected SurfaceItem Container { get; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SurfaceItemState"/>.
    /// </summary>
    /// <param name="container">Контейнер, которому принадлежит состояние.</param>
    protected SurfaceItemState(SurfaceItem container) => Container = container;

    /// <summary>
    /// Вызывается при входе в состояние.
    /// </summary>
    /// <param name="from">Предыдущее состояние.</param>
    public virtual void Enter(SurfaceItemState from) { }

    /// <summary>
    /// Вызывается при выходе из состояния.
    /// </summary>
    public virtual void Exit() { }

    /// <summary>
    /// Вызывается при повторном входе в состояние после возврата из вложенного состояния.
    /// </summary>
    /// <param name="from">Состояние, из которого произошел возврат.</param>
    public virtual void ReEnter(SurfaceItemState from) { }

    /// <summary>
    /// Обрабатывает нажатие указателя.
    /// </summary>
    /// <param name="e">Аргументы указателя.</param>
    public virtual void OnPointerPressed(PointerPressedEventArgs e) { }

    /// <summary>
    /// Обрабатывает перемещение указателя.
    /// </summary>
    /// <param name="e">Аргументы указателя.</param>
    public virtual void OnPointerMoved(PointerEventArgs e) { }

    /// <summary>
    /// Обрабатывает отпускание указателя.
    /// </summary>
    /// <param name="e">Аргументы указателя.</param>
    public virtual void OnPointerReleased(PointerReleasedEventArgs e) { }

    /// <summary>
    /// Обрабатывает изменение размера.
    /// </summary>
    /// <param name="e">Аргументы изменения размера.</param>
    public virtual void OnResizeDelta(ResizeDeltaEventArgs e) { }

    /// <summary>
    /// Вызывается у базового состояния после потери захвата указателя.
    /// </summary>
    /// <remarks>
    /// Вложенные состояния о потере захвата узнают через <c>Exit</c> — контейнер
    /// разбирает стек. Базовое состояние не снимается никогда, поэтому о брошенном
    /// жесте ему нужно сказать отдельно: нажатие, за которым не последует отпускания,
    /// иначе осталось бы записанным.
    /// </remarks>
    public virtual void OnPointerCaptureLost() { }

    /// <summary>
    /// Псевдокласс, которым контейнер показывает это состояние, или <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Состояние называет его само: контейнер ядра не знает состояний, которые приносят
    /// слои выше, и не должен их перечислять.
    /// </remarks>
    public virtual string? PseudoClass => null;
}

/// <summary>
/// Состояние покоя. Ожидает выделения или начала перетаскивания.
/// </summary>
internal class ItemIdleState : SurfaceItemState
{
    private readonly GestureCursorScope _cursor = new GestureCursorScope();
    private Point _startPoint;
    private bool _isPressed;
    private bool _shouldSkipSelectionToggle;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ItemIdleState"/>.
    /// </summary>
    /// <param name="container">Контейнер, которому принадлежит состояние.</param>
    public ItemIdleState(SurfaceItem container) : base(container) { }

    /// <inheritdoc />
    public override void ReEnter(SurfaceItemState from)
    {
        _isPressed = false;
        _shouldSkipSelectionToggle = false;
        _cursor.Restore();
    }

    /// <inheritdoc />
    public override void OnPointerCaptureLost()
    {
        _isPressed = false;
        _cursor.Restore();
    }

    /// <inheritdoc />
    public override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(Container).Properties;
        if (props.IsLeftButtonPressed)
        {
            var owningEditor = Container.FindAncestorOfType<SurfaceView>();

            // Жест по пустой области может принадлежать рамке выделения.
            // Тогда контейнер не захватывает указатель и не помечает событие
            // обработанным — нажатие всплывает до редактора, который сам решит.
            if (owningEditor != null &&
                owningEditor.ShouldDeferPressToMarquee(Container, e.GetPosition(owningEditor), e.KeyModifiers))
            {
                return;
            }

            e.Pointer.Capture(Container);
            e.Handled = true;
            _isPressed = true;

            var editor = owningEditor;
            var parent = Container.GetVisualParent();
            if (editor != null)
                _startPoint = e.GetPosition(editor);
            else if (parent != null)
                _startPoint = e.GetPosition((Visual)parent);

            HandleSelectionOnPress(e);
        }
    }

    /// <inheritdoc />
    public override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_isPressed || !Container.IsDraggable) return;

        var parent = Container.GetVisualParent();
        if (parent == null) return;

        var editor = Container.FindAncestorOfType<SurfaceView>();

        // Порог считается до проверок, а не после: курсор запрета имеет смысл только
        // тогда, когда пользователь уже повёл элемент. На дрожании в пределах порога
        // он мигал бы на каждом нажатии.
        var currentPoint = editor != null
            ? e.GetPosition(editor)
            : e.GetPosition((Visual)parent);
        var dragStartThreshold = Math.Max(0.0, editor?.InteractionOptions.DragStartThreshold ?? 3.0);
        var beyondThreshold = Vector.Distance(_startPoint, currentPoint) > dragStartThreshold;

        if (editor != null)
        {
            // Спрашиваем не про родителя контейнера, а про фактическую цель жеста:
            // двигаться будет именно она. Что с ней делать, решает поверхность —
            // ядро знает только политику, слой выше знает раскладку и группы.
            var moveTarget = editor.ResolveInteractionTarget(Container);
            var plan = editor.PlanItemDrag(Container, moveTarget);

            if (plan.Kind == ItemDragKind.Refuse)
            {
                RefuseDrag(editor, beyondThreshold);
                if (plan.MarkHandled && beyondThreshold)
                    e.Handled = true;

                return;
            }

            if (!beyondThreshold)
                return;

            if (plan.Kind == ItemDragKind.Custom && plan.CreateState is { } create)
            {
                Container.PushState(create(Container, _startPoint));
                return;
            }
        }
        else if (!Container.CanMoveWithoutSurface)
        {
            // Без поверхности остаётся только fallback-ветка, а она пишет
            // Container.Location — и только там, где его кто-то прочтёт.
            return;
        }

        if (!beyondThreshold)
            return;

        Container.PushState(new ItemDraggingState(Container, _startPoint));
    }

    /// <summary>
    /// Показывает курсором, что перетаскивание не начнётся.
    /// </summary>
    /// <remarks>
    /// Отказ живёт здесь, а не в <see cref="ItemDraggingState"/>: заблокированный
    /// элемент до состояния перетаскивания не доходит вовсе — его отсекают проверки
    /// выше. Это то же правило «не предлагать жест, который ничего не делает»,
    /// только выраженное курсором.
    /// </remarks>
    private void RefuseDrag(SurfaceView? editor, bool beyondThreshold)
    {
        if (editor == null || !beyondThreshold)
            return;

        _cursor.Apply(Container, editor.Cursors.ResolveBlocked());
    }

    /// <inheritdoc />
    public override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_isPressed)
        {
            HandleSelectionOnRelease(e);
            _isPressed = false;
            _cursor.Restore();
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void HandleSelectionOnPress(PointerPressedEventArgs e)
    {
        var editor = Container.FindAncestorOfType<SurfaceView>();
        if (editor == null) return;

        // Контейнер может быть вложен в другой контейнер. Индексный выбор работает
        // только с item'ами верхнего уровня, поэтому selection адресуется владельцу,
        // а сам Container участвует дальше как design target.
        var owner = editor.ResolveOwningItem(Container);
        if (owner == null) return;

        editor.SetLastInputModifiers(e.KeyModifiers);
        bool isAdditive = editor.ShouldUseAdditiveSelection(e.KeyModifiers);
        bool isContainerInteraction = editor.ShouldUseContainerInteraction(e.KeyModifiers);
        if (isAdditive && !isContainerInteraction && !editor.CanAddNestedTargetToContainer(owner))
        {
            _shouldSkipSelectionToggle = true;
            return;
        }

        // Индексный слой пишет редактор — вместе со слоем target'ов, одной транзакцией.
        // Признак «владельца выбрали именно сейчас» снимается до записи.
        _shouldSkipSelectionToggle = !owner.IsSelected;

        editor.UpdateSelectionTargetFromPoint(owner, e.GetPosition(editor), e.KeyModifiers, e.ClickCount);
    }

    private void HandleSelectionOnRelease(PointerReleasedEventArgs e)
    {
        var editor = Container.FindAncestorOfType<SurfaceView>();
        if (editor == null) return;

        var owner = editor.ResolveOwningItem(Container);
        if (owner == null) return;

        bool isAdditive = editor.ShouldUseAdditiveSelection(e.KeyModifiers);
        bool isContainerInteraction = editor.ShouldUseContainerInteraction(e.KeyModifiers);
        var index = editor.IndexFromContainer(owner);
        if (isAdditive && !isContainerInteraction)
        {
            // Additive click should never remove selection from already selected item.
            // This keeps primary target/adorner stable when retargeting nested controls.
            if (!_shouldSkipSelectionToggle && !owner.IsSelected)
                editor.Selection.Select(index);
        }
        else if (!isAdditive && owner.IsSelected && editor.Selection.Count > 1)
        {
            editor.CollapseSelectionTo(owner);
        }
    }
}

/// <summary>
/// Состояние перетаскивания элемента.
/// </summary>
internal class ItemDraggingState : SurfaceItemState
{
    private Point _previousPointerPosition;
    private readonly Point _initialPointerPosition;
    private Point _elementStartLocation;
    private Control _dragTarget = null!;
    private Vector _previousAppliedDelta;
    private readonly GestureCursorScope _cursor = new GestureCursorScope();
    private bool _accepted;

    /// <inheritdoc />
    public override string? PseudoClass => ":dragging";

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ItemDraggingState"/>.
    /// </summary>
    /// <param name="container">Контейнер, который перетаскивается.</param>
    /// <param name="initialPointerPosition">Начальная позиция указателя в координатах редактора.</param>
    public ItemDraggingState(SurfaceItem container, Point initialPointerPosition) : base(container)
    {
        _initialPointerPosition = initialPointerPosition;
        _previousPointerPosition = initialPointerPosition;
    }

    /// <inheritdoc />
    public override void Enter(SurfaceItemState from)
    {
        var editor = Container.FindAncestorOfType<SurfaceView>();
        _dragTarget = editor?.ResolveInteractionTarget(Container) ?? Container;
        _elementStartLocation = editor?.GetDesignPosition(_dragTarget) ?? Container.Location;
        _previousAppliedDelta = Vector.Zero;

        // Соседи снимаются здесь, до первого кадра: во время жеста они не двигаются,
        // и направляющая обязана стоять там, где пользователь её увидел.
        editor?.BeginSnapGuides(_dragTarget);

        Container.RaiseEvent(new DragStartedEventArgs(_initialPointerPosition.X, _initialPointerPosition.Y) { RoutedEvent = SurfaceItem.DragStartedEvent });

        // Редактор мог жест отклонить. Признак — открытая единица редактирования:
        // e.Handled не различает отказ от согласия, редактор ставит его на всех
        // ветках. Без этой проверки отклонённый жест продолжал писать геометрию
        // каждый кадр при закрытой единице, то есть мимо undo.
        _accepted = editor == null || editor.HasActiveEdit;

        // Курсор ставится контейнеру: захват взял он, ещё в ItemIdleState. Курсор запрета
        // сюда не приходит — до этого состояния отклонённый жест не доходит вовсе,
        // его отсекает ItemIdleState, и ставить запрет надо там.
        if (editor != null)
            _cursor.Apply(Container, editor.Cursors.ResolveMove());
    }

    /// <inheritdoc />
    public override void Exit()
    {
        _cursor.Restore();

        var editor = Container.FindAncestorOfType<SurfaceView>();
        editor?.EndSnapGuides();

        var total = editor != null
            ? new Point(_previousAppliedDelta.X, _previousAppliedDelta.Y)
            : _previousPointerPosition - _initialPointerPosition;
        Container.RaiseEvent(new DragCompletedEventArgs(total.X, total.Y, false) { RoutedEvent = SurfaceItem.DragCompletedEvent });
    }

    /// <inheritdoc />
    public override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_accepted)
        {
            e.Handled = true;
            return;
        }

        var editor = Container.FindAncestorOfType<SurfaceView>();
        if (editor != null)
        {
            var currentPointerPosition = e.GetPosition(editor);
            var rawTotalDelta = editor.GetWorldPosition(currentPointerPosition) - editor.GetWorldPosition(_initialPointerPosition);
            var appliedTotalDelta = editor.ApplyMovePolicy(_dragTarget, rawTotalDelta);

            // Привязывается результат, а не дельта: иначе элемент сохранил бы
            // исходное смещение относительно сетки и на узел бы не встал.
            // Направляющие занимают свою ось первыми, сетка получает остальные.
            var snapped = editor.ResolveDragPosition(_dragTarget, _elementStartLocation + appliedTotalDelta, e.KeyModifiers);

            // Дальше по группе идёт уже привязанное смещение, поэтому соседи
            // двигаются ровно на столько же и взаимное расположение сохраняется.
            var effectiveDelta = snapped - _elementStartLocation;

            editor.SetDesignPosition(_dragTarget, snapped);
            var frameDelta = effectiveDelta - _previousAppliedDelta;
            Container.RaiseEvent(new DragDeltaEventArgs(frameDelta.X, frameDelta.Y) { RoutedEvent = SurfaceItem.DragDeltaEvent });
            _previousAppliedDelta = effectiveDelta;
            _previousPointerPosition = currentPointerPosition;
            e.Handled = true;
            return;
        }

        var parent = Container.GetVisualParent();
        if (parent == null) return;

        var currentPointerPositionFallback = e.GetPosition((Visual)parent);
        var totalDeltaFallback = currentPointerPositionFallback - _initialPointerPosition;
        double fallbackX = Math.Round(_elementStartLocation.X + totalDeltaFallback.X);
        double fallbackY = Math.Round(_elementStartLocation.Y + totalDeltaFallback.Y);
        Container.Location = new Point(fallbackX, fallbackY);

        var frameDeltaFallback = currentPointerPositionFallback - _previousPointerPosition;
        Container.RaiseEvent(new DragDeltaEventArgs(frameDeltaFallback.X, frameDeltaFallback.Y) { RoutedEvent = SurfaceItem.DragDeltaEvent });
        _previousPointerPosition = currentPointerPositionFallback;
        e.Handled = true;
    }

    /// <inheritdoc />
    public override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        Container.PopState();
        e.Pointer.Capture(null);
        e.Handled = true;
    }
}

