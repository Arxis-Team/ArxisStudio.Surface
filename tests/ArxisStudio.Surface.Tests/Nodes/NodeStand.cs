using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using ArxisStudio.Surface.Nodes;

namespace ArxisStudio.Tests;

/// <summary>
/// Связь, как её держит приложение: данные двух портов.
/// </summary>
internal sealed record LinkData(object From, object To);

/// <summary>
/// Стенд редактора узлов: окно 800 × 600 и голый <see cref="NodeEditor"/> над заданными узлами.
/// </summary>
/// <remarks>
/// Узлы ставятся в заданные точки и размеры после первого прохода раскладки — как в
/// <c>SurfaceViewTests</c>: до него контейнеров ещё нет. Привязка к сетке и направляющие по
/// умолчанию выключены — тесты механики не должны зависеть от того, куда притянуло.
/// </remarks>
internal sealed class NodeStand
{
    public static readonly Size NodeSize = new(120, 80);

    /// <summary>
    /// Узел с одним входом и одним выходом; ключи портов — «имя узла.in» и «имя узла.out».
    /// </summary>
    public static IDataTemplate PortedNode { get; } = new FuncDataTemplate<string>((name, _) => new StackPanel
    {
        Children =
        {
            new TextBlock { Text = name },
            new Port { Direction = PortDirection.Input, Data = name + ".in", Content = "in" },
            new Port { Direction = PortDirection.Output, Data = name + ".out", Content = "out" }
        }
    }, supportsRecycling: false);

