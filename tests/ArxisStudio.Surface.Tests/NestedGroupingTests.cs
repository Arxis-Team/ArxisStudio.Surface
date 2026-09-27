using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ArxisStudio.Surface.UiDesigner;
using Xunit;
using DesignLayout = ArxisStudio.Surface.UiDesigner.Layout;
using ArxisStudio.Surface;
using ArxisStudio.Surface.Editing;

namespace ArxisStudio.Tests;

/// <summary>
/// Группа внутри группы: путь вместо плоской пометки и рамка на кластер.
/// </summary>
/// <remarks>
/// Стенд свой, на четыре контрола: у общего <see cref="EditorHarness"/> их два, а вложенность
/// на двух не показать — нужен и состав группы, и сосед за её пределами.
/// </remarks>
public class NestedGroupingTests
{
    private const double CellWidth = 60;
    private const double CellHeight = 40;

    private static readonly string[] Names = { "A", "B", "C", "D" };

    private static EditorHarness Create()
    {
        var nodes = new List<TestNode> { new("form0") };

        var editor = new DesignEditor
        {
            ItemsSource = nodes,
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<TestNode>((_, _) =>
            {
                var panel = new AbsolutePanel();

                for (var i = 0; i < Names.Length; i++)
                {
                    var cell = new Border
                    {
                        Name = Names[i],
                        Width = CellWidth,
                        Height = CellHeight,
                        Background = Brushes.Transparent
                    };

                    DesignLayout.SetX(cell, 10 + (i * 90));
                    DesignLayout.SetY(cell, 10);
                    panel.Children.Add(cell);
                }

                return panel;
            }, supportsRecycling: false)
        };

        var window = new Window { Width = 800, Height = 600, Content = editor };
        editor.InteractionOptions.IsSnapToGridEnabled = false;
        editor.InteractionOptions.IsSnapToGuidesEnabled = false;
        window.Show();

        var harness = EditorHarness.Adopt(window, editor, nodes);
        harness.RunLayout();
        harness.PlaceContainer(0, new Point(100, 100), new Size(400, 150));
        return harness;
    }

    private static Border Cell(EditorHarness harness, string name) => harness.Named(0, name);

    private static void Click(EditorHarness harness, string name, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var point = harness.CentreOf(Cell(harness, name));
        harness.Window.MouseDown(point, MouseButton.Left, modifiers);
        harness.Window.MouseUp(point, MouseButton.Left, modifiers);
        harness.RunLayout();
    }

    private static void Select(EditorHarness harness, params string[] names)
    {
        for (var i = 0; i < names.Length; i++)
            Click(harness, names[i], i == 0 ? RawInputModifiers.None : RawInputModifiers.Shift);
    }

    /// <summary>Собирает группу из A и B.</summary>
    private static EditorHarness CreateWithGroup()
    {
        var harness = Create();
        Select(harness, "A", "B");
        Assert.True(harness.Editor.GroupSelection());
        harness.RunLayout();
        return harness;
    }

    private static string? PathOf(EditorHarness harness, string name) => DesignGroup.GetId(Cell(harness, name));

    // ---- Вложенность ------------------------------------------------------------

    /// <summary>
    /// Группа плюс сосед дают группу внутри группы, а не растворение прежней.
    /// </summary>
    /// <remarks>
    /// Пометка — путь от внешней группы к внутренней, поэтому прежний идентификатор
    /// остаётся хвостом. Плоская модель на этом месте переписывала участникам
    /// идентификатор целиком, и внутренняя группа исчезала.
    /// </remarks>
    [AvaloniaFact]
    public void Grouping_A_Group_With_A_Neighbour_Nests_It()
    {
        var harness = CreateWithGroup();
        var inner = PathOf(harness, "A")!;

        // Клик по участнику выбирает всю группу, Shift добавляет соседа.
        Select(harness, "A", "C");
        Assert.True(harness.Editor.GroupSelection());
        harness.RunLayout();

        var outer = PathOf(harness, "C")!;
        Assert.NotEqual(inner, outer);
        Assert.Equal(outer + "/" + inner, PathOf(harness, "A"));
        Assert.Equal(outer + "/" + inner, PathOf(harness, "B"));
        Assert.Null(PathOf(harness, "D"));
    }

