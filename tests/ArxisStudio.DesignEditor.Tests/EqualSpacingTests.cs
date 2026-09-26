using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ArxisStudio.Controls;
using ArxisStudio.States;
using Xunit;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Равные интервалы во время перетаскивания.
/// </summary>
/// <remarks>
/// Стенд — ряд из трёх: два неподвижных соседа и элемент между ними. Свободного места
/// между соседями 240, элемент шириной 40, значит ровное положение это 270 —
/// по 100 с каждой стороны.
/// <para>
/// Привязка к сетке выключена: иначе нельзя отличить, кто поставил элемент на место,
/// интервал или узел.
/// </para>
/// </remarks>
public class EqualSpacingTests
{
    private static readonly Point ContainerLocation = new(100, 100);
    private static readonly Size ContainerSize = new(500, 300);

    /// <summary>Геометрия перетаскиваемого элемента в design-координатах.</summary>
    private static readonly Rect Moving = new(200, 150, 40, 40);

    /// <summary>Положение, при котором зазоры до соседей равны.</summary>
    private const double EvenX = 270;

    private static EditorHarness Create(bool withAlignmentBait = false)
    {
        var nodes = new List<TestNode> { new("spacing0") };

        var editor = new DesignEditor
        {
            ItemsSource = nodes,
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<TestNode>((_, _) =>
            {
                // В сценарии с приманкой соседи по ряду намеренно сдвинуты по Y на 7:
                // иначе их верх совпадает с верхом Moving, ось Y занимает выравнивание,
                // а при обеих занятых осях интервалы не считаются вовсе — и тест
                // на приоритет проходил бы, ничего не проверяя.
                var rowY = withAlignmentBait ? 57 : 50;

                var panel = new AbsolutePanel();
                panel.Children.Add(Place("Left", 10, rowY, 60, 40));
                panel.Children.Add(Place("Moving", 100, 50, 40, 40));
                panel.Children.Add(Place("Right", 310, rowY, 60, 40));

                if (withAlignmentBait)
                {
                    // Другой ряд: по X он даёт кандидата выравнивания, а в интервал
                    // не входит — перекрытия по Y с Moving у него нет.
                    //
                    // Отступ по Y выбран так, чтобы ни один его край не оказался
                    // в радиусе захвата от края Moving: иначе выравнивание займёт
                    // и вторую ось, интервалы считаться перестанут, и тест снова
                    // проходил бы, ничего не проверяя.
                    panel.Children.Add(Place("Bait", 168, 0, 40, 40));
                }

                return panel;
            }, supportsRecycling: false)
        };

        editor.InteractionOptions.IsSnapToGridEnabled = false;

        var window = new Window { Width = 900, Height = 700, Content = editor };
        window.Show();

        var harness = EditorHarness.Adopt(window, editor, nodes);
        harness.RunLayout();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);
        return harness;
    }

    private static Border Place(string name, double x, double y, double width, double height)
    {
        var border = new Border { Name = name, Width = width, Height = height };
        Layout.SetX(border, x);
        Layout.SetY(border, y);
        return border;
    }

    private static Point MovingCentre => new(
        Moving.X + (Moving.Width / 2),
        Moving.Y + (Moving.Height / 2));

    private static double PositionOf(EditorHarness harness) =>
        Layout.GetDesignX(harness.Named(0, "Moving"));

    private static void Drag(EditorHarness harness, double dx, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        harness.Window.MouseDown(MovingCentre, MouseButton.Left, modifiers);
        harness.Window.MouseMove(MovingCentre + new Vector(6, 0), modifiers);
        harness.Window.MouseMove(MovingCentre + new Vector(dx, 0), modifiers);
        harness.Window.MouseUp(MovingCentre + new Vector(dx, 0), MouseButton.Left, modifiers);
        harness.RunLayout();
    }

    [AvaloniaFact]
    public void Equal_Spacing_Is_Enabled_By_Default()
    {
        var harness = Create();

        Assert.True(harness.Editor.InteractionOptions.IsEqualSpacingEnabled);
    }

    /// <summary>
    /// Подведённый к ровному положению элемент встаёт на него.
    /// </summary>
    [AvaloniaFact]
    public void The_Element_Snaps_To_The_Even_Position()
    {
        var harness = Create();

        // 200 + 64 = 264, до ровного положения остаётся 6 — радиус захвата.
        Drag(harness, 64);

        Assert.Equal(EvenX, PositionOf(harness));
    }

    [AvaloniaFact]
    public void Beyond_The_Tolerance_Nothing_Pulls()
    {
        var harness = Create();

        Drag(harness, 50);

        Assert.Equal(250, PositionOf(harness));
    }

    [AvaloniaFact]
    public void Disabling_It_Restores_Exact_Movement()
    {
        var harness = Create();
        harness.Editor.InteractionOptions.IsEqualSpacingEnabled = false;

        Drag(harness, 64);

        Assert.Equal(264, PositionOf(harness));
    }

    /// <summary>
    /// Модификатор обхода привязки снимает и интервалы.
    /// </summary>
    [AvaloniaFact]
    public void The_Bypass_Modifier_Ignores_Spacing()
    {
        var harness = Create();

        Drag(harness, 64, RawInputModifiers.Alt);

        Assert.Equal(264, PositionOf(harness));
    }

    /// <summary>
    /// Выравнивание сильнее интервала на той оси, которую заняло.
    /// </summary>
    /// <remarks>
    /// Выравнивание точнее: оно связывает элемент с конкретным краем соседа, а интервал —
    /// с расстоянием, которого на макете не видно. Приманка стоит на 268, ровное
    /// положение — 270, и элемент обязан встать на 268.
    /// </remarks>
    [AvaloniaFact]
    public void Alignment_Beats_Spacing_On_The_Same_Axis()
    {
        var harness = Create(withAlignmentBait: true);

        Drag(harness, 64);

        Assert.Equal(268, PositionOf(harness));
    }

    // ---- Повтор шага ----------------------------------------------------------

    /// <summary>
    /// Стенд-ряд: два элемента с шагом 40 и третий за ними, без соседа справа.
    /// </summary>
    /// <remarks>
    /// Форма шире общего стенда намеренно: при ширине 500 её правый край попадает
    /// в радиус захвата от края элемента, ось X занимает выравнивание, и проверялось
    /// бы не то. Ровное положение здесь — 310.
    /// </remarks>
    private static EditorHarness CreateRun()
    {
        var nodes = new List<TestNode> { new("run0") };

        var editor = new DesignEditor
        {
            ItemsSource = nodes,
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<TestNode>((_, _) =>
            {
                var panel = new AbsolutePanel();
                panel.Children.Add(Place("First", 10, 50, 60, 40));
                panel.Children.Add(Place("Second", 110, 50, 60, 40));
                panel.Children.Add(Place("Moving", 240, 50, 40, 40));
                return panel;
            }, supportsRecycling: false)
        };

        editor.InteractionOptions.IsSnapToGridEnabled = false;

        var window = new Window { Width = 900, Height = 700, Content = editor };
        window.Show();

        var harness = EditorHarness.Adopt(window, editor, nodes);
        harness.RunLayout();
        harness.PlaceContainer(0, ContainerLocation, new Size(540, 300));
        return harness;
    }

    /// <summary>
    /// Элемент в конце ряда подхватывает шаг, который там уже стоит.
    /// </summary>
    /// <remarks>
    /// Справа от него никого нет, поэтому положение посередине невозможно: без повтора
    /// шага интервалы здесь не сработали бы вовсе.
    /// </remarks>
    [AvaloniaFact]
    public void A_Drag_Continues_The_Existing_Step()
    {
        var harness = CreateRun();
        var moving = harness.Named(0, "Moving");
        var from = new Point(340 + 20, 150 + 20);

        harness.Window.MouseDown(from, MouseButton.Left);
        harness.Window.MouseMove(from + new Vector(-6, 0));
        harness.Window.MouseMove(from + new Vector(-24, 0));
        harness.Window.MouseUp(from + new Vector(-24, 0), MouseButton.Left);
        harness.RunLayout();

        // Шаг между First и Second — 40, значит Moving встаёт на 270 + 40 = 310.
        Assert.Equal(310, Layout.GetDesignX(moving));
    }

    // ---- Изменение размера ----------------------------------------------------

    /// <summary>
    /// Тянет правый край так, как его видит редактор: ручка стоит на применённом крае.
    /// </summary>
    private static void DragRightEdge(EditorHarness harness, double to)
    {
        var target = harness.Named(0, "Moving");
        var container = harness.Container(0);
        var state = new ItemResizingState(container, target, ResizeDirection.Right);
        container.PushState(state);

        var applied = harness.Editor.GetDesignPosition(target).X + harness.Editor.GetDesignSize(target).Width;

        state.OnResizeDelta(new ResizeDeltaEventArgs(
            new Vector(to - applied, 0),
            ResizeDirection.Right,
            DesignEditorItem.ResizeDeltaEvent));

        harness.RunLayout();
    }

    /// <summary>
    /// Потянутый край повторяет зазор неподвижного.
    /// </summary>
    /// <remarks>
    /// Левый зазор 30 (200 − 170), значит правый край обязан встать на 380 — на столько же
    /// от левого края правого соседа.
    /// </remarks>
    [AvaloniaFact]
    public void Resize_Repeats_The_Fixed_Gap()
    {
        var harness = Create();

        DragRightEdge(harness, 374);

        var target = harness.Named(0, "Moving");
        Assert.Equal(380, harness.Editor.GetDesignPosition(target).X + harness.Editor.GetDesignSize(target).Width);

        // Неподвижный край остался на месте: привязывается край, а не размер.
        Assert.Equal(Moving.X, harness.Editor.GetDesignPosition(target).X);
    }

    [AvaloniaFact]
    public void Resize_Beyond_The_Tolerance_Keeps_The_Edge()
    {
        var harness = Create();

        DragRightEdge(harness, 360);

        var target = harness.Named(0, "Moving");
        Assert.Equal(360, harness.Editor.GetDesignPosition(target).X + harness.Editor.GetDesignSize(target).Width);
    }

    [AvaloniaFact]
    public void Disabled_Spacing_Leaves_The_Resized_Edge_Alone()
    {
        var harness = Create();
        harness.Editor.InteractionOptions.IsEqualSpacingEnabled = false;

        DragRightEdge(harness, 374);

        var target = harness.Named(0, "Moving");
        Assert.Equal(374, harness.Editor.GetDesignPosition(target).X + harness.Editor.GetDesignSize(target).Width);
    }

    /// <summary>
    /// Подсказки показываются и при изменении размера.
    /// </summary>
    /// <remarks>
    /// Публикация у resize своя точка входа, поэтому она проверяется отдельно:
    /// на перетаскивании тот же набор собирается другим вызовом.
    /// </remarks>
    [AvaloniaFact]
    public void Resize_Shows_The_Hints_Too()
    {
        var harness = Create();

        DragRightEdge(harness, 374);

        var hints = harness.Editor.SpacingHints;
        Assert.Equal(2, hints.Count);
        Assert.All(hints, h => Assert.Equal(30, h.End - h.Start, 3));
    }

    // ---- Подсказки ------------------------------------------------------------

    [AvaloniaFact]
    public void Hints_Are_Published_During_The_Gesture()
    {
        var harness = Create();

        harness.Window.MouseDown(MovingCentre, MouseButton.Left);
        harness.Window.MouseMove(MovingCentre + new Vector(6, 0));
        harness.Window.MouseMove(MovingCentre + new Vector(64, 0));
        harness.RunLayout();

        var hints = harness.Editor.SpacingHints;
        Assert.Equal(2, hints.Count);
        Assert.All(hints, h => Assert.Equal(100, h.End - h.Start, 3));

        harness.Window.MouseUp(MovingCentre + new Vector(64, 0), MouseButton.Left);
    }

    [AvaloniaFact]
    public void Hints_Are_Cleared_When_The_Gesture_Ends()
    {
        var harness = Create();

        Drag(harness, 64);

        Assert.Empty(harness.Editor.SpacingHints);
    }

    /// <summary>
    /// Неровное положение подсказок не даёт.
    /// </summary>
    [AvaloniaFact]
    public void Unequal_Gaps_Show_Nothing()
    {
        var harness = Create();

        harness.Window.MouseDown(MovingCentre, MouseButton.Left);
        harness.Window.MouseMove(MovingCentre + new Vector(6, 0));
        harness.Window.MouseMove(MovingCentre + new Vector(30, 0));
        harness.RunLayout();

        Assert.Empty(harness.Editor.SpacingHints);

        harness.Window.MouseUp(MovingCentre + new Vector(30, 0), MouseButton.Left);
    }
}
