using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ArxisStudio.Controls;
using ArxisStudio.Surface.UiDesigner.Placement;
using ArxisStudio.Surface;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.States;

/// <summary>
/// Базовый класс состояния элемента дизайнера.
/// </summary>
internal abstract class DesignEditorItemState
{
    /// <summary>
    /// Получает контейнер, которому принадлежит состояние.
    /// </summary>
    protected DesignEditorItem Container { get; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignEditorItemState"/>.
    /// </summary>
    /// <param name="container">Контейнер, которому принадлежит состояние.</param>
    protected DesignEditorItemState(DesignEditorItem container) => Container = container;

    /// <summary>
    /// Вызывается при входе в состояние.
    /// </summary>
    /// <param name="from">Предыдущее состояние.</param>
    public virtual void Enter(DesignEditorItemState from) { }

    /// <summary>
    /// Вызывается при выходе из состояния.
    /// </summary>
    public virtual void Exit() { }

    /// <summary>
    /// Вызывается при повторном входе в состояние после возврата из вложенного состояния.
    /// </summary>
    /// <param name="from">Состояние, из которого произошел возврат.</param>
    public virtual void ReEnter(DesignEditorItemState from) { }

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
}

/// <summary>
/// Состояние покоя. Ожидает выделения или начала перетаскивания.
/// </summary>
internal class ItemIdleState : DesignEditorItemState
{
    private readonly GestureCursorScope _cursor = new GestureCursorScope();
    private Point _startPoint;
    private bool _isPressed;
    private bool _shouldSkipSelectionToggle;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ItemIdleState"/>.
    /// </summary>
    /// <param name="container">Контейнер, которому принадлежит состояние.</param>
    public ItemIdleState(DesignEditorItem container) : base(container) { }

    /// <inheritdoc />
    public override void ReEnter(DesignEditorItemState from)
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
            var owningEditor = Container.FindAncestorOfType<DesignEditor>();

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

        var editor = Container.FindAncestorOfType<DesignEditor>();
        var semantics = DesignMoveSemantics.Reposition;

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
            // двигаться будет именно она, и решает её собственная раскладка.
            // Прежний гейт проверял не тот уровень и поэтому пропускал жесты,
            // которые затем молча ничего не делали.
            var moveTarget = editor.ResolveInteractionTarget(Container);

            // Пользовательский запрет сильнее любой раскладки — в том числе
            // сильнее перестановки.
            if (editor.GetMovePolicy(moveTarget) == ArxisStudio.Surface.MovePolicy.None)
            {
                RefuseDrag(editor, beyondThreshold);
                return;
            }

            semantics = DesignEditor.GetPlacementStrategy(moveTarget).MoveSemantics;
            if (semantics == DesignMoveSemantics.None)
            {
                RefuseDrag(editor, beyondThreshold);
                return;
            }
        }
        else if (parent is not AbsolutePanel)
        {
            // Без редактора остаётся только fallback-ветка, а она пишет
            // Container.Location, которую читает лишь AbsolutePanel.
            return;
        }

        if (!beyondThreshold)
            return;

        if (semantics == DesignMoveSemantics.Reorder)
        {
            // Перестановку выполняет приложение, и без подписчика она
            // не произойдёт. Жест тогда не начинается вовсе: вести точку
            // вставки за курсором, зная, что на отпускании ничего не будет,
            // — то же самое, что предлагать заблокированное перемещение.
            if (editor is { CanRequestReorder: true })
                Container.PushState(new ItemReorderingState(Container, _startPoint));
            else
                RefuseDrag(editor, true);

            return;
        }

        if (editor != null && editor.ShouldBlockNestedGroupDrag())
        {
            RefuseDrag(editor, true);
            e.Handled = true;
            return;
        }

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
    private void RefuseDrag(DesignEditor? editor, bool beyondThreshold)
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
        var editor = Container.FindAncestorOfType<DesignEditor>();
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
        var editor = Container.FindAncestorOfType<DesignEditor>();
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
internal class ItemDraggingState : DesignEditorItemState
{
    private Point _previousPointerPosition;
    private readonly Point _initialPointerPosition;
    private Point _elementStartLocation;
    private Control _dragTarget = null!;
    private Vector _previousAppliedDelta;
    private readonly GestureCursorScope _cursor = new GestureCursorScope();
    private bool _accepted;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ItemDraggingState"/>.
    /// </summary>
    /// <param name="container">Контейнер, который перетаскивается.</param>
    /// <param name="initialPointerPosition">Начальная позиция указателя в координатах редактора.</param>
    public ItemDraggingState(DesignEditorItem container, Point initialPointerPosition) : base(container)
    {
        _initialPointerPosition = initialPointerPosition;
        _previousPointerPosition = initialPointerPosition;
    }

