using Avalonia;
using ArxisStudio.Surface;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Сетка ячеек слоёв упрощённого вида (ADR 0011): запрос отдаёт ровно те элементы, чей прямоугольник
/// пересекает область, — каждый один раз, сколько бы ячеек он ни занял.
/// </summary>
public class CellGridTests
{
    private static int[] ByHand(Rect[] rects, Rect world) =>
        Enumerable.Range(0, rects.Length).Where(i => rects[i].Intersects(world)).ToArray();

    private static int[] ByGrid(CellGrid grid, Rect world)
    {
        var found = new List<int>();
        foreach (var i in grid.Within(world))
            found.Add(i);

        return found.ToArray();
    }

    [Fact]
    public void A_Query_Finds_Each_Crossing_Rectangle_Once()
    {
        // Мелкие прямоугольники вперемешку с длинными — длинный лежит во многих ячейках, как связь
        // из конца ряда в начало следующего.
        var random = new Random(11);
        var rects = new Rect[3000];
        for (var i = 0; i < rects.Length; i++)
        {
            var at = new Point(random.NextDouble() * 20000, random.NextDouble() * 12000);
            rects[i] = i % 50 == 0
                ? new Rect(at, new Size(random.NextDouble() * 15000, 40 + (random.NextDouble() * 3000)))
                : new Rect(at, new Size(20 + (random.NextDouble() * 200), 20 + (random.NextDouble() * 120)));
        }

        var grid = CellGrid.Build(rects);
        for (var q = 0; q < 200; q++)
        {
            var world = new Rect(
                new Point((random.NextDouble() * 26000) - 3000, (random.NextDouble() * 16000) - 2000),
                new Size(random.NextDouble() * 8000, random.NextDouble() * 6000));

            var found = ByGrid(grid, world);
            Assert.Equal(found.Length, found.Distinct().Count());
            Assert.Equal(ByHand(rects, world), found.Order().ToArray());
        }
    }

    [Fact]
    public void A_Query_Outside_Finds_Nothing_And_An_Empty_Grid_Answers()
    {
        var grid = CellGrid.Build([new Rect(0, 0, 100, 60), new Rect(150, 0, 100, 60)]);

        Assert.Empty(ByGrid(grid, new Rect(-500, -500, 100, 100)));
        Assert.Empty(ByGrid(grid, new Rect(1000, 1000, 100, 100)));
        Assert.Equal([0, 1], ByGrid(grid, new Rect(-10, -10, 1000, 1000)));
        Assert.Empty(ByGrid(CellGrid.Build([]), new Rect(0, 0, 100, 100)));
    }
}
