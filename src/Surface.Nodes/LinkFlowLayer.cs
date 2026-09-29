using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Поток по проводам: маркеры направления и импульсы отладки (ADR 0014).
/// </summary>
/// <remarks>
/// Стоит в шаблоне редактора над связями и под узлами, во весь его размер, и переводит мир в экран
/// трансформацией viewport сам — как слой упрощённых связей. Рисует по записям связей, а не по их
/// контролам: маркеры и импульсы видны и в упрощённом виде, и у связи без контрола. Маркеры — только у
/// видимых связей, по ячейкам окна связей; импульсы — у горящих. Пока горит хоть один импульс, слой
/// просит у окна кадр за кадром; погас последний — не просит ничего.
/// <para>
/// Вид по умолчанию — свойства слоя, которые шаблон берёт у темы (<c>NodeEditor.LinkPulse.*</c>,
/// <c>NodeEditor.LinkMarker.*</c>); вид отдельного импульса или маркера задаёт хост в
/// <see cref="LinkPulse"/> и <see cref="LinkMarker"/>.
/// </para>
/// </remarks>
internal sealed class LinkFlowLayer : Control
{
    public static readonly StyledProperty<IBrush?> PulseBrushProperty =
        AvaloniaProperty.Register<LinkFlowLayer, IBrush?>(nameof(PulseBrush));

    public static readonly StyledProperty<IBrush?> GlowBrushProperty =
        AvaloniaProperty.Register<LinkFlowLayer, IBrush?>(nameof(GlowBrush));

    public static readonly StyledProperty<double> PulseSizeProperty =
        AvaloniaProperty.Register<LinkFlowLayer, double>(nameof(PulseSize), 7);

    public static readonly StyledProperty<double> GlowThicknessProperty =
        AvaloniaProperty.Register<LinkFlowLayer, double>(nameof(GlowThickness), 5);

    public static readonly StyledProperty<double> GlowOpacityProperty =
        AvaloniaProperty.Register<LinkFlowLayer, double>(nameof(GlowOpacity), 0.5);

    public static readonly StyledProperty<IBrush?> MarkerBrushProperty =
        AvaloniaProperty.Register<LinkFlowLayer, IBrush?>(nameof(MarkerBrush));

    public static readonly StyledProperty<IBrush?> SelectedMarkerBrushProperty =
        AvaloniaProperty.Register<LinkFlowLayer, IBrush?>(nameof(SelectedMarkerBrush));

    /// <summary>
    /// Больше маркеров на одной связи не рисуется: шаг, заданный слишком мелким, не должен ставить их
    /// тысячами.
    /// </summary>
    private const int MaxMarkersPerLink = 256;

    // Фигуры в квадрате от −1 до 1, остриём по оси X.
    private static readonly Geometry ArrowShape = Geometry.Parse("M 1,0 L -1,-0.8 L -0.7,0 L -1,0.8 Z");
    private static readonly Geometry ChevronShape = Geometry.Parse("M -0.5,-0.9 L 0.6,0 L -0.5,0.9");
    private static readonly Geometry DiamondShape = Geometry.Parse("M 1,0 L 0,-0.6 L -1,0 L 0,0.6 Z");
    private static readonly Geometry SquareShape = Geometry.Parse("M -0.7,-0.7 L 0.7,-0.7 L 0.7,0.7 L -0.7,0.7 Z");

    private readonly List<LinkRecord> _visible = new();
    private NodeEditor? _editor;
    private bool _frameRequested;

    static LinkFlowLayer()
    {
        AffectsRender<LinkFlowLayer>(
            PulseBrushProperty, GlowBrushProperty, PulseSizeProperty, GlowThicknessProperty, GlowOpacityProperty,
            MarkerBrushProperty, SelectedMarkerBrushProperty);
    }

    /// <summary>Кисть фигур импульса по умолчанию.</summary>
    public IBrush? PulseBrush
    {
        get => GetValue(PulseBrushProperty);
        set => SetValue(PulseBrushProperty, value);
    }

    /// <summary>Кисть свечения провода по умолчанию.</summary>
    public IBrush? GlowBrush
    {
        get => GetValue(GlowBrushProperty);
        set => SetValue(GlowBrushProperty, value);
    }

    /// <summary>Размер фигуры импульса по умолчанию, в мировых единицах.</summary>
    public double PulseSize
    {
        get => GetValue(PulseSizeProperty);
        set => SetValue(PulseSizeProperty, value);
    }

