using System;
using Avalonia;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Кривая связи: кубическая Безье из выхода во вход, и расстояния до неё.
/// </summary>
/// <remarks>
/// Касательные горизонтальны: из выхода связь уходит вправо, во вход приходит слева — так же, как
/// стоят штырьки портов. Плечо касательной — половина горизонтального расстояния, но не короче
/// <see cref="MinTangent"/>: иначе связь к узлу, стоящему левее, разворачивалась бы петлёй без
/// изгиба, прямо сквозь оба узла.
/// <para>
/// Расстояние до кривой меряется по ломаной из <see cref="Segments"/> отрезков. Арифметика не знает
/// ни контролов, ни редактора — точки приходят готовыми, в мировых координатах, — поэтому её потом
/// заменит пространственный индекс, не трогая её потребителей.
/// </para>
/// </remarks>
internal readonly struct LinkGeometry
{
    public const double MinTangent = 40;

    public const int Segments = 24;

    public LinkGeometry(Point source, Point target)
    {
        Source = source;
        Target = target;

        var reach = Math.Max(Math.Abs(target.X - source.X) / 2, MinTangent);
        SourceControl = source + new Vector(reach, 0);
        TargetControl = target - new Vector(reach, 0);
    }

    public Point Source { get; }

    public Point Target { get; }

    public Point SourceControl { get; }

    public Point TargetControl { get; }

    /// <summary>
    /// Точка кривой при параметре от 0 (источник) до 1 (цель).
    /// </summary>
    public Point At(double t)
    {
        var u = 1 - t;
        var a = u * u * u;
        var b = 3 * u * u * t;
        var c = 3 * u * t * t;
        var d = t * t * t;

        return new Point(
            (a * Source.X) + (b * SourceControl.X) + (c * TargetControl.X) + (d * Target.X),
            (a * Source.Y) + (b * SourceControl.Y) + (c * TargetControl.Y) + (d * Target.Y));
    }

    /// <summary>
    /// Рамка кривой. Безье лежит внутри выпуклой оболочки своих опорных точек, поэтому рамка по
    /// ним — верхняя оценка без обхода кривой.
    /// </summary>
    public Rect Bounds
    {
        get
        {
            var left = Math.Min(Math.Min(Source.X, Target.X), Math.Min(SourceControl.X, TargetControl.X));
            var top = Math.Min(Math.Min(Source.Y, Target.Y), Math.Min(SourceControl.Y, TargetControl.Y));
            var right = Math.Max(Math.Max(Source.X, Target.X), Math.Max(SourceControl.X, TargetControl.X));
            var bottom = Math.Max(Math.Max(Source.Y, Target.Y), Math.Max(SourceControl.Y, TargetControl.Y));
            return new Rect(left, top, right - left, bottom - top);
        }
    }

    /// <summary>
    /// Расстояние от точки до кривой в мировых единицах.
    /// </summary>
    public double DistanceTo(Point point) => Nearest(point, out _);

    /// <summary>
    /// Параметр ближайшей к точке точки кривой: 0 — у источника, 1 — у цели.
    /// </summary>
    public double ParameterAt(Point point)
    {
        Nearest(point, out var t);
        return t;
    }

    private double Nearest(Point point, out double parameter)
    {
        var best = double.MaxValue;
        parameter = 0;
        var previous = Source;

        for (var i = 1; i <= Segments; i++)
        {
            var t1 = (double)i / Segments;
            var next = At(t1);
            var (distance, along) = DistanceToSegment(point, previous, next);

            if (distance < best)
            {
                best = distance;
                parameter = (i - 1 + along) / Segments;
            }

            previous = next;
        }

        return best;
    }

    private static (double Distance, double Along) DistanceToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        var lengthSquared = (ab.X * ab.X) + (ab.Y * ab.Y);
        var along = lengthSquared <= double.Epsilon
            ? 0
            : Math.Clamp((((p.X - a.X) * ab.X) + ((p.Y - a.Y) * ab.Y)) / lengthSquared, 0, 1);

        var closest = a + (ab * along);
        var dx = p.X - closest.X;
        var dy = p.Y - closest.Y;
        return (Math.Sqrt((dx * dx) + (dy * dy)), along);
    }
}
