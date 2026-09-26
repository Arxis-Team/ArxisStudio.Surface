using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ArxisStudio.Surface.Editing;

/// <summary>
/// Миникарта поверхности: весь холст разом и рамка видимой области (ADR 0005).
/// </summary>
/// <remarks>
/// Хост ставит её рядом с редактором или поверх него, как <see cref="DesignRuler"/>, и задаёт
/// <see cref="Editor"/> — остальное миникарта берёт у него сама: контейнеры верхнего уровня,
/// видимую область, масштаб. Работает с любой поверхностью — дизайнером форм, редактором узлов,
/// голым <see cref="SurfaceView"/>; что нарисовать сверх контейнеров, слой выше отдаёт через
/// internal-шов <see cref="IMinimapLayer"/>.
/// <para>
/// Показывает объединение занятого и видимого, с полем по краям, в одном масштабе по обеим осям.
/// Щелчок ставит туда центр холста; протяжка рамки ведёт холст, не дёргая его на нажатии. Пока
/// тянут, масштаб карты стоит: рамка — часть того, что карта показывает, и иначе карта ехала бы
/// под указателем.
/// </para>
/// </remarks>
public class SurfaceMinimap : Control
{
    /// <summary>
    /// Идентификатор свойства редактора.
    /// </summary>
    public static readonly StyledProperty<SurfaceView?> EditorProperty =
        AvaloniaProperty.Register<SurfaceMinimap, SurfaceView?>(nameof(Editor));

    /// <summary>
    /// Идентификатор свойства кисти фона.
    /// </summary>
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<SurfaceMinimap, IBrush?>(nameof(Background));

    /// <summary>
    /// Идентификатор свойства кисти контейнеров.
    /// </summary>
    public static readonly StyledProperty<IBrush?> ItemFillProperty =
        AvaloniaProperty.Register<SurfaceMinimap, IBrush?>(nameof(ItemFill));

    /// <summary>
    /// Идентификатор свойства кисти рамки видимой области.
    /// </summary>
    public static readonly StyledProperty<IBrush?> ViewportStrokeProperty =
        AvaloniaProperty.Register<SurfaceMinimap, IBrush?>(nameof(ViewportStroke));

    /// <summary>
    /// Идентификатор свойства заливки видимой области.
    /// </summary>
    public static readonly StyledProperty<IBrush?> ViewportFillProperty =
        AvaloniaProperty.Register<SurfaceMinimap, IBrush?>(nameof(ViewportFill));

    /// <summary>
    /// Поле вокруг показанного — доля его большей стороны.
    /// </summary>
    private const double Border = 0.05;

    private IDisposable? _subscription;
    private SurfaceView? _listening;
    private Rect _frame;
    private double _scale = 1;
    private Point _offset;
    private bool _dragging;
    private Vector _grab;

    static SurfaceMinimap()
    {
        AffectsRender<SurfaceMinimap>(BackgroundProperty, ItemFillProperty, ViewportStrokeProperty, ViewportFillProperty);
        ClipToBoundsProperty.OverrideDefaultValue<SurfaceMinimap>(true);
    }

    /// <summary>
    /// Получает или задает редактор, чей холст показывает миникарта.
    /// </summary>
    public SurfaceView? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    /// <summary>
    /// Получает или задает кисть фона.
    /// </summary>
    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>
    /// Получает или задает кисть контейнеров.
    /// </summary>
    public IBrush? ItemFill
    {
        get => GetValue(ItemFillProperty);
        set => SetValue(ItemFillProperty, value);
    }

    /// <summary>
    /// Получает или задает кисть рамки видимой области.
    /// </summary>
    public IBrush? ViewportStroke
    {
        get => GetValue(ViewportStrokeProperty);
        set => SetValue(ViewportStrokeProperty, value);
    }

    /// <summary>
    /// Получает или задает заливку видимой области.
    /// </summary>
    public IBrush? ViewportFill
    {
        get => GetValue(ViewportFillProperty);
        set => SetValue(ViewportFillProperty, value);
    }

    /// <summary>
    /// Сколько раз миникарта рисовала себя — для тестов перерисовки.
    /// </summary>
    internal int RenderCount { get; private set; }

    /// <summary>
    /// Рамка видимой области в координатах миникарты.
    /// </summary>
    internal Rect ViewportFrame { get; private set; }

    /// <summary>
    /// Переводит мировую точку в точку миникарты.
    /// </summary>
    internal Point WorldToMinimap(Point world) => _offset + ((world - _frame.TopLeft) * _scale);

