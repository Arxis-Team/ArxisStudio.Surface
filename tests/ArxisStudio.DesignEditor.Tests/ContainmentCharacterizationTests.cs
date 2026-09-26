using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ArxisStudio.States;
using Xunit;
using ArxisStudio.Surface;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Tests;

/// <summary>
/// Что происходит, когда вложенный контрол растягивают за пределы карточки.
/// </summary>
/// <remarks>
/// Фиксирует сегодняшнее поведение; ограничение по контейнеру перевернёт
/// последнее утверждение.
/// <para>
/// Замер снял главный вопрос по видео <c>Resize_BUG</c>: рамка не врёт.
/// <c>Bounds</c>, запрошенный <c>Height</c> и <c>SelectionBounds</c> совпадают
/// до единицы — расходится не геометрия, а отрисовка: карточка обрезает контрол,
/// а рамка честно показывает его настоящий размер. Поэтому чинить надо не рамку,
/// а отсутствие верхней границы у resize.
/// </para>
/// </remarks>
public class ContainmentCharacterizationTests
{
    private static readonly Point CardLocation = new(100, 100);
    private static readonly Size CardSize = new(340, 400);

    private const double Overshoot = 400;

    private static (EditorHarness Harness, Button Action) ResizeBeyondCard()
    {
        var harness = EditorHarness.CreateStackHosted();
        var container = harness.PlaceContainer(0, CardLocation, CardSize);
        var action = harness.Find<Button>(0, "Action");

        var centre = harness.CentreOf(action);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseUp(centre, MouseButton.Left);
        harness.RunLayout();

        var state = new ItemResizingState(container, action, ResizeDirection.Bottom);
        container.PushState(state);
        state.OnResizeDelta(new ResizeDeltaEventArgs(
            new Vector(0, Overshoot), ResizeDirection.Bottom, DesignEditorItem.ResizeDeltaEvent));
        harness.RunLayout();

        return (harness, action);
    }

    [AvaloniaFact]
    public void Frame_Requested_And_Arranged_Sizes_All_Agree()
    {
        var harness = EditorHarness.CreateStackHosted();
        var container = harness.PlaceContainer(0, CardLocation, CardSize);
        var action = harness.Find<Button>(0, "Action");

        var centre = harness.CentreOf(action);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseUp(centre, MouseButton.Left);
        harness.RunLayout();

        var state = new ItemResizingState(container, action, ResizeDirection.Bottom);
        container.PushState(state);
        state.OnResizeDelta(new ResizeDeltaEventArgs(
            new Vector(0, Overshoot), ResizeDirection.Bottom, DesignEditorItem.ResizeDeltaEvent));
        harness.RunLayout();

        // Запрошенный и фактический размеры совпадают...
        Assert.Equal(action.Height, action.Bounds.Height, 1);

        // ...и рамка равна фактическому. Расхождения понятий размера нет —
        // именно поэтому чинить надо было не рамку.
        Assert.Equal(action.Bounds.Height, harness.Editor.SelectionBounds.Height, 1);
    }

    [AvaloniaFact]
    public void Resize_Stops_At_The_Card_Edge()
    {
        var (harness, _) = ResizeBeyondCard();

        var cardBottom = CardLocation.Y + CardSize.Height;

        // То, ради чего всё затевалось: контрол упирается в свою форму, и рамка
        // останавливается вместе с ним. Раньше он уходил на 139 единиц ниже,
        // форма его обрезала, а ручки оставались на пустом холсте.
        Assert.Equal(cardBottom, harness.Editor.SelectionBounds.Bottom, 1);
    }

    [AvaloniaFact]
    public void Containment_Can_Be_Disabled()
    {
        var harness = EditorHarness.CreateStackHosted();
        harness.Editor.InteractionOptions.IsResizeContainedToParent = false;

        var container = harness.PlaceContainer(0, CardLocation, CardSize);
        var action = harness.Find<Button>(0, "Action");

        var centre = harness.CentreOf(action);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseUp(centre, MouseButton.Left);
        harness.RunLayout();

        var state = new ItemResizingState(container, action, ResizeDirection.Bottom);
        container.PushState(state);
        state.OnResizeDelta(new ResizeDeltaEventArgs(
            new Vector(0, Overshoot), ResizeDirection.Bottom, DesignEditorItem.ResizeDeltaEvent));
        harness.RunLayout();

        // Намеренный overflow остаётся возможен — но по явному решению, а не по умолчанию.
        Assert.True(harness.Editor.SelectionBounds.Bottom > CardLocation.Y + CardSize.Height);
    }

    [AvaloniaFact]
    public void Group_Resize_Stops_At_The_Card_Edge()
    {
        var harness = EditorHarness.Create();
        var container = harness.PlaceContainer(0, new Point(100, 100), new Size(300, 200));
        var nested = harness.Nested(0);
        var sibling = harness.Named(0, "Sibling");

        var nestedBounds = new Rect(110, 110, EditorHarness.NestedWidth, EditorHarness.NestedHeight);
        var siblingBounds = new Rect(200, 110, EditorHarness.NestedWidth, EditorHarness.NestedHeight);
        var groupBounds = nestedBounds.Union(siblingBounds);

        var operation = new GroupResizeOperation(
            ResizeDirection.Right,
            groupBounds,
            new[]
            {
                new GroupResizeTarget(nested, nestedBounds),
                new GroupResizeTarget(sibling, siblingBounds)
            },
            minSize: 10);

        operation.Update(harness.Editor, new Vector(500, 0));
        harness.RunLayout();

        var right = harness.Editor.GetDesignPosition(sibling).X + harness.Editor.GetDesignSize(sibling).Width;

        // Ограничение применяется к рамке группы целиком, поэтому пропорции внутри
        // сохраняются, а крайний target останавливается ровно на границе формы.
        Assert.Equal(container.Location.X + 300, right, 1);
    }

    [AvaloniaFact]
    public void Top_Level_Container_Is_Not_Contained()
    {
        var harness = EditorHarness.CreateStackHosted();
        var container = harness.PlaceContainer(0, CardLocation, CardSize);

        // У формы верхнего уровня владельца нет: она лежит на бесконечном холсте,
        // и ограничивать её нечем.
        Assert.False(harness.Editor.TryGetContainmentBounds(container, out _));
    }

    [AvaloniaFact]
    public void MaxHeight_Clamps_The_Written_Size()
    {
        var harness = EditorHarness.CreateStackHosted();
        var container = harness.PlaceContainer(0, CardLocation, CardSize);
        var action = harness.Find<Button>(0, "Action");
        action.MaxHeight = 60;
        harness.RunLayout();

        var centre = harness.CentreOf(action);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseUp(centre, MouseButton.Left);
        harness.RunLayout();

        var state = new ItemResizingState(container, action, ResizeDirection.Bottom);
        container.PushState(state);
        state.OnResizeDelta(new ResizeDeltaEventArgs(
            new Vector(0, Overshoot), ResizeDirection.Bottom, DesignEditorItem.ResizeDeltaEvent));
        harness.RunLayout();

        // Редактор читает MaxHeight сам, поэтому запрошенное и фактическое совпадают.
        // Раньше он писал 429, раскладка выдавала 60, и дальше всё считалось от 429.
        Assert.Equal(60, action.Height, 1);
        Assert.Equal(60, action.Bounds.Height, 1);
    }
}
