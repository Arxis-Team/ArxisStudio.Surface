using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Протягиваемая связь: кривая от порта к указателю, пока жест не закончен.
/// </summary>
/// <remarks>
/// Та же кривая, что у настоящей связи, и так же стоит в своём прямоугольнике в мировых
/// координатах. Концы всегда «из выхода во вход»: тянут ли от входа или от выхода, кривая
/// выходит вправо и входит слева.
/// </remarks>
internal sealed class PendingLinkPreview : Control
{
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<PendingLinkPreview, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<PendingLinkPreview, double>(nameof(StrokeThickness), 2.0);

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public LinkGeometry Geometry { get; private set; }

    public Rect WorldBounds { get; private set; }

    public bool IsActive { get; private set; }

    public PendingLinkPreview()
    {
        IsVisible = false;
    }

    public void Show(Point source, Point target)
    {
        Geometry = new LinkGeometry(source, target);
        WorldBounds = Geometry.Bounds.Inflate(StrokeThickness);
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
        var g = Geometry;
        var figure = new StreamGeometry();
        using (var ctx = figure.Open())
        {
            ctx.BeginFigure(g.Source - offset, isFilled: false);
            ctx.CubicBezierTo(g.SourceControl - offset, g.TargetControl - offset, g.Target - offset);
            ctx.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, new Pen(stroke, StrokeThickness, lineCap: PenLineCap.Round), figure);
    }
}
