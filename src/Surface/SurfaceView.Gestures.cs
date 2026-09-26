using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ArxisStudio.Surface.States;

namespace ArxisStudio.Surface;

// Жесты поверхности: машина состояний редактора и маршрутизация указателя в неё.
// Часть SurfaceView; общее описание типа — в SurfaceView.cs.
public partial class SurfaceView
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

        if (OnContextPointerPressed(e, _lastMousePosition))
        {
            e.Handled = true;
            return;
        }

        CurrentState.OnPointerPressed(e);

        if (!e.Handled) base.OnPointerPressed(e);
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

        // Брошенный жест не должен оставить холст ехать: таймер пережил бы его и
        // пересчитывал бы жест, которого уже нет.
        StopAutoPan();

        while (_states.Count > 1)
            PopState();

        OnGestureAbandoned();
    }

    /// <summary>
    /// Даёт слою выше перехватить нажатие, которое открывает контекст.
    /// </summary>
    /// <returns><see langword="true"/>, если нажатие потреблено и в состояния не идёт.</returns>
    /// <remarks>
    /// Ядро открывает контекст правой кнопкой и больше ничего не делает; слой, у которого
    /// своё правило выделения под курсором, переопределяет.
    /// </remarks>
    private protected virtual bool OnContextPointerPressed(PointerPressedEventArgs e, Point position)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            return false;

        RequestContextSafe(DesignEditorContextSource.Pointer, position, e.KeyModifiers);
        return true;
    }

    /// <summary>
    /// Сообщает слою выше, что жест брошен потерей захвата.
    /// </summary>
    /// <remarks>
    /// Состояния к этому моменту уже вышли своим <c>Exit</c>; здесь закрывается то,
    /// что живёт вне стека, — например групповая операция, начатая с ручки.
    /// </remarks>
    private protected virtual void OnGestureAbandoned()
    {
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

    /// <summary>
    /// Идентификатор свойства, показывающего активен ли marquee-selection.
    /// </summary>
    public static readonly DirectProperty<SurfaceView, bool> IsSelectingProperty =
        AvaloniaProperty.RegisterDirect<SurfaceView, bool>(nameof(IsSelecting), o => o.IsSelecting, (o, v) => o.IsSelecting = v);

    /// <summary>
    /// Идентификатор свойства прямоугольника выделения в мировых координатах.
    /// </summary>
    public static readonly DirectProperty<SurfaceView, Rect> SelectedAreaProperty =
        AvaloniaProperty.RegisterDirect<SurfaceView, Rect>(nameof(SelectedArea), o => o.SelectedArea, (o, v) => o.SelectedArea = v);

    private bool _isSelecting;

    /// <summary>
    /// Получает или задает признак активного прямоугольного выделения.
    /// </summary>
    public bool IsSelecting
    {
        get => _isSelecting;
        set => SetAndRaise(IsSelectingProperty, ref _isSelecting, value);
    }

    private Rect _selectedArea;

    /// <summary>
    /// Получает или задает текущий прямоугольник выделения в мировых координатах.
    /// </summary>
    public Rect SelectedArea
    {
        get => _selectedArea;
        set => SetAndRaise(SelectedAreaProperty, ref _selectedArea, value);
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
        LastPointerScreen = e.GetPosition(this);
        _pointerSample = new PointerSample(
            e.Pointer.Id,
            GetWorldPosition(LastPointerScreen),
            ++_pointerMoveCount);
    }

    /// <summary>
    /// Последнее положение указателя над редактором, в его координатах.
    /// </summary>
    /// <remarks>
    /// Снимается тем же туннельным обработчиком, что и снимок: во время изменения размера
    /// указатель захвачен ручкой, и до редактора движение иначе не доходит.
    /// </remarks>
    internal Point LastPointerScreen { get; private set; }

    /// <summary>
    /// Пересчитывает снимок указателя после того, как холст сдвинулся под ним.
    /// </summary>
    /// <remarks>
    /// Снимок хранит точку на холсте, а указатель стоит на экране: сдвинулся холст — и
    /// под тем же указателем уже другое место. Номер движения не растёт — указатель не
    /// двигался, — поэтому жест, начатый после последнего движения, снимком по-прежнему
    /// не пользуется.
    /// </remarks>
    private void RefreshPointerSample()
    {
        if (_pointerSample is { } sample)
            _pointerSample = sample with { World = GetWorldPosition(LastPointerScreen) };
    }

    // --- Поправка позиции ---
    // Жест считает, куда элемент хочет встать; где он встанет, решает модификатор,
    // которого подключила служба инструментов (ISurfacePositionModifier).

    private protected GroupDragOperation? _groupDragOperation;

    private ISurfacePositionModifier? PositionModifier => GetService<ISurfacePositionModifier>();

    internal void BeginSnapGuides(Control movingTarget) => PositionModifier?.Begin(movingTarget);

    internal void EndSnapGuides() => PositionModifier?.End();

    /// <summary>
    /// Возвращает позицию перетаскиваемого target'а с учётом поправки.
    /// </summary>
    /// <remarks>
    /// Группа ставится рамкой выделения, а не тем элементом, за который её схватили:
    /// так уже устроен групповой resize, и так же выглядит происходящее на экране —
    /// пользователь ведёт рамку, её и надо ставить на место.
    /// <para>
    /// Отображение стоит снаружи поправки, а не внутри неё: правило одно на всю
    /// привязку, и сетка обязана ставить на узел ту же рамку, что и направляющие.
    /// </para>
    /// </remarks>
    internal Point ResolveDragPosition(Control target, Point proposed, KeyModifiers modifiers)
    {
        if (_groupDragOperation is { } group && ReferenceEquals(target, group.SourceTarget))
        {
            var frame = ResolveOrigin(proposed + group.FrameOffset, group.Frame.Size, modifiers);
            return frame - group.FrameOffset;
        }

        return ResolveOrigin(proposed, GetDesignSize(target), modifiers);
    }

    private Point ResolveOrigin(Point proposed, Size size, KeyModifiers modifiers)
        => PositionModifier is { } modifier
            ? modifier.ResolveOrigin(proposed, size, modifiers)
            : new Point(Math.Round(proposed.X), Math.Round(proposed.Y));

    internal bool CanSnapResizeEdge(KeyModifiers modifiers) => PositionModifier?.CanSnapEdge(modifiers) == true;

    internal double ResolveResizeEdge(double edge, Rect proposed, bool xAxis, bool farEdge, KeyModifiers modifiers)
        => PositionModifier is { } modifier ? modifier.ResolveEdge(edge, proposed, xAxis, farEdge, modifiers) : edge;

    internal void PublishResizeGuides(Rect bounds) => PositionModifier?.PublishApplied(bounds);

    /// <summary>
    /// Решает, что делать с протяжкой контейнера.
    /// </summary>
    /// <param name="container">Контейнер, который тянут.</param>
    /// <param name="moveTarget">Target, который будет двигаться.</param>
    /// <remarks>
    /// Правило ядра одно: не предлагать жест, который ничего не делает, — заблокированный
    /// политикой target не перетаскивается. Слой, у которого есть свои причины отказать
    /// или своё состояние жеста, переопределяет.
    /// </remarks>
    internal virtual ItemDragPlan PlanItemDrag(SurfaceItem container, Control moveTarget)
        => GetEffectiveMovePolicy(moveTarget) == MovePolicy.None ? ItemDragPlan.Refuse : ItemDragPlan.Drag;
}
