using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Завершение жестов редакторного уровня: рамки выделения и панорамирования.
/// </summary>
/// <remarks>
/// Оба состояния выходят только через <c>OnPointerReleased</c>. Само отпускание
/// доходит: Avalonia на нажатии захватывает попавший под указатель элемент сама,
/// поэтому release приходит даже за пределами окна. Не доходит он тогда, когда
/// <b>захват потерян</b> — его забрал другой элемент, захваченный элемент покинул
/// дерево, или платформа отобрала его сама. Тогда состояние остаётся на стеке
/// навсегда: <see cref="UiDesignerView.IsSelecting"/> залипает, рамка висит,
/// курсор остаётся Hand, и каждое следующее движение панорамирует.
/// <para>
/// Автозахват при этом достаётся не редактору, а тому, что попало под указатель, —
/// в шаблоне это <c>Border</c>. Поэтому состояние захватывает указатель само:
/// иначе доставка зависит от формы шаблона, а не от жеста.
/// </para>
/// <para>
/// У контейнера этот случай закрыт давно (<c>UiDesignerItem.OnPointerCaptureLost</c>),
/// у редактора не был закрыт вовсе.
/// </para>
/// </remarks>
public class GestureLifecycleTests
{
    private static readonly Point EmptyCanvas = new(600, 420);

    /// <summary>
    /// Стенд с контейнером в стороне: жест начинается по пустому холсту.
    /// </summary>
    private static EditorHarness Create()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, new Point(80, 80), new Size(200, 150));
        return harness;
    }

    /// <summary>
    /// Возвращает указатель, которым идёт жест: без него потерю захвата не изобразить.
    /// </summary>
    private static IPointer StartGesture(EditorHarness harness, Point from, MouseButton button, RawInputModifiers modifiers)
    {
        IPointer? pointer = null;
        void Capture(object? sender, PointerPressedEventArgs e) => pointer ??= e.Pointer;

        harness.Editor.AddHandler(InputElement.PointerPressedEvent, Capture, handledEventsToo: true);
        try
        {
            harness.Window.MouseDown(from, button, modifiers);
        }
        finally
        {
            harness.Editor.RemoveHandler(InputElement.PointerPressedEvent, (EventHandler<PointerPressedEventArgs>)Capture);
        }

        Assert.NotNull(pointer);
        return pointer!;
    }

    [AvaloniaFact]
    public void Marquee_Captures_The_Pointer()
    {
        var harness = Create();

        var pointer = StartGesture(harness, EmptyCanvas, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(EmptyCanvas + new Vector(40, 30));
        harness.RunLayout();

        Assert.True(harness.Editor.IsSelecting);

        // Захват принадлежит жесту, а не случайному элементу шаблона.
        Assert.Same(harness.Editor, pointer.Captured);
    }

    [AvaloniaFact]
    public void Losing_Capture_Ends_The_Marquee()
    {
        var harness = Create();

        var pointer = StartGesture(harness, EmptyCanvas, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(EmptyCanvas + new Vector(40, 30));
        harness.RunLayout();
        Assert.True(harness.Editor.IsSelecting);

        pointer.Capture(null);
        harness.RunLayout();

        Assert.False(harness.Editor.IsSelecting);
        Assert.Equal(default, harness.Editor.SelectedArea);
        Assert.Null(harness.Editor.MarqueeScope);
    }

    [AvaloniaFact]
    public void Panning_Captures_The_Pointer()
    {
        var harness = Create();

        var pointer = StartGesture(harness, EmptyCanvas, MouseButton.Middle, RawInputModifiers.None);
        harness.Window.MouseMove(EmptyCanvas + new Vector(30, 20));
        harness.RunLayout();

        Assert.NotEqual(default, harness.Editor.ViewportLocation);
        Assert.Same(harness.Editor, pointer.Captured);
    }

    [AvaloniaFact]
    public void Losing_Capture_Ends_Panning()
    {
        var harness = Create();

        var pointer = StartGesture(harness, EmptyCanvas, MouseButton.Middle, RawInputModifiers.None);
        harness.Window.MouseMove(EmptyCanvas + new Vector(30, 20));
        harness.RunLayout();

        var afterPan = harness.Editor.ViewportLocation;
        pointer.Capture(null);
        harness.RunLayout();

        // Жест закончился, значит движение мыши больше не панорамирует.
        // Точка обязана остаться внутри окна 800x600: снаружи headless ничего
        // не доставляет, и тест проходил бы независимо от захвата.
        harness.Window.MouseMove(EmptyCanvas + new Vector(120, 100));
        harness.RunLayout();

        Assert.Equal(afterPan, harness.Editor.ViewportLocation);
    }

    [AvaloniaFact]
    public void A_Marquee_Left_Behind_Does_Not_Swallow_The_Next_Drag()
    {
        var harness = Create();

        // Рамка начата и брошена: без захвата это состояние оставалось на стеке,
        // и дальше OnItemsDragStarted отклонял перетаскивание по IsSelecting —
        // но контейнер об отказе не знал и продолжал писать геометрию каждый кадр
        // при закрытой единице редактирования. Правка шла мимо undo.
        var pointer = StartGesture(harness, EmptyCanvas, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(EmptyCanvas + new Vector(40, 30));
        harness.RunLayout();
        pointer.Capture(null);
        harness.RunLayout();

        var edits = new List<SurfaceEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);

        var nested = harness.Nested(0);
        var centre = harness.CentreOf(nested);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseMove(centre + new Vector(8, 6));
        harness.Window.MouseMove(centre + new Vector(40, 30));
        harness.Window.MouseUp(centre + new Vector(40, 30), MouseButton.Left);
        harness.RunLayout();

        Assert.Single(edits);
        Assert.Equal(SurfaceEditKind.Move, edits[0].Kind);
    }

    [AvaloniaFact]
    public void Delete_Stops_At_The_First_Handler_That_Performed_It()
    {
        var harness = Create();
        harness.Window.MouseDown(harness.CentreOf(harness.Nested(0)), MouseButton.Left);
        harness.Window.MouseUp(harness.CentreOf(harness.Nested(0)), MouseButton.Left);
        harness.RunLayout();

        var calls = 0;
        harness.Editor.DeleteRequested += (_, e) => { calls++; e.Handled = true; };
        harness.Editor.DeleteRequested += (_, _) => calls++;

        harness.Editor.Focus();
        harness.Window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        harness.RunLayout();

        // Список targets снят до правки: второму обработчику он описывал бы
        // выделение, которого уже нет. То же правило, что и у ReorderRequested.
        Assert.Equal(1, calls);
    }
}
