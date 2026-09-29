using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface.Nodes;

namespace ArxisStudio.Tests;

/// <summary>
/// Связи идут между портами по их данным и следуют за узлами (ADR 0004).
/// </summary>
/// <remarks>
/// Концы связи сверяются с концами портов, посчитанными независимо: центр штырька в координатах
/// панели узлов. Узел тянут за шапку — там, где нет портов.
/// </remarks>
public class LinkTests
{
    private static NodeStand Create(params Point[] locations) =>
        NodeStand.Create(locations, itemTemplate: NodeStand.PortedNode);

    private static void AssertEndsOnPorts(NodeStand stand, Link link, int from, int to)
    {
        Assert.True(link.IsVisible, "Связь с обоими портами на поверхности обязана быть видна.");
        Assert.Equal(stand.PinCentreInWorld(from, PortDirection.Output), link.SourceAnchor);
        Assert.Equal(stand.PinCentreInWorld(to, PortDirection.Input), link.TargetAnchor);
    }

    [AvaloniaFact]
    public void Link_Ends_Sit_On_Port_Anchors()
    {
        var stand = Create(new Point(100, 100), new Point(400, 200));
        var data = stand.Connect(0, 1);

        AssertEndsOnPorts(stand, stand.LinkOf(data), 0, 1);
    }

    [AvaloniaFact]
    public void A_Link_Takes_Only_Its_Curve_On_The_Canvas()
    {
        // Прямоугольник связи — прямоугольник её кривой, а не весь холст: по нему считаются
        // области перерисовки.
        var stand = Create(new Point(100, 100), new Point(400, 200));
        var link = stand.LinkOf(stand.Connect(0, 1));

        Assert.Equal(link.WorldBounds, link.Bounds);
        Assert.True(link.Bounds.Contains(link.SourceAnchor) && link.Bounds.Contains(link.TargetAnchor));
    }

    [AvaloniaFact]
    public void A_New_Link_Curve_Reshapes_Every_Wire()
    {
        // LinkCurve редактора пересчитывает кривые сразу, без сдвига узлов.
        var stand = Create(new Point(100, 100), new Point(400, 200));
        var link = stand.LinkOf(stand.Connect(0, 1));
        var before = link.Geometry.SourceControl.X - link.Geometry.Source.X;

        stand.Editor.LinkCurve = new LinkCurve { ForwardHorizontalFactor = 0, ForwardVerticalFactor = 0 };
        stand.RunLayout();

        Assert.True(before > 0, "у провода вперёд плечо положительное");
        Assert.Equal(0, link.Geometry.SourceControl.X - link.Geometry.Source.X, 6);
    }

    [AvaloniaFact]
    public void A_Link_Follows_A_Dragged_Node()
    {
        var stand = Create(new Point(100, 100), new Point(400, 200));
        var link = stand.LinkOf(stand.Connect(0, 1));
        var targetBefore = link.TargetAnchor;

        stand.Drag(stand.GripOf(0), new Vector(60, 40));

        Assert.Equal(new Point(160, 140), stand.Node(0).Location);
        AssertEndsOnPorts(stand, link, 0, 1);
        Assert.Equal(targetBefore, link.TargetAnchor);
    }

    [AvaloniaFact]
    public void A_Link_Follows_A_Group_Drag()
    {
        var stand = Create(new Point(100, 100), new Point(400, 200));
        var link = stand.LinkOf(stand.Connect(0, 1));

        stand.Editor.Focus();
        stand.Window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        stand.Drag(stand.GripOf(0), new Vector(30, 20));

        Assert.Equal(new Point(430, 220), stand.Node(1).Location);
        AssertEndsOnPorts(stand, link, 0, 1);
    }

    [AvaloniaFact]
    public void A_Link_Appears_When_Its_Node_Arrives_Later()
    {
        var stand = Create(new Point(100, 100));
        var data = new LinkData(NodeStand.Out(0), NodeStand.In(1));
        stand.Links.Add(data);
        stand.RunLayout();

        Assert.False(stand.LinkOf(data).IsVisible, "Связь без второго порта рисоваться не должна.");

        stand.Items.Add(NodeStand.NodeName(1));
        stand.RunLayout();
        stand.Node(1).Width = NodeStand.NodeSize.Width;
        stand.Node(1).Height = NodeStand.NodeSize.Height;
        stand.Node(1).Location = new Point(400, 200);
        stand.RunLayout();

        AssertEndsOnPorts(stand, stand.LinkOf(data), 0, 1);
    }