    /// <summary>
    /// Узел, как его пишет обычный хост: заголовок, под ним списки портов — два входа слева
    /// («имя.in», «имя.in2»), выход справа («имя.out»).
    /// </summary>
    /// <remarks>
    /// Порт здесь лежит в контейнере списка, а тот — в сетке под заголовком: собственные границы
    /// порта от сдвига списка внутри узла не меняются.
    /// </remarks>
    public static IDataTemplate ListedPorts { get; } = new FuncDataTemplate<string>((name, _) =>
    {
        var inputs = new ItemsControl { ItemsSource = new[] { name + ".in", name + ".in2" }, ItemTemplate = PortOf(PortDirection.Input) };
        var outputs = new ItemsControl { ItemsSource = new[] { name + ".out" }, ItemTemplate = PortOf(PortDirection.Output) };
        Grid.SetColumn(outputs, 1);

        return new StackPanel
        {
            Children =
            {
                new TextBlock { Text = name, Name = "Title" },
                new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Children = { inputs, outputs } }
            }
        };
    }, supportsRecycling: false);

    private static IDataTemplate PortOf(PortDirection direction) =>
        new FuncDataTemplate<string>((key, _) => new Port { Direction = direction, Data = key, Content = key }, supportsRecycling: false);

    public static string NodeName(int index) => $"Узел {index}";

    private NodeStand(Window window, NodeEditor editor)
    {
        Window = window;
        Editor = editor;
    }

    public Window Window { get; }

    public NodeEditor Editor { get; }

    public ObservableCollection<object> Items { get; } = new();

    public ObservableCollection<object> Links { get; } = new();

    public static string Out(int index) => NodeName(index) + ".out";

    public static string In(int index) => NodeName(index) + ".in";

    public static NodeStand Create(IReadOnlyList<Point> locations, bool snap = false, IDataTemplate? itemTemplate = null)
    {
        var editor = new NodeEditor
        {
            ItemTemplate = itemTemplate,
            LinkSourceBinding = new Binding(nameof(LinkData.From)),
            LinkTargetBinding = new Binding(nameof(LinkData.To))
        };
        editor.InteractionOptions.IsSnapToGridEnabled = snap;
        editor.InteractionOptions.IsSnapToGuidesEnabled = snap;

        var window = new Window { Width = 800, Height = 600, Content = editor };
        var stand = new NodeStand(window, editor);
        for (var i = 0; i < locations.Count; i++)
            stand.Items.Add(NodeName(i));

        editor.ItemsSource = stand.Items;
        editor.Links = stand.Links;
        window.Show();
        stand.RunLayout();

        for (var i = 0; i < locations.Count; i++)
        {
            var node = stand.Node(i);
            node.Width = NodeSize.Width;
            node.Height = NodeSize.Height;
            node.Location = locations[i];
        }

        stand.RunLayout();
        return stand;
    }

    /// <summary>
    /// Стенд, где узлы ставит хост, как обычное приложение: положение задаётся, когда готов
    /// контейнер, — до первой раскладки, — размера узлу никто не задаёт, а связи есть с самого начала.
    /// </summary>
    public static NodeStand CreatePlacedByHost(IReadOnlyList<Point> locations, IDataTemplate itemTemplate, params LinkData[] links)
    {
        var editor = new NodeEditor
        {
            ItemTemplate = itemTemplate,
            LinkSourceBinding = new Binding(nameof(LinkData.From)),
            LinkTargetBinding = new Binding(nameof(LinkData.To))
        };
        editor.InteractionOptions.IsSnapToGridEnabled = false;
        editor.InteractionOptions.IsSnapToGuidesEnabled = false;
        editor.ContainerPrepared += (_, e) =>
        {
            if (e.Container is Node node)
                node.Location = locations[e.Index];
        };

        var window = new Window { Width = 800, Height = 600, Content = editor };
        var stand = new NodeStand(window, editor);
        for (var i = 0; i < locations.Count; i++)
            stand.Items.Add(NodeName(i));

        foreach (var link in links)
            stand.Links.Add(link);

        editor.ItemsSource = stand.Items;
        editor.Links = stand.Links;
        window.Show();
        stand.RunLayout();
        return stand;
    }

    /// <summary>
    /// Центр штырька порта с этим ключом, посчитанный независимо от редактора, — в координатах
    /// панели узлов, то есть мировых.
    /// </summary>
    public Point PinCentreOf(object key)
    {
        var port = Editor.GetVisualDescendants().OfType<Port>().Single(p => Equals(p.Data, key));
        var pin = port.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "PART_Pin");
        var panel = (Visual)port.FindAncestorOfType<Node>()!.GetVisualParent()!;
        return pin.TranslatePoint(new Point(pin.Bounds.Width / 2, pin.Bounds.Height / 2), panel)!.Value;
    }

    public Node Node(int index) => (Node)Editor.ContainerFromIndex(index)!;

    public LinkData Connect(int from, int to)
    {
        var link = new LinkData(Out(from), In(to));
        Links.Add(link);
        RunLayout();
        return link;
    }

    /// <summary>
    /// Живая связь редактора для элемента коллекции.
    /// </summary>
    public Link LinkOf(object item) =>
        Editor.GetVisualDescendants().OfType<Link>().Single(l => ReferenceEquals(l, item) || ReferenceEquals(l.DataContext, item));

    /// <summary>
    /// Точка на узле, где нет портов, — за неё узел и тянут.
    /// </summary>
    public Point GripOf(int index) => Node(index).Location + new Vector(NodeSize.Width / 2, 12);

    public Port PortOf(int index, PortDirection direction) =>
        Node(index).GetVisualDescendants().OfType<Port>().Single(p => p.Direction == direction);

    /// <summary>
    /// Конец связи у порта, посчитанный независимо от редактора: центр штырька в координатах
    /// панели узлов — а они и есть мировые.
    /// </summary>
    public Point PinCentreInWorld(int index, PortDirection direction)
    {
        var pin = PortOf(index, direction).GetVisualDescendants().OfType<Control>().Single(c => c.Name == "PART_Pin");
        var panel = (Visual)Node(index).GetVisualParent()!;
        return pin.TranslatePoint(new Point(pin.Bounds.Width / 2, pin.Bounds.Height / 2), panel)!.Value;
    }

    public Point CentreOf(int index) => Node(index).Location + new Vector(NodeSize.Width / 2, NodeSize.Height / 2);

    public void RunLayout()
    {
        var manager = Window.GetLayoutManager();
        manager?.ExecuteInitialLayoutPass();
        manager?.ExecuteLayoutPass();
    }

    public void Drag(Point from, Vector by, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.MouseMove(from, modifiers);
        Window.MouseDown(from, MouseButton.Left, modifiers);
        Window.MouseMove(from + new Vector(Math.Sign(by.X) * 6, Math.Sign(by.Y) * 6), modifiers);
        Window.MouseMove(from + by, modifiers);
        Window.MouseUp(from + by, MouseButton.Left, modifiers);
        RunLayout();
    }
}
