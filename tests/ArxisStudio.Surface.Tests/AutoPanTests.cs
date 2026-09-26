using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Автопрокрутка у края: перетаскивание и рамка, доведённые до края, сдвигают холст,
/// и жест продолжается за пределы видимого.
/// </summary>
/// <remarks>
/// В безголовом режиме время стоит, поэтому таймер здесь не ждут, а делают его шаг сами —
/// <c>StepAutoPan</c> с заданным временем. Скорость у края по умолчанию 900 пикселей в
/// секунду, полоса 32 пикселя; редактор 800 × 600.
/// </remarks>
public class AutoPanTests
{
    private const int Precision = 6;
    private static readonly Point Origin = new(100, 100);
    private static readonly Size ContainerSize = new(200, 150);

    // Правый край минус 10: глубина в полосе (32 − 10) / 32.
    private static readonly Point NearRightEdge = new(790, 175);
    private const double NearRightSpeed = 900.0 * 22 / 32;

    /// <summary>
    /// Перетаскивание ставит элемент на целый пиксель (без привязки — округлением),
    /// поэтому положение формы сверяется с точностью до половины пикселя.
    /// </summary>
    private static void AssertWithinHalfPixel(double expected, double actual) =>
        Assert.InRange(actual, expected - 0.5, expected + 0.5);

