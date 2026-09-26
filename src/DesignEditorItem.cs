using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Mixins;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using ArxisStudio.States;
using ArxisStudio.Controls;
using ArxisStudio.Surface;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio;

/// <summary>
/// Представляет контейнер элемента редактора с поддержкой выделения,
/// перетаскивания и изменения размеров.
/// </summary>
/// <remarks>
/// Обычно экземпляры создаются автоматически <see cref="DesignEditor"/>
/// как контейнеры для элементов <see cref="ItemsControl.ItemsSource"/>.
/// </remarks>
/// <example>
/// <code language="xml"><![CDATA[
/// <Style Selector="design|DesignEditorItem">
///     <Setter Property="Location" Value="{Binding Location, Mode=TwoWay}" />
///     <Setter Property="Width" Value="{Binding Width, Mode=TwoWay}" />
///     <Setter Property="Height" Value="{Binding Height, Mode=TwoWay}" />
/// </Style>
/// ]]></code>
/// </example>
[TemplatePart("PART_Border", typeof(Border))]
[PseudoClasses(":selected", ":dragging", ":resizing")]
public class DesignEditorItem : ContentControl, ISelectable
{
    #region Fields
    private readonly Stack<DesignEditorItemState> _states = new();
    private bool _isUpdatingLocation;
    #endregion

    #region Standard Properties

    /// <summary>
    /// Идентификатор свойства выделения элемента.
    /// </summary>
    public static readonly StyledProperty<bool> IsSelectedProperty =
        SelectingItemsControl.IsSelectedProperty.AddOwner<DesignEditorItem>();

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
        AvaloniaProperty.Register<DesignEditorItem, Point>(nameof(Location));

    /// <summary>
    /// Получает или задает позицию элемента на холсте в локальных координатах родительской панели.
    /// </summary>
    /// <remarks>
    /// Свойство синхронизируется с attached-свойствами <c>Layout.X</c> и <c>Layout.Y</c>.
    /// Обычно именно его удобнее привязывать к ViewModel.
    /// </remarks>
    public Point Location
    {
        get => GetValue(LocationProperty);
        set => SetValue(LocationProperty, value);
    }

    /// <summary>
    /// Идентификатор свойства режима содержимого.
    /// </summary>
    public static readonly StyledProperty<DesignContentMode> ContentModeProperty =
        AvaloniaProperty.Register<DesignEditorItem, DesignContentMode>(
            nameof(ContentMode), DesignContentMode.Annotated);

    /// <summary>
    /// Получает или задает способ поиска редактируемых элементов внутри контейнера.
    /// </summary>
    /// <remarks>
    /// По умолчанию <see cref="DesignContentMode.Annotated"/> — совместимо с шаблонами,
    /// размеченными вручную. Для формы, загруженной из <c>.axaml</c>, задайте
    /// <see cref="DesignContentMode.Loaded"/>: контейнер сам погасит ввод и откроет
    /// на редактирование всё дерево разметки.
    /// </remarks>
    public DesignContentMode ContentMode
    {
        get => GetValue(ContentModeProperty);
        set => SetValue(ContentModeProperty, value);
    }

    /// <summary>
    /// Идентификатор свойства, определяющего возможность перетаскивания элемента.
    /// </summary>
    public static readonly StyledProperty<bool> IsDraggableProperty =
        AvaloniaProperty.Register<DesignEditorItem, bool>(nameof(IsDraggable), true);

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
        RoutedEvent.Register<DragStartedEventArgs>(nameof(DragStarted), RoutingStrategies.Bubble, typeof(DesignEditorItem));

    /// <summary>
    /// Идентификатор routed event изменения позиции во время перетаскивания.
    /// </summary>
    public static readonly RoutedEvent<DragDeltaEventArgs> DragDeltaEvent =
        RoutedEvent.Register<DragDeltaEventArgs>(nameof(DragDelta), RoutingStrategies.Bubble, typeof(DesignEditorItem));

    /// <summary>
    /// Идентификатор routed event завершения перетаскивания.
    /// </summary>
    public static readonly RoutedEvent<DragCompletedEventArgs> DragCompletedEvent =
        RoutedEvent.Register<DragCompletedEventArgs>(nameof(DragCompleted), RoutingStrategies.Bubble, typeof(DesignEditorItem));

