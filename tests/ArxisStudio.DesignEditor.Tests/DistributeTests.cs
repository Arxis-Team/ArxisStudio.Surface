using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using ArxisStudio.Controls;
using Xunit;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Surface.Editing;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Распределение выбранных элементов с равными зазорами.
/// </summary>
/// <remarks>
/// Это операция над выделением, а не подсказка внутри жеста: она выполняется целиком
/// и одной единицей редактирования. Крайние элементы задают отрезок и остаются на месте,
/// промежуточные расставляются.
/// <para>
/// Стенд: три элемента разной ширины в ряду. Ширины разные намеренно — при одинаковых
/// равные зазоры и равные центры дают один ответ, и тест не отличил бы одно от другого.
/// </para>
/// </remarks>
public class DistributeTests
{
    private static readonly Point ContainerLocation = new(100, 100);
    private static readonly Size ContainerSize = new(500, 300);

    private static EditorHarness Create()
    {
        var nodes = new List<TestNode> { new("dist0") };

        var editor = new DesignEditor
        {
            ItemsSource = nodes,
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<TestNode>((_, _) =>
            {
                var panel = new AbsolutePanel();

                // Мировые координаты: A 110..170, B 210..250, C 350..450.
                panel.Children.Add(Place("A", 10, 50, 60, 40));
                panel.Children.Add(Place("B", 110, 50, 40, 40));
                panel.Children.Add(Place("C", 250, 50, 100, 40));
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

    private static void SelectAllThree(EditorHarness harness)
    {
        harness.Editor.SelectDesignTarget(harness.Named(0, "A"));
        harness.Editor.SelectDesignTarget(harness.Named(0, "B"), additive: true);
        harness.Editor.SelectDesignTarget(harness.Named(0, "C"), additive: true);
        harness.RunLayout();
    }

    private static double X(EditorHarness harness, string name) =>
        harness.Editor.GetDesignPosition(harness.Named(0, name)).X;

    [AvaloniaFact]
    public void Gaps_Become_Equal()
    {
        var harness = Create();
        SelectAllThree(harness);

        Assert.True(harness.Editor.DistributeHorizontally());
        harness.RunLayout();

        // Отрезок 110..450 длиной 340, занято 60+40+100 = 200, свободного 140 на два
        // зазора — по 70. Значит B встаёт на 170 + 70 = 240.
        Assert.Equal(110, X(harness, "A"));
        Assert.Equal(240, X(harness, "B"));
        Assert.Equal(350, X(harness, "C"));
    }

    /// <summary>
    /// Равными делаются зазоры, а не центры.
    /// </summary>
    /// <remarks>
    /// При разной ширине это разные ответы: ровные центры поставили бы B на 260.
    /// Вся остальная библиотека говорит о зазорах, и операция обязана говорить так же.
    /// </remarks>
    [AvaloniaFact]
    public void It_Is_Gaps_Not_Centres()
    {
        var harness = Create();
        SelectAllThree(harness);

        harness.Editor.DistributeHorizontally();
        harness.RunLayout();

        var b = harness.Editor.GetDesignPosition(harness.Named(0, "B"));
        Assert.NotEqual(260, b.X);
    }

    [AvaloniaFact]
    public void The_Outer_Elements_Do_Not_Move()
    {
        var harness = Create();
        SelectAllThree(harness);

        harness.Editor.DistributeHorizontally();
        harness.RunLayout();

        Assert.Equal(110, X(harness, "A"));
        Assert.Equal(350, X(harness, "C"));
    }

    /// <summary>
    /// Ось не трогается по другой координате.
    /// </summary>
    [AvaloniaFact]
    public void The_Other_Axis_Is_Left_Alone()
    {
        var harness = Create();
        SelectAllThree(harness);

        harness.Editor.DistributeHorizontally();
        harness.RunLayout();

        Assert.Equal(150, harness.Editor.GetDesignPosition(harness.Named(0, "B")).Y);
    }

    [AvaloniaFact]
    public void Fewer_Than_Three_Is_Refused()
    {
        var harness = Create();
        harness.Editor.SelectDesignTarget(harness.Named(0, "A"));
        harness.Editor.SelectDesignTarget(harness.Named(0, "C"), additive: true);
        harness.RunLayout();

        Assert.False(harness.Editor.DistributeHorizontally());
        Assert.Equal(110, X(harness, "A"));
        Assert.Equal(350, X(harness, "C"));
    }

    /// <summary>
    /// Заблокированный промежуточный элемент отменяет операцию целиком.
    /// </summary>
    /// <remarks>
    /// Расставить остальных значило бы выдать за распределение то, что им не является:
    /// зазоры равными всё равно не станут. То же правило, по которому смешанная группа
    /// не двигается вовсе.
    /// </remarks>
    [AvaloniaFact]
    public void A_Locked_Middle_Element_Cancels_Everything()
    {
        var harness = Create();
        DesignInteraction.SetMovePolicy(harness.Named(0, "B"), MovePolicy.Y);
        SelectAllThree(harness);

        Assert.False(harness.Editor.DistributeHorizontally());
        Assert.Equal(210, X(harness, "B"));
    }

    /// <summary>
    /// Политика крайних роли не играет: они и так не двигаются.
    /// </summary>
    [AvaloniaFact]
    public void A_Locked_Outer_Element_Does_Not_Block_It()
    {
        var harness = Create();
        DesignInteraction.SetMovePolicy(harness.Named(0, "A"), MovePolicy.None);
        SelectAllThree(harness);

        Assert.True(harness.Editor.DistributeHorizontally());
        harness.RunLayout();

        Assert.Equal(240, X(harness, "B"));
    }

    /// <summary>
    /// Вся операция — одна единица редактирования.
    /// </summary>
    /// <remarks>
    /// Иначе отмена разбирала бы её по одному элементу, и промежуточные состояния
    /// показывали бы наполовину распределённый ряд.
    /// </remarks>
    [AvaloniaFact]
    public void The_Whole_Operation_Is_One_Edit()
    {
        var harness = Create();
        SelectAllThree(harness);

        var edits = new List<DesignEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);

        harness.Editor.DistributeHorizontally();
        harness.RunLayout();

        var edit = Assert.Single(edits);
        Assert.Equal(DesignEditKind.Move, edit.Kind);
    }

    [AvaloniaFact]
    public void Reverting_Puts_Everything_Back()
    {
        var harness = Create();
        SelectAllThree(harness);

        DesignEditCompletedEventArgs? edit = null;
        harness.Editor.EditCompleted += (_, e) => edit = e;

        harness.Editor.DistributeHorizontally();
        harness.RunLayout();
        Assert.Equal(240, X(harness, "B"));

        foreach (var change in edit!.Changes)
            harness.Editor.Revert(change);

        harness.RunLayout();
        Assert.Equal(210, X(harness, "B"));
    }

    [AvaloniaFact]
    public void The_Vertical_Axis_Works_The_Same_Way()
    {
        var harness = Create();

        // Ставим тот же ряд вертикально: A 110..170, B 210..250, C 350..450 по Y.
        Layout.SetX(harness.Named(0, "A"), 10);
        Layout.SetY(harness.Named(0, "A"), 10);
        Layout.SetX(harness.Named(0, "B"), 10);
        Layout.SetY(harness.Named(0, "B"), 110);
        Layout.SetX(harness.Named(0, "C"), 10);
        Layout.SetY(harness.Named(0, "C"), 250);

        harness.Named(0, "A").Height = 60;
        harness.Named(0, "B").Height = 40;
        harness.Named(0, "C").Height = 100;
        harness.RunLayout();

        SelectAllThree(harness);

        Assert.True(harness.Editor.DistributeVertically());
        harness.RunLayout();

        Assert.Equal(240, harness.Editor.GetDesignPosition(harness.Named(0, "B")).Y);
    }
}
