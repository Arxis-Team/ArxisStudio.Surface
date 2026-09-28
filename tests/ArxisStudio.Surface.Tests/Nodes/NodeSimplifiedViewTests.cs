using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface.Nodes;

namespace ArxisStudio.Tests;

/// <summary>
/// Связи в упрощённом виде (ADR 0008): контрол — только у закреплённой связи и у связи развёрнутого
/// узла, остальные рисует слой.
/// </summary>
/// <remarks>
/// Стенд: окно 800 × 600, редактор узлов над сеткой 10 × 10 узлов 120 × 80 с шагом 200 × 120 и
/// связями цепочкой внутри ряда; ещё одна, длинная, идёт из первого узла ряда в четвёртый — под вторым
/// и третьим. Привязки положения и узла порта заданы; масштаб 0,4 — ниже порога, и видно всё.
/// </remarks>
public class NodeSimplifiedViewTests
{
    private const int Columns = 10;
    private static readonly Size NodeSize = new(120, 80);
    private static readonly IDataTemplate NodeTemplate = GraphTemplates.Node(NodeSize);

    private sealed record Stand(Window Window, NodeEditor Editor, ObservableCollection<object> Nodes, ObservableCollection<object> Links)
    {
        public LinkModel Long { get; init; } = null!;

        public NodeModel Node(int index) => (NodeModel)Nodes[index];

        public LinkRecord Record(object link) => Editor.RecordOf(link)!;

        public SimplifiedLinkLayer Layer => Editor.GetVisualDescendants().OfType<SimplifiedLinkLayer>().Single();

        public void RunLayout()
        {
            var manager = Window.GetLayoutManager();
            manager?.ExecuteInitialLayoutPass();
            manager?.ExecuteLayoutPass();
        }

        public void Render() => Window.CaptureRenderedFrame();

        public Point Screen(Point world) => new(world.X * Editor.ViewportZoom, world.Y * Editor.ViewportZoom);
    }

    private static Stand Create()
    {
        var nodes = new ObservableCollection<object>();
        for (var i = 0; i < Columns * Columns; i++)
            nodes.Add(new NodeModel("Узел " + i, new Point(i % Columns * 200, i / Columns * 120)));

        var links = new ObservableCollection<object>();
        for (var i = 0; i + 1 < nodes.Count; i++)
        {
            if ((i + 1) % Columns != 0)
                links.Add(new LinkModel(((NodeModel)nodes[i]).Out, ((NodeModel)nodes[i + 1]).In));
        }

        var longLink = new LinkModel(((NodeModel)nodes[0]).Out, ((NodeModel)nodes[3]).In);
        links.Add(longLink);

        var editor = new NodeEditor
        {
            ItemsSource = nodes,
            Links = links,
            ItemTemplate = NodeTemplate,
            ItemLocationBinding = new Binding(nameof(NodeModel.Location)),
            PortNodeBinding = new Binding(nameof(PortModel.Node)),
            EstimatedItemSize = NodeSize,
            LinkSourceBinding = new Binding(nameof(LinkModel.From)),
            LinkTargetBinding = new Binding(nameof(LinkModel.To))
        };
        editor.InteractionOptions.IsSnapToGridEnabled = false;
        editor.InteractionOptions.IsSnapToGuidesEnabled = false;

        var window = new Window { Width = 800, Height = 600, Content = editor };
        window.Show();

        var stand = new Stand(window, editor, nodes, links) { Long = longLink };
        stand.RunLayout();
        editor.ViewportZoom = 0.4;
        stand.RunLayout();
        return stand;
    }

    /// <summary>
    /// Середина кривой связи в мировых координатах.
    /// </summary>
    private static Point Middle(LinkRecord record)
    {
        var g = record.Geometry;
        return new Point(
            (0.125 * g.Source.X) + (0.375 * g.SourceControl.X) + (0.375 * g.TargetControl.X) + (0.125 * g.Target.X),
            (0.125 * g.Source.Y) + (0.375 * g.SourceControl.Y) + (0.375 * g.TargetControl.Y) + (0.125 * g.Target.Y));
    }

    /// <summary>
    /// Ставит второй узел ряда так, чтобы его карточка накрыла середину длинной связи.
    /// </summary>
    private static Point CoverTheLongLink(Stand stand)
    {
        var middle = Middle(stand.Record(stand.Long));
        stand.Node(1).Location = middle - new Vector(NodeSize.Width / 2, NodeSize.Height / 2);
        stand.RunLayout();
        return middle;
    }

    [AvaloniaFact]
    public void A_Link_Has_A_Control_Only_At_A_Realized_Node_Or_When_Pinned()
    {
        var stand = Create();
        Assert.True(stand.Editor.IsSimplified);
        Assert.Equal(0, stand.Editor.RealizedLinks);

        // Выбранный узел развёрнут — его две связи рисуются вживую.
        stand.Editor.Selection.Select(22);
        stand.RunLayout();
        Assert.Equal(2, stand.Editor.RealizedLinks);
        Assert.NotNull(stand.Record(stand.Links[(2 * (Columns - 1)) + 1]).Control);

        // Подсвеченная связь закреплена жестом.
        var far = stand.Record(stand.Links[60]);
        stand.Editor.SetLinkHighlighted(far, true);
        stand.RunLayout();
        Assert.Equal(3, stand.Editor.RealizedLinks);

        stand.Editor.SetLinkHighlighted(far, false);
        stand.Editor.Selection.Clear();
        stand.RunLayout();
        Assert.Equal(0, stand.Editor.RealizedLinks);
    }