    /// <summary>
    /// Выбранная группа и сосед рисуются двумя рамками.
    /// </summary>
    /// <remarks>
    /// Кластер — это то, что пользователь видит одной рамкой: группа целиком либо
    /// одиночный контрол. Плоская модель на этом месте показывала три рамки, то есть
    /// обещала три независимых элемента там, где их два.
    /// </remarks>
    [AvaloniaFact]
    public void A_Selected_Group_And_A_Neighbour_Draw_Two_Frames()
    {
        var harness = CreateWithGroup();

        Select(harness, "A", "C");

        var adorners = harness.Editor.SecondarySelectionAdorners;
        Assert.Equal(2, adorners.Count);

        var groupFrame = Assert.Single(adorners, a => a.Role == SelectionAdornerRole.Group);
        var expected = DesignBoundsOf(harness, "A").Union(DesignBoundsOf(harness, "B"));
        Assert.Equal(expected, groupFrame.Bounds);

        var loneFrame = Assert.Single(adorners, a => a.Role != SelectionAdornerRole.Group);
        Assert.Same(Cell(harness, "C"), loneFrame.Target);
    }

    private static Rect DesignBoundsOf(EditorHarness harness, string name)
    {
        Assert.True(harness.Editor.TryGetDesignBounds(Cell(harness, name), out var bounds));
        return bounds;
    }

    /// <summary>Собирает внешнюю группу из группы A+B и соседа C.</summary>
    private static EditorHarness CreateNested(out string outer, out string inner)
    {
        var harness = CreateWithGroup();
        Select(harness, "A", "C");
        Assert.True(harness.Editor.GroupSelection());
        harness.RunLayout();

        outer = PathOf(harness, "C")!;
        inner = PathOf(harness, "A")!;
        return harness;
    }

    // ---- Чтение и операции ------------------------------------------------------

    [AvaloniaFact]
    public void Nested_Members_Read_Back_As_A_Tree()
    {
        var harness = CreateNested(out var outer, out var inner);
        var container = harness.Container(0);

        var root = Assert.Single(harness.Editor.GetGroups(container));
        Assert.Equal(outer, root.Path);
        Assert.Equal(outer, root.Id);
        Assert.Equal(new Control[] { Cell(harness, "C") }, root.Members);

        var nested = Assert.Single(root.Groups);
        Assert.Equal(inner, nested.Path);
        Assert.Equal(new Control[] { Cell(harness, "A"), Cell(harness, "B") }, nested.Members);

        // Весь состав отдаёт GetGroupMembers, включая вложенные уровни.
        Assert.Equal(
            new Control[] { Cell(harness, "A"), Cell(harness, "B"), Cell(harness, "C") },
            harness.Editor.GetGroupMembers(container, outer));
    }

    /// <summary>
    /// Роспуск снимает один внешний уровень.
    /// </summary>
    /// <remarks>
    /// Вложенная группа переживает его и поднимается на уровень выше: дробить заодно
    /// и её значило бы разрушить структуру, которую собирали отдельным действием.
    /// </remarks>
    [AvaloniaFact]
    public void Ungrouping_Removes_Only_The_Outer_Level()
    {
        var harness = CreateNested(out _, out var inner);
        var leaf = inner.Substring(inner.IndexOf('/') + 1);

        Click(harness, "A");
        Assert.True(harness.Editor.UngroupSelection());
        harness.RunLayout();

        Assert.Equal(leaf, PathOf(harness, "A"));
        Assert.Equal(leaf, PathOf(harness, "B"));
        Assert.Null(PathOf(harness, "C"));
    }

    [AvaloniaFact]
    public void Renaming_Renames_One_Segment()
    {
        var harness = CreateNested(out var outer, out var inner);

        Assert.True(harness.Editor.RenameGroup(harness.Container(0), inner, "left"));
        harness.RunLayout();

        Assert.Equal(outer + "/left", PathOf(harness, "A"));
        Assert.Equal(outer + "/left", PathOf(harness, "B"));
        Assert.Equal(outer, PathOf(harness, "C"));
    }

