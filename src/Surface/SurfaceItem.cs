using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Mixins;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using ArxisStudio.Surface.States;

namespace ArxisStudio.Surface;

/// <summary>
/// Контейнер элемента на <see cref="SurfaceView"/>: обёртка, которая знает положение
/// своего содержимого и умеет сообщать о перетаскивании и изменении размера, но не
/// знает, что именно содержит.
/// </summary>
/// <remarks>
/// Что лежит внутри — форма, узел графа или фигура — решают слои выше (ADR 0003).
/// Дизайнер форм наследует контейнер и добавляет к нему режим содержимого и связь
/// <see cref="Location"/> с attached-свойствами раскладки.
/// </remarks>
[PseudoClasses(":selected", ":dragging", ":resizing")]
public class SurfaceItem : ContentControl, ISelectable
{
    #region Standard Properties

    /// <summary>
    /// Идентификатор свойства выделения элемента.
    /// </summary>
    public static readonly StyledProperty<bool> IsSelectedProperty =
        SelectingItemsControl.IsSelectedProperty.AddOwner<SurfaceItem>();

    /// <summary>
    /// Получает или задает признак выделения элемента.
    /// </summary>
    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>
    /// Идентификатор свойства позиции элемента на холсте.
    /// </summary>
    public static readonly StyledProperty<Point> LocationProperty =
        AvaloniaProperty.Register<SurfaceItem, Point>(nameof(Location));

    /// <summary>
    /// Получает или задает позицию элемента на холсте в локальных координатах родительской панели.
    /// </summary>
    /// <remarks>
    /// Обычно именно его удобнее привязывать к ViewModel.
    /// </remarks>
    public Point Location
    {
        get => GetValue(LocationProperty);
        set => SetValue(LocationProperty, value);
    }

    /// <summary>
    /// Идентификатор свойства, определяющего возможность перетаскивания элемента.
    /// </summary>
    public static readonly StyledProperty<bool> IsDraggableProperty =
        AvaloniaProperty.Register<SurfaceItem, bool>(nameof(IsDraggable), true);

    /// <summary>
    /// Получает или задает признак, разрешающий перетаскивание элемента мышью.
    /// </summary>
    public bool IsDraggable
    {
        get => GetValue(IsDraggableProperty);
        set => SetValue(IsDraggableProperty, value);
    }

    #endregion

    #region Routed Events

    /// <summary>
    /// Идентификатор routed event начала перетаскивания.
    /// </summary>
    public static readonly RoutedEvent<DragStartedEventArgs> DragStartedEvent =
        RoutedEvent.Register<DragStartedEventArgs>(nameof(DragStarted), RoutingStrategies.Bubble, typeof(SurfaceItem));

    /// <summary>
    /// Идентификатор routed event изменения позиции во время перетаскивания.
    /// </summary>
    public static readonly RoutedEvent<DragDeltaEventArgs> DragDeltaEvent =
        RoutedEvent.Register<DragDeltaEventArgs>(nameof(DragDelta), RoutingStrategies.Bubble, typeof(SurfaceItem));

    /// <summary>
    /// Идентификатор routed event завершения перетаскивания.
    /// </summary>
    public static readonly RoutedEvent<DragCompletedEventArgs> DragCompletedEvent =
        RoutedEvent.Register<DragCompletedEventArgs>(nameof(DragCompleted), RoutingStrategies.Bubble, typeof(SurfaceItem));

    /// <summary>
    /// Идентификатор routed event изменения размера.
    /// </summary>
    public static readonly RoutedEvent<ResizeDeltaEventArgs> ResizeDeltaEvent =
        RoutedEvent.Register<ResizeDeltaEventArgs>(nameof(ResizeDelta), RoutingStrategies.Bubble, typeof(SurfaceItem));

    /// <summary>
    /// Идентификатор routed event начала изменения размера.
    /// </summary>
    public static readonly RoutedEvent<VectorEventArgs> ResizeStartedEvent =
        RoutedEvent.Register<VectorEventArgs>(nameof(ResizeStarted), RoutingStrategies.Bubble, typeof(SurfaceItem));

    /// <summary>
    /// Идентификатор routed event завершения изменения размера.
    /// </summary>
    public static readonly RoutedEvent<VectorEventArgs> ResizeCompletedEvent =
        RoutedEvent.Register<VectorEventArgs>(nameof(ResizeCompleted), RoutingStrategies.Bubble, typeof(SurfaceItem));

    /// <summary>
    /// Возникает при начале перетаскивания элемента.
    /// </summary>
    public event EventHandler<DragStartedEventArgs> DragStarted { add => AddHandler(DragStartedEvent, value); remove => RemoveHandler(DragStartedEvent, value); }

    /// <summary>
    /// Возникает при изменении позиции элемента во время перетаскивания.
    /// </summary>
    public event EventHandler<DragDeltaEventArgs> DragDelta { add => AddHandler(DragDeltaEvent, value); remove => RemoveHandler(DragDeltaEvent, value); }

