using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering.SceneGraph;
using SkiaSharp;

namespace ArxisStudio.Surface;

/// <summary>
/// Слой упрощённого вида: карточки свёрнутых элементов, когда масштаб ниже
/// <see cref="SurfaceView.SimplifiedZoom"/> (ADR 0008).
/// </summary>
/// <remarks>
/// Стоит в шаблоне поверхности под элементами, во весь её размер, и переводит мировые координаты в
/// экранные трансформацией viewport сам, как сетка. Слой без размера в холсте под трансформацией
/// рендерер отсекал бы, когда его начало ложится на край окна: живая проверка так потеряла все
/// карточки на 20 % при холсте в начале координат. Прямоугольники слой берёт у виртуализирующей панели
/// своей поверхности — той, чей шаблон его создал, — и собирает их в снимок с сеткой ячеек, который
/// держит, пока свёрнутое не сменится: геометрия элемента без контейнера, коллекция, состав развёрнутых.
/// Панорама и перетаскивание развёрнутого элемента его не пересобирают. Развёрнутые элементы в карточки
/// не входят — рисуют себя сами, поверх слоя. Рамка карточки — в один пиксель экрана при любом масштабе.
/// <para>
/// Рисует слой своей операцией, а не геометрией (ADR 0011): её границы — прямоугольник слоя, и
/// композитору нечего мерить, а геометрию на десять тысяч фигур он мерил бы на каждом кадре панорамы —
/// сотни миллисекунд. Операция берёт из сетки только видимые карточки. Мельче
/// <see cref="MinOutlinedPixels"/> пикселей экрана карточка рисуется без рамки, а полоса — не тоньше
/// пикселя.
/// </para>
/// <para>
/// Панель не рисует сама: <see cref="Panel.Render"/> в Avalonia запечатан.
/// </para>
/// </remarks>
public sealed class SurfaceSimplifiedLayer : Control
{
    /// <summary>
    /// Идентификатор свойства <see cref="Fill"/>.
    /// </summary>
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<SurfaceSimplifiedLayer, IBrush?>(nameof(Fill));

    /// <summary>
    /// Идентификатор свойства <see cref="Stroke"/>.
    /// </summary>
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<SurfaceSimplifiedLayer, IBrush?>(nameof(Stroke));

    /// <summary>
    /// Идентификатор свойства <see cref="AccentHeight"/>.
    /// </summary>
    public static readonly StyledProperty<double> AccentHeightProperty =
        AvaloniaProperty.Register<SurfaceSimplifiedLayer, double>(nameof(AccentHeight), 16);

    /// <summary>
    /// Идентификатор свойства <see cref="SelectedStroke"/>.
    /// </summary>
    public static readonly StyledProperty<IBrush?> SelectedStrokeProperty =
        AvaloniaProperty.Register<SurfaceSimplifiedLayer, IBrush?>(nameof(SelectedStroke));

    /// <summary>
    /// Идентификатор свойства <see cref="SelectedStrokeThickness"/>.
    /// </summary>
    public static readonly StyledProperty<double> SelectedStrokeThicknessProperty =
        AvaloniaProperty.Register<SurfaceSimplifiedLayer, double>(nameof(SelectedStrokeThickness), 2);

    /// <summary>
    /// Мельче этого по меньшей стороне, в пикселях экрана, карточка рисуется без рамки.
    /// </summary>
    internal const double MinOutlinedPixels = 4;

    private SurfaceView? _view;
    private Snapshot? _snapshot;
    private bool _stale = true;

    static SurfaceSimplifiedLayer()
    {
        AffectsRender<SurfaceSimplifiedLayer>(FillProperty, StrokeProperty, SelectedStrokeProperty, SelectedStrokeThicknessProperty);
    }

    /// <summary>
    /// Получает или задает заливку карточки.
    /// </summary>
    /// <remarks>
    /// Ставит её тема: у поверхности ядра — <c>Surface.Simplified.ItemFill</c>, у редактора узлов —
    /// фон карточки узла.
    /// </remarks>
    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>
    /// Получает или задает рамку карточки — в один пиксель экрана.
    /// </summary>
    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>
    /// Получает или задает высоту полосы заголовка в мировых единицах.
    /// </summary>
    /// <remarks>
    /// Цвет полосы даёт <see cref="SurfaceView.ItemAccentBinding"/>. Выше трети карточки полоса не
    /// бывает: у маленького элемента она иначе закрыла бы его целиком.
    /// </remarks>
    public double AccentHeight
    {
        get => GetValue(AccentHeightProperty);
        set => SetValue(AccentHeightProperty, value);
    }

