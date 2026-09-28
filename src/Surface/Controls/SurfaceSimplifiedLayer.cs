using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArxisStudio.Surface;

/// <summary>
/// Слой упрощённого вида: карточки свёрнутых элементов, когда масштаб ниже
/// <see cref="SurfaceView.SimplifiedZoom"/> (ADR 0008).
/// </summary>
/// <remarks>
/// Стоит в шаблоне поверхности под элементами, в слое с трансформацией viewport, и рисует в мировых
/// координатах. Прямоугольники берёт у виртуализирующей панели своей поверхности — той, чей шаблон его
/// создал, — и собирает их в одну геометрию, которую держит, пока свёрнутое не сменится: геометрия
/// элемента без контейнера, коллекция, состав развёрнутых. Панорама и перетаскивание развёрнутого
/// элемента её не пересобирают. Развёрнутые элементы в карточки не входят — рисуют себя сами, поверх
/// слоя. Рамка карточки — в один пиксель экрана при любом масштабе.
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

    private SurfaceView? _view;
    private StreamGeometry? _cards;
    private bool _stale = true;

    static SurfaceSimplifiedLayer()
    {
        AffectsRender<SurfaceSimplifiedLayer>(FillProperty, StrokeProperty);
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

        // Слой лежит под трансформацией viewport: пиксель экрана — это 1 / zoom мировых единиц.
        StrokeThickness = 1 / Math.Max(view.ViewportZoom, 0.0001);
        var pen = Stroke is { } stroke ? new Pen(stroke, StrokeThickness) : null;
        context.DrawGeometry(Fill, pen, _cards);
        Draws++;
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
            _stale = true;
            InvalidateVisual();
        }
        else if (e.Property == SurfaceView.ViewportZoomProperty && _view is { IsSimplified: true })
        {
            // Рамка — в один пиксель экрана, и её толщина следует масштабу.
            InvalidateVisual();
        }
    }

    private void Rebuild(VirtualizingSurfacePanel panel)
    {
        _stale = false;
        Rebuilds++;

        // Прямоугольники обходятся в одну сторону, и правило NonZero закрашивает их объединение: по
        // умолчанию EvenOdd, и перекрытие двух карточек вышло бы дырой.
        var cards = new StreamGeometry();
        var count = 0;
        using (var context = cards.Open())
        {
            context.SetFillRule(FillRule.NonZero);
            foreach (var bounds in panel.EnumerateCollapsedBounds())
            {
                context.BeginFigure(bounds.TopLeft, isFilled: true);
                context.LineTo(bounds.TopRight);
                context.LineTo(bounds.BottomRight);
                context.LineTo(bounds.BottomLeft);
                context.EndFigure(isClosed: true);
                count++;
            }
        }

        _cards = count > 0 ? cards : null;
        Cards = count;
    }
}
