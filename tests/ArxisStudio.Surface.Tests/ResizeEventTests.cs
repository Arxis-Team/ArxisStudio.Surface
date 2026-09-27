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
/// Публичное событие <see cref="SurfaceItem.ResizeDelta"/> контейнера: одно на движение указателя.
/// </summary>
/// <remarks>
/// Поднимали его двое — состояние изменения размера, применив геометрию, и обработчик
/// ручки у дизайнера интерфейса следом за ним, — и хост получал каждое движение дважды с одной
/// и той же дельтой. Сумма дельт тогда вдвое больше пройденного, а счётчик шагов врёт.
/// Проверяется настоящей протяжкой ручки: указатель сперва приходит на неё, как у человека.
/// </remarks>
public class ResizeEventTests
{
    private static readonly Point Origin = new(100, 100);
    private static readonly Size ContainerSize = new(200, 150);

    private static Thumb RightThumb(SelectionAdorner adorner) =>
        adorner.GetVisualDescendants().OfType<Thumb>().Single(t => t.Name == "PART_Right");

    private static Point CentreIn(EditorHarness harness, Thumb thumb) =>
        thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), harness.Editor)!.Value;

    private static List<Vector> Record(SurfaceItem container)
    {
        var deltas = new List<Vector>();
        container.AddHandler(SurfaceItem.ResizeDeltaEvent, (object? _, ResizeDeltaEventArgs e) => deltas.Add(e.Delta));
        return deltas;
    }

    /// <summary>
    /// Тянет ручку тремя движениями по 10 пикселей и отдаёт, что пришло на каждое.
    /// </summary>
    private static List<int> CountPerMove(EditorHarness harness, Thumb thumb, List<Vector> deltas)
    {
        var handle = CentreIn(harness, thumb);
        harness.Window.MouseMove(handle);
        harness.Window.MouseDown(handle, MouseButton.Left);

        var counts = new List<int>();
        for (var i = 1; i <= 3; i++)
        {
            deltas.Clear();
            harness.Window.MouseMove(handle + new Vector(10 * i, 0));
            counts.Add(deltas.Count);
        }

        harness.Window.MouseUp(handle + new Vector(30, 0), MouseButton.Left);
        return counts;
    }

    [AvaloniaFact]
    public void The_Primary_Handle_Raises_One_Event_Per_Move()
    {
        // Форма выбрана целиком: её рамка — главный адорнер.
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, Origin, ContainerSize);
        var inside = harness.CentreOf(harness.Nested(0));
        harness.Window.MouseDown(inside, MouseButton.Left, RawInputModifiers.Control);
        harness.Window.MouseUp(inside, MouseButton.Left, RawInputModifiers.Control);
        harness.RunLayout();

        var primary = harness.Editor.GetVisualDescendants()
            .OfType<SelectionAdorner>()
            .Single(a => a.Name == "PART_SelectionAdorner");
        var deltas = Record(harness.Container(0));

        var counts = CountPerMove(harness, RightThumb(primary), deltas);

        Assert.True(counts.All(c => c == 1), $"На каждое движение ручки — одно событие, получено: {string.Join(", ", counts)}.");
    }

    [AvaloniaFact]
    public void A_Secondary_Handle_Raises_One_Event_Per_Move()
    {
        // Два вложенных контрола одной формы: у каждого свой вторичный адорнер с ручками.
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, Origin, ContainerSize);
        var nested = harness.CentreOf(harness.Nested(0));
        var sibling = harness.CentreOf(harness.Named(0, "Sibling"));
        harness.Window.MouseDown(nested, MouseButton.Left);
        harness.Window.MouseUp(nested, MouseButton.Left);
        harness.Window.MouseDown(sibling, MouseButton.Left, RawInputModifiers.Shift);
        harness.Window.MouseUp(sibling, MouseButton.Left, RawInputModifiers.Shift);
        harness.RunLayout();

        Assert.Equal(2, harness.Editor.SelectedTargetsCount);

        var secondary = harness.Editor.GetVisualDescendants()
            .OfType<SelectionAdorner>()
            .First(a => a.Name != "PART_SelectionAdorner" && a.Name != "PART_GroupSelectionAdorner" && a.IsEffectivelyVisible);
        var deltas = Record(harness.Container(0));

        var counts = CountPerMove(harness, RightThumb(secondary), deltas);

        Assert.True(counts.All(c => c == 1), $"На каждое движение ручки — одно событие, получено: {string.Join(", ", counts)}.");
    }
}
