using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface.Nodes;

namespace ArxisStudio.Tests;

/// <summary>
/// Концы связей у портов, которые лежат глубоко в узле, — так, как их раскладывает обычный хост.
/// </summary>
/// <remarks>
/// Найдено живой проверкой демо: связи в разные входы одного узла сходились в одну точку у его
/// левого верхнего угла. Порт стоял в списке под заголовком, узел ставился до первой раскладки, и
/// конец считался в тот миг, когда границы получил порт, но ещё не получили его предки в узле, —
/// а позже его не пересчитывало ничто. Стенд <see cref="NodeStand.Create"/> этого не ловил: порты у
/// него лежат прямо в панели узла, а узлы ставятся после первой раскладки, и сдвиг пересчитывал всё.
/// </remarks>
public class NestedPortTests
{
    private static NodeStand Create() => NodeStand.CreatePlacedByHost(
        [new Point(100, 100), new Point(420, 220)],
        NodeStand.ListedPorts,
        new LinkData(NodeStand.Out(0), NodeStand.In(1)),
        new LinkData(NodeStand.Out(0), NodeStand.NodeName(1) + ".in2"));

    [AvaloniaFact]
    public void Links_Land_On_The_Pins_Of_Ports_Listed_In_A_Node()
    {
        var stand = Create();

        foreach (var data in stand.Links.OfType<LinkData>())
        {
            var link = stand.LinkOf(data);
            Assert.Equal(stand.PinCentreOf(data.From), link.SourceAnchor);
            Assert.Equal(stand.PinCentreOf(data.To), link.TargetAnchor);
        }

        var ends = stand.Links.OfType<LinkData>().Select(d => stand.LinkOf(d).TargetAnchor).Distinct().Count();
        Assert.Equal(2, ends);
    }

    [AvaloniaFact]
    public void A_Port_Moving_Inside_Its_Node_Takes_Its_Links_Along()
    {
        // Заголовок вырос — списки портов съехали вниз, а собственные границы портов не изменились:
        // они отсчитываются от контейнеров списка.
        var stand = Create();
        var title = stand.Node(1).GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "Title");
        var before = stand.LinkOf(stand.Links[0]).TargetAnchor;

        title.Margin = new Thickness(0, 0, 0, 40);
        stand.RunLayout();

        var link = stand.LinkOf(stand.Links[0]);
        Assert.Equal(stand.PinCentreOf(NodeStand.In(1)), link.TargetAnchor);
        Assert.Equal(before.Y + 40, link.TargetAnchor.Y, precision: 3);
    }

    [AvaloniaFact]
    public void A_Port_Rearranged_On_Its_Own_Takes_Its_Links_Along()
    {
        // Выравнивание меняет только расстановку, и заново раскладывается один порт — без узла.
        // Короткий вход «in» лежит в списке, растянутом по длинному «in2», и прижатый вправо
        // сдвигается внутри своего места.
        var stand = Create();
        var port = stand.Editor.GetVisualDescendants().OfType<Port>().Single(p => Equals(p.Data, NodeStand.In(1)));
        var before = stand.LinkOf(stand.Links[0]).TargetAnchor;

        port.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        stand.RunLayout();

        var after = stand.LinkOf(stand.Links[0]).TargetAnchor;
        Assert.Equal(stand.PinCentreOf(NodeStand.In(1)), after);
        Assert.True(after.X > before.X, $"{after.X} против {before.X}");
    }

    [AvaloniaFact]
    public void Dragging_A_Node_Still_Costs_Its_Degree_Once()
    {
        // Пересчёт после раскладки узла не должен удвоить кадр перетаскивания: сдвиг узла переносит
        // концы сразу, а раскладка узла, чьи порты внутри него не сдвинулись, не пересчитывает ничего.
        var stand = Create();
        var grip = stand.Node(1).Location + new Vector(60, 8);
        stand.Window.MouseMove(grip);
        stand.Window.MouseDown(grip, Avalonia.Input.MouseButton.Left);
        stand.Window.MouseMove(grip + new Vector(10, 10));
        stand.RunLayout();

        var before = stand.Editor.LinkUpdates;
        stand.Window.MouseMove(grip + new Vector(30, 10));
        stand.RunLayout();

        Assert.Equal(2, stand.Editor.LinkUpdates - before);
        stand.Window.MouseUp(grip + new Vector(30, 10), Avalonia.Input.MouseButton.Left);
    }
}
