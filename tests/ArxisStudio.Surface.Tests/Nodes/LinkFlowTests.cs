using Avalonia;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface.Nodes;

namespace ArxisStudio.Tests;

/// <summary>
/// Поток по проводам (ADR 0014): импульсы отладки, как пузыри на проводах исполнения Blueprint, и
/// маркеры направления — с выключателями у каждого.
/// </summary>
/// <remarks>
/// Два узла с портами и связь между ними. Время в безголовом режиме стоит, поэтому часы импульсов
/// тест ставит сам, а кадр рисует снимком окна.
/// </remarks>
public class LinkFlowTests
{
    private sealed class Stand
    {
        public required NodeStand Nodes { get; init; }

        public required LinkData Link { get; init; }

        public TimeSpan Now { get; set; }

        public NodeEditor Editor => Nodes.Editor;

        public LinkFlowLayer Layer => Editor.GetVisualDescendants().OfType<LinkFlowLayer>().Single();

        public void Frame() => Nodes.Window.CaptureRenderedFrame();

        public void At(double seconds)
        {
            Now = TimeSpan.FromSeconds(seconds);
            Frame();
        }
    }

    private static Stand Create()
    {
        var nodes = NodeStand.Create([new Point(40, 100), new Point(460, 160)], itemTemplate: NodeStand.PortedNode);
        var stand = new Stand { Nodes = nodes, Link = nodes.Connect(0, 1) };
        nodes.Editor.PulseClock = () => stand.Now;
        return stand;
    }

    [AvaloniaFact]
    public void A_Pulse_Lights_The_Wire_Runs_Along_It_And_Fades_Out()
    {
        var stand = Create();
        Assert.True(stand.Editor.PulseLink(stand.Link));
        stand.At(0.5);

        Assert.Equal(1, stand.Layer.DrawnGlows);
        Assert.True(stand.Layer.DrawnPulseShapes > 1, $"фигур на проводе {stand.Layer.DrawnPulseShapes}");

        // Секунда в полную силу и 400 мс угасания — дальше импульса нет, и слой ничего не рисует.
        stand.At(1.5);
        Assert.Equal(0, stand.Editor.ActivePulses);
        Assert.Equal(0, stand.Layer.DrawnGlows + stand.Layer.DrawnPulseShapes);
    }

    [AvaloniaFact]
    public void Pulse_Shapes_Run_From_The_Output_To_The_Input()
    {
        var stand = Create();
        var source = stand.Nodes.PinCentreInWorld(0, PortDirection.Output);
        stand.Editor.PulseLink(stand.Link);

        double Lead()
        {
            // Фигура, ближайшая к выходу, — насколько она от него ушла.
            return stand.Layer.PulsePoints.Min(p => Math.Abs(p.X - source.X) + Math.Abs(p.Y - source.Y));
        }

        stand.At(0.05);
        var early = Lead();
        stand.At(0.25);
        var later = Lead();

        Assert.True(later > early, $"фигура у выхода была в {early:F1}, стала в {later:F1}: бег назад");
    }

    [AvaloniaFact]
    public void Pulsing_Again_Keeps_A_Firing_Wire_Lit()
    {
        // Провод, который срабатывает снова, горит ровно, а не гаснет по первому вызову.
        var stand = Create();
        stand.Editor.PulseLink(stand.Link);
        stand.At(1.2);
        stand.Editor.PulseLink(stand.Link);
        stand.At(1.9);

        Assert.Equal(1, stand.Editor.ActivePulses);
        Assert.Equal(1, stand.Layer.DrawnGlows);
    }

    [AvaloniaFact]
    public void Switching_Pulses_Off_Clears_The_Lit_Ones_And_Ignores_New()
    {
        var stand = Create();
        stand.Editor.PulseLink(stand.Link);
        stand.At(0.2);
        Assert.Equal(1, stand.Layer.DrawnGlows);

        stand.Editor.IsLinkPulseEnabled = false;
        stand.At(0.3);
        Assert.Equal(0, stand.Editor.ActivePulses);
        Assert.Equal(0, stand.Layer.DrawnGlows + stand.Layer.DrawnPulseShapes);

        Assert.False(stand.Editor.PulseLink(stand.Link), "выключенный редактор импульс зажёг");
        Assert.Equal(0, stand.Editor.ActivePulses);
    }

