using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Xunit;
using ArxisStudio.Surface.Nodes;

namespace ArxisStudio.Tests;

/// <summary>
/// Связи выбираются щелчком по кривой и удаляются запросом к приложению (ADR 0001, 0004).
/// </summary>
/// <remarks>
/// Две связи — из узла 0 и из узла 2 во вход узла 1, — и между ними ничего: середина каждой
/// кривой лежит на пустом холсте.
/// </remarks>
public class LinkSelectionTests
{
    private sealed record Stand(NodeStand Nodes, LinkData First, LinkData Second)
    {
        public NodeEditor Editor => Nodes.Editor;
    }

    private static Stand Create(params Point[] extra)
    {
        var nodes = NodeStand.Create(
            [new Point(100, 100), new Point(400, 200), new Point(100, 380), .. extra],
            itemTemplate: NodeStand.PortedNode);
        return new Stand(nodes, nodes.Connect(0, 1), nodes.Connect(2, 1));
    }

    private static Point Middle(Stand stand, LinkData data) => stand.Nodes.LinkOf(data).Geometry.At(0.5);

    /// <summary>
    /// Точка на нормали к кривой в её середине, на заданном расстоянии в мировых единицах.
    /// </summary>
    private static Point OffMiddle(Stand stand, LinkData data, double distance)
    {
        var geometry = stand.Nodes.LinkOf(data).Geometry;
        var tangent = geometry.At(0.51) - geometry.At(0.49);
        var length = Math.Sqrt((tangent.X * tangent.X) + (tangent.Y * tangent.Y));
        var normal = new Vector(-tangent.Y / length, tangent.X / length);
        return geometry.At(0.5) + (normal * distance);
    }

    private static Point ToScreen(NodeEditor editor, Point world) =>
        (world - (Vector)editor.ViewportLocation) * editor.ViewportZoom;

    private static void Click(Stand stand, Point screen, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        stand.Nodes.Window.MouseMove(screen, modifiers);
        stand.Nodes.Window.MouseDown(screen, MouseButton.Left, modifiers);
        stand.Nodes.Window.MouseUp(screen, MouseButton.Left, modifiers);
        stand.Nodes.RunLayout();
    }

    private static void Press(Stand stand, PhysicalKey key) =>
        stand.Nodes.Window.KeyPressQwerty(key, RawInputModifiers.None);

    private static bool PressReachesWindowUnhandled(Stand stand, PhysicalKey key)
    {
        var unhandled = false;
        void Record(object? sender, KeyEventArgs e) => unhandled = !e.Handled;

        stand.Nodes.Window.AddHandler(InputElement.KeyDownEvent, Record, RoutingStrategies.Bubble, handledEventsToo: true);
        Press(stand, key);
        stand.Nodes.Window.RemoveHandler(InputElement.KeyDownEvent, Record);
        return unhandled;
    }

    [AvaloniaFact]
    public void Clicking_A_Link_Selects_It()
    {
        var stand = Create();

        Click(stand, Middle(stand, stand.First));

        Assert.Equal([stand.First], stand.Editor.SelectedLinks);
        var link = stand.Nodes.LinkOf(stand.First);
        Assert.True(link.IsSelected);
        Assert.Contains(":selected", link.Classes);
        Assert.False(stand.Nodes.LinkOf(stand.Second).IsSelected);
    }

    [AvaloniaFact]
    public void A_Click_Within_The_Tolerance_Hits()
    {
        var stand = Create();

        Click(stand, OffMiddle(stand, stand.First, 5));

        Assert.Equal([stand.First], stand.Editor.SelectedLinks);
    }

    [AvaloniaFact]
    public void The_Tolerance_Is_In_Screen_Pixels()
    {
        // На отдалении 0,5 пять пикселей экрана — десять мировых единиц: допуск, не поделённый
        // на масштаб, до них бы не дотянулся. Двенадцать пикселей — уже мимо на любом масштабе.
        var stand = Create();
        stand.Editor.ViewportZoom = 0.5;
        stand.Nodes.RunLayout();

        Click(stand, ToScreen(stand.Editor, OffMiddle(stand, stand.First, 5 / 0.5)));
        Assert.Equal([stand.First], stand.Editor.SelectedLinks);

        stand.Editor.ClearLinkSelection();
        Click(stand, ToScreen(stand.Editor, OffMiddle(stand, stand.First, 12 / 0.5)));
        Assert.Empty(stand.Editor.SelectedLinks);
    }

