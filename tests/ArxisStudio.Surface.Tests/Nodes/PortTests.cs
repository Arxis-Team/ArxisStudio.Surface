using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Xunit;
using ArxisStudio.Surface.Nodes;

namespace ArxisStudio.Tests;

/// <summary>
/// Порты находятся по своим данным, и конец связи у них — в мировых координатах (ADR 0004).
/// </summary>
/// <remarks>
/// Ожидаемый конец считается независимо от редактора: центр штырька, переведённый в координаты
/// панели узлов. Редактор же складывает положение узла и положение штырька внутри него — две
/// разные дороги к одной точке.
/// </remarks>
public class PortTests
{
    private static string Out(int index) => NodeStand.NodeName(index) + ".out";

    private static string In(int index) => NodeStand.NodeName(index) + ".in";

    private static NodeStand Create(params Point[] locations) =>
        NodeStand.Create(locations, itemTemplate: NodeStand.PortedNode);

    private static Point AnchorOf(NodeStand stand, string key)
    {
        Assert.True(stand.Editor.TryGetLinkEnd(key, LinkEnd.Source, out var anchor), $"Порт «{key}» обязан быть на поверхности.");
        return anchor;
    }

    [AvaloniaFact]
    public void The_Anchor_Is_The_Pin_Centre_In_World_Coordinates()
    {
        var stand = Create(new Point(100, 100));

        Assert.Equal(stand.PinCentreInWorld(0, PortDirection.Output), AnchorOf(stand, Out(0)));
        Assert.Equal(stand.PinCentreInWorld(0, PortDirection.Input), AnchorOf(stand, In(0)));

        // Вход слева, выход справа — связь идёт слева направо.
        Assert.True(AnchorOf(stand, In(0)).X < AnchorOf(stand, Out(0)).X);
    }

    [AvaloniaFact]
    public void The_Anchor_Follows_The_Node()
    {
        var stand = Create(new Point(100, 100));
        var before = AnchorOf(stand, Out(0));

        stand.Node(0).Location = new Point(150, 120);

        // Ещё до прохода раскладки: узел сдвигается целиком, порт внутри него — нет.
        Assert.Equal(before + new Vector(50, 20), AnchorOf(stand, Out(0)));

        stand.RunLayout();
        Assert.Equal(stand.PinCentreInWorld(0, PortDirection.Output), AnchorOf(stand, Out(0)));
    }

    [AvaloniaFact]
    public void Zoom_And_Pan_Do_Not_Move_The_Anchor()
    {
        var stand = Create(new Point(100, 100));
        var before = AnchorOf(stand, Out(0));

        stand.Editor.ViewportZoom = 2;
        stand.Editor.ViewportLocation = new Point(-40, 30);
        stand.RunLayout();

        Assert.Equal(before, AnchorOf(stand, Out(0)));
    }

    [AvaloniaFact]
    public void A_Removed_Node_Takes_Its_Ports_And_A_Returned_One_Brings_Them_Back()
    {
        var stand = Create(new Point(100, 100), new Point(300, 100));
        var removed = stand.Items[1];

        stand.Items.RemoveAt(1);
        stand.RunLayout();
        Assert.False(stand.Editor.TryGetLinkEnd(Out(1), LinkEnd.Source, out _), "Порт убранного узла обязан уйти из реестра.");

        stand.Items.Add(removed);
        stand.RunLayout();
        Assert.True(stand.Editor.TryGetLinkEnd(Out(1), LinkEnd.Source, out _), "Вернувшийся узел обязан вернуть свои порты.");
    }

    [AvaloniaFact]
    public void Data_Wins_Over_The_Data_Context()
    {
        var template = new FuncDataTemplate<string>((name, _) => new StackPanel
        {
            Children =
            {
                // Без Data ключ — DataContext, то есть сам узел; с Data — заданное.
                new Port { Direction = PortDirection.Output },
                new Port { Direction = PortDirection.Input, Data = "явный" }
            }
        }, supportsRecycling: false);
        var stand = NodeStand.Create([new Point(100, 100)], itemTemplate: template);

        Assert.True(stand.Editor.TryGetLinkEnd(NodeStand.NodeName(0), LinkEnd.Source, out _), "Порт без Data ключуется своим DataContext.");
        Assert.True(stand.Editor.TryGetLinkEnd("явный", LinkEnd.Source, out _), "Порт с Data ключуется им.");
    }

    [AvaloniaFact]
    public void Changing_Data_Moves_The_Port_To_The_New_Key()
    {
        var stand = Create(new Point(100, 100));
        var port = stand.PortOf(0, PortDirection.Output);

        port.Data = "другой";

        Assert.False(stand.Editor.TryGetLinkEnd(Out(0), LinkEnd.Source, out _));
        Assert.True(stand.Editor.TryGetLinkEnd("другой", LinkEnd.Source, out _));
    }

    [AvaloniaFact]
    public void Of_Two_Ports_With_One_Key_The_First_Answers()
    {
        var template = new FuncDataTemplate<string>((_, _) => new StackPanel
        {
            Children =
            {
                new Port { Direction = PortDirection.Output, Data = "общий", Content = "первый" },
                new Port { Direction = PortDirection.Output, Data = "общий", Content = "второй" }
            }
        }, supportsRecycling: false);
        var stand = NodeStand.Create([new Point(100, 100)], itemTemplate: template);

        Assert.Equal("первый", stand.Editor.Ports.Find("общий")!.Content);
    }

    [AvaloniaFact]
    public void Replacing_A_Port_Does_Not_Lose_The_Newcomer()
    {
        // Пересозданный контейнер регистрирует новый порт раньше, чем уходит старый. Снятие
        // обязано убирать ровно тот экземпляр, что уходит, а не всё, что лежит на ключе.
        var registry = new PortRegistry();
        var old = new Port();
        var newcomer = new Port();

        registry.Register("ключ", old);
        registry.Register("ключ", newcomer);
        registry.Unregister("ключ", old);

        Assert.Same(newcomer, registry.Find("ключ"));
    }

    [AvaloniaFact]
    public void Direction_Shows_As_A_Pseudo_Class()
    {
        var port = new Port { Direction = PortDirection.Output };
        Assert.Contains(":output", port.Classes);
        Assert.DoesNotContain(":input", port.Classes);

        port.Direction = PortDirection.Input;
        Assert.Contains(":input", port.Classes);
        Assert.DoesNotContain(":output", port.Classes);
    }
}
