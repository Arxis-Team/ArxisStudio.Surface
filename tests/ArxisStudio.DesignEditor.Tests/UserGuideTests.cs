using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface.Editing;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Tests;

/// <summary>
/// Пользовательские направляющие: заданные снаружи линии, к которым идёт притяжение.
/// </summary>
/// <remarks>
/// От направляющих выравнивания они отличаются происхождением и сроком жизни: те
/// редактор находит сам и показывает только внутри жеста, эти задаёт хост и они
/// видны всегда. Механика притяжения при этом общая — направляющая приходит в резолвер
/// прямоугольником нулевой толщины, у которого ближний край, центр и дальний край
/// совпадают, поэтому отдельного правила под неё нет.
/// <para>
/// Стенд подобран так, чтобы направляющая была единственным объяснением результата:
/// и контейнер, и сосед, и шаг сетки отстоят от проверяемой позиции дальше радиуса
/// захвата. Иначе тест не отличил бы, кто поставил элемент на место.
/// </para>
/// </remarks>
public class UserGuideTests
{
    private static readonly Point ContainerLocation = new(105, 103);
    private static readonly Size ContainerSize = new(320, 260);

    /// <summary>Геометрия перетаскиваемого контрола в design-координатах.</summary>
    private static readonly Rect Moving = new(115, 113, 60, 40);

    /// <summary>Сосед: нужен только как источник конкурирующих кандидатов.</summary>
    private static readonly Rect Anchor = new(305, 203, 100, 80);

