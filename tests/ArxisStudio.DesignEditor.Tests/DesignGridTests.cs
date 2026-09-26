using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ArxisStudio.Controls;
using Xunit;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Фоновая сетка редактора.
/// </summary>
public class DesignGridTests
{
    private static DesignGrid Grid(EditorHarness harness) =>
        harness.Editor.GetVisualDescendants().OfType<DesignGrid>().Single();

    [AvaloniaFact]
    public void Grid_Is_Part_Of_The_Editor_Template()
    {
        var harness = EditorHarness.Create();

        var grid = Grid(harness);

        Assert.True(harness.Editor.ShowGrid);
        Assert.True(grid.IsVisible);

        // Сетка не должна перехватывать ввод: под ней работают marquee и pan.
        Assert.False(grid.IsHitTestVisible);
    }

    [AvaloniaFact]
    public void Grid_Follows_The_Viewport()
    {
        var harness = EditorHarness.Create();
        harness.Editor.ViewportLocation = new Point(140, 60);
        harness.Editor.ViewportZoom = 2.5;
        harness.RunLayout();

        var grid = Grid(harness);

        Assert.Equal(new Point(140, 60), grid.ViewportLocation);
        Assert.Equal(2.5, grid.ViewportZoom, 6);
    }

    [AvaloniaFact]
    public void ShowGrid_Toggles_The_Grid()
    {
        var harness = EditorHarness.Create();

        harness.Editor.ShowGrid = false;
        harness.RunLayout();

        Assert.False(Grid(harness).IsVisible);
    }

    [AvaloniaTheory]
    // Шаг 20 мировых единиц при разном масштабе против порога в 6 пикселей.
    [InlineData(20, 1.0, 6.0, true)]     // 20 px — видно
    [InlineData(20, 0.3, 6.0, true)]     // 6 px — ровно на границе
    [InlineData(20, 0.25, 6.0, false)]   // 5 px — скрыто
    [InlineData(20, 0.05, 6.0, false)]   // 1 px — скрыто
    public void Level_Is_Hidden_When_Denser_Than_The_Threshold(
        double cellSize, double zoom, double minCellSize, bool expected)
    {
        Assert.Equal(expected, DesignGrid.IsLevelVisible(cellSize * zoom, minCellSize));
    }

    [AvaloniaFact]
    public void Major_Level_Survives_Zoom_That_Hides_The_Minor_One()
    {
        // Именно это отличает рендер от плиточной кисти: на отдалении остаётся
        // разреженная сетка вместо сплошной заливки.
        const double cell = 20;
        const int major = 5;
        const double zoom = 0.26;
        const double threshold = 6;

        Assert.False(DesignGrid.IsLevelVisible(cell * zoom, threshold));
        Assert.True(DesignGrid.IsLevelVisible(cell * major * zoom, threshold));
    }

    [AvaloniaTheory]
    [InlineData(10.3, 1.0, 10.5)]
    [InlineData(10.9, 1.0, 10.5)]
    [InlineData(11.0, 1.0, 11.5)]
    [InlineData(10.3, 2.0, 10.25)]
    [InlineData(10.3, 1.5, 10.3333333333)]
    public void Snapping_Centres_The_Line_On_A_Device_Pixel(double value, double scaling, double expected)
    {
        // Линия толщиной в один пиксель должна попадать в центр пикселя устройства,
        // иначе она размазывается на два соседних с половинной интенсивностью.
        Assert.Equal(expected, DesignGrid.SnapToDevicePixel(value, scaling), 6);
    }

    [AvaloniaFact]
    public void Snapped_Line_Covers_Exactly_One_Device_Pixel()
    {
        foreach (var scaling in new[] { 1.0, 1.5, 2.0 })
        {
            var snapped = DesignGrid.SnapToDevicePixel(37.42, scaling);
            var thicknessDip = 1.0 / scaling;

            var startDevice = (snapped - (thicknessDip / 2)) * scaling;
            var endDevice = (snapped + (thicknessDip / 2)) * scaling;

            Assert.Equal(1.0, endDevice - startDevice, 6);
            Assert.Equal(Math.Round(startDevice), startDevice, 6);
        }
    }
}
