using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArxisStudio.Surface.Editing;

/// <summary>
/// Миникарта поверхности: весь холст разом и рамка видимой области (ADR 0005).
/// </summary>
/// <remarks>
/// Хост ставит её рядом с редактором или поверх него, как <see cref="SurfaceRuler"/>, и задаёт
/// <see cref="Editor"/> — остальное миникарта берёт у него сама: контейнеры верхнего уровня,
/// видимую область, масштаб. Работает с любой поверхностью — дизайнером интерфейса, редактором узлов,
/// голым <see cref="SurfaceView"/>; что нарисовать сверх контейнеров, слой выше отдаёт через
/// internal-шов <see cref="IMinimapLayer"/>.
/// <para>
/// Показывает объединение занятого и видимого, с полем по краям, в одном масштабе по обеим осям.
/// Щелчок ставит туда центр холста; протяжка рамки ведёт холст, не дёргая его на нажатии. Пока
/// тянут, масштаб карты стоит: рамка — часть того, что карта показывает, и иначе карта ехала бы
/// под указателем.
/// </para>
/// <para>
/// Содержимое — контейнеры и кривые слоёв — собирается в мировых координатах (ADR 0007) и рисуется
/// отдельным визуалом с кэшем в картинку, своей операцией, а не геометрией (ADR 0011): панорама двигает
/// только рамку видимой области, тоже отдельный визуал, и содержимое не записывается заново, пока не
/// сменились оно само или соответствие карты. Пересобирается оно по сигналу поверхности «содержимое
/// сменилось», а не после прохода раскладки окна, и во время непрерывных правок — не чаще раза в
/// <see cref="RebuildInterval"/>; отложенное дособерёт таймер.
/// </para>
/// </remarks>
public class SurfaceMinimap : Control
{
    /// <summary>
    /// Не чаще этого содержимое пересобирается, пока правки идут подряд.
    /// </summary>
    internal static readonly TimeSpan RebuildInterval = TimeSpan.FromMilliseconds(50);

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

    // Содержимое в мировых координатах: контейнеры и кривые слоя выше. Пока оно устарело,
    // пересборка назначена — кадром или таймером, — и новый сигнал её не назначает заново.
    private MinimapSnapshot? _snapshot;
    private IMinimapLayer? _layerSource;
    private readonly MinimapContent _content = new();
    private readonly MinimapFrame _frameView = new();
    private bool _contentStale = true;
    private long? _lastRebuild;
    private DispatcherTimer? _rebuildTimer;

    // Пересчёт карты назначен на ближайший кадр.
    private bool _refreshRequested;

    static SurfaceMinimap()
    {
        AffectsRender<SurfaceMinimap>(BackgroundProperty);
        ClipToBoundsProperty.OverrideDefaultValue<SurfaceMinimap>(true);
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SurfaceMinimap"/>.
    /// </summary>
    public SurfaceMinimap()
    {
        // Содержимое — снизу, рамка — поверх; нажатия принимает сама карта.
        VisualChildren.Add(_content);
        VisualChildren.Add(_frameView);
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
    /// Сколько раз миникарта рисовала себя, своё содержимое или рамку — для тестов перерисовки.
    /// </summary>
    internal int RenderCount => _renders + _content.Renders + _frameView.Renders;

    /// <summary>
    /// Сколько раз содержимое записывалось заново — для тестов: панорама его не трогает.
    /// </summary>
    internal int ContentRenders => _content.Renders;

    private int _renders;

    /// <summary>
    /// Сколько раз миникарта пересобирала содержимое — для тестов и стенда.
    /// </summary>
    internal int ContentRebuilds { get; private set; }

    /// <summary>
    /// Охват контейнеров в последнем собранном содержимом, в мировых координатах.
    /// </summary>
    internal Rect ContentBounds { get; private set; }

    /// <summary>
    /// Ждёт ли отложенная пересборка своего таймера.
    /// </summary>
    internal bool IsRebuildScheduled => _rebuildTimer?.IsEnabled == true;

    /// <summary>
    /// Часы прореживания — метка <see cref="Stopwatch"/>; тест подменяет их, потому что в безголовом
    /// режиме время стоит, а настоящие часы идут.
    /// </summary>
    internal Func<long> Clock { get; set; } = Stopwatch.GetTimestamp;

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
        _renders++;

        // Фон прозрачен, если его не задали: нажатия по карте принимает она сама, всей площадью.
        context.DrawRectangle(Background ?? Brushes.Transparent, null, new Rect(Bounds.Size));
    }

    /// <summary>
    /// Назначает пересчёт карты на ближайший кадр.
    /// </summary>
    /// <remarks>
    /// Кадром анимации, а не отрисовкой: пересчёт отдаёт новое частям карты, и те просят перерисовки, а
    /// внутри прохода отрисовки просить её нельзя. Сколько бы поводов ни пришло до кадра, пересчёт один.
    /// </remarks>
    private void RequestRefresh()
    {
        if (_refreshRequested || TopLevel.GetTopLevel(this) is not { } top)
            return;

        _refreshRequested = true;
        top.RequestAnimationFrame(_ => Refresh());
    }

    /// <summary>
    /// Пересчитывает соответствие, дособирает содержимое, если пора, и отдаёт его частям карты.
    /// </summary>
    private void Refresh()
    {
        _refreshRequested = false;

        if (_listening is not { } editor || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            _content.Show(null, Matrix.Identity, 1, null, null);
            _frameView.Show(default, null, null);
            return;
        }

        if (!_dragging)
            UpdateMapping(editor);

        if (_contentStale && UntilRebuild() <= TimeSpan.Zero)
            RebuildContent(editor);

        // Содержимое записывается заново, только если сменилось оно само или соответствие карты, —
        // панорама двигает одну рамку (ADR 0011).
        _content.Show(_snapshot, WorldToMinimapMatrix(), _scale, ItemFill, _layerSource?.FindStroke(this));
        ViewportFrame = Map(VisibleWorld(editor));
        _frameView.Show(ViewportFrame, ViewportFill, ViewportStroke);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        _content.Measure(availableSize);
        _frameView.Measure(availableSize);
        return default;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var rect = new Rect(finalSize);
        _content.Arrange(rect);
        _frameView.Arrange(rect);
        return finalSize;
    }

    /// <inheritdoc />
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        RequestRefresh();
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

        RequestRefresh();
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = false;
        RequestRefresh();
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

        // Редактор живёт дольше миникарты, и подписка на него держала бы её в памяти, а таймер
        // отложенной пересборки — до своего срабатывания.
        Listen(null);
    }

