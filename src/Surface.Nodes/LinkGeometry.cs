using System;
using Avalonia;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Кривая связи: кубическая Безье из выхода во вход, и расстояния до неё.
/// </summary>
/// <remarks>
/// Касательные горизонтальны: из выхода связь уходит вправо, во вход приходит слева — так же, как
/// стоят штырьки портов. Развёрнутый конец — у развёрнутого узла перенаправления (ADR 0013) — смотрит
/// в обратную сторону: из выхода влево, во вход справа.
/// <para>
/// Длина касательной — по правилу Blueprint (<see cref="LinkCurve"/>, ADR 0015): от расстояния по обеим
/// осям, отдельно для провода вперёд и назад; плечо кривой — треть эрмитовой касательной. Вперёд — вход
/// лежит по ходу выхода: правее обычного выхода или левее развёрнутого. Плечо провода назад не короче
/// <see cref="MinTangent"/>: иначе связь к узлу вплотную слева шла бы без петли, прямо сквозь оба узла.
/// </para>
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

    public LinkGeometry(Point source, Point target, bool sourceReversed = false, bool targetReversed = false, LinkCurve? curve = null)
    {
        Source = source;
        Target = target;

        var c = curve ?? LinkCurve.Default;
        var dx = Math.Abs(target.X - source.X);
        var dy = Math.Abs(target.Y - source.Y);
        var forward = (sourceReversed ? source.X - target.X : target.X - source.X) >= 0;
        var tangent = forward
            ? (Math.Min(dx, c.ForwardHorizontalRange) * c.ForwardHorizontalFactor) + (Math.Min(dy, c.ForwardVerticalRange) * c.ForwardVerticalFactor)
            : (Math.Min(dx, c.BackwardHorizontalRange) * c.BackwardHorizontalFactor) + (Math.Min(dy, c.BackwardVerticalRange) * c.BackwardVerticalFactor);

        var reach = forward ? tangent / 3 : Math.Max(tangent / 3, MinTangent);
        SourceControl = source + new Vector(sourceReversed ? -reach : reach, 0);
        TargetControl = target - new Vector(targetReversed ? -reach : reach, 0);
    }

    public Point Source { get; }

    public Point Target { get; }

    public Point SourceControl { get; }

    public Point TargetControl { get; }

    /// <summary>
    /// Ломаная кривой с накопленной длиной — для хода по длине провода, а не по параметру: параметр
    /// кривой Безье идёт по ней неравномерно, и фигуры бежали бы то быстрее, то медленнее (ADR 0014).
    /// </summary>
    public LinkPath Path() => new(this);

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

    /// <summary>
    /// Пересекает ли отрезок кривую — по той же ломаной, по которой меряется расстояние.
    /// </summary>
    /// <param name="a">Начало отрезка в мировых координатах.</param>
    /// <param name="b">Конец отрезка в мировых координатах.</param>
    public bool Intersects(Point a, Point b)
    {
        var previous = Source;
        for (var i = 1; i <= Segments; i++)
        {
            var next = At((double)i / Segments);
            if (SegmentsCross(a, b, previous, next))
                return true;

            previous = next;
        }

        return false;
    }

    /// <summary>
    /// Лежат ли концы каждого отрезка по разные стороны прямой другого.
    /// </summary>
    /// <remarks>
    /// Точка на самой прямой считается лежащей по положительную сторону. Со строгим сравнением
    /// отрезок, прошедший ровно через вершину ломаной, не пересекал ни одного из двух её отрезков —
    /// у симметричной связи середина и есть вершина, и разрез через неё связь не резал. Так касание
    /// засчитывается ровно один раз, а наложение на одной прямой — ни разу: разрез, прошедший вдоль
    /// связи, её не режет.
    /// </remarks>
    private static bool SegmentsCross(Point p1, Point p2, Point q1, Point q2) =>
        Side(p1, p2, q1) != Side(p1, p2, q2) && Side(q1, q2, p1) != Side(q1, q2, p2);

    private static bool Side(Point a, Point b, Point c) => Cross(b - a, c - a) >= 0;

    private static double Cross(Vector a, Vector b) => (a.X * b.Y) - (a.Y * b.X);

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