    [AvaloniaFact]
    public void A_Link_Crossing_The_View_Collapses_When_The_View_Simplifies()
    {
        // Над порогом связь, пересекающая окно, развёрнута без своих узлов; ниже порога живого порта у
        // неё нет, и контрол уходит — хотя ни один узел не развернулся и не свернулся.
        var a = new NodeModel("A", new Point(0, 0));
        var b = new NodeModel("B", new Point(3000, 0));
        var link = new LinkModel(a.Out, b.In);
        var editor = new NodeEditor
        {
            ItemsSource = new ObservableCollection<object> { a, b },
            Links = new ObservableCollection<object> { link },
            ItemTemplate = NodeTemplate,
            ItemLocationBinding = new Binding(nameof(NodeModel.Location)),
            PortNodeBinding = new Binding(nameof(PortModel.Node)),
            EstimatedItemSize = NodeSize,
            LinkSourceBinding = new Binding(nameof(LinkModel.From)),
            LinkTargetBinding = new Binding(nameof(LinkModel.To)),
            ViewportLocation = new Point(1100, -200)
        };
        var window = new Window { Width = 800, Height = 600, Content = editor };
        window.Show();
        var stand = new Stand(window, editor, new(), new());
        stand.RunLayout();
        Assert.Empty(editor.GetRealizedContainers());
        Assert.Equal(1, editor.RealizedLinks);

        editor.ViewportZoom = 0.4;
        stand.RunLayout();

        Assert.Empty(editor.GetRealizedContainers());
        Assert.Equal(0, editor.RealizedLinks);
    }

    [AvaloniaFact]
    public void Panning_Below_The_Threshold_Measures_No_Links()
    {
        // Контролы связей держит живой порт, а не окно: панорама им ничего не меняет.
        var stand = Create();
        stand.Editor.Selection.Select(22);
        stand.RunLayout();
        var panel = stand.Editor.GetVisualDescendants().OfType<LinkPanel>().Single();
        var measured = panel.MeasuredChildren;

        stand.Editor.ViewportLocation = new Point(300, 200);
        stand.RunLayout();
        stand.Editor.ViewportLocation = new Point(900, 600);
        stand.RunLayout();

        Assert.Equal(measured, panel.MeasuredChildren);
        Assert.Equal(2, stand.Editor.RealizedLinks);
    }

    [AvaloniaFact]
    public void The_Layer_Draws_The_Links_Without_Controls_And_The_Selected_Apart()
    {
        var stand = Create();
        stand.Render();
        Assert.Equal(stand.Links.Count, stand.Layer.Links);
        Assert.Equal(0, stand.Layer.SelectedLinks);

        stand.Editor.SelectLink(stand.Links[40]);
        stand.RunLayout();
        stand.Render();
        Assert.Equal(stand.Links.Count - 1, stand.Layer.Links);
        Assert.Equal(1, stand.Layer.SelectedLinks);

        // Ушедшая связь уходит и со слоя.
        stand.Links.RemoveAt(0);
        stand.RunLayout();
        stand.Render();
        Assert.Equal(stand.Links.Count - 1, stand.Layer.Links);
    }

    [AvaloniaFact]
    public void A_Dragged_Node_Carries_Its_Links_Live_Without_Rebuilding_The_Layer()
    {
        // Первый кадр разворачивает узел и его связи — слой собирается без них; дальше связи едут
        // своими контролами, и слой не пересобирается.
        var stand = Create();
        stand.Render();
        var grip = stand.Screen(stand.Node(22).Location + new Vector(NodeSize.Width / 2, 10));

        stand.Window.MouseDown(grip, MouseButton.Left);
        stand.Window.MouseMove(grip + new Vector(10, 5));
        stand.RunLayout();
        stand.Render();
        Assert.Equal(2, stand.Editor.RealizedLinks);
        var rebuilds = stand.Layer.Rebuilds;

        for (var i = 1; i <= 5; i++)
        {
            stand.Window.MouseMove(grip + new Vector(10 + (i * 8), 5));
            stand.RunLayout();
            stand.Render();
        }

        stand.Window.MouseUp(grip + new Vector(50, 5), MouseButton.Left);
        Assert.Equal(rebuilds, stand.Layer.Rebuilds);
        Assert.Equal(stand.Links.Count - 2, stand.Layer.Links);
    }

    [AvaloniaFact]
    public void Hovering_A_Card_Does_Not_Highlight_The_Link_Beneath()
    {
        var stand = Create();
        var middle = CoverTheLongLink(stand);

        stand.Window.MouseMove(stand.Screen(middle));

        Assert.False(stand.Record(stand.Long).IsHighlighted);
    }

    [AvaloniaFact]
    public void A_Click_On_A_Card_Over_A_Link_Selects_The_Node()
    {
        // Нажатие разворачивает узел раньше, чем редактор ищет связь под указателем.
        var stand = Create();
        var middle = CoverTheLongLink(stand);

        stand.Window.MouseDown(stand.Screen(middle), MouseButton.Left);
        stand.Window.MouseUp(stand.Screen(middle), MouseButton.Left);
        stand.RunLayout();

        Assert.True(stand.Editor.Selection.IsSelected(1));
        Assert.Empty(stand.Editor.SelectedLinks);
    }
}
