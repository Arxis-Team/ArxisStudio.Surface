using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface.Nodes;
using ArxisStudio.Surface.States;

namespace ArxisStudio.Tests;

/// <summary>
/// Двойной щелчок по связи просит разрезать её узлом перенаправления (ADR 0005).
/// </summary>
/// <remarks>
/// Связь из узла 0 во вход узла 1. В безголовом режиме время стоит, поэтому два нажатия в одной
/// точке и есть двойной щелчок.
/// </remarks>
public class LinkSplitTests
{
    /// <summary>
    /// Узел 3 — узел перенаправления, как его поставил бы хост; остальные — обычные узлы с портами.
    /// </summary>
    private static readonly IDataTemplate KnotAtThree = new FuncDataTemplate<string>((name, _) =>
        name == NodeStand.NodeName(3)
            ? new Reroute { Input = name + ".in", Output = name + ".out" }
            : NodeStand.PortedNode.Build(name)!, supportsRecycling: false);

    private sealed record Stand(NodeStand Nodes, LinkData First)
    {
        public NodeEditor Editor => Nodes.Editor;

        public Link Link => Nodes.LinkOf(First);

        public List<LinkSplitRequestedEventArgs> Requests { get; } = new();
    }

    private static Stand Create(params Point[] extra)
    {
        var nodes = NodeStand.Create([new Point(100, 100), new Point(400, 200), new Point(100, 380), .. extra], itemTemplate: KnotAtThree);
        var stand = new Stand(nodes, nodes.Connect(0, 1));
        stand.Editor.LinkSplitRequested += (_, e) => stand.Requests.Add(e);
        return stand;
    }

    private static void DoubleClick(NodeStand stand, Point at)
    {
        stand.Window.MouseMove(at);
        stand.Window.MouseDown(at, MouseButton.Left);
        stand.Window.MouseUp(at, MouseButton.Left);
        stand.Window.MouseDown(at, MouseButton.Left);
        stand.Window.MouseUp(at, MouseButton.Left);
        stand.RunLayout();
    }

    [AvaloniaFact]
    public void Double_Clicking_A_Link_Asks_To_Split_It_On_The_Curve()
    {
        var stand = Create();
        var near = stand.Link.Geometry.At(0.5) + new Vector(0, 3);

        DoubleClick(stand.Nodes, near);

        var request = Assert.Single(stand.Requests);
        Assert.Same(stand.First, request.Link);
        Assert.True(stand.Link.Geometry.DistanceTo(request.Location) < 0.5, $"{request.Location} не на кривой");
        Assert.True(Point.Distance(near, request.Location) <= 3.01, $"{request.Location} далеко от щелчка");
    }

    [AvaloniaFact]
    public void A_Single_Click_Does_Not_Ask()
    {
        var stand = Create();
        var middle = stand.Link.Geometry.At(0.5);

        stand.Nodes.Window.MouseDown(middle, MouseButton.Left);
        stand.Nodes.Window.MouseUp(middle, MouseButton.Left);

        Assert.Empty(stand.Requests);
        Assert.Equal([stand.First], stand.Editor.SelectedLinks);
    }

    [AvaloniaFact]
    public void An_Additive_Double_Click_Only_Toggles()
    {
        // С модификатором добавления щелчок — жест выбора: дважды переключил и ничего не просит.
        var stand = Create();
        var middle = stand.Link.Geometry.At(0.5);

        stand.Nodes.Window.MouseMove(middle, RawInputModifiers.Shift);
        stand.Nodes.Window.MouseDown(middle, MouseButton.Left, RawInputModifiers.Shift);
        stand.Nodes.Window.MouseUp(middle, MouseButton.Left, RawInputModifiers.Shift);
        stand.Nodes.Window.MouseDown(middle, MouseButton.Left, RawInputModifiers.Shift);
        stand.Nodes.Window.MouseUp(middle, MouseButton.Left, RawInputModifiers.Shift);

        Assert.Empty(stand.Requests);
        Assert.Empty(stand.Editor.SelectedLinks);
    }

    [AvaloniaFact]
    public void Nobody_Splitting_Leaves_The_Link_Selected()
    {
        var stand = Create();

        DoubleClick(stand.Nodes, stand.Link.Geometry.At(0.5));

        Assert.Single(stand.Requests);
        Assert.Equal([stand.First], stand.Editor.SelectedLinks);
    }

