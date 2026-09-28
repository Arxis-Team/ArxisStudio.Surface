using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using SkiaSharp;
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

    /// <summary>Середина узла 22 в мире: (400, 240) плюс половина узла.</summary>
    private static readonly Point Node22 = new(460, 280);

    /// <summary>Пустое место между рядами узлов в мире.</summary>
    private static readonly Point Gap = new(160, 100);

    /// <summary>
    /// Щелчок по точке мира: по карточке — развернуть и выбрать узел, мимо — снять закрепление.
    /// </summary>
    private static void Press(Stand stand, Point world)
    {
        var at = stand.Screen(world);
        stand.Window.MouseDown(at, MouseButton.Left);
        stand.Window.MouseUp(at, MouseButton.Left);
        stand.RunLayout();
    }

    [AvaloniaFact]
    public void A_Link_Has_A_Control_Only_At_A_Realized_Node_Or_When_Pinned()
    {
        var stand = Create();
        Assert.True(stand.Editor.IsSimplified);
        Assert.Equal(0, stand.Editor.RealizedLinks);

        // Нажатый узел развёрнут — его две связи рисуются вживую.
        Press(stand, Node22);
        Assert.Equal(2, stand.Editor.RealizedLinks);
        Assert.NotNull(stand.Record(stand.Links[(2 * (Columns - 1)) + 1]).Control);

        // Подсвеченная связь закреплена жестом.
        var far = stand.Record(stand.Links[60]);
        stand.Editor.SetLinkHighlighted(far, true);
        stand.RunLayout();
        Assert.Equal(3, stand.Editor.RealizedLinks);

        stand.Editor.SetLinkHighlighted(far, false);
        Press(stand, Gap);
        Assert.Equal(0, stand.Editor.RealizedLinks);
    }

    [AvaloniaFact]
    public void Selecting_Nodes_Without_Containers_Clears_The_Link_Selection()
    {
        // Выбор связей и узлов исключают друг друга по элементам: выбранные ниже порога узлы не
        // развёрнуты, а выбор связи всё равно снимается.
        var stand = Create();
        stand.Editor.SelectLink(stand.Links[0]);
        Assert.Single(stand.Editor.SelectedLinks);

        stand.Editor.Selection.SelectAll();
        stand.RunLayout();

        Assert.Empty(stand.Editor.SelectedLinks);
        Assert.Equal(0, stand.Editor.RealizedLinks);
        Assert.Equal(stand.Nodes.Count, stand.Editor.Selection.Count);
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
        Press(stand, Node22);
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
    public void The_Link_Layer_Spans_The_Editor_And_Follows_The_Pan()
    {
        // Как слой карточек ядра: во весь размер, мир в экран — сам; панорама перерисовывает, не
        // пересобирая кривых.
        var stand = Create();
        stand.Render();
        Assert.Equal(new Rect(stand.Editor.Bounds.Size), stand.Layer.Bounds);

        var (draws, rebuilds) = (stand.Layer.Draws, stand.Layer.Rebuilds);
        stand.Editor.ViewportLocation = new Point(200, 100);
        stand.RunLayout();
        stand.Render();

        Assert.Equal(draws + 1, stand.Layer.Draws);
        Assert.Equal(rebuilds, stand.Layer.Rebuilds);
    }

    /// <summary>
    /// Сколько связей без контролов пересекает видимую часть мира — прямым проходом по записям, мимо
    /// сетки слоя; рамка кривой — по контрольным точкам.
    /// </summary>
    private static int VisibleByHand(Stand stand)
    {
        var world = new Rect(stand.Editor.ViewportLocation, stand.Editor.Bounds.Size / stand.Editor.ViewportZoom);
        return stand.Editor.LinkRecords.Count(record =>
        {
            if (record.Control != null || !record.IsResolved)
                return false;

            var g = record.Geometry;
            Point[] points = [g.Source, g.SourceControl, g.TargetControl, g.Target];
            var hull = new Rect(
                new Point(points.Min(p => p.X), points.Min(p => p.Y)),
                new Point(points.Max(p => p.X), points.Max(p => p.Y)));
            return hull.Intersects(world);
        });
    }

    [AvaloniaFact]
    public void A_Frame_Draws_Only_The_Visible_Links_In_Segments_By_Their_Length()
    {
        // Кадр берёт из сетки только видимые связи (ADR 0011) и рисует каждую ломаной: на экране
        // крупнее — отрезков больше, но не меньше одного у видимой.
        var stand = Create();
        stand.Editor.ViewportLocation = new Point(700, 300);
        stand.RunLayout();
        stand.Render();

        var (visible, segments) = stand.Layer.CountVisible();
        Assert.InRange(visible, 1, stand.Layer.Links - 1);
        Assert.Equal(VisibleByHand(stand), visible);
        Assert.True(segments >= visible, $"{segments} отрезков на {visible} связей");

        stand.Editor.ViewportLocation = new Point(-5000, -5000);
        stand.RunLayout();
        stand.Render();
        Assert.Equal((0, 0), stand.Layer.CountVisible());

        stand.Editor.ViewportLocation = default;
        stand.Editor.ViewportZoom = 0.1;
        stand.RunLayout();
        stand.Render();
        var (all, fewer) = stand.Layer.CountVisible();
        Assert.Equal(stand.Links.Count, all);
        stand.Editor.ViewportZoom = 0.45;
        stand.RunLayout();
        stand.Render();
        var (_, more) = stand.Layer.CountVisible();
        Assert.True(more > fewer, $"на 0,45 отрезков {more}, на 0,1 — {fewer}");
    }

    [AvaloniaFact]
    public void The_Skia_Path_Draws_Each_Link_Along_Its_Curve()
    {
        // Одним путём на перо (ADR 0012): середина связи на экране закрашена, пустое место между рядами —
        // нет.
        var stand = Create();
        stand.Render();
        var size = stand.Layer.Bounds.Size;
        using var bitmap = new SKBitmap((int)size.Width, (int)size.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            Assert.True(stand.Layer.RenderTo(canvas), "путь Skia отказался рисовать сплошными кистями");
        }

        var middle = stand.Screen(Middle(stand.Record(stand.Links[0])));
        Assert.True(bitmap.GetPixel((int)Math.Round(middle.X), (int)Math.Round(middle.Y)).Alpha > 0, $"связь не нарисована в {middle}");
        var gap = stand.Screen(Gap);
        Assert.Equal(0, bitmap.GetPixel((int)gap.X, (int)gap.Y).Alpha);
    }

    [AvaloniaFact]
    public void A_Curve_Shorter_Than_A_Pixel_Has_No_Segments()
    {
        // Длина — по контрольным точкам, в пикселях экрана; отрезок — на каждые 12 пикселей, не
        // больше 16.
        var (a, b, c, d) = (new Point(0, 0), new Point(1, 0), new Point(2, 0), new Point(3, 0));
        Assert.Equal(0, ArxisStudio.Surface.CurveSegments.Count(a, b, c, d, 0.3));
        Assert.Equal(1, ArxisStudio.Surface.CurveSegments.Count(a, b, c, d, 1));
        Assert.Equal(3, ArxisStudio.Surface.CurveSegments.Count(a, b, c, d * 10, 1));
        Assert.Equal(16, ArxisStudio.Surface.CurveSegments.Count(a, b, c, d * 1000, 1));
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
    public void A_Node_Moves_With_A_Drag_From_Its_Card_And_Again_Once_Realized()
    {
        // Первая протяжка начинается на карточке — нажатие разворачивает узел; вторая — на уже
        // развёрнутом и выбранном узле. Обе двигают модель на сдвиг экрана, делённый на масштаб.
        var stand = Create();
        var node = stand.Node(22);
        var start = node.Location;

        void Drag(Vector by)
        {
            var grip = stand.Screen(node.Location + new Vector(NodeSize.Width / 2, 10));
            stand.Window.MouseDown(grip, MouseButton.Left);
            stand.Window.MouseMove(grip + new Vector(10, 5));
            stand.Window.MouseMove(grip + by);
            stand.Window.MouseUp(grip + by, MouseButton.Left);
            stand.RunLayout();

            // Щелчок в стороне: иначе следующее нажатие засчиталось бы вторым щелчком двойного.
            stand.Window.MouseDown(new Point(790, 590), MouseButton.Right);
            stand.Window.MouseUp(new Point(790, 590), MouseButton.Right);
            stand.RunLayout();
        }

        Drag(new Vector(40, 20));
        Assert.Equal(start + new Vector(100, 50), node.Location);

        stand.Editor.Selection.Select(22);
        stand.RunLayout();
        Drag(new Vector(40, 20));
        Assert.Equal(start + new Vector(200, 100), node.Location);
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
