using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface.Nodes;
using ArxisStudio.Surface.Nodes.States;
using ArxisStudio.Surface.States;

namespace ArxisStudio.Tests;

/// <summary>
/// Разрез: Alt + протяжка тянет отрезок, перечёркнутые связи просят удалить (ADR 0005).
/// </summary>
/// <remarks>
/// Связи из узлов 0 и 2 во вход узла 1. Вертикаль x = 300 пересекает обе: первую около y = 190,
/// вторую около y = 350. Масштаб 1 и холст в начале координат: экранные точки совпадают с мировыми.
/// </remarks>
public class LinkCutTests
{
    private sealed record Stand(NodeStand Nodes, LinkData First, LinkData Second)
    {
        public NodeEditor Editor => Nodes.Editor;

        public List<LinkDeleteRequestedEventArgs> Deletes { get; } = new();
    }

    private static Stand Create()
    {
        var nodes = NodeStand.Create(
            [new Point(100, 100), new Point(400, 200), new Point(100, 380)],
            itemTemplate: NodeStand.PortedNode);
        var stand = new Stand(nodes, nodes.Connect(0, 1), nodes.Connect(2, 1));
        stand.Editor.LinkDeleteRequested += (_, e) => stand.Deletes.Add(e);
        return stand;
    }

    private static void Press(Stand stand, Point at, RawInputModifiers modifiers = RawInputModifiers.Alt)
    {
        stand.Nodes.Window.MouseMove(at, modifiers);
        stand.Nodes.Window.MouseDown(at, MouseButton.Left, modifiers);
    }

    private static void MoveTo(Stand stand, Point to, RawInputModifiers modifiers = RawInputModifiers.Alt)
    {
        stand.Nodes.Window.MouseMove(to, modifiers);
        stand.Nodes.RunLayout();
    }

    private static void Release(Stand stand, Point at, RawInputModifiers modifiers = RawInputModifiers.Alt)
    {
        stand.Nodes.Window.MouseUp(at, MouseButton.Left, modifiers);
        stand.Nodes.RunLayout();
    }

    private static void Stroke(Stand stand, Point from, Point to, RawInputModifiers modifiers = RawInputModifiers.Alt)
    {
        Press(stand, from, modifiers);
        MoveTo(stand, from + ((to - from) / 2), modifiers);
        MoveTo(stand, to, modifiers);
        Release(stand, to, modifiers);
    }

    [AvaloniaFact]
    public void An_Alt_Stroke_Cuts_The_Links_It_Crosses()
    {
        var stand = Create();

        Stroke(stand, new Point(300, 40), new Point(300, 520));

        var request = Assert.Single(stand.Deletes);
        Assert.Equal([stand.First, stand.Second], request.Links);
    }

    [AvaloniaFact]
    public void A_Stroke_Crossing_Nothing_Asks_Nothing()
    {
        var stand = Create();

        Stroke(stand, new Point(600, 40), new Point(700, 520));

        Assert.Empty(stand.Deletes);
    }

    [AvaloniaFact]
    public void A_Stroke_Inside_A_Links_Frame_But_Not_Across_It_Asks_Nothing()
    {
        // Рамка первой связи — выпуклая оболочка её опорных точек; у правого верхнего её угла кривой нет.
        var stand = Create();
        var frame = stand.Nodes.LinkOf(stand.First).WorldBounds;
        var from = new Point(frame.Right - 30, frame.Top + 6);
        var to = new Point(frame.Right - 8, frame.Top + 10);
        Assert.True(frame.Contains(from) && frame.Contains(to));

        Stroke(stand, from, to);

        Assert.Empty(stand.Deletes);
    }

    [AvaloniaFact]
    public void Crossed_Links_Show_It_While_Cutting()
    {
        var stand = Create();
        var first = stand.Nodes.LinkOf(stand.First);
        var second = stand.Nodes.LinkOf(stand.Second);

        Press(stand, new Point(300, 40));
        MoveTo(stand, new Point(300, 260));

        Assert.Contains(":cutting", first.Classes);
        Assert.DoesNotContain(":cutting", second.Classes);
        Assert.True(stand.Editor.CutPreview!.IsVisible);

        Release(stand, new Point(300, 260));
        Assert.DoesNotContain(":cutting", first.Classes);
        Assert.False(stand.Editor.CutPreview.IsVisible);
        Assert.Equal([stand.First], Assert.Single(stand.Deletes).Links);
    }

