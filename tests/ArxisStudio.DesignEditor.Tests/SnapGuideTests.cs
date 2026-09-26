using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using ArxisStudio.Surface.Editing;
using Xunit;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Выравнивание по краям и центрам соседей во время перетаскивания.
/// </summary>
/// <remarks>
/// Стенд собран отдельно от общего: у <c>Moving</c> и <c>Anchor</c> намеренно
/// разные размеры и позиции, поэтому каждое выравнивание — край к краю, центр
/// к центру, край к противоположному краю — проверяется по отдельности.
/// В общем стенде оба соседа одинаковы, и все три совпадали бы разом.
/// <para>
/// Контейнер стоит мимо узлов сетки, а цели выравнивания — мимо них тем более:
/// иначе нельзя отличить, кто поставил элемент на место, направляющая или сетка.
/// </para>
/// </remarks>
public class SnapGuideTests
{
    private static readonly Point ContainerLocation = new(105, 103);
    private static readonly Size ContainerSize = new(320, 260);

    /// <summary>Геометрия перетаскиваемого контрола в design-координатах.</summary>
    private static readonly Rect Moving = new(115, 113, 60, 40);

    /// <summary>Геометрия соседа, по которому идёт выравнивание.</summary>
    private static readonly Rect Anchor = new(305, 203, 100, 80);

    /// <summary>Шаг сетки редактора по умолчанию.</summary>
    private const double Cell = 20;