    /// <summary>
    /// Получает или задает рамку выбранной карточки (ADR 0010).
    /// </summary>
    /// <remarks>
    /// Выбранный элемент без контейнера остаётся выбранным, и карточка показывает это рамкой выбора
    /// поверх своей. Ставит её тема: у поверхности ядра — <c>SurfaceItem.SelectionBrush</c>, у редактора
    /// узлов — рамка выбранного узла.
    /// </remarks>
    public IBrush? SelectedStroke
    {
        get => GetValue(SelectedStrokeProperty);
        set => SetValue(SelectedStrokeProperty, value);
    }

    /// <summary>
    /// Получает или задает толщину рамки выбранной карточки в пикселях экрана.
    /// </summary>
    public double SelectedStrokeThickness
    {
        get => GetValue(SelectedStrokeThicknessProperty);
        set => SetValue(SelectedStrokeThicknessProperty, value);
    }

    /// <summary>
    /// Сколько выбранных карточек в последней сборке — для тестов.
    /// </summary>
    internal int SelectedCards { get; private set; }

    /// <summary>
    /// Сколько полос в последней сборке — для тестов.
    /// </summary>
    internal int Bands { get; private set; }

    /// <summary>
    /// Сколько разных кистей у полос последней сборки — для тестов.
    /// </summary>
    internal int AccentGroups => _snapshot?.Palette.Length ?? 0;

    /// <summary>
    /// Сколько карточек в последней сборке — для тестов и стенда.
    /// </summary>
    internal int Cards { get; private set; }

    /// <summary>
    /// Сколько раз карточки собирались заново — для тестов и стенда.
    /// </summary>
    internal int Rebuilds { get; private set; }

    /// <summary>
    /// Сколько раз слой отрисовывался — для тестов.
    /// </summary>
    internal int Renders { get; private set; }

    /// <summary>
    /// Сколько раз карточки нарисованы — для тестов.
    /// </summary>
    internal int Draws { get; private set; }

    /// <summary>
    /// Толщина рамки в мировых единицах на последней отрисовке — для тестов.
    /// </summary>
    internal double StrokeThickness { get; private set; }

    /// <summary>
    /// Рамка всех карточек последней сборки в мировых координатах — для тестов.
    /// </summary>
    internal Rect CardsBounds => _snapshot?.Bounds ?? default;

    /// <summary>
    /// Сколько карточек нарисовал бы кадр сейчас и у скольких из них рамка — тем же отбором, что у
    /// операции; для тестов и стенда.
    /// </summary>
    internal (int Visible, int Outlined) CountVisible()
    {
        if (_snapshot is not { } snapshot || _view is not { } view)
            return default;

        var (world, zoom) = Viewport(view, out _);
        var (visible, outlined) = (0, 0);
        foreach (var i in snapshot.Grid.Within(world))
        {
            visible++;
            if (IsOutlined(snapshot.Grid.Rects[i], zoom))
                outlined++;
        }

        return (visible, outlined);
    }

    /// <summary>
    /// Рисуется ли у карточки рамка при этом масштабе.
    /// </summary>
    internal static bool IsOutlined(Rect card, double zoom) => Math.Min(card.Width, card.Height) * zoom >= MinOutlinedPixels;

    /// <summary>
    /// Высота полосы: не выше трети карточки и <paramref name="accentHeight"/>, не тоньше пикселя
    /// экрана и не выше самой карточки.
    /// </summary>
    internal static double BandHeight(Rect card, double accentHeight, double zoom) =>
        Math.Min(card.Height, Math.Max(Math.Min(accentHeight, card.Height / 3), 1 / zoom));

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        Renders++;
        if (_view is not { IsSimplified: true } view || view.ItemsPanelRoot is not VirtualizingSurfacePanel panel)
            return;

        if (_stale)
            Rebuild(panel);

        if (_snapshot is not { } snapshot)
            return;