    /// <summary>Толщина свечения по умолчанию, в мировых единицах.</summary>
    public double GlowThickness
    {
        get => GetValue(GlowThicknessProperty);
        set => SetValue(GlowThicknessProperty, value);
    }

    /// <summary>Непрозрачность свечения в полную силу импульса.</summary>
    public double GlowOpacity
    {
        get => GetValue(GlowOpacityProperty);
        set => SetValue(GlowOpacityProperty, value);
    }

    /// <summary>Кисть маркера у провода без цвета модели.</summary>
    public IBrush? MarkerBrush
    {
        get => GetValue(MarkerBrushProperty);
        set => SetValue(MarkerBrushProperty, value);
    }

    /// <summary>Кисть маркера у выбранного провода.</summary>
    public IBrush? SelectedMarkerBrush
    {
        get => GetValue(SelectedMarkerBrushProperty);
        set => SetValue(SelectedMarkerBrushProperty, value);
    }

    /// <summary>Сколько маркеров нарисовал последний кадр — для тестов.</summary>
    internal int DrawnMarkers { get; private set; }

    /// <summary>Сколько фигур импульсов нарисовал последний кадр — для тестов.</summary>
    internal int DrawnPulseShapes { get; private set; }

    /// <summary>Где стояли фигуры импульсов в последнем кадре, в мировых координатах, — для тестов.</summary>
    internal List<Point> PulsePoints { get; } = new();

    /// <summary>Сколько проводов светилось в последнем кадре — для тестов.</summary>
    internal int DrawnGlows { get; private set; }

    /// <summary>Сколько раз слой просил у окна кадр — для тестов: без импульсов не просит.</summary>
    internal int FramesRequested { get; private set; }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        DrawnMarkers = 0;
        DrawnPulseShapes = 0;
        DrawnGlows = 0;
        PulsePoints.Clear();

        if (_editor is not { } editor)
            return;

        var matrix = editor.ViewportTransform?.Value ?? Matrix.Identity;
        var bounds = new Rect(Bounds.Size);
        var world = matrix.TryInvert(out var inverse) ? bounds.TransformToAABB(inverse) : bounds;
        var zoom = Math.Max(editor.ViewportZoom, 0.0001);

        var pulses = editor.ActivePulses > 0 ? editor.LivePulses(editor.PulseClock()) : [];
        if (!editor.HasMarkers && pulses.Count == 0)
            return;

        using (context.PushTransform(matrix))
        {
            if (editor.HasMarkers)
                DrawMarkers(context, editor, world, zoom);

            if (pulses.Count > 0)
                DrawPulses(context, editor, pulses, world, zoom);
        }