    /// <summary>
    /// Идентификатор routed event изменения размера.
    /// </summary>
    public static readonly RoutedEvent<ResizeDeltaEventArgs> ResizeDeltaEvent =
        RoutedEvent.Register<ResizeDeltaEventArgs>(nameof(ResizeDelta), RoutingStrategies.Bubble, typeof(DesignEditorItem));

    /// <summary>
    /// Идентификатор routed event начала изменения размера.
    /// </summary>
    public static readonly RoutedEvent<VectorEventArgs> ResizeStartedEvent =
        RoutedEvent.Register<VectorEventArgs>(nameof(ResizeStarted), RoutingStrategies.Bubble, typeof(DesignEditorItem));

    /// <summary>
    /// Идентификатор routed event завершения изменения размера.
    /// </summary>
    public static readonly RoutedEvent<VectorEventArgs> ResizeCompletedEvent =
        RoutedEvent.Register<VectorEventArgs>(nameof(ResizeCompleted), RoutingStrategies.Bubble, typeof(DesignEditorItem));

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

    /// <summary>
    /// Получает текущее состояние контейнера.
    /// </summary>
    internal DesignEditorItemState CurrentState => _states.Count > 0 ? _states.Peek() : null!;

    static DesignEditorItem()
    {
        SelectableMixin.Attach<DesignEditorItem>(IsSelectedProperty);
        FocusableProperty.OverrideDefaultValue<DesignEditorItem>(true);
        Layout.XProperty.Changed.AddClassHandler<DesignEditorItem>((item, _) => item.SyncLocationFromLayout());
        Layout.YProperty.Changed.AddClassHandler<DesignEditorItem>((item, _) => item.SyncLocationFromLayout());
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignEditorItem"/>.
    /// </summary>
    public DesignEditorItem()
    {
        _states.Push(new ItemIdleState(this));
    }

    /// <summary>
    /// Реагирует на изменение свойств контейнера и синхронизирует editor-specific state.
    /// </summary>
    /// <param name="change">Аргументы изменения свойства.</param>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsSelectedProperty)
        {
            UpdatePseudoClasses();
        }
        else if (change.Property == LocationProperty)
        {
            if (_isUpdatingLocation)
                return;

            try
            {
                _isUpdatingLocation = true;
                Layout.SetX(this, Location.X);
                Layout.SetY(this, Location.Y);
            }
            finally
            {
                _isUpdatingLocation = false;
            }
        }
    }

    private void UpdatePseudoClasses() => PseudoClasses.Set(":selected", IsSelected);

    private void SyncLocationFromLayout()
    {
        if (_isUpdatingLocation)
            return;

        var x = Layout.GetX(this);
        var y = Layout.GetY(this);

        if (double.IsNaN(x) && double.IsNaN(y))
            return;

        var nextLocation = new Point(
            double.IsNaN(x) ? Location.X : x,
            double.IsNaN(y) ? Location.Y : y);

        if (Math.Abs(nextLocation.X - Location.X) < 0.01 &&
            Math.Abs(nextLocation.Y - Location.Y) < 0.01)
            return;

        try
        {
            _isUpdatingLocation = true;
            SetCurrentValue(LocationProperty, nextLocation);
        }
        finally
        {
            _isUpdatingLocation = false;
        }
    }

    #region State Machine Management

    /// <summary>
    /// Помещает новое состояние контейнера в стек и делает его активным.
    /// </summary>
    /// <param name="state">Новое состояние.</param>
    internal void PushState(DesignEditorItemState state)
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

    private void UpdatePseudoClassesState(DesignEditorItemState state)
    {
        PseudoClasses.Set(":dragging", state is ItemDraggingState);
        PseudoClasses.Set(":resizing", state is ItemResizingState);
    }

    #endregion

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

    internal void OnResizeStarted(Vector vector) => RaiseEvent(new VectorEventArgs { RoutedEvent = ResizeStartedEvent, Vector = vector });
    internal void OnResizeDelta(ResizeDeltaEventArgs e) => RaiseEvent(e);
    internal void OnResizeCompleted(Vector vector) => RaiseEvent(new VectorEventArgs { RoutedEvent = ResizeCompletedEvent, Vector = vector });
}
