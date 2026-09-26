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

    private NodeStand(Window window, NodeEditor editor)
    {
        Window = window;
        Editor = editor;
    }

    public Window Window { get; }

    public NodeEditor Editor { get; }

    public static NodeStand Create(IReadOnlyList<Point> locations, bool snap = false, IDataTemplate? itemTemplate = null)
    {
        var editor = new NodeEditor
        {
            ItemsSource = locations.Select((_, i) => (object)$"Узел {i}").ToList(),
            ItemTemplate = itemTemplate
        };

        editor.InteractionOptions.IsSnapToGridEnabled = snap;
        editor.InteractionOptions.IsSnapToGuidesEnabled = snap;

        var window = new Window { Width = 800, Height = 600, Content = editor };
        window.Show();

        var stand = new NodeStand(window, editor);
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
