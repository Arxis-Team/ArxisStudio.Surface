using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Колесо мыши: зум и то, что происходит, когда зумить не просили.
/// </summary>
/// <remarks>
/// <c>ZoomModifiers</c> по умолчанию <c>None</c>, то есть колесо всегда зумит, —
/// поэтому дефект виден только у потребителя, который задал модификатор. Такой
/// потребитель обычно и кладёт редактор внутрь прокручиваемой области: зум по
/// <c>Ctrl</c>, прокрутка без него.
/// </remarks>
public class WheelTests
{
    private static readonly Point Centre = new(400, 300);

    private static EditorHarness Create()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, new Point(100, 100), new Size(200, 150));
        return harness;
    }

    /// <summary>
    /// Признак того, что колесо ушло выше редактора.
    /// </summary>
    /// <remarks>
    /// Обработчик на окне поставлен без <c>handledEventsToo</c>: помеченное
    /// обработанным событие до него не дойдёт — ровно как до внешнего
    /// <c>ScrollViewer</c> у потребителя.
    /// </remarks>
    private static Func<bool> WatchBubbling(EditorHarness harness)
    {
        var reached = false;
        harness.Window.AddHandler(
            InputElement.PointerWheelChangedEvent,
            (object? _, PointerWheelEventArgs _) => reached = true);

        return () => reached;
    }

    [AvaloniaFact]
    public void Wheel_Zooms_When_No_Modifier_Is_Required()
    {
        var harness = Create();
        var before = harness.Editor.ViewportZoom;

        harness.Window.MouseWheel(Centre, new Vector(0, 1));
        harness.RunLayout();

        Assert.True(harness.Editor.ViewportZoom > before);
    }

    [AvaloniaFact]
    public void A_Zooming_Wheel_Does_Not_Bubble()
    {
        var harness = Create();
        var reached = WatchBubbling(harness);

        harness.Window.MouseWheel(Centre, new Vector(0, 1));
        harness.RunLayout();

        // Зум состоялся — событие наше, наружу его отдавать незачем.
        Assert.False(reached());
    }

    [AvaloniaFact]
    public void A_Wheel_Without_The_Required_Modifier_Bubbles()
    {
        var harness = Create();
        harness.Editor.InputGestures.ZoomModifiers = KeyModifiers.Control;
        var before = harness.Editor.ViewportZoom;
        var reached = WatchBubbling(harness);

        harness.Window.MouseWheel(Centre, new Vector(0, 1));
        harness.RunLayout();

        // Зума не было, значит редактор колесо не потребил — и обязан пропустить
        // его дальше. Иначе внешний ScrollViewer не прокручивается вовсе.
        Assert.Equal(before, harness.Editor.ViewportZoom);
        Assert.True(reached());
    }

    [AvaloniaFact]
    public void A_Wheel_With_The_Required_Modifier_Zooms_And_Stops()
    {
        var harness = Create();
        harness.Editor.InputGestures.ZoomModifiers = KeyModifiers.Control;
        var before = harness.Editor.ViewportZoom;
        var reached = WatchBubbling(harness);

        harness.Window.MouseWheel(Centre, new Vector(0, 1), RawInputModifiers.Control);
        harness.RunLayout();

        Assert.True(harness.Editor.ViewportZoom > before);
        Assert.False(reached());
    }
}
