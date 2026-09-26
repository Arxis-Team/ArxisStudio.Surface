using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Xunit;
using DesignLayout = ArxisStudio.Surface.UiDesigner.Layout;
using ArxisStudio.Surface.Editing;
using ArxisStudio.Surface;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Tests;

/// <summary>
/// Перестановка контрола среди соседей в раскладке, которая владеет позицией.
/// </summary>
/// <remarks>
/// Деревом контролов редактор не владеет: он распознаёт жест, показывает точку
/// вставки и поднимает <c>ReorderRequested</c>. Саму перестановку выполняет
/// приложение — здесь его роль играет обработчик в тесте.
/// </remarks>
public class ReorderTests
{
    private static readonly Point CardLocation = new(100, 100);
    private static readonly Size CardSize = new(340, 400);

    private static readonly Vector DragDelta = new(60, 40);

    private sealed record Request(Control Target, int OldIndex, int NewIndex);

    /// <summary>
    /// Собирает стенд и подключает обработчик, который выполняет перестановку.
    /// </summary>
    private static (EditorHarness Harness, List<Request> Requests) Create(bool handle = true)
    {
        var harness = EditorHarness.CreateStackHosted();
        harness.PlaceContainer(0, CardLocation, CardSize);

        var requests = new List<Request>();
        harness.Editor.ReorderRequested += (_, e) =>
        {
            requests.Add(new Request(e.Target, e.OldIndex, e.NewIndex));

            if (!handle)
                return;

            // Роль библиотеки разметки: структурную правку делает она.
            if (e.Target.GetVisualParent() is Panel panel)
            {
                panel.Children.Move(e.OldIndex, e.NewIndex);
                e.Handled = true;
            }
        };

        return (harness, requests);
    }

    private static Panel PanelOf(Control child) => (Panel)child.GetVisualParent()!;

    private static void Drag(EditorHarness harness, Point from, Vector delta)
    {
        harness.Window.MouseDown(from, MouseButton.Left);
        harness.Window.MouseMove(from + new Vector(4, 6));
        harness.Window.MouseMove(from + delta);
        harness.Window.MouseUp(from + delta, MouseButton.Left);
        harness.RunLayout();
    }

    [AvaloniaFact]
    public void Gesture_Asks_To_Reorder_And_Reports_Both_Indices()
    {
        var (harness, requests) = Create();
        var action = harness.Find<Button>(0, "Action");

        Drag(harness, harness.CentreOf(action), new Vector(0, -60));

        var request = Assert.Single(requests);
        Assert.Same(action, request.Target);
        Assert.Equal(1, request.OldIndex);
        Assert.Equal(0, request.NewIndex);
    }

    [AvaloniaFact]
    public void Handled_Request_Changes_The_Order()
    {
        var (harness, _) = Create();
        var action = harness.Find<Button>(0, "Action");
        var field = harness.Find<TextBox>(0, "Field");
        var panel = PanelOf(action);

        Assert.Equal(0, panel.Children.IndexOf(field));
        Assert.Equal(1, panel.Children.IndexOf(action));

        Drag(harness, harness.CentreOf(action), new Vector(0, -60));

        Assert.Equal(0, panel.Children.IndexOf(action));
        Assert.Equal(1, panel.Children.IndexOf(field));
    }

    [AvaloniaFact]
    public void Unhandled_Request_Leaves_The_Tree_Alone()
    {
        var (harness, requests) = Create(handle: false);
        var action = harness.Find<Button>(0, "Action");
        var panel = PanelOf(action);

        Drag(harness, harness.CentreOf(action), new Vector(0, -60));

        // Редактор сообщил намерение, но сам ничего не тронул.
        Assert.Single(requests);
        Assert.Equal(1, panel.Children.IndexOf(action));
    }

    [AvaloniaFact]
    public void Reorder_Stays_Out_Of_The_Edit_Contract()
    {
        var (harness, requests) = Create();
        var action = harness.Find<Button>(0, "Action");
        var panel = PanelOf(action);

        var edits = new List<DesignEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);

        Drag(harness, harness.CentreOf(action), new Vector(0, -60));

        // Сначала убеждаемся, что перестановка вообще произошла: без этого
        // Assert.Empty(edits) держался бы и на полностью удалённом жесте.
        Assert.Single(requests);
        Assert.Equal(0, panel.Children.IndexOf(action));

