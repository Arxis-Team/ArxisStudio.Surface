using System.Diagnostics;

using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface.Nodes;
using ArxisStudio.Surface.Nodes.States;

namespace ArxisStudio.Tests;

/// <summary>
/// Цена графа: что в редакторе узлов растёт вместе с числом узлов и связей, а что нет.
/// </summary>
/// <remarks>
/// Стенд, по числам которого решается, нужны ли пространственный индекс и виртуализация (ADR 0004).
/// Графы по 200 и 2000 узлов рядами по <see cref="Columns"/>, в каждом ряду — цепочка, где выход
/// узла ведёт во вход следующего.
/// <para>
/// Утверждается то, что не зависит от машины и от JIT: сколько связей пересчитывается за кадр и
/// скольким поиск меряет точное расстояние — счётчиками. Время утверждается только там, где оно
/// в миллисекундах, и только отношением. Поиски стоят микросекунды, и их отношение здесь не
/// держится: первый замер в процессе идёт по неоптимизированному коду даже после прогрева, и
/// отношение гуляло от 0,4 до 10. Их время печатается — прогон и есть источник чисел, — а
/// тесты идут в отладочной сборке, где наш код не оптимизирован вовсе, так что для решений числа
/// берутся из прогона с <c>-c Release</c>.
/// </para>
/// </remarks>
public class NodeGraphCostProbeTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Инициализирует пробу.</summary>
    /// <param name="output">Приёмник напечатанных величин.</param>
    public NodeGraphCostProbeTests(ITestOutputHelper output) => _output = output;

    private const int Small = 200;
    private const int Large = 2000;
    private const int Columns = 40;

    /// <summary>
    /// Замеров, из которых берётся медиана: одиночный переживает не каждый чужой квант процессора.
    /// </summary>
    private const int Runs = 5;

    /// <summary>
    /// Прогрев по времени, а не по числу вызовов: оптимизированный код метода среда ставит, когда
    /// JIT какое-то время не занят новыми методами, и короткий прогрев числом вызовов кончался раньше.
    /// </summary>
    private static readonly TimeSpan Warmup = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Узел, за который тянут: третий ряд, третий столбец — в окне, с соседями по цепочке с обеих
    /// сторон и вдали от краёв.
    /// </summary>
    /// <remarks>
    /// Вдали от краёв — обязательно. Узел из первого ряда тянули в полосе автопрокрутки, и на
    /// большом графе, где кадр идёт миллисекунды, таймер успевал тикнуть по настоящим часам:
    /// холст ехал, узел двигался дважды за кадр, и счётчик показывал не степень узла.
    /// </remarks>
    private const int Dragged = 2 * Columns + 2;

    /// <summary>
    /// Пустой холст в окне: просвет между вторым и третьим рядами, куда не заходит ни одна связь.
    /// </summary>
    private static readonly Point Gap = new(600, 205);

    private static NodeStand CreateGraph(int nodes)
    {
        var locations = Enumerable.Range(0, nodes)
            .Select(i => new Point(i % Columns * 160, i / Columns * 110))
            .ToList();

        // Цепочка не переходит с ряда на ряд: такая связь накрывает рамкой весь граф по ширине
        // (A_Long_Backward_Link_Is_Framed_Across_The_Whole_Width), и замеры мерили бы её.
        var stand = NodeStand.Create(locations, itemTemplate: NodeStand.PortedNode);
        for (var i = 0; i + 1 < nodes; i++)
        {
            if ((i + 1) % Columns != 0)
                stand.Links.Add(new LinkData(NodeStand.Out(i), NodeStand.In(i + 1)));
        }

        stand.RunLayout();
        return stand;
    }

    /// <summary>
    /// Медиана времени одного вызова в микросекундах, после прогрева.
    /// </summary>
    private static double MicrosecondsPerCall(Action call, int calls)
    {
        var warmup = Stopwatch.StartNew();
        do
        {
            for (var i = 0; i < calls; i++)
                call();
        }
        while (warmup.Elapsed < Warmup);

        var samples = new List<double>();
        for (var run = 0; run < Runs; run++)
        {
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < calls; i++)
                call();

            watch.Stop();
            samples.Add(watch.Elapsed.TotalMilliseconds * 1000 / calls);
        }

        samples.Sort();
        return samples[samples.Count / 2];
    }

    /// <summary>
    /// Скольким связям поиск в этой точке мерил точное расстояние.
    /// </summary>
    private static int DistanceChecks(NodeStand stand, Point world)
    {
        var before = stand.Editor.LinkDistanceChecks;
        stand.Editor.HitTestLink(world);
        return stand.Editor.LinkDistanceChecks - before;
    }

    private static Link LinkFrom(NodeStand stand, int node) =>
        stand.LinkOf(stand.Links.OfType<LinkData>().Single(l => Equals(l.From, NodeStand.Out(node))));

    /// <summary>
    /// Берёт узел за свободное от портов место и уводит за порог перетаскивания.
    /// </summary>
    private static Point StartDrag(NodeStand stand)
    {
        var grip = stand.GripOf(Dragged);
        stand.Window.MouseMove(grip);
        stand.Window.MouseDown(grip, MouseButton.Left);
        var moved = grip + new Vector(10, 10);
        stand.Window.MouseMove(moved);
        stand.RunLayout();
        return moved;
    }

    /// <summary>
    /// Кадр перетаскивания: указатель попеременно туда и обратно, чтобы каждый кадр что-то менял.
    /// </summary>
    private static Action DragFrame(NodeStand stand, Point at)
    {
        var flip = false;
        return () =>
        {
            flip = !flip;
            stand.Window.MouseMove(at + (flip ? new Vector(10, 0) : default));
            stand.RunLayout();
        };
    }

    [AvaloniaFact]
    public void A_Drag_Frame_Refreshes_The_Node_Degree_At_Any_Size()
    {
        // У узла в середине цепочки две связи: столько и пересчитывается за кадр, сколько бы
        // узлов и связей ни лежало вокруг. Общего прохода по связям нет.
        foreach (var size in new[] { Small, Large })
        {
            var stand = CreateGraph(size);
            var frame = DragFrame(stand, StartDrag(stand));

            const int frames = 20;
            var before = stand.Editor.LinkUpdates;
            for (var i = 0; i < frames; i++)
                frame();

            var perFrame = (double)(stand.Editor.LinkUpdates - before) / frames;
            _output.WriteLine($"{size} узлов: {perFrame:F1} пересчёта связей на кадр");
            Assert.Equal(2.0, perFrame);
        }
    }

    [AvaloniaFact]
    public void Only_Links_Framing_The_Point_Pay_For_The_Distance()
    {
        // Поиск проходит все связи, но у каждой лишь сверяет рамку, посчитанную при пересчёте
        // концов; точное расстояние до кривой меряется только у тех, чья рамка накрыла точку, —
        // и их столько же при любом размере графа. Времена печатаются: проход мимо всех связей,
        // попадание и поиск порта под свободным концом.
        foreach (var size in new[] { Small, Large })
        {
            var stand = CreateGraph(size);
            var link = LinkFrom(stand, Dragged);
            var on = link.Geometry.At(0.5);

            Assert.Same(link, stand.Editor.HitTestLink(on));
            Assert.Equal(1, DistanceChecks(stand, on));
            Assert.Equal(0, DistanceChecks(stand, Gap));

            var scan = MicrosecondsPerCall(() => stand.Editor.HitTestLink(Gap), calls: 2000);
            var hit = MicrosecondsPerCall(() => stand.Editor.HitTestLink(on), calls: 2000);

            var from = stand.PinCentreInWorld(0, PortDirection.Output);
            stand.Window.MouseMove(from);
            stand.Window.MouseDown(from, MouseButton.Left);
            var state = Assert.IsType<PendingLinkState>(stand.Editor.CurrentState);
            var near = stand.PinCentreInWorld(Dragged, PortDirection.Input);
            Assert.NotNull(state.FindCandidate(near, out _));
            var port = MicrosecondsPerCall(() => state.FindCandidate(near, out _), calls: 2000);
            stand.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            stand.Window.MouseUp(from, MouseButton.Left);

            _output.WriteLine($"{stand.Links.Count} связей: проход мимо всех {scan:F2} мкс, попадание {hit:F2} мкс; "
                + $"{2 * size} портов: поиск под свободным концом {port:F2} мкс");
        }
    }

    [AvaloniaFact]
    public void A_Long_Backward_Link_Is_Framed_Across_The_Whole_Width()
    {
        // Предел отсева по рамке. Связь из конца ряда в начало следующего идёт назад, её плечо —
        // половина пролёта, и рамка по опорным точкам накрывает граф на всю ширину и шире: в любой
        // точке полосы между рядами она платит точное расстояние. Индекс по рамкам таких связей не
        // отсеет — это стоит знать, когда дойдёт до индекса.
        var stand = CreateGraph(Small);
        Assert.Equal(0, DistanceChecks(stand, Gap));

        var wrap = new LinkData(NodeStand.Out(2 * Columns - 1), NodeStand.In(2 * Columns));
        stand.Links.Add(wrap);
        stand.RunLayout();

        var bounds = stand.LinkOf(wrap).WorldBounds;
        _output.WriteLine($"рамка обратной связи через ряд: {bounds}");
        Assert.True(bounds.Width > Columns * 160, $"рамка {bounds.Width:F0} против графа в {Columns * 160}");
        Assert.Equal(1, DistanceChecks(stand, Gap));
        Assert.Equal(1, DistanceChecks(stand, new Point(20, 205)));
    }

    [AvaloniaFact]
    public void A_Drag_Frame_Grows_With_The_Graph_Though_Its_Links_Do_Not()
    {
        // Связи за кадр пересчитываются только у узла, и всё же кадр растёт вместе с графом:
        // платят раскладка, проходящая по всем детям обеих панелей, и отрисовка. Кадр — это уже
        // миллисекунды, и рост здесь утверждается; но кадр малого графа шумит в разы, поэтому
        // порог мягкий, а разбивка печатается: по ней решается, чем лечить.
        var drag = new Dictionary<int, double>();
        foreach (var size in new[] { Small, Large })
        {
            var stand = CreateGraph(size);

            // Наведение: указатель ходит по пустому холсту между рядами.
            var flip = false;
            var hover = MicrosecondsPerCall(() =>
            {
                flip = !flip;
                stand.Window.MouseMove(Gap + (flip ? new Vector(10, 0) : default));
            }, calls: 200) / 1000;
            var linkHit = MicrosecondsPerCall(() => stand.Editor.HitTestLink(Gap), calls: 200) / 1000;
            var treeHit = MicrosecondsPerCall(() => stand.Window.InputHitTest(Gap), calls: 200) / 1000;

            var node = stand.Node(Dragged);
            var home = node.Location;
            var layout = MicrosecondsPerCall(() =>
            {
                flip = !flip;
                node.Location = home + (flip ? new Vector(10, 0) : default);
                stand.RunLayout();
            }, calls: 60) / 1000;
            node.Location = home;
            stand.RunLayout();

            var at = StartDrag(stand);
            drag[size] = MicrosecondsPerCall(DragFrame(stand, at), calls: 60) / 1000;
            stand.Window.MouseUp(at, MouseButton.Left);

            _output.WriteLine($"{size} узлов: наведение {hover:F3} мс (поиск связи {linkHit:F3}, попадание по дереву {treeHit:F3}); "
                + $"кадр перетаскивания {drag[size]:F3} мс (сдвиг узла с раскладкой {layout:F3})");
        }

        var ratio = drag[Large] / drag[Small];
        _output.WriteLine($"кадр дороже в {ratio:F1} раза");
        Assert.True(ratio > 2, $"{drag[Large]:F3} против {drag[Small]:F3}");
    }
}