    /// <inheritdoc />
    public override void Enter(DesignEditorItemState from)
    {
        var editor = Container.FindAncestorOfType<DesignEditor>();
        _dragTarget = editor?.ResolveInteractionTarget(Container) ?? Container;
        _elementStartLocation = editor?.GetDesignPosition(_dragTarget) ?? Container.Location;
        _previousAppliedDelta = Vector.Zero;

        // Соседи снимаются здесь, до первого кадра: во время жеста они не двигаются,
        // и направляющая обязана стоять там, где пользователь её увидел.
        editor?.BeginSnapGuides(_dragTarget);

        Container.RaiseEvent(new DragStartedEventArgs(_initialPointerPosition.X, _initialPointerPosition.Y) { RoutedEvent = DesignEditorItem.DragStartedEvent });

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

        var editor = Container.FindAncestorOfType<DesignEditor>();
        editor?.EndSnapGuides();

        var total = editor != null
            ? new Point(_previousAppliedDelta.X, _previousAppliedDelta.Y)
            : _previousPointerPosition - _initialPointerPosition;
        Container.RaiseEvent(new DragCompletedEventArgs(total.X, total.Y, false) { RoutedEvent = DesignEditorItem.DragCompletedEvent });
    }

    /// <inheritdoc />
    public override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_accepted)
        {
            e.Handled = true;
            return;
        }

        var editor = Container.FindAncestorOfType<DesignEditor>();
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
            Container.RaiseEvent(new DragDeltaEventArgs(frameDelta.X, frameDelta.Y) { RoutedEvent = DesignEditorItem.DragDeltaEvent });
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
        Container.RaiseEvent(new DragDeltaEventArgs(frameDeltaFallback.X, frameDeltaFallback.Y) { RoutedEvent = DesignEditorItem.DragDeltaEvent });
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

/// <summary>
/// Состояние изменения размера элемента.
/// </summary>
internal class ItemResizingState : DesignEditorItemState
{
    private readonly Control _target;
    private readonly ResizeDirection _direction;
    private Point _location;
    private Size _size;

    /// <summary>
    /// Указатель, которым начали жест, и насколько он отстоял от двигающегося края.
    /// </summary>
    /// <remarks>
    /// Ручку берут не за край, а за её середину плюс-минус несколько пикселей. Без этой
    /// поправки край на первом же движении прыгнул бы к указателю на величину захвата.
    /// </remarks>
    private PointerSample? _grabSample;
    private Vector _grabOffset;

    /// <summary>
    /// Размер на входе в жест: выше него предел редактора не поднимается.
    /// </summary>
    private Size _startSize;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ItemResizingState"/>.
    /// </summary>
    /// <param name="container">Контейнер, размер которого изменяется.</param>
    /// <param name="target">Visual target, к которому применяется изменение размера.</param>
    /// <param name="direction">Направление активной ручки изменения размера.</param>
    public ItemResizingState(DesignEditorItem container, Control target, ResizeDirection direction) : base(container)
    {
        _target = target;
        _direction = direction;
    }

    /// <inheritdoc />
    public override void Enter(DesignEditorItemState from)
    {
        var editor = Container.FindAncestorOfType<DesignEditor>();
        _location = editor?.GetDesignPosition(_target) ?? Container.Location;

        var size = editor?.GetDesignSize(_target) ?? new Size(
            double.IsNaN(Container.Width) ? Container.Bounds.Width : Container.Width,
            double.IsNaN(Container.Height) ? Container.Bounds.Height : Container.Height);

        editor?.SetDesignSize(_target, size);
        _size = size;
        _startSize = size;

        _grabSample = editor?.PointerSample;
        if (_grabSample is { } sample)
            _grabOffset = sample.World - DesignEditor.MovingEdge(_direction, new Rect(_location, _size));

        // Соседи снимаются здесь по той же причине, что и при перетаскивании:
        // внутри жеста они не двигаются, и линия обязана стоять там, где её
        // увидел пользователь.
        editor?.BeginSnapGuides(_target);
    }

    /// <inheritdoc />
    public override void Exit()
    {
        Container.FindAncestorOfType<DesignEditor>()?.EndSnapGuides();
    }

