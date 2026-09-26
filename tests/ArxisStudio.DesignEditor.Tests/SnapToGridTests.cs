using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using ArxisStudio.Controls;
using ArxisStudio.States;
using Xunit;
using ArxisStudio.Surface;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Tests;

/// <summary>
/// Привязка к сетке при перетаскивании и изменении размера.
/// </summary>
/// <remarks>
/// Контейнер намеренно стоит мимо узлов сетки: главное свойство привязки в том,
/// что она ставит на узел <b>результат</b>, а не округляет смещение. Округление
/// дельты сохранило бы исходный сдвиг, и элемент так и остался бы вне сетки.
/// </remarks>
public class SnapToGridTests
{
    /// <summary>Шаг сетки редактора по умолчанию.</summary>
    private const double Cell = 20;

    private static readonly Point OffGridLocation = new(103, 107);
    private static readonly Size ContainerSize = new(200, 150);

    private static Point NestedCentre => new(
        OffGridLocation.X + EditorHarness.NestedOffset + (EditorHarness.NestedWidth / 2),
        OffGridLocation.Y + EditorHarness.NestedOffset + (EditorHarness.NestedHeight / 2));

    private static Point SiblingCentre => new(
        OffGridLocation.X + EditorHarness.SiblingOffset + (EditorHarness.NestedWidth / 2),
        OffGridLocation.Y + EditorHarness.NestedOffset + (EditorHarness.NestedHeight / 2));

    /// <summary>Смещение указателя, при котором обе координаты округляются в разные стороны.</summary>
    private static readonly Vector DragDelta = new(34, 18);

    private static EditorHarness CreateWithNestedSelected(bool snapToGrid = true)
    {
        var harness = EditorHarness.Create(snapToGrid: snapToGrid);
        harness.PlaceContainer(0, OffGridLocation, ContainerSize);

        harness.Window.MouseDown(NestedCentre, MouseButton.Left);
        harness.Window.MouseUp(NestedCentre, MouseButton.Left);
        harness.RunLayout();

        return harness;
    }

    private static Point DesignPositionOf(EditorHarness harness, string name) => new(
        Layout.GetDesignX(harness.Named(0, name)),
        Layout.GetDesignY(harness.Named(0, name)));

    private static void Drag(EditorHarness harness, Vector delta, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        harness.Window.MouseDown(NestedCentre, MouseButton.Left, modifiers);
        // Первый сдвиг переводит контейнер в состояние перетаскивания.
        harness.Window.MouseMove(NestedCentre + new Vector(8, 5), modifiers);
        harness.Window.MouseMove(NestedCentre + delta, modifiers);
        harness.Window.MouseUp(NestedCentre + delta, MouseButton.Left, modifiers);
        harness.RunLayout();
    }

    [AvaloniaFact]
    public void Snapping_Is_Enabled_By_Default()
    {
        // Сетка перестаёт быть декорацией только вместе с привязкой,
        // поэтому по умолчанию она включена.
        var harness = EditorHarness.Create(snapToGrid: true);

        Assert.True(new DesignEditorInteractionOptions().IsSnapToGridEnabled);
        Assert.True(harness.Editor.ShouldSnap(KeyModifiers.None));
    }

    [AvaloniaFact]
    public void Step_Follows_The_Grid_Cell_Size()
    {
        var harness = EditorHarness.Create(snapToGrid: true);
        var grid = harness.Editor.GetVisualDescendants().OfType<DesignGrid>().Single();

        // Не задан — берётся у сетки: рисовать одну структуру, а привязывать
        // к другой редактор не должен.
        Assert.True(double.IsNaN(harness.Editor.InteractionOptions.SnapStep));
        Assert.Equal(Cell, harness.Editor.ResolveSnapStep(), 6);

        grid.CellSize = 25;
        Assert.Equal(25, harness.Editor.ResolveSnapStep(), 6);
    }

