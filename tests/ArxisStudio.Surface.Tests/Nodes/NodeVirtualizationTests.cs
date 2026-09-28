using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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

    private static readonly IDataTemplate NodeTemplate = GraphTemplates.Node(NodeSize);

    private sealed record Stand(Window Window, NodeEditor Editor, ObservableCollection<object> Nodes, NodeModel A, NodeModel B)
    {
        public LinkModel AToB { get; init; } = null!;

        public LinkModel BToA { get; init; } = null!;

        public LinkModel CToD { get; init; } = null!;

        /// <summary>
        /// Запись связи: при виртуализации контрола у дальней связи нет, а запись есть всегда.
        /// </summary>
        public LinkRecord LinkOf(LinkModel model) => Editor.RecordOf(model)!;

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

        Assert.True(stand.LinkOf(stand.AToB).IsResolved);
        Assert.Equal(new Point(3000, 140), stand.LinkOf(stand.AToB).Geometry.Target);
        Assert.Equal(new Point(3120, 140), stand.LinkOf(stand.BToA).Geometry.Source);
        Assert.Equal(stand.PinOf(stand.A.Out), stand.LinkOf(stand.AToB).Geometry.Source);

        stand.B.Location = new Point(3000, 500);
        stand.RunLayout();

        Assert.Equal(new Point(3000, 540), stand.LinkOf(stand.AToB).Geometry.Target);
        Assert.Equal(new Point(3120, 540), stand.LinkOf(stand.BToA).Geometry.Source);
    }

    [AvaloniaFact]
    public void A_Link_Between_Nodes_Never_Shown_Runs_Edge_To_Edge()
    {
        // Ни одного живого порта: связь создаётся раньше, чем панель прочтёт геометрию, и пересчитать
        // её после чтения некому, кроме сигнала панели.
        var stand = Create();

        Assert.True(stand.LinkOf(stand.CToD).IsResolved);
        Assert.Equal(new Point(3120, 1040), stand.LinkOf(stand.CToD).Geometry.Source);
        Assert.Equal(new Point(3400, 1040), stand.LinkOf(stand.CToD).Geometry.Target);
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
        Assert.Equal(pinIn, stand.LinkOf(stand.AToB).Geometry.Target);
        Assert.Equal(pinOut, stand.LinkOf(stand.BToA).Geometry.Source);

        stand.Pan(new Point(0, 0));
        Assert.Null(stand.Editor.ContainerFromIndex(1));
        Assert.Equal(pinIn, stand.LinkOf(stand.AToB).Geometry.Target);
        Assert.Equal(pinOut, stand.LinkOf(stand.BToA).Geometry.Source);

        stand.B.Location += new Vector(50, 20);
        stand.RunLayout();

        Assert.Equal(pinIn + new Vector(50, 20), stand.LinkOf(stand.AToB).Geometry.Target);
        Assert.Equal(pinOut + new Vector(50, 20), stand.LinkOf(stand.BToA).Geometry.Source);
    }

    [AvaloniaFact]
    public void Without_PortNodeBinding_A_Link_Waits_For_Its_Node_To_Show()
    {
        var stand = Create(portNodeBinding: false);

        Assert.False(stand.LinkOf(stand.AToB).IsResolved);

        stand.Pan(new Point(2800, 0));
        Assert.True(stand.LinkOf(stand.AToB).IsResolved);
        Assert.Equal(stand.PinOf(stand.B.In), stand.LinkOf(stand.AToB).Geometry.Target);
    }

    [AvaloniaFact]
    public void A_Link_Being_Drawn_Holds_The_Nodes_It_Took()
    {
        // Протяжка снимает порты на входе; узел, от которого тянут, не сворачивается, даже когда
        // холст уехал, — и уходит на ближайшей мере после отпускания.
        var stand = Create();
        var from = stand.PinOf(stand.A.Out);
        stand.Window.MouseDown(from, MouseButton.Left);
        stand.Window.MouseMove(from + new Vector(20, 10));

        // Нажатие на порт отдало узлу фокус клавиатуры, а узел с фокусом не сворачивается и без жеста:
        // фокус уводится, чтобы узел держало одно удержание.
        stand.Editor.Focus();
        stand.Pan(new Point(2800, 0));
        Assert.NotNull(stand.Editor.ContainerFromIndex(0));

        stand.Window.MouseUp(from + new Vector(20, 10), MouseButton.Left);
        stand.RunLayout();
        Assert.Null(stand.Editor.ContainerFromIndex(0));
    }

    [AvaloniaFact]
    public void Only_Links_In_View_Have_Controls()
    {
        // A↔B тянутся через окно и развёрнуты; C→D — далеко, у неё только запись. Холст уехал к C и
        // D — контролы ушли к ней, а связи A↔B свернулись.
        var stand = Create();
        Assert.NotNull(stand.LinkOf(stand.AToB).Control);
        Assert.NotNull(stand.LinkOf(stand.BToA).Control);
        Assert.Null(stand.LinkOf(stand.CToD).Control);
        Assert.Equal(2, stand.Editor.RealizedLinks);

        stand.Pan(new Point(2900, 800));

        Assert.NotNull(stand.LinkOf(stand.CToD).Control);
        Assert.Null(stand.LinkOf(stand.AToB).Control);
        Assert.Null(stand.LinkOf(stand.BToA).Control);
        Assert.Equal(new Rect(stand.LinkOf(stand.CToD).WorldBounds.Position, stand.LinkOf(stand.CToD).WorldBounds.Size),
            stand.LinkOf(stand.CToD).Control!.Bounds);
    }

    [AvaloniaFact]
    public void Hit_Test_Cut_And_Minimap_Reach_A_Collapsed_Link()
    {
        var stand = Create();
        var far = stand.LinkOf(stand.CToD);
        Assert.Null(far.Control);
        var middle = far.Geometry.At(0.5);

        Assert.Same(far, stand.Editor.HitTestLink(middle));

        var crossing = new HashSet<LinkRecord>();
        stand.Editor.CollectCrossing(middle - new Vector(0, 30), middle + new Vector(0, 30), crossing);
        Assert.Equal(new[] { far }, crossing);

        var layer = stand.Editor.GetService<ArxisStudio.Surface.Editing.IMinimapLayer>()!;
        using (var context = new Avalonia.Media.StreamGeometry().Open())
            layer.Build(context);

        Assert.Equal(3, stand.Editor.LinksOnMinimap);
    }

    [AvaloniaFact]
    public void A_Selected_Link_Stays_Selected_Through_Collapse()
    {
        // Выбор живёт в записи: контрол, развёрнутый заново, показывает его сам.
        var stand = Create();
        Assert.True(stand.Editor.SelectLink(stand.CToD));
        Assert.Null(stand.LinkOf(stand.CToD).Control);

        stand.Pan(new Point(2900, 800));
        Assert.True(stand.LinkOf(stand.CToD).Control!.IsSelected);

        stand.Pan(new Point(0, 0));
        Assert.Null(stand.LinkOf(stand.CToD).Control);
        Assert.Equal(new object[] { stand.CToD }, stand.Editor.SelectedLinks);

        stand.Pan(new Point(2900, 800));
        Assert.True(stand.LinkOf(stand.CToD).Control!.IsSelected);
    }

    [AvaloniaFact]
    public void A_Ready_Link_Is_Its_Own_Control_And_Stays()
    {
        var stand = Create();
        var own = new Link { Source = stand.B.Out, Target = stand.B.In };
        ((ObservableCollection<object>)stand.Editor.Links!).Add(own);
        stand.RunLayout();

        Assert.Same(own, stand.Editor.RecordOf(own)!.Control);
        Assert.True(own.IsVisible);

        stand.Pan(new Point(-3000, -3000));
        Assert.Same(own, stand.Editor.RecordOf(own)!.Control);
    }

    private sealed class MutableLink(PortModel from, PortModel to) : INotifyPropertyChanged
    {
        private PortModel _to = to;

        public PortModel From { get; } = from;

        public PortModel To
        {
            get => _to;
            set
            {
                _to = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(To)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    [AvaloniaFact]
    public void A_Link_Model_Changing_Its_End_Moves_The_Link()
    {
        // Концы созданной редактором связи даёт модель, и перецепляет её хост моделью — контрола у
        // связи при этом может и не быть.
        var stand = Create();
        var d = (NodeModel)stand.Nodes[3];
        var model = new MutableLink(stand.A.Out, stand.B.In);
        ((ObservableCollection<object>)stand.Editor.Links!).Add(model);
        stand.RunLayout();

        model.To = d.In;

        Assert.Equal(new Point(3400, 1040), stand.Editor.RecordOf(model)!.Geometry.Target);
    }

    [AvaloniaFact]
    public void A_Pan_Across_A_Long_Link_Realizes_It_Without_Its_Nodes()
    {
        // Узлы E и F далеко по обе стороны, а холст смотрит на середину их связи: ни один узел не
        // развернётся, и развернуть связь может только сама смена видимой области.
        var stand = Create();
        var e = new NodeModel("E", new Point(-5000, 3000));
        var f = new NodeModel("F", new Point(5000, 3000));
        stand.Nodes.Add(e);
        stand.Nodes.Add(f);
        var model = new LinkModel(e.Out, f.In);
        ((ObservableCollection<object>)stand.Editor.Links!).Add(model);
        stand.RunLayout();
        Assert.Null(stand.Editor.RecordOf(model)!.Control);

        stand.Pan(new Point(0, 2800));

        Assert.NotNull(stand.Editor.RecordOf(model)!.Control);
        Assert.Null(stand.Editor.ContainerFromItem(e));
        Assert.Null(stand.Editor.ContainerFromItem(f));
    }

    [AvaloniaFact]
    public void A_Link_Brought_Into_View_By_Its_Nodes_Gets_A_Control()
    {
        // Ни один жест: хост сдвинул модели C и D под окно — связь въехала и развернулась.
        var stand = Create();
        var c = (NodeModel)stand.Nodes[2];
        var d = (NodeModel)stand.Nodes[3];

        c.Location = new Point(200, 400);
        d.Location = new Point(500, 400);
        stand.RunLayout();

        Assert.NotNull(stand.LinkOf(stand.CToD).Control);
    }

    [AvaloniaFact]
    public void Collection_Changes_Keep_Link_Ends_On_Their_Nodes()
    {
        // Вставка сдвигает индексы — конец ищется по узлу, а не по прежнему индексу. Перестановка
        // сообщает об узле, когда хранилище уже сошлось с коллекцией: посреди неё индекс узла указывал
        // на ячейку соседа, и конец уезжал к нему. Ушедший свёрнутым узел уносит свои связи.
        var stand = Create();

        stand.Nodes.Insert(0, new NodeModel("C", new Point(5000, 5000)));
        stand.RunLayout();
        stand.B.Location = new Point(3000, 300);
        stand.RunLayout();
        Assert.Equal(new Point(3000, 340), stand.LinkOf(stand.AToB).Geometry.Target);

        stand.Nodes.Move(stand.Nodes.IndexOf(stand.B), 0);
        stand.RunLayout();
        Assert.Equal(new Point(3000, 340), stand.LinkOf(stand.AToB).Geometry.Target);

        stand.Nodes.Remove(stand.B);
        stand.RunLayout();
        Assert.False(stand.LinkOf(stand.AToB).IsResolved);
        Assert.False(stand.LinkOf(stand.BToA).IsResolved);
    }

    /// <summary>
    /// Сменённый граф редактор не держит — ни узлов его, ни портов.
    /// </summary>
    /// <remarks>
    /// Оценка конца заводит запись на каждый узел, до которого дошла связь, и прежде эти записи не
    /// убирались: демо, перезагружая граф на 10 000 узлов, прибавляло по пять мегабайт за раз. Узел D
    /// ни разу не показывался, контейнера у него нет, и держать его могли записи концов, читатель узла
    /// порта и указатель панели по элементам — его строит сдвиг модели свёрнутого узла, и прежде он
    /// держал прежнюю коллекцию до следующего вопроса. Смену источника Avalonia сообщает удалением
    /// прежних элементов, и узлы забываются по одному.
    /// </remarks>
    [AvaloniaFact]
    public void A_Replaced_Graph_Is_Not_Held_By_The_Editor()
    {
        var (editor, node) = ReplaceGraph(clear: false);

        Collect();

        Assert.False(node.IsAlive, "редактор держит узел сменённого графа");
        GC.KeepAlive(editor);
    }

    /// <summary>
    /// Очищенный граф — тоже: очистка приходит сбросом без списка ушедших, и узлы забываются, когда
    /// панель перечитала коллекцию.
    /// </summary>
    [AvaloniaFact]
    public void A_Cleared_Graph_Is_Not_Held_By_The_Editor()
    {
        var (editor, node) = ReplaceGraph(clear: true);

        Collect();

        Assert.False(node.IsAlive, "редактор держит узел очищенного графа");
        GC.KeepAlive(editor);
    }

    /// <summary>
    /// Удалённый узел редактор не держит, как и сменённый граф.
    /// </summary>
    [AvaloniaFact]
    public void A_Removed_Node_Is_Not_Held_By_The_Editor()
    {
        var (editor, node) = RemoveNode();

        Collect();

        Assert.False(node.IsAlive, "редактор держит удалённый узел");
        GC.KeepAlive(editor);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (NodeEditor Editor, WeakReference Node) ReplaceGraph(bool clear)
    {
        var stand = Create();
        var d = MoveCollapsed(stand);

        if (clear)
        {
            ((ObservableCollection<object>)stand.Editor.Links!).Clear();
            stand.Nodes.Clear();
        }
        else
        {
            stand.Editor.ItemsSource = new ObservableCollection<object> { new NodeModel("E", new Point(100, 100)) };
            stand.Editor.Links = new ObservableCollection<object>();
        }

        stand.RunLayout();
        return (stand.Editor, new WeakReference(d));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (NodeEditor Editor, WeakReference Node) RemoveNode()
    {
        var stand = Create();
        var d = MoveCollapsed(stand);

        // Как у хоста: сперва связи узла, затем сам узел.
        ((ObservableCollection<object>)stand.Editor.Links!).Remove(stand.CToD);
        stand.Nodes.Remove(d);
        stand.RunLayout();
        return (stand.Editor, new WeakReference(d));
    }

    /// <summary>
    /// Сдвигает моделью свёрнутый узел D: панель ищет его индекс и строит указатель по элементам.
    /// </summary>
    private static NodeModel MoveCollapsed(Stand stand)
    {
        var d = (NodeModel)stand.Nodes[3];
        d.Location = new Point(3400, 1100);
        stand.RunLayout();
        Assert.Equal(new Point(3400, 1140), stand.LinkOf(stand.CToD).Geometry.Target);
        return d;
    }

    private static void Collect()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
        }
    }
}
