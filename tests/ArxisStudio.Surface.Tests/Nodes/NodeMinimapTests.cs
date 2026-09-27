using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Xunit;
using ArxisStudio.Surface.Editing;
using ArxisStudio.Surface.Nodes;

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

    [AvaloniaFact]
    public void A_Removed_Link_Leaves_The_Map()
    {
        // Ни узел, ни другая связь не меняются: об ушедшей связи карте говорит её уход.
        var stand = NodeStand.Create(
            [new Point(100, 100), new Point(400, 200), new Point(100, 380)],
            itemTemplate: NodeStand.PortedNode);
        stand.Connect(0, 1);
        var gone = stand.Connect(2, 1);

        stand.Window.Content = null;
        var map = new SurfaceMinimap { Editor = stand.Editor, Width = 200, Height = 150 };
        var clock = MinimapClock.On(map);
        stand.Window.Content = new Grid { Children = { stand.Editor, map } };
        stand.RunLayout();
        stand.Window.CaptureRenderedFrame();
        Assert.Equal(2, stand.Editor.LinksOnMinimap);
        clock.PassInterval();

        stand.Links.Remove(gone);
        stand.RunLayout();
        stand.Window.CaptureRenderedFrame();

        Assert.Equal(1, stand.Editor.LinksOnMinimap);
    }

    [AvaloniaFact]
    public void A_Link_Moved_To_Another_Port_Rebuilds_The_Map()
    {
        // Перецепленная связь меняет только себя — ни один узел не сдвинулся, — и сообщить карте о
        // новой кривой обязан её пересчёт. На месте концы меняет хост у готовой связи из коллекции;
        // у созданной редактором их даёт модель.
        var stand = NodeStand.Create(
            [new Point(100, 100), new Point(400, 200), new Point(400, 380)],
            itemTemplate: NodeStand.PortedNode);
        var link = new Link { Source = NodeStand.Out(0), Target = NodeStand.In(1) };
        stand.Links.Add(link);
        stand.RunLayout();

        stand.Window.Content = null;
        var map = new SurfaceMinimap { Editor = stand.Editor, Width = 200, Height = 150 };
        var clock = MinimapClock.On(map);
        stand.Window.Content = new Grid { Children = { stand.Editor, map } };
        stand.RunLayout();
        stand.Window.CaptureRenderedFrame();
        var rebuilt = map.ContentRebuilds;
        clock.PassInterval();

        link.Target = NodeStand.In(2);
        stand.RunLayout();
        stand.Window.CaptureRenderedFrame();

        Assert.Equal(rebuilt + 1, map.ContentRebuilds);
    }
}
