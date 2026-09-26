using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ResizePolicyMode = ArxisStudio.Surface.ResizePolicy;
using MovePolicyMode = ArxisStudio.Surface.MovePolicy;
using ArxisStudio.Surface;

namespace ArxisStudio.Surface.Editing;


/// <summary>
/// Определяет роль адорнера в overlay-системе редактора.
/// </summary>
public enum SelectionAdornerRole
{
    /// <summary>
    /// Основной adorner активного target.
    /// </summary>
    Primary,

    /// <summary>
    /// Вторичный adorner дополнительного выбранного target.
    /// </summary>
    Secondary,

    /// <summary>
    /// Групповой adorner, описывающий границы selection bounds.
    /// </summary>
    Group
}


/// <summary>
/// Представляет editor adorner для отрисовки границ выделения и опциональных resize handles.
/// </summary>
[PseudoClasses(":locked", ":primary", ":secondary", ":group")]
[TemplatePart("PART_TopLeft", typeof(Thumb))]
[TemplatePart("PART_Top", typeof(Thumb))]
[TemplatePart("PART_TopRight", typeof(Thumb))]
[TemplatePart("PART_Right", typeof(Thumb))]
[TemplatePart("PART_BottomRight", typeof(Thumb))]
[TemplatePart("PART_Bottom", typeof(Thumb))]
[TemplatePart("PART_BottomLeft", typeof(Thumb))]
[TemplatePart("PART_Left", typeof(Thumb))]
public class SelectionAdorner : TemplatedControl
{
    private readonly Dictionary<Thumb, ResizeDirection> _thumbDirections = new();

    /// <summary>
    /// Идентификатор свойства кисти рамки и ручек.
    /// </summary>
    public static readonly StyledProperty<IBrush> AdornerBrushProperty =
        AvaloniaProperty.Register<SelectionAdorner, IBrush>(nameof(AdornerBrush), Brushes.DodgerBlue);

    /// <summary>
    /// Идентификатор свойства размера ручек.
    /// </summary>
    public static readonly StyledProperty<double> HandleSizeProperty =
        AvaloniaProperty.Register<SelectionAdorner, double>(nameof(HandleSize), 8.0);

    /// <summary>
    /// Идентификатор свойства видимости resize handles.
    /// </summary>
    public static readonly StyledProperty<bool> ShowHandlesProperty =
        AvaloniaProperty.Register<SelectionAdorner, bool>(nameof(ShowHandles), true);

    /// <summary>
    /// Идентификатор свойства интерактивности адорнера.
    /// </summary>
    public static readonly StyledProperty<bool> IsInteractiveProperty =
        AvaloniaProperty.Register<SelectionAdorner, bool>(nameof(IsInteractive), true);

    /// <summary>
    /// Идентификатор политики resize.
    /// </summary>
    public static readonly StyledProperty<ResizePolicyMode> ResizePolicyProperty =
        AvaloniaProperty.Register<SelectionAdorner, ResizePolicyMode>(nameof(ResizePolicy), ResizePolicyMode.All);

    /// <summary>
    /// Идентификатор политики перемещения.
    /// </summary>
    public static readonly StyledProperty<MovePolicyMode> MovePolicyProperty =
        AvaloniaProperty.Register<SelectionAdorner, MovePolicyMode>(nameof(MovePolicy), MovePolicyMode.Both);

    /// <summary>
    /// Идентификатор свойства заливки рамки.
    /// </summary>
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<SelectionAdorner, IBrush?>(nameof(Fill), Brushes.Transparent);

    /// <summary>
    /// Идентификатор толщины рамки.
    /// </summary>
    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<SelectionAdorner, double>(nameof(StrokeThickness), 1.0);

    /// <summary>
    /// Идентификатор штрихового паттерна рамки.
    /// </summary>
    public static readonly StyledProperty<AvaloniaList<double>?> StrokeDashArrayProperty =
        AvaloniaProperty.Register<SelectionAdorner, AvaloniaList<double>?>(nameof(StrokeDashArray));

    /// <summary>
    /// Идентификатор роли адорнера.
    /// </summary>
    public static readonly StyledProperty<SelectionAdornerRole> RoleProperty =
        AvaloniaProperty.Register<SelectionAdorner, SelectionAdornerRole>(nameof(Role), SelectionAdornerRole.Primary);

    /// <summary>
    /// Идентификатор routed event изменения размера.
    /// </summary>
    public static readonly RoutedEvent<ResizeDeltaEventArgs> ResizeDeltaEvent =
        RoutedEvent.Register<ResizeDeltaEventArgs>(nameof(ResizeDelta), RoutingStrategies.Bubble, typeof(SelectionAdorner));

