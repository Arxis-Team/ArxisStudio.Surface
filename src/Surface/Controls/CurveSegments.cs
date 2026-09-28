using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using SkiaSharp;

namespace ArxisStudio.Surface;

/// <summary>
/// Кубическая кривая ломаной — отрезков столько, сколько нужно её длине на экране (ADR 0011).
/// </summary>
/// <remarks>
/// Общая у слоёв, которые рисуют кривые своей операцией: связей упрощённого вида и миникарты.
/// </remarks>
internal static class CurveSegments
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
    public static int Count(Point p0, Point p1, Point p2, Point p3, double scale)
    {
        // Длина ломаной по контрольным точкам — верхняя оценка длины кривой.
        var length = (Point.Distance(p0, p1) + Point.Distance(p1, p2) + Point.Distance(p2, p3)) * scale;
        if (length < 1)
            return 0;

        return Math.Clamp((int)Math.Ceiling(length / PixelsPerSegment), 1, MaxSegments);
    }

    /// <summary>
    /// Точка кривой при параметре <paramref name="t"/>.
    /// </summary>
    public static Point At(Point p0, Point p1, Point p2, Point p3, double t)
    {
        var u = 1 - t;
        return new Point(
            (u * u * u * p0.X) + (3 * u * u * t * p1.X) + (3 * u * t * t * p2.X) + (t * t * t * p3.X),
            (u * u * u * p0.Y) + (3 * u * u * t * p1.Y) + (3 * u * t * t * p2.Y) + (t * t * t * p3.Y));
    }

    /// <summary>
    /// Рисует кривую ломаной через контекст — запасной путь; возвращает число отрезков.
    /// </summary>
    public static int Draw(ImmediateDrawingContext context, ImmutablePen pen, Point p0, Point p1, Point p2, Point p3, double scale)
    {
        var segments = Count(p0, p1, p2, p3, scale);
        var previous = p0;
        for (var i = 1; i <= segments; i++)
        {
            var point = At(p0, p1, p2, p3, (double)i / segments);
            context.DrawLine(pen, previous, point);
            previous = point;
        }

        return segments;
    }

    /// <summary>
    /// Добавляет кривую ломаной в путь Skia — весь путь потом рисуется одним вызовом; возвращает число
    /// отрезков.
    /// </summary>
    public static int Append(SKPath path, Point p0, Point p1, Point p2, Point p3, double scale)
    {
        var segments = Count(p0, p1, p2, p3, scale);
        if (segments == 0)
            return 0;

        path.MoveTo((float)p0.X, (float)p0.Y);
        for (var i = 1; i <= segments; i++)
        {
            var point = At(p0, p1, p2, p3, (double)i / segments);
            path.LineTo((float)point.X, (float)point.Y);
        }

        return segments;
    }
}
