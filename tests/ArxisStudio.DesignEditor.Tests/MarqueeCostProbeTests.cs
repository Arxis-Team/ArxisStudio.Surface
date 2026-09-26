using System.Diagnostics;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using ArxisStudio.Controls;
using Xunit;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Tests;

/// <summary>
/// Цена разрешения владельца рамки: по чему она растёт и что с ней делает снимок.
/// </summary>
/// <remarks>
/// <see cref="MarqueeScopeSnapshotTests"/> закрепляет семантику снимка, а здесь стоит
/// стенд, на котором он вообще понадобился: 500 контейнеров, формы из двух и из тридцати
/// контролов. Раньше такого стенда в репозитории не было, и числа в документации
/// воспроизвести было нечем — а число, которое нельзя перепроверить, документ портит.
/// <para>
/// Утверждается не время, а отношения: абсолютные миллисекунды зависят от машины,
/// и потолок по ним либо ничего не ловит, либо мигает. Числа при этом печатаются —
/// прогон здесь и есть источник тех величин, что стоят в <c>CLAUDE.md</c>.
/// </para>
/// </remarks>
public class MarqueeCostProbeTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Инициализирует пробу.</summary>
    /// <param name="output">Приёмник напечатанных величин.</param>
    public MarqueeCostProbeTests(ITestOutputHelper output) => _output = output;

    private const int Containers = 500;
    private const int Columns = 25;
    private const double ContainerWidth = 200;
    private const double ContainerHeight = 150;

    /// <summary>Кадров в одном замере: столько шагов даёт неспешная протяжка.</summary>
    private const int Frames = 200;

    /// <summary>
    /// Замеров, из которых берётся медиана.
    /// </summary>
    /// <remarks>
    /// Одиночный замер здесь уже один раз оказался шумом и назвал цену вчетверо
    /// больше настоящей. Медиана переживает случайный чужой квант процессора.
    /// </remarks>
    private const int Runs = 5;

    /// <summary>Прямоугольник рамки: лежит внутри первого контейнера.</summary>
    private static readonly Rect Marquee = new(10, 10, 60, 40);

    /// <summary>
    /// Собирает стенд из <see cref="Containers"/> форм заданного размера.
    /// </summary>
    /// <param name="controlsPerForm">Число контролов в одной форме.</param>
    /// <remarks>
    /// Designer-метаданные контролам не нужны: обход спускается по визуальному дереву
    /// и платит за каждый визуал независимо от того, можно ли его выбрать. Зато без них
    /// стенд не заводит пятнадцать тысяч подписок на <c>LayoutUpdated</c>.
    /// </remarks>
    private static EditorHarness CreateStand(int controlsPerForm)
    {
        var nodes = Enumerable.Range(0, Containers).Select(i => new TestNode("n" + i)).ToList();

        var editor = new DesignEditor
        {
            ItemsSource = nodes,
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<TestNode>((_, _) =>
            {
                var panel = new AbsolutePanel();
                for (var i = 0; i < controlsPerForm; i++)
                    panel.Children.Add(new Border { Width = 20, Height = 10 });

                return panel;
            }, supportsRecycling: false)
        };

        var window = new Window { Width = 800, Height = 600, Content = editor };
        window.Show();

        var harness = EditorHarness.Adopt(window, editor, nodes);
        harness.RunLayout();

        // Раскладывать по одному через PlaceContainer нельзя: он гоняет layout на
        // каждый контейнер, и стенд собирался бы пятьюстами проходами.
        for (var i = 0; i < Containers; i++)
        {
            var container = harness.Container(i);
            container.Width = ContainerWidth;
            container.Height = ContainerHeight;
            container.HorizontalAlignment = HorizontalAlignment.Left;
            container.VerticalAlignment = VerticalAlignment.Top;
            container.Location = new Point(i % Columns * 220, i / Columns * 170);
        }

        harness.RunLayout();
        return harness;
    }

    /// <summary>
    /// Медиана времени одного разрешения владельца рамки, в миллисекундах.
    /// </summary>
    /// <param name="harness">Стенд.</param>
    /// <param name="snapshot">Снят ли снимок контейнеров на время замера.</param>
    private static double Measure(EditorHarness harness, bool snapshot)
    {
        if (snapshot)
            harness.Editor.BeginContainerSnapshot();

        Pass(harness);   // прогрев: первый проход платит за JIT

        var samples = new List<double>();
        for (var i = 0; i < Runs; i++)
            samples.Add(Pass(harness));

        if (snapshot)
            harness.Editor.EndContainerSnapshot();

        samples.Sort();
        return samples[samples.Count / 2];
    }

    private static double Pass(EditorHarness harness)
    {
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < Frames; i++)
            harness.Editor.UpdateMarqueeScope(Marquee, useContainerSelection: false);

        watch.Stop();
        return watch.Elapsed.TotalMilliseconds / Frames;
    }

    /// <summary>
    /// Обход стоит по размеру макета, а не по числу форм.
    /// </summary>
    /// <remarks>
    /// Форм в обоих стендах поровну; разное — только их содержимое. Если бы цена
    /// шла по числу контейнеров, оба замера совпали бы, и снимок был бы не нужен.
    /// </remarks>
    [AvaloniaFact]
    public void The_Walk_Costs_By_Layout_Size_Not_Container_Count()
    {
        var small = Measure(CreateStand(controlsPerForm: 2), snapshot: false);
        var large = Measure(CreateStand(controlsPerForm: 30), snapshot: false);

        _output.WriteLine($"обход, {Containers} форм по 2 контрола:  {small:F3} мс на кадр");
        _output.WriteLine($"обход, {Containers} форм по 30 контролов: {large:F3} мс на кадр");
        _output.WriteLine($"отношение: {large / small:F2}");

        Assert.True(large > small * 1.5, $"{large:F3} против {small:F3}");
    }

    /// <summary>
    /// Снимок эту зависимость снимает.
    /// </summary>
    /// <remarks>
    /// Сравниваются отношения, а не времена: со снимком обход не идёт вовсе, поэтому
    /// содержимое форм на цену перестаёт влиять. Общая для обеих веток работа —
    /// геометрия и глубина каждого контейнера — при этом остаётся, и абсолютная
    /// разница поэтому куда скромнее, чем можно ждать от снятого обхода.
    /// </remarks>
    [AvaloniaFact]
    public void The_Snapshot_Takes_The_Layout_Size_Out_Of_The_Frame()
    {
        var smallStand = CreateStand(controlsPerForm: 2);
        var largeStand = CreateStand(controlsPerForm: 30);

        var freshRatio = Measure(largeStand, snapshot: false) / Measure(smallStand, snapshot: false);
        var snappedRatio = Measure(largeStand, snapshot: true) / Measure(smallStand, snapshot: true);

        _output.WriteLine($"без снимка форма из 30 дороже формы из 2 в {freshRatio:F2} раза");
        _output.WriteLine($"со снимком — в {snappedRatio:F2} раза");

        Assert.True(snappedRatio < freshRatio / 2, $"{snappedRatio:F2} против {freshRatio:F2}");
    }
}
