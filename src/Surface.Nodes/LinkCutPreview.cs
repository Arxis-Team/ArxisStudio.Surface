using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Отрезок разреза: от нажатия до указателя, пока жест не закончен.
/// </summary>
/// <remarks>
/// Стоит, как и превью протяжки, в своём прямоугольнике в мировых координатах, но толщину держит
/// в пикселях экрана: разрез — указка, а не часть графа, и расти с приближением ему незачем.
/// Пунктир отмеряется в толщинах пера и поэтому тоже от масштаба не зависит.
/// </remarks>
internal sealed class LinkCutPreview : Control
{
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<LinkCutPreview, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<LinkCutPreview, double>(nameof(StrokeThickness), 1.5);

    private static readonly DashStyle Dashes = new([4.0, 3.0], 0);

    private double _zoom = 1;

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>
    /// Толщина в пикселях экрана.
    /// </summary>
    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public Point Start { get; private set; }

    public Point End { get; private set; }

    public Rect WorldBounds { get; private set; }

    public bool IsActive { get; private set; }

    public LinkCutPreview()
    {
        IsVisible = false;
    }

    public void Show(Point start, Point end, double zoom)
    {
        Start = start;
        End = end;
        _zoom = Math.Max(zoom, 0.0001);

        var pad = StrokeThickness / _zoom;
        WorldBounds = new Rect(
            new Point(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y)),
            new Point(Math.Max(start.X, end.X), Math.Max(start.Y, end.Y))).Inflate(pad);

        IsActive = true;
        IsVisible = true;
        Canvas.SetLeft(this, WorldBounds.X);
        Canvas.SetTop(this, WorldBounds.Y);
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void Hide()
    {
        IsActive = false;
        IsVisible = false;
    }

    protected override Size MeasureOverride(Size availableSize) => IsActive ? WorldBounds.Size : default;

    public override void Render(DrawingContext context)
    {
        if (!IsActive || Stroke is not { } stroke)
            return;

        var offset = WorldBounds.Position;
        var pen = new Pen(stroke, StrokeThickness / _zoom, Dashes, PenLineCap.Round);
        context.DrawLine(pen, Start - offset, End - offset);
    }
}
