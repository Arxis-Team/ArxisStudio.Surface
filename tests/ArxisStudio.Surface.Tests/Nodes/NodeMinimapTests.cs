using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Xunit;
using ArxisStudio.Surface.Editing;

namespace ArxisStudio.Tests;

/// <summary>
/// Редактор узлов дорисовывает на миникарте инструментов свои связи (ADR 0005).
/// </summary>
public class NodeMinimapTests
{
    [AvaloniaFact]
    public void The_Minimap_Draws_The_Resolved_Links()
    {
        // Две настоящие связи и одна в порт, которого на поверхности нет: на холсте её не видно —
        // не должно быть и на карте.
        var stand = NodeStand.Create(
            [new Point(100, 100), new Point(400, 200), new Point(100, 380)],
            itemTemplate: NodeStand.PortedNode);
        stand.Connect(0, 1);
        stand.Connect(2, 1);
        stand.Links.Add(new LinkData(NodeStand.Out(0), "нет такого порта"));

        stand.Window.Content = null;
        var map = new SurfaceMinimap
        {
            Editor = stand.Editor,
            Width = 200,
            Height = 150,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        stand.Window.Content = new Grid { Children = { stand.Editor, map } };
        stand.RunLayout();
        stand.Window.CaptureRenderedFrame();

        Assert.True(map.RenderCount > 0);
        Assert.Equal(2, stand.Editor.LinksOnMinimap);
    }
}