    /// <summary>
    /// Переводит точку миникарты в мировую.
    /// </summary>
    internal Point MinimapToWorld(Point point) => _frame.TopLeft + ((point - _offset) / _scale);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        RenderCount++;

        var bounds = new Rect(Bounds.Size);
        if (Background is { } background)
            context.DrawRectangle(background, null, bounds);

        if (Editor is not { } editor || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        if (!_dragging)
            UpdateMapping(editor);

        if (ItemFill is { } fill)
        {
            for (var i = 0; i < editor.ItemCount; i++)
            {
                if (editor.ContainerFromIndex(i) is SurfaceItem { IsVisible: true } item)
                    context.DrawRectangle(fill, null, Map(new Rect(item.Location, item.Bounds.Size)));
            }
        }

        editor.GetService<IMinimapLayer>()?.Render(context, WorldToMinimapMatrix(), this);

        ViewportFrame = Map(VisibleWorld(editor));
        var pen = ViewportStroke is { } stroke ? new Pen(stroke) : null;
        context.DrawRectangle(ViewportFill, pen, ViewportFrame);
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (Editor is not { } editor || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        UpdateMapping(editor);
        var point = e.GetPosition(this);
        var world = MinimapToWorld(point);
        var visible = VisibleWorld(editor);

        // Взяли рамку — ведём её за то место, за которое взяли; щелчок мимо — ставим туда центр.
        _grab = Map(visible).Contains(point) ? world - visible.Center : default;
        if (_grab == default)
            editor.CenterOn(world);

        _dragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_dragging && Editor is { } editor)
            editor.CenterOn(MinimapToWorld(e.GetPosition(this)) - _grab);
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!_dragging)
            return;

        _dragging = false;
        if (ReferenceEquals(e.Pointer.Captured, this))
            e.Pointer.Capture(null);

        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = false;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Listen(Editor);
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // Редактор живёт дольше миникарты, и подписка на него держала бы её в памяти.
        Listen(null);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == EditorProperty && this.IsAttachedToVisualTree())
            Listen(Editor);
    }

    /// <summary>
    /// Слушает редактор: смену видимой области — сразу, сдвиг и появление контейнеров — после
    /// прохода раскладки.
    /// </summary>
    private void Listen(SurfaceView? editor)
    {
        _subscription?.Dispose();
        _subscription = null;

        if (_listening != null)
            _listening.LayoutUpdated -= OnEditorLayoutUpdated;

        _listening = editor;
        if (editor != null)
        {
            editor.LayoutUpdated += OnEditorLayoutUpdated;
            _subscription = new Subscriptions(
                editor.GetObservable(SurfaceView.ViewportLocationProperty).Subscribe(new Invalidator<Point>(this)),
                editor.GetObservable(SurfaceView.ViewportZoomProperty).Subscribe(new Invalidator<double>(this)));
        }

        InvalidateVisual();
    }

    private void OnEditorLayoutUpdated(object? sender, EventArgs e) => InvalidateVisual();

    private void UpdateMapping(SurfaceView editor)
    {
        var visible = VisibleWorld(editor);
        var extent = editor.ItemsExtent;
        var shown = extent.Width > 0 || extent.Height > 0 ? extent.Union(visible) : visible;
        shown = shown.Inflate(Math.Max(shown.Width, shown.Height) * Border);

        var size = Bounds.Size;
        _frame = shown;
        _scale = shown.Width <= 0 || shown.Height <= 0 ? 1 : Math.Min(size.Width / shown.Width, size.Height / shown.Height);
        _offset = new Point((size.Width - (shown.Width * _scale)) / 2, (size.Height - (shown.Height * _scale)) / 2);
    }

    private static Rect VisibleWorld(SurfaceView editor)
    {
        var zoom = Math.Max(editor.ViewportZoom, 0.0001);
        return new Rect(editor.ViewportLocation, editor.Bounds.Size / zoom);
    }

    private Rect Map(Rect world) => new(WorldToMinimap(world.TopLeft), world.Size * _scale);

    private Matrix WorldToMinimapMatrix() =>
        Matrix.CreateTranslation(-_frame.X, -_frame.Y)
        * Matrix.CreateScale(_scale, _scale)
        * Matrix.CreateTranslation(_offset.X, _offset.Y);

    private sealed class Invalidator<T>(SurfaceMinimap minimap) : IObserver<T>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(T value) => minimap.InvalidateVisual();
    }

    private sealed class Subscriptions(params IDisposable[] items) : IDisposable
    {
        public void Dispose()
        {
            foreach (var item in items)
                item.Dispose();
        }
    }
}