        // Структурная правка не принадлежит редактору, поэтому и в его поток
        // изменений не попадает: там живут геометрия и порядок перекрытия.
        Assert.Empty(edits);
    }

    [AvaloniaFact]
    public void Gesture_Returning_To_The_Original_Slot_Asks_For_Nothing()
    {
        var (harness, requests) = Create();
        var action = harness.Find<Button>(0, "Action");

        var panel = PanelOf(action);

        // Смещение меньше половины соседа: точка вставки не меняется.
        Drag(harness, harness.CentreOf(action), new Vector(0, -4));

        Assert.Empty(requests);
        Assert.Equal(1, panel.Children.IndexOf(action));
    }

    [AvaloniaFact]
    public void User_Lock_Blocks_Reorder()
    {
        var (harness, requests) = Create();
        var action = harness.Find<Button>(0, "Action");
        DesignInteraction.SetMovePolicy(action, MovePolicy.None);

        var panel = PanelOf(action);

        Drag(harness, harness.CentreOf(action), new Vector(0, -60));

        // Запрет пользователя сильнее любой раскладки, включая перестановку.
        Assert.Empty(requests);

        // И дерево не тронуто: правка мимо CommitReorder была бы ровно тем
        // нарушением границы, ради которого запрос и заводился.
        Assert.Equal(1, panel.Children.IndexOf(action));
    }

    [AvaloniaFact]
    public void Indicator_Is_Shown_During_The_Gesture_And_Cleared_After()
    {
        var (harness, _) = Create();
        var action = harness.Find<Button>(0, "Action");
        var centre = harness.CentreOf(action);

        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseMove(centre + new Vector(4, 6));
        harness.Window.MouseMove(centre + new Vector(0, -60));
        harness.RunLayout();

        Assert.True(harness.Editor.IsReordering);
        Assert.True(harness.Editor.ReorderIndicator.Width > 0);

        harness.Window.MouseUp(centre + new Vector(0, -60), MouseButton.Left);
        harness.RunLayout();

        Assert.False(harness.Editor.IsReordering);
    }

    [AvaloniaFact]
    public void Dragging_Down_Reports_The_Post_Removal_Index()
    {
        var (harness, requests) = Create();
        var field = harness.Find<TextBox>(0, "Field");
        var action = harness.Find<Button>(0, "Action");
        var panel = PanelOf(field);

        Assert.Equal(0, panel.Children.IndexOf(field));

        // Движение вниз — единственный случай, где точка вставки и индекс
        // переноса расходятся: Move сначала удаляет, поэтому позиции правее
        // источника сдвигаются на одну. Вверх обе величины совпадают,
        // и до этого теста ветка не исполнялась ни разу.
        Drag(harness, harness.CentreOf(field), new Vector(0, 60));

        var request = Assert.Single(requests);
        Assert.Same(field, request.Target);
        Assert.Equal(0, request.OldIndex);
        Assert.Equal(1, request.NewIndex);

        // NewIndex — итоговая позиция, и Children.Move понимает его именно так.
        Assert.Equal(1, panel.Children.IndexOf(field));
        Assert.Equal(0, panel.Children.IndexOf(action));
    }

    [AvaloniaFact]
    public void The_Anchor_Names_The_Neighbour_Instead_Of_A_Slot()
    {
        // handle: false — иначе обработчик стенда пометит запрос выполненным
        // и обход подписчиков остановится на нём, не дойдя до этого.
        var (harness, _) = Create(handle: false);
        var field = harness.Find<TextBox>(0, "Field");
        var action = harness.Find<Button>(0, "Action");

        Control? anchor = null;
        var seen = false;
        harness.Editor.ReorderRequested += (_, e) =>
        {
            anchor = e.Anchor;
            seen = true;
        };

        Drag(harness, harness.CentreOf(action), new Vector(0, -60));

        Assert.True(seen);

        // Ссылка на соседа переживает любое представление дерева, индекс — нет.
        Assert.Same(field, anchor);
    }

    [AvaloniaFact]
    public void Without_A_Subscriber_The_Gesture_Does_Not_Start()
    {
        var harness = EditorHarness.CreateStackHosted();
        harness.PlaceContainer(0, CardLocation, CardSize);

        var action = harness.Find<Button>(0, "Action");
        var panel = PanelOf(action);

        var centre = harness.CentreOf(action);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseMove(centre + new Vector(4, 6));
        harness.Window.MouseMove(centre + new Vector(0, -60));
        harness.RunLayout();

        // Выполнить перестановку некому, поэтому редактор её и не обещает:
        // точка вставки не рисуется вовсе.
        Assert.False(harness.Editor.IsReordering);

        harness.Window.MouseUp(centre + new Vector(0, -60), MouseButton.Left);
        harness.RunLayout();

        Assert.Equal(1, panel.Children.IndexOf(action));
    }

    [AvaloniaFact]
    public void Only_The_First_Handler_Applies_The_Request()
    {
        var (harness, requests) = Create();
        var action = harness.Find<Button>(0, "Action");
        var field = harness.Find<TextBox>(0, "Field");
        var panel = PanelOf(action);

        // Второй подписчик той же формы, что и первый. Индексы сняты до правки,
        // поэтому применённые повторно они переставляют уже не тот контрол:
        // на двух обработчиках перестановка молча отменяла сама себя.
        var secondRan = false;
        harness.Editor.ReorderRequested += (_, e) =>
        {
            secondRan = true;

            if (e.Target.GetVisualParent() is Panel other)
                other.Children.Move(e.OldIndex, e.NewIndex);
        };

        Drag(harness, harness.CentreOf(action), new Vector(0, -60));

        Assert.Single(requests);
        Assert.False(secondRan);
        Assert.Equal(0, panel.Children.IndexOf(action));
        Assert.Equal(1, panel.Children.IndexOf(field));
    }

    [AvaloniaFact]
    public void A_Declined_Request_Leaves_The_Press_Unhandled()
    {
        var (harness, _) = Create(handle: false);
        var action = harness.Find<Button>(0, "Action");

        var released = 0;
        harness.Editor.AddHandler(
            InputElement.PointerReleasedEvent,
            (object? _, PointerReleasedEventArgs _) => released++,
            RoutingStrategies.Bubble);

        Drag(harness, harness.CentreOf(action), new Vector(0, -60));

        // Отказ обработчика обязан быть заметен: иначе Handled — свойство,
        // которое никто не читает, и его можно удалить, не сломав ни одного теста.
        Assert.Equal(1, released);
    }

    [AvaloniaFact]
    public void Selection_Drops_A_Target_The_Handler_Removed()
    {
        var harness = EditorHarness.CreateStackHosted();
        harness.PlaceContainer(0, CardLocation, CardSize);

        // Разметку можно не переставить, а пересобрать: библиотека разметки
        // владеет деревом и вправе выдать другое.
        harness.Editor.ReorderRequested += (_, e) =>
        {
            if (e.Target.GetVisualParent() is Panel panel)
            {
                panel.Children.Remove(e.Target);
                e.Handled = true;
            }
        };

        var action = harness.Find<Button>(0, "Action");
        Drag(harness, harness.CentreOf(action), new Vector(0, -60));

        // Рамка на контроле вне дерева рисовалась бы по его последним координатам
        // и принимала бы на него нюдж.
        Assert.DoesNotContain(
            harness.Editor.SelectedDesignTargets,
            target => ReferenceEquals(target.Target, action));
    }

    [AvaloniaFact]
    public void A_Removed_Target_No_Longer_Takes_The_Nudge()
    {
        var harness = EditorHarness.CreateStackHosted();
        harness.PlaceContainer(0, CardLocation, CardSize);

        harness.Editor.ReorderRequested += (_, e) =>
        {
            if (e.Target.GetVisualParent() is Panel panel)
            {
                panel.Children.Remove(e.Target);
                e.Handled = true;
            }
        };

        var action = harness.Find<Button>(0, "Action");
        Drag(harness, harness.CentreOf(action), new Vector(0, -60));

        var before = new Point(Layout.GetDesignX(action), Layout.GetDesignY(action));

        harness.Editor.Focus();
        harness.Window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        harness.RunLayout();

        // Публикуемый снимок выделения — не единственный слой: нюдж идёт по
        // внутреннему списку targets. Контрол, покинувший дерево, обязан выпасть
        // из обоих, иначе редактор продолжает писать геометрию в никуда.
        Assert.Equal(before, new Point(Layout.GetDesignX(action), Layout.GetDesignY(action)));
    }

    /// <summary>
    /// Стенд с переносом строк: шесть ячеек по 100x40 в панели шириной 300.
    /// </summary>
    private static (EditorHarness Harness, List<Request> Requests) CreateWrapped()
    {
        var nodes = new List<TestNode> { new("wrap0") };

        var editor = new DesignEditor
        {
            ItemsSource = nodes,
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<TestNode>((_, _) =>
            {
                var panel = new WrapPanel
                {
                    Name = "Cells",
                    Orientation = Orientation.Horizontal,
                    ItemWidth = 100,
                    ItemHeight = 40
                };

                for (var i = 0; i < 6; i++)
                {
                    var cell = new Border { Name = "Cell" + i };
                    Layout.SetIsTracked(cell, true);
                    panel.Children.Add(cell);
                }

                return panel;
            }, supportsRecycling: false)
        };

        var window = new Window { Width = 800, Height = 600, Content = editor };
        editor.InteractionOptions.IsSnapToGridEnabled = false;
        window.Show();

        var requests = new List<Request>();
        editor.ReorderRequested += (_, e) =>
        {
            requests.Add(new Request(e.Target, e.OldIndex, e.NewIndex));

            if (e.Target.GetVisualParent() is Panel panel)
            {
                panel.Children.Move(e.OldIndex, e.NewIndex);
                e.Handled = true;
            }
        };

        var harness = EditorHarness.Adopt(window, editor, nodes);
        harness.RunLayout();
        harness.PlaceContainer(0, new Point(100, 100), new Size(300, 100));
        return (harness, requests);
    }

    [AvaloniaFact]
    public void A_Stale_Insertion_Point_Is_Refused_Instead_Of_Coerced()
    {
        var (harness, requests) = CreateWrapped();
        var panel = harness.Find<WrapPanel>(0, "Cells");
        var first = harness.Named(0, "Cell0");
        var from = harness.CentreOf(first);

        // Целимся за последнюю ячейку: точка вставки — «в конец», то есть 6.
        harness.Window.MouseDown(from, MouseButton.Left);
        harness.Window.MouseMove(from + new Vector(6, 4));
        harness.Window.MouseMove(from + new Vector(240, 40));
        harness.RunLayout();

        // Дерево изменилось внутри жеста — так вправе поступить владелец разметки.
        panel.Children.RemoveAt(5);
        panel.Children.RemoveAt(4);
        harness.RunLayout();

        harness.Window.MouseUp(from + new Vector(240, 40), MouseButton.Left);
        harness.RunLayout();

        // Точка вставки описывает панель, которой больше нет. Подогнать её под
        // новый размер — значит поставить контрол туда, куда пользователь не
        // целился, и промолчать об этом. Запрос не отправляется вовсе.
        Assert.Empty(requests);
        Assert.Equal(0, panel.Children.IndexOf(first));
    }

    [AvaloniaFact]
    public void A_Drop_Into_The_Second_Row_Stays_In_That_Row()
    {
        var (harness, requests) = CreateWrapped();
        var panel = harness.Find<WrapPanel>(0, "Cells");
        var first = harness.Named(0, "Cell0");

        // Три ячейки в ряд, значит Cell3..Cell5 лежат во втором ряду.
        Assert.Equal(0, panel.Children.IndexOf(first));

        // Целимся между Cell4 и Cell5 — во втором ряду. Точка вставки, считанная
        // по одной оси потока, сравнивала бы X с серединами первого ряда
        // и уводила ячейку в чужой ряд: обе строки проецируются на один отрезок.
        Drag(harness, harness.CentreOf(first), new Vector(145, 40));

        var request = Assert.Single(requests);
        Assert.Equal(0, request.OldIndex);
        Assert.Equal(4, request.NewIndex);

        // Cell0 встала перед Cell5, а не в первый ряд.
        Assert.Equal(4, panel.Children.IndexOf(first));
        Assert.Equal(5, panel.Children.IndexOf(harness.Named(0, "Cell5")));
    }

    [AvaloniaFact]
    public void Absolute_Child_Still_Moves_Instead_Of_Reordering()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, CardLocation, new Size(300, 200));
        var nested = harness.Nested(0);

        var edits = new List<DesignEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);

        Drag(harness, harness.CentreOf(nested), DragDelta);

        // Раскладка с абсолютным позиционированием не превращается в перестановку,
        // и перемещение остаётся зоной редактора.
        var edit = Assert.Single(edits);
        Assert.Equal(DesignEditKind.Move, edit.Kind);
        Assert.IsType<DesignGeometryChange>(Assert.Single(edit.Changes));
    }

    // ---- Точка вставки не зависит от места захвата -------------------------------

    /// <summary>
    /// Стенд с узким соседом: колонка широких строк и прижатая влево кнопка в конце.
    /// </summary>
    /// <remarks>
    /// Именно так устроен экран сценария в демо: три строки во всю ширину и
    /// «+ Добавить действие» под ними. На стенде из одинаково растянутых детей дефект
    /// не воспроизводится — все центры стоят на одной вертикали.
    /// </remarks>
    private static EditorHarness CreateColumnWithNarrowTail()
    {
        var nodes = new List<TestNode> { new("rows0") };

        var editor = new DesignEditor
        {
            ItemsSource = nodes,
            ItemTemplate = new FuncDataTemplate<TestNode>((_, _) =>
            {
                var panel = new StackPanel { Name = "Rows", Spacing = 10 };

                for (var i = 0; i < 3; i++)
                {
                    var row = new Border { Name = "Row" + i, Height = 40, IsHitTestVisible = false };
                    DesignLayout.SetIsTracked(row, true);
                    panel.Children.Add(row);
                }

                var add = new Button
                {
                    Name = "Add",
                    Content = "+",
                    Width = 60,
                    Height = 30,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    IsHitTestVisible = false
                };

                DesignLayout.SetIsTracked(add, true);
                panel.Children.Add(add);
                DesignLayout.SetIsTracked(panel, true);
                return panel;
            }, supportsRecycling: false)
        };

        var window = new Window { Width = 800, Height = 600, Content = editor };
        editor.InteractionOptions.IsSnapToGridEnabled = false;
        editor.InteractionOptions.IsSnapToGuidesEnabled = false;
        window.Show();

        var harness = EditorHarness.Adopt(window, editor, nodes);
        harness.RunLayout();
        harness.PlaceContainer(0, new Point(100, 100), new Size(400, 300));
        return harness;
    }

    /// <summary>
    /// Точка вставки не зависит от того, за какое место схватили элемент.
    /// </summary>
    /// <remarks>
    /// Ближайший сосед искался по расстоянию до его <b>центра</b>, и поперечная ось
    /// соревновалась с продольной. Узкая кнопка внизу прижата влево, её центр по X
    /// близок к левому краю строк — указатель у левого края оказывался ближе к ней,
    /// чем к центрам самих строк, и точка вставки уезжала вниз. Взятый за правый край
    /// тот же элемент вставал верно.
    /// <para>
    /// Замер до правки: одна и та же протяжка вниз на 55 давала индекс 2 от левого края
    /// и 1 от центра и от правого.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void The_Insertion_Point_Ignores_Where_The_Element_Was_Grabbed()
    {
        var indices = new List<int>();

        foreach (var grab in new[] { 6.0, 0.5, -6.0 })
        {
            var harness = CreateColumnWithNarrowTail();
            harness.Editor.ReorderRequested += (_, e) =>
            {
                indices.Add(e.NewIndex);
                e.Handled = true;
            };

            var row = harness.Find<Border>(0, "Row0");
            var origin = harness.Editor.GetDesignPosition(row);
            var size = harness.Editor.GetDesignSize(row);

            // 6 от левого края, середина, 6 от правого.
            var x = grab switch
            {
                > 1 => origin.X + grab,
                < 0 => origin.X + size.Width + grab,
                _ => origin.X + (size.Width / 2)
            };

            var from = new Point(x, origin.Y + (size.Height / 2));
            harness.Window.MouseDown(from, MouseButton.Left);
            harness.Window.MouseMove(from + new Vector(0, 6));
            harness.Window.MouseMove(from + new Vector(0, 55));
            harness.Window.MouseUp(from + new Vector(0, 55), MouseButton.Left);
            harness.RunLayout();
        }

        Assert.Equal(3, indices.Count);
        Assert.Equal(indices[0], indices[1]);
        Assert.Equal(indices[1], indices[2]);
    }
}
