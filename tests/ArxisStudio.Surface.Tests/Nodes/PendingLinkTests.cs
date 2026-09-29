using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface;
using ArxisStudio.Surface.Nodes;
using ArxisStudio.Surface.Nodes.States;

namespace ArxisStudio.Tests;

/// <summary>
/// Новая связь протягивается от порта и становится запросом к приложению (ADR 0001, 0004).
/// </summary>
/// <remarks>
/// Масштаб 1 и холст в начале координат: точки экрана совпадают с мировыми, и указатель ведётся
/// прямо на центры штырьков.
/// </remarks>
public class PendingLinkTests
{
    private static NodeStand Create() =>
        NodeStand.Create([new Point(100, 100), new Point(400, 200)], itemTemplate: NodeStand.PortedNode);

    private static Point Pin(NodeStand stand, int node, PortDirection direction) =>
        stand.PinCentreInWorld(node, direction);

    private static void Press(NodeStand stand, Point at)
    {
        stand.Window.MouseMove(at);
        stand.Window.MouseDown(at, MouseButton.Left);
    }

    private static void MoveTo(NodeStand stand, Point to)
    {
        stand.Window.MouseMove(to);
        stand.RunLayout();
    }

    private static void DragLink(NodeStand stand, Point from, Point to)
    {
        Press(stand, from);
        MoveTo(stand, from + ((to - from) / 2));
        MoveTo(stand, to);
        stand.Window.MouseUp(to, MouseButton.Left);
        stand.RunLayout();
    }

    private static List<ConnectRequestedEventArgs> RecordRequests(NodeStand stand, bool handle = false)
    {
        var requests = new List<ConnectRequestedEventArgs>();
        stand.Editor.ConnectRequested += (_, e) =>
        {
            requests.Add(e);
            if (!handle)
                return;

            stand.Links.Add(new LinkData(e.Source, e.Target));
            e.Handled = true;
        };
        return requests;
    }

    [AvaloniaFact]
    public void Dragging_An_Output_To_An_Input_Requests_A_Link()
    {
        var stand = Create();
        var requests = RecordRequests(stand);

        DragLink(stand, Pin(stand, 0, PortDirection.Output), Pin(stand, 1, PortDirection.Input));

        var request = Assert.Single(requests);
        Assert.Equal(NodeStand.Out(0), request.Source);
        Assert.Equal(NodeStand.In(1), request.Target);
    }

    [AvaloniaFact]
    public void Dragging_From_An_Input_Is_Normalised()
    {
        // Источник — всегда выход, в какую сторону ни тянули: иначе хосту пришлось бы
        // разбирать направление самому, и связи ложились бы задом наперёд.
        var stand = Create();
        var requests = RecordRequests(stand);

        DragLink(stand, Pin(stand, 1, PortDirection.Input), Pin(stand, 0, PortDirection.Output));

        var request = Assert.Single(requests);
        Assert.Equal(NodeStand.Out(0), request.Source);
        Assert.Equal(NodeStand.In(1), request.Target);
    }

    [AvaloniaFact]
    public void Two_Outputs_Are_Refused_And_The_Port_Shows_It()
    {
        var stand = Create();
        var requests = RecordRequests(stand);
        var target = stand.PortOf(1, PortDirection.Output);

        Press(stand, Pin(stand, 0, PortDirection.Output));
        MoveTo(stand, Pin(stand, 1, PortDirection.Output));
        Assert.Contains(":refusing", target.Classes);

        stand.Window.MouseUp(Pin(stand, 1, PortDirection.Output), MouseButton.Left);
        Assert.Empty(requests);
        Assert.DoesNotContain(":refusing", target.Classes);
    }

