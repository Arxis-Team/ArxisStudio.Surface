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
/// Конец существующей связи отцепляют протяжкой её тела (ADR 0001, 0004).
/// </summary>
/// <remarks>
/// Связь из узла 0 во вход узла 1; узлы 2 и 3 — куда переносить концы: у узла 2 берут выход,
/// у узла 3 — вход. Масштаб 1 и холст в начале координат: экранные точки совпадают с мировыми.
/// </remarks>
public class LinkDetachTests
{
    private sealed class Stand
    {
        /// <param name="record">
        /// Записывать ли запросы: записывающие обработчики подписываются первыми, и тесту порядка
        /// обхода они мешали бы.
        /// </param>
        public Stand(bool record = true)
        {
            Nodes = NodeStand.Create(
                [new Point(100, 100), new Point(400, 200), new Point(100, 380), new Point(400, 420)],
                itemTemplate: NodeStand.PortedNode);
            Data = Nodes.Connect(0, 1);

            if (!record)
                return;

            Nodes.Editor.ReconnectRequested += (_, e) => Reconnects.Add(e);
            Nodes.Editor.LinkDeleteRequested += (_, e) => Deletes.Add(e);
            Nodes.Editor.ConnectDropped += (_, e) => Drops.Add(e);
        }

        public NodeStand Nodes { get; }

        public NodeEditor Editor => Nodes.Editor;

        public LinkData Data { get; }

        public Link Link => Nodes.LinkOf(Data);

        public List<ReconnectRequestedEventArgs> Reconnects { get; } = new();

        public List<LinkDeleteRequestedEventArgs> Deletes { get; } = new();

        public List<ConnectDroppedEventArgs> Drops { get; } = new();

        /// <summary>
        /// Точка на связи: у источника при малом параметре, у цели при большом.
        /// </summary>
        public Point On(double t) => Link.Geometry.At(t);

        public Point Pin(int node, PortDirection direction) => Nodes.PinCentreInWorld(node, direction);

        public void Press(Point at)
        {
            Nodes.Window.MouseMove(at);
            Nodes.Window.MouseDown(at, MouseButton.Left);
        }

        public void MoveTo(Point to)
        {
            Nodes.Window.MouseMove(to);
            Nodes.RunLayout();
        }

        public void Release(Point at)
        {
            Nodes.Window.MouseUp(at, MouseButton.Left);
            Nodes.RunLayout();
        }

        /// <summary>
        /// Протягивает тело связи из точки <paramref name="t"/> в <paramref name="to"/>.
        /// </summary>
        public void Drag(double t, Point to)
        {
            var from = On(t);
            Press(from);
            MoveTo(from + ((to - from) / 2));
            MoveTo(to);
            Release(to);
        }
    }

    [AvaloniaFact]
    public void The_End_Near_The_Target_Moves_To_Another_Input()
    {
        var stand = new Stand();

        stand.Drag(0.8, stand.Pin(3, PortDirection.Input));

        var request = Assert.Single(stand.Reconnects);
        Assert.Same(stand.Data, request.Link);
        Assert.Equal(LinkEnd.Target, request.End);
        Assert.Equal(NodeStand.In(1), request.OldPort);
        Assert.Equal(NodeStand.In(3), request.NewPort);
        Assert.Empty(stand.Deletes);
    }

    [AvaloniaFact]
    public void The_End_Near_The_Source_Moves_To_Another_Output()
    {
        var stand = new Stand();

        stand.Drag(0.2, stand.Pin(2, PortDirection.Output));

        var request = Assert.Single(stand.Reconnects);
        Assert.Equal(LinkEnd.Source, request.End);
        Assert.Equal(NodeStand.Out(0), request.OldPort);
        Assert.Equal(NodeStand.Out(2), request.NewPort);
    }

    [AvaloniaFact]
    public void The_Host_Reconnects_And_The_Link_Follows()
    {
        var stand = new Stand();
        stand.Editor.ReconnectRequested += (_, e) =>
        {
            var data = (LinkData)e.Link;
            var index = stand.Nodes.Links.IndexOf(data);
            stand.Nodes.Links[index] = e.End == LinkEnd.Target ? data with { To = e.NewPort } : data with { From = e.NewPort };
            e.Handled = true;
        };

        stand.Drag(0.8, stand.Pin(3, PortDirection.Input));

        var moved = Assert.Single(stand.Nodes.Links);
        var link = stand.Nodes.LinkOf(moved);
        Assert.Equal(stand.Pin(3, PortDirection.Input), link.TargetAnchor);
        Assert.Equal(stand.Pin(0, PortDirection.Output), link.SourceAnchor);
        Assert.False(stand.Nodes.PortOf(1, PortDirection.Input).IsConnected, "Прежний вход остался без связи.");
        Assert.True(stand.Nodes.PortOf(3, PortDirection.Input).IsConnected);
    }

