using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Масштабирование щипком: тачпад и пальцы, и общее для них правило неподвижной точки.
/// </summary>
/// <remarks>
/// Настоящего касания headless не умеет, поэтому события щипка поднимаются здесь руками —
/// теми же аргументами, что даёт платформа: тачпад кладёт в <c>Delta</c> приращение за
/// событие, распознаватель пальцев — масштаб, накопленный от начала жеста, и точку между
/// пальцами в координатах редактора. Что так их и дают, сверено с исходниками Avalonia 12.
/// </remarks>
public class PinchZoomTests
{
    // Число знаков, а не допуск: с double здесь выбралась бы перегрузка с допуском ±6,
    // и тесты масштаба проходили бы почти на любом значении.
    private const int Precision = 6;

    private static Point WorldUnder(SurfaceView view, Point screen) => view.GetWorldPosition(screen);

    private static bool Magnify(EditorHarness harness, double delta, Point at)
    {
        var editor = harness.Editor;
        var args = new PointerDeltaEventArgs(
            InputElement.PointerTouchPadGestureMagnifyEvent,
            editor,
            new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true),
            editor,
            at,
            0,
            PointerPointProperties.None,
            KeyModifiers.None,
            new Vector(delta, delta));

        editor.RaiseEvent(args);
        return args.Handled;
    }

    private static void Pinch(EditorHarness harness, double scale, Point origin) =>
        harness.Editor.RaiseEvent(new PinchEventArgs(scale, origin) { RoutedEvent = InputElement.PinchEvent });

    private static void PinchEnded(EditorHarness harness) =>
        harness.Editor.RaiseEvent(new PinchEndedEventArgs { RoutedEvent = InputElement.PinchEndedEvent });

    [AvaloniaFact]
    public void Zoom_At_Keeps_The_Point_Under_The_Origin()
    {
        var editor = EditorHarness.Create().Editor;
        var origin = new Point(240, 160);
        var before = WorldUnder(editor, origin);

        editor.ZoomAt(2.5, origin);

        Assert.Equal(2.5, editor.ViewportZoom, Precision);
        Assert.Equal(before.X, WorldUnder(editor, origin).X, Precision);
        Assert.Equal(before.Y, WorldUnder(editor, origin).Y, Precision);
    }

    [AvaloniaFact]
    public void Zoom_At_Stays_Within_The_Range()
    {
        var editor = EditorHarness.Create().Editor;

        editor.ZoomAt(1000, new Point(10, 10));
        Assert.Equal(editor.MaxZoom, editor.ViewportZoom, Precision);

        editor.ZoomAt(0.0001, new Point(10, 10));
        Assert.Equal(editor.MinZoom, editor.ViewportZoom, Precision);
    }

    [AvaloniaFact]
    public void Touchpad_Magnify_Grows_By_Its_Increment_Around_The_Pointer()
    {
        var harness = EditorHarness.Create();
        var editor = harness.Editor;
        var at = new Point(300, 200);
        var before = WorldUnder(editor, at);

        var handled = Magnify(harness, 0.25, at);
        Magnify(harness, 0.2, at);

        // Приращения умножаются: 1 × 1,25 × 1,2, а не 1 + 0,25 + 0,2.
        Assert.True(handled);
        Assert.Equal(1.5, editor.ViewportZoom, Precision);
        Assert.Equal(before.X, WorldUnder(editor, at).X, Precision);
        Assert.Equal(before.Y, WorldUnder(editor, at).Y, Precision);
    }

    [AvaloniaFact]
    public void Touchpad_Magnify_Shrinks_With_A_Negative_Increment()
    {
        var harness = EditorHarness.Create();

        Magnify(harness, -0.5, new Point(100, 100));

        Assert.Equal(0.5, harness.Editor.ViewportZoom, Precision);
    }

    [AvaloniaFact]
    public void Finger_Pinch_Scale_Counts_From_The_Start_Of_The_Gesture()
    {
        var harness = EditorHarness.Create();
        var origin = new Point(200, 150);

        // Распознаватель отдаёт масштаб от начала жеста: 1,5, потом 2. Итог — 2,
        // а не 3, как вышло бы, умножай поверхность каждое событие на текущий масштаб.
        Pinch(harness, 1.5, origin);
        Pinch(harness, 2.0, origin);

        Assert.Equal(2.0, harness.Editor.ViewportZoom, Precision);
    }

    [AvaloniaFact]
    public void Finger_Pinch_Carries_The_Canvas_With_The_Fingers()
    {
        var harness = EditorHarness.Create();
        var editor = harness.Editor;
        var start = new Point(200, 150);
        var anchor = WorldUnder(editor, start);

        Pinch(harness, 1.0, start);
        var moved = new Point(260, 190);
        Pinch(harness, 1.6, moved);

        // Точка холста, бывшая между пальцами в начале, остаётся между ними и после
        // сдвига: щипок заодно панорамирует.
        Assert.Equal(1.6, editor.ViewportZoom, Precision);
        Assert.Equal(anchor.X, WorldUnder(editor, moved).X, Precision);
        Assert.Equal(anchor.Y, WorldUnder(editor, moved).Y, Precision);
    }

    [AvaloniaFact]
    public void A_New_Pinch_Starts_From_The_Zoom_The_Last_One_Left()
    {
        var harness = EditorHarness.Create();
        var origin = new Point(200, 150);

        Pinch(harness, 2.0, origin);
        PinchEnded(harness);
        Pinch(harness, 1.5, origin);

        Assert.Equal(3.0, harness.Editor.ViewportZoom, Precision);
    }

    [AvaloniaFact]
    public void Finger_Pinch_Stays_Within_The_Range()
    {
        var harness = EditorHarness.Create();

        Pinch(harness, 100, new Point(50, 50));

        Assert.Equal(harness.Editor.MaxZoom, harness.Editor.ViewportZoom, Precision);
    }

    [AvaloniaFact]
    public void Turning_Pinch_Off_Leaves_Both_Sources_Alone()
    {
        var harness = EditorHarness.Create();
        harness.Editor.InteractionOptions.IsPinchZoomEnabled = false;

        var handled = Magnify(harness, 0.5, new Point(100, 100));
        Pinch(harness, 2.0, new Point(100, 100));

        Assert.False(handled, "Выключенный щипок не должен забирать событие у приложения.");
        Assert.Equal(1.0, harness.Editor.ViewportZoom, Precision);
    }

    [AvaloniaFact]
    public void The_Surface_Listens_For_Finger_Pinches()
    {
        // Без распознавателя события щипка пальцами не возникнут вовсе, и тесты выше
        // проходили бы на поверхности, которая щипка не слышит.
        var editor = EditorHarness.Create().Editor;

        Assert.Contains(editor.GestureRecognizers, recognizer => recognizer is PinchGestureRecognizer);
    }
}
