using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Tests;

/// <summary>
/// Состояния контейнера, которые приносят разные слои (ADR 0003).
/// </summary>
/// <remarks>
/// Машина состояний контейнера живёт в ядре, а состояния — в трёх слоях: перетаскивание
/// в ядре, изменение размера в инструментах, перестановка в дизайнере интерфейса. Контейнер
/// ядра поэтому не перечисляет их, а спрашивает у самого состояния, и не знает, какая
/// панель прочтёт его позицию, а спрашивает у наследника. Оба вопроса закреплены здесь:
/// до разделения на слои их не держал ни один тест.
/// </remarks>
public class ItemStateTests
{
    [AvaloniaFact]
    public void A_Dragged_Container_Wears_The_Dragging_Pseudo_Class()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, new Point(100, 100), new Size(220, 170));
        var container = harness.Container(0);
        var from = harness.CentreOf(harness.Nested(0));

        harness.Window.MouseDown(from, MouseButton.Left);
        harness.Window.MouseMove(from + new Vector(6, 4));
        harness.Window.MouseMove(from + new Vector(40, 30));

        Assert.Contains(":dragging", container.Classes);

        harness.Window.MouseUp(from + new Vector(40, 30), MouseButton.Left);

        Assert.DoesNotContain(":dragging", container.Classes);
    }

    [AvaloniaFact]
    public void Outside_An_Editor_A_Container_Moves_Only_Where_Its_Location_Is_Read()
    {
        // Без редактора жест пишет Location напрямую, а её читает только AbsolutePanel.
        // В Canvas такая запись ничего бы не сдвинула, и жест не начинается вовсе.
        Assert.NotEqual(new Point(10, 10), DragOutsideAnEditor(new AbsolutePanel()));
        Assert.Equal(new Point(10, 10), DragOutsideAnEditor(new Canvas()));
    }

    private static Point DragOutsideAnEditor(Panel panel)
    {
        var item = new UiDesignerItem
        {
            Location = new Point(10, 10),
            Width = 80,
            Height = 60,
            Content = new Border()
        };

        panel.Children.Add(item);
        var window = new Window { Width = 400, Height = 300, Content = panel };
        window.Show();
        window.UpdateLayout();

        var from = new Point(40, 30);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Vector(6, 4));
        window.MouseMove(from + new Vector(50, 40));
        window.MouseUp(from + new Vector(50, 40), MouseButton.Left);
        window.Close();

        return item.Location;
    }
}
