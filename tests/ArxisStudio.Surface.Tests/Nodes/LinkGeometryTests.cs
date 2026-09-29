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
    public void A_Forward_Wire_Bends_By_Both_Axes()
    {
        // Правило Blueprint (ADR 0015): касательная вперёд — |dx| + |dy|, плечо — её треть. Почти
        // вертикальный провод изгибается плавно, а не идёт прямо с изломом у порта.
        var g = new LinkGeometry(new Point(0, 0), new Point(30, 270));

        Assert.Equal(100, g.SourceControl.X - g.Source.X, Precision);
        Assert.Equal(100, g.Target.X - g.TargetControl.X, Precision);
    }

    [Fact]
    public void A_Backward_Wire_Loops_Wide()
    {
        // Назад — 3 · min(|dx|, 200) + 1,5 · min(|dy|, 200): петля обходит узлы, а не делает шпильку у
        // порта. Вход вплотную слева — не короче наименьшего плеча.
        var g = new LinkGeometry(new Point(200, 0), new Point(50, 170));
        Assert.Equal(((3 * 150) + (1.5 * 170)) / 3, g.SourceControl.X - g.Source.X, Precision);

        var tight = new LinkGeometry(new Point(200, 0), new Point(195, 0));
        Assert.Equal(LinkGeometry.MinTangent, tight.SourceControl.X - tight.Source.X, Precision);
    }

    [Fact]
    public void The_Tangent_Stops_Growing_Past_Its_Range()
    {
        var forward = new LinkGeometry(new Point(0, 0), new Point(5000, 0));
        Assert.Equal(1000.0 / 3, forward.SourceControl.X - forward.Source.X, Precision);

        var backward = new LinkGeometry(new Point(5000, 0), new Point(0, 5000));
        Assert.Equal(((3 * 200) + (1.5 * 200)) / 3, backward.SourceControl.X - backward.Source.X, Precision);
    }

    [Fact]
    public void A_Reversed_Source_Is_Forward_Towards_The_Left()
    {
        // Развёрнутый узел перенаправления выходит влево: вход левее — провод вперёд, короткое плечо.
        var g = new LinkGeometry(new Point(200, 0), new Point(50, 0), sourceReversed: true);

        Assert.Equal(-50, g.SourceControl.X - g.Source.X, Precision);
    }

    [Fact]
    public void The_Host_Curve_Replaces_The_Numbers()
    {
        var curve = new LinkCurve { ForwardHorizontalFactor = 3, ForwardVerticalFactor = 0 };
        var g = new LinkGeometry(new Point(0, 0), new Point(100, 400), curve: curve);

        Assert.Equal(100, g.SourceControl.X - g.Source.X, Precision);
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

    [Fact]
    public void A_Segment_Across_The_Curve_Intersects_It_And_One_Beside_Does_Not()
    {
        var g = new LinkGeometry(new Point(0, 0), new Point(200, 100));

        Assert.True(g.Intersects(new Point(60, -20), new Point(60, 120)));
        Assert.False(g.Intersects(new Point(150, -20), new Point(190, -5)));
    }

    [Fact]
    public void A_Segment_Through_A_Vertex_Of_The_Polyline_Intersects()
    {
        // Середина симметричной кривой — вершина ломаной, по которой считается пересечение: со
        // строгим сравнением отрезок ровно через неё не пересекал ни одного из двух соседних отрезков.
        var g = new LinkGeometry(new Point(0, 0), new Point(200, 100));
        Assert.Equal(new Point(100, 50), g.At(0.5));

        Assert.True(g.Intersects(new Point(100, -20), new Point(100, 120)));
    }

    [Fact]
    public void A_Segment_Along_A_Straight_Link_Does_Not_Cut_It()
    {
        var flat = new LinkGeometry(new Point(0, 0), new Point(200, 0));

        Assert.False(flat.Intersects(new Point(20, 0), new Point(180, 0)));
    }
}