    /// <summary>
    /// Пересобирает отложенное содержимое сейчас — так срабатывает таймер, и так тест, в котором
    /// время стоит, доводит карту до итога.
    /// </summary>
    internal void FlushContent()
    {
        _rebuildTimer?.Stop();
        if (_contentStale && _listening is { } editor)
            RebuildContent(editor);

        RequestRefresh();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == EditorProperty && this.IsAttachedToVisualTree())
            Listen(Editor);
        else if (change.Property == ItemFillProperty || change.Property == ViewportStrokeProperty
                 || change.Property == ViewportFillProperty)
            RequestRefresh();
    }

    /// <summary>
    /// Слушает редактор: смену видимой области — подпиской на её свойства, смену содержимого —
    /// сигналом поверхности (<see cref="SurfaceView.ContentChanged"/>).
    /// </summary>
    /// <remarks>
    /// Проход раскладки окна карта не слушает: событие раскладки у Avalonia общее на окно, и карта
    /// перерисовывалась бы от раскладки, к редактору отношения не имеющей.
    /// </remarks>
    private void Listen(SurfaceView? editor)
    {
        _subscription?.Dispose();
        _subscription = null;

        if (_listening != null)
            _listening.ContentChanged -= OnEditorContentChanged;

        // Новый редактор — новое содержимое, и собирается оно сразу, без прореживания.
        _listening = editor;
        _rebuildTimer?.Stop();
        _snapshot = null;
        _layerSource = null;
        _contentStale = true;
        _lastRebuild = null;

        if (editor != null)
        {
            editor.ContentChanged += OnEditorContentChanged;
            _subscription = new Subscriptions(
                editor.GetObservable(SurfaceView.ViewportLocationProperty).Subscribe(new Invalidator<Point>(this)),
                editor.GetObservable(SurfaceView.ViewportZoomProperty).Subscribe(new Invalidator<double>(this)));
        }

        RequestRefresh();
    }

    private void OnEditorContentChanged(object? sender, EventArgs e)
    {
        if (_contentStale)
            return;

        // Прошло больше интервала — пересобирается на ближайшем кадре; иначе карта остаётся
        // прежней, пока не сработает таймер: перерисовка без пересборки показала бы то же самое.
        _contentStale = true;
        var wait = UntilRebuild();
        if (wait <= TimeSpan.Zero)
        {
            RequestRefresh();
            return;
        }

        if (_rebuildTimer == null)
        {
            _rebuildTimer = new DispatcherTimer();
            _rebuildTimer.Tick += (_, _) => FlushContent();
        }

        _rebuildTimer.Interval = wait;
        _rebuildTimer.Start();
    }

    /// <summary>
    /// Сколько ещё ждать пересборки; ноль и меньше — пора.
    /// </summary>
    private TimeSpan UntilRebuild() =>
        _lastRebuild is { } last ? RebuildInterval - Stopwatch.GetElapsedTime(last, Clock()) : TimeSpan.Zero;

    /// <summary>
    /// Собирает контейнеры и кривые слоя выше в снимок мировых координат.
    /// </summary>
    private void RebuildContent(SurfaceView editor)
    {
        var items = new List<Rect>();
        var bounds = default(Rect);
        foreach (var rect in editor.EnumerateItemBounds())
        {
            bounds = items.Count > 0 ? bounds.Union(rect) : rect;
            items.Add(rect);
        }

        ContentBounds = bounds;

        var curves = new List<MinimapCurve>();
        _layerSource = editor.GetService<IMinimapLayer>();
        _layerSource?.Build(curves);
        _snapshot = new MinimapSnapshot(items.ToArray(), curves.ToArray());

        _contentStale = false;
        _lastRebuild = Clock();
        ContentRebuilds++;
    }

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

        public void OnNext(T value) => minimap.RequestRefresh();
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
