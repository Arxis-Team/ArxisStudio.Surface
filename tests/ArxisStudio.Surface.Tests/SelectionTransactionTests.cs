using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Одна операция выделения — одно событие.
/// </summary>
/// <remarks>
/// Выделение пишется из десяти мест, и каждое делало это по-своему: батчинг стоял
/// в двух из десяти. Там, где его нет, промежуточное состояние публикуется наружу —
/// хост, зеркалящий выделение в своё дерево, видит его как отдельное изменение
/// и гасит подсветку между двумя событиями одного жеста.
/// <para>
/// Стенд на двух контейнерах: в одном замена индексного слоя не наблюдаема.
/// </para>
/// </remarks>
public class SelectionTransactionTests
{
    private static (EditorHarness Harness, List<SurfaceSelectionChangedEventArgs> Events) Create()
    {
        var harness = EditorHarness.Create(nodeCount: 2);
        harness.PlaceContainer(0, new Point(100, 100), new Size(200, 150));
        harness.PlaceContainer(1, new Point(400, 100), new Size(200, 150));

        var events = new List<SurfaceSelectionChangedEventArgs>();
        harness.Editor.SurfaceSelectionChanged += (_, e) => events.Add(e);
        return (harness, events);
    }

    private static void Click(EditorHarness harness, Point point, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        harness.Window.MouseDown(point, MouseButton.Left, modifiers);
        harness.Window.MouseUp(point, MouseButton.Left, modifiers);
        harness.RunLayout();
    }

    [AvaloniaFact]
    public void Select_All_Raises_One_Event()
    {
        var (harness, events) = Create();
        harness.Editor.Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        harness.RunLayout();

        // Индексный слой пишется в батче, слой target'ов — после и снаружи него.
        var single = Assert.Single(events);
        Assert.Equal(2, single.NewTargets.Count);
    }

    [AvaloniaFact]
    public void Collapsing_A_Group_Never_Publishes_An_Empty_Selection()
    {
        var (harness, events) = Create();
        harness.Editor.Focus();
        harness.Window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        harness.RunLayout();
        events.Clear();

        // Обычный клик по уже выбранному контейнеру схлопывает группу до него.
        Click(harness, harness.CentreOf(harness.Nested(0)));

        // Событий два, и оба описывают настоящие изменения: на нажатии сменился
        // primary, на отпускании схлопнулась группа. Схлопывание отложено на
        // отпускание намеренно — иначе группу нельзя было бы потащить.
        //
        // Проверяется не их число, а то, что наружу не публикуется состояние,
        // которого не было: раньше здесь было три события, и одно из них
        // объявляло пустое выделение между двумя записями индексного слоя.
        Assert.All(events, e => Assert.NotEmpty(e.NewTargets));
        Assert.Equal(1, harness.Editor.SelectedTargetsCount);
    }

    [AvaloniaFact]
    public void Starting_A_Marquee_Raises_One_Event()
    {
        var (harness, events) = Create();
        harness.Editor.SelectTarget(harness.Nested(0));
        harness.RunLayout();
        events.Clear();

        // Рамка сбрасывает прежнее выделение двумя записями индексного слоя подряд.
        var empty = new Point(700, 450);
        harness.Window.MouseDown(empty, MouseButton.Left);
        harness.Window.MouseMove(empty + new Vector(30, 20));
        harness.RunLayout();

        Assert.Single(events);
    }

    [AvaloniaFact]
    public void Clearing_By_Escape_Raises_One_Event()
    {
        var (harness, events) = Create();
        harness.Editor.Focus();
        harness.Window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        harness.RunLayout();
        events.Clear();

        harness.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        harness.RunLayout();

        var single = Assert.Single(events);
        Assert.Empty(single.NewTargets);
    }
}
