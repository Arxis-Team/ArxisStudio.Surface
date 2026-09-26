using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Порядок перекрытия design targets.
/// </summary>
/// <remarks>
/// Порядок нормализуется в пределах родительской панели: по умолчанию у всех
/// соседей <c>ZIndex</c> равен нулю, и без нормализации перестановка на одну
/// позицию была бы невыполнима.
/// </remarks>
public class ZOrderTests
{
    private static readonly Point ContainerLocation = new(100, 100);
    private static readonly Size ContainerSize = new(200, 150);

    private static Point NestedCentre => new(
        ContainerLocation.X + EditorHarness.NestedOffset + (EditorHarness.NestedWidth / 2),
        ContainerLocation.Y + EditorHarness.NestedOffset + (EditorHarness.NestedHeight / 2));

    private static Point SiblingCentre => new(
        ContainerLocation.X + EditorHarness.SiblingOffset + (EditorHarness.NestedWidth / 2),
        ContainerLocation.Y + EditorHarness.NestedOffset + (EditorHarness.NestedHeight / 2));

    private static EditorHarness CreateWithNestedSelected()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);

        harness.Window.MouseDown(NestedCentre, MouseButton.Left);
        harness.Window.MouseUp(NestedCentre, MouseButton.Left);
        harness.RunLayout();

        return harness;
    }

    /// <summary>Видимый порядок соседей: сначала ZIndex, при равенстве — порядок в панели.</summary>
    private static List<Control> VisualOrder(Control anyChild)
    {
        var parent = anyChild.GetVisualParent()!;
        var siblings = parent.GetVisualChildren().OfType<Control>().ToList();

        return siblings
            .Select((control, index) => (control, index))
            .OrderBy(x => x.control.ZIndex)
            .ThenBy(x => x.index)
            .Select(x => x.control)
            .ToList();
    }

    [AvaloniaFact]
    public void Reorder_Without_Selection_Does_Nothing()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);

        Assert.False(harness.Editor.BringToFront());
        Assert.False(harness.Editor.SendToBack());
    }

    [AvaloniaFact]
    public void BringToFront_Puts_Target_Last_In_Visual_Order()
    {
        var harness = CreateWithNestedSelected();
        var nested = harness.Nested(0);

        Assert.True(harness.Editor.BringToFront());
        harness.RunLayout();

        Assert.Same(nested, VisualOrder(nested).Last());
    }

    [AvaloniaFact]
    public void SendToBack_Puts_Target_First_In_Visual_Order()
    {
        var harness = CreateWithNestedSelected();
        var nested = harness.Nested(0);

        Assert.True(harness.Editor.SendToBack());
        harness.RunLayout();

        Assert.Same(nested, VisualOrder(nested).First());
    }

    [AvaloniaFact]
    public void Forward_And_Backward_Move_By_One_Position()
    {
        var harness = CreateWithNestedSelected();
        var nested = harness.Nested(0);

        harness.Editor.SendToBack();
        harness.RunLayout();
        Assert.Equal(0, VisualOrder(nested).IndexOf(nested));

        harness.Editor.BringForward();
        harness.RunLayout();
        Assert.Equal(1, VisualOrder(nested).IndexOf(nested));

        harness.Editor.SendBackward();
        harness.RunLayout();
        Assert.Equal(0, VisualOrder(nested).IndexOf(nested));
    }

    [AvaloniaFact]
    public void Forward_At_The_Top_Changes_Nothing()
    {
        var harness = CreateWithNestedSelected();
        var nested = harness.Nested(0);

        harness.Editor.BringToFront();
        harness.RunLayout();
        var top = VisualOrder(nested).IndexOf(nested);

        harness.Editor.BringForward();
        harness.RunLayout();

        Assert.Equal(top, VisualOrder(nested).IndexOf(nested));
    }

    [AvaloniaFact]
    public void Reorder_Produces_An_Edit_Of_Kind_Reorder()
    {
        var harness = CreateWithNestedSelected();
        var edits = new List<DesignEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);

        harness.Editor.BringToFront();
        harness.RunLayout();

        var edit = Assert.Single(edits);
        Assert.Equal(DesignEditKind.Order, edit.Kind);
        Assert.All(edit.Changes, c => Assert.IsType<DesignOrderChange>(c));
    }

    [AvaloniaFact]
    public void Repeated_Reorder_Produces_No_Edit()
    {
        var harness = CreateWithNestedSelected();

        harness.Editor.BringToFront();
        harness.RunLayout();

        var edits = new List<DesignEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);

        // Порядок уже такой — записи в стек быть не должно.
        harness.Editor.BringToFront();
        harness.RunLayout();

        Assert.Empty(edits);
    }

    [AvaloniaFact]
    public void Revert_Restores_The_Previous_Order()
    {
        var harness = CreateWithNestedSelected();
        var nested = harness.Nested(0);

        var before = VisualOrder(nested).Select(c => c.ZIndex).ToList();

        DesignEditCompletedEventArgs? edit = null;
        harness.Editor.EditCompleted += (_, e) => edit = e;

        harness.Editor.BringToFront();
        harness.RunLayout();
        Assert.NotNull(edit);

        foreach (var change in edit!.Changes)
            harness.Editor.Revert(change);
        harness.RunLayout();

        Assert.Equal(before, VisualOrder(nested).Select(c => c.ZIndex).ToList());
    }

    [AvaloniaFact]
    public void Revert_Does_Not_Produce_A_New_Edit()
    {
        var harness = CreateWithNestedSelected();

        DesignEditCompletedEventArgs? edit = null;
        harness.Editor.EditCompleted += (_, e) => edit = e;

        harness.Editor.BringToFront();
        harness.RunLayout();

        var later = new List<DesignEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => later.Add(e);

        foreach (var change in edit!.Changes)
            harness.Editor.Revert(change);
        harness.RunLayout();

        Assert.Empty(later);
    }

    /// <summary>
    /// Повтор возвращает порядок, который задала команда.
    /// </summary>
    /// <remarks>
    /// Изменение порядка едет отдельным типом (<c>DesignOrderChange</c>) и своей парой
    /// значений, поэтому обе половины контракта надо проверять и на нём: отмена геометрии
    /// ничего не говорит про <c>ZIndex</c>.
    /// </remarks>
    [AvaloniaFact]
    public void Reapply_Restores_The_New_Order()
    {
        var harness = CreateWithNestedSelected();
        var nested = harness.Nested(0);

        DesignEditCompletedEventArgs? edit = null;
        harness.Editor.EditCompleted += (_, e) => edit = e;

        harness.Editor.BringToFront();
        harness.RunLayout();
        var afterCommand = VisualOrder(nested).Select(c => c.ZIndex).ToList();

        foreach (var change in edit!.Changes)
            harness.Editor.Revert(change);
        harness.RunLayout();
        Assert.NotEqual(afterCommand, VisualOrder(nested).Select(c => c.ZIndex).ToList());

        foreach (var change in edit.Changes)
            harness.Editor.Reapply(change);
        harness.RunLayout();

        Assert.Equal(afterCommand, VisualOrder(nested).Select(c => c.ZIndex).ToList());
    }

    [AvaloniaFact]
    public void Group_Reorder_Keeps_Relative_Order()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);

        harness.Window.MouseDown(NestedCentre, MouseButton.Left);
        harness.Window.MouseUp(NestedCentre, MouseButton.Left);
        harness.Window.MouseDown(SiblingCentre, MouseButton.Left, RawInputModifiers.Shift);
        harness.Window.MouseUp(SiblingCentre, MouseButton.Left, RawInputModifiers.Shift);
        harness.RunLayout();
        Assert.Equal(2, harness.Editor.SelectedDesignTargetsCount);

        var nested = harness.Nested(0);
        var sibling = harness.Named(0, "Sibling");

        harness.Editor.SendToBack();
        harness.RunLayout();

        var order = VisualOrder(nested);
        Assert.True(order.IndexOf(nested) < order.IndexOf(sibling));
        Assert.True(order.IndexOf(sibling) < 2);
    }

    [AvaloniaFact]
    public void Selection_No_Longer_Overrides_Explicit_Order()
    {
        var harness = CreateWithNestedSelected();
        var nested = harness.Nested(0);

        harness.Editor.SendToBack();
        harness.RunLayout();
        var sentBack = nested.ZIndex;

        // Раньше тема поднимала выбранный элемент на ZIndex = 99,
        // молча перебивая заданный порядок.
        Assert.True(nested.ZIndex < 99);
        Assert.Equal(sentBack, nested.ZIndex);
    }
}