    [AvaloniaTheory]
    // Ровно посередине между узлами — всегда вверх, независимо от чётности узла.
    [InlineData(310, 320)]
    [InlineData(290, 300)]
    [InlineData(-310, -300)]
    [InlineData(-290, -280)]
    public void Midpoint_Always_Resolves_Upwards(double value, double expected)
    {
        // Math.Round округляет к чётному: 310 -> 320, но 290 -> 280.
        // На медленной протяжке край дёргался бы назад.
        var harness = EditorHarness.Create(snapToGrid: true);

        Assert.Equal(expected, harness.Editor.SnapCoordinate(value), 6);
    }

    [AvaloniaFact]
    public void Explicit_Step_Overrides_The_Grid()
    {
        var harness = EditorHarness.Create(snapToGrid: true);

        harness.Editor.InteractionOptions.SnapStep = 8;

        Assert.Equal(8, harness.Editor.ResolveSnapStep(), 6);
    }

    [AvaloniaFact]
    public void Drag_Puts_The_Result_On_A_Grid_Node()
    {
        var harness = CreateWithNestedSelected();
        var before = DesignPositionOf(harness, "Nested");

        Drag(harness, DragDelta);

        var after = DesignPositionOf(harness, "Nested");

        // 113+34 = 147 -> 140, 117+18 = 135 -> 140.
        Assert.Equal(140, after.X, 1);
        Assert.Equal(140, after.Y, 1);

        // Ключевое: сдвиг получился не тем, что запросил указатель.
        // Округли редактор дельту — элемент остался бы на 113+40 = 153.
        Assert.NotEqual(before.X + DragDelta.X, after.X, 1);
    }

    [AvaloniaFact]
    public void Bypass_Modifier_Restores_Exact_Movement()
    {
        var harness = CreateWithNestedSelected();
        var before = DesignPositionOf(harness, "Nested");

        Drag(harness, DragDelta, RawInputModifiers.Alt);

        var after = DesignPositionOf(harness, "Nested");

        Assert.Equal(KeyModifiers.Alt, new DesignEditorInputGestures().SnapBypassModifiers);
        Assert.Equal(before.X + DragDelta.X, after.X, 1);
        Assert.Equal(before.Y + DragDelta.Y, after.Y, 1);
    }

    [AvaloniaFact]
    public void Disabled_Snapping_Restores_Exact_Movement()
    {
        var harness = CreateWithNestedSelected(snapToGrid: false);
        var before = DesignPositionOf(harness, "Nested");

        Drag(harness, DragDelta);

        var after = DesignPositionOf(harness, "Nested");

        Assert.Equal(before.X + DragDelta.X, after.X, 1);
        Assert.Equal(before.Y + DragDelta.Y, after.Y, 1);
    }

    [AvaloniaFact]
    public void Group_Drag_Keeps_Relative_Offsets()
    {
        var harness = CreateWithNestedSelected();
        harness.Window.MouseDown(SiblingCentre, MouseButton.Left, RawInputModifiers.Shift);
        harness.Window.MouseUp(SiblingCentre, MouseButton.Left, RawInputModifiers.Shift);
        harness.RunLayout();
        Assert.Equal(2, harness.Editor.SelectedDesignTargetsCount);

        var nestedBefore = DesignPositionOf(harness, "Nested");
        var siblingBefore = DesignPositionOf(harness, "Sibling");

        Drag(harness, DragDelta);

        var nestedAfter = DesignPositionOf(harness, "Nested");
        var siblingAfter = DesignPositionOf(harness, "Sibling");

        // По группе идёт уже привязанное смещение рамки, поэтому взаимное
        // расположение сохраняется. Привязывай редактор каждого по отдельности —
        // соседи сошлись бы к общим узлам и группа «слиплась» бы.
        //
        // Здесь Nested — левый верхний угол рамки, поэтому её узел и его узел
        // совпадают; случай, где они расходятся, закреплён в GroupSnapTests.
        Assert.Equal(140, nestedAfter.X, 1);
        Assert.Equal(siblingBefore.X - nestedBefore.X, siblingAfter.X - nestedAfter.X, 1);
        Assert.Equal(siblingBefore.Y - nestedBefore.Y, siblingAfter.Y - nestedAfter.Y, 1);
    }

