using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface;
using ArxisStudio.Surface.Editing;

namespace ArxisStudio.Tests;

/// <summary>
/// <see cref="SurfaceView.IsInteracting"/>: жест держит указатель (ADR 0022).
/// </summary>
/// <remarks>
/// Хост откладывает по признаку то, что перестроило бы холст под рукой, и делает отложенное по его
/// уведомлению. Поэтому проверяется не только значение посреди жеста, но и что уведомлений ровно два —
/// начало и конец, — и что ни один путь выхода из жеста не оставляет признак поднятым.
/// </remarks>
public class InteractionFlagTests
{
    private static readonly Point EmptyCanvas = new(600, 420);

    private static EditorHarness Create(int nodeCount = 1)
    {
        var harness = EditorHarness.Create(nodeCount);
        harness.PlaceContainer(0, new Point(80, 80), new Size(200, 150));
        return harness;
    }

    /// <summary>Записывает каждое уведомление признака.</summary>
    private static List<bool> Record(SurfaceView editor)
    {
        var changes = new List<bool>();
        editor.PropertyChanged += (_, e) =>
        {
            if (e.Property == SurfaceView.IsInteractingProperty)
                changes.Add((bool)e.NewValue!);
        };

        return changes;
    }

    private static Thumb RightThumb(SelectionAdorner adorner) =>
        adorner.GetVisualDescendants().OfType<Thumb>().Single(thumb => thumb.Name == "PART_Right");

