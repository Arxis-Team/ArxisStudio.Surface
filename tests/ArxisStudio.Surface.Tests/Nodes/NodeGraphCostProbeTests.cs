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
/// Графы по 200 и 2000 узлов — цепочка, где выход каждого узла ведёт во вход следующего, — и
/// сравниваются они друг с другом, как в <see cref="MarqueeCostProbeTests"/>.
/// <para>
/// Детерминированное утверждается точно: пересчёт связей за кадр перетаскивания — счётчиком.
/// Время — только отношениями: абсолютные миллисекунды зависят от машины, и потолок по ним либо
/// ничего не ловит, либо мигает. Числа при этом печатаются — прогон здесь и есть их источник.
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
    /// Узел, за который тянут: третий ряд, третий столбец — в окне, с соседями по цепочке с обеих
    /// сторон и вдали от краёв.
    /// </summary>
    /// <remarks>
    /// Вдали от краёв — обязательно. Узел из первого ряда тянули в полосе автопрокрутки, и на
    /// большом графе, где кадр идёт миллисекунды, таймер успевал тикнуть по настоящим часам:
    /// холст ехал, узел двигался дважды за кадр, и счётчик показывал не степень узла.
    /// </remarks>
    private const int Dragged = 2 * Columns + 2;

    private static NodeStand CreateGraph(int nodes)
    {
        var locations = Enumerable.Range(0, nodes)
            .Select(i => new Point(i % Columns * 160, i / Columns * 110))
            .ToList();

        var stand = NodeStand.Create(locations, itemTemplate: NodeStand.PortedNode);
        for (var i = 0; i + 1 < nodes; i++)
            stand.Links.Add(new LinkData(NodeStand.Out(i), NodeStand.In(i + 1)));

        stand.RunLayout();
        return stand;
    }

    /// <summary>
    /// Медиана времени одного вызова в микросекундах; первый проход прогревает.
    /// </summary>
    private static double MicrosecondsPerCall(Action call, int calls)
    {
        var samples = new List<double>();
        for (var run = 0; run <= Runs; run++)
        {
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < calls; i++)
                call();

            watch.Stop();
            if (run > 0)
                samples.Add(watch.Elapsed.TotalMilliseconds * 1000 / calls);
        }

        samples.Sort();
        return samples[samples.Count / 2];
    }

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
    public void Hit_Testing_A_Link_Is_A_Linear_Search()
    {
        var times = new Dictionary<int, double>();
        foreach (var size in new[] { Small, Large })
        {
            var stand = CreateGraph(size);
            var link = stand.LinkOf(stand.Links[Dragged]);
            var on = link.Geometry.At(0.5);
            Assert.Same(link, stand.Editor.HitTestLink(on));

            times[size] = MicrosecondsPerCall(() => stand.Editor.HitTestLink(on), calls: 2000);
            _output.WriteLine($"попадание по связи, {size - 1} связей: {times[size]:F2} мкс");
        }

        var ratio = times[Large] / times[Small];
        _output.WriteLine($"отношение: {ratio:F1}");
        Assert.True(ratio > 4, $"{times[Large]:F2} против {times[Small]:F2}");
    }

    [AvaloniaFact]
    public void Finding_The_Port_Under_A_Loose_End_Is_A_Linear_Search()
    {
        var times = new Dictionary<int, double>();
        foreach (var size in new[] { Small, Large })
        {
            var stand = CreateGraph(size);
            var from = stand.PinCentreInWorld(0, PortDirection.Output);
            stand.Window.MouseMove(from);
            stand.Window.MouseDown(from, MouseButton.Left);
            var state = Assert.IsType<PendingLinkState>(stand.Editor.CurrentState);
            var near = stand.PinCentreInWorld(Dragged, PortDirection.Input);
            Assert.NotNull(state.FindCandidate(near, out _));

            times[size] = MicrosecondsPerCall(() => state.FindCandidate(near, out _), calls: 2000);
            _output.WriteLine($"порт под свободным концом, {2 * size} портов: {times[size]:F2} мкс");

            stand.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            stand.Window.MouseUp(from, MouseButton.Left);
        }

        var ratio = times[Large] / times[Small];
        _output.WriteLine($"отношение: {ratio:F1}");
        Assert.True(ratio > 4, $"{times[Large]:F2} против {times[Small]:F2}");
    }

    [AvaloniaFact]
    public void A_Drag_Frame_Grows_With_The_Graph_Though_Its_Links_Do_Not()
    {
        // Связи за кадр пересчитываются только у узла, и всё же кадр растёт вместе с графом:
        // платят раскладка, проходящая по всем детям обеих панелей, и отрисовка. Утверждается
        // только рост — кадр малого графа шумит в разы, — а разбивка печатается: по ней решается,
        // чем лечить, индексом или виртуализацией.
        var drag = new Dictionary<int, double>();
        foreach (var size in new[] { Small, Large })
        {
            var stand = CreateGraph(size);
            var empty = new Point(600, 205);

            // Наведение: указатель ходит по пустому холсту между рядами.
            var flip = false;
            var hover = MicrosecondsPerCall(() =>
            {
                flip = !flip;
                stand.Window.MouseMove(empty + (flip ? new Vector(10, 0) : default));
            }, calls: 200) / 1000;
            var linkHit = MicrosecondsPerCall(() => stand.Editor.HitTestLink(empty), calls: 200) / 1000;
            var treeHit = MicrosecondsPerCall(() => stand.Window.InputHitTest(empty), calls: 200) / 1000;

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
