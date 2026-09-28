using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface;
using ArxisStudio.Surface.Nodes;

namespace ArxisStudio.Tests;

/// <summary>
/// Редактор узлов как четвёртый слой: свой шаблон и контейнеры, перетаскивание ядра и
/// привязка инструментов без правок (ADR 0004).
/// </summary>
public class NodeEditorTests
{
    [AvaloniaFact]
    public void The_Node_Editor_Has_Its_Own_Template_And_Node_Containers()
    {
        var stand = NodeStand.Create([new Point(100, 100), new Point(300, 100)]);

        Assert.NotNull(stand.Editor.Grid);
        Assert.Equal(typeof(Node), stand.Node(0).GetType());
        Assert.Equal(new Point(300, 100), stand.Node(1).Bounds.Position);
    }

    [AvaloniaFact]
    public void Links_Lie_Beneath_Nodes()
    {
        // Порядок слоёв — порядок детей сетки шаблона: связи рисуются раньше узлов, и узел,
        // лёгший на связь, её закрывает.
        var stand = NodeStand.Create([new Point(100, 100)]);
        var layers = stand.Editor.GetVisualDescendants().OfType<Canvas>().Select(c => c.Name).ToList();

        var links = layers.IndexOf("PART_LinksLayer");
        var items = layers.IndexOf("PART_ItemsLayer");
        Assert.True(links >= 0 && items > links, $"Слой связей обязан лежать под слоем узлов, порядок: {string.Join(", ", layers)}.");
    }

    [AvaloniaFact]
    public void A_Node_Drags_As_One_Edit_That_Undoes()
    {
        var stand = NodeStand.Create([new Point(100, 100)]);
        var edits = new List<SurfaceEditCompletedEventArgs>();
        stand.Editor.EditCompleted += (_, e) => edits.Add(e);

        stand.Drag(stand.CentreOf(0), new Vector(40, 30));

        Assert.Equal(new Point(140, 130), stand.Node(0).Location);
        var edit = Assert.Single(edits);
        Assert.Equal(SurfaceEditKind.Move, edit.Kind);

        foreach (var change in edit.Changes)
            stand.Editor.Revert(change);

        Assert.Equal(new Point(100, 100), stand.Node(0).Location);
    }

    [AvaloniaFact]
    public void A_Dragged_Node_Snaps_To_The_Grid()
    {
        // Привязку редактор узлов берёт у инструментов той же службой, что и дизайнер интерфейса.
        var stand = NodeStand.Create([new Point(100, 100)], snap: true);

        stand.Drag(stand.CentreOf(0), new Vector(37, 23));

        Assert.Equal(new Point(140, 120), stand.Node(0).Location);
    }

    [AvaloniaFact]
    public void A_Dragged_Node_Is_Not_Pulled_To_Its_Neighbour()
    {
        // Направляющие выравнивания и интервалы — инструмент макета: у редактора узлов их нет, и узел,
        // брошенный в трёх пикселях от края соседа, остаётся где брошен. Хост включает их сам.
        var stand = NodeStand.Create([new Point(100, 100), new Point(140, 300)]);

        stand.Drag(stand.CentreOf(0), new Vector(37, 0));

        Assert.Equal(new Point(137, 100), stand.Node(0).Location);
        Assert.False(stand.Editor.InteractionOptions.IsSnapToGuidesEnabled);
        Assert.False(stand.Editor.InteractionOptions.IsEqualSpacingEnabled);
        Assert.True(new NodeEditor().InteractionOptions.IsSnapToGridEnabled, "привязка к сетке у узлов остаётся");
    }

    [AvaloniaFact]
    public void The_Nodes_Theme_Names_Only_Its_Own_Or_Lower_Keys()
    {
        // Тема слоя вправе называть ключи своего слоя и тех, что ниже, — ядра и инструментов, —
        // но не сестры: без дизайнера интерфейса они молча остались бы пустыми кистями.
        var src = Path.Combine(ThisDirectory(), "..", "..", "..", "src");
        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layer in new[] { "Surface", "Surface.Editing", "Surface.Nodes" })
        {
            foreach (var file in Directory.GetFiles(Path.Combine(src, layer, "Themes"), "*.axaml"))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(file), "x:Key=\"([^\"{]+)\""))
                    defined.Add(m.Groups[1].Value);
            }
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(Path.Combine(src, "Surface.Nodes", "Themes"), "*.axaml"))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "(?:Dynamic|Static)Resource ([A-Za-z0-9_.]+)"))
                used.Add(m.Groups[1].Value);
        }

        Assert.NotEmpty(used);
        var missing = used.Except(defined).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, "Тема редактора узлов называет ключи вне своего слоя и слоёв ниже:\n" + string.Join("\n", missing));
    }

    [AvaloniaFact]
    public void Light_And_Dark_Define_The_Same_Keys()
    {
        var text = File.ReadAllText(Path.Combine(ThisDirectory(), "..", "..", "..", "src", "Surface.Nodes", "Themes", "NodesResources.axaml"));
        var light = KeysOf(Section(text, "Light"));
        var dark = KeysOf(Section(text, "Dark"));

        Assert.NotEmpty(light);
        Assert.Equal(light, dark);

        static string Section(string text, string variant)
        {
            var start = text.IndexOf($"x:Key=\"{variant}\"", StringComparison.Ordinal);
            var end = text.IndexOf("</ResourceDictionary>", start, StringComparison.Ordinal);
            return text[start..end];
        }

        static SortedSet<string> KeysOf(string section) =>
            new(Regex.Matches(section, "x:Key=\"(NodeEditor[^\"]+)\"").Select(m => m.Groups[1].Value), StringComparer.Ordinal);
    }

    private static string ThisDirectory([CallerFilePath] string? thisFile = null) => Path.GetDirectoryName(thisFile)!;
}
