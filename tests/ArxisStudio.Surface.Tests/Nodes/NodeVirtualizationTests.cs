using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface.Nodes;

namespace ArxisStudio.Tests;

/// <summary>
/// Концы связей к узлам без контейнера (ADR 0007): смещение порта с последнего показа и оценка по
/// краю узла, который ни разу не показывался.
/// </summary>
/// <remarks>
/// Стенд: окно 800 × 600, редактор узлов с привязкой положения над моделями. Узел — 120 × 80, и
/// предполагаемый размер тот же; порты «in» и «out» ключуются своими моделями, а модель порта знает
/// свой узел. Узел A стоит в (100, 100) и виден, узел B — в (3000, 100), далеко за краем; связи
/// идут A.out → B.in и B.out → A.in. Ещё дальше — пара C (3000, 1000) и D (3400, 1000) со связью
/// C.out → D.in: у неё ни один конец не показывался.
/// </remarks>
public class NodeVirtualizationTests
{
    private static readonly Size NodeSize = new(120, 80);

    private sealed class NodeModel : INotifyPropertyChanged
    {
        private Point _location;

        public NodeModel(string name, Point location)
        {
            Name = name;
            _location = location;
            In = new PortModel(this, "in");
            Out = new PortModel(this, "out");
        }

        public string Name { get; }

        public PortModel In { get; }

        public PortModel Out { get; }

        public Point Location
        {
            get => _location;
            set
            {
                if (_location == value)
                    return;

                _location = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Location)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class PortModel(NodeModel node, string name)
    {
        public NodeModel Node { get; } = node;

        public override string ToString() => Node.Name + "." + name;
    }

    private sealed record LinkModel(PortModel From, PortModel To);

    // Переработка очищает содержимое контейнера, пока он ещё в дереве, и шаблон строится и с пустыми
    // данными — как шаблон разметки, он обязан это переносить.
    private static readonly IDataTemplate NodeTemplate = new FuncDataTemplate<NodeModel?>((node, _) => new StackPanel
    {
        Width = NodeSize.Width,
        Height = NodeSize.Height,
        Children =
        {
            new TextBlock { Text = node?.Name },
            new Port { Direction = PortDirection.Input, Data = node?.In, Content = "in" },
            new Port { Direction = PortDirection.Output, Data = node?.Out, Content = "out" }
        }
    }, supportsRecycling: false);

    private sealed record Stand(Window Window, NodeEditor Editor, ObservableCollection<object> Nodes, NodeModel A, NodeModel B)
    {
        public LinkModel AToB { get; init; } = null!;

        public LinkModel BToA { get; init; } = null!;

        public LinkModel CToD { get; init; } = null!;

        public Link LinkOf(LinkModel model) =>
            Editor.GetVisualDescendants().OfType<Link>().Single(l => ReferenceEquals(l.DataContext, model));

        public void RunLayout()
        {
            var manager = Window.GetLayoutManager();
            manager?.ExecuteInitialLayoutPass();
            manager?.ExecuteLayoutPass();
        }

        public void Pan(Point location)
        {
            Editor.ViewportLocation = location;
            RunLayout();
        }

        /// <summary>
        /// Центр штырька живого порта в мировых координатах, посчитанный мимо редактора.
        /// </summary>
        public Point PinOf(PortModel model)
        {
            var port = Editor.GetVisualDescendants().OfType<Port>().Single(p => ReferenceEquals(p.Data, model));
            var pin = port.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "PART_Pin");
            var panel = (Visual)port.FindAncestorOfType<Node>()!.GetVisualParent()!;
            return pin.TranslatePoint(new Point(pin.Bounds.Width / 2, pin.Bounds.Height / 2), panel)!.Value;
        }
    }

    private static Stand Create(bool portNodeBinding = true)
    {
        var a = new NodeModel("A", new Point(100, 100));
        var b = new NodeModel("B", new Point(3000, 100));
        var c = new NodeModel("C", new Point(3000, 1000));
        var d = new NodeModel("D", new Point(3400, 1000));
        var nodes = new ObservableCollection<object> { a, b, c, d };
        var aToB = new LinkModel(a.Out, b.In);
        var bToA = new LinkModel(b.Out, a.In);
        var cToD = new LinkModel(c.Out, d.In);

        var editor = new NodeEditor
        {
            ItemsSource = nodes,
            Links = new ObservableCollection<object> { aToB, bToA, cToD },
            ItemTemplate = NodeTemplate,
            ItemLocationBinding = new Binding(nameof(NodeModel.Location)),
            PortNodeBinding = portNodeBinding ? new Binding(nameof(PortModel.Node)) : null,
            EstimatedItemSize = NodeSize,
            LinkSourceBinding = new Binding(nameof(LinkModel.From)),
            LinkTargetBinding = new Binding(nameof(LinkModel.To))
        };
        editor.InteractionOptions.IsSnapToGridEnabled = false;
        editor.InteractionOptions.IsSnapToGuidesEnabled = false;

        var window = new Window { Width = 800, Height = 600, Content = editor };
        window.Show();

        var stand = new Stand(window, editor, nodes, a, b) { AToB = aToB, BToA = bToA, CToD = cToD };
        stand.RunLayout();
        return stand;
    }