    /// <summary>
    /// Идентификатор routed event начала изменения размера.
    /// </summary>
    public static readonly RoutedEvent<ResizeStartedEventArgs> ResizeStartedEvent =
        RoutedEvent.Register<ResizeStartedEventArgs>(nameof(ResizeStarted), RoutingStrategies.Bubble, typeof(SelectionAdorner));

    /// <summary>
    /// Идентификатор routed event завершения изменения размера.
    /// </summary>
    public static readonly RoutedEvent<VectorEventArgs> ResizeCompletedEvent =
        RoutedEvent.Register<VectorEventArgs>(nameof(ResizeCompleted), RoutingStrategies.Bubble, typeof(SelectionAdorner));

    static SelectionAdorner()
    {
        ShowHandlesProperty.Changed.AddClassHandler<SelectionAdorner>((adorner, _) => adorner.RefreshVisualState());
        IsInteractiveProperty.Changed.AddClassHandler<SelectionAdorner>((adorner, _) => adorner.RefreshVisualState());
        ResizePolicyProperty.Changed.AddClassHandler<SelectionAdorner>((adorner, _) => adorner.RefreshVisualState());
        MovePolicyProperty.Changed.AddClassHandler<SelectionAdorner>((adorner, _) => adorner.RefreshVisualState());
        RoleProperty.Changed.AddClassHandler<SelectionAdorner>((adorner, _) => adorner.RefreshVisualState());
    }

    /// <summary>
    /// Кисть для отрисовки рамки и границ ручек.
    /// </summary>
    public IBrush AdornerBrush
    {
        get => GetValue(AdornerBrushProperty);
        set => SetValue(AdornerBrushProperty, value);
    }

    /// <summary>
    /// Размер ручек в пикселях.
    /// </summary>
    public double HandleSize
    {
        get => GetValue(HandleSizeProperty);
        set => SetValue(HandleSizeProperty, value);
    }

    /// <summary>
    /// Получает или задает признак видимости resize handles.
    /// </summary>
    public bool ShowHandles
    {
        get => GetValue(ShowHandlesProperty);
        set => SetValue(ShowHandlesProperty, value);
    }

    /// <summary>
    /// Получает или задает признак интерактивности адорнера.
    /// </summary>
    public bool IsInteractive
    {
        get => GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    /// <summary>
    /// Получает или задает policy изменения размера target.
    /// </summary>
    public ResizePolicyMode ResizePolicy
    {
        get => GetValue(ResizePolicyProperty);
        set => SetValue(ResizePolicyProperty, value);
    }

    /// <summary>
    /// Получает или задает policy перемещения target.
    /// </summary>
    public MovePolicyMode MovePolicy
    {
        get => GetValue(MovePolicyProperty);
        set => SetValue(MovePolicyProperty, value);
    }

    /// <summary>
    /// Получает или задает заливку рамки адорнера.
    /// </summary>
    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>
    /// Получает или задает толщину рамки адорнера.
    /// </summary>
    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>
    /// Получает или задает штриховой паттерн рамки адорнера.
    /// </summary>
    public AvaloniaList<double>? StrokeDashArray
    {
        get => GetValue(StrokeDashArrayProperty);
        set => SetValue(StrokeDashArrayProperty, value);
    }

    /// <summary>
    /// Получает или задает роль адорнера в overlay-системе редактора.
    /// </summary>
    public SelectionAdornerRole Role
    {
        get => GetValue(RoleProperty);
        set => SetValue(RoleProperty, value);
    }

    /// <summary>
    /// Возникает при изменении размера через одну из ручек.
    /// </summary>
    public event EventHandler<ResizeDeltaEventArgs> ResizeDelta
    {
        add => AddHandler(ResizeDeltaEvent, value);
        remove => RemoveHandler(ResizeDeltaEvent, value);
    }

    /// <summary>
    /// Возникает в момент начала изменения размера.
    /// </summary>
    public event EventHandler<ResizeStartedEventArgs> ResizeStarted
    {
        add => AddHandler(ResizeStartedEvent, value);
        remove => RemoveHandler(ResizeStartedEvent, value);
    }

    /// <summary>
    /// Возникает после завершения изменения размера.
    /// </summary>
    public event EventHandler<VectorEventArgs> ResizeCompleted
    {
        add => AddHandler(ResizeCompletedEvent, value);
        remove => RemoveHandler(ResizeCompletedEvent, value);
    }

    /// <summary>
    /// Применяет шаблон контрола и связывает найденные resize handles с их направлениями.
    /// </summary>
    /// <param name="e">Аргументы применения шаблона.</param>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        UnbindThumbs();
        BindThumb(e, "PART_TopLeft", ResizeDirection.TopLeft);
        BindThumb(e, "PART_Top", ResizeDirection.Top);
        BindThumb(e, "PART_TopRight", ResizeDirection.TopRight);
        BindThumb(e, "PART_Right", ResizeDirection.Right);
        BindThumb(e, "PART_BottomRight", ResizeDirection.BottomRight);
        BindThumb(e, "PART_Bottom", ResizeDirection.Bottom);
        BindThumb(e, "PART_BottomLeft", ResizeDirection.BottomLeft);
        BindThumb(e, "PART_Left", ResizeDirection.Left);
        RefreshVisualState();
    }