    private static EditorHarness Create(bool snapToGrid = false)
    {
        var nodes = new List<TestNode> { new("guides0") };

        var editor = new DesignEditor
        {
            ItemsSource = nodes,
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<TestNode>((_, _) =>
            {
                var moving = new Border { Name = "Moving", Width = Moving.Width, Height = Moving.Height };
                Layout.SetX(moving, Moving.X - ContainerLocation.X);
                Layout.SetY(moving, Moving.Y - ContainerLocation.Y);

                var anchor = new Border { Name = "Anchor", Width = Anchor.Width, Height = Anchor.Height };
                Layout.SetX(anchor, Anchor.X - ContainerLocation.X);
                Layout.SetY(anchor, Anchor.Y - ContainerLocation.Y);

                var panel = new AbsolutePanel();
                panel.Children.Add(moving);
                panel.Children.Add(anchor);
                return panel;
            }, supportsRecycling: false)
        };

        var window = new Window { Width = 800, Height = 600, Content = editor };
        editor.InteractionOptions.IsSnapToGridEnabled = snapToGrid;
        window.Show();

        var harness = EditorHarness.Adopt(window, editor, nodes);
        harness.RunLayout();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);
        return harness;
    }

    private static Point MovingCentre => new(
        Moving.X + (Moving.Width / 2),
        Moving.Y + (Moving.Height / 2));

    private static Point PositionOf(EditorHarness harness) => new(
        Layout.GetDesignX(harness.Named(0, "Moving")),
        Layout.GetDesignY(harness.Named(0, "Moving")));

    private static void Drag(EditorHarness harness, Vector delta, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        harness.Window.MouseDown(MovingCentre, MouseButton.Left, modifiers);
        harness.Window.MouseMove(MovingCentre + new Vector(6, 4), modifiers);
        harness.Window.MouseMove(MovingCentre + delta, modifiers);
        harness.Window.MouseUp(MovingCentre + delta, MouseButton.Left, modifiers);
        harness.RunLayout();
    }

    /// <summary>Протяжка без отпускания: направляющие живут только внутри жеста.</summary>
    private static void DragWithoutRelease(EditorHarness harness, Vector delta)
    {
        harness.Window.MouseDown(MovingCentre, MouseButton.Left);
        harness.Window.MouseMove(MovingCentre + new Vector(6, 4));
        harness.Window.MouseMove(MovingCentre + delta);
        harness.RunLayout();
    }

    // ---- Арифметика резолвера ------------------------------------------------

    [AvaloniaFact]
    public void Resolver_Pulls_The_Nearest_Edge_Onto_The_Neighbour()
    {
        var moving = new Rect(302, 113, 60, 40);

        var found = DesignSnapGuideResolver.TryResolveOffset(
            moving, new[] { Anchor }, tolerance: 6, out var offset, out var snappedX, out var snappedY);

        Assert.True(found);
        Assert.True(snappedX);
        Assert.False(snappedY);
        Assert.Equal(3, offset.X);
        Assert.Equal(0, offset.Y);
    }

    [AvaloniaFact]
    public void Resolver_Ignores_Candidates_Beyond_The_Tolerance()
    {
        // Сосед подобран так, что ближайшая из девяти пар краёв и центров
        // отстоит ровно на 7: радиус захвата проверяется на границе, а не «где-то далеко».
        var moving = new Rect(100, 100, 60, 40);
        var neighbour = new Rect(167, 300, 60, 40);

        Assert.False(DesignSnapGuideResolver.TryResolveOffset(
            moving, new[] { neighbour }, tolerance: 6, out _, out var missed, out _));
        Assert.False(missed);

        Assert.True(DesignSnapGuideResolver.TryResolveOffset(
            moving, new[] { neighbour }, tolerance: 7, out var offset, out var caught, out _));
        Assert.True(caught);
        Assert.Equal(7, offset.X);
    }

    [AvaloniaFact]
    public void Resolver_Handles_The_Two_Axes_Independently()
    {
        // По X — в радиусе захвата, по Y — далеко.
        var moving = new Rect(302, 113, 60, 40);

        DesignSnapGuideResolver.TryResolveOffset(
            moving, new[] { Anchor }, tolerance: 6, out var offset, out var snappedX, out var snappedY);

        Assert.True(snappedX);
        Assert.False(snappedY);

        // Ось без выравнивания не сдвигается вовсе: её отдают сетке.
        Assert.Equal(0, offset.Y);
    }

    [AvaloniaFact]
    public void Guide_Spans_Both_Elements()
    {
        var bounds = new Rect(305, 113, 60, 40);

        var guides = DesignSnapGuideResolver.CollectGuides(bounds, new[] { Anchor });

        var guide = Assert.Single(guides);
        Assert.Equal(DesignSnapGuideOrientation.Vertical, guide.Orientation);
        Assert.Equal(305, guide.Position);

        // Линия натянута между перетаскиваемым элементом и соседом.
        Assert.Equal(113, guide.Start);
        Assert.Equal(283, guide.End);
    }

    [AvaloniaFact]
    public void Guide_Reaches_The_Farthest_Match()
    {
        var bounds = new Rect(305, 113, 60, 40);
        var far = new Rect(305, 400, 40, 60);

        var guides = DesignSnapGuideResolver.CollectGuides(bounds, new[] { Anchor, far });

        var guide = Assert.Single(guides);
        Assert.Equal(113, guide.Start);

        // Совпавших соседей двое, и линия обязана дотянуться до дальнего.
        Assert.Equal(460, guide.End);
    }

    // ---- Жест ---------------------------------------------------------------

    [AvaloniaFact]
    public void Guides_Are_Enabled_By_Default()
    {
        // Направляющая без включённости по умолчанию — такая же декорация,
        // какой была бы сетка без привязки.
        var editor = new DesignEditor();

        Assert.True(editor.InteractionOptions.IsSnapToGuidesEnabled);
        Assert.Equal(6.0, editor.InteractionOptions.SnapGuideTolerance);
    }

    [AvaloniaFact]
    public void Drag_Aligns_The_Left_Edge_To_A_Neighbour()
    {
        var harness = Create();

        // 187 не доводит до края соседа трёх единиц — их добирает направляющая.
        Drag(harness, new Vector(187, 0));

        Assert.Equal(new Point(Anchor.X, Moving.Y), PositionOf(harness));
    }

    [AvaloniaFact]
    public void Drag_Aligns_Centres()
    {
        var harness = Create();

        Drag(harness, new Vector(207, 0));

        var position = PositionOf(harness);
        Assert.Equal(Anchor.X + (Anchor.Width / 2), position.X + (Moving.Width / 2));
    }

    [AvaloniaFact]
    public void Drag_Aligns_To_The_Form_Itself()
    {
        var harness = Create();

        // Границы формы — не отдельный элемент, но выравнивают по ним чаще всего.
        Drag(harness, new Vector(-13, 0));

        Assert.Equal(ContainerLocation.X, PositionOf(harness).X);
    }

    [AvaloniaFact]
    public void A_Guide_Beats_The_Grid_On_The_Axis_It_Claimed()
    {
        var harness = Create(snapToGrid: true);

        Drag(harness, new Vector(187, 0));

        // 305 — край соседа, и он не узел сетки: победила направляющая.
        Assert.Equal(Anchor.X, PositionOf(harness).X);
        Assert.NotEqual(0, Anchor.X % Cell);
    }

    [AvaloniaFact]
    public void The_Grid_Still_Owns_The_Axis_Without_A_Guide()
    {
        var harness = Create(snapToGrid: true);

        // По X направляющая есть, по Y её нет — и оси расходятся.
        Drag(harness, new Vector(187, 9));

        var position = PositionOf(harness);
        Assert.Equal(Anchor.X, position.X);
        Assert.Equal(120, position.Y);
    }

    [AvaloniaFact]
    public void Snap_Bypass_Modifier_Suppresses_Guides()
    {
        var harness = Create();

        Drag(harness, new Vector(187, 0), RawInputModifiers.Alt);

        // Модификатор снимает всю привязку: элемент встаёт ровно туда, куда ведут.
        Assert.Equal(new Point(Moving.X + 187, Moving.Y), PositionOf(harness));
        Assert.Empty(harness.Editor.SnapGuides);
    }

    [AvaloniaFact]
    public void Disabled_Guides_Leave_The_Position_Alone()
    {
        var harness = Create();
        harness.Editor.InteractionOptions.IsSnapToGuidesEnabled = false;

        Drag(harness, new Vector(187, 0));

        Assert.Equal(Moving.X + 187, PositionOf(harness).X);
        Assert.Empty(harness.Editor.SnapGuides);
    }

    [AvaloniaFact]
    public void Guides_Are_Published_During_The_Gesture()
    {
        var harness = Create();

        DragWithoutRelease(harness, new Vector(187, 0));

        var guide = Assert.Single(harness.Editor.SnapGuides);
        Assert.Equal(DesignSnapGuideOrientation.Vertical, guide.Orientation);
        Assert.Equal(Anchor.X, guide.Position);
    }

    [AvaloniaFact]
    public void Guides_Are_Cleared_When_The_Gesture_Ends()
    {
        var harness = Create();

        DragWithoutRelease(harness, new Vector(187, 0));
        Assert.NotEmpty(harness.Editor.SnapGuides);

        harness.Window.MouseUp(MovingCentre + new Vector(187, 0), MouseButton.Left);
        harness.RunLayout();

        Assert.Empty(harness.Editor.SnapGuides);
    }

    [AvaloniaFact]
    public void A_Target_Does_Not_Align_To_Itself()
    {
        var harness = Create();

        // Сдвиг на единицу: сам элемент рядом со своей исходной позицией всегда,
        // и если бы он попадал в соседи, жест не сдвинул бы его никогда.
        Drag(harness, new Vector(1, 1));

        Assert.Equal(new Point(Moving.X + 1, Moving.Y + 1), PositionOf(harness));
    }

    [AvaloniaFact]
    public void A_Group_Does_Not_Align_To_Its_Own_Members()
    {
        var harness = Create();
        var anchorCentre = new Point(Anchor.X + (Anchor.Width / 2), Anchor.Y + (Anchor.Height / 2));

        harness.Window.MouseDown(MovingCentre, MouseButton.Left);
        harness.Window.MouseUp(MovingCentre, MouseButton.Left);
        harness.Window.MouseDown(anchorCentre, MouseButton.Left, RawInputModifiers.Shift);
        harness.Window.MouseUp(anchorCentre, MouseButton.Left, RawInputModifiers.Shift);
        harness.RunLayout();

        Assert.Equal(2, harness.Editor.SelectedDesignTargetsCount);

        // Сдвиг, который в одиночку встал бы на край Anchor. Здесь Anchor едет
        // вместе с группой, поэтому выравниваться не на что.
        Drag(harness, new Vector(187, 0));

        Assert.Equal(Moving.X + 187, PositionOf(harness).X);
    }

    // ---- Изменение размера --------------------------------------------------

    /// <summary>
    /// Тянет правый край так, как его видит редактор: ручка стоит на применённом
    /// крае, поэтому дельта считается от него, а не от точки нажатия.
    /// </summary>
    private static void DragRightEdge(EditorHarness harness, Control target, double to)
    {
        var container = harness.Container(0);
        var state = new ItemResizingState(container, target, ResizeDirection.Right);
        container.PushState(state);

        var applied = harness.Editor.GetDesignPosition(target).X + harness.Editor.GetDesignSize(target).Width;

        state.OnResizeDelta(new ResizeDeltaEventArgs(
            new Vector(to - applied, 0),
            ResizeDirection.Right,
            DesignEditorItem.ResizeDeltaEvent));

        harness.RunLayout();
    }

    private static Rect BoundsOf(EditorHarness harness, Control target)
        => new(harness.Editor.GetDesignPosition(target), harness.Editor.GetDesignSize(target));

    [AvaloniaFact]
    public void Resize_Aligns_The_Moving_Edge_To_A_Neighbour()
    {
        var harness = Create();
        var moving = harness.Named(0, "Moving");

        // Правый край не доходит до левого края соседа трёх единиц.
        DragRightEdge(harness, moving, Anchor.X - 3);

        // Привязывается край, а не размер: левый край остался на месте.
        var bounds = BoundsOf(harness, moving);
        Assert.Equal(Anchor.X, bounds.Right);
        Assert.Equal(Moving.X, bounds.X);
    }

    [AvaloniaFact]
    public void Resize_Publishes_A_Guide_For_The_Edge_It_Caught()
    {
        var harness = Create();
        var moving = harness.Named(0, "Moving");

        DragRightEdge(harness, moving, Anchor.X - 3);

        var guide = Assert.Single(harness.Editor.SnapGuides);
        Assert.Equal(DesignSnapGuideOrientation.Vertical, guide.Orientation);
        Assert.Equal(Anchor.X, guide.Position);
    }

    [AvaloniaFact]
    public void A_Guide_Beats_The_Grid_On_Resize_Too()
    {
        var harness = Create(snapToGrid: true);
        var moving = harness.Named(0, "Moving");

        DragRightEdge(harness, moving, Anchor.X - 3);

        // 305 — край соседа и не узел сетки.
        Assert.Equal(Anchor.X, BoundsOf(harness, moving).Right);
        Assert.NotEqual(0, Anchor.X % Cell);
    }

    [AvaloniaFact]
    public void Resize_Beyond_The_Tolerance_Falls_Back_To_The_Grid()
    {
        var harness = Create(snapToGrid: true);
        var moving = harness.Named(0, "Moving");

        // Далеко от всех кандидатов: ось достаётся сетке. Значение подобрано
        // с запасом — центральная ось формы проходит по 265, и край рядом с ней
        // поймала бы направляющая, а не сетка.
        DragRightEdge(harness, moving, 222);

        Assert.Equal(220, BoundsOf(harness, moving).Right);
    }

    [AvaloniaFact]
    public void Resize_Across_A_Guide_Does_Not_Oscillate()
    {
        var harness = Create(snapToGrid: true);
        var moving = harness.Named(0, "Moving");
        var container = harness.Container(0);

        var state = new ItemResizingState(container, moving, ResizeDirection.Right);
        container.PushState(state);

        // Ручка стоит на ПРИМЕНЁННОМ крае, поэтому дельта каждый раз считается
        // от него. Именно здесь однажды и жила прыгающая рамка: накопление дельт
        // складывало бы остаток, съеденный привязкой, сам с собой.
        var widths = new List<double>();
        var applied = BoundsOf(harness, moving).Right;
        var pointer = applied;

        for (var i = 0; i < 30; i++)
        {
            pointer += 7;

            state.OnResizeDelta(new ResizeDeltaEventArgs(
                new Vector(pointer - applied, 0),
                ResizeDirection.Right,
                DesignEditorItem.ResizeDeltaEvent));

            harness.RunLayout();

            applied = BoundsOf(harness, moving).Right;
            widths.Add(harness.Editor.GetDesignSize(moving).Width);
        }

        // Протяжка идёт в одну сторону через край соседа: ширина обязана расти
        // монотонно. Любой откат назад — это возвращённый остаток привязки.
        for (var i = 1; i < widths.Count; i++)
            Assert.True(widths[i] >= widths[i - 1], $"шаг {i}: {widths[i - 1]} -> {widths[i]}");

        // И край соседа по дороге действительно был пойман.
        Assert.Contains(Anchor.X - Moving.X, widths);
    }

    [AvaloniaFact]
    public void Resize_Guides_Are_Cleared_When_The_Gesture_Ends()
    {
        var harness = Create();
        var moving = harness.Named(0, "Moving");
        var container = harness.Container(0);

        DragRightEdge(harness, moving, Anchor.X - 3);
        Assert.NotEmpty(harness.Editor.SnapGuides);

        container.PopState();
        harness.RunLayout();

        Assert.Empty(harness.Editor.SnapGuides);
    }

    [AvaloniaFact]
    public void Guides_Do_Not_Republish_An_Unchanged_Set()
    {
        var harness = Create();
        var publications = 0;

        harness.Editor.PropertyChanged += (_, e) =>
        {
            if (e.Property == DesignEditor.SnapGuidesProperty)
                publications++;
        };

        harness.Window.MouseDown(MovingCentre, MouseButton.Left);
        harness.Window.MouseMove(MovingCentre + new Vector(6, 4));

        // Три кадра на одной и той же направляющей: набор линий не меняется.
        harness.Window.MouseMove(MovingCentre + new Vector(187, 0));
        harness.Window.MouseMove(MovingCentre + new Vector(188, 0));
        harness.Window.MouseMove(MovingCentre + new Vector(189, 0));
        harness.RunLayout();

        Assert.Equal(1, publications);
    }
}