    [AvaloniaFact]
    public void A_Link_To_A_Node_Never_Shown_Ends_On_The_Middle_Of_Its_Edge()
    {
        // Вход — левый край, выход — правый; двигается модель свёрнутого узла — за ней и концы.
        var stand = Create();
        Assert.Null(stand.Editor.ContainerFromIndex(1));

        Assert.True(stand.LinkOf(stand.AToB).IsVisible);
        Assert.Equal(new Point(3000, 140), stand.LinkOf(stand.AToB).TargetAnchor);
        Assert.Equal(new Point(3120, 140), stand.LinkOf(stand.BToA).SourceAnchor);
        Assert.Equal(stand.PinOf(stand.A.Out), stand.LinkOf(stand.AToB).SourceAnchor);

        stand.B.Location = new Point(3000, 500);
        stand.RunLayout();

        Assert.Equal(new Point(3000, 540), stand.LinkOf(stand.AToB).TargetAnchor);
        Assert.Equal(new Point(3120, 540), stand.LinkOf(stand.BToA).SourceAnchor);
    }

    [AvaloniaFact]
    public void A_Link_Between_Nodes_Never_Shown_Runs_Edge_To_Edge()
    {
        // Ни одного живого порта: связь создаётся раньше, чем панель прочтёт геометрию, и пересчитать
        // её после чтения некому, кроме сигнала панели.
        var stand = Create();

        Assert.True(stand.LinkOf(stand.CToD).IsVisible);
        Assert.Equal(new Point(3120, 1040), stand.LinkOf(stand.CToD).SourceAnchor);
        Assert.Equal(new Point(3400, 1040), stand.LinkOf(stand.CToD).TargetAnchor);
    }

    [AvaloniaFact]
    public void A_Shown_Node_Keeps_The_Offsets_Of_Its_Pins_When_Collapsed()
    {
        // Показанный узел даёт точные штырьки; свёрнутый — те же точки от своего положения, и они
        // едут за моделью.
        var stand = Create();

        stand.Pan(new Point(2800, 0));
        Assert.NotNull(stand.Editor.ContainerFromIndex(1));
        var pinIn = stand.PinOf(stand.B.In);
        var pinOut = stand.PinOf(stand.B.Out);
        Assert.Equal(pinIn, stand.LinkOf(stand.AToB).TargetAnchor);
        Assert.Equal(pinOut, stand.LinkOf(stand.BToA).SourceAnchor);

        stand.Pan(new Point(0, 0));
        Assert.Null(stand.Editor.ContainerFromIndex(1));
        Assert.Equal(pinIn, stand.LinkOf(stand.AToB).TargetAnchor);
        Assert.Equal(pinOut, stand.LinkOf(stand.BToA).SourceAnchor);

        stand.B.Location += new Vector(50, 20);
        stand.RunLayout();

        Assert.Equal(pinIn + new Vector(50, 20), stand.LinkOf(stand.AToB).TargetAnchor);
        Assert.Equal(pinOut + new Vector(50, 20), stand.LinkOf(stand.BToA).SourceAnchor);
    }

    [AvaloniaFact]
    public void Without_PortNodeBinding_A_Link_Waits_For_Its_Node_To_Show()
    {
        var stand = Create(portNodeBinding: false);

        Assert.False(stand.LinkOf(stand.AToB).IsVisible);

        stand.Pan(new Point(2800, 0));
        Assert.True(stand.LinkOf(stand.AToB).IsVisible);
        Assert.Equal(stand.PinOf(stand.B.In), stand.LinkOf(stand.AToB).TargetAnchor);
    }

    [AvaloniaFact]
    public void Collection_Changes_Keep_Link_Ends_On_Their_Nodes()
    {
        // Вставка сдвигает индексы — конец ищется по узлу, а не по прежнему индексу; ушедший свёрнутым
        // узел уносит свои связи.
        var stand = Create();

        stand.Nodes.Insert(0, new NodeModel("C", new Point(5000, 5000)));
        stand.RunLayout();
        stand.B.Location = new Point(3000, 300);
        stand.RunLayout();
        Assert.Equal(new Point(3000, 340), stand.LinkOf(stand.AToB).TargetAnchor);

        stand.Nodes.Remove(stand.B);
        stand.RunLayout();
        Assert.False(stand.LinkOf(stand.AToB).IsVisible);
        Assert.False(stand.LinkOf(stand.BToA).IsVisible);
    }
}
