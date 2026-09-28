using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Связи упрощённого вида: кривые записей без контролов, когда масштаб ниже
/// <see cref="SurfaceView.SimplifiedZoom"/> (ADR 0008).
/// </summary>
/// <remarks>
/// Стоит в шаблоне редактора под панелью связей, в слое с трансформацией viewport. Ниже порога
/// контрол есть только у закреплённой связи и у связи развёрнутого узла; остальные слой рисует сам —
/// одной геометрией, выбранные — второй, своей кистью. Геометрия держится, пока не сменится запись без
/// контрола, состав развёрнутых или выбор, и над порогом не держится. Толщина — в пикселях экрана.
/// </remarks>
internal sealed class SimplifiedLinkLayer : Control
{
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<SimplifiedLinkLayer, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> SelectedStrokeProperty =
        AvaloniaProperty.Register<SimplifiedLinkLayer, IBrush?>(nameof(SelectedStroke));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<SimplifiedLinkLayer, double>(nameof(StrokeThickness), 1);

    private NodeEditor? _editor;
    private StreamGeometry? _links;
    private StreamGeometry? _selected;
    private bool _stale = true;

    static SimplifiedLinkLayer()
    {
        AffectsRender<SimplifiedLinkLayer>(StrokeProperty, SelectedStrokeProperty, StrokeThicknessProperty);
    }

    /// <summary>
    /// Кисть связи.
    /// </summary>
    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>
    /// Кисть выбранной связи.
    /// </summary>
    public IBrush? SelectedStroke
    {
        get => GetValue(SelectedStrokeProperty);
        set => SetValue(SelectedStrokeProperty, value);
    }

    /// <summary>
    /// Толщина связи в пикселях экрана.
    /// </summary>
    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>
    /// Сколько невыбранных связей в последней сборке — для тестов.
    /// </summary>
    internal int Links { get; private set; }

    /// <summary>
    /// Сколько выбранных связей в последней сборке — для тестов.
    /// </summary>
    internal int SelectedLinks { get; private set; }

    /// <summary>
    /// Сколько раз связи собирались заново — для тестов и стенда.
    /// </summary>
    internal int Rebuilds { get; private set; }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        if (_editor is not { IsSimplified: true } editor)
            return;

        if (_stale)
            Rebuild(editor);

        // Слой лежит под трансформацией viewport: пиксель экрана — это 1 / zoom мировых единиц.
        var thickness = StrokeThickness / Math.Max(editor.ViewportZoom, 0.0001);
        if (_links != null && Stroke is { } stroke)
            context.DrawGeometry(null, new Pen(stroke, thickness), _links);

        if (_selected != null && SelectedStroke is { } selected)
            context.DrawGeometry(null, new Pen(selected, thickness), _selected);
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Watch(TemplatedParent as NodeEditor);
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Watch(null);
    }

    private void Watch(NodeEditor? editor)
    {
        if (_editor != null)
        {
            _editor.SimplifiedLinksChanged -= OnLinksChanged;
            _editor.PropertyChanged -= OnEditorPropertyChanged;
        }

        _editor = editor;
        Forget();

        if (_editor != null)
        {
            _editor.SimplifiedLinksChanged += OnLinksChanged;
            _editor.PropertyChanged += OnEditorPropertyChanged;
        }

        InvalidateVisual();
    }

    private void OnLinksChanged(object? sender, EventArgs e)
    {
        _stale = true;
        if (_editor is { IsSimplified: true })
            InvalidateVisual();
    }

    private void OnEditorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == SurfaceView.IsSimplifiedProperty)
        {
            // Над порогом кривые не нужны и не держатся: на большом графе это тысячи фигур.
            Forget();
            InvalidateVisual();
        }
        else if (e.Property == SurfaceView.ViewportZoomProperty && _editor is { IsSimplified: true })
        {
            InvalidateVisual();
        }
    }

    private void Forget()
    {
        _links = null;
        _selected = null;
        _stale = true;
    }

    private void Rebuild(NodeEditor editor)
    {
        _stale = false;
        Rebuilds++;

        var links = new StreamGeometry();
        var selected = new StreamGeometry();
        var (count, selectedCount) = (0, 0);
        using (var linksContext = links.Open())
        using (var selectedContext = selected.Open())
        {
            foreach (var record in editor.LinkRecords)
            {
                if (record.Control != null || !record.IsResolved)
                    continue;

                var context = record.IsSelected ? selectedContext : linksContext;
                var g = record.Geometry;
                context.BeginFigure(g.Source, isFilled: false);
                context.CubicBezierTo(g.SourceControl, g.TargetControl, g.Target);
                context.EndFigure(isClosed: false);

                if (record.IsSelected)
                    selectedCount++;
                else
                    count++;
            }
        }

        _links = count > 0 ? links : null;
        _selected = selectedCount > 0 ? selected : null;
        Links = count;
        SelectedLinks = selectedCount;
    }
}
