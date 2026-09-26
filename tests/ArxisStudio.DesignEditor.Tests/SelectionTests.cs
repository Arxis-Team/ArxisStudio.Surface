using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Поведение выделения при клике.
/// </summary>
/// <remarks>
/// Тесты намеренно опираются только на публичное наблюдаемое состояние
/// (<see cref="DesignEditor.SelectedDesignTargets"/>, <see cref="DesignEditor.PrimarySelectionTarget"/>,
/// счётчики), а не на внутренние структуры выбора. Внутреннее представление
/// будет переписано при переходе к рекурсивной вложенности, ожидания — нет.
/// </remarks>
public class SelectionTests
{
    private static readonly Point ContainerLocation = new(100, 100);
    private static readonly Size ContainerSize = new(200, 150);

    /// <summary>Точка внутри вложенного Border в координатах редактора.</summary>
    private static Point NestedCentre => new(
        ContainerLocation.X + EditorHarness.NestedOffset + (EditorHarness.NestedWidth / 2),
        ContainerLocation.Y + EditorHarness.NestedOffset + (EditorHarness.NestedHeight / 2));

    private static void Click(EditorHarness harness, Point point)
    {
        harness.Window.MouseDown(point, MouseButton.Left);
        harness.Window.MouseUp(point, MouseButton.Left);
        harness.RunLayout();
    }

    [AvaloniaFact]
    public void Nothing_Is_Selected_Initially()
    {
        var harness = EditorHarness.Create();

        Assert.Empty(harness.Editor.SelectedDesignTargets);
        Assert.Null(harness.Editor.PrimarySelectionTarget);
        Assert.False(harness.Editor.HasSingleSelection);
        Assert.False(harness.Editor.HasMultipleSelection);
        Assert.Equal(default, harness.Editor.SelectionBounds);
    }