    [AvaloniaFact]
    public void Double_Clicking_A_Node_Over_A_Link_Does_Not_Ask()
    {
        // Узел 4 стоит на середине связи тем местом, за которое узел берут.
        var probe = Create();
        var middle = probe.Link.Geometry.At(0.5);
        var stand = Create(new Point(700, 500), middle - (probe.Nodes.GripOf(0) - probe.Nodes.Node(0).Location));

        DoubleClick(stand.Nodes, middle);

        Assert.Empty(stand.Requests);
    }

    [AvaloniaFact]
    public void The_Second_Press_Does_Not_Detach()
    {
        var stand = Create();
        var deletes = 0;
        stand.Editor.LinkDeleteRequested += (_, _) => deletes++;
        var middle = stand.Link.Geometry.At(0.5);
        var away = new Point(700, 520);

        stand.Nodes.Window.MouseMove(middle);
        stand.Nodes.Window.MouseDown(middle, MouseButton.Left);
        stand.Nodes.Window.MouseUp(middle, MouseButton.Left);
        stand.Nodes.Window.MouseDown(middle, MouseButton.Left);
        stand.Nodes.Window.MouseMove(away);
        stand.Nodes.Window.MouseUp(away, MouseButton.Left);

        Assert.IsType<EditorIdleState>(stand.Editor.CurrentState);
        Assert.Equal(0, deletes);
    }

    [AvaloniaFact]
    public void The_Split_Point_Is_In_World_Coordinates()
    {
        var stand = Create();
        stand.Editor.ViewportZoom = 0.5;
        stand.Nodes.RunLayout();
        var world = stand.Link.Geometry.At(0.5);
        var screen = (world - (Vector)stand.Editor.ViewportLocation) * 0.5;

        DoubleClick(stand.Nodes, screen);

        var request = Assert.Single(stand.Requests);
        Assert.True(stand.Link.Geometry.DistanceTo(request.Location) < 0.5, $"{request.Location} не на кривой");
        Assert.True(Point.Distance(world, request.Location) < 2, $"{request.Location} против {world}");
    }

    [AvaloniaFact]
    public void The_Host_Splits_The_Link_With_A_Knot()
    {
        // Как это делает хост: узел перенаправления в точке запроса, вместо одной связи — две через него.
        var stand = Create();
        var knot = NodeStand.NodeName(3);
        Point? location = null;
        stand.Editor.LinkSplitRequested += (_, e) =>
        {
            var old = (LinkData)e.Link;
            stand.Nodes.Items.Add(knot);
            stand.Nodes.Links.Remove(old);
            stand.Nodes.Links.Add(new LinkData(old.From, knot + ".in"));
            stand.Nodes.Links.Add(new LinkData(knot + ".out", old.To));
            location = e.Location;
            e.Handled = true;
        };

        DoubleClick(stand.Nodes, stand.Link.Geometry.At(0.5));
        Assert.NotNull(location);
        Assert.True(stand.Editor.TryFindResource("NodeEditor.Reroute.Size", out var size));
        var half = (double)size! / 2;
        stand.Nodes.Node(3).Location = location!.Value - new Vector(half, half);
        stand.Nodes.RunLayout();

        Assert.Equal(2, stand.Nodes.Links.Count);
        Assert.Equal(location, stand.Nodes.LinkOf(stand.Nodes.Links[0]).TargetAnchor);
        Assert.Equal(location, stand.Nodes.LinkOf(stand.Nodes.Links[1]).SourceAnchor);
    }

    [AvaloniaFact]
    public void Split_Stops_At_The_First_Handler()
    {
        var nodes = NodeStand.Create([new Point(100, 100), new Point(400, 200)], itemTemplate: NodeStand.PortedNode);
        var data = nodes.Connect(0, 1);
        var late = 0;
        nodes.Editor.LinkSplitRequested += (_, e) => e.Handled = true;
        nodes.Editor.LinkSplitRequested += (_, _) => late++;

        DoubleClick(nodes, nodes.LinkOf(data).Geometry.At(0.5));

        Assert.Equal(0, late);
    }
}
