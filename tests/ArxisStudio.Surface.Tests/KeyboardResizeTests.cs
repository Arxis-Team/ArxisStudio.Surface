using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface;
using ArxisStudio.Surface.Editing;

namespace ArxisStudio.Tests;

/// <summary>
/// Изменение размера с клавиатуры — замена ручкам для тех, кто не может тянуть
/// (WCAG 2.2, «Dragging Movements»).
/// </summary>
/// <remarks>
/// Правила здесь те же, что у ручки, и тесты сверяют их по одному: политика стороны,
/// ограничение формой, предел жеста, <c>Min</c>/<c>Max</c>. Результат не должен зависеть
/// от того, мышью его добивались или клавиатурой.
/// <para>
/// Стенд: форма 200 × 150 в (100, 100), вложенный контрол 60 × 40 в (110, 110) —
/// правый край формы для него на 190 пикселях ширины.
/// </para>
/// </remarks>
public class KeyboardResizeTests
{
    private static readonly Point ContainerLocation = new(100, 100);
    private static readonly Size ContainerSize = new(200, 150);

    private static Point NestedCentre => new(
        ContainerLocation.X + EditorHarness.NestedOffset + (EditorHarness.NestedWidth / 2),
        ContainerLocation.Y + EditorHarness.NestedOffset + (EditorHarness.NestedHeight / 2));