    /// <summary>
    /// Возникает после завершения перетаскивания элемента.
    /// </summary>
    public event EventHandler<DragCompletedEventArgs> DragCompleted { add => AddHandler(DragCompletedEvent, value); remove => RemoveHandler(DragCompletedEvent, value); }

    /// <summary>
    /// Возникает при изменении размеров элемента.
    /// </summary>
    public event EventHandler<ResizeDeltaEventArgs> ResizeDelta { add => AddHandler(ResizeDeltaEvent, value); remove => RemoveHandler(ResizeDeltaEvent, value); }

    /// <summary>
    /// Возникает при начале изменения размеров элемента.
    /// </summary>
    public event EventHandler<VectorEventArgs> ResizeStarted { add => AddHandler(ResizeStartedEvent, value); remove => RemoveHandler(ResizeStartedEvent, value); }

    /// <summary>
    /// Возникает после завершения изменения размеров элемента.
    /// </summary>
    public event EventHandler<VectorEventArgs> ResizeCompleted { add => AddHandler(ResizeCompletedEvent, value); remove => RemoveHandler(ResizeCompletedEvent, value); }

    #endregion

    static SurfaceItem()
    {
        SelectableMixin.Attach<SurfaceItem>(IsSelectedProperty);
        FocusableProperty.OverrideDefaultValue<SurfaceItem>(true);
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SurfaceItem"/>.
    /// </summary>
    public SurfaceItem()
    {
        _states.Push(new ItemIdleState(this));
    }

    /// <summary>
    /// Можно ли тащить контейнер, который не лежит ни в одной поверхности.
    /// </summary>
    /// <remarks>
    /// Без поверхности жест пишет <see cref="Location"/> напрямую, а её читает не всякая
    /// панель. Ядро не знает, какая прочтёт, поэтому отвечает «нельзя»; слой, у которого
    /// своя панель, знает и переопределяет.
    /// </remarks>
    internal virtual bool CanMoveWithoutSurface => false;

    /// <summary>
    /// Реагирует на изменение свойств контейнера.
    /// </summary>
    /// <param name="change">Аргументы изменения свойства.</param>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsSelectedProperty)
            PseudoClasses.Set(":selected", IsSelected);
    }

    internal void OnResizeStarted(Vector vector) => RaiseEvent(new VectorEventArgs { RoutedEvent = ResizeStartedEvent, Vector = vector });
    internal void OnResizeCompleted(Vector vector) => RaiseEvent(new VectorEventArgs { RoutedEvent = ResizeCompletedEvent, Vector = vector });

    private readonly Stack<SurfaceItemState> _states = new();

    /// <summary>
    /// Получает текущее состояние контейнера.
    /// </summary>
    internal SurfaceItemState CurrentState => _states.Count > 0 ? _states.Peek() : null!;

    /// <summary>
    /// Помещает новое состояние контейнера в стек и делает его активным.
    /// </summary>
    /// <param name="state">Новое состояние.</param>
    internal void PushState(SurfaceItemState state)
    {
        var previous = CurrentState;
        _states.Push(state);
        state.Enter(previous);
        UpdatePseudoClassesState(state);
    }

    /// <summary>
    /// Завершает текущее состояние контейнера и возвращается к предыдущему.
    /// </summary>
    internal void PopState()
    {
        if (_states.Count > 1)
        {
            var current = _states.Pop();
            current.Exit();
            CurrentState.ReEnter(current);
            UpdatePseudoClassesState(CurrentState);
        }
    }

    private void UpdatePseudoClassesState(SurfaceItemState state)
    {
        PseudoClasses.Set(":dragging", state.PseudoClass == ":dragging");
        PseudoClasses.Set(":resizing", state.PseudoClass == ":resizing");
    }

    /// <summary>
    /// Передает событие нажатия указателя в текущее состояние контейнера.
    /// </summary>
    /// <param name="e">Аргументы указателя.</param>
    protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); if (!e.Handled) CurrentState.OnPointerPressed(e); }

    /// <summary>
    /// Передает событие перемещения указателя в текущее состояние контейнера.
    /// </summary>
    /// <param name="e">Аргументы указателя.</param>
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); CurrentState.OnPointerMoved(e); }

    /// <summary>
    /// Передает событие отпускания указателя в текущее состояние контейнера.
    /// </summary>
    /// <param name="e">Аргументы указателя.</param>
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); CurrentState.OnPointerReleased(e); }

    /// <summary>
    /// Сбрасывает вложенные состояния, если контейнер теряет захват указателя.
    /// </summary>
    /// <param name="e">Аргументы потери захвата указателя.</param>
    /// <inheritdoc />
    /// <remarks>
    /// Стек разбирается до базового состояния, а базовому о брошенном жесте говорится
    /// отдельно: снять его нечем, а нажатие, за которым не последует отпускания,
    /// иначе осталось бы у него записанным.
    /// </remarks>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        while (_states.Count > 1)
            PopState();

        CurrentState.OnPointerCaptureLost();
    }
}
