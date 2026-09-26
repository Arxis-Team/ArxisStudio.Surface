using Avalonia;
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
/// Автопрокрутка у края при изменении размера ручкой.
/// </summary>
/// <remarks>
/// Ручка сообщает о движении, только пока движется указатель. У края он стоит, а холст
/// едет, поэтому шаг автопрокрутки повторяет обработчик ручки сам, а размер берётся из
/// снимка указателя на холсте, который тот же шаг пересчитал. Проверяется настоящей
/// протяжкой ручки мышью — только так работает снимок указателя.
/// <para>
/// Тянется форма целиком, а не вложенный контрол: вложенный упёрся бы в свою форму, и
/// проверялось бы ограничение формой. Форма 200 × 150 в (100, 100), правая ручка — на
/// середине её правого края.
/// </para>
/// </remarks>
public class ResizeAutoPanTests
{
    private static readonly Point Origin = new(100, 100);
    private static readonly Size ContainerSize = new(200, 150);
    private static readonly Point NearRightEdge = new(790, 175);

    private static EditorHarness CreateWithFormSelected()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, Origin, ContainerSize);

        var inside = harness.CentreOf(harness.Nested(0));
        harness.Window.MouseDown(inside, MouseButton.Left, RawInputModifiers.Control);
        harness.Window.MouseUp(inside, MouseButton.Left, RawInputModifiers.Control);
        harness.RunLayout();

        Assert.Same(harness.Container(0), Assert.Single(harness.Editor.SelectedDesignTargets).Target);
        return harness;
    }

    private static Point RightHandleCentre(EditorHarness harness)
    {
        var thumb = harness.Editor.GetVisualDescendants()
            .OfType<SelectionAdorner>()
            .Single(a => a.Name == "PART_SelectionAdorner")
            .GetVisualDescendants()
            .OfType<Thumb>()
            .Single(t => t.Name == "PART_Right");

        return thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), harness.Editor)!.Value;
    }

    private static double WidthOf(EditorHarness harness) => harness.Editor.GetDesignSize(harness.Container(0)).Width;

    private static void GrabRightHandleAndMoveTo(EditorHarness harness, Point to)
    {
        var handle = RightHandleCentre(harness);

        // Сперва указатель приходит на ручку, как у человека: без движения до нажатия
        // снимка указателя нет, и жест всю дорогу считал бы по дельте ручки — путь,
        // которым живая ручка почти не ходит.
        harness.Window.MouseMove(handle);
        harness.Window.MouseDown(handle, MouseButton.Left);
        harness.Window.MouseMove(handle + new Vector(10, 0));
        harness.Window.MouseMove(to);
        harness.RunLayout();
    }

    [AvaloniaFact]
    public void A_Handle_At_The_Edge_Pans_And_The_Edge_Follows()
    {
        var harness = CreateWithFormSelected();
        var editor = harness.Editor;
        GrabRightHandleAndMoveTo(harness, NearRightEdge);

        Assert.True(editor.IsAutoPanning, "Ручка у края обязана включить автопрокрутку.");

        var widthBefore = WidthOf(harness);
        var viewportBefore = editor.ViewportLocation.X;

        Assert.True(editor.StepAutoPan(TimeSpan.FromMilliseconds(40)));
        harness.RunLayout();

        // Холст ушёл вправо, указатель стоит — под ним теперь место дальше по холсту, и
        // правый край формы обязан уйти туда же.
        var panned = editor.ViewportLocation.X - viewportBefore;
        Assert.True(panned > 0);
        Assert.InRange(WidthOf(harness), widthBefore + panned - 0.5, widthBefore + panned + 0.5);

        harness.Window.MouseUp(NearRightEdge, MouseButton.Left);
    }

    [AvaloniaFact]
    public void The_Edge_Stays_Under_The_Pointer_Through_Panning()
    {
        var harness = CreateWithFormSelected();
        var editor = harness.Editor;
        GrabRightHandleAndMoveTo(harness, NearRightEdge);
        var grab = editor.GetWorldPosition(NearRightEdge).X - (Origin.X + WidthOf(harness));

        editor.StepAutoPan(TimeSpan.FromMilliseconds(40));
        editor.StepAutoPan(TimeSpan.FromMilliseconds(40));
        harness.RunLayout();

        // Расстояние от указателя до края то же, что было до прокрутки: край идёт за
        // указателем по холсту, а не стоит на экране.
        var after = editor.GetWorldPosition(NearRightEdge).X - (Origin.X + WidthOf(harness));
        Assert.InRange(after, grab - 0.5, grab + 0.5);

        harness.Window.MouseUp(NearRightEdge, MouseButton.Left);
    }

    [AvaloniaFact]
    public void A_Panned_Resize_Is_Still_One_Edit()
    {
        var harness = CreateWithFormSelected();
        var edits = new List<DesignEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);

        GrabRightHandleAndMoveTo(harness, NearRightEdge);
        harness.Editor.StepAutoPan(TimeSpan.FromMilliseconds(40));
        harness.Window.MouseUp(NearRightEdge, MouseButton.Left);

        var edit = Assert.Single(edits);
        Assert.Equal(DesignEditKind.Resize, edit.Kind);
    }

    [AvaloniaFact]
    public void Release_Stops_Panning()
    {
        var harness = CreateWithFormSelected();
        GrabRightHandleAndMoveTo(harness, NearRightEdge);

        harness.Window.MouseUp(NearRightEdge, MouseButton.Left);

        Assert.False(harness.Editor.IsAutoPanning);
        Assert.False(harness.Editor.StepAutoPan(TimeSpan.FromMilliseconds(40)), "После жеста шагать нечем.");
    }

    [AvaloniaFact]
    public void The_Container_Event_Reports_The_Canvas_Shift()
    {
        // Размер во время прокрутки меняется, и событие шага контейнера обязано об этом
        // сказать: указатель относительно холста ушёл на сдвиг холста.
        var harness = CreateWithFormSelected();
        var deltas = new List<Vector>();
        harness.Container(0).AddHandler(SurfaceItem.ResizeDeltaEvent, (object? _, ResizeDeltaEventArgs e) => deltas.Add(e.Delta));

        GrabRightHandleAndMoveTo(harness, NearRightEdge);
        deltas.Clear();
        harness.Window.MouseMove(NearRightEdge + new Vector(1, 0));
        var perMove = deltas.Count;
        deltas.Clear();
        harness.Editor.StepAutoPan(TimeSpan.FromMilliseconds(40));

        // Шаг сообщает о себе так же, как движение указателя, — тем же числом событий
        // (сколько бы их ни было на движение), и каждое несёт сдвиг холста.
        Assert.True(perMove > 0);
        Assert.Equal(perMove, deltas.Count);
        Assert.All(deltas, d => Assert.Equal(harness.Editor.LastAutoPanShift, d));

        harness.Window.MouseUp(NearRightEdge, MouseButton.Left);
    }
}