    [AvaloniaFact]
    public void A_Press_Beside_A_Link_Starts_The_Marquee()
    {
        var stand = Create();
        Click(stand, Middle(stand, stand.First));
        var beside = OffMiddle(stand, stand.First, 30);

        stand.Nodes.Window.MouseMove(beside);
        stand.Nodes.Window.MouseDown(beside, MouseButton.Left);
        stand.Nodes.Window.MouseMove(beside + new Vector(20, 20));

        Assert.True(stand.Editor.IsSelecting, "Мимо связи нажатие принадлежит рамке ядра.");
        Assert.Empty(stand.Editor.SelectedLinks);
        stand.Nodes.Window.MouseUp(beside + new Vector(20, 20), MouseButton.Left);
    }

    [AvaloniaFact]
    public void A_Node_Over_A_Link_Wins()
    {
        // Узел 3 стоит на середине первой связи — тем местом, за которое узел берут, а не портом:
        // нажатие по нему достаётся узлу, а не связи под ним.
        var probe = Create();
        var middle = Middle(probe, probe.First);
        var stand = Create(middle - (probe.Nodes.GripOf(0) - probe.Nodes.Node(0).Location));

        Assert.Equal(middle, stand.Nodes.GripOf(3));
        Assert.NotNull(stand.Editor.HitTestLink(middle));
        Click(stand, middle);

        Assert.Empty(stand.Editor.SelectedLinks);
        Assert.Contains(stand.Editor.SelectedDesignTargets, t => ReferenceEquals(t.Target, stand.Nodes.Node(3)));
    }

    [AvaloniaFact]
    public void Link_And_Node_Selection_Exclude_Each_Other()
    {
        var stand = Create();

        Click(stand, stand.Nodes.GripOf(0));
        Assert.NotEmpty(stand.Editor.SelectedDesignTargets);

        Click(stand, Middle(stand, stand.First));
        Assert.Empty(stand.Editor.SelectedDesignTargets);
        Assert.Equal([stand.First], stand.Editor.SelectedLinks);

        Click(stand, stand.Nodes.GripOf(0));
        Assert.Empty(stand.Editor.SelectedLinks);
        Assert.False(stand.Nodes.LinkOf(stand.First).IsSelected);
        Assert.NotEmpty(stand.Editor.SelectedDesignTargets);
    }

    [AvaloniaFact]
    public void The_Additive_Modifier_Toggles_A_Link()
    {
        var stand = Create();

        Click(stand, Middle(stand, stand.First));
        Click(stand, Middle(stand, stand.Second), RawInputModifiers.Shift);
        Assert.Equal([stand.First, stand.Second], stand.Editor.SelectedLinks);

        Click(stand, Middle(stand, stand.First), RawInputModifiers.Shift);
        Assert.Equal([stand.Second], stand.Editor.SelectedLinks);

        // Без модификатора — замена, а не добавление.
        Click(stand, Middle(stand, stand.First));
        Assert.Equal([stand.First], stand.Editor.SelectedLinks);
    }

    [AvaloniaFact]
    public void Delete_Asks_For_The_Selected_Links()
    {
        var stand = Create();
        var nodeRequests = 0;
        var linkRequests = new List<IReadOnlyList<object>>();
        stand.Editor.DeleteRequested += (_, _) => nodeRequests++;
        stand.Editor.LinkDeleteRequested += (_, e) =>
        {
            linkRequests.Add(e.Links);
            foreach (var item in e.Links)
                stand.Nodes.Links.Remove(item);
            e.Handled = true;
        };

        Click(stand, Middle(stand, stand.First));
        Click(stand, Middle(stand, stand.Second), RawInputModifiers.Shift);
        Press(stand, PhysicalKey.Delete);
        stand.Nodes.RunLayout();

        var request = Assert.Single(linkRequests);
        Assert.Equal([stand.First, stand.Second], request);
        Assert.Equal(0, nodeRequests);
        Assert.Empty(stand.Nodes.Links);
        Assert.Empty(stand.Editor.SelectedLinks);
    }

    [AvaloniaFact]
    public void Delete_With_Nodes_Selected_Asks_For_The_Nodes()
    {
        var stand = Create();
        var nodeRequests = 0;
        var linkRequests = 0;
        stand.Editor.DeleteRequested += (_, e) =>
        {
            nodeRequests++;
            e.Handled = true;
        };
        stand.Editor.LinkDeleteRequested += (_, _) => linkRequests++;

        Click(stand, stand.Nodes.GripOf(0));
        Press(stand, PhysicalKey.Delete);

        Assert.Equal(1, nodeRequests);
        Assert.Equal(0, linkRequests);
    }