    /// <summary>
    /// Переезд на имя, занятое братом, отклоняется.
    /// </summary>
    /// <remarks>
    /// Личность группы — путь целиком, поэтому одинаковые имена на разных ветках
    /// не сталкиваются, а под одним родителем — сливают две группы в одну.
    /// </remarks>
    [AvaloniaFact]
    public void A_Sibling_Segment_Is_Refused()
    {
        var harness = CreateNested(out var outer, out var inner);
        DesignGroup.SetId(Cell(harness, "D"), outer + "/left");

        Assert.False(harness.Editor.RenameGroup(harness.Container(0), inner, "left"));
        Assert.Equal(inner, PathOf(harness, "A"));
    }

    // ---- Выделение --------------------------------------------------------------

    /// <summary>
    /// Повторный клик по тому же месту спускается ровно на один уровень.
    /// </summary>
    /// <remarks>
    /// Первый клик берёт самую внешнюю группу, второй входит в неё и берёт вложенную
    /// целиком, третий — сам контрол. Проваливаться сразу до контрола нельзя: тогда
    /// промежуточные уровни указателем не выбрать, а вход в группу ради работы внутри
    /// неё и заводился.
    /// </remarks>
    [AvaloniaFact]
    public void Clicking_Again_Descends_One_Level()
    {
        var harness = CreateNested(out _, out _);

        Click(harness, "A");
        Assert.Equal(3, harness.Editor.SelectedTargetsCount);

        Click(harness, "A");
        Assert.Equal(2, harness.Editor.SelectedTargetsCount);

        Click(harness, "A");
        Assert.Equal(1, harness.Editor.SelectedTargetsCount);
    }

    /// <summary>
    /// Наполовину выбранная группа кластером не становится.
    /// </summary>
    /// <remarks>
    /// Рамка группы обещает жест над всем её составом. Выбрать часть указателем нельзя,
    /// но можно публичным <c>SelectTarget</c> — и тогда рамка соврала бы.
    /// </remarks>
    [AvaloniaFact]
    public void A_Partly_Selected_Group_Is_Not_A_Cluster()
    {
        var harness = CreateWithGroup();

        harness.Editor.SelectTarget(Cell(harness, "A"), additive: false);
        harness.Editor.SelectTarget(Cell(harness, "C"), additive: true);
        harness.RunLayout();

        var adorners = harness.Editor.SecondarySelectionAdorners;
        Assert.Equal(2, adorners.Count);
        Assert.DoesNotContain(adorners, a => a.Role == SelectionAdornerRole.Group);
    }

    // ---- Жест -------------------------------------------------------------------

    /// <summary>
    /// Ручки рамки кластера масштабируют только его состав.
    /// </summary>
    /// <remarks>
    /// Группа и внутри жеста остаётся одним элементом: сосед, выбранный рядом, своей
    /// рамкой и своими ручками распоряжается сам.
    /// </remarks>
    [AvaloniaFact]
    public void Resizing_A_Group_Cluster_Scales_Only_Its_Members()
    {
        var harness = CreateWithGroup();
        Select(harness, "A", "C");

        var before = DesignBoundsOf(harness, "C");
        var frame = DesignBoundsOf(harness, "A").Union(DesignBoundsOf(harness, "B"));

        var adorner = harness.Editor.GetVisualDescendants()
            .OfType<SelectionAdornerLayer>()
            .SelectMany(layer => layer.GetVisualChildren().OfType<SelectionAdorner>())
            .Single(a => a.Role == SelectionAdornerRole.Group);

        var delta = new Vector(30, 0);
        adorner.RaiseEvent(new ResizeStartedEventArgs(default, ResizeDirection.Right, SelectionAdorner.ResizeStartedEvent));
        adorner.RaiseEvent(new ResizeDeltaEventArgs(delta, ResizeDirection.Right, SelectionAdorner.ResizeDeltaEvent));
        adorner.RaiseEvent(new VectorEventArgs { RoutedEvent = SelectionAdorner.ResizeCompletedEvent, Vector = delta });
        harness.RunLayout();

        Assert.Equal(before, DesignBoundsOf(harness, "C"));
        Assert.True(DesignBoundsOf(harness, "A").Union(DesignBoundsOf(harness, "B")).Width > frame.Width);
    }