    [AvaloniaFact]
    public void Click_On_Nested_Control_Selects_It_As_Nested_Target()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);
        var nested = harness.Nested(0);

        Click(harness, NestedCentre);

        Assert.True(harness.Editor.HasSingleSelection);
        Assert.Equal(1, harness.Editor.SelectedDesignTargetsCount);

        var primary = harness.Editor.PrimarySelectionTarget;
        Assert.NotNull(primary);
        Assert.Same(nested, primary!.Target);
        Assert.Equal(DesignSelectionScope.NestedTarget, primary.Scope);
        Assert.Same(harness.Container(0), primary.Container);
    }

    [AvaloniaFact]
    public void Selection_Bounds_Match_Nested_Control_Geometry()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);

        Click(harness, NestedCentre);

        var expected = new Rect(
            ContainerLocation.X + EditorHarness.NestedOffset,
            ContainerLocation.Y + EditorHarness.NestedOffset,
            EditorHarness.NestedWidth,
            EditorHarness.NestedHeight);

        Assert.Equal(expected.X, harness.Editor.SelectionBounds.X, 1);
        Assert.Equal(expected.Y, harness.Editor.SelectionBounds.Y, 1);
        Assert.Equal(expected.Width, harness.Editor.SelectionBounds.Width, 1);
        Assert.Equal(expected.Height, harness.Editor.SelectionBounds.Height, 1);
    }

    [AvaloniaFact]
    public void Selection_Bounds_Follow_Geometry_Changes_Of_The_Target()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);

        Click(harness, NestedCentre);
        Assert.Equal(EditorHarness.NestedWidth, harness.Editor.SelectionBounds.Width, 1);

        // Изменение геометрии выбранного target'а обязано дойти до overlay.
        // Раньше это ловил глобальный class handler на BoundsProperty,
        // теперь — точечная подписка на сами targets.
        harness.Nested(0).Width = EditorHarness.NestedWidth + 40;
        harness.RunLayout();

        Assert.Equal(EditorHarness.NestedWidth + 40, harness.Editor.SelectionBounds.Width, 1);
    }

    [AvaloniaFact]
    public void Selection_Bounds_Follow_Design_Position_Changes_Of_The_Target()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);

        Click(harness, NestedCentre);
        var startX = harness.Editor.SelectionBounds.X;

        // Сдвиг через Layout.X идёт по тому же пути подписки, что и Bounds.
        ArxisStudio.Surface.UiDesigner.Layout.SetX(harness.Nested(0), EditorHarness.NestedOffset + 25);
        harness.RunLayout();

        Assert.Equal(startX + 25, harness.Editor.SelectionBounds.X, 1);
    }

    /// <summary>Точка внутри второго вложенного контрола.</summary>
    private static Point SiblingCentre => new(
        ContainerLocation.X + EditorHarness.SiblingOffset + (EditorHarness.NestedWidth / 2),
        ContainerLocation.Y + EditorHarness.NestedOffset + (EditorHarness.NestedHeight / 2));

    [AvaloniaFact]
    public void Additive_Click_Groups_Siblings_In_The_Same_Host()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);

        Click(harness, NestedCentre);
        harness.Window.MouseDown(SiblingCentre, MouseButton.Left, RawInputModifiers.Shift);
        harness.Window.MouseUp(SiblingCentre, MouseButton.Left, RawInputModifiers.Shift);
        harness.RunLayout();

        var targets = harness.Editor.SelectedDesignTargets.Select(t => t.Target).ToList();

        Assert.Equal(2, harness.Editor.SelectedDesignTargetsCount);
        Assert.Contains(harness.Nested(0), targets);
        Assert.Contains(harness.Named(0, "Sibling"), targets);
        Assert.True(harness.Editor.HasMultipleNestedSelection);
    }

    [AvaloniaFact]
    public void Repeated_Additive_Click_Removes_Target_From_The_Group()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);

        Click(harness, NestedCentre);
        harness.Window.MouseDown(SiblingCentre, MouseButton.Left, RawInputModifiers.Shift);
        harness.Window.MouseUp(SiblingCentre, MouseButton.Left, RawInputModifiers.Shift);
        harness.RunLayout();
        Assert.Equal(2, harness.Editor.SelectedDesignTargetsCount);

        harness.Window.MouseDown(SiblingCentre, MouseButton.Left, RawInputModifiers.Shift);
        harness.Window.MouseUp(SiblingCentre, MouseButton.Left, RawInputModifiers.Shift);
        harness.RunLayout();

        Assert.Equal(1, harness.Editor.SelectedDesignTargetsCount);
        Assert.Same(harness.Nested(0), harness.Editor.PrimarySelectionTarget!.Target);
    }

    [AvaloniaFact]
    public void Plain_Click_On_Grouped_Target_Makes_It_Primary_Without_Collapsing()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);

        Click(harness, NestedCentre);
        harness.Window.MouseDown(SiblingCentre, MouseButton.Left, RawInputModifiers.Shift);
        harness.Window.MouseUp(SiblingCentre, MouseButton.Left, RawInputModifiers.Shift);
        harness.RunLayout();

        // Обычный клик по уже выбранному участнику группы не схлопывает её.
        Click(harness, SiblingCentre);

        Assert.Equal(2, harness.Editor.SelectedDesignTargetsCount);
        Assert.Same(harness.Named(0, "Sibling"), harness.Editor.PrimarySelectionTarget!.Target);
    }

    [AvaloniaFact]
    public void Click_On_Empty_Surface_Clears_Selection()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);

        Click(harness, NestedCentre);
        Assert.True(harness.Editor.HasSingleSelection);

        // Точка заведомо вне контейнера.
        Click(harness, new Point(600, 500));

        Assert.Empty(harness.Editor.SelectedDesignTargets);
        Assert.Null(harness.Editor.PrimarySelectionTarget);
        Assert.False(harness.Editor.HasSingleSelection);
    }

    [AvaloniaFact]
    public void Click_On_Untracked_Area_Inside_Container_Falls_Back_To_Container()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);
        var container = harness.Container(0);

        // Правый нижний угол контейнера: внутри него, но вне вложенного Border
        // и без designer-метаданных под курсором.
        Click(harness, new Point(
            ContainerLocation.X + ContainerSize.Width - 10,
            ContainerLocation.Y + ContainerSize.Height - 10));

        var primary = harness.Editor.PrimarySelectionTarget;
        Assert.NotNull(primary);
        Assert.Same(container, primary!.Target);
        Assert.Equal(DesignSelectionScope.Container, primary.Scope);
    }
}