    [AvaloniaFact]
    public void An_Unhandled_Link_Delete_Is_Not_Swallowed()
    {
        var stand = Create();
        var requests = 0;
        stand.Editor.LinkDeleteRequested += (_, _) => requests++;
        Click(stand, Middle(stand, stand.First));

        Assert.True(PressReachesWindowUnhandled(stand, PhysicalKey.Delete));
        Assert.Equal(1, requests);
        Assert.Equal(2, stand.Nodes.Links.Count);
    }

    [AvaloniaFact]
    public void Escape_Clears_The_Link_Selection()
    {
        var stand = Create();
        Click(stand, Middle(stand, stand.First));

        Press(stand, PhysicalKey.Escape);

        Assert.Empty(stand.Editor.SelectedLinks);
        Assert.False(stand.Nodes.LinkOf(stand.First).IsSelected);
    }

    [AvaloniaFact]
    public void Escape_Without_Selected_Links_Is_Left_To_Others()
    {
        var stand = Create();
        Assert.True(PressReachesWindowUnhandled(stand, PhysicalKey.Escape));
    }

    [AvaloniaFact]
    public void A_Link_Leaving_The_Collection_Leaves_The_Selection()
    {
        var stand = Create();
        Click(stand, Middle(stand, stand.First));
        Click(stand, Middle(stand, stand.Second), RawInputModifiers.Shift);

        stand.Nodes.Links.Remove(stand.First);
        stand.Nodes.RunLayout();

        Assert.Equal([stand.Second], stand.Editor.SelectedLinks);
    }

    [AvaloniaFact]
    public void Hovering_A_Link_Highlights_It()
    {
        var stand = Create();
        var link = stand.Nodes.LinkOf(stand.First);

        stand.Nodes.Window.MouseMove(Middle(stand, stand.First));
        Assert.Contains(":highlighted", link.Classes);

        stand.Nodes.Window.MouseMove(OffMiddle(stand, stand.First, 40));
        Assert.DoesNotContain(":highlighted", link.Classes);
    }

    [AvaloniaFact]
    public void A_Node_Over_A_Link_Takes_The_Hover()
    {
        // Движение узел не обрабатывает никогда, и подсветка связи под ним обещала бы щелчок,
        // который достанется узлу.
        var probe = Create();
        var middle = Middle(probe, probe.First);
        var stand = Create(middle - (probe.Nodes.GripOf(0) - probe.Nodes.Node(0).Location));

        stand.Nodes.Window.MouseMove(middle);

        Assert.DoesNotContain(":highlighted", stand.Nodes.LinkOf(stand.First).Classes);
    }

    [AvaloniaFact]
    public void Dragging_A_New_Link_Leaves_The_Others_Dark()
    {
        var stand = Create();
        var from = stand.Nodes.PinCentreInWorld(1, PortDirection.Output);

        stand.Nodes.Window.MouseMove(from);
        stand.Nodes.Window.MouseDown(from, MouseButton.Left);
        stand.Nodes.Window.MouseMove(Middle(stand, stand.First));

        Assert.IsType<ArxisStudio.Surface.Nodes.States.PendingLinkState>(stand.Editor.CurrentState);
        Assert.DoesNotContain(":highlighted", stand.Nodes.LinkOf(stand.First).Classes);
        stand.Nodes.Window.MouseUp(Middle(stand, stand.First), MouseButton.Left);
    }

    [AvaloniaFact]
    public void The_Host_Selects_A_Link_By_Its_Item()
    {
        var stand = Create();

        Assert.True(stand.Editor.SelectLink(stand.Second));
        Assert.Equal([stand.Second], stand.Editor.SelectedLinks);

        Assert.True(stand.Editor.SelectLink(stand.First, additive: true));
        Assert.Equal([stand.Second, stand.First], stand.Editor.SelectedLinks);

        Assert.False(stand.Editor.SelectLink(new object()));
    }

    [AvaloniaFact]
    public void An_Unchanged_Selection_Is_Not_Republished()
    {
        var stand = Create();
        Click(stand, Middle(stand, stand.First));
        var published = 0;
        stand.Editor.PropertyChanged += (_, e) =>
        {
            if (e.Property == NodeEditor.SelectedLinksProperty)
                published++;
        };

        Click(stand, Middle(stand, stand.First));

        Assert.Equal(0, published);
    }
}
