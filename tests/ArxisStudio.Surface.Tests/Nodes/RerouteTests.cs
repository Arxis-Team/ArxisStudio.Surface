using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface.Nodes;
using ArxisStudio.Surface.Nodes.States;

namespace ArxisStudio.Tests;

/// <summary>
/// Узел-перевалка (ADR 0005): вход и выход в одной точке, в центре кольца.
/// </summary>
/// <remarks>
/// Цепочка «узел 0 → перевалка (узел 1) → узел 2», узлы ставятся так, как их ставит хост, — до первой
/// раскладки, без заданного размера.
/// </remarks>
public class RerouteTests
{
    private static readonly IDataTemplate KnotAtOne = new FuncDataTemplate<string>((name, _) =>
        name == NodeStand.NodeName(1)
            ? new Reroute { Input = name + ".in", Output = name + ".out" }
            : NodeStand.PortedNode.Build(name)!, supportsRecycling: false);

    private static NodeStand Create() => NodeStand.CreatePlacedByHost(
        [new Point(100, 100), new Point(320, 180), new Point(480, 100)],
        KnotAtOne,
        new LinkData(NodeStand.Out(0), NodeStand.In(1)),
        new LinkData(NodeStand.Out(1), NodeStand.In(2)));

    private static Reroute KnotOf(NodeStand stand) =>
        stand.Node(1).GetVisualDescendants().OfType<Reroute>().Single();

    /// <summary>
    /// Центр кольца в мировых координатах, посчитанный независимо от редактора.
    /// </summary>
    private static Point CentreOf(NodeStand stand)
    {
        var knot = KnotOf(stand);
        var panel = (Visual)stand.Node(1).GetVisualParent()!;
        return knot.TranslatePoint(new Point(knot.Bounds.Width / 2, knot.Bounds.Height / 2), panel)!.Value;
    }

    private static Port PortOf(NodeStand stand, PortDirection direction) =>
        KnotOf(stand).GetVisualDescendants().OfType<Port>().Single(p => p.Direction == direction);

    [AvaloniaFact]
    public void Both_Links_Meet_At_The_Centre_Of_The_Knot()
    {
        var stand = Create();
        var centre = CentreOf(stand);

        Assert.Equal(centre, stand.LinkOf(stand.Links[0]).TargetAnchor);
        Assert.Equal(centre, stand.LinkOf(stand.Links[1]).SourceAnchor);
    }

    [AvaloniaFact]
    public void The_Knot_Takes_The_Card_Off_Its_Node()
    {
        var stand = Create();
        Assert.True(stand.Editor.TryFindResource("NodeEditor.Reroute.Size", out var value));
        var size = (double)value!;

        Assert.Contains(":reroute", stand.Node(1).Classes);
        Assert.DoesNotContain(":reroute", stand.Node(0).Classes);
        Assert.Equal(new Size(size, size), stand.Node(1).Bounds.Size);
    }

    [AvaloniaFact]
    public void Both_Ports_Of_The_Knot_Show_Their_Links()
    {
        var stand = Create();

        Assert.True(PortOf(stand, PortDirection.Input).IsConnected);
        Assert.True(PortOf(stand, PortDirection.Output).IsConnected);
    }

    [AvaloniaFact]
    public void Pressing_The_Ring_Drags_The_Knot_And_Its_Links()
    {
        var stand = Create();
        var ring = CentreOf(stand) + new Vector(8, 0);

        stand.Drag(ring, new Vector(40, 30));

        Assert.Equal(new Point(360, 210), stand.Node(1).Location);
        Assert.Equal(CentreOf(stand), stand.LinkOf(stand.Links[0]).TargetAnchor);
        Assert.Equal(CentreOf(stand), stand.LinkOf(stand.Links[1]).SourceAnchor);
    }

    [AvaloniaFact]
    public void Pressing_The_Centre_Starts_A_Link_From_The_Output()
    {
        var stand = Create();
        var centre = CentreOf(stand);
        var requests = new List<ConnectRequestedEventArgs>();
        stand.Editor.ConnectRequested += (_, e) => requests.Add(e);

        stand.Window.MouseMove(centre);
        stand.Window.MouseDown(centre, MouseButton.Left);
        Assert.IsType<PendingLinkState>(stand.Editor.CurrentState);

        var target = stand.PinCentreOf(NodeStand.In(0));
        stand.Window.MouseMove(centre + new Vector(-30, 40));
        stand.Window.MouseMove(target);
        stand.Window.MouseUp(target, MouseButton.Left);

        var request = Assert.Single(requests);
        Assert.Equal(NodeStand.Out(1), request.Source);
        Assert.Equal(NodeStand.In(0), request.Target);
        Assert.Equal(new Point(320, 180), stand.Node(1).Location);
    }

    [AvaloniaFact]
    public void The_Knot_Shows_The_Selection_Of_Its_Node()
    {
        var stand = Create();
        var knot = KnotOf(stand);
        var ring = CentreOf(stand) + new Vector(8, 0);

        stand.Window.MouseDown(ring, MouseButton.Left);
        stand.Window.MouseUp(ring, MouseButton.Left);
        Assert.Contains(":selected", knot.Classes);

        stand.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.DoesNotContain(":selected", knot.Classes);
    }
}