    private static Point CentreIn(EditorHarness harness, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), harness.Editor)!.Value;

    private static SelectionAdorner Adorner(EditorHarness harness, string name) =>
        harness.Editor.GetVisualDescendants().OfType<SelectionAdorner>().Single(adorner => adorner.Name == name);

    [AvaloniaFact]
    public void Drag_Sets_IsInteracting_Until_Release()
    {
        var harness = Create();
        var changes = Record(harness.Editor);
        var nested = harness.CentreOf(harness.Nested(0));

        harness.Window.MouseDown(nested, MouseButton.Left);

        // Нажатие ещё не жест: перетаскивание начинается за порогом.
        Assert.False(harness.Editor.IsInteracting, "Нажатие без движения не держит жест.");

        harness.Window.MouseMove(nested + new Vector(30, 0));
        Assert.True(harness.Editor.IsInteracting, "Перетаскивание за порогом — жест.");

        harness.Window.MouseMove(nested + new Vector(60, 0));
        harness.Window.MouseUp(nested + new Vector(60, 0), MouseButton.Left);

        Assert.False(harness.Editor.IsInteracting, "Отпускание кончает жест.");
        Assert.Equal(new[] { true, false }, changes);
    }

    [AvaloniaFact]
    public void Marquee_Holds_IsInteracting_From_Press_To_Release()
    {
        var harness = Create();
        var changes = Record(harness.Editor);

        harness.Window.MouseDown(EmptyCanvas, MouseButton.Left);
        Assert.True(harness.Editor.IsInteracting, "Рамка захватывает указатель на нажатии — жест с нажатия.");

        harness.Window.MouseMove(EmptyCanvas + new Vector(40, 30));
        harness.Window.MouseUp(EmptyCanvas + new Vector(40, 30), MouseButton.Left);

        Assert.False(harness.Editor.IsInteracting);
        Assert.Equal(new[] { true, false }, changes);
    }

    [AvaloniaFact]
    public void Panning_Holds_IsInteracting()
    {
        var harness = Create();

        harness.Window.MouseDown(EmptyCanvas, MouseButton.Middle);
        Assert.True(harness.Editor.IsInteracting, "Панорама — жест.");

        harness.Window.MouseMove(EmptyCanvas + new Vector(40, 30));
        harness.Window.MouseUp(EmptyCanvas + new Vector(40, 30), MouseButton.Middle);

        Assert.False(harness.Editor.IsInteracting);
    }

    [AvaloniaFact]
    public void Resizing_By_A_Handle_Holds_IsInteracting()
    {
        var harness = Create();
        var inside = harness.CentreOf(harness.Nested(0));

        // Форма целиком: её рамка — главный адорнер.
        harness.Window.MouseDown(inside, MouseButton.Left, RawInputModifiers.Control);
        harness.Window.MouseUp(inside, MouseButton.Left, RawInputModifiers.Control);
        harness.RunLayout();

        var handle = CentreIn(harness, RightThumb(Adorner(harness, "PART_SelectionAdorner")));
        harness.Window.MouseMove(handle);
        harness.Window.MouseDown(handle, MouseButton.Left);
        harness.Window.MouseMove(handle + new Vector(20, 0));

        Assert.True(harness.Editor.IsInteracting, "Изменение размера ручкой — жест, хотя захват у ручки, а не у контейнера.");

        harness.Window.MouseUp(handle + new Vector(20, 0), MouseButton.Left);

        Assert.False(harness.Editor.IsInteracting);
    }

    [AvaloniaFact]
    public void Group_Resize_Holds_IsInteracting()
    {
        var harness = Create(nodeCount: 2);
        harness.PlaceContainer(1, new Point(320, 80), new Size(200, 150));

        var first = harness.CentreOf(harness.Nested(0));
        var second = harness.CentreOf(harness.Nested(1));
        harness.Window.MouseDown(first, MouseButton.Left, RawInputModifiers.Control);
        harness.Window.MouseUp(first, MouseButton.Left, RawInputModifiers.Control);
        harness.Window.MouseDown(second, MouseButton.Left, RawInputModifiers.Control | RawInputModifiers.Shift);
        harness.Window.MouseUp(second, MouseButton.Left, RawInputModifiers.Control | RawInputModifiers.Shift);
        harness.RunLayout();

        Assert.True(harness.Editor.ShowsGroupFrame, "Две формы выбраны целиком — у них общая рамка.");

        // Групповой жест ведёт операция, а не состояние контейнера: признак обязан видеть и её.
        var handle = CentreIn(harness, RightThumb(Adorner(harness, "PART_GroupSelectionAdorner")));
        harness.Window.MouseMove(handle);
        harness.Window.MouseDown(handle, MouseButton.Left);
        harness.Window.MouseMove(handle + new Vector(20, 0));

        Assert.True(harness.Editor.IsInteracting, "Групповое изменение размера — жест.");

        harness.Window.MouseUp(handle + new Vector(20, 0), MouseButton.Left);

        Assert.False(harness.Editor.IsInteracting);
    }

    [AvaloniaFact]
    public void A_Pinch_Holds_IsInteracting_Until_It_Ends()
    {
        var harness = Create();
        var changes = Record(harness.Editor);

        // Касания headless не умеет: события щипка поднимаются руками, как в PinchZoomTests.
        harness.Editor.RaiseEvent(new PinchEventArgs(1.2, EmptyCanvas) { RoutedEvent = InputElement.PinchEvent });
        harness.Editor.RaiseEvent(new PinchEventArgs(1.4, EmptyCanvas) { RoutedEvent = InputElement.PinchEvent });
        Assert.True(harness.Editor.IsInteracting, "Щипок — жест.");

        harness.Editor.RaiseEvent(new PinchEndedEventArgs { RoutedEvent = InputElement.PinchEndedEvent });

        Assert.False(harness.Editor.IsInteracting);
        Assert.Equal(new[] { true, false }, changes);
    }

    [AvaloniaFact]
    public void Lost_Capture_Clears_IsInteracting()
    {
        var harness = Create();
        var nested = harness.CentreOf(harness.Nested(0));

        IPointer? pointer = null;
        harness.Editor.AddHandler(
            InputElement.PointerPressedEvent,
            (object? _, PointerPressedEventArgs e) => pointer ??= e.Pointer,
            handledEventsToo: true);

        harness.Window.MouseDown(nested, MouseButton.Left);
        harness.Window.MouseMove(nested + new Vector(30, 0));
        Assert.True(harness.Editor.IsInteracting);

        // Захват забрали — платформа, другой элемент: отпускания не будет.
        pointer!.Capture(null);

        Assert.False(harness.Editor.IsInteracting, "Брошенный жест не держит признак.");
    }

    [AvaloniaFact]
    public void A_Container_Leaving_The_Tree_Mid_Resize_Does_Not_Leave_IsInteracting_Raised()
    {
        var harness = Create();
        var inside = harness.CentreOf(harness.Nested(0));

        harness.Window.MouseDown(inside, MouseButton.Left, RawInputModifiers.Control);
        harness.Window.MouseUp(inside, MouseButton.Left, RawInputModifiers.Control);
        harness.RunLayout();

        // Захват у ручки рамки, состояние — у контейнера: уход контейнера захват не отнимает.
        var handle = CentreIn(harness, RightThumb(Adorner(harness, "PART_SelectionAdorner")));
        harness.Window.MouseMove(handle);
        harness.Window.MouseDown(handle, MouseButton.Left);
        harness.Window.MouseMove(handle + new Vector(20, 0));
        Assert.True(harness.Editor.IsInteracting);

        // Хост сменил то, что лежит на холсте, — так уходит форма при замене сборки.
        harness.Editor.ItemsSource = Array.Empty<TestNode>();
        harness.RunLayout();

        Assert.False(harness.Editor.IsInteracting, "Отметка контейнера, ушедшего из дерева, не держит признак.");
    }
}