    /// <summary>Собирает две отдельные группы: A+B и C+D.</summary>
    private static EditorHarness CreateTwoGroups()
    {
        var harness = CreateWithGroup();
        Select(harness, "C", "D");
        Assert.True(harness.Editor.GroupSelection());
        harness.RunLayout();
        return harness;
    }

    /// <summary>
    /// Shift-клик по чужой группе добавляет её целиком.
    /// </summary>
    /// <remarks>
    /// Раскрытие до кластера — свойство клика по участнику, а не свойство замены
    /// выделения: группа остаётся одним элементом и когда её добавляют к уже
    /// выбранному. Иначе к группе добавлялся бы один контрол из другой, и рамок
    /// на экране становилось бы три вместо двух.
    /// </remarks>
    [AvaloniaFact]
    public void Shift_Click_On_Another_Group_Adds_It_Whole()
    {
        var harness = CreateTwoGroups();

        Click(harness, "A");
        Assert.Equal(2, harness.Editor.SelectedTargetsCount);

        Click(harness, "C", RawInputModifiers.Shift);

        Assert.Equal(4, harness.Editor.SelectedTargetsCount);
        Assert.Equal(2, harness.Editor.SecondarySelectionAdorners.Count);
        Assert.All(
            harness.Editor.SecondarySelectionAdorners,
            adorner => Assert.Equal(SelectionAdornerRole.Group, adorner.Role));
    }

    /// <summary>
    /// Повторный Shift-клик убирает группу целиком.
    /// </summary>
    /// <remarks>
    /// Обратная сторона того же правила: добавляли группой — убирать половиной нельзя,
    /// иначе от неё осталась бы часть, которую пользователь не выбирал.
    /// </remarks>
    [AvaloniaFact]
    public void Shift_Click_On_A_Selected_Group_Removes_It_Whole()
    {
        var harness = CreateTwoGroups();

        Click(harness, "A");
        Click(harness, "C", RawInputModifiers.Shift);
        Assert.Equal(4, harness.Editor.SelectedTargetsCount);

        Click(harness, "D", RawInputModifiers.Shift);

        Assert.Equal(2, harness.Editor.SelectedTargetsCount);
        Assert.True(harness.Editor.HasGroupSelection);
    }

    /// <summary>
    /// Контекстное меню на одной из двух выбранных групп сохраняет обе.
    /// </summary>
    /// <remarks>
    /// Правый клик переводит выделение, только когда щёлкнули мимо него. Иначе выбор
    /// схлопывался до группы под курсором, и сгруппировать две группы через меню было
    /// нечем — команда становилась недоступной ровно в тот момент, когда её вызывают.
    /// </remarks>
    [AvaloniaFact]
    public void A_Context_Click_On_One_Of_Two_Groups_Keeps_Both()
    {
        var harness = CreateTwoGroups();

        Click(harness, "A");
        Click(harness, "C", RawInputModifiers.Shift);
        Assert.Equal(4, harness.Editor.SelectedTargetsCount);

        var point = harness.CentreOf(Cell(harness, "C"));
        harness.Window.MouseDown(point, MouseButton.Right);
        harness.Window.MouseUp(point, MouseButton.Right);
        harness.RunLayout();

        Assert.Equal(4, harness.Editor.SelectedTargetsCount);
        Assert.True(harness.Editor.CanGroupSelection());
        Assert.True(harness.Editor.GroupSelection());
    }

    /// <summary>Собирает три уровня: group-3 { group-1 { A, B }, group-2 { C, D } }.</summary>
    private static EditorHarness CreateTwoLevels(out string outer)
    {
        var harness = CreateTwoGroups();

        Click(harness, "A");
        Click(harness, "C", RawInputModifiers.Shift);
        Assert.True(harness.Editor.GroupSelection());
        harness.RunLayout();

        outer = DesignGroupPath.Split(PathOf(harness, "A"))[0];
        return harness;
    }

    /// <summary>Выбирает состав группы так, как это делает панель групп: по одному target'у.</summary>
    private static void SelectByApi(EditorHarness harness, string path)
    {
        var members = harness.Editor.GetGroupMembers(harness.Container(0), path);
        for (var i = 0; i < members.Count; i++)
            harness.Editor.SelectTarget(members[i], additive: i > 0);

        harness.RunLayout();
    }

