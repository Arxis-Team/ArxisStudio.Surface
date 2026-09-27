using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Поведение редактора там, где позицией ребёнка владеет раскладка.
/// </summary>
/// <remarks>
/// Фиксирует сегодняшнее поведение карточки на <see cref="StackPanel"/>: жест
/// перемещения выполняется, ничего не двигает и при этом попадает в поток
/// изменений. Стратегии размещения перевернут эти утверждения.
/// </remarks>
public class StackHostedCharacterizationTests
{
    private static readonly Point CardLocation = new(100, 100);
    private static readonly Size CardSize = new(340, 400);

    private static readonly Vector DragDelta = new(60, 40);

    private static (EditorHarness Harness, List<SurfaceEditCompletedEventArgs> Edits) Create()
    {
        var harness = EditorHarness.CreateStackHosted();
        harness.PlaceContainer(0, CardLocation, CardSize);

        var edits = new List<SurfaceEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);
        return (harness, edits);
    }

    private static void Drag(EditorHarness harness, Point from, Vector delta)
    {
        harness.Window.MouseDown(from, MouseButton.Left);
        harness.Window.MouseMove(from + new Vector(10, 6));
        harness.Window.MouseMove(from + delta);
        harness.Window.MouseUp(from + delta, MouseButton.Left);
        harness.RunLayout();
    }

    [AvaloniaFact]
    public void Drag_Of_A_Stack_Child_Moves_Nothing()
    {
        var (harness, _) = Create();
        var action = harness.Find<Button>(0, "Action");

        var centre = harness.CentreOf(action);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseUp(centre, MouseButton.Left);
        harness.RunLayout();

        var boundsBefore = harness.Editor.SelectionBounds;
        Drag(harness, centre, DragDelta);

        // Layout.X/Y читает только AbsolutePanel, поэтому запись позиции уходит
        // в пустоту: StackPanel продолжает раскладывать ребёнка по-своему.
        Assert.Equal(boundsBefore, harness.Editor.SelectionBounds);
    }

    [AvaloniaFact]
    public void Drag_Of_A_Stack_Child_Records_Nothing()
    {
        var (harness, edits) = Create();
        var action = harness.Find<Button>(0, "Action");

        var centre = harness.CentreOf(action);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseUp(centre, MouseButton.Left);
        harness.RunLayout();

        var boundsBefore = harness.Editor.SelectionBounds;
        Drag(harness, centre, DragDelta);

        // Раньше стек отмены получал перемещение, которого не было: политика
        // разрешала жест, запись уходила в пустоту, а SurfaceEditScope фиксировал
        // задаваемое значение. Теперь раскладка обнуляет смещение до записи,
        // и фильтр no-op отбрасывает изменение целиком.
        Assert.Empty(edits);
        Assert.Equal(boundsBefore, harness.Editor.SelectionBounds);
    }

    [AvaloniaFact]
    public void Drag_Of_The_Card_Body_Moves_Nothing()
    {
        var (harness, _) = Create();
        var container = harness.Container(0);

        // Пустое место карточки: ниже кнопки, но внутри контейнера.
        var point = new Point(CardLocation.X + (CardSize.Width / 2), CardLocation.Y + CardSize.Height - 40);
        Drag(harness, point, DragDelta);

        // ResolveInteractionTarget отдаёт контейнер только если он уже выбран
        // или зажат Ctrl. Иначе адресуется вложенный контрол, которым владеет стек,
        // и карточка остаётся на месте.
        Assert.Equal(CardLocation, container.Location);
    }

    [AvaloniaFact]
    public void Container_Interaction_Modifier_Still_Moves_The_Card()
    {
        var (harness, _) = Create();
        var container = harness.Container(0);

        var point = new Point(CardLocation.X + (CardSize.Width / 2), CardLocation.Y + CardSize.Height - 40);
        harness.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.Control);
        harness.Window.MouseMove(point + new Vector(10, 6), RawInputModifiers.Control);
        harness.Window.MouseMove(point + DragDelta, RawInputModifiers.Control);
        harness.Window.MouseUp(point + DragDelta, MouseButton.Left, RawInputModifiers.Control);
        harness.RunLayout();

        // Путь, который работает сегодня и должен продолжать работать:
        // карточка лежит на DesignSurface, то есть на AbsolutePanel.
        Assert.Equal(CardLocation.X + DragDelta.X, container.Location.X, 1);
        Assert.Equal(CardLocation.Y + DragDelta.Y, container.Location.Y, 1);
    }
}
