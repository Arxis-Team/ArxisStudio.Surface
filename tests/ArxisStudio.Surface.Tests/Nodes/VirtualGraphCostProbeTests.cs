using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface;
using ArxisStudio.Surface.Editing;
using ArxisStudio.Surface.Nodes;

namespace ArxisStudio.Tests;

/// <summary>
/// Цена графа с виртуализацией (ADR 0007): что остаётся постоянным от 2000 до 10 000 узлов.
/// </summary>
/// <remarks>
/// Стенд: графы по 2000 и 10 000 узлов 160 × 80 рядами по 100, шаг 200 × 120, цепочкой внутри ряда;
/// редактор с <c>ItemLocationBinding</c>, <c>PortNodeBinding</c> и предполагаемым размером, равным
/// настоящему, в окне 800 × 600 у начала координат. Утверждаются счётчики — сколько развёрнуто, что
/// переставляет кадр перетаскивания, что меряет кадр панорамы — и то, что они одинаковы на обоих
/// графах. Время печатается: тесты идут в отладочной сборке, и числа для решений берутся из прогона
/// с <c>-c Release</c>. Прежний стенд без виртуализации — <see cref="NodeGraphCostProbeTests"/>.
/// </remarks>
public class VirtualGraphCostProbeTests
{
    private const int Small = 2000;
    private const int Large = 10000;
    private const int Columns = 100;

    private static readonly Size NodeSize = new(160, 80);
    private static readonly IDataTemplate Template = GraphTemplates.Node(NodeSize);

    /// <summary>
    /// Узел, за который тянут: третий ряд, третий столбец — в окне, вдали от полосы автопрокрутки.
    /// </summary>
    private const int Dragged = (2 * Columns) + 2;

    private readonly ITestOutputHelper _output;

    /// <summary>Инициализирует пробу.</summary>
    /// <param name="output">Приёмник напечатанных величин.</param>
    public VirtualGraphCostProbeTests(ITestOutputHelper output) => _output = output;

    private sealed record Graph(Window Window, NodeEditor Editor, ObservableCollection<object> Nodes, TimeSpan Load)
    {
        public VirtualizingSurfacePanel Panel => Editor.GetVisualDescendants().OfType<VirtualizingSurfacePanel>().Single();

        public LinkPanel LinkPanel => Editor.GetVisualDescendants().OfType<LinkPanel>().Single();

        public NodeModel Node(int index) => (NodeModel)Nodes[index];

        public void RunLayout()
        {
            var manager = Window.GetLayoutManager();
            manager?.ExecuteInitialLayoutPass();
            manager?.ExecuteLayoutPass();
        }
    }