    [AvaloniaFact]
    public void A_Pulse_Has_Its_Own_Look_And_A_Removed_Link_Drops_It()
    {
        var stand = Create();
        var look = new LinkPulse { Shape = LinkShape.Arrow, Spacing = 20, GlowThickness = 0 };
        stand.Editor.PulseLink(stand.Link, look);
        stand.At(0.2);

        Assert.Equal(0, stand.Layer.DrawnGlows);
        var dense = stand.Layer.DrawnPulseShapes;
        Assert.True(dense > 10, $"шаг 20 поставил {dense} фигур");

        stand.Nodes.Links.Remove(stand.Link);
        stand.At(0.3);
        Assert.Equal(0, stand.Editor.ActivePulses);
        Assert.False(stand.Editor.PulseLink(stand.Link), "импульс зажёгся на связи вне коллекции");
    }

    [AvaloniaFact]
    public void Without_Pulses_The_Layer_Asks_For_No_Frames()
    {
        // Кадр за кадром слой просит только ради горящего импульса.
        var stand = Create();
        stand.Editor.LinkMarker = new LinkMarker();
        stand.At(0);
        Assert.Equal(0, stand.Layer.FramesRequested);

        stand.Editor.PulseLink(stand.Link);
        stand.At(0.1);
        Assert.True(stand.Layer.FramesRequested > 0, "горящий импульс не попросил кадра");

        // Погас — просьбы кончаются.
        stand.At(2);
        var asked = stand.Layer.FramesRequested;
        stand.At(2.1);
        Assert.Equal(asked, stand.Layer.FramesRequested);
    }

    [AvaloniaFact]
    public void Markers_Follow_The_Setting_And_Hide_Without_Losing_It()
    {
        var stand = Create();
        stand.Frame();
        Assert.Equal(0, stand.Layer.DrawnMarkers);

        stand.Editor.LinkMarker = new LinkMarker { Shape = LinkShape.Chevron };
        stand.Frame();
        Assert.Equal(1, stand.Layer.DrawnMarkers);

        stand.Editor.AreLinkMarkersVisible = false;
        stand.Frame();
        Assert.Equal(0, stand.Layer.DrawnMarkers);

        stand.Editor.AreLinkMarkersVisible = true;
        stand.Frame();
        Assert.Equal(1, stand.Layer.DrawnMarkers);

        stand.Editor.LinkMarker = new LinkMarker { Spacing = 40 };
        stand.Frame();
        Assert.True(stand.Layer.DrawnMarkers > 5, $"шаг 40 поставил {stand.Layer.DrawnMarkers} маркеров");
    }

    [AvaloniaFact]
    public void A_Marker_Too_Small_On_Screen_Is_Not_Drawn()
    {
        var stand = Create();
        stand.Editor.LinkMarker = new LinkMarker { Size = 10 };
        stand.Editor.ViewportZoom = 0.35;
        stand.Frame();

        Assert.Equal(0, stand.Layer.DrawnMarkers);
    }

    /// <summary>Связь, которая сама говорит, нужен ли ей маркер.</summary>
    private sealed record MarkedLink(object From, object To, LinkMarker? Marker);

    [AvaloniaFact]
    public void A_Marker_Binding_Gives_Each_Link_Its_Own()
    {
        // Провод исполнения несёт стрелку, провод данных — ничего; маркер редактора при привязке не
        // действует.
        var nodes = NodeStand.Create([new Point(40, 100), new Point(460, 160), new Point(460, 360)], itemTemplate: NodeStand.PortedNode);
        nodes.Editor.LinkMarkerBinding = new Binding(nameof(MarkedLink.Marker));
        nodes.Editor.LinkMarker = new LinkMarker();
        nodes.Links.Add(new MarkedLink(NodeStand.Out(0), NodeStand.In(1), new LinkMarker()));
        nodes.Links.Add(new MarkedLink(NodeStand.Out(0), NodeStand.In(2), null));
        nodes.RunLayout();

        nodes.Window.CaptureRenderedFrame();
        var layer = nodes.Editor.GetVisualDescendants().OfType<LinkFlowLayer>().Single();
        Assert.Equal(1, layer.DrawnMarkers);
    }
}
