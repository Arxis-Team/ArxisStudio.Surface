using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Событие изменения выделения.
/// </summary>
/// <remarks>
/// Главное требование — не гранулярность данных, а отсутствие ложных срабатываний:
/// внутренний снимок пересобирается на каждом кадре перетаскивания, и наивная
/// реализация уведомляла бы инспектор свойств десятки раз за один жест.
/// </remarks>
public class SelectionEventTests
{
    private static readonly Point ContainerLocation = new(100, 100);
    private static readonly Size ContainerSize = new(200, 150);

    private static Point NestedCentre => new(
        ContainerLocation.X + EditorHarness.NestedOffset + (EditorHarness.NestedWidth / 2),
        ContainerLocation.Y + EditorHarness.NestedOffset + (EditorHarness.NestedHeight / 2));

    private static Point SiblingCentre => new(
        ContainerLocation.X + EditorHarness.SiblingOffset + (EditorHarness.NestedWidth / 2),
        ContainerLocation.Y + EditorHarness.NestedOffset + (EditorHarness.NestedHeight / 2));

    private static (EditorHarness Harness, List<DesignSelectionChangedEventArgs> Events) Create()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);

        var events = new List<DesignSelectionChangedEventArgs>();
        harness.Editor.DesignSelectionChanged += (_, e) => events.Add(e);
        return (harness, events);
    }

    private static void Click(EditorHarness harness, Point point, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        harness.Window.MouseDown(point, MouseButton.Left, modifiers);
        harness.Window.MouseUp(point, MouseButton.Left, modifiers);
        harness.RunLayout();
    }

    [AvaloniaFact]
    public void Selecting_A_Target_Raises_The_Event()
    {
        var (harness, events) = Create();

        Click(harness, NestedCentre);

        var e = Assert.Single(events);
        Assert.Empty(e.OldTargets);
        Assert.Same(harness.Nested(0), Assert.Single(e.NewTargets).Target);
        Assert.Same(harness.Nested(0), Assert.Single(e.Added).Target);
        Assert.Empty(e.Removed);
        Assert.Null(e.OldPrimary);
        Assert.NotNull(e.NewPrimary);
        Assert.True(e.IsPrimaryChanged);
    }

    [AvaloniaFact]
    public void Dragging_Does_Not_Raise_The_Event()
    {
        var (harness, events) = Create();

        Click(harness, NestedCentre);
        events.Clear();

        harness.Window.MouseDown(NestedCentre, MouseButton.Left);
        harness.Window.MouseMove(NestedCentre + new Vector(10, 6));
        harness.Window.MouseMove(NestedCentre + new Vector(40, 30));
        harness.Window.MouseMove(NestedCentre + new Vector(70, 50));
        harness.Window.MouseUp(NestedCentre + new Vector(70, 50), MouseButton.Left);
        harness.RunLayout();

        // Геометрия изменилась, выделение — нет.
        Assert.Empty(events);
    }

    [AvaloniaFact]
    public void Additive_Click_Reports_Only_The_Added_Target()
    {
        var (harness, events) = Create();

        Click(harness, NestedCentre);
        events.Clear();

        Click(harness, SiblingCentre, RawInputModifiers.Shift);

        var e = Assert.Single(events);
        Assert.Equal(2, e.NewTargets.Count);
        Assert.Same(harness.Named(0, "Sibling"), Assert.Single(e.Added).Target);
        Assert.Empty(e.Removed);
    }

    [AvaloniaFact]
    public void Removing_From_The_Group_Reports_It_As_Removed()
    {
        var (harness, events) = Create();

        Click(harness, NestedCentre);
        Click(harness, SiblingCentre, RawInputModifiers.Shift);
        events.Clear();

        Click(harness, SiblingCentre, RawInputModifiers.Shift);

        var e = Assert.Single(events);
        Assert.Same(harness.Named(0, "Sibling"), Assert.Single(e.Removed).Target);
        Assert.Empty(e.Added);
    }

    [AvaloniaFact]
    public void Promoting_A_Group_Member_Reports_A_Primary_Change()
    {
        var (harness, events) = Create();

        Click(harness, NestedCentre);
        Click(harness, SiblingCentre, RawInputModifiers.Shift);
        events.Clear();

        // Обычный клик по участнику группы не меняет состав, но делает его primary.
        Click(harness, SiblingCentre);

        var e = Assert.Single(events);
        Assert.Empty(e.Added);
        Assert.Empty(e.Removed);
        Assert.True(e.IsPrimaryChanged);
        Assert.Same(harness.Named(0, "Sibling"), e.NewPrimary!.Target);
    }

    [AvaloniaFact]
    public void Clearing_Selection_Reports_Everything_As_Removed()
    {
        var (harness, events) = Create();

        Click(harness, NestedCentre);
        events.Clear();

        Click(harness, new Point(600, 500));

        var e = Assert.Single(events);
        Assert.Empty(e.NewTargets);
        Assert.Same(harness.Nested(0), Assert.Single(e.Removed).Target);
        Assert.Null(e.NewPrimary);
    }

    [AvaloniaFact]
    public void Repeated_Click_On_The_Same_Target_Raises_Nothing()
    {
        var (harness, events) = Create();

        Click(harness, NestedCentre);
        events.Clear();

        Click(harness, NestedCentre);

        // Набор не изменился и primary тот же — уведомлять не о чем.
        Assert.Empty(events);
    }

    [AvaloniaFact]
    public void Nudging_Does_Not_Raise_The_Event()
    {
        var (harness, events) = Create();

        Click(harness, NestedCentre);
        events.Clear();

        harness.Window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        harness.Window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        harness.RunLayout();

        Assert.Empty(events);
    }

    [AvaloniaFact]
    public void Selected_Targets_Property_Is_Stable_While_Dragging()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);

        Click(harness, NestedCentre);
        var snapshot = harness.Editor.SelectedDesignTargets;

        harness.Window.MouseDown(NestedCentre, MouseButton.Left);
        harness.Window.MouseMove(NestedCentre + new Vector(10, 6));
        harness.Window.MouseMove(NestedCentre + new Vector(50, 40));
        harness.Window.MouseUp(NestedCentre + new Vector(50, 40), MouseButton.Left);
        harness.RunLayout();

        // Ссылка та же: привязки к SelectedDesignTargets не пересчитываются
        // на каждом кадре жеста.
        Assert.Same(snapshot, harness.Editor.SelectedDesignTargets);
    }
}
