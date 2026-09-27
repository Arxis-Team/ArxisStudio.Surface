using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface;
using ArxisStudio.Surface.Editing;

namespace ArxisStudio.Tests;

/// <summary>
/// Групповое изменение размера через тот же путь, что и в приложении:
/// событиями от адорнера группы, а не вызовом операции напрямую.
/// </summary>
/// <remarks>
/// <c>GroupResizeOperationTests</c> проверяет арифметику на контролах вне дерева.
/// Здесь проверяется, что жест доходит до неё и что рамка после него совпадает
/// с тем, что произошло с target'ами.
/// <para>
/// Групповой адорнер — принадлежность выбора <b>контейнеров</b>: он и виден только
/// при <c>HasMultipleContainerSelection</c>. У группы вложенных target'ов свой
/// набор secondary-адорнеров, каждый со своими ручками, а общей рамки с ручками
/// там нет — поэтому и жеста, который ничего не делает, тоже нет.
/// </para>
/// </remarks>
public class GroupResizeGestureTests
{
    private static readonly Size ContainerSize = new(200, 150);

    private static EditorHarness CreateWithContainerSelection()
    {
        var harness = EditorHarness.Create(nodeCount: 2);
        harness.PlaceContainer(0, new Point(100, 100), ContainerSize);
        harness.PlaceContainer(1, new Point(400, 100), ContainerSize);

        // Ctrl+A — поддержанный способ набрать группу контейнеров, см. KeyboardTests.
        harness.Editor.Focus();
        harness.Window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        harness.RunLayout();

        Assert.True(harness.Editor.HasMultipleContainerSelection);
        return harness;
    }

    private static SelectionAdorner GroupAdorner(EditorHarness harness)
        => harness.Editor.GetVisualDescendants()
            .OfType<SelectionAdorner>()
            .Single(a => a.Name == "PART_GroupSelectionAdorner");

    private static void DragGroupHandle(EditorHarness harness, ResizeDirection direction, Vector delta)
    {
        var adorner = GroupAdorner(harness);

        adorner.RaiseEvent(new ResizeStartedEventArgs(default, direction, SelectionAdorner.ResizeStartedEvent));
        adorner.RaiseEvent(new ResizeDeltaEventArgs(delta, direction, SelectionAdorner.ResizeDeltaEvent));
        adorner.RaiseEvent(new VectorEventArgs { RoutedEvent = SelectionAdorner.ResizeCompletedEvent, Vector = delta });
        harness.RunLayout();
    }

    [AvaloniaFact]
    public void Group_Handle_Resizes_Every_Container()
    {
        var harness = CreateWithGroupSelection();
        var first = harness.Container(0);
        var second = harness.Container(1);

        var firstBefore = harness.Editor.GetTargetSize(first).Width;
        var secondBefore = harness.Editor.GetTargetSize(second).Width;

        DragGroupHandle(harness, ResizeDirection.Right, new Vector(-100, 0));

        // Жест обязан дойти до операции — иначе рамка живёт своей жизнью.
        Assert.True(harness.Editor.GetTargetSize(first).Width < firstBefore);
        Assert.True(harness.Editor.GetTargetSize(second).Width < secondBefore);
    }

    [AvaloniaFact]
    public void Frame_Matches_The_Containers_After_The_Gesture()
    {
        var harness = CreateWithGroupSelection();

        DragGroupHandle(harness, ResizeDirection.Right, new Vector(-100, 0));

        var union = BoundsOf(harness, 0).Union(BoundsOf(harness, 1));
        var frame = harness.Editor.SelectionBounds;

        Assert.Equal(union.Left, frame.Left, 1);
        Assert.Equal(union.Right, frame.Right, 1);
    }

    [AvaloniaFact]
    public void Nested_Group_Has_No_Group_Handles()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, new Point(100, 100), new Size(400, 300));

        SelectNested(harness, harness.Nested(0));
        SelectNested(harness, harness.Named(0, "Sibling"), additive: true);

        Assert.Equal(2, harness.Editor.SelectedTargetsCount);
        Assert.True(harness.Editor.HasMultipleNestedSelection);

        // Общей рамки с ручками у вложенной группы нет — каждый target
        // тянется своим адорнером. Ручек, которые ничего не делают, не появляется.
        Assert.False(harness.Editor.HasMultipleContainerSelection);
        Assert.False(GroupAdorner(harness).IsVisible);
    }

    private static Rect BoundsOf(EditorHarness harness, int index)
    {
        var container = harness.Container(index);
        return new Rect(harness.Editor.GetTargetPosition(container), harness.Editor.GetTargetSize(container));
    }

    private static EditorHarness CreateWithGroupSelection() => CreateWithContainerSelection();

    private static void SelectNested(EditorHarness harness, Control target, bool additive = false)
    {
        var modifiers = additive ? RawInputModifiers.Shift : RawInputModifiers.None;
        var centre = harness.CentreOf(target);
        harness.Window.MouseDown(centre, MouseButton.Left, modifiers);
        harness.Window.MouseUp(centre, MouseButton.Left, modifiers);
        harness.RunLayout();
    }

    /// <summary>
    /// Групповая рамка тоже считает дельту от указателя.
    /// </summary>
    /// <remarks>
    /// Половина правки, которую не держал ни один тест: её ветку в
    /// <c>GroupResizeOperation.Update</c> можно было удалить целиком, не уронив ничего.
    /// Здесь ручка не двигается вовсе, а указатель уезжает на 10, 20, 30, и рамка обязана
    /// следовать за ним, а не сложить накопленные дельты в 60.
    /// </remarks>
    [AvaloniaFact]
    public void The_Group_Frame_Follows_The_Pointer_When_The_Handle_Lags()
    {
        var harness = CreateWithContainerSelection();
        var width0 = harness.Editor.SelectionBounds.Width;

        var right = harness.Editor.SelectionBounds.Right;
        var y = harness.Editor.SelectionBounds.Y + 5;

        // Указатель на ручке — с этого снимка и начинается жест.
        harness.Window.MouseMove(new Point(right, y));

        var adorner = GroupAdorner(harness);
        adorner.RaiseEvent(new ResizeStartedEventArgs(default, ResizeDirection.Right, SelectionAdorner.ResizeStartedEvent));

        for (var i = 1; i <= 3; i++)
        {
            harness.Window.MouseMove(new Point(right + (i * 10), y));

            adorner.RaiseEvent(new ResizeDeltaEventArgs(
                new Vector(i * 10, 0),
                ResizeDirection.Right,
                SelectionAdorner.ResizeDeltaEvent));

            harness.RunLayout();
        }

        adorner.RaiseEvent(new VectorEventArgs { RoutedEvent = SelectionAdorner.ResizeCompletedEvent, Vector = new Vector(30, 0) });
        harness.RunLayout();

        Assert.Equal(width0 + 30, harness.Editor.SelectionBounds.Width, 1);
    }
}