    [AvaloniaFact]
    public void Escape_Cancels_The_Cut()
    {
        var stand = Create();
        var first = stand.Nodes.LinkOf(stand.First);

        Press(stand, new Point(300, 40));
        MoveTo(stand, new Point(300, 520));
        stand.Nodes.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.IsType<EditorIdleState>(stand.Editor.CurrentState);
        Assert.DoesNotContain(":cutting", first.Classes);
        Assert.False(stand.Editor.CutPreview!.IsVisible);

        Release(stand, new Point(300, 520));
        Assert.Empty(stand.Deletes);
    }

    [AvaloniaFact]
    public void Losing_Capture_Cancels_The_Cut()
    {
        var stand = Create();
        IPointer? pointer = null;
        void Take(object? sender, PointerPressedEventArgs e) => pointer ??= e.Pointer;
        stand.Editor.AddHandler(InputElement.PointerPressedEvent, Take, handledEventsToo: true);

        Press(stand, new Point(300, 40));
        stand.Editor.RemoveHandler(InputElement.PointerPressedEvent, (EventHandler<PointerPressedEventArgs>)Take);
        MoveTo(stand, new Point(300, 520));
        pointer!.Capture(null);

        Assert.IsType<EditorIdleState>(stand.Editor.CurrentState);
        Release(stand, new Point(300, 520));
        Assert.Empty(stand.Deletes);
    }

    [AvaloniaFact]
    public void Without_The_Modifier_The_Press_Starts_The_Marquee()
    {
        var stand = Create();
        stand.Editor.LinkCutModifiers = KeyModifiers.None;

        Press(stand, new Point(300, 40));
        MoveTo(stand, new Point(300, 520));

        Assert.True(stand.Editor.IsSelecting, "Выключенный разрез уступает нажатие рамке ядра.");
        Release(stand, new Point(300, 520));
        Assert.Empty(stand.Deletes);
    }

    [AvaloniaFact]
    public void A_Stroke_Starting_On_A_Node_Is_Not_A_Cut()
    {
        var stand = Create();

        Press(stand, stand.Nodes.GripOf(2));
        Assert.IsNotType<LinkCutState>(stand.Editor.CurrentState);
        MoveTo(stand, stand.Nodes.GripOf(2) + new Vector(0, -300));
        Release(stand, stand.Nodes.GripOf(2) + new Vector(0, -300));

        Assert.Empty(stand.Deletes);
    }

    [AvaloniaFact]
    public void A_Stroke_Starting_On_A_Link_Cuts_Rather_Than_Selects()
    {
        var stand = Create();
        var onFirst = stand.Nodes.LinkOf(stand.First).Geometry.At(0.5);

        Press(stand, onFirst);
        Assert.IsType<LinkCutState>(stand.Editor.CurrentState);
        MoveTo(stand, new Point(onFirst.X, 520));
        Release(stand, new Point(onFirst.X, 520));

        Assert.Empty(stand.Editor.SelectedLinks);
        Assert.Contains(stand.Second, Assert.Single(stand.Deletes).Links);
    }

    [AvaloniaFact]
    public void The_Stroke_Is_In_World_Coordinates()
    {
        // На отдалении 0,5 вертикаль x = 300 мира лежит на экране у x = 150: считай разрез в
        // экранных точках — он прошёл бы левее обеих связей.
        var stand = Create();
        stand.Editor.ViewportZoom = 0.5;
        stand.Nodes.RunLayout();
        Point Screen(Point world) => (world - (Vector)stand.Editor.ViewportLocation) * 0.5;

        Stroke(stand, Screen(new Point(300, 40)), Screen(new Point(300, 520)));

        Assert.Equal([stand.First, stand.Second], Assert.Single(stand.Deletes).Links);
    }

    [AvaloniaFact]
    public void Auto_Pan_Keeps_The_End_Of_The_Stroke_Under_The_Pointer()
    {
        var stand = Create();
        var nearEdge = new Point(790, 300);

        Press(stand, new Point(300, 300));
        MoveTo(stand, nearEdge);
        Assert.True(stand.Editor.IsAutoPanning);

        stand.Editor.StepAutoPan(TimeSpan.FromMilliseconds(40));

        Assert.Equal(stand.Editor.GetWorldPosition(nearEdge), stand.Editor.CutPreview!.End);
        Assert.Equal(new Point(300, 300), stand.Editor.CutPreview.Start);
        Release(stand, nearEdge);
    }
}