    [AvaloniaFact]
    public void Nudge_Ignores_Snapping()
    {
        // Привязка исправляет неточный ввод указателем. Стрелка задаёт смещение
        // точно, и подтягивать её к узлу — значит отнять единственный способ
        // поставить элемент мимо сетки.
        var harness = CreateWithNestedSelected();
        var before = DesignPositionOf(harness, "Nested");

        harness.Window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        harness.RunLayout();

        var after = DesignPositionOf(harness, "Nested");

        Assert.Equal(before.X + 1, after.X, 1);
    }

    private static (EditorHarness Harness, ItemResizingState State) BeginResize(ResizeDirection direction)
    {
        var harness = EditorHarness.Create(snapToGrid: true);
        var container = harness.PlaceContainer(0, OffGridLocation, ContainerSize);

        var state = new ItemResizingState(container, container, direction);
        container.PushState(state);

        return (harness, state);
    }

    [AvaloniaFact]
    public void Resize_Snaps_The_Dragged_Edge()
    {
        var (harness, state) = BeginResize(ResizeDirection.Right);
        var container = harness.Container(0);

        // Правый край 303 + 4 = 307 -> 300.
        state.OnResizeDelta(new ResizeDeltaEventArgs(new Vector(4, 0), ResizeDirection.Right, DesignEditorItem.ResizeDeltaEvent));
        harness.RunLayout();

        Assert.Equal(300, container.Location.X + container.Width, 1);

        // Противоположный край не двигается: привязывается только та сторона,
        // за которую тянут.
        Assert.Equal(OffGridLocation.X, container.Location.X, 1);
    }

    [AvaloniaFact]
    public void Resize_From_The_Left_Keeps_The_Opposite_Edge_Fixed()
    {
        var (harness, state) = BeginResize(ResizeDirection.Left);
        var container = harness.Container(0);
        var initialRight = OffGridLocation.X + ContainerSize.Width;

        // Левый край 103 - 7 = 96 -> 100.
        state.OnResizeDelta(new ResizeDeltaEventArgs(new Vector(-7, 0), ResizeDirection.Left, DesignEditorItem.ResizeDeltaEvent));
        harness.RunLayout();

        Assert.Equal(100, container.Location.X, 1);
        Assert.Equal(initialRight, container.Location.X + container.Width, 1);
    }

    [AvaloniaFact]
    public void Group_Resize_Snaps_The_Frame_Not_Each_Target()
    {
        // Здесь редактор без шаблона, поэтому шаг задаётся явно.
        var editor = new DesignEditor();
        editor.InteractionOptions.SnapStep = Cell;

        var left = new Avalonia.Controls.Border { Width = 100, Height = 20 };
        var right = new Avalonia.Controls.Border { Width = 100, Height = 20 };

        var operation = new GroupResizeOperation(
            ResizeDirection.Right,
            new Rect(0, 0, 200, 20),
            new[]
            {
                new GroupResizeTarget(left, new Rect(0, 0, 100, 20)),
                new GroupResizeTarget(right, new Rect(100, 0, 100, 20))
            },
            minSize: 10);

        // Ширина 200 - 93 = 107 -> рамка привязывается к 100, масштаб 0.5.
        operation.Update(editor, new Vector(-93, 0));

        // Каждый target стал 50 — не кратно 20. Привязывай операция каждого
        // по отдельности, пропорции внутри группы разъехались бы.
        Assert.Equal(new Rect(0, 0, 50, 20), new Rect(editor.GetDesignPosition(left), editor.GetDesignSize(left)));
        Assert.Equal(new Rect(50, 0, 50, 20), new Rect(editor.GetDesignPosition(right), editor.GetDesignSize(right)));
    }

    [AvaloniaFact]
    public void No_Grid_Means_No_Snapping()
    {
        // Редактор без шаблона: привязываться не к чему, и включённый флаг
        // не должен молча округлять до целых.
        var editor = new DesignEditor();

        Assert.True(editor.InteractionOptions.IsSnapToGridEnabled);
        Assert.Equal(0, editor.ResolveSnapStep(), 6);
        Assert.False(editor.ShouldSnap(KeyModifiers.None));
    }
}
