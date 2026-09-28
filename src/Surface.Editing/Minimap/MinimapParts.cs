using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering.SceneGraph;

namespace ArxisStudio.Surface.Editing;

/// <summary>
/// Содержимое миникарты, собранное на UI-потоке: прямоугольники контейнеров и кривые слоя выше, в
/// мировых координатах. Неизменяемо — его читает поток отрисовки.
/// </summary>
internal sealed class MinimapSnapshot(Rect[] items, MinimapCurve[] curves)
{
    public Rect[] Items { get; } = items;

    public MinimapCurve[] Curves { get; } = curves;
}

/// <summary>
/// Содержимое миникарты — отдельный визуал с кэшем в картинку (ADR 0011).
/// </summary>
/// <remarks>
/// Панорама двигает только рамку видимой области, и содержимое между сменами самого содержимого или
/// соответствия карты не записывается заново и не растеризуется: композитор берёт готовую картинку.
/// Рисует оно своей операцией, чьи границы — прямоугольник визуала: границы геометрии на десять тысяч
/// фигур композитор иначе мерил бы на каждой записи.
/// </remarks>
internal sealed class MinimapContent : Control
{
    private MinimapSnapshot? _snapshot;
    private Matrix _matrix = Matrix.Identity;
    private double _scale = 1;
    private IBrush? _fill;
    private IBrush? _stroke;
    private IImmutableBrush? _immutableFill;
    private IImmutableBrush? _immutableStroke;

    public MinimapContent()
    {
        CacheMode = new BitmapCache();
        IsHitTestVisible = false;
    }

    /// <summary>
    /// Сколько раз содержимое записывалось заново — для тестов.
    /// </summary>
    internal int Renders { get; private set; }

    /// <summary>
    /// Ставит содержимое и соответствие; то же самое записи не трогает.
    /// </summary>
    /// <remarks>
    /// Кисти сравниваются ссылкой на кисть хоста: неизменяемая копия новая на каждом снятии, и по ней
    /// содержимое записывалось бы заново на каждом кадре.
    /// </remarks>
    public void Show(MinimapSnapshot? snapshot, Matrix matrix, double scale, IBrush? fill, IBrush? stroke)
    {
        if (ReferenceEquals(snapshot, _snapshot) && matrix == _matrix && ReferenceEquals(fill, _fill) && ReferenceEquals(stroke, _stroke))
            return;

        _snapshot = snapshot;
        _matrix = matrix;
        _scale = scale;
        _fill = fill;
        _stroke = stroke;
        _immutableFill = fill?.ToImmutable();
        _immutableStroke = stroke?.ToImmutable();
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        Renders++;
        if (_snapshot is { } snapshot && Bounds.Width > 0 && Bounds.Height > 0)
            context.Custom(new Operation(new Rect(Bounds.Size), snapshot, _matrix, _scale, _immutableFill, _immutableStroke));
    }

    private sealed class Operation(
        Rect bounds, MinimapSnapshot snapshot, Matrix matrix, double scale, IImmutableBrush? fill, IImmutableBrush? stroke)
        : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            using (context.PushClip(bounds))
            using (context.PushPreTransform(matrix))
            {
                if (fill != null)
                {
                    foreach (var rect in snapshot.Items)
                        context.FillRectangle(fill, rect);
                }

                if (stroke != null && snapshot.Curves.Length > 0)
                {
                    // Точка толщиной: после трансформации в пиксель карты.
                    var pen = new ImmutablePen(stroke, 1 / Math.Max(scale, 1e-9));
                    foreach (var curve in snapshot.Curves)
                        Polyline.Draw(context, pen, curve.Source, curve.SourceControl, curve.TargetControl, curve.Target, scale);
                }
            }
        }
    }
}

/// <summary>
/// Рамка видимой области поверх содержимого миникарты.
/// </summary>
internal sealed class MinimapFrame : Control
{
    private Rect _frame;
    private IBrush? _fill;
    private IBrush? _stroke;

    public MinimapFrame() => IsHitTestVisible = false;

    /// <summary>
    /// Сколько раз рамка рисовалась — для тестов.
    /// </summary>
    internal int Renders { get; private set; }

    public void Show(Rect frame, IBrush? fill, IBrush? stroke)
    {
        if (frame == _frame && ReferenceEquals(fill, _fill) && ReferenceEquals(stroke, _stroke))
            return;

        _frame = frame;
        _fill = fill;
        _stroke = stroke;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        Renders++;
        context.DrawRectangle(_fill, _stroke is { } stroke ? new Pen(stroke) : null, _frame);
    }
}

/// <summary>
/// Кубическая кривая ломаной — отрезков столько, сколько нужно её длине на экране (ADR 0011).
/// </summary>
internal static class Polyline
{
    /// <summary>
    /// Пикселей экрана на отрезок.
    /// </summary>
    private const double PixelsPerSegment = 12;

    /// <summary>
    /// Больше отрезков кривой не нужно ни на каком масштабе.
    /// </summary>
    private const int MaxSegments = 16;

    /// <summary>
    /// Сколько отрезков кривой при данном масштабе; ноль — кривая короче пикселя и не рисуется.
    /// </summary>
    public static int Segments(Point p0, Point p1, Point p2, Point p3, double scale)
    {
        // Длина ломаной по контрольным точкам — верхняя оценка длины кривой.
        var length = (Point.Distance(p0, p1) + Point.Distance(p1, p2) + Point.Distance(p2, p3)) * scale;
        if (length < 1)
            return 0;

        return Math.Clamp((int)Math.Ceiling(length / PixelsPerSegment), 1, MaxSegments);
    }

    /// <summary>
    /// Рисует кривую ломаной; возвращает число отрезков.
    /// </summary>
    public static int Draw(ImmediateDrawingContext context, ImmutablePen pen, Point p0, Point p1, Point p2, Point p3, double scale)
    {
        var segments = Segments(p0, p1, p2, p3, scale);
        var previous = p0;
        for (var i = 1; i <= segments; i++)
        {
            var t = (double)i / segments;
            var u = 1 - t;
            var point = new Point(
                (u * u * u * p0.X) + (3 * u * u * t * p1.X) + (3 * u * t * t * p2.X) + (t * t * t * p3.X),
                (u * u * u * p0.Y) + (3 * u * u * t * p1.Y) + (3 * u * t * t * p2.Y) + (t * t * t * p3.Y));
            context.DrawLine(pen, previous, point);
            previous = point;
        }

        return segments;
    }
}