    /// <inheritdoc />
    public override void OnResizeDelta(ResizeDeltaEventArgs e)
    {
        // Отсчёт идёт от УЖЕ ПРИМЕНЁННОЙ геометрии, а не от исходной, и дельты
        // не накапливаются. Thumb отдаёт смещение относительно самой ручки, а
        // ручка стоит на применённом крае: всё, что съели привязка, Max или
        // границы формы, вернётся в следующей дельте. Накопление складывало бы
        // этот остаток снова и снова — рамка и контрол начинали прыгать.
        double newW = _size.Width;
        double newH = _size.Height;
        double newX = _location.X;
        double newY = _location.Y;
        var editor = Container.FindAncestorOfType<DesignEditor>();
        // Предел редактора не увеличивает: он существует, чтобы контрол нельзя было
        // схлопнуть в точку и потерять, а про контрол, который автор сделал мельче,
        // он ничего не знает. Иначе первая же протяжка за нижний край подбрасывала бы
        // высоту вверх — и именно так это и выглядело.
        double editorMinSize = Math.Max(0.0, editor?.InteractionOptions.ResizeMinSize ?? 10.0);
        double minWidth = Math.Max(Math.Min(editorMinSize, _startSize.Width), _target.MinWidth);
        double minHeight = Math.Max(Math.Min(editorMinSize, _startSize.Height), _target.MinHeight);

        // Неподвижный край берётся из текущей геометрии, поэтому он остаётся
        // на месте на всём жесте, даже когда размер во что-нибудь упёрся.
        double fixedRight = _location.X + _size.Width;
        double fixedBottom = _location.Y + _size.Height;

        // Дельта считается от указателя, а не от того, успела ли переехать ручка.
        // Смысл величины прежний — «насколько указатель убежал от применённого края», —
        // поэтому остаток, съеденный привязкой или ограничением, по-прежнему возвращается
        // следующим движением, а элемент не отстаёт от курсора. Изменилось только то,
        // чем этот остаток измеряется: раньше положением ручки, теперь самим указателем.
        var delta = e.Delta;
        if (DesignEditor.TryGetGesturePointer(editor?.PointerSample, _grabSample, out var pointer))
            delta = (pointer - _grabOffset) - DesignEditor.MovingEdge(_direction, new Rect(_location, _size));

        double dx = delta.X;
        double dy = delta.Y;

        switch (_direction)
        {
            case ResizeDirection.Right: newW += dx; break;
            case ResizeDirection.Bottom: newH += dy; break;
            case ResizeDirection.Left: newW -= dx; newX += dx; break;
            case ResizeDirection.Top: newH -= dy; newY += dy; break;
            case ResizeDirection.BottomRight: newW += dx; newH += dy; break;
            case ResizeDirection.BottomLeft: newW -= dx; newX += dx; newH += dy; break;
            case ResizeDirection.TopRight: newW += dx; newH -= dy; newY += dy; break;
            case ResizeDirection.TopLeft: newW -= dx; newX += dx; newH -= dy; newY += dy; break;
        }

        // Привязывается двигающийся край, а не размер: иначе у элемента,
        // стоящего мимо сетки, край так и остался бы вне узла. Направляющая
        // занимает свою ось первой, сетка получает остальное — та же композиция,
        // что и при перетаскивании.
        // Модификатор читается на момент нажатия — в аргументах resize его нет.
        if (editor != null && editor.CanSnapResizeEdge(editor.LastInputModifiers))
        {
            var modifiers = editor.LastInputModifiers;

            // Равные интервалы считаются по всему прямоугольнику, а не по одной
            // координате: неподвижный край задаёт зазор, с которым сравнивается второй.
            var proposed = new Rect(newX, newY, newW, newH);

            if (_direction is ResizeDirection.Right or ResizeDirection.TopRight or ResizeDirection.BottomRight)
                newW = editor.ResolveResizeEdge(newX + newW, proposed, xAxis: true, farEdge: true, modifiers) - newX;
            else if (_direction is ResizeDirection.Left or ResizeDirection.TopLeft or ResizeDirection.BottomLeft)
                newW = fixedRight - editor.ResolveResizeEdge(fixedRight - newW, proposed, xAxis: true, farEdge: false, modifiers);

            if (_direction is ResizeDirection.Bottom or ResizeDirection.BottomLeft or ResizeDirection.BottomRight)
                newH = editor.ResolveResizeEdge(newY + newH, proposed, xAxis: false, farEdge: true, modifiers) - newY;
            else if (_direction is ResizeDirection.Top or ResizeDirection.TopLeft or ResizeDirection.TopRight)
                newH = fixedBottom - editor.ResolveResizeEdge(fixedBottom - newH, proposed, xAxis: false, farEdge: false, modifiers);
        }

        // Приведение к Min/Max делается здесь, а не только внутри SetDesignSize:
        // неподвижный край считается из итогового размера, иначе он уплывал бы
        // на разницу между запрошенным и разрешённым.
        if (editor != null)
        {
            // Ограничение по форме применяется до Min/Max: минимум обязан
            // побеждать, иначе контрол схлопнулся бы в ноль у края формы.
            if (editor.TryGetContainmentBounds(_target, out var limit))
            {
                if (ChangesWidth(_direction))
                {
                    var maxW = _direction is ResizeDirection.Left or ResizeDirection.TopLeft or ResizeDirection.BottomLeft
                        ? fixedRight - limit.Left
                        : limit.Right - newX;

                    if (maxW > 0)
                        newW = Math.Min(newW, maxW);
                }

                if (ChangesHeight(_direction))
                {
                    var maxH = _direction is ResizeDirection.Top or ResizeDirection.TopLeft or ResizeDirection.TopRight
                        ? fixedBottom - limit.Top
                        : limit.Bottom - newY;

                    if (maxH > 0)
                        newH = Math.Min(newH, maxH);
                }
            }

            // Предел жеста применяется здесь, а не на шве записи: шов проходят и
            // фиксация текущего размера на входе в жест, и отмена, и им предел
            // не адресован. Min/Max самого контрола сильнее — их накладывает
            // CoerceDesignSize следом.
            var coerced = editor.CoerceDesignSize(
                _target,
                new Size(Math.Max(minWidth, newW), Math.Max(minHeight, newH)));

            newW = coerced.Width;
            newH = coerced.Height;
        }
        else
        {
            newW = Math.Max(minWidth, newW);
            newH = Math.Max(minHeight, newH);
        }

        if (_direction is ResizeDirection.Left or ResizeDirection.TopLeft or ResizeDirection.BottomLeft)
            newX = fixedRight - newW;

        if (_direction is ResizeDirection.Top or ResizeDirection.TopLeft or ResizeDirection.TopRight)
            newY = fixedBottom - newH;

        if (editor != null)
            editor.SetDesignSize(_target, new Size(newW, newH));
        else
        {
            Container.Width = newW;
            Container.Height = newH;
        }

        // Обновляем позицию только если она изменилась (при ресайзе слева/сверху)
        if (Math.Abs(newX - _location.X) > 0.1 || Math.Abs(newY - _location.Y) > 0.1)
        {
            if (editor != null)
                editor.SetDesignPosition(_target, new Point(newX, newY));
            else
                Container.Location = new Point(newX, newY);
        }

        // Запоминаем применённое, а не запрошенное: следующая дельта придёт
        // относительно ручки, которая встанет именно сюда.
        _location = new Point(newX, newY);
        _size = new Size(newW, newH);

        // Линии считаются по применённой геометрии, а не по запрошенной: край
        // мог упереться в минимум или в границу формы, и показывать выравнивание,
        // которого не случилось, нельзя.
        editor?.PublishResizeGuides(new Rect(_location, _size));

        Container.RaiseEvent(new ResizeDeltaEventArgs(e.Delta, _direction, DesignEditorItem.ResizeDeltaEvent));
    }

    /// <summary>
    /// Определяет, меняет ли направление ширину.
    /// </summary>
    /// <remarks>
    /// Ограничение применяется только к той оси, которую тянут: иначе оно задним
    /// числом ужимало бы уже вылезший контрол при перетаскивании соседнего края.
    /// </remarks>
    private static bool ChangesWidth(ResizeDirection direction)
        => direction is ResizeDirection.Left or ResizeDirection.Right
            or ResizeDirection.TopLeft or ResizeDirection.TopRight
            or ResizeDirection.BottomLeft or ResizeDirection.BottomRight;

    /// <summary>
    /// Определяет, меняет ли направление высоту.
    /// </summary>
    private static bool ChangesHeight(ResizeDirection direction)
        => direction is ResizeDirection.Top or ResizeDirection.Bottom
            or ResizeDirection.TopLeft or ResizeDirection.TopRight
            or ResizeDirection.BottomLeft or ResizeDirection.BottomRight;
}