    /// <summary>
    /// Узел 1 — вход и выход в одной точке, как у узла перенаправления. Выход поставлен первым: по одному
    /// расстоянию — поровну — выбирался бы тот, что встретился раньше.
    /// </summary>
    private static readonly IDataTemplate PortsOnTopAtOne = new FuncDataTemplate<string>((name, _) =>
        name == NodeStand.NodeName(1)
            ? new Grid
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Children =
                {
                    new Port { Direction = PortDirection.Output, Data = name + ".out" },
                    new Port { Direction = PortDirection.Input, Data = name + ".in" }
                }
            }
            : NodeStand.PortedNode.Build(name)!, supportsRecycling: false);

    [AvaloniaFact]
    public void Ports_On_Top_Of_Each_Other_Resolve_By_Direction()
    {
        // По одному расстоянию конец из выхода выбрал бы выход узла 1 и получил отказ: связь в
        // узел перенаправления не входила бы вовсе.
        var stand = NodeStand.Create([new Point(100, 100), new Point(400, 200)], itemTemplate: PortsOnTopAtOne);
        var requests = RecordRequests(stand);
        var output = Pin(stand, 1, PortDirection.Output);
        Assert.Equal(output, Pin(stand, 1, PortDirection.Input));

        DragLink(stand, Pin(stand, 0, PortDirection.Output), output);

        var request = Assert.Single(requests);
        Assert.Equal(NodeStand.Out(0), request.Source);
        Assert.Equal(NodeStand.In(1), request.Target);
    }

    [AvaloniaFact]
    public void An_Accepting_Port_Shows_It_Before_The_Release()
    {
        var stand = Create();
        var target = stand.PortOf(1, PortDirection.Input);

        Press(stand, Pin(stand, 0, PortDirection.Output));
        MoveTo(stand, Pin(stand, 1, PortDirection.Input));

        Assert.Contains(":accepting", target.Classes);
        stand.Window.MouseUp(Pin(stand, 1, PortDirection.Input), MouseButton.Left);
        Assert.DoesNotContain(":accepting", target.Classes);
    }

    [AvaloniaFact]
    public void The_Host_Can_Veto_And_The_Port_Shows_It()
    {
        var stand = Create();
        var requests = RecordRequests(stand);
        stand.Editor.ConnectValidating += (_, e) => e.IsAllowed = false;
        var target = stand.PortOf(1, PortDirection.Input);

        Press(stand, Pin(stand, 0, PortDirection.Output));
        MoveTo(stand, Pin(stand, 1, PortDirection.Input));
        Assert.Contains(":refusing", target.Classes);

        stand.Window.MouseUp(Pin(stand, 1, PortDirection.Input), MouseButton.Left);
        Assert.Empty(requests);
    }

    [AvaloniaFact]
    public void A_New_Link_Is_Validated_Without_A_Link()
    {
        var stand = Create();
        var asked = new List<object?>();
        stand.Editor.ConnectValidating += (_, e) => asked.Add(e.Link);

        DragLink(stand, Pin(stand, 0, PortDirection.Output), Pin(stand, 1, PortDirection.Input));

        Assert.NotEmpty(asked);
        Assert.All(asked, Assert.Null);
    }

    [AvaloniaFact]
    public void Without_A_Handler_Nothing_Changes()
    {
        var stand = Create();

        DragLink(stand, Pin(stand, 0, PortDirection.Output), Pin(stand, 1, PortDirection.Input));

        Assert.Empty(stand.Links);
        Assert.False(stand.PortOf(0, PortDirection.Output).IsConnected);
    }

    [AvaloniaFact]
    public void A_Handled_Request_Draws_The_Link()
    {
        var stand = Create();
        RecordRequests(stand, handle: true);

        DragLink(stand, Pin(stand, 0, PortDirection.Output), Pin(stand, 1, PortDirection.Input));

        var data = Assert.Single(stand.Links);
        var link = stand.LinkOf(data);
        Assert.True(link.IsVisible);
        Assert.Equal(Pin(stand, 1, PortDirection.Input), link.TargetAnchor);
    }

    [AvaloniaFact]
    public void Connect_Stops_At_The_First_Handler()
    {
        var stand = Create();
        var late = 0;
        stand.Editor.ConnectRequested += (_, e) =>
        {
            stand.Links.Add(new LinkData(e.Source, e.Target));
            e.Handled = true;
        };
        stand.Editor.ConnectRequested += (_, _) => late++;

        DragLink(stand, Pin(stand, 0, PortDirection.Output), Pin(stand, 1, PortDirection.Input));

        Assert.Single(stand.Links);
        Assert.Equal(0, late);
    }

    [AvaloniaFact]
    public void Pressing_A_Port_Neither_Drags_Nor_Selects_The_Node()
    {
        var stand = Create();
        var from = Pin(stand, 0, PortDirection.Output);

        DragLink(stand, from, from + new Vector(80, 60));

        Assert.Equal(new Point(100, 100), stand.Node(0).Location);
        Assert.Empty(stand.Editor.SelectedTargets);
    }

    [AvaloniaFact]
    public void A_Release_On_The_Empty_Canvas_Requests_Nothing()
    {
        var stand = Create();
        var requests = RecordRequests(stand);

        DragLink(stand, Pin(stand, 0, PortDirection.Output), new Point(700, 500));

        Assert.Empty(requests);
        Assert.False(stand.Editor.PendingPreview!.IsVisible, "Превью обязано погаснуть с концом жеста.");
    }

    private static List<ConnectDroppedEventArgs> RecordDrops(NodeStand stand)
    {
        var drops = new List<ConnectDroppedEventArgs>();
        stand.Editor.ConnectDropped += (_, e) => drops.Add(e);
        return drops;
    }

    [AvaloniaFact]
    public void A_Release_On_The_Empty_Canvas_Reports_The_Port_And_The_Point()
    {
        // Как у Blueprint: провод, брошенный в пустоту, — повод хосту открыть меню действий по типу пина
        // и поставить узел в точку отпускания (ADR 0018).
        var stand = Create();
        var drops = RecordDrops(stand);
        var shift = new Vector(50, 40);
        stand.Editor.ViewportLocation = new Point(shift.X, shift.Y);
        stand.RunLayout();
        var screen = new Point(700, 500);

        DragLink(stand, Pin(stand, 1, PortDirection.Input) - shift, screen);

        var drop = Assert.Single(drops);
        Assert.Equal(NodeStand.In(1), drop.Port);
        Assert.Equal(PortDirection.Input, drop.Direction);
        Assert.Equal(screen, drop.ViewportPoint);
        Assert.Equal(screen + shift, drop.Location);
    }

    [AvaloniaFact]
    public void A_Refusing_Port_Is_Not_A_Drop()
    {
        // Человек целился в порт: меню действий вместо отказа было бы наказанием за промах.
        var stand = Create();
        var drops = RecordDrops(stand);
        stand.Editor.ConnectValidating += (_, e) => e.IsAllowed = false;

        DragLink(stand, Pin(stand, 0, PortDirection.Output), Pin(stand, 1, PortDirection.Input));

        Assert.Empty(drops);
    }

    [AvaloniaFact]
    public void A_Click_On_A_Port_Is_Not_A_Drop()
    {
        // Порт начинает жест нажатием, и без проверки каждый щелчок по пину открывал бы хосту меню.
        var stand = Create();
        var drops = RecordDrops(stand);
        var pin = Pin(stand, 0, PortDirection.Output);

        Press(stand, pin);
        MoveTo(stand, pin + new Vector(3, 2));
        stand.Window.MouseUp(pin + new Vector(3, 2), MouseButton.Left);

        Assert.Empty(drops);
    }

    [AvaloniaFact]
    public void The_Preview_Follows_The_Pointer_And_Snaps_To_An_Accepting_Pin()
    {
        var stand = Create();
        var origin = Pin(stand, 0, PortDirection.Output);
        var preview = stand.Editor.PendingPreview!;

        Press(stand, origin);
        MoveTo(stand, new Point(600, 450));
        Assert.True(preview.IsVisible);
        Assert.Equal(origin, preview.Geometry.Source);
        Assert.Equal(new Point(600, 450), preview.Geometry.Target);

        // Рядом со штырьком, но не в центре: конец притягивается к штырьку.
        var near = Pin(stand, 1, PortDirection.Input) + new Vector(3, 2);
        MoveTo(stand, near);
        Assert.Equal(Pin(stand, 1, PortDirection.Input), preview.Geometry.Target);

        stand.Window.MouseUp(near, MouseButton.Left);
    }

    [AvaloniaFact]
    public void Escape_Cancels_The_Drag()
    {
        var stand = Create();
        var requests = RecordRequests(stand);

        Press(stand, Pin(stand, 0, PortDirection.Output));
        MoveTo(stand, Pin(stand, 1, PortDirection.Input));
        stand.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.IsNotType<PendingLinkState>(stand.Editor.CurrentState);
        Assert.False(stand.Editor.PendingPreview!.IsVisible);
        Assert.DoesNotContain(":accepting", stand.PortOf(1, PortDirection.Input).Classes);

        stand.Window.MouseUp(Pin(stand, 1, PortDirection.Input), MouseButton.Left);
        Assert.Empty(requests);
    }

    [AvaloniaFact]
    public void Losing_Capture_Ends_The_Drag_Without_A_Request()
    {
        var stand = Create();
        var requests = RecordRequests(stand);
        IPointer? pointer = null;
        void Take(object? sender, PointerPressedEventArgs e) => pointer ??= e.Pointer;
        stand.Editor.AddHandler(InputElement.PointerPressedEvent, Take, handledEventsToo: true);

        Press(stand, Pin(stand, 0, PortDirection.Output));
        stand.Editor.RemoveHandler(InputElement.PointerPressedEvent, (EventHandler<PointerPressedEventArgs>)Take);
        MoveTo(stand, Pin(stand, 1, PortDirection.Input));
        Assert.NotNull(pointer);
        Assert.Same(stand.Editor, pointer!.Captured);

        pointer.Capture(null);

        Assert.IsNotType<PendingLinkState>(stand.Editor.CurrentState);
        Assert.False(stand.Editor.PendingPreview!.IsVisible);
        Assert.DoesNotContain(":accepting", stand.PortOf(1, PortDirection.Input).Classes);
        stand.Window.MouseUp(Pin(stand, 1, PortDirection.Input), MouseButton.Left);
        Assert.Empty(requests);
    }

    [AvaloniaFact]
    public void Escape_Without_A_Drag_Is_Left_To_Others()
    {
        // Команда отмены уступает Escape, когда тянуть нечего: снятие выделения остаётся за ним.
        var stand = Create();
        stand.Window.MouseDown(stand.GripOf(0), MouseButton.Left);
        stand.Window.MouseUp(stand.GripOf(0), MouseButton.Left);
        Assert.NotEmpty(stand.Editor.SelectedTargets);

        stand.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.Empty(stand.Editor.SelectedTargets);
    }

    [AvaloniaFact]
    public void The_Capture_Radius_Is_In_Screen_Pixels()
    {
        // 10 пикселей экрана от края порта: при радиусе 12 это попадание на любом масштабе.
        // На отдалении 0,5 это 20 мировых единиц — радиус, не поделённый на масштаб, их не накрыл бы.
        const double zoom = 0.5;
        var stand = Create();
        stand.Editor.ViewportZoom = zoom;
        stand.RunLayout();

        var input = stand.PortOf(1, PortDirection.Input);
        Assert.True(input.TryGetWorldBounds(out var bounds));
        var outsideLeft = new Point(bounds.Left - (10 / zoom), bounds.Center.Y);
        var from = Pin(stand, 0, PortDirection.Output);
        var toScreen = (outsideLeft - (Vector)stand.Editor.ViewportLocation) * zoom;
        var fromScreen = (from - (Vector)stand.Editor.ViewportLocation) * zoom;

        var requests = RecordRequests(stand);
        Press(stand, fromScreen);
        MoveTo(stand, toScreen);
        stand.Window.MouseUp(toScreen, MouseButton.Left);

        Assert.Single(requests);
    }

    [AvaloniaFact]
    public void Auto_Pan_Keeps_The_Loose_End_Under_The_Pointer()
    {
        var stand = Create();
        var preview = stand.Editor.PendingPreview!;
        var nearEdge = new Point(790, 300);

        Press(stand, Pin(stand, 0, PortDirection.Output));
        MoveTo(stand, nearEdge);
        Assert.True(stand.Editor.IsAutoPanning);

        stand.Editor.StepAutoPan(TimeSpan.FromMilliseconds(40));

        Assert.Equal(stand.Editor.GetWorldPosition(nearEdge), preview.Geometry.Target);
        stand.Window.MouseUp(nearEdge, MouseButton.Left);
        Assert.False(stand.Editor.IsAutoPanning);
    }

    [AvaloniaFact]
    public void The_Host_Undoes_A_Link_Through_Surface_History()
    {
        // Структурная правка — хоста (ADR 0001), и в отмену она попадает его же изменением.
        var stand = Create();
        var history = new SurfaceHistory(stand.Editor);
        stand.Editor.ConnectRequested += (_, e) =>
        {
            var data = new LinkData(e.Source, e.Target);
            stand.Links.Add(data);
            history.Push(new AddLink(stand, data));
            e.Handled = true;
        };

        DragLink(stand, Pin(stand, 0, PortDirection.Output), Pin(stand, 1, PortDirection.Input));
        Assert.Single(stand.Links);

        stand.Editor.Focus();
        stand.Window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        stand.RunLayout();

        Assert.Empty(stand.Links);
        Assert.False(stand.PortOf(1, PortDirection.Input).IsConnected);
    }

    private sealed class AddLink(NodeStand stand, LinkData data) : ISurfaceChange
    {
        public void Revert() => stand.Links.Remove(data);

        public void Reapply() => stand.Links.Add(data);
    }
}
