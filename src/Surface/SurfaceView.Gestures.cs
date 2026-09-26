using System.Collections.Generic;
using Avalonia;
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

        while (_states.Count > 1)
            PopState();

        OnGestureAbandoned();
    }

    /// <summary>
    /// Даёт слою выше перехватить нажатие, которое открывает контекст.
    /// </summary>
    /// <returns><see langword="true"/>, если нажатие потреблено и в состояния не идёт.</returns>
    private protected virtual bool OnContextPointerPressed(PointerPressedEventArgs e, Point position) => false;

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

    // --- Рамка выделения ---
    // Что рамка выбирает, решает слой, который знает содержимое. Ядро ведёт сам жест:
    // захват, прямоугольник в мировых координатах, курсор и отмену.

    /// <summary>
    /// Снимает контейнеры на время жеста рамки.
    /// </summary>
    internal virtual void BeginContainerSnapshot()
    {
    }

    /// <summary>
    /// Отпускает снимок контейнеров.
    /// </summary>
    internal virtual void EndContainerSnapshot()
    {
    }

    /// <summary>
    /// Решает, набирает ли рамка контейнеры целиком.
    /// </summary>
    internal virtual bool ShouldUseContainerMarquee(Point viewportPoint, KeyModifiers modifiers) => true;

    /// <summary>
    /// Пересчитывает область действия рамки по текущему прямоугольнику.
    /// </summary>
    internal virtual void UpdateMarqueeScope(Rect worldBounds, bool useContainerSelection)
    {
    }

    /// <summary>
    /// Сбрасывает область действия рамки.
    /// </summary>
    internal virtual void ClearMarqueeScope()
    {
    }

    /// <summary>
    /// Применяет выделение по прямоугольнику рамки.
    /// </summary>
    internal virtual void CommitSelection(Rect bounds, bool isAdditive, bool useContainerSelection)
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
}