        if (pulses.Count > 0)
            RequestFrame();
    }

    private void DrawMarkers(DrawingContext context, NodeEditor editor, Rect world, double zoom)
    {
        _visible.Clear();
        editor.CollectLinksWithin(world, _visible);
        foreach (var record in _visible)
        {
            if (editor.MarkerOf(record) is not { } marker || marker.Size * zoom < marker.MinScreenSize)
                continue;

            var brush = marker.Brush ?? (record.IsSelected ? SelectedMarkerBrush : record.Stroke ?? MarkerBrush);
            if (brush == null)
                continue;

            var path = record.Path;
            if (marker.Spacing > 0)
            {
                var count = 0;
                for (var s = marker.Spacing / 2; s < path.Length && count < MaxMarkersPerLink; s += marker.Spacing, count++)
                    DrawAt(context, path, s, marker.Shape, marker.Geometry, marker.Size, brush, world);
            }
            else
            {
                DrawAt(context, path, Math.Clamp(marker.Position, 0, 1) * path.Length, marker.Shape, marker.Geometry, marker.Size, brush, world);
            }
        }

        _visible.Clear();
    }

    private void DrawAt(DrawingContext context, LinkPath path, double distance, LinkShape shape, Geometry? custom, double size, IBrush brush, Rect world)
    {
        var (point, direction) = path.At(distance);
        if (!world.Inflate(size).Contains(point))
            return;

        DrawShape(context, shape, custom, point, direction, size, brush);
        DrawnMarkers++;
    }

    private void DrawPulses(DrawingContext context, NodeEditor editor, IReadOnlyList<(LinkRecord Record, NodeEditor.ActivePulse Pulse)> pulses, Rect world, double zoom)
    {
        var now = editor.PulseClock();
        foreach (var (record, active) in pulses)
        {
            if (!record.IsResolved || !world.Intersects(record.WorldBounds))
                continue;

            var style = active.Style;
            var strength = style.Envelope(now - active.Lit);
            if (strength <= 0)
                continue;

            var glowThickness = style.GlowThickness ?? GlowThickness;
            if (glowThickness > 0 && (style.GlowBrush ?? GlowBrush) is { } glow)
            {
                using (context.PushOpacity(strength * GlowOpacity))
                    context.DrawGeometry(null, new Pen(glow, glowThickness, lineCap: PenLineCap.Round), Curve(record.Geometry));

                DrawnGlows++;
            }

            if ((style.Brush ?? PulseBrush) is not { } brush || style.Spacing <= 0 || style.Speed < 0)
                continue;

            var size = Math.Max(style.Size ?? PulseSize, 1.5 / zoom);
            var path = record.Path;
            var travelled = (now - active.Started).TotalSeconds * style.Speed;
            var phase = travelled % style.Spacing;
            using (context.PushOpacity(strength))
            {
                var count = 0;
                for (var s = phase; s <= path.Length && count < MaxMarkersPerLink; s += style.Spacing, count++)
                {
                    var (point, direction) = path.At(s);
                    if (!world.Inflate(size).Contains(point))
                        continue;

                    DrawShape(context, style.Shape, style.Geometry, point, direction, size, brush);
                    PulsePoints.Add(point);
                    DrawnPulseShapes++;
                }
            }
        }
    }

    private static StreamGeometry Curve(LinkGeometry g)
    {
        var figure = new StreamGeometry();
        using (var ctx = figure.Open())
        {
            ctx.BeginFigure(g.Source, isFilled: false);
            ctx.CubicBezierTo(g.SourceControl, g.TargetControl, g.Target);
            ctx.EndFigure(isClosed: false);
        }

        return figure;
    }

    /// <summary>
    /// Рисует фигуру длиной <paramref name="size"/> в точке, остриём по <paramref name="direction"/>.
    /// </summary>
    private static void DrawShape(DrawingContext context, LinkShape shape, Geometry? custom, Point point, Vector direction, double size, IBrush brush)
    {
        var half = size / 2;
        if (shape == LinkShape.Circle || (shape == LinkShape.Custom && custom == null))
        {
            context.DrawEllipse(brush, null, point, half, half);
            return;
        }

        var geometry = shape switch
        {
            LinkShape.Arrow => ArrowShape,
            LinkShape.Chevron => ChevronShape,
            LinkShape.Diamond => DiamondShape,
            LinkShape.Square => SquareShape,
            _ => custom!
        };

        var angle = Math.Atan2(direction.Y, direction.X);
        var transform = Matrix.CreateScale(half, half) * Matrix.CreateRotation(angle) * Matrix.CreateTranslation(point.X, point.Y);
        using (context.PushTransform(transform))
        {
            // Шеврон — линия, а не заливка: толщина — в единицах фигуры, треть её полувысоты.
            if (shape == LinkShape.Chevron)
                context.DrawGeometry(null, new Pen(brush, 0.35, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
            else
                context.DrawGeometry(brush, null, geometry);
        }
    }

    private void RequestFrame()
    {
        if (_frameRequested || TopLevel.GetTopLevel(this) is not { } top)
            return;

        _frameRequested = true;
        FramesRequested++;
        top.RequestAnimationFrame(_ =>
        {
            _frameRequested = false;
            InvalidateVisual();
        });
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
            _editor.LinkFlowChanged -= OnFlowChanged;
            _editor.PropertyChanged -= OnEditorPropertyChanged;
        }

        _editor = editor;

        if (_editor != null)
        {
            _editor.LinkFlowChanged += OnFlowChanged;
            _editor.PropertyChanged += OnEditorPropertyChanged;
        }

        InvalidateVisual();
    }

    private bool IsBusy => _editor is { } editor && (editor.HasMarkers || editor.ActivePulses > 0);

    private void OnFlowChanged(object? sender, EventArgs e)
    {
        // Пустой слой перерисовывать незачем, кроме кадра, стирающего погасшее.
        if (IsBusy || DrawnMarkers + DrawnPulseShapes + DrawnGlows > 0)
            InvalidateVisual();
    }

    private void OnEditorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if ((e.Property == SurfaceView.ViewportZoomProperty || e.Property == SurfaceView.ViewportLocationProperty) && IsBusy)
            InvalidateVisual();
    }
}