        // Снимок мировой: под трансформацией viewport пиксель экрана — это 1 / zoom мировых единиц.
        var (world, zoom) = Viewport(view, out var matrix);
        StrokeThickness = 1 / zoom;
        context.Custom(new CardsOperation(
            new Rect(Bounds.Size), snapshot, matrix, world, zoom, AccentHeight,
            Fill?.ToImmutable(), Stroke?.ToImmutable(), SelectedStroke?.ToImmutable(), SelectedStrokeThickness));

        Draws++;
    }

    /// <summary>
    /// Рисует кадр путём Skia на данный холст — тем, каким операция рисует из аренды; для тестов.
    /// </summary>
    /// <returns><see langword="false"/>, если рисовать нечего или кисть не сплошная.</returns>
    internal bool RenderTo(SKCanvas canvas)
    {
        if (_view is not { } view || _snapshot is not { } snapshot)
            return false;

        var (world, zoom) = Viewport(view, out var matrix);
        var operation = new CardsOperation(
            new Rect(Bounds.Size), snapshot, matrix, world, zoom, AccentHeight,
            Fill?.ToImmutable(), Stroke?.ToImmutable(), SelectedStroke?.ToImmutable(), SelectedStrokeThickness);
        return operation.CanPaint && operation.RenderSkia(canvas, 1);
    }

    /// <summary>
    /// Видимая часть мира и масштаб — по трансформации viewport, которой слой рисует.
    /// </summary>
    private (Rect World, double Zoom) Viewport(SurfaceView view, out Matrix matrix)
    {
        matrix = view.ViewportTransform?.Value ?? Matrix.Identity;
        var bounds = new Rect(Bounds.Size);
        var world = matrix.TryInvert(out var inverse) ? bounds.TransformToAABB(inverse) : bounds;
        return (world, Math.Max(view.ViewportZoom, 0.0001));
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == AccentHeightProperty)
        {
            _stale = true;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Watch(TemplatedParent as SurfaceView);
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Watch(null);
    }

    private void Watch(SurfaceView? view)
    {
        if (_view != null)
        {
            _view.SimplifiedContentChanged -= OnContentChanged;
            _view.PropertyChanged -= OnViewPropertyChanged;
        }

        _view = view;
        _snapshot = null;
        _stale = true;

        if (_view != null)
        {
            _view.SimplifiedContentChanged += OnContentChanged;
            _view.PropertyChanged += OnViewPropertyChanged;
        }

        InvalidateVisual();
    }

    private void OnContentChanged(object? sender, EventArgs e)
    {
        _stale = true;
        if (_view is { IsSimplified: true })
            InvalidateVisual();
    }

    private void OnViewPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == SurfaceView.IsSimplifiedProperty)
        {
            // Над порогом снимок не нужен и не держится: на большом холсте это тысячи карточек.
            _snapshot = null;
            _stale = true;
            InvalidateVisual();
        }
        else if ((e.Property == SurfaceView.ViewportZoomProperty || e.Property == SurfaceView.ViewportLocationProperty)
                 && _view is { IsSimplified: true })
        {
            // Мир в экран слой переводит сам, а рамка — в один пиксель экрана при любом масштабе.
            InvalidateVisual();
        }
    }

    private void Rebuild(VirtualizingSurfacePanel panel)
    {
        _stale = false;
        Rebuilds++;

        // Полосы — номером в палитре: кистей немного, а поток отрисовки получает их неизменяемыми.
        var rects = new List<Rect>();
        var accents = new List<int>();
        var selected = new List<bool>();
        var palette = new List<IImmutableBrush>();
        Dictionary<IBrush, int>? paletteIndex = null;
        var bounds = default(Rect);
        var (selectedCount, bandCount) = (0, 0);
        foreach (var (card, accent, isSelected) in panel.EnumerateCollapsed())
        {
            bounds = rects.Count > 0 ? bounds.Union(card) : card;
            rects.Add(card);
            selected.Add(isSelected);
            if (isSelected)
                selectedCount++;

            if (accent == null || AccentHeight <= 0)
            {
                accents.Add(-1);
                continue;
            }

            paletteIndex ??= new Dictionary<IBrush, int>(ReferenceEqualityComparer.Instance);
            if (!paletteIndex.TryGetValue(accent, out var index))
            {
                index = palette.Count;
                paletteIndex[accent] = index;
                palette.Add(accent.ToImmutable());
            }

            accents.Add(index);
            bandCount++;
        }

        _snapshot = rects.Count > 0
            ? new Snapshot(CellGrid.Build(rects.ToArray()), accents.ToArray(), selected.ToArray(), selectedCount, palette.ToArray(), bounds)
            : null;
        Cards = rects.Count;
        SelectedCards = selectedCount;
        Bands = bandCount;
    }

    /// <summary>
    /// Свёрнутое, собранное на UI-потоке: карточки по сетке, номер кисти полосы и выбор. Неизменяемо —
    /// его читает поток отрисовки.
    /// </summary>
    private sealed class Snapshot(
        CellGrid grid, int[] accents, bool[] selected, int selectedCount, IImmutableBrush[] palette, Rect bounds)
    {
        public CellGrid Grid { get; } = grid;

        public int[] Accents { get; } = accents;

        public bool[] Selected { get; } = selected;

        public int SelectedCount { get; } = selectedCount;

        public IImmutableBrush[] Palette { get; } = palette;

        public Rect Bounds { get; } = bounds;

        /// <summary>
        /// Вершины для холста Skia — кэш потока отрисовки, собирается при первом кадре снимка.
        /// </summary>
        public CardVertices? Skia { get; set; }
    }

    /// <summary>
    /// Карточки снимка треугольниками для холста Skia (ADR 0012).
    /// </summary>
    /// <remarks>
    /// Все карточки — два набора вершин на кадр (<see cref="TriangleBuilder"/>): заливки,
    /// которые от масштаба не зависят и собираются раз на снимок, и полосы с рамками — их толщина в
    /// пикселях экрана и детализация зависят от масштаба, и набор собирается заново при его смене.
    /// Отбора видимого здесь нет: лишние треугольники обрезает видеокарта, а не цикл. Живёт кэш в
    /// потоке отрисовки; нативные вершины прежнего снимка освобождает сборщик.
    /// </remarks>
    private sealed class CardVertices
    {
        private SKColor _fill;
        private double _zoom = double.NaN;
        private double _accentHeight;
        private double _selectedThickness;
        private SKColor _stroke;
        private SKColor _selected;
        private SKColor[] _palette = [];

        public SKVertices? Fills { get; private set; }

        public SKVertices? Details { get; private set; }

        public void Update(
            Snapshot snapshot, double zoom, double accentHeight, double selectedThickness,
            SKColor fill, SKColor stroke, SKColor selected, SKColor[] palette)
        {
            var rects = snapshot.Grid.Rects;
            if (Fills == null || fill != _fill)
            {
                _fill = fill;
                var builder = new TriangleBuilder(rects.Length);
                if (fill.Alpha > 0)
                {
                    foreach (var rect in rects)
                        builder.Quad(rect, fill);
                }

                Fills = builder.Build();
            }

            if (Details != null && zoom == _zoom && accentHeight == _accentHeight && selectedThickness == _selectedThickness
                && stroke == _stroke && selected == _selected && palette.AsSpan().SequenceEqual(_palette))
            {
                return;
            }

            (_zoom, _accentHeight, _selectedThickness, _stroke, _selected, _palette) =
                (zoom, accentHeight, selectedThickness, stroke, selected, palette);

            // Полоса ложится на заливку, рамка — поверх полосы, рамка выбора — поверх всех карточек.
            var details = new TriangleBuilder(rects.Length * 2);
            var pixel = 1 / zoom;
            if (accentHeight > 0)
            {
                for (var i = 0; i < rects.Length; i++)
                {
                    if (snapshot.Accents[i] is var accent and >= 0)
                        details.Quad(rects[i].WithHeight(BandHeight(rects[i], accentHeight, zoom)), palette[accent]);
                }
            }

            if (stroke.Alpha > 0)
            {
                foreach (var rect in rects)
                {
                    if (IsOutlined(rect, zoom))
                        details.Frame(rect, pixel, stroke);
                }
            }

            if (selected.Alpha > 0 && snapshot.SelectedCount > 0)
            {
                for (var i = 0; i < rects.Length; i++)
                {
                    if (snapshot.Selected[i])
                        details.Frame(rects[i], selectedThickness * pixel, selected);
                }
            }

            Details = details.Build();
        }

    }

    /// <summary>
    /// Кадр упрощённого вида: только видимые карточки, детализация по масштабу.
    /// </summary>
    /// <remarks>
    /// Холст Skia из аренды рисует все карточки двумя наборами вершин (ADR 0012); без аренды — прежним
    /// путём, по примитиву и только видимые.
    /// </remarks>
    private sealed class CardsOperation(
        Rect bounds, Snapshot snapshot, Matrix matrix, Rect world, double zoom, double accentHeight,
        IImmutableBrush? fill, IImmutableBrush? stroke, IImmutableBrush? selectedStroke, double selectedThickness)
        : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        /// <summary>
        /// Все ли кисти холст из аренды нарисует.
        /// </summary>
        public bool CanPaint =>
            SkiaCanvas.CanPaint(fill) && SkiaCanvas.CanPaint(stroke) && SkiaCanvas.CanPaint(selectedStroke)
            && Array.TrueForAll(snapshot.Palette, SkiaCanvas.CanPaint);

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            if (CanPaint)
            {
                using var lease = SkiaCanvas.TryLease(context);
                if (lease != null)
                {
                    RenderSkia(lease.SkCanvas, lease.CurrentOpacity);
                    return;
                }
            }

            RenderImmediate(context);
        }

        /// <summary>
        /// Путь Skia: два вызова на кадр — заливки и остальное, вершинами из кэша снимка.
        /// </summary>
        public bool RenderSkia(SKCanvas canvas, double opacity)
        {
            var vertices = snapshot.Skia ??= new CardVertices();
            vertices.Update(
                snapshot, zoom, accentHeight, selectedThickness, SkiaCanvas.Color(fill, 1), SkiaCanvas.Color(stroke, 1),
                SkiaCanvas.Color(selectedStroke, 1), Array.ConvertAll(snapshot.Palette, brush => SkiaCanvas.Color(brush, 1)));

            SkiaCanvas.Enter(canvas, bounds, matrix);
            try
            {
                // Прозрачность контекста — слоем: цвета вершин собраны непрозрачными.
                using var layer = opacity < 1 ? new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(255 * opacity)) } : null;
                if (layer != null)
                    canvas.SaveLayer(layer);

                // Цвет — у вершин: краска без шейдера, и режим Dst оставляет их цвет как есть.
                using var paint = new SKPaint { Color = SKColors.White };
                if (vertices.Fills is { } fills)
                    canvas.DrawVertices(fills, SKBlendMode.Dst, paint);

                if (vertices.Details is { } details)
                    canvas.DrawVertices(details, SKBlendMode.Dst, paint);

                if (layer != null)
                    canvas.Restore();
            }
            finally
            {
                canvas.Restore();
            }

            return true;
        }

        /// <summary>
        /// Запасной путь: по примитиву через контекст.
        /// </summary>
        private void RenderImmediate(ImmediateDrawingContext context)
        {
            var pixel = 1 / zoom;
            var pen = stroke != null ? new ImmutablePen(stroke, pixel) : null;
            var rects = snapshot.Grid.Rects;
            using (context.PushClip(bounds))
            using (context.PushPreTransform(matrix))
            {
                // Полоса ложится на заливку, а рамка — поверх полосы.
                foreach (var i in snapshot.Grid.Within(world))
                {
                    var card = rects[i];
                    if (fill != null)
                        context.FillRectangle(fill, card);

                    if (snapshot.Accents[i] is var accent and >= 0 && accentHeight > 0)
                        context.FillRectangle(snapshot.Palette[accent], card.WithHeight(BandHeight(card, accentHeight, zoom)));

                    if (pen != null && IsOutlined(card, zoom))
                        context.DrawRectangle(pen, card);
                }

                // Рамка выбора — поверх всех карточек, чтобы соседняя её не закрыла.
                if (selectedStroke != null && snapshot.SelectedCount > 0)
                {
                    var selectedPen = new ImmutablePen(selectedStroke, selectedThickness * pixel);
                    foreach (var i in snapshot.Grid.Within(world))
                    {
                        if (snapshot.Selected[i])
                            context.DrawRectangle(selectedPen, rects[i]);
                    }
                }
            }
        }
    }
}