    private static EditorHarness CreateWithSelection()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);

        harness.Window.MouseDown(NestedCentre, MouseButton.Left);
        harness.Window.MouseUp(NestedCentre, MouseButton.Left);
        harness.RunLayout();

        return harness;
    }

    private static void Press(EditorHarness harness, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.Alt)
    {
        harness.Window.KeyPressQwerty(key, modifiers);
        harness.RunLayout();
    }

    private static Size SizeOf(EditorHarness harness) => harness.Editor.GetTargetSize(harness.Nested(0));

    private static Point PositionOf(EditorHarness harness) => harness.Editor.GetTargetPosition(harness.Nested(0));

    [AvaloniaTheory]
    [InlineData(PhysicalKey.ArrowRight, 61, 40)]
    [InlineData(PhysicalKey.ArrowLeft, 59, 40)]
    [InlineData(PhysicalKey.ArrowDown, 60, 41)]
    [InlineData(PhysicalKey.ArrowUp, 60, 39)]
    public void Alt_Arrow_Moves_The_Right_Or_Bottom_Edge(PhysicalKey key, double width, double height)
    {
        var harness = CreateWithSelection();
        var before = PositionOf(harness);

        Press(harness, key);

        Assert.Equal(new Size(width, height), SizeOf(harness));

        // Двигается дальний край, левый верхний угол стоит.
        Assert.Equal(before, PositionOf(harness));
    }

    [AvaloniaFact]
    public void The_Large_Step_Works_For_Resize_Too()
    {
        var harness = CreateWithSelection();

        Press(harness, PhysicalKey.ArrowRight, RawInputModifiers.Alt | RawInputModifiers.Shift);

        Assert.Equal(70, SizeOf(harness).Width);
    }

    [AvaloniaFact]
    public void A_Plain_Arrow_Still_Moves_Instead_Of_Resizing()
    {
        var harness = CreateWithSelection();
        var before = PositionOf(harness);

        Press(harness, PhysicalKey.ArrowRight, RawInputModifiers.None);

        Assert.Equal(new Size(60, 40), SizeOf(harness));
        Assert.Equal(before.X + 1, PositionOf(harness).X);
    }

    [AvaloniaFact]
    public void One_Press_Is_One_Resize_Edit_And_Undoes_Whole()
    {
        var harness = CreateWithSelection();
        var edits = new List<SurfaceEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);

        Press(harness, PhysicalKey.ArrowRight);

        var edit = Assert.Single(edits);
        Assert.Equal(SurfaceEditKind.Resize, edit.Kind);

        foreach (var change in edit.Changes)
            harness.Editor.Revert(change);
        harness.RunLayout();

        Assert.Equal(new Size(60, 40), SizeOf(harness));
    }

    [AvaloniaFact]
    public void A_Side_The_Policy_Forbids_Does_Not_Move()
    {
        var harness = CreateWithSelection();
        SurfaceInteraction.SetResizePolicy(harness.Nested(0), ResizePolicy.Bottom);

        Press(harness, PhysicalKey.ArrowRight);
        Press(harness, PhysicalKey.ArrowDown);

        // Правый край запрещён, нижний — нет: у ручки было бы так же.
        Assert.Equal(new Size(60, 41), SizeOf(harness));
    }

    [AvaloniaFact]
    public void A_Locked_Target_Produces_No_Edit()
    {
        var harness = CreateWithSelection();
        SurfaceInteraction.SetResizePolicy(harness.Nested(0), ResizePolicy.None);
        var edits = new List<SurfaceEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);

        Press(harness, PhysicalKey.ArrowRight);

        Assert.Empty(edits);
    }

    [AvaloniaFact]
    public void Content_Does_Not_Grow_Past_Its_Form()
    {
        var harness = CreateWithSelection();
        harness.Nested(0).Width = 187;
        harness.RunLayout();

        Press(harness, PhysicalKey.ArrowRight, RawInputModifiers.Alt | RawInputModifiers.Shift);

        // Правый край формы — на 190: крупный шаг в 10 упирается в неё после трёх.
        Assert.Equal(190, SizeOf(harness).Width);
    }

    [AvaloniaFact]
    public void Content_Already_Past_Its_Form_Is_Not_Shrunk_By_Growing()
    {
        var harness = CreateWithSelection();
        harness.Nested(0).Width = 250;
        harness.RunLayout();

        Press(harness, PhysicalKey.ArrowRight);

        // Упёрлось — но не ужимается задним числом до границы формы.
        Assert.Equal(250, SizeOf(harness).Width);
    }

    [AvaloniaFact]
    public void The_Gesture_Floor_Stops_Shrinking_But_Never_Grows_A_Small_Control()
    {
        var harness = CreateWithSelection();
        harness.Nested(0).Height = 12;
        harness.RunLayout();

        Press(harness, PhysicalKey.ArrowUp, RawInputModifiers.Alt | RawInputModifiers.Shift);
        Assert.Equal(10, SizeOf(harness).Height);

        // Мельче предела он уже есть — и не подбрасывается вверх.
        harness.Nested(0).Height = 5;
        harness.RunLayout();
        Press(harness, PhysicalKey.ArrowUp);
        Assert.Equal(5, SizeOf(harness).Height);
    }

    [AvaloniaFact]
    public void Max_Width_Of_The_Control_Wins()
    {
        var harness = CreateWithSelection();
        harness.Nested(0).MaxWidth = 63;

        Press(harness, PhysicalKey.ArrowRight, RawInputModifiers.Alt | RawInputModifiers.Shift);

        Assert.Equal(63, SizeOf(harness).Width);
    }

    [AvaloniaFact]
    public void No_Modifier_Means_No_Keyboard_Resize()
    {
        var harness = CreateWithSelection();
        harness.Editor.InputGestures.KeyboardResizeModifiers = KeyModifiers.None;

        Press(harness, PhysicalKey.ArrowRight);

        // Без модификатора изменением размера стала бы каждая стрелка, поэтому None
        // выключает его, а стрелка остаётся смещением.
        Assert.Equal(new Size(60, 40), SizeOf(harness));
    }

    [AvaloniaFact]
    public void Every_Selected_Form_Grows_On_Its_Own()
    {
        var harness = EditorHarness.Create(nodeCount: 2);
        harness.PlaceContainer(0, new Point(100, 100), ContainerSize);
        harness.PlaceContainer(1, new Point(400, 100), new Size(120, 90));
        harness.Editor.Focus();
        Press(harness, PhysicalKey.A, RawInputModifiers.Control);

        var edits = new List<SurfaceEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);
        Press(harness, PhysicalKey.ArrowRight);

        // Форма верхнего уровня лежит на бесконечном холсте, и ограничивать её нечем.
        Assert.Equal(201, harness.Container(0).Width);
        Assert.Equal(121, harness.Container(1).Width);
        Assert.Single(edits);
    }
}
