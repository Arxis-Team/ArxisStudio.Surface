using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Смысл перетаскивания, начатого на пустой области контейнера.
/// </summary>
/// <remarks>
/// Политика задаётся через <see cref="DesignEditorInputGestures.ContainerEmptyAreaDrag"/>.
/// По умолчанию пустая область — фон для рамки выделения, как в form designer'ах;
/// перемещение контейнера остаётся доступным через модификатор и через
/// перетаскивание уже выбранного контейнера.
/// </remarks>
public class MarqueeGestureTests
{
    private static readonly Point ContainerLocation = new(100, 100);
    private static readonly Size ContainerSize = new(200, 150);

    /// <summary>Пустая область контейнера: правый нижний угол, ниже обоих контролов.</summary>
    private static Point EmptyArea => new(
        ContainerLocation.X + ContainerSize.Width - 20,
        ContainerLocation.Y + ContainerSize.Height - 20);

    /// <summary>Точка выше и левее обоих контролов.</summary>
    private static Point AboveBoth => new(ContainerLocation.X + 5, ContainerLocation.Y + 5);

    private static EditorHarness CreatePlaced()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);
        return harness;
    }

    private static void Drag(EditorHarness harness, Point from, Point to, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        harness.Window.MouseDown(from, MouseButton.Left, modifiers);
        harness.Window.MouseMove(from + ((to - from) * 0.5), modifiers);
        harness.Window.MouseMove(to, modifiers);
        harness.Window.MouseUp(to, MouseButton.Left, modifiers);
        harness.RunLayout();
    }

    [AvaloniaFact]
    public void Default_Gesture_Is_Marquee()
    {
        var harness = EditorHarness.Create();

        Assert.Equal(
            ContainerEmptyAreaDragGesture.Marquee,
            harness.Editor.InputGestures.ContainerEmptyAreaDrag);
    }

    /// <summary>
    /// Рамка, начатая на пустом холсте, набирает формы, а не их содержимое.
    /// </summary>
    /// <remarks>
    /// Снаружи контейнеров выбирать их содержимое не за что: пользователь видит формы
    /// и обводит формы. Модификатор для этого не нужен — он остаётся способом
    /// потребовать того же изнутри формы.
    /// </remarks>
    [AvaloniaFact]
    public void A_Marquee_From_Empty_Canvas_Collects_Containers()
    {
        var harness = CreatePlaced();

        // Рамка накрывает форму целиком: иначе внутри неё не окажется ни одного
        // вложенного контрола, сработает запасной путь «пустая рамка выбирает
        // контейнер», и тест пройдёт, ничего не проверив.
        Drag(harness, new Point(500, 400), new Point(50, 50));

        var target = Assert.Single(harness.Editor.SelectedDesignTargets);
        Assert.Equal(DesignSelectionScope.Container, target.Scope);
        Assert.Same(harness.Container(0), target.Target);
    }

    [AvaloniaFact]
    public void Drag_On_Empty_Area_Selects_Children_Instead_Of_Moving_Container()
    {
        var harness = CreatePlaced();
        var container = harness.Container(0);

        Drag(harness, EmptyArea, AboveBoth);

        var targets = harness.Editor.SelectedDesignTargets.Select(t => t.Target).ToList();

        Assert.Contains(harness.Nested(0), targets);
        Assert.Contains(harness.Named(0, "Sibling"), targets);

        // Контейнер остался на месте: жест ушёл рамке, а не перемещению.
        Assert.Equal(ContainerLocation, container.Location);
    }

    [AvaloniaFact]
    public void MoveContainer_Gesture_Restores_Dragging_By_Empty_Area()
    {
        var harness = CreatePlaced();
        harness.Editor.InputGestures.ContainerEmptyAreaDrag = ContainerEmptyAreaDragGesture.MoveContainer;
        var container = harness.Container(0);

        Drag(harness, EmptyArea, EmptyArea + new Vector(40, 25));

        Assert.NotEqual(ContainerLocation, container.Location);
    }

    [AvaloniaFact]
    public void Container_Interaction_Modifier_Still_Moves_The_Container()
    {
        var harness = CreatePlaced();
        var container = harness.Container(0);

        // Ctrl удерживает жест за контейнером даже при политике Marquee.
        Drag(harness, EmptyArea, EmptyArea + new Vector(40, 25), RawInputModifiers.Control);

        Assert.NotEqual(ContainerLocation, container.Location);
    }

    [AvaloniaFact]
    public void Already_Selected_Container_Is_Draggable_Without_Modifiers()
    {
        var harness = CreatePlaced();
        var container = harness.Container(0);

        // Клик по пустой области выбирает контейнер...
        harness.Window.MouseDown(EmptyArea, MouseButton.Left);
        harness.Window.MouseUp(EmptyArea, MouseButton.Left);
        harness.RunLayout();
        Assert.Same(container, harness.Editor.PrimarySelectionTarget!.Target);

        // ...после чего его можно тянуть без модификаторов.
        Drag(harness, EmptyArea, EmptyArea + new Vector(40, 25));

        Assert.NotEqual(ContainerLocation, container.Location);
    }

    [AvaloniaFact]
    public void Marquee_Scope_Is_Null_When_No_Marquee_Is_Active()
    {
        var harness = CreatePlaced();

        Assert.Null(harness.Editor.MarqueeScope);
    }

    [AvaloniaFact]
    public void Marquee_Scope_Follows_The_Rectangle_Not_The_Press_Point()
    {
        var harness = EditorHarness.Create(nodeCount: 2);
        harness.PlaceContainer(0, new Point(100, 100), new Size(200, 150));
        harness.PlaceContainer(1, new Point(400, 100), new Size(200, 150));

        // Старт в пустой области первого контейнера.
        var from = new Point(290, 240);
        harness.Window.MouseDown(from, MouseButton.Left);
        harness.Window.MouseMove(new Point(250, 200));
        harness.RunLayout();

        // Рамка целиком внутри первого — он и есть область действия.
        Assert.Same(harness.Container(0), harness.Editor.MarqueeScope);

        // Протяжка через холст ко второму контейнеру: рамка больше не помещается
        // в первый, а перекрывает преимущественно второй.
        harness.Window.MouseMove(new Point(600, 110));
        harness.RunLayout();

        Assert.Same(harness.Container(1), harness.Editor.MarqueeScope);

        harness.Window.MouseUp(new Point(600, 110), MouseButton.Left);
        harness.RunLayout();

        // После отпускания область действия сбрасывается.
        Assert.Null(harness.Editor.MarqueeScope);
    }

    [AvaloniaFact]
    public void Commit_Follows_The_Final_Rectangle_Not_The_Press_Point()
    {
        var harness = EditorHarness.Create(nodeCount: 2);
        harness.PlaceContainer(0, new Point(100, 100), new Size(200, 150));
        harness.PlaceContainer(1, new Point(400, 100), new Size(200, 150));

        // Нажатие в первом контейнере, протяжка на второй.
        harness.Window.MouseDown(new Point(290, 240), MouseButton.Left);
        harness.Window.MouseMove(new Point(450, 180));
        harness.Window.MouseMove(new Point(600, 105));
        harness.Window.MouseUp(new Point(600, 105), MouseButton.Left);
        harness.RunLayout();

        var targets = harness.Editor.SelectedDesignTargets.Select(t => t.Target).ToList();

        // Выбраны дети второго контейнера — того, что накрыт рамкой,
        // а не того, где произошло нажатие.
        Assert.Contains(harness.Nested(1), targets);
        Assert.DoesNotContain(harness.Nested(0), targets);
    }

    [AvaloniaFact]
    public void Container_Level_Marquee_Has_No_Scope()
    {
        var harness = CreatePlaced();

        harness.Window.MouseDown(new Point(600, 500), MouseButton.Left, RawInputModifiers.Control);
        harness.Window.MouseMove(new Point(150, 150), RawInputModifiers.Control);
        harness.RunLayout();

        // Рамка на уровне контейнеров не ограничена ни одним из них.
        Assert.Null(harness.Editor.MarqueeScope);

        harness.Window.MouseUp(new Point(150, 150), MouseButton.Left, RawInputModifiers.Control);
    }

    [AvaloniaFact]
    public void Click_Without_Drag_Still_Selects_The_Container()
    {
        var harness = CreatePlaced();
        var container = harness.Container(0);

        harness.Window.MouseDown(EmptyArea, MouseButton.Left);
        harness.Window.MouseUp(EmptyArea, MouseButton.Left);
        harness.RunLayout();

        // Пустая рамка трактуется как клик: контейнер выбран, а не «ничего».
        Assert.Same(container, harness.Editor.PrimarySelectionTarget!.Target);
        Assert.Equal(DesignSelectionScope.Container, harness.Editor.PrimarySelectionTarget.Scope);
        Assert.Equal(ContainerLocation, container.Location);
    }
}