    [AvaloniaFact]
    public void While_Detaching_The_Preview_Hangs_From_The_Fixed_End()
    {
        var stand = new Stand();
        var loose = new Point(640, 330);
        var preview = stand.Editor.PendingPreview!;

        stand.Press(stand.On(0.8));
        stand.MoveTo(loose);

        Assert.IsType<PendingLinkState>(stand.Editor.CurrentState);
        Assert.True(preview.IsVisible);
        Assert.Equal(stand.Pin(0, PortDirection.Output), preview.Geometry.Source);
        Assert.Equal(loose, preview.Geometry.Target);
        Assert.Contains(":detaching", stand.Link.Classes);

        stand.Release(loose);
        Assert.DoesNotContain(":detaching", stand.Link.Classes);
        Assert.False(preview.IsVisible);
    }

    [AvaloniaFact]
    public void Dropping_An_End_On_Empty_Canvas_Asks_To_Delete_The_Link()
    {
        var stand = new Stand();

        stand.Drag(0.8, new Point(700, 540));

        var request = Assert.Single(stand.Deletes);
        Assert.Equal([stand.Data], request.Links);
        Assert.Empty(stand.Reconnects);
        Assert.Empty(stand.Drops);
    }

    [AvaloniaFact]
    public void Dropping_An_End_Back_On_Its_Port_Asks_Nothing()
    {
        // Правило приложения «во вход — одна связь» отказало бы занятому входу; свой же порт
        // конец принимает без вопроса — вернуть конец на место не правка.
        var stand = new Stand();
        stand.Editor.ConnectValidating += (_, e) =>
            e.IsAllowed = !stand.Nodes.Links.OfType<LinkData>().Any(l => Equals(l.To, e.Target));
        var own = stand.Pin(1, PortDirection.Input);

        stand.Press(stand.On(0.8));
        stand.MoveTo(stand.On(0.8) + new Vector(-30, 40));
        stand.MoveTo(own);
        Assert.Contains(":accepting", stand.Nodes.PortOf(1, PortDirection.Input).Classes);
        stand.Release(own);

        Assert.Empty(stand.Reconnects);
        Assert.Empty(stand.Deletes);
    }

    [AvaloniaFact]
    public void A_Refusing_Port_Is_Not_Empty_Canvas()
    {
        // Выход узла 3 не примет конец у входа: это отказ, а не пустота, и связь не удаляется.
        var stand = new Stand();

        stand.Drag(0.8, stand.Pin(3, PortDirection.Output));

        Assert.Empty(stand.Reconnects);
        Assert.Empty(stand.Deletes);
    }

    [AvaloniaFact]
    public void The_Host_Can_Veto_A_Reconnect()
    {
        var stand = new Stand();
        stand.Editor.ConnectValidating += (_, e) => e.IsAllowed = !Equals(e.Target, NodeStand.In(3));
        var target = stand.Nodes.PortOf(3, PortDirection.Input);

        stand.Press(stand.On(0.8));
        stand.MoveTo(stand.Pin(3, PortDirection.Input));
        Assert.Contains(":refusing", target.Classes);
        stand.Release(stand.Pin(3, PortDirection.Input));

        Assert.Empty(stand.Reconnects);
        Assert.Empty(stand.Deletes);
    }

    [AvaloniaFact]
    public void Validation_Names_The_Link_Being_Reconnected()
    {
        // Правило «во вход — одна связь», а вход узла 1 занят этой же связью: не зная, какую
        // связь перецепляют, приложение отказало бы ей перенести свой выход.
        var stand = new Stand();
        var asked = new List<object?>();
        stand.Editor.ConnectValidating += (_, e) =>
        {
            asked.Add(e.Link);
            e.IsAllowed = !stand.Nodes.Links.OfType<LinkData>()
                .Any(l => !ReferenceEquals(l, e.Link) && Equals(l.To, e.Target));
        };

        stand.Drag(0.2, stand.Pin(2, PortDirection.Output));

        Assert.NotEmpty(asked);
        Assert.All(asked, link => Assert.Same(stand.Data, link));
        var request = Assert.Single(stand.Reconnects);
        Assert.Equal(NodeStand.Out(2), request.NewPort);
    }

