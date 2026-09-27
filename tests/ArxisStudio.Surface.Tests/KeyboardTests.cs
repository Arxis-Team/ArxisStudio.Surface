using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Surface.Editing;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Клавиатурное взаимодействие с редактором.
/// </summary>
public class KeyboardTests
{
    private static readonly Point ContainerLocation = new(100, 100);
    private static readonly Size ContainerSize = new(200, 150);

    private static Point NestedCentre => new(
        ContainerLocation.X + EditorHarness.NestedOffset + (EditorHarness.NestedWidth / 2),
        ContainerLocation.Y + EditorHarness.NestedOffset + (EditorHarness.NestedHeight / 2));

    private static EditorHarness CreateWithSelection()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);

        harness.Window.MouseDown(NestedCentre, MouseButton.Left);
        harness.Window.MouseUp(NestedCentre, MouseButton.Left);
        harness.RunLayout();

        return harness;
    }

    private static Point DesignPositionOf(EditorHarness harness) => new(
        Layout.GetDesignX(harness.Nested(0)),
        Layout.GetDesignY(harness.Nested(0)));

    [AvaloniaFact]
    public void Clicking_The_Editor_Gives_It_Keyboard_Focus()
    {
        // Без фокуса ни один клавиатурный жест не сработает.
        var harness = CreateWithSelection();

        Assert.True(harness.Editor.IsKeyboardFocusWithin);
    }

    [AvaloniaTheory]
    [InlineData(Key.Right, 1, 0)]
    [InlineData(Key.Left, -1, 0)]
    [InlineData(Key.Down, 0, 1)]
    [InlineData(Key.Up, 0, -1)]
    public void Arrow_Nudges_Selection_By_One_Step(Key key, double dx, double dy)
    {
        var harness = CreateWithSelection();
        var before = DesignPositionOf(harness);

        harness.Window.KeyPressQwerty(PhysicalKeyFromArrow(key), RawInputModifiers.None);
        harness.RunLayout();

        var after = DesignPositionOf(harness);
        Assert.Equal(before.X + dx, after.X, 1);
        Assert.Equal(before.Y + dy, after.Y, 1);
    }

    [AvaloniaFact]
    public void Modifier_Switches_To_The_Large_Step()
    {
        var harness = CreateWithSelection();
        var before = DesignPositionOf(harness);

        harness.Window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        harness.RunLayout();

        Assert.Equal(before.X + 10, DesignPositionOf(harness).X, 1);
    }

    [AvaloniaFact]
    public void Nudge_Produces_One_Edit_Per_Key_Press()
    {
        var harness = CreateWithSelection();
        var edits = new List<SurfaceEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);

        harness.Window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        harness.Window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        harness.RunLayout();

        // Смещение стрелкой попадает в стек отмены той же записью, что и перетаскивание.
        Assert.Equal(2, edits.Count);
        Assert.All(edits, e => Assert.Equal(SurfaceEditKind.Move, e.Kind));
    }

    [AvaloniaFact]
    public void Nudge_Respects_Move_Policy()
    {
        var harness = CreateWithSelection();
        SurfaceInteraction.SetMovePolicy(harness.Nested(0), MovePolicy.Y);
        var before = DesignPositionOf(harness);

        harness.Window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        harness.RunLayout();

        // Ось X запрещена политикой — смещения нет и записи в стек тоже.
        Assert.Equal(before.X, DesignPositionOf(harness).X, 1);
    }

    [AvaloniaFact]
    public void Locked_Target_Produces_No_Edit()
    {
        var harness = CreateWithSelection();
        SurfaceInteraction.SetMovePolicy(harness.Nested(0), MovePolicy.None);

        var edits = new List<SurfaceEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);

        harness.Window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        harness.RunLayout();

        Assert.Empty(edits);
    }

    [AvaloniaFact]
    public void Escape_Clears_Selection()
    {
        var harness = CreateWithSelection();
        Assert.True(harness.Editor.HasSingleSelection);

        harness.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        harness.RunLayout();

        Assert.Empty(harness.Editor.SelectedTargets);
    }

    [AvaloniaFact]
    public void Select_All_Selects_Every_Container()
    {
        var harness = EditorHarness.Create(nodeCount: 3);
        for (var i = 0; i < 3; i++)
            harness.PlaceContainer(i, new Point(100 + (i * 260), 100), ContainerSize);

        harness.Editor.Focus();
        harness.Window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        harness.RunLayout();

        Assert.Equal(3, harness.Editor.SelectedTargetsCount);
        Assert.True(harness.Editor.HasMultipleContainerSelection);
    }

    [AvaloniaFact]
    public void Delete_Raises_A_Request_Instead_Of_Deleting()
    {
        var harness = CreateWithSelection();
        SurfaceDeleteRequestedEventArgs? request = null;

        harness.Editor.DeleteRequested += (_, e) =>
        {
            request = e;
            e.Handled = true;
        };

        harness.Window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        harness.RunLayout();

        // Редактор не владеет коллекцией и удалить сам не может.
        Assert.NotNull(request);
        Assert.Single(request!.Targets);
        Assert.Same(harness.Nested(0), request.Targets[0].Target);
        Assert.Equal(1, harness.Editor.ItemCount);
    }

    [AvaloniaFact]
    public void Delete_Without_A_Handler_Is_Not_Swallowed()
    {
        var harness = CreateWithSelection();

        // Обработчика нет — нажатие должно остаться необработанным,
        // чтобы приложение могло повесить на Delete что-то своё выше по дереву.
        harness.Window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        harness.RunLayout();

        Assert.True(harness.Editor.HasSingleSelection);
    }

    private static PhysicalKey PhysicalKeyFromArrow(Key key) => key switch
    {
        Key.Left => PhysicalKey.ArrowLeft,
        Key.Right => PhysicalKey.ArrowRight,
        Key.Up => PhysicalKey.ArrowUp,
        Key.Down => PhysicalKey.ArrowDown,
        _ => PhysicalKey.None
    };
}
