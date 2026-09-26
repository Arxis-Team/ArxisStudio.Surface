using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using ArxisStudio.Surface.Nodes;

namespace ArxisStudio.Tests;

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

    public static string NodeName(int index) => $"Узел {index}";

    private NodeStand(Window window, NodeEditor editor)
    {
        Window = window;
        Editor = editor;
    }

    public Window Window { get; }

    public NodeEditor Editor { get; }

    public ObservableCollection<object> Items { get; } = new();

    public static NodeStand Create(IReadOnlyList<Point> locations, bool snap = false, IDataTemplate? itemTemplate = null)
    {
        var editor = new NodeEditor { ItemTemplate = itemTemplate };
        editor.InteractionOptions.IsSnapToGridEnabled = snap;
        editor.InteractionOptions.IsSnapToGuidesEnabled = snap;

        var window = new Window { Width = 800, Height = 600, Content = editor };
        var stand = new NodeStand(window, editor);
        for (var i = 0; i < locations.Count; i++)
            stand.Items.Add(NodeName(i));

        editor.ItemsSource = stand.Items;
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

    public Node Node(int index) => (Node)Editor.ContainerFromIndex(index)!;

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
