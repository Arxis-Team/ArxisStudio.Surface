using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

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
/// своей поверхности — той, чей шаблон его создал, — и собирает их в одну геометрию, которую держит,
/// пока свёрнутое не сменится: геометрия элемента без контейнера, коллекция, состав развёрнутых.
/// Панорама и перетаскивание развёрнутого элемента её не пересобирают — панорама только перерисовывает
/// готовое. Развёрнутые элементы в карточки не входят — рисуют себя сами, поверх слоя. Рамка карточки —
/// в один пиксель экрана при любом масштабе.
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

    private SurfaceView? _view;
    private StreamGeometry? _cards;
    private StreamGeometry? _selected;
    private List<(IBrush Brush, StreamGeometry Geometry)>? _bands;
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
    internal int AccentGroups => _bands?.Count ?? 0;

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
    internal Rect CardsBounds => _cards?.Bounds ?? default;

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        Renders++;
        if (_view is not { IsSimplified: true } view || view.ItemsPanelRoot is not VirtualizingSurfacePanel panel)
            return;

        if (_stale)
            Rebuild(panel);

        if (_cards == null)
            return;

        // Геометрия мировая: под трансформацией viewport пиксель экрана — это 1 / zoom мировых единиц.
        StrokeThickness = 1 / Math.Max(view.ViewportZoom, 0.0001);
        var pen = Stroke is { } stroke ? new Pen(stroke, StrokeThickness) : null;
        using (context.PushTransform(view.ViewportTransform?.Value ?? Matrix.Identity))
        {
            if (_bands == null)
            {
                context.DrawGeometry(Fill, pen, _cards);
            }
            else
            {
                // Полоса ложится на заливку, а рамка — поверх полосы.
                context.DrawGeometry(Fill, null, _cards);
                foreach (var (brush, geometry) in _bands)
                    context.DrawGeometry(brush, null, geometry);

                if (pen != null)
                    context.DrawGeometry(null, pen, _cards);
            }

            // Рамка выбора — поверх всех карточек, чтобы соседняя её не закрыла.
            if (_selected != null && SelectedStroke is { } selected)
                context.DrawGeometry(null, new Pen(selected, SelectedStrokeThickness * StrokeThickness), _selected);
        }

        Draws++;
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
        _cards = null;
        _selected = null;
        _bands = null;
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
            // Над порогом геометрия не нужна и не держится: на большом холсте это тысячи фигур.
            _cards = null;
            _selected = null;
            _bands = null;
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

        // Прямоугольники обходятся в одну сторону, и правило NonZero закрашивает их объединение: по
        // умолчанию EvenOdd, и перекрытие двух карточек вышло бы дырой. Полосы — по геометрии на
        // кисть: их немного, а рисуются они одним вызовом на кисть.
        var cards = new StreamGeometry();
        var selected = new StreamGeometry();
        Dictionary<IBrush, (StreamGeometry Geometry, StreamGeometryContext Context)>? bands = null;
        var count = 0;
        var selectedCount = 0;
        var bandCount = 0;
        using (var context = cards.Open())
        using (var selectedContext = selected.Open())
        {
            context.SetFillRule(FillRule.NonZero);
            foreach (var (bounds, accent, isSelected) in panel.EnumerateCollapsed())
            {
                AddRectangle(context, bounds);
                count++;

                if (isSelected)
                {
                    AddRectangle(selectedContext, bounds);
                    selectedCount++;
                }

                if (accent == null || AccentHeight <= 0)
                    continue;

                bands ??= new Dictionary<IBrush, (StreamGeometry, StreamGeometryContext)>(ReferenceEqualityComparer.Instance);
                if (!bands.TryGetValue(accent, out var band))
                {
                    var geometry = new StreamGeometry();
                    band = (geometry, geometry.Open());
                    band.Context.SetFillRule(FillRule.NonZero);
                    bands[accent] = band;
                }

                AddRectangle(band.Context, bounds.WithHeight(Math.Min(AccentHeight, bounds.Height / 3)));
                bandCount++;
            }
        }

        List<(IBrush, StreamGeometry)>? built = null;
        if (bands != null)
        {
            built = new List<(IBrush, StreamGeometry)>(bands.Count);
            foreach (var (brush, band) in bands)
            {
                band.Context.Dispose();
                built.Add((brush, band.Geometry));
            }
        }

        _cards = count > 0 ? cards : null;
        _selected = selectedCount > 0 ? selected : null;
        _bands = built;
        Cards = count;
        SelectedCards = selectedCount;
        Bands = bandCount;
    }

    private static void AddRectangle(StreamGeometryContext context, Rect bounds)
    {
        context.BeginFigure(bounds.TopLeft, isFilled: true);
        context.LineTo(bounds.TopRight);
        context.LineTo(bounds.BottomRight);
        context.LineTo(bounds.BottomLeft);
        context.EndFigure(isClosed: true);
    }
}
