using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ArxisStudio.Controls;
using Xunit;
using ArxisStudio.Surface.Editing;
using ArxisStudio.Surface;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Tests;

/// <summary>
/// Курсор жеста: чем редактор показывает, что жест идёт.
/// </summary>
/// <remarks>
/// Проверяется наблюдаемое — <c>Cursor</c> того элемента, который держит захват
/// указателя: у жестов контейнера это сам контейнер, у жестов редактора — редактор.
/// Тесты жестов закрепляют проводку — что курсор применяется, что заданный хостом
/// выигрывает у умолчания и что после жеста возвращается ровно то, что было.
/// Сам рисунок закреплён один раз, у умолчаний, через <c>Cursor.ToString()</c>:
/// это единственное, что <see cref="Cursor"/> о себе рассказывает.
/// </remarks>
public class CursorTests
{
    private static readonly Point ContainerLocation = new(100, 100);
    private static readonly Size ContainerSize = new(200, 150);

    /// <summary>Точка холста, свободная от контейнера.</summary>
    private static readonly Point EmptyCanvas = new(600, 480);

    private static EditorHarness Create()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);
        return harness;
    }

    // ---- Перемещение ----------------------------------------------------------

    [AvaloniaFact]
    public void Dragging_Applies_The_Move_Cursor()
    {
        var harness = Create();
        var move = new Cursor(StandardCursorType.SizeAll);
        harness.Editor.Cursors.Move = move;

        var item = harness.Container(0);
        var before = item.Cursor;
        var centre = harness.CentreOf(harness.Nested(0));
        var to = centre + new Vector(30, 20);

        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseMove(to);

        Assert.Same(move, item.Cursor);

        harness.Window.MouseUp(to, MouseButton.Left);

        Assert.Same(before, item.Cursor);
    }

    /// <summary>
    /// Без настройки берётся библиотечный курсор, а не остаётся прежний.
    /// </summary>
    [AvaloniaFact]
    public void An_Unset_Cursor_Falls_Back_To_The_Library_Default()
    {
        var harness = Create();
        var item = harness.Container(0);
        var centre = harness.CentreOf(harness.Nested(0));

        Assert.Null(harness.Editor.Cursors.Move);

        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseMove(centre + new Vector(30, 20));

        Assert.Same(harness.Editor.Cursors.ResolveMove(), item.Cursor);
    }

    /// <summary>
    /// Дрожание в пределах порога — ещё не жест, и курсор ему не меняется.
    /// </summary>
    [AvaloniaFact]
    public void A_Press_Below_The_Drag_Threshold_Leaves_The_Cursor_Alone()
    {
        var harness = Create();
        var item = harness.Container(0);
        var before = item.Cursor;
        var centre = harness.CentreOf(harness.Nested(0));

        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseMove(centre + new Vector(1, 1));

        Assert.Same(before, item.Cursor);
    }

    // ---- Отказ ----------------------------------------------------------------

    /// <summary>
    /// Заблокированный политикой элемент показывает запрет.
    /// </summary>
    /// <remarks>
    /// До <c>ItemDraggingState</c> такой жест не доходит вовсе: его отсекает
    /// <c>ItemIdleState</c>. Поэтому и курсор запрета ставится там — иначе он
    /// не появился бы ни разу.
    /// </remarks>
    [AvaloniaFact]
    public void A_Refused_Drag_Applies_The_Blocked_Cursor()
    {
        var harness = Create();
        var blocked = new Cursor(StandardCursorType.No);
        harness.Editor.Cursors.Blocked = blocked;

        var nested = harness.Nested(0);
        DesignInteraction.SetMovePolicy(nested, MovePolicy.None);

        var item = harness.Container(0);
        var before = item.Cursor;
        var centre = harness.CentreOf(nested);
        var to = centre + new Vector(40, 30);

        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseMove(to);

        Assert.Same(blocked, item.Cursor);

        // Курсор описывает то, что произошло: элемент не двинулся.
        Assert.Equal(EditorHarness.NestedOffset, Layout.GetX(nested));

        harness.Window.MouseUp(to, MouseButton.Left);

        Assert.Same(before, item.Cursor);
    }

    /// <summary>
    /// Запрет — тоже жест: пока элемент не повели, курсор не мигает.
    /// </summary>
    [AvaloniaFact]
    public void A_Refused_Press_Below_The_Threshold_Leaves_The_Cursor_Alone()
    {
        var harness = Create();
        var nested = harness.Nested(0);
        DesignInteraction.SetMovePolicy(nested, MovePolicy.None);

        var item = harness.Container(0);
        var before = item.Cursor;
        var centre = harness.CentreOf(nested);

        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseMove(centre + new Vector(1, 1));

        Assert.Same(before, item.Cursor);
    }

    /// <summary>
    /// Курсор жеста пишется один раз, а не на каждом движении указателя.
    /// </summary>
    /// <remarks>
    /// Замерено на живом демо: до правки одна протяжка давала 48 записей свойства —
    /// курсор возвращался и ставился заново на каждом движении. Та же дисциплина,
    /// что у снимка выделения: совпало — не пишем.
    /// </remarks>
    [AvaloniaFact]
    public void A_Repeated_Gesture_Cursor_Is_Written_Once()
    {
        var harness = Create();
        var nested = harness.Nested(0);
        DesignInteraction.SetMovePolicy(nested, MovePolicy.None);

        var item = harness.Container(0);
        var writes = 0;
        item.PropertyChanged += (_, e) =>
        {
            if (e.Property == InputElement.CursorProperty)
                writes++;
        };

        var centre = harness.CentreOf(nested);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseMove(centre + new Vector(20, 10));
        harness.Window.MouseMove(centre + new Vector(30, 15));
        harness.Window.MouseMove(centre + new Vector(40, 20));

        Assert.Equal(1, writes);

        harness.Window.MouseUp(centre + new Vector(40, 20), MouseButton.Left);

        // Вторая запись — возврат прежнего курсора на отпускании.
        Assert.Equal(2, writes);
    }

    /// <summary>
    /// Потеря захвата возвращает курсор так же, как отпускание.
    /// </summary>
    /// <remarks>
    /// Базовое состояние со стека не снимается, поэтому о брошенном жесте ему
    /// говорит контейнер. Без этого курсор запрета оставался бы на контейнере
    /// навсегда.
    /// </remarks>
    [AvaloniaFact]
    public void Losing_Capture_Restores_The_Cursor()
    {
        var harness = Create();
        var nested = harness.Nested(0);
        DesignInteraction.SetMovePolicy(nested, MovePolicy.None);

        var item = harness.Container(0);
        var before = item.Cursor;

        IPointer? pointer = null;
        item.AddHandler(
            InputElement.PointerPressedEvent,
            (object? _, PointerPressedEventArgs e) => pointer = e.Pointer,
            RoutingStrategies.Tunnel);

        var centre = harness.CentreOf(nested);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseMove(centre + new Vector(40, 30));

        Assert.NotSame(before, item.Cursor);

        Assert.NotNull(pointer);
        pointer!.Capture(null);

        Assert.Same(before, item.Cursor);
    }

    // ---- Панорамирование ------------------------------------------------------

    /// <summary>
    /// После жеста возвращается курсор хоста, а не <see cref="Cursor.Default"/>.
    /// </summary>
    /// <remarks>
    /// Прежде оба состояния писали на выходе <c>Cursor.Default</c> и тем затирали
    /// то, что задал хост.
    /// </remarks>
    [AvaloniaFact]
    public void The_Host_Cursor_Survives_A_Gesture()
    {
        var harness = Create();
        var host = new Cursor(StandardCursorType.Ibeam);
        var pan = new Cursor(StandardCursorType.Hand);
        harness.Editor.Cursor = host;
        harness.Editor.Cursors.Pan = pan;

        harness.Window.MouseDown(EmptyCanvas, MouseButton.Middle);
        harness.Window.MouseMove(EmptyCanvas + new Vector(40, 30));

        Assert.Same(pan, harness.Editor.Cursor);

        harness.Window.MouseUp(EmptyCanvas + new Vector(40, 30), MouseButton.Middle);

        Assert.Same(host, harness.Editor.Cursor);
    }

    // ---- Рамка ----------------------------------------------------------------

    [AvaloniaFact]
    public void Marquee_Applies_Its_Cursor()
    {
        var harness = Create();
        var marquee = new Cursor(StandardCursorType.Cross);
        harness.Editor.Cursors.Marquee = marquee;

        var before = harness.Editor.Cursor;
        var to = EmptyCanvas + new Vector(-80, -60);

        harness.Window.MouseDown(EmptyCanvas, MouseButton.Left);
        harness.Window.MouseMove(to);

        Assert.Same(marquee, harness.Editor.Cursor);

        harness.Window.MouseUp(to, MouseButton.Left);

        Assert.Same(before, harness.Editor.Cursor);
    }

    // ---- Перестановка ---------------------------------------------------------

    private static EditorHarness CreateStack(bool withHandler = true)
    {
        var harness = EditorHarness.CreateStackHosted();
        harness.PlaceContainer(0, ContainerLocation, new Size(340, 400));

        if (withHandler)
        {
            harness.Editor.ReorderRequested += (_, e) =>
            {
                if (e.Target.GetVisualParent() is Panel panel)
                {
                    panel.Children.Move(e.OldIndex, e.NewIndex);
                    e.Handled = true;
                }
            };
        }

        return harness;
    }

    [AvaloniaFact]
    public void Reorder_Applies_Its_Cursor()
    {
        var harness = CreateStack();
        var reorder = new Cursor(StandardCursorType.DragMove);
        harness.Editor.Cursors.Reorder = reorder;

        var item = harness.Container(0);
        var before = item.Cursor;
        var action = harness.Find<Button>(0, "Action");
        var centre = harness.CentreOf(action);
        var to = centre + new Vector(0, -60);

        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseMove(to);

        Assert.Same(reorder, item.Cursor);

        harness.Window.MouseUp(to, MouseButton.Left);

        Assert.Same(before, item.Cursor);
    }

    /// <summary>
    /// Без подписчика перестановка не начнётся, и курсор говорит об этом.
    /// </summary>
    /// <remarks>
    /// Для пользователя это тот же случай, что заблокированный элемент: он ведёт
    /// контрол, а тот не переставится.
    /// </remarks>
    [AvaloniaFact]
    public void Reorder_Without_A_Handler_Applies_The_Blocked_Cursor()
    {
        var harness = CreateStack(withHandler: false);
        var blocked = new Cursor(StandardCursorType.No);
        harness.Editor.Cursors.Blocked = blocked;

        var item = harness.Container(0);
        var action = harness.Find<Button>(0, "Action");
        var centre = harness.CentreOf(action);

        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseMove(centre + new Vector(0, -60));

        Assert.Same(blocked, item.Cursor);
    }

    // ---- Направляющие ---------------------------------------------------------

    private static EditorHarness CreateGuide(DesignGuide guide)
    {
        var harness = Create();
        harness.Editor.Guides = new[] { guide };
        harness.Editor.GuideChangeRequested += (_, _) => { };
        harness.RunLayout();
        return harness;
    }

    [AvaloniaFact]
    public void Dragging_A_Vertical_Guide_Applies_Its_Cursor()
    {
        var harness = CreateGuide(DesignGuide.Vertical(500));
        var vertical = new Cursor(StandardCursorType.SizeWestEast);
        var horizontal = new Cursor(StandardCursorType.SizeNorthSouth);
        harness.Editor.Cursors.GuideVertical = vertical;
        harness.Editor.Cursors.GuideHorizontal = horizontal;

        var before = harness.Editor.Cursor;

        harness.Window.MouseDown(new Point(500, 400), MouseButton.Left);
        harness.Window.MouseMove(new Point(520, 400));

        Assert.Same(vertical, harness.Editor.Cursor);

        harness.Window.MouseUp(new Point(520, 400), MouseButton.Left);

        Assert.Same(before, harness.Editor.Cursor);
    }

    [AvaloniaFact]
    public void Dragging_A_Horizontal_Guide_Applies_Its_Cursor()
    {
        var harness = CreateGuide(DesignGuide.Horizontal(400));
        var vertical = new Cursor(StandardCursorType.SizeWestEast);
        var horizontal = new Cursor(StandardCursorType.SizeNorthSouth);
        harness.Editor.Cursors.GuideVertical = vertical;
        harness.Editor.Cursors.GuideHorizontal = horizontal;

        harness.Window.MouseDown(new Point(500, 400), MouseButton.Left);
        harness.Window.MouseMove(new Point(500, 420));

        Assert.Same(horizontal, harness.Editor.Cursor);
    }

    // ---- Умолчания ------------------------------------------------------------

    /// <summary>
    /// Каждый жест несёт свой курсор, и умолчание считается один раз.
    /// </summary>
    [AvaloniaFact]
    public void Each_Gesture_Has_Its_Own_Default()
    {
        var cursors = new DesignEditorCursors();

        Assert.Same(cursors.ResolveMove(), cursors.ResolveMove());

        var all = new[]
        {
            cursors.ResolveMove(),
            cursors.ResolveBlocked(),
            cursors.ResolvePan(),
            cursors.ResolveMarquee(),
            cursors.ResolveReorder(),
            cursors.ResolveGuide(DesignGuideOrientation.Horizontal),
            cursors.ResolveGuide(DesignGuideOrientation.Vertical)
        };

        Assert.Equal(all.Length, all.Distinct().Count());
        Assert.Equal("SizeAll|No|Hand|Cross|DragMove|SizeNorthSouth|SizeWestEast", string.Join("|", all.Select(c => c.ToString())));
    }

    /// <summary>
    /// Ориентация направляющей не перепутана: заданный курсор достаётся своей оси.
    /// </summary>
    [AvaloniaFact]
    public void Guide_Cursors_Follow_The_Orientation()
    {
        var cursors = new DesignEditorCursors();
        var vertical = new Cursor(StandardCursorType.SizeWestEast);
        cursors.GuideVertical = vertical;

        Assert.Same(vertical, cursors.ResolveGuide(DesignGuideOrientation.Vertical));
        Assert.NotSame(vertical, cursors.ResolveGuide(DesignGuideOrientation.Horizontal));
    }

    // ---- Части --------------------------------------------------------------

    /// <summary>
    /// Курсор ручки приходит из ресурса, а не из литерала в шаблоне.
    /// </summary>
    /// <remarks>
    /// Литерал внутри <c>ControlTemplate</c> перекрыть снаружи нечем — это уже
    /// замерено на <c>PART_HoverBorder</c>. Ресурс перекрывается обычным способом,
    /// и проверяется здесь именно это.
    /// </remarks>
    [AvaloniaFact]
    public void Handle_Cursors_Come_From_Resources()
    {
        var custom = new Cursor(StandardCursorType.Help);
        var adorner = new SelectionAdorner { Width = 120, Height = 90, ShowHandles = true };
        var window = new Window { Width = 400, Height = 300, Content = adorner };
        window.Resources["DesignEditor.SelectionAdorner.CursorTopLeft"] = custom;
        window.Show();

        var manager = window.GetLayoutManager();
        manager?.ExecuteInitialLayoutPass();
        manager?.ExecuteLayoutPass();

        var thumbs = adorner.GetVisualDescendants().OfType<Thumb>().ToList();
        var topLeft = thumbs.FirstOrDefault(t => t.Name == "PART_TopLeft");
        var topRight = thumbs.FirstOrDefault(t => t.Name == "PART_TopRight");

        Assert.NotNull(topLeft);
        Assert.NotNull(topRight);

        Assert.Same(custom, topLeft!.Cursor);

        // Перекрыт один ключ, а не все: у соседней ручки курсор остался свой.
        Assert.NotSame(custom, topRight!.Cursor);
        Assert.NotNull(topRight.Cursor);
    }
}