    private void RefreshVisualState()
    {
        var isLocked = ResizePolicy == ResizePolicyMode.None && MovePolicy == MovePolicyMode.None;
        PseudoClasses.Set(":locked", isLocked);
        PseudoClasses.Set(":primary", Role == SelectionAdornerRole.Primary);
        PseudoClasses.Set(":secondary", Role == SelectionAdornerRole.Secondary);
        PseudoClasses.Set(":group", Role == SelectionAdornerRole.Group);
        UpdateThumbBindings();
    }

    private void BindThumb(TemplateAppliedEventArgs e, string name, ResizeDirection direction)
    {
        if (e.NameScope.Find(name) is Thumb thumb)
            _thumbDirections[thumb] = direction;
    }

    private void UpdateThumbBindings()
    {
        var isLocked = ResizePolicy == ResizePolicyMode.None && MovePolicy == MovePolicyMode.None;
        var shouldBind = ShowHandles && IsInteractive && !isLocked && ResizePolicy != ResizePolicyMode.None;

        foreach (var pair in _thumbDirections)
        {
            var thumb = pair.Key;
            var direction = pair.Value;
            var isDirectionAllowed = IsDirectionAllowed(direction);
            var shouldShowThumb = ShowHandles && (isLocked || isDirectionAllowed);

            thumb.DragDelta -= OnThumbDragDelta;
            thumb.DragStarted -= OnThumbDragStarted;
            thumb.DragCompleted -= OnThumbDragCompleted;
            thumb.IsVisible = shouldShowThumb;
            thumb.IsHitTestVisible = shouldBind && isDirectionAllowed;
            thumb.Classes.Set("locked", isLocked);

            if (!shouldBind || !isDirectionAllowed)
                continue;

            thumb.DragDelta += OnThumbDragDelta;
            thumb.DragStarted += OnThumbDragStarted;
            thumb.DragCompleted += OnThumbDragCompleted;
        }
    }

    private void UnbindThumbs()
    {
        foreach (var thumb in _thumbDirections.Keys)
        {
            thumb.DragDelta -= OnThumbDragDelta;
            thumb.DragStarted -= OnThumbDragStarted;
            thumb.DragCompleted -= OnThumbDragCompleted;
        }

        _thumbDirections.Clear();
    }

    private void OnThumbDragDelta(object? sender, VectorEventArgs args)
    {
        if (sender is Thumb thumb &&
            _thumbDirections.TryGetValue(thumb, out var direction) &&
            IsDirectionAllowed(direction))
        {
            RaiseEvent(new ResizeDeltaEventArgs(args.Vector, direction, ResizeDeltaEvent));
        }
    }

    private void OnThumbDragStarted(object? sender, VectorEventArgs args)
    {
        if (sender is Thumb thumb &&
            _thumbDirections.TryGetValue(thumb, out var direction) &&
            IsDirectionAllowed(direction))
        {
            RaiseEvent(new ResizeStartedEventArgs(args.Vector, direction, ResizeStartedEvent));
        }
    }

    private void OnThumbDragCompleted(object? sender, VectorEventArgs args)
    {
        RaiseEvent(new VectorEventArgs
        {
            RoutedEvent = ResizeCompletedEvent,
            Vector = args.Vector
        });
    }

    private bool IsDirectionAllowed(ResizeDirection direction)
    {
        return direction switch
        {
            ResizeDirection.Left => ResizePolicy.HasFlag(ResizePolicyMode.Left),
            ResizeDirection.Top => ResizePolicy.HasFlag(ResizePolicyMode.Top),
            ResizeDirection.Right => ResizePolicy.HasFlag(ResizePolicyMode.Right),
            ResizeDirection.Bottom => ResizePolicy.HasFlag(ResizePolicyMode.Bottom),
            ResizeDirection.TopLeft => ResizePolicy.HasFlag(ResizePolicyMode.Top) && ResizePolicy.HasFlag(ResizePolicyMode.Left),
            ResizeDirection.TopRight => ResizePolicy.HasFlag(ResizePolicyMode.Top) && ResizePolicy.HasFlag(ResizePolicyMode.Right),
            ResizeDirection.BottomLeft => ResizePolicy.HasFlag(ResizePolicyMode.Bottom) && ResizePolicy.HasFlag(ResizePolicyMode.Left),
            ResizeDirection.BottomRight => ResizePolicy.HasFlag(ResizePolicyMode.Bottom) && ResizePolicy.HasFlag(ResizePolicyMode.Right),
            _ => false
        };
    }
}
