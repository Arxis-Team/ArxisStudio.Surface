using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Контракт панорамирования viewport.
/// </summary>
/// <remarks>
/// До этого о панорамировании было закреплено только то, что жест захватывает
/// указатель и переживает потерю захвата (<c>GestureLifecycleTests</c>). Само же
/// обещание — куда и на сколько уезжает холст — не проверял никто, при том что
/// в нём два места, где легко ошибиться знаком и делением: смещение считается
/// <b>от точки нажатия</b>, а не накоплением по кадрам, и делится на зум, потому
/// что указатель ходит в экранных пикселях, а <c>ViewportLocation</c> живёт
/// в координатах холста.
/// </remarks>
public class PanningTests
{
    /// <summary>Точка на пустом холсте: контейнер стоит в стороне.</summary>
    private static readonly Point EmptyCanvas = new(600, 420);

    private static EditorHarness Create()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, new Point(80, 80), new Size(200, 150));
        return harness;
    }

    private static void Pan(EditorHarness harness, Vector movement, MouseButton button = MouseButton.Middle)
    {
        harness.Window.MouseDown(EmptyCanvas, button);
        harness.Window.MouseMove(EmptyCanvas + (movement * 0.5));
        harness.Window.MouseMove(EmptyCanvas + movement);
        harness.Window.MouseUp(EmptyCanvas + movement, button);
        harness.RunLayout();
    }

    [AvaloniaFact]
    public void The_Canvas_Follows_The_Pointer()
    {
        var harness = Create();

        Pan(harness, new Vector(60, 40));

        // Указатель уехал вправо-вниз — значит содержимое должно уехать туда же,
        // а окно в мировых координатах, наоборот, влево-вверх. Знак здесь и есть
        // всё обещание жеста: перепутанный превращает «тащу холст» в «тащу окно».
        Assert.Equal(new Point(-60, -40), harness.Editor.ViewportLocation);
    }

    [AvaloniaFact]
    public void Screen_Movement_Is_Divided_By_The_Zoom()
    {
        var harness = Create();
        harness.Editor.ViewportZoom = 2.0;
        harness.RunLayout();

        Pan(harness, new Vector(60, 40));

        // На двукратном увеличении те же 60 экранных пикселей — это 30 мировых.
        // Без деления холст уезжал бы вдвое дальше указателя, и чем ближе
        // подъехал пользователь, тем сильнее.
        Assert.Equal(new Point(-30, -20), harness.Editor.ViewportLocation);
    }

    [AvaloniaFact]
    public void Returning_To_The_Press_Point_Returns_The_Viewport()
    {
        var harness = Create();
        var before = harness.Editor.ViewportLocation;

        harness.Window.MouseDown(EmptyCanvas, MouseButton.Middle);
        harness.Window.MouseMove(EmptyCanvas + new Vector(90, 70));
        harness.Window.MouseMove(EmptyCanvas + new Vector(-40, 120));
        harness.Window.MouseMove(EmptyCanvas);
        harness.RunLayout();

        // Смещение считается от точки нажатия, а не складывается по кадрам,
        // поэтому возврат указателя возвращает и холст — ровно, без остатка.
        // Накопительная реализация проходила бы прямые протяжки и копила бы
        // ошибку на такой вот петле.
        Assert.Equal(before, harness.Editor.ViewportLocation);

        harness.Window.MouseUp(EmptyCanvas, MouseButton.Middle);
    }

    [AvaloniaFact]
    public void The_Left_Button_Does_Not_Pan()
    {
        var harness = Create();

        Pan(harness, new Vector(60, 40), MouseButton.Left);

        // Левая кнопка на пустом холсте — это рамка выделения, и вести
        // одновременно оба жеста было бы нечем.
        Assert.Equal(default, harness.Editor.ViewportLocation);
    }

    [AvaloniaFact]
    public void The_Button_Is_Configurable()
    {
        var harness = Create();
        harness.Editor.InputGestures.PanButton = SurfacePointerButton.Right;
        harness.Editor.InputGestures.PanModifiers = KeyModifiers.Alt;

        Pan(harness, new Vector(60, 40));

        // Средняя кнопка больше не панорамирует: политику задаёт конфиг,
        // а не зашитая в состояние кнопка.
        Assert.Equal(default, harness.Editor.ViewportLocation);
    }

    [AvaloniaFact]
    public void Panning_Leaves_The_Selection_Alone()
    {
        var harness = Create();
        harness.Editor.SelectTarget(harness.Nested(0));
        var selected = harness.Editor.SelectedTargets.Single().Target;

        var raised = 0;
        harness.Editor.SurfaceSelectionChanged += (_, _) => raised++;

        // Протяжка проходит прямо по контейнеру, а не по пустому месту. Точка
        // выбрана мимо ручек адорнера: (150,130) — это угол выделенного Nested,
        // то есть ручка, и нажатие туда жестом редактора уже не будет.
        harness.Window.MouseDown(new Point(250, 200), MouseButton.Middle);
        harness.Window.MouseMove(new Point(310, 240));
        harness.Window.MouseUp(new Point(310, 240), MouseButton.Middle);
        harness.RunLayout();

        Assert.NotEqual(default, harness.Editor.ViewportLocation);
        Assert.Same(selected, harness.Editor.SelectedTargets.Single().Target);
        Assert.Equal(0, raised);
    }

    [AvaloniaFact]
    public void The_Wheel_Still_Zooms_While_Panning()
    {
        var harness = Create();

        harness.Window.MouseDown(EmptyCanvas, MouseButton.Middle);
        harness.Window.MouseMove(EmptyCanvas + new Vector(30, 20));
        harness.Window.MouseWheel(EmptyCanvas + new Vector(30, 20), new Vector(0, 1));
        harness.RunLayout();

        // Панорамирование не съедает колесо: состояние отвечает на него тем же
        // зумом, что и Idle. Иначе жест запирал бы масштаб до отпускания кнопки.
        Assert.True(harness.Editor.ViewportZoom > 1.0);

        harness.Window.MouseUp(EmptyCanvas + new Vector(30, 20), MouseButton.Middle);
    }

    [AvaloniaFact]
    public void The_Cursor_Is_Restored_When_The_Gesture_Ends()
    {
        var harness = Create();
        var before = harness.Editor.Cursor;

        harness.Window.MouseDown(EmptyCanvas, MouseButton.Middle);
        harness.Window.MouseMove(EmptyCanvas + new Vector(30, 20));

        var duringGesture = harness.Editor.Cursor;

        harness.Window.MouseUp(EmptyCanvas + new Vector(30, 20), MouseButton.Middle);
        harness.RunLayout();

        // Курсор — единственная видимая часть состояния панорамирования, и именно
        // по застрявшей руке замечали брошенный жест. Оба конца проверяются вместе:
        // «стало рукой» без «вернулось» ничего не гарантирует.
        Assert.Same(harness.Editor.Cursors.ResolvePan(), duringGesture);
        Assert.Same(before, harness.Editor.Cursor);

        // Возвращается прежнее значение, а не Cursor.Default: прежняя версия писала
        // на выходе Default и тем затирала курсор, заданный хостом. Проверка на этом
        // и стояла, закрепляя затирание, — см. CursorTests.
        Assert.Null(harness.Editor.Cursor);
    }
}