    [AvaloniaFact]
    public void A_Click_Below_The_Threshold_Only_Selects()
    {
        var stand = new Stand();
        var at = stand.On(0.8);

        stand.Press(at);
        stand.MoveTo(at + new Vector(1, 1));
        stand.Release(at + new Vector(1, 1));

        Assert.Equal([stand.Data], stand.Editor.SelectedLinks);
        Assert.IsType<EditorIdleState>(stand.Editor.CurrentState);
        Assert.Empty(stand.Reconnects);
        Assert.Empty(stand.Deletes);
    }

    [AvaloniaFact]
    public void Escape_Cancels_A_Detach()
    {
        var stand = new Stand();
        var loose = new Point(700, 540);

        stand.Press(stand.On(0.8));
        stand.MoveTo(loose);
        stand.Nodes.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.IsType<EditorIdleState>(stand.Editor.CurrentState);
        Assert.DoesNotContain(":detaching", stand.Link.Classes);
        Assert.False(stand.Editor.PendingPreview!.IsVisible);

        stand.Release(loose);
        Assert.Empty(stand.Deletes);
        Assert.Empty(stand.Reconnects);
    }

    [AvaloniaFact]
    public void Escape_Before_The_Threshold_Cancels_The_Press()
    {
        // Иначе Escape снял бы выбор, а следующее движение всё равно отцепило бы конец.
        var stand = new Stand();
        var loose = new Point(700, 540);

        stand.Press(stand.On(0.8));
        stand.Nodes.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        stand.MoveTo(loose);
        stand.Release(loose);

        Assert.Empty(stand.Deletes);
        Assert.Empty(stand.Reconnects);
    }

    [AvaloniaFact]
    public void The_Additive_Press_Does_Not_Detach()
    {
        var stand = new Stand();
        var from = stand.On(0.8);
        var loose = new Point(700, 540);

        stand.Nodes.Window.MouseMove(from, RawInputModifiers.Shift);
        stand.Nodes.Window.MouseDown(from, MouseButton.Left, RawInputModifiers.Shift);
        stand.Nodes.Window.MouseMove(loose, RawInputModifiers.Shift);
        stand.Nodes.Window.MouseUp(loose, MouseButton.Left, RawInputModifiers.Shift);

        Assert.Empty(stand.Deletes);
        Assert.Equal([stand.Data], stand.Editor.SelectedLinks);
    }

    [AvaloniaFact]
    public void Reconnect_Stops_At_The_First_Handler()
    {
        // Первый заменил элемент; второму достался бы элемент, которого в коллекции уже нет.
        var stand = new Stand(record: false);
        var late = 0;
        stand.Editor.ReconnectRequested += (_, e) =>
        {
            var data = (LinkData)e.Link;
            stand.Nodes.Links[stand.Nodes.Links.IndexOf(data)] = data with { To = e.NewPort };
            e.Handled = true;
        };
        stand.Editor.ReconnectRequested += (_, _) => late++;

        stand.Drag(0.8, stand.Pin(3, PortDirection.Input));

        Assert.Equal(0, late);
    }

    [AvaloniaFact]
    public void A_Link_Leaving_Mid_Press_Is_Not_Detached()
    {
        var stand = new Stand();
        var loose = new Point(700, 540);

        stand.Press(stand.On(0.8));
        stand.Nodes.Links.Remove(stand.Data);
        stand.Nodes.RunLayout();
        stand.MoveTo(loose);

        Assert.IsType<EditorIdleState>(stand.Editor.CurrentState);
        stand.Release(loose);
        Assert.Empty(stand.Deletes);
    }

    [AvaloniaFact]
    public void A_Link_Losing_Its_Port_Mid_Press_Is_Not_Detached()
    {
        // Уходит узел 1 — с портом того конца, который отцепился бы: связь уже не видна.
        var stand = new Stand();
        var loose = new Point(700, 540);

        stand.Press(stand.On(0.8));
        stand.Nodes.Items.RemoveAt(1);
        stand.Nodes.RunLayout();
        stand.MoveTo(loose);

        Assert.IsType<EditorIdleState>(stand.Editor.CurrentState);
        stand.Release(loose);
        Assert.Empty(stand.Deletes);
    }

    [AvaloniaFact]
    public void The_Capture_Passes_To_The_Detach_Without_A_Loss()
    {
        // Потеря захвата посреди жеста объявила бы его брошенным. Один раз она случается — на
        // отпускании, когда жест действительно кончился.
        var stand = new Stand();
        var losses = 0;
        stand.Editor.PointerCaptureLost += (_, _) => losses++;
        var loose = new Point(640, 330);

        stand.Press(stand.On(0.8));
        stand.MoveTo(loose);
        Assert.Equal(0, losses);

        stand.Release(loose);
        Assert.Equal(1, losses);
    }
}
