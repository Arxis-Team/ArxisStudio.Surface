using System;
using Avalonia;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Кривая связи как ломаная с накопленной длиной: точка и направление по длине от выхода (ADR 0014).
/// </summary>
/// <remarks>
/// Ломаная — та же, по которой меряются расстояние и разрез (<see cref="LinkGeometry.Segments"/>
/// отрезков). Направление — отрезка, на котором лежит точка: у стрелки на изгибе оно и есть касательная
/// с точностью до двадцать четвёртой части кривой.
/// </remarks>
internal sealed class LinkPath
{
    private readonly Point[] _points = new Point[LinkGeometry.Segments + 1];
    private readonly double[] _lengths = new double[LinkGeometry.Segments + 1];

    public LinkPath(LinkGeometry geometry)
    {
        _points[0] = geometry.Source;
        for (var i = 1; i <= LinkGeometry.Segments; i++)
        {
            _points[i] = geometry.At((double)i / LinkGeometry.Segments);
            var step = _points[i] - _points[i - 1];
            _lengths[i] = _lengths[i - 1] + Math.Sqrt((step.X * step.X) + (step.Y * step.Y));
        }
    }

    /// <summary>
    /// Длина ломаной в мировых единицах.
    /// </summary>
    public double Length => _lengths[LinkGeometry.Segments];

    /// <summary>
    /// Точка на расстоянии <paramref name="distance"/> от выхода и направление провода в ней —
    /// единичный вектор к входу.
    /// </summary>
    public (Point Point, Vector Direction) At(double distance)
    {
        var s = Math.Clamp(distance, 0, Length);
        var i = Array.BinarySearch(_lengths, s);
        if (i < 0)
            i = ~i;

        // Отрезок, на котором лежит точка: от i − 1 до i; у самого выхода — первый.
        i = Math.Clamp(i, 1, LinkGeometry.Segments);
        var a = _points[i - 1];
        var b = _points[i];
        var span = _lengths[i] - _lengths[i - 1];
        var along = span > double.Epsilon ? (s - _lengths[i - 1]) / span : 0;
        var d = b - a;
        var length = Math.Sqrt((d.X * d.X) + (d.Y * d.Y));
        var direction = length > double.Epsilon ? d / length : new Vector(1, 0);
        return (a + (d * along), direction);
    }
}