    private static EditorHarness Create(bool snapToGrid = false)
    {
        var nodes = new List<TestNode> { new("user0") };

        var editor = new DesignEditor
        {
            ItemsSource = nodes,
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<TestNode>((_, _) =>
            {
                var moving = new Border { Name = "Moving", Width = Moving.Width, Height = Moving.Height };
                Layout.SetX(moving, Moving.X - ContainerLocation.X);
                Layout.SetY(moving, Moving.Y - ContainerLocation.Y);

                var anchor = new Border { Name = "Anchor", Width = Anchor.Width, Height = Anchor.Height };
                Layout.SetX(anchor, Anchor.X - ContainerLocation.X);
                Layout.SetY(anchor, Anchor.Y - ContainerLocation.Y);

                var panel = new AbsolutePanel();
                panel.Children.Add(moving);
                panel.Children.Add(anchor);
                return panel;
            }, supportsRecycling: false)
        };

        var window = new Window { Width = 800, Height = 600, Content = editor };
        editor.InteractionOptions.IsSnapToGridEnabled = snapToGrid;
        window.Show();

        var harness = EditorHarness.Adopt(window, editor, nodes);
        harness.RunLayout();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);
        return harness;
    }

    private static Point MovingCentre => new(
        Moving.X + (Moving.Width / 2),
        Moving.Y + (Moving.Height / 2));

    private static Point PositionOf(EditorHarness harness) => new(
        Layout.GetDesignX(harness.Named(0, "Moving")),
        Layout.GetDesignY(harness.Named(0, "Moving")));

    private static void Drag(EditorHarness harness, Vector delta, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        harness.Window.MouseDown(MovingCentre, MouseButton.Left, modifiers);
        harness.Window.MouseMove(MovingCentre + new Vector(6, 4), modifiers);
        harness.Window.MouseMove(MovingCentre + delta, modifiers);
        harness.Window.MouseUp(MovingCentre + delta, MouseButton.Left, modifiers);
        harness.RunLayout();
    }

    // ---- Притяжение -----------------------------------------------------------

    /// <summary>
    /// Смещение на 100 ставит левый край на 215; направляющая на 218 добирает три.
    /// </summary>
    [AvaloniaFact]
    public void A_Vertical_Guide_Catches_The_Left_Edge()
    {
        var harness = Create();
        harness.Editor.Guides = new[] { DesignGuide.Vertical(218) };

        Drag(harness, new Vector(100, 0));

        Assert.Equal(218, PositionOf(harness).X);

        // По второй оси не изменилось ничего: направляющая вертикальная,
        // а сетка в этом стенде выключена.
        Assert.Equal(Moving.Y, PositionOf(harness).Y);
    }

    /// <summary>
    /// То же для горизонтальной: смещение на 40 даёт верх 153, направляющая на 156.
    /// </summary>
    [AvaloniaFact]
    public void A_Horizontal_Guide_Catches_The_Top_Edge()
    {
        var harness = Create();
        harness.Editor.Guides = new[] { DesignGuide.Horizontal(156) };

        Drag(harness, new Vector(0, 40));

        Assert.Equal(156, PositionOf(harness).Y);
        Assert.Equal(Moving.X, PositionOf(harness).X);
    }

    /// <summary>
    /// Направляющая сильнее сетки на занятой оси, а сетка забирает вторую.
    /// </summary>
    /// <remarks>
    /// Композиция та же, что у направляющих выравнивания: по X встаём на 218,
    /// а не на узел 220, по Y — на узел 120. Порядок наоборот сбивал бы найденное
    /// выравнивание, ради которого пользователь направляющую и поставил.
    /// </remarks>
    [AvaloniaFact]
    public void A_Guide_Beats_The_Grid_On_Its_Own_Axis()
    {
        var harness = Create(snapToGrid: true);
        harness.Editor.Guides = new[] { DesignGuide.Vertical(218) };

        Drag(harness, new Vector(100, 0));

        Assert.Equal(218, PositionOf(harness).X);
        Assert.Equal(120, PositionOf(harness).Y);
    }

    /// <summary>
    /// За радиусом захвата направляющая не действует вовсе.
    /// </summary>
    [AvaloniaFact]
    public void Beyond_The_Tolerance_The_Guide_Is_Ignored()
    {
        var harness = Create();
        harness.Editor.Guides = new[] { DesignGuide.Vertical(230) };

        Drag(harness, new Vector(100, 0));

        Assert.Equal(215, PositionOf(harness).X);
    }

    /// <summary>
    /// Модификатор обхода привязки снимает и пользовательские направляющие.
    /// </summary>
    /// <remarks>
    /// Обещание одно на всю привязку — «держу нажатым, ставлю куда хочу», — и делить
    /// его между видами направляющих было бы нечестно.
    /// </remarks>
    [AvaloniaFact]
    public void The_Bypass_Modifier_Ignores_User_Guides()
    {
        var harness = Create();
        harness.Editor.Guides = new[] { DesignGuide.Vertical(218) };

        Drag(harness, new Vector(100, 0), RawInputModifiers.Alt);

        Assert.Equal(215, PositionOf(harness).X);
    }

    // ---- Владение набором -----------------------------------------------------

    /// <summary>
    /// Направляющая, добавленная в уже присвоенную коллекцию, действует сразу.
    /// </summary>
    /// <remarks>
    /// Хост держит один экземпляр коллекции и правит содержимое; требовать
    /// переприсваивания свойства значило бы навязывать ему чужую дисциплину.
    /// </remarks>
    [AvaloniaFact]
    public void Adding_To_A_Live_Collection_Takes_Effect()
    {
        var harness = Create();
        var guides = new ObservableCollection<DesignGuide>();
        harness.Editor.Guides = guides;

        guides.Add(DesignGuide.Vertical(218));

        Drag(harness, new Vector(100, 0));

        Assert.Equal(218, PositionOf(harness).X);
    }

    /// <summary>
    /// Убранная направляющая перестаёт притягивать.
    /// </summary>
    [AvaloniaFact]
    public void Removing_A_Guide_Takes_Effect_Too()
    {
        var harness = Create();
        var guides = new ObservableCollection<DesignGuide> { DesignGuide.Vertical(218) };
        harness.Editor.Guides = guides;

        guides.Clear();

        Drag(harness, new Vector(100, 0));

        Assert.Equal(215, PositionOf(harness).X);
    }

    /// <summary>
    /// Равный по содержимому набор не публикуется заново.
    /// </summary>
    /// <remarks>
    /// Та же дисциплина, что у выделения и у линий выравнивания: снимок сравнивается
    /// с текущим, и совпавший не меняет идентичность. Иначе каждое переприсваивание
    /// эквивалентной коллекции инвалидировало бы привязку слоя и вызывало перерисовку.
    /// </remarks>
    [AvaloniaFact]
    public void An_Equivalent_Collection_Is_Not_Republished()
    {
        var harness = Create();
        harness.Editor.Guides = new[] { DesignGuide.Vertical(218), DesignGuide.Horizontal(156) };
        var published = harness.Editor.UserGuides;

        harness.Editor.Guides = new[] { DesignGuide.Vertical(218), DesignGuide.Horizontal(156) };

        Assert.Same(published, harness.Editor.UserGuides);
    }

    /// <summary>
    /// Снятая коллекция очищает снимок.
    /// </summary>
    [AvaloniaFact]
    public void Clearing_The_Source_Clears_The_Snapshot()
    {
        var harness = Create();
        harness.Editor.Guides = new[] { DesignGuide.Vertical(218) };
        Assert.Single(harness.Editor.UserGuides);

        harness.Editor.Guides = null;

        Assert.Empty(harness.Editor.UserGuides);
    }

    // ---- Модель ---------------------------------------------------------------

    [AvaloniaFact]
    public void Guides_Compare_By_Value()
    {
        Assert.Equal(DesignGuide.Vertical(10), DesignGuide.Vertical(10));
        Assert.NotEqual(DesignGuide.Vertical(10), DesignGuide.Horizontal(10));
        Assert.True(DesignGuide.Horizontal(4) == new DesignGuide(DesignGuideOrientation.Horizontal, 4));
    }
}