    private static Graph Create(int size)
    {
        var watch = Stopwatch.StartNew();
        var nodes = new ObservableCollection<object>();
        for (var i = 0; i < size; i++)
            nodes.Add(new NodeModel("Узел " + i, new Point(i % Columns * 200, i / Columns * 120)));

        var links = new ObservableCollection<object>();
        for (var i = 0; i + 1 < size; i++)
        {
            if ((i + 1) % Columns != 0)
                links.Add(new LinkModel(((NodeModel)nodes[i]).Out, ((NodeModel)nodes[i + 1]).In));
        }

        var editor = new NodeEditor
        {
            ItemsSource = nodes,
            Links = links,
            ItemTemplate = Template,
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
        var graph = new Graph(window, editor, nodes, default);
        graph.RunLayout();
        watch.Stop();
        return graph with { Load = watch.Elapsed };
    }

    /// <summary>
    /// Кадр перетаскивания: указатель ходит туда-обратно на 10 пикселей, и раскладка догоняет.
    /// </summary>
    private static Action StartDrag(Graph graph)
    {
        var grip = graph.Node(Dragged).Location + new Vector(NodeSize.Width / 2, 8);
        graph.Window.MouseMove(grip);
        graph.Window.MouseDown(grip, MouseButton.Left);
        var at = grip + new Vector(10, 10);
        graph.Window.MouseMove(at);
        graph.RunLayout();

        var flip = false;
        return () =>
        {
            flip = !flip;
            graph.Window.MouseMove(at + (flip ? new Vector(10, 0) : default));
            graph.RunLayout();
        };
    }

    [AvaloniaFact]
    public void A_Loaded_Graph_Realizes_As_Much_At_Ten_Thousand_As_At_Two()
    {
        // Панель связей стоит в шаблоне раньше панели узлов и меряется первой: создано контролов связей
        // обязано быть столько же, сколько развёрнуто, а не весь граф, свёрнутый обратно.
        var realized = new Dictionary<int, (int Nodes, int Links, int LinksCreated)>();
        foreach (var size in new[] { Small, Large })
        {
            var graph = Create(size);
            realized[size] = (graph.Panel.RealizedCount, graph.Editor.RealizedLinks, graph.Editor.LinksCreated);
            _output.WriteLine($"{size} узлов: граф построен за {graph.Load.TotalMilliseconds:F0} мс; "
                + $"развёрнуто узлов {realized[size].Nodes}, связей {realized[size].Links}, "
                + $"контролов связей создано {realized[size].LinksCreated}");
        }

        Assert.Equal(realized[Small], realized[Large]);
        Assert.InRange(realized[Large].Nodes, 1, 100);
        Assert.Equal(realized[Large].Links, realized[Large].LinksCreated);
    }

    [AvaloniaFact]
    public void A_Drag_Frame_Touches_The_Node_And_Its_Links_Alone()
    {
        // Кадр переставляет один узел, пересчитывает и переставляет две его связи и ничего не
        // меряет — на обоих графах.
        var frameTime = new Dictionary<int, double>();
        foreach (var size in new[] { Small, Large })
        {
            var graph = Create(size);
            var frame = StartDrag(graph);
            frame();

            const int frames = 10;
            var (measured, arranged) = (graph.Panel.MeasuredChildren, graph.Panel.ArrangedChildren);
            var linksArranged = graph.LinkPanel.ArrangedChildren;
            var linkUpdates = graph.Editor.LinkUpdates;
            for (var i = 0; i < frames; i++)
                frame();

            Assert.Equal(0, (graph.Panel.MeasuredChildren - measured) / frames);
            Assert.Equal(1, (graph.Panel.ArrangedChildren - arranged) / frames);
            Assert.Equal(2, (graph.LinkPanel.ArrangedChildren - linksArranged) / frames);
            Assert.Equal(2, (graph.Editor.LinkUpdates - linkUpdates) / frames);

            frameTime[size] = CostProbe.MicrosecondsPerCall(frame, calls: 60) / 1000;
            _output.WriteLine($"{size} узлов: кадр перетаскивания {frameTime[size]:F3} мс");
        }

        _output.WriteLine($"кадр на {Large} против {Small}: {frameTime[Large] / frameTime[Small]:F2}");
    }

    [AvaloniaFact]
    public void Select_All_Realizes_Nothing_And_A_Drag_Moves_The_Whole_Graph()
    {
        // Выбор — данные (ADR 0010): «выбрать всё» разворачивает не больше, чем уже развёрнуто, на обоих
        // графах, а кадр перетаскивания всего выбора двигает свёрнутые узлы записью в модель.
        var realized = new Dictionary<int, (int Nodes, int Links)>();
        foreach (var size in new[] { Small, Large })
        {
            var graph = Create(size);
            var before = (graph.Panel.RealizedCount, graph.Editor.RealizedLinks);
            graph.Editor.Focus();
            var watch = Stopwatch.StartNew();
            graph.Window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            graph.RunLayout();
            watch.Stop();

            Assert.Equal(size, graph.Editor.Selection.Count);
            Assert.Equal(before, (graph.Panel.RealizedCount, graph.Editor.RealizedLinks));

            var last = graph.Node(size - 1);
            var start = last.Location;
            var frame = StartDrag(graph);
            frame();
            Assert.NotEqual(start, last.Location);
            Assert.Equal(graph.Node(Dragged).Location - new Point(Dragged % Columns * 200, Dragged / Columns * 120), last.Location - start);

            // Модель, записанная панелью, отвечает эхом, а дочитка по эху — проход всей коллекции: на
            // каждой записи он делал кадр квадратом графа, а раз на кадр — читал все модели заново.
            // Ячейку панель ставит сама, и проходов в кадре нет.
            // Узел порта читается привязкой раз на порт, а не на каждом кадре по два раза на связь.
            var (passes, reads) = (graph.Panel.SlotPasses, graph.Editor.PortNodeReads);
            frame();
            Assert.Equal(passes, graph.Panel.SlotPasses);
            Assert.Equal(reads, graph.Editor.PortNodeReads);

            var frameMs = CostProbe.MicrosecondsPerCall(frame, calls: 6) / 1000;
            realized[size] = (graph.Panel.RealizedCount, graph.Editor.RealizedLinks);
            _output.WriteLine($"{size} узлов: «выбрать всё» {watch.Elapsed.TotalMilliseconds:F0} мс, "
                + $"кадр перетаскивания всего выбора {frameMs:F1} мс; развёрнуто узлов {realized[size].Nodes}, "
                + $"связей {realized[size].Links}");
        }

        Assert.Equal(realized[Small], realized[Large]);
    }

    [AvaloniaFact]
    public void A_Pan_Frame_Measures_What_Is_Realized_Not_The_Graph()
    {
        // Панорама перемеряет панели — но только развёрнутые контейнеры, и их столько же на любом
        // графе.
        var measuredPerFrame = new Dictionary<int, int>();
        foreach (var size in new[] { Small, Large })
        {
            var graph = Create(size);
            var flip = false;
            void Frame()
            {
                flip = !flip;
                graph.Editor.ViewportLocation = new Point(flip ? 10 : 0, 0);
                graph.RunLayout();
            }

            Frame();
            const int frames = 10;
            var measured = graph.Panel.MeasuredChildren + graph.LinkPanel.MeasuredChildren;
            for (var i = 0; i < frames; i++)
                Frame();

            measuredPerFrame[size] = (graph.Panel.MeasuredChildren + graph.LinkPanel.MeasuredChildren - measured) / frames;
            var time = CostProbe.MicrosecondsPerCall(Frame, calls: 20) / 1000;
            _output.WriteLine($"{size} узлов: кадр панорамы {time:F3} мс, перемерено {measuredPerFrame[size]}");
        }

        Assert.Equal(measuredPerFrame[Small], measuredPerFrame[Large]);
        Assert.InRange(measuredPerFrame[Large], 1, 200);
    }

    [AvaloniaFact]
    public void Zooming_Out_Realizes_Down_To_The_Simplified_Zoom_And_No_Further()
    {
        // До порога упрощённого вида окно видит тем больше, чем дальше холст, и развёрнуто всё видимое:
        // на 50 % — вчетверо больше, чем на 100 %. Ниже порога не развёрнуто ничего — ни узлов, ни
        // связей, — и на обоих графах одинаково (ADR 0008).
        var counts = new Dictionary<(int Size, double Zoom), (int Nodes, int Links)>();
        foreach (var size in new[] { Small, Large })
        {
            var graph = Create(size);
            counts[(size, 1)] = (graph.Panel.RealizedCount, graph.Editor.RealizedLinks);
            foreach (var zoom in new[] { 0.5, 0.3, 0.1 })
            {
                var (realized, milliseconds, megabytes) = ZoomTo(graph, zoom);
                counts[(size, zoom)] = (realized, graph.Editor.RealizedLinks);
                _output.WriteLine($"{size} узлов на {zoom:P0}: развёрнуто {realized} узлов и {graph.Editor.RealizedLinks} "
                    + $"связей за {milliseconds:F0} мс, память {megabytes:F1} МБ");
            }
        }

        Assert.True(counts[(Small, 0.5)].Nodes >= 3 * counts[(Small, 1)].Nodes, $"на 50 % развёрнуто {counts[(Small, 0.5)]}");
        Assert.Equal(counts[(Small, 0.5)], counts[(Large, 0.5)]);
        foreach (var size in new[] { Small, Large })
        {
            Assert.Equal((0, 0), counts[(size, 0.3)]);
            Assert.Equal((0, 0), counts[(size, 0.1)]);
        }
    }

    [AvaloniaFact]
    public void A_Pan_Frame_Below_The_Simplified_Zoom_Measures_Nothing()
    {
        // Ниже порога окна нет: кадр панорамы не перемеряет ни узлов, ни связей — на любом графе.
        foreach (var size in new[] { Small, Large })
        {
            var graph = Create(size);
            graph.Editor.ViewportZoom = 0.1;
            graph.RunLayout();

            var flip = false;
            void Frame()
            {
                flip = !flip;
                graph.Editor.ViewportLocation = new Point(flip ? 100 : 0, 0);
                graph.RunLayout();
            }

            Frame();
            var measured = graph.Panel.MeasuredChildren + graph.LinkPanel.MeasuredChildren;
            for (var i = 0; i < 10; i++)
                Frame();

            Assert.Equal(measured, graph.Panel.MeasuredChildren + graph.LinkPanel.MeasuredChildren);
            var time = CostProbe.MicrosecondsPerCall(Frame, calls: 20) / 1000;
            _output.WriteLine($"{size} узлов на 10 %: кадр панорамы {time:F3} мс");
        }
    }

    /// <summary>
    /// Ставит масштаб у начала координат и отвечает, сколько развёрнуто после прохода раскладки, за
    /// сколько он прошёл и сколько управляемой памяти осталось после полной сборки.
    /// </summary>
    private static (int Realized, double Milliseconds, double Megabytes) ZoomTo(Graph graph, double zoom)
    {
        var watch = Stopwatch.StartNew();
        graph.Editor.ViewportZoom = zoom;
        graph.RunLayout();
        watch.Stop();
        return (graph.Panel.RealizedCount, watch.Elapsed.TotalMilliseconds, GC.GetTotalMemory(forceFullCollection: true) / 1048576.0);
    }

    [AvaloniaFact]
    public void The_Minimap_Redraw_Does_Not_Grow_With_The_Graph()
    {
        // Перерисовка рисует собранное — её цена от графа не зависит. Пересборка печатается у
        // прежнего стенда: здесь её цену мерила бы заглушка платформы, квадратичная по фигурам.
        foreach (var size in new[] { Small, Large })
        {
            var graph = Create(size);
            graph.Window.Content = null;
            var map = new SurfaceMinimap
            {
                Editor = graph.Editor,
                Width = 200,
                Height = 150,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            graph.Window.Content = new Grid { Children = { graph.Editor, map } };
            graph.RunLayout();
            graph.Window.CaptureRenderedFrame();

            Assert.Equal(size - (size / Columns), graph.Editor.LinksOnMinimap);

            var bitmap = new RenderTargetBitmap(new PixelSize(200, 150));
            var redraw = CostProbe.MicrosecondsPerCall(() => bitmap.Render(map), calls: 20) / 1000;
            _output.WriteLine($"{size} узлов: перерисовка миникарты {redraw:F3} мс");
        }
    }
}