    /// <summary>
    /// Выбор состава группы показывает её одной рамкой, куда бы ни был открыт вход.
    /// </summary>
    /// <remarks>
    /// Вход в группу — состояние указателя, и он переживает выбор, сделанный снаружи:
    /// панель групп выбирает состав по одному target'у и об открытом уровне не знает.
    /// Оставшийся глубже уровень разбивал выделение на отдельные контролы — вместо
    /// одной рамки пользователь получал четыре.
    /// </remarks>
    [AvaloniaFact]
    public void Selecting_A_Group_Shows_One_Frame_However_Deep_The_Entry_Was()
    {
        var harness = CreateTwoLevels(out var outer);

        // Входим двумя уровнями внутрь: сначала в group-3, затем в group-1.
        Click(harness, "A");
        Click(harness, "A");
        Click(harness, "A");
        Assert.Equal(1, harness.Editor.SelectedTargetsCount);

        SelectByApi(harness, outer);

        Assert.Equal(4, harness.Editor.SelectedTargetsCount);
        Assert.True(harness.Editor.HasGroupSelection);
    }

    /// <summary>
    /// Выбор состава вложенной группы тоже показывает её одной рамкой.
    /// </summary>
    /// <remarks>
    /// Уровень показа задаёт сама форма выделения: выбран ровно состав группы — значит
    /// смотрим на неё снаружи. Иначе клик по строке вложенной группы в панели рисовал бы
    /// её участников поодиночке, потому что вход стоял ровно на ней.
    /// </remarks>
    [AvaloniaFact]
    public void Selecting_A_Nested_Group_Shows_One_Frame()
    {
        var harness = CreateTwoLevels(out _);
        var inner = PathOf(harness, "A")!;

        Click(harness, "A");
        Click(harness, "A");
        Click(harness, "A");

        SelectByApi(harness, inner);

        Assert.Equal(2, harness.Editor.SelectedTargetsCount);
        Assert.True(harness.Editor.HasGroupSelection);
    }

    /// <summary>
    /// Участник, вытащенный из группы, выходит из неё, а не уносит её уровень с собой.
    /// </summary>
    /// <remarks>
    /// Хвост пути сохраняется ради вложенности — у кластера-группы, которая входит
    /// в новую целиком. Одиночный контрол группой не является, и прежний уровень у него
    /// превращался в фантомную группу с тем же именем, что и настоящая.
    /// </remarks>
    [AvaloniaFact]
    public void A_Member_Pulled_Out_Of_A_Group_Leaves_It()
    {
        var harness = CreateWithGroup();
        var inner = PathOf(harness, "A")!;

        // Клик по строке участника в панели выбирает один контрол из группы.
        harness.Editor.SelectTarget(Cell(harness, "A"));
        harness.Editor.SelectTarget(Cell(harness, "C"), additive: true);
        harness.RunLayout();

        Assert.True(harness.Editor.GroupSelection());
        harness.RunLayout();

        var outer = PathOf(harness, "C")!;
        Assert.Equal(outer, PathOf(harness, "A"));
        Assert.Equal(inner, PathOf(harness, "B"));
        Assert.DoesNotContain(DesignGroupPath.Separator, PathOf(harness, "A")!);
    }

    /// <summary>
    /// Переименование внешнего уровня перестраивает пути всех потомков.
    /// </summary>
    /// <remarks>
    /// У вложенного уровня переименование сводится к замене последнего сегмента,
    /// и ошибка в пересборке хвоста там не видна.
    /// </remarks>
    [AvaloniaFact]
    public void Renaming_The_Outer_Level_Rebases_Descendants()
    {
        var harness = CreateNested(out var outer, out var inner);
        var leaf = DesignGroupPath.Leaf(inner);

        Assert.True(harness.Editor.RenameGroup(harness.Container(0), outer, "screen"));
        harness.RunLayout();

        Assert.Equal("screen/" + leaf, PathOf(harness, "A"));
        Assert.Equal("screen/" + leaf, PathOf(harness, "B"));
        Assert.Equal("screen", PathOf(harness, "C"));
    }

