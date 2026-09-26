using Avalonia;
using Xunit;
using ArxisStudio.Surface.Nodes;

namespace ArxisStudio.Tests;

/// <summary>
/// Арифметика кривой связи — без контролов и редактора.
/// </summary>
public class LinkGeometryTests
{
    private const int Precision = 6;

    [Fact]
    public void The_Curve_Runs_From_Source_To_Target()
    {
        var g = new LinkGeometry(new Point(0, 0), new Point(200, 100));

        Assert.Equal(new Point(0, 0), g.At(0));
        Assert.Equal(new Point(200, 100), g.At(1));
    }

    [Fact]
    public void Tangents_Leave_Rightwards_And_Arrive_From_The_Left()
    {
        var g = new LinkGeometry(new Point(0, 0), new Point(200, 100));

        Assert.Equal(g.Source.Y, g.SourceControl.Y, Precision);
        Assert.Equal(g.Target.Y, g.TargetControl.Y, Precision);
        Assert.True(g.SourceControl.X > g.Source.X);
        Assert.True(g.TargetControl.X < g.Target.X);
    }

    [Fact]
    public void A_Target_To_The_Left_Still_Gets_A_Bend()
    {
        // Вход левее выхода: половина расстояния по горизонтали дала бы петлю без изгиба прямо
        // сквозь узлы — плечо не короче наименьшего.
        var g = new LinkGeometry(new Point(200, 0), new Point(190, 100));

        Assert.Equal(LinkGeometry.MinTangent, g.SourceControl.X - g.Source.X, Precision);
        Assert.Equal(LinkGeometry.MinTangent, g.Target.X - g.TargetControl.X, Precision);
    }

    [Fact]
    public void Distance_Is_Zero_On_The_Curve_And_Grows_Off_It()
    {
        var g = new LinkGeometry(new Point(0, 0), new Point(200, 100));
        var onCurve = g.At(0.5);

        Assert.True(g.DistanceTo(onCurve) < 0.5, $"Точка кривой обязана лежать на ней, расстояние {g.DistanceTo(onCurve)}.");

        // Концы на одной высоте — кривая вырождается в отрезок y = 0, и расстояние до него
        // известно точно. На наклонной кривой точка «на 40 ниже» ближе 40: кратчайшее — по нормали.
        var flat = new LinkGeometry(new Point(0, 0), new Point(200, 0));
        Assert.Equal(40, flat.DistanceTo(new Point(100, 40)), Precision);
    }

    [Fact]
    public void The_Parameter_Tells_Which_End_Is_Nearer()
    {
        var g = new LinkGeometry(new Point(0, 0), new Point(200, 100));

        Assert.True(g.ParameterAt(g.At(0.2)) < 0.5);
        Assert.True(g.ParameterAt(g.At(0.8)) > 0.5);
    }

    [Fact]
    public void Bounds_Hold_The_Whole_Curve()
    {
        var g = new LinkGeometry(new Point(0, 0), new Point(200, 100));

        for (var i = 0; i <= 20; i++)
            Assert.True(g.Bounds.Inflate(0.001).Contains(g.At(i / 20.0)));
    }
}
