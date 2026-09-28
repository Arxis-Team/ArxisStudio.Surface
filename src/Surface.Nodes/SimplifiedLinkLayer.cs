using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Связи упрощённого вида: кривые записей без контролов, когда масштаб ниже
/// <see cref="SurfaceView.SimplifiedZoom"/> (ADR 0008).
/// </summary>
/// <remarks>
/// Стоит в шаблоне редактора под панелью связей, во весь его размер, и переводит мир в экран
/// трансформацией viewport сам — как слой карточек ядра, и по той же причине: слой без размера под
/// трансформацией рендерер отсекает. Ниже порога контрол есть только у закреплённой связи и у связи
/// развёрнутого узла; остальные слой рисует сам — одной геометрией, выбранные — второй, своей кистью.
/// Геометрия держится, пока не сменится запись без контрола, состав развёрнутых или выбор, и над
/// порогом не держится. Толщина — в пикселях экрана.
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
    private List<(IBrush Brush, StreamGeometry Geometry)>? _colored;
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
    /// Сколько разных цветов модели у проводов последней сборки — для тестов.
    /// </summary>
    internal int StrokeGroups => _colored?.Count ?? 0;

    /// <summary>
    /// Сколько раз связи собирались заново — для тестов и стенда.
    /// </summary>
    internal int Rebuilds { get; private set; }

    /// <summary>
    /// Сколько раз связи нарисованы — для тестов.
    /// </summary>
    internal int Draws { get; private set; }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        if (_editor is not { IsSimplified: true } editor)
            return;

        if (_stale)
            Rebuild(editor);

        // Геометрия мировая: под трансформацией viewport пиксель экрана — это 1 / zoom мировых единиц.
        var thickness = StrokeThickness / Math.Max(editor.ViewportZoom, 0.0001);
        using (context.PushTransform(editor.ViewportTransform?.Value ?? Matrix.Identity))
        {
            if (_links != null && Stroke is { } stroke)
                context.DrawGeometry(null, new Pen(stroke, thickness), _links);

            if (_colored != null)
            {
                foreach (var (brush, geometry) in _colored)
                    context.DrawGeometry(null, new Pen(brush, thickness), geometry);
            }

            if (_selected != null && SelectedStroke is { } selected)
                context.DrawGeometry(null, new Pen(selected, thickness), _selected);
        }

        Draws++;
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
        else if ((e.Property == SurfaceView.ViewportZoomProperty || e.Property == SurfaceView.ViewportLocationProperty)
                 && _editor is { IsSimplified: true })
        {
            InvalidateVisual();
        }
    }

    private void Forget()
    {
        _links = null;
        _selected = null;
        _colored = null;
        _stale = true;
    }

    private void Rebuild(NodeEditor editor)
    {
        _stale = false;
        Rebuilds++;

        // Провода без цвета модели — одной геометрией кистью темы, с цветом — по геометрии на кисть,
        // выбранные — своей, цветом выбора.
        var links = new StreamGeometry();
        var selected = new StreamGeometry();
        Dictionary<IBrush, (StreamGeometry Geometry, StreamGeometryContext Context)>? colored = null;
        var (count, selectedCount) = (0, 0);
        using (var linksContext = links.Open())
        using (var selectedContext = selected.Open())
        {
            foreach (var record in editor.LinkRecords)
            {
                if (record.Control != null || !record.IsResolved)
                    continue;

                StreamGeometryContext context;
                if (record.IsSelected)
                {
                    context = selectedContext;
                    selectedCount++;
                }
                else
                {
                    count++;
                    if (record.Stroke is { } brush)
                    {
                        colored ??= new Dictionary<IBrush, (StreamGeometry, StreamGeometryContext)>(ReferenceEqualityComparer.Instance);
                        if (!colored.TryGetValue(brush, out var group))
                        {
                            var geometry = new StreamGeometry();
                            colored[brush] = group = (geometry, geometry.Open());
                        }

                        context = group.Context;
                    }
                    else
                    {
                        context = linksContext;
                    }
                }

                var g = record.Geometry;
                context.BeginFigure(g.Source, isFilled: false);
                context.CubicBezierTo(g.SourceControl, g.TargetControl, g.Target);
                context.EndFigure(isClosed: false);
            }
        }

        List<(IBrush, StreamGeometry)>? groups = null;
        if (colored != null)
        {
            groups = new List<(IBrush, StreamGeometry)>(colored.Count);
            foreach (var (brush, group) in colored)
            {
                group.Context.Dispose();
                groups.Add((brush, group.Geometry));
            }
        }

        _links = links;
        _selected = selectedCount > 0 ? selected : null;
        _colored = groups;
        Links = count;
        SelectedLinks = selectedCount;
    }
}