    /// <summary>
    /// Пробелы вокруг имени обрезаются на шве записи.
    /// </summary>
    /// <remarks>
    /// Правка имени на месте легко оставляет пробел, а пути сравниваются Ordinal:
    /// « toolbar » и «toolbar» стали бы двумя внешне неотличимыми группами.
    /// </remarks>
    [AvaloniaFact]
    public void Renaming_Trims_The_Identifier()
    {
        var harness = CreateWithGroup();
        var id = PathOf(harness, "A")!;

        Assert.True(harness.Editor.RenameGroup(harness.Container(0), id, "  toolbar  "));
        harness.RunLayout();

        Assert.Equal("toolbar", PathOf(harness, "A"));
    }

    private static SelectionAdorner ClusterAdorner(EditorHarness harness, bool group) =>
        harness.Editor.GetVisualDescendants()
            .OfType<SelectionAdornerLayer>()
            .SelectMany(layer => layer.GetVisualChildren().OfType<SelectionAdorner>())
            .Single(a => (a.Role == SelectionAdornerRole.Group) == group);

    private static void Resize(SelectionAdorner adorner, Vector delta)
    {
        adorner.RaiseEvent(new ResizeStartedEventArgs(default, ResizeDirection.Right, SelectionAdorner.ResizeStartedEvent));
        adorner.RaiseEvent(new ResizeDeltaEventArgs(delta, ResizeDirection.Right, SelectionAdorner.ResizeDeltaEvent));
        adorner.RaiseEvent(new VectorEventArgs { RoutedEvent = SelectionAdorner.ResizeCompletedEvent, Vector = delta });
    }

    /// <summary>Начинает жест кластера и не заканчивает его.</summary>
    private static void StartClusterResize(EditorHarness harness)
    {
        var adorner = ClusterAdorner(harness, group: true);
        adorner.RaiseEvent(new ResizeStartedEventArgs(default, ResizeDirection.Right, SelectionAdorner.ResizeStartedEvent));
        adorner.RaiseEvent(new ResizeDeltaEventArgs(new Vector(20, 0), ResizeDirection.Right, SelectionAdorner.ResizeDeltaEvent));
    }

    /// <summary>
    /// Потеря захвата закрывает групповой жест и фиксирует его единицу редактирования.
    /// </summary>
    /// <remarks>
    /// Геометрия к этому моменту уже применена, поэтому единица именно фиксируется:
    /// поздняя запись хуже своевременной и несравнимо лучше потерянной. Заодно снимается
    /// сама операция — иначе она пережила бы жест.
    /// </remarks>
    [AvaloniaFact]
    public void A_Dropped_Cluster_Resize_Commits_Its_Edit()
    {
        var harness = CreateWithGroup();
        Select(harness, "A", "C");

        var edits = new List<SurfaceEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);

        StartClusterResize(harness);
        harness.Editor.RaiseEvent(new PointerCaptureLostEventArgs(
            harness.Editor,
            new Pointer(1, PointerType.Mouse, isPrimary: true)));
        harness.RunLayout();

        var edit = Assert.Single(edits);
        Assert.Equal(SurfaceEditKind.Resize, edit.Kind);
    }

    /// <summary>
    /// Рамка одиночного контрола тянет только его, даже если групповой жест не закрыт.
    /// </summary>
    /// <remarks>
    /// Обработчики вторичных адорнеров ветвятся по составу адорнера — тому же условию,
    /// что и на входе в жест. Пока они смотрели на само поле операции, незакрытая
    /// операция кластера перехватывала чужой жест: тянули одну рамку, менялся другой набор.
    /// </remarks>
    [AvaloniaFact]
    public void A_Lone_Adorner_Resizes_Only_Its_Own_Target()
    {
        var harness = CreateWithGroup();
        Select(harness, "A", "C");

        StartClusterResize(harness);
        harness.RunLayout();

        // Читать design-координаты можно только после layout-прохода: они отстают
        // на один проход диспетчера.
        var beforeA = DesignBoundsOf(harness, "A");
        var beforeB = DesignBoundsOf(harness, "B");

        Resize(ClusterAdorner(harness, group: false), new Vector(30, 0));
        harness.RunLayout();

        Assert.Equal(beforeA, DesignBoundsOf(harness, "A"));
        Assert.Equal(beforeB, DesignBoundsOf(harness, "B"));
        Assert.True(DesignBoundsOf(harness, "C").Width > CellWidth);
    }
}