    [AvaloniaFact]
    public void A_Link_Hides_When_Its_Port_Leaves()
    {
        var stand = Create(new Point(100, 100), new Point(400, 200));
        var data = stand.Connect(0, 1);

        stand.Items.RemoveAt(1);
        stand.RunLayout();

        Assert.False(stand.LinkOf(data).IsVisible, "Связь, у которой ушёл порт, рисоваться не должна.");
    }

    [AvaloniaFact]
    public void Zoom_Does_Not_Move_Link_Ends()
    {
        var stand = Create(new Point(100, 100), new Point(400, 200));
        var link = stand.LinkOf(stand.Connect(0, 1));
        var source = link.SourceAnchor;

        stand.Editor.ViewportZoom = 2;
        stand.Editor.ViewportLocation = new Point(50, -20);
        stand.RunLayout();

        Assert.Equal(source, link.SourceAnchor);
    }

    [AvaloniaFact]
    public void Links_Can_Be_Link_Controls()
    {
        // Готовая связь в коллекции: её концы задал тот, кто её создал, и привязки редактора
        // её не трогают.
        var stand = Create(new Point(100, 100), new Point(400, 200));
        var link = new Link { Source = NodeStand.Out(0), Target = NodeStand.In(1) };

        stand.Links.Add(link);
        stand.RunLayout();

        AssertEndsOnPorts(stand, link, 0, 1);
    }

    [AvaloniaFact]
    public void Connected_Ports_Show_It()
    {
        var stand = Create(new Point(100, 100), new Point(400, 200));
        var data = stand.Connect(0, 1);

        Assert.True(stand.PortOf(0, PortDirection.Output).IsConnected);
        Assert.True(stand.PortOf(1, PortDirection.Input).IsConnected);
        Assert.False(stand.PortOf(0, PortDirection.Input).IsConnected);
        Assert.Contains(":connected", stand.PortOf(0, PortDirection.Output).Classes);

        stand.Links.Remove(data);
        stand.RunLayout();
        Assert.False(stand.PortOf(0, PortDirection.Output).IsConnected);
    }

    [AvaloniaFact]
    public void Moving_A_Node_Updates_Only_Its_Own_Links()
    {
        // Сдвиг узла пересчитывает связи его портов — столько, какова его степень, — а не все
        // связи графа.
        var stand = Create(new Point(100, 100), new Point(300, 100), new Point(500, 100), new Point(700, 100));
        stand.Connect(0, 1);
        stand.Connect(1, 2);
        stand.Connect(2, 3);

        var before = stand.Editor.LinkUpdates;
        stand.Node(3).Location = new Point(700, 300);
        Assert.Equal(1, stand.Editor.LinkUpdates - before);

        before = stand.Editor.LinkUpdates;
        stand.Node(1).Location = new Point(300, 300);
        Assert.Equal(2, stand.Editor.LinkUpdates - before);
    }

    [AvaloniaFact]
    public void A_Drag_Frame_Costs_The_Node_Degree_Once()
    {
        // Настоящий кадр перетаскивания — сдвиг и проход раскладки. Сдвиг узла меняет и его
        // границы, и слушай редактор их тоже, каждая связь пересчитывалась бы за кадр дважды.
        var stand = Create(new Point(100, 100), new Point(400, 100));
        stand.Connect(0, 1);

        var from = stand.GripOf(1);
        stand.Window.MouseMove(from);
        stand.Window.MouseDown(from, MouseButton.Left);
        stand.Window.MouseMove(from + new Vector(10, 0));
        stand.RunLayout();

        var before = stand.Editor.LinkUpdates;
        stand.Window.MouseMove(from + new Vector(30, 10));
        stand.RunLayout();
        var frame = stand.Editor.LinkUpdates - before;

        stand.Window.MouseUp(from + new Vector(30, 10), MouseButton.Left);
        Assert.Equal(1, frame);
    }
}