    private static EditorHarness Create()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, Origin, ContainerSize);
        return harness;
    }

    /// <summary>
    /// Тянет форму целиком — с модификатором контейнера: вложенный контрол у края своей
    /// формы упёрся бы в неё, и проверялось бы ограничение формой, а не прокрутка.
    /// </summary>
    private static void GrabFormAndMoveTo(EditorHarness harness, Point to)
    {
        var from = harness.CentreOf(harness.Nested(0));
        harness.Window.MouseDown(from, MouseButton.Left, RawInputModifiers.Control);
        harness.Window.MouseMove(from + new Vector(6, 4), RawInputModifiers.Control);
        harness.Window.MouseMove(to, RawInputModifiers.Control);
    }

    [AvaloniaFact]
    public void The_Middle_Of_The_Editor_Does_Not_Pan()
    {
        var editor = EditorHarness.Create().Editor;

        Assert.Equal(default, editor.GetAutoPanVelocity(new Point(400, 300)));
    }

    [AvaloniaFact]
    public void Speed_Grows_With_Depth_Into_The_Edge_Band()
    {
        var editor = EditorHarness.Create().Editor;

        Assert.Equal(NearRightSpeed, editor.GetAutoPanVelocity(NearRightEdge).X, Precision);
        Assert.Equal(-900.0 / 2, editor.GetAutoPanVelocity(new Point(16, 300)).X, Precision);
        Assert.Equal(0, editor.GetAutoPanVelocity(NearRightEdge).Y, Precision);
    }

    [AvaloniaFact]
    public void Beyond_The_Edge_The_Speed_Is_The_Largest()
    {
        // Захваченный указатель уходит за пределы редактора — там человек просит ехать
        // быстрее, а не останавливаться.
        var editor = EditorHarness.Create().Editor;

        Assert.Equal(900, editor.GetAutoPanVelocity(new Point(1200, 300)).X, Precision);
        Assert.Equal(-900, editor.GetAutoPanVelocity(new Point(400, -300)).Y, Precision);
    }

    [AvaloniaFact]
    public void In_A_Corner_Both_Axes_Pan()
    {
        var editor = EditorHarness.Create().Editor;

        var velocity = editor.GetAutoPanVelocity(new Point(795, 595));

        Assert.True(velocity.X > 0 && velocity.Y > 0, $"В углу холст едет по диагонали, получено {velocity}.");
    }

    [AvaloniaFact]
    public void Turned_Off_It_Never_Pans()
    {
        var editor = EditorHarness.Create().Editor;
        editor.InteractionOptions.IsAutoPanEnabled = false;

        Assert.Equal(default, editor.GetAutoPanVelocity(new Point(1200, 300)));
    }

    [AvaloniaFact]
    public void A_Drag_At_The_Edge_Pans_And_Carries_The_Form()
    {
        var harness = Create();
        var editor = harness.Editor;
        GrabFormAndMoveTo(harness, NearRightEdge);

        Assert.True(editor.IsAutoPanning, "У края перетаскивание обязано включить автопрокрутку.");

        var viewportBefore = editor.ViewportLocation;
        var formBefore = harness.Container(0).Location;

        Assert.True(editor.StepAutoPan(TimeSpan.FromMilliseconds(40)));

        // Холст сдвинулся, и форма — на столько же: указатель стоит, а под ним уже
        // другое место холста.
        var panned = editor.ViewportLocation.X - viewportBefore.X;
        Assert.Equal(NearRightSpeed * 0.04, panned, Precision);
        AssertWithinHalfPixel(formBefore.X + panned, harness.Container(0).Location.X);
        Assert.Equal(formBefore.Y, harness.Container(0).Location.Y, Precision);

        harness.Window.MouseUp(NearRightEdge, MouseButton.Left, RawInputModifiers.Control);
    }

    [AvaloniaFact]
    public void The_Form_Follows_The_Pointer_After_Panning()
    {
        var harness = Create();
        var editor = harness.Editor;

        // До жеста холст не сдвинут, и экран совпадает с холстом: точка захвата на
        // форме — это просто разница с её углом.
        var grabOffset = harness.CentreOf(harness.Nested(0)) - Origin;

        GrabFormAndMoveTo(harness, NearRightEdge);
        editor.StepAutoPan(TimeSpan.FromMilliseconds(40));
        editor.StepAutoPan(TimeSpan.FromMilliseconds(40));

        // Вернулись внутрь: под указателем обязана оказаться та же точка формы, за
        // которую её схватили, — смещение считается от точки нажатия на холсте.
        var inside = new Point(500, 175);
        harness.Window.MouseMove(inside, RawInputModifiers.Control);
        Assert.False(editor.IsAutoPanning, "Вне полосы автопрокрутка останавливается.");

        var offset = editor.GetWorldPosition(inside) - harness.Container(0).Location;
        AssertWithinHalfPixel(grabOffset.X, offset.X);
        AssertWithinHalfPixel(grabOffset.Y, offset.Y);

        harness.Window.MouseUp(inside, MouseButton.Left, RawInputModifiers.Control);
    }

    [AvaloniaFact]
    public void Wheel_Zoom_Mid_Drag_Keeps_The_Form_Under_The_Pointer()
    {
        // До автопрокрутки начало жеста пересчитывалось по текущему viewport, и зум
        // колесом посреди перетаскивания сдвигал форму из-под указателя.
        var harness = Create();
        var editor = harness.Editor;
        var at = new Point(400, 300);
        GrabFormAndMoveTo(harness, at);
        var grabOffset = editor.GetWorldPosition(at) - harness.Container(0).Location;

        harness.Window.MouseWheel(at, new Vector(0, 1), RawInputModifiers.Control);
        harness.Window.MouseMove(at + new Vector(1, 0), RawInputModifiers.Control);

        Assert.NotEqual(1.0, editor.ViewportZoom);
        var offsetAfter = editor.GetWorldPosition(at + new Vector(1, 0)) - harness.Container(0).Location;
        AssertWithinHalfPixel(grabOffset.X, offsetAfter.X);
        AssertWithinHalfPixel(grabOffset.Y, offsetAfter.Y);

        harness.Window.MouseUp(at, MouseButton.Left, RawInputModifiers.Control);
    }

    [AvaloniaFact]
    public void A_Panned_Drag_Is_Still_One_Edit()
    {
        var harness = Create();
        var edits = new List<DesignEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);

        GrabFormAndMoveTo(harness, NearRightEdge);
        harness.Editor.StepAutoPan(TimeSpan.FromMilliseconds(40));
        harness.Editor.StepAutoPan(TimeSpan.FromMilliseconds(40));
        harness.Window.MouseUp(NearRightEdge, MouseButton.Left, RawInputModifiers.Control);

        // Прокрутка не правка: отмена вернёт форму целиком, одним шагом.
        Assert.Single(edits);
    }

    [AvaloniaFact]
    public void Release_Stops_Panning()
    {
        var harness = Create();
        GrabFormAndMoveTo(harness, NearRightEdge);

        harness.Window.MouseUp(NearRightEdge, MouseButton.Left, RawInputModifiers.Control);

        Assert.False(harness.Editor.IsAutoPanning);
        Assert.False(harness.Editor.StepAutoPan(TimeSpan.FromMilliseconds(40)), "После жеста шагать нечем.");
    }

    [AvaloniaFact]
    public void Pressing_At_The_Edge_Without_Moving_Does_Not_Pan()
    {
        var harness = Create();

        harness.Window.MouseDown(new Point(795, 580), MouseButton.Left);

        Assert.False(harness.Editor.IsAutoPanning, "Нажатие у края ещё ничего не тянет.");
        harness.Window.MouseUp(new Point(795, 580), MouseButton.Left);
    }

    [AvaloniaFact]
    public void A_Marquee_Keeps_Its_Start_And_Grows_With_The_Canvas()
    {
        var harness = Create();
        var editor = harness.Editor;
        var start = new Point(500, 400);
        var nearBottom = new Point(600, 590);

        harness.Window.MouseDown(start, MouseButton.Left);
        harness.Window.MouseMove(nearBottom);
        Assert.True(editor.IsAutoPanning);

        var areaBefore = editor.SelectedArea;
        editor.StepAutoPan(TimeSpan.FromMilliseconds(40));
        var panned = editor.ViewportLocation.Y;

        // Начало рамки лежит на холсте и не двигается, угол уходит вместе с холстом.
        Assert.True(panned > 0);
        Assert.Equal(areaBefore.Top, editor.SelectedArea.Top, Precision);
        Assert.Equal(areaBefore.Bottom + panned, editor.SelectedArea.Bottom, Precision);

        harness.Window.MouseUp(nearBottom, MouseButton.Left);
        Assert.False(editor.IsAutoPanning);
    }

    [AvaloniaFact]
    public void The_Same_Screen_Speed_Covers_More_Canvas_When_Zoomed_Out()
    {
        var harness = Create();
        var editor = harness.Editor;
        editor.ViewportZoom = 0.5;

        harness.Window.MouseDown(new Point(500, 400), MouseButton.Left);
        harness.Window.MouseMove(new Point(790, 400));
        editor.StepAutoPan(TimeSpan.FromMilliseconds(40));

        Assert.Equal(NearRightSpeed * 0.04 / 0.5, editor.ViewportLocation.X, Precision);
        harness.Window.MouseUp(new Point(790, 400), MouseButton.Left);
    }
}
