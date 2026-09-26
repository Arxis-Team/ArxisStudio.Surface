using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Tests;

/// <summary>
/// Направляющие и интервалы при групповом перетаскивании.
/// </summary>
/// <remarks>
/// Группа притягивается <b>рамкой выделения</b>, а не тем элементом, за который её
/// схватили. Так уже устроен групповой resize, и так же выглядит происходящее на экране:
/// пользователь видит рамку группы, её и ведёт.
/// <para>
/// Стенд: <c>Mate</c> слева, <c>Grabbed</c> справа, тянут за правый. Левый край рамки
/// принадлежит <c>Mate</c>, поэтому ответы двух правил различаются — иначе тест
/// не отличил бы одно от другого.
/// </para>
/// </remarks>
public class GroupSnapTests
{
    private static readonly Point ContainerLocation = new(100, 100);
    private static readonly Size ContainerSize = new(540, 300);

    private static EditorHarness Create(double mateX = 60)
    {
        var nodes = new List<TestNode> { new("group0") };

        var editor = new DesignEditor
        {
            ItemsSource = nodes,
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<TestNode>((_, _) =>
            {
                var panel = new AbsolutePanel();

                // Мировые координаты: Mate 160..200, Grabbed 300..340 — рамка 160..340.
                panel.Children.Add(Place("Mate", mateX, 50, 40, 40));
                panel.Children.Add(Place("Grabbed", 200, 50, 40, 40));

                // Сосед, к левому краю которого идёт выравнивание: 170..230.
                panel.Children.Add(Place("Anchor", 70, 150, 60, 40));
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

    private static void SelectPair(EditorHarness harness)
    {
        harness.Editor.SelectDesignTarget(harness.Named(0, "Mate"));
        harness.Editor.SelectDesignTarget(harness.Named(0, "Grabbed"), additive: true);
        harness.RunLayout();
    }

    private static void DragGrabbed(EditorHarness harness, double dx)
    {
        var from = new Point(320, 170);

        harness.Window.MouseDown(from, MouseButton.Left);
        harness.Window.MouseMove(from + new Vector(4, 0));
        harness.Window.MouseMove(from + new Vector(dx, 0));
        harness.Window.MouseUp(from + new Vector(dx, 0), MouseButton.Left);
        harness.RunLayout();
    }

    private static double X(EditorHarness harness, string name) =>
        harness.Editor.GetDesignPosition(harness.Named(0, name)).X;

    /// <summary>
    /// Притягивается рамка группы, а не схваченный элемент.
    /// </summary>
    /// <remarks>
    /// Сдвиг на 5 ставит левый край рамки на 165, до края соседа остаётся 5 —
    /// в пределах радиуса захвата. Рамка встаёт на 170, значит и <c>Grabbed</c>
    /// уезжает на те же 10 от исходных 300.
    /// <para>
    /// Правило по схваченному элементу дало бы 305: его собственные края и центр
    /// ни с чем не совпадают.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void The_Group_Frame_Catches_The_Guide()
    {
        var harness = Create();
        SelectPair(harness);

        DragGrabbed(harness, 5);

        Assert.Equal(310, X(harness, "Grabbed"));
    }

    /// <summary>
    /// Взаимное расположение внутри группы сохраняется.
    /// </summary>
    [AvaloniaFact]
    public void The_Group_Keeps_Its_Shape()
    {
        var harness = Create();
        SelectPair(harness);

        DragGrabbed(harness, 5);

        Assert.Equal(X(harness, "Grabbed") - 140, X(harness, "Mate"));
    }

    /// <summary>
    /// Линия публикуется по рамке, а не по элементу.
    /// </summary>
    [AvaloniaFact]
    public void The_Published_Guide_Belongs_To_The_Frame()
    {
        var harness = Create();
        SelectPair(harness);
        var from = new Point(320, 170);

        harness.Window.MouseDown(from, MouseButton.Left);
        harness.Window.MouseMove(from + new Vector(4, 0));
        harness.Window.MouseMove(from + new Vector(5, 0));
        harness.RunLayout();

        var guide = Assert.Single(harness.Editor.SnapGuides);
        Assert.Equal(170, guide.Position);

        harness.Window.MouseUp(from + new Vector(5, 0), MouseButton.Left);
    }

    /// <summary>
    /// На узел сетки садится тоже рамка.
    /// </summary>
    /// <remarks>
    /// Правило одно на всю привязку, поэтому проверяется и здесь. Стенд подобран
    /// так, что схваченный элемент отстоит от угла рамки на 134 — не на кратное шагу,
    /// — иначе оба правила дали бы один ответ и тест ничего бы не показал.
    /// Выравнивание и интервалы выключены: они заняли бы ось раньше сетки.
    /// </remarks>
    [AvaloniaFact]
    public void The_Grid_Node_Belongs_To_The_Frame_Too()
    {
        var harness = Create(mateX: 66);
        harness.Editor.InteractionOptions.IsSnapToGridEnabled = true;
        harness.Editor.InteractionOptions.IsSnapToGuidesEnabled = false;
        harness.Editor.InteractionOptions.IsEqualSpacingEnabled = false;
        SelectPair(harness);

        // Сдвиг на 7 ставит угол рамки на 173, ближайший узел — 180.
        DragGrabbed(harness, 7);

        Assert.Equal(314, X(harness, "Grabbed"));
    }

    // ---- Интервалы ------------------------------------------------------------

    /// <summary>
    /// Стенд-ряд: рамка группы между двумя соседями той же строки.
    /// </summary>
    /// <remarks>
    /// Мировые координаты: <c>Before</c> 100..140, рамка 180..320, <c>After</c> 560..600.
    /// Свободного места 420, ровное положение рамки — 280.
    /// </remarks>
    private static EditorHarness CreateRow()
    {
        var nodes = new List<TestNode> { new("grouprow0") };

        var editor = new DesignEditor
        {
            ItemsSource = nodes,
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<TestNode>((_, _) =>
            {
                var panel = new AbsolutePanel();
                panel.Children.Add(Place("Before", 0, 50, 40, 40));
                panel.Children.Add(Place("Mate", 80, 50, 40, 40));
                panel.Children.Add(Place("Grabbed", 180, 50, 40, 40));
                panel.Children.Add(Place("After", 460, 50, 40, 40));
                return panel;
            }, supportsRecycling: false)
        };

        editor.InteractionOptions.IsSnapToGridEnabled = false;

        var window = new Window { Width = 900, Height = 700, Content = editor };
        window.Show();

        var harness = EditorHarness.Adopt(window, editor, nodes);
        harness.RunLayout();
        harness.PlaceContainer(0, ContainerLocation, new Size(700, 300));
        return harness;
    }

    /// <summary>
    /// Интервалы считаются по рамке группы.
    /// </summary>
    /// <remarks>
    /// Это следует из того же отображения: раз разрешается рамка, интервал считается
    /// для неё, и группа встаёт между соседями как один элемент. Отдельной ветки
    /// под группу не понадобилось.
    /// </remarks>
    [AvaloniaFact]
    public void The_Group_Frame_Takes_The_Even_Position()
    {
        var harness = CreateRow();
        SelectPair(harness);

        var from = new Point(300, 170);
        harness.Window.MouseDown(from, MouseButton.Left);
        harness.Window.MouseMove(from + new Vector(4, 0));
        harness.Window.MouseMove(from + new Vector(96, 0));
        harness.RunLayout();

        // Рамка встаёт на 280, значит Grabbed — на 100 правее, то есть на 380.
        Assert.Equal(380, X(harness, "Grabbed"));

        var hints = harness.Editor.SpacingHints;
        Assert.Equal(2, hints.Count);
        Assert.All(hints, h => Assert.Equal(140, h.End - h.Start, 3));

        harness.Window.MouseUp(from + new Vector(96, 0), MouseButton.Left);
    }

    /// <summary>
    /// Одиночное перетаскивание правилом группы не затрагивается.
    /// </summary>
    [AvaloniaFact]
    public void A_Single_Drag_Still_Uses_The_Element()
    {
        var harness = Create();
        harness.Editor.SelectDesignTarget(harness.Named(0, "Grabbed"));
        harness.RunLayout();

        DragGrabbed(harness, 5);

        // Рамка одного элемента — он сам, и совпадений у него нет.
        Assert.Equal(305, X(harness, "Grabbed"));
    }
}
