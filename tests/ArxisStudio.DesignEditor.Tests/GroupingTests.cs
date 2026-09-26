using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using ArxisStudio.Controls;
using Xunit;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Surface;
using ArxisStudio.Surface.Editing;

namespace ArxisStudio.Tests;

/// <summary>
/// Design-time группы: пометка на контролах, одна рамка, один жест.
/// </summary>
/// <remarks>
/// Группа не создаёт узла в дереве — редактор его не правит (ADR 0001), — а помечает
/// участников attached-свойством <see cref="DesignGroup"/>. Отсюда всё остальное: пометка
/// живёт там же, где <c>Layout.X</c>/<c>Y</c>, и сохраняется хостом тем же способом;
/// у группировки есть шов записи, поэтому она попадает в контракт изменений и отменяется.
/// </remarks>
public class GroupingTests
{
    private static readonly Point ContainerLocation = new(100, 100);
    private static readonly Size ContainerSize = new(200, 150);

    private static Point NestedCentre => new(
        ContainerLocation.X + EditorHarness.NestedOffset + (EditorHarness.NestedWidth / 2),
        ContainerLocation.Y + EditorHarness.NestedOffset + (EditorHarness.NestedHeight / 2));

    private static Point SiblingCentre => new(
        ContainerLocation.X + EditorHarness.SiblingOffset + (EditorHarness.NestedWidth / 2),
        ContainerLocation.Y + EditorHarness.NestedOffset + (EditorHarness.NestedHeight / 2));

    private static EditorHarness Create(int nodeCount = 1)
    {
        var harness = EditorHarness.Create(nodeCount: nodeCount);
        for (var i = 0; i < nodeCount; i++)
            harness.PlaceContainer(i, ContainerLocation + new Vector(i * 320, 0), ContainerSize);

        return harness;
    }

    private static void Click(EditorHarness harness, Point point, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        harness.Window.MouseDown(point, MouseButton.Left, modifiers);
        harness.Window.MouseUp(point, MouseButton.Left, modifiers);
        harness.RunLayout();
    }

    /// <summary>Собирает группу из двух соседей одной формы.</summary>
    private static EditorHarness CreateGrouped()
    {
        var harness = Create();
        Click(harness, NestedCentre);
        Click(harness, SiblingCentre, RawInputModifiers.Shift);

        Assert.True(harness.Editor.GroupSelection());
        harness.RunLayout();
        return harness;
    }

    // ---- Сборка группы ----------------------------------------------------------

    [AvaloniaFact]
    public void Grouping_Marks_Every_Member()
    {
        var harness = CreateGrouped();

        var id = DesignGroup.GetId(harness.Nested(0));
        Assert.NotNull(id);
        Assert.Equal(id, DesignGroup.GetId(harness.Named(0, "Sibling")));
    }

    [AvaloniaFact]
    public void A_Single_Target_Is_Not_A_Group()
    {
        var harness = Create();
        Click(harness, NestedCentre);

        Assert.False(harness.Editor.CanGroupSelection());
        Assert.False(harness.Editor.GroupSelection());
        Assert.Null(DesignGroup.GetId(harness.Nested(0)));
    }

    /// <summary>
    /// Группа не собирается поперёк форм.
    /// </summary>
    /// <remarks>
    /// Выделение двухслойно: индексный слой адресует формы, слой target'ов — контролы.
    /// Группа из двух форм разошлась бы с индексным слоем, и рамка обещала бы жест,
    /// который к половине участников не применяется.
    /// </remarks>
    [AvaloniaFact]
    public void A_Group_Does_Not_Span_Two_Forms()
    {
        var harness = Create(nodeCount: 2);

        Click(harness, NestedCentre);
        Click(harness, NestedCentre + new Vector(320, 0), RawInputModifiers.Shift);

        // Группу поперёк форм не из чего собрать: слой выделения такой набор не строит
        // вовсе — добавление к выделению требует общего design host. Проверять надо
        // именно это, а не отказ группировки: до него дело просто не доходит.
        Assert.Equal(1, harness.Editor.SelectedDesignTargetsCount);
        Assert.False(harness.Editor.GroupSelection());
        Assert.Null(DesignGroup.GetId(harness.Nested(0)));
        Assert.Null(DesignGroup.GetId(harness.Nested(1)));
    }

    /// <summary>
    /// Выбранная целиком группа сама по себе не группируется.
    /// </summary>
    /// <remarks>
    /// Считаются <b>кластеры</b>, а не target'ы: группа — это один кластер, и новый
    /// уровень над ней в одиночку ничего бы не разделял. Плоская модель на этом месте
    /// просто выдавала группе новый идентификатор — другого способа «перегруппировать»
    /// она не знала; вложенность делает это осмысленным только вместе с соседом.
    /// </remarks>
    [AvaloniaFact]
    public void A_Whole_Group_Alone_Is_Not_Regrouped()
    {
        var harness = CreateGrouped();
        var id = DesignGroup.GetId(harness.Nested(0));

        Click(harness, NestedCentre);
        Assert.True(harness.Editor.HasGroupSelection);

        Assert.False(harness.Editor.GroupSelection());
        harness.RunLayout();

        Assert.Equal(id, DesignGroup.GetId(harness.Nested(0)));
        Assert.Equal(id, DesignGroup.GetId(harness.Named(0, "Sibling")));
    }

    // ---- Одна рамка -------------------------------------------------------------

    /// <summary>
    /// У группы рисуется ровно один adorner.
    /// </summary>
    /// <remarks>
    /// Это и есть видимая часть группировки: контуры участников по отдельности говорили бы,
    /// что элементов несколько, а жест применяется к ним как к одному.
    /// </remarks>
    [AvaloniaFact]
    public void A_Group_Draws_One_Adorner()
    {
        var harness = CreateGrouped();

        Assert.True(harness.Editor.HasGroupSelection);
        Assert.Empty(harness.Editor.SecondarySelectionAdorners);
        Assert.False(harness.Editor.HasMultipleNestedSelection);
    }

    /// <summary>
    /// Рамка группы накрывает всех участников.
    /// </summary>
    [AvaloniaFact]
    public void The_Frame_Covers_Every_Member()
    {
        var harness = CreateGrouped();

        var bounds = harness.Editor.SelectionBounds;
        var nested = harness.Editor.GetDesignPosition(harness.Nested(0));
        var sibling = harness.Editor.GetDesignPosition(harness.Named(0, "Sibling"));

        Assert.Equal(nested.X, bounds.X, 1);
        Assert.Equal(sibling.X + EditorHarness.NestedWidth, bounds.Right, 1);
    }

    /// <summary>
    /// После роспуска контуры участников возвращаются.
    /// </summary>
    [AvaloniaFact]
    public void Ungrouping_Brings_The_Adorners_Back()
    {
        var harness = CreateGrouped();

        Assert.True(harness.Editor.UngroupSelection());
        harness.RunLayout();

        Assert.False(harness.Editor.HasGroupSelection);
        Assert.True(harness.Editor.HasMultipleNestedSelection);
        Assert.Equal(2, harness.Editor.SecondarySelectionAdorners.Count);
        Assert.Null(DesignGroup.GetId(harness.Nested(0)));
        Assert.Null(DesignGroup.GetId(harness.Named(0, "Sibling")));
    }

    // ---- Выделение --------------------------------------------------------------

    /// <summary>
    /// Клик по участнику выбирает всю группу.
    /// </summary>
    [AvaloniaFact]
    public void Clicking_A_Member_Selects_The_Whole_Group()
    {
        var harness = CreateGrouped();
        Click(harness, new Point(600, 500));
        Assert.Empty(harness.Editor.SelectedDesignTargets);

        Click(harness, NestedCentre);

        Assert.Equal(2, harness.Editor.SelectedDesignTargetsCount);
        Assert.True(harness.Editor.HasGroupSelection);
    }

    /// <summary>
    /// Двойной клик входит в группу и выбирает один контрол.
    /// </summary>
    /// <remarks>
    /// Без входа отредактировать участника можно было бы только распустив группу.
    /// Так устроены Figma и Blend, и ожидание у пользователя именно это.
    /// </remarks>
    [AvaloniaFact]
    public void A_Double_Click_Enters_The_Group()
    {
        var harness = CreateGrouped();

        harness.Window.MouseDown(NestedCentre, MouseButton.Left);
        harness.Window.MouseUp(NestedCentre, MouseButton.Left);
        harness.Window.MouseDown(NestedCentre, MouseButton.Left);
        harness.Window.MouseUp(NestedCentre, MouseButton.Left);
        harness.RunLayout();

        Assert.Equal(1, harness.Editor.SelectedDesignTargetsCount);
        Assert.False(harness.Editor.HasGroupSelection);
        Assert.Same(harness.Nested(0), harness.Editor.PrimarySelectionTarget!.Target);
    }

    /// <summary>
    /// Клик мимо группы закрывает её обратно.
    /// </summary>
    /// <remarks>
    /// Открытая навсегда группа — это режим, из которого пользователь не знает, как выйти.
    /// </remarks>
    [AvaloniaFact]
    public void Leaving_The_Group_Closes_It()
    {
        var harness = CreateGrouped();

        harness.Window.MouseDown(NestedCentre, MouseButton.Left);
        harness.Window.MouseUp(NestedCentre, MouseButton.Left);
        harness.Window.MouseDown(NestedCentre, MouseButton.Left);
        harness.Window.MouseUp(NestedCentre, MouseButton.Left);
        harness.RunLayout();
        Assert.Equal(1, harness.Editor.SelectedDesignTargetsCount);

        Click(harness, new Point(600, 500));
        Click(harness, NestedCentre);

        Assert.Equal(2, harness.Editor.SelectedDesignTargetsCount);
        Assert.True(harness.Editor.HasGroupSelection);
    }

    // ---- Жесты ------------------------------------------------------------------

    /// <summary>
    /// Группа перетаскивается как одно.
    /// </summary>
    [AvaloniaFact]
    public void Dragging_A_Member_Moves_The_Whole_Group()
    {
        var harness = CreateGrouped();

        var nestedBefore = harness.Editor.GetDesignPosition(harness.Nested(0));
        var siblingBefore = harness.Editor.GetDesignPosition(harness.Named(0, "Sibling"));

        harness.Window.MouseDown(NestedCentre, MouseButton.Left);
        harness.Window.MouseMove(NestedCentre + new Vector(10, 6));
        harness.Window.MouseMove(NestedCentre + new Vector(40, 20));
        harness.Window.MouseUp(NestedCentre + new Vector(40, 20), MouseButton.Left);
        harness.RunLayout();

        var nestedAfter = harness.Editor.GetDesignPosition(harness.Nested(0));
        var siblingAfter = harness.Editor.GetDesignPosition(harness.Named(0, "Sibling"));

        Assert.NotEqual(nestedBefore.X, nestedAfter.X);
        Assert.Equal(nestedAfter.X - nestedBefore.X, siblingAfter.X - siblingBefore.X, 1);
        Assert.Equal(nestedAfter.Y - nestedBefore.Y, siblingAfter.Y - siblingBefore.Y, 1);
    }

    private static SelectionAdorner GroupAdorner(EditorHarness harness)
        => harness.Editor.GetVisualDescendants()
            .OfType<SelectionAdorner>()
            .Single(a => a.Name == "PART_GroupSelectionAdorner");

    /// <summary>
    /// Рамка группы не выглядит заблокированной.
    /// </summary>
    /// <remarks>
    /// Locked-визуал — это не отдельное оформление, а то, как рисуется adorner с
    /// политиками <c>None</c>/<c>None</c>. Поэтому рамка группы, которой политики не
    /// посчитали, выглядела ровно как заблокированный элемент — и ручки у неё были
    /// неинтерактивными по той же причине, а не по двум разным.
    /// </remarks>
    [AvaloniaFact]
    public void The_Group_Frame_Is_Not_Drawn_Locked()
    {
        var harness = CreateGrouped();
        var adorner = GroupAdorner(harness);

        Assert.NotEqual(ResizePolicy.None, adorner.ResizePolicy);
        Assert.NotEqual(MovePolicy.None, adorner.MovePolicy);
    }

    /// <summary>
    /// Группа масштабируется ручкой рамки.
    /// </summary>
    /// <remarks>
    /// Тянем тем же путём, которым тянет приложение: события поднимает сам adorner.
    /// </remarks>
    [AvaloniaFact]
    public void Resizing_The_Frame_Scales_The_Whole_Group()
    {
        var harness = CreateGrouped();
        var nested = harness.Nested(0);
        var sibling = harness.Named(0, "Sibling");

        var widthBefore = harness.Editor.GetDesignSize(nested).Width;
        var frameBefore = harness.Editor.SelectionBounds;

        var adorner = GroupAdorner(harness);
        var delta = new Vector(40, 0);
        adorner.RaiseEvent(new ResizeStartedEventArgs(default, ResizeDirection.Right, SelectionAdorner.ResizeStartedEvent));
        adorner.RaiseEvent(new ResizeDeltaEventArgs(delta, ResizeDirection.Right, SelectionAdorner.ResizeDeltaEvent));
        adorner.RaiseEvent(new VectorEventArgs { RoutedEvent = SelectionAdorner.ResizeCompletedEvent, Vector = delta });
        harness.RunLayout();

        Assert.True(harness.Editor.SelectionBounds.Width > frameBefore.Width);
        Assert.True(harness.Editor.GetDesignSize(nested).Width > widthBefore);
        Assert.True(harness.Editor.GetDesignSize(sibling).Width > 0);
    }

    // ---- Контракт изменений -----------------------------------------------------

    /// <summary>
    /// Группировка — одна единица редактирования.
    /// </summary>
    [AvaloniaFact]
    public void Grouping_Is_One_Edit()
    {
        var harness = Create();
        Click(harness, NestedCentre);
        Click(harness, SiblingCentre, RawInputModifiers.Shift);

        var edits = new List<DesignEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);

        harness.Editor.GroupSelection();
        harness.RunLayout();

        var edit = Assert.Single(edits);
        Assert.Equal(DesignEditKind.Group, edit.Kind);
        Assert.Equal(2, edit.Changes.Count);
        Assert.All(edit.Changes, c => Assert.IsType<DesignGroupChange>(c));
    }

    [AvaloniaFact]
    public void Reverting_Dissolves_The_Group_And_Reapplying_Restores_It()
    {
        var harness = Create();
        Click(harness, NestedCentre);
        Click(harness, SiblingCentre, RawInputModifiers.Shift);

        DesignEditCompletedEventArgs? edit = null;
        harness.Editor.EditCompleted += (_, e) => edit = e;

        harness.Editor.GroupSelection();
        harness.RunLayout();
        var id = DesignGroup.GetId(harness.Nested(0));
        Assert.NotNull(id);

        foreach (var change in edit!.Changes)
            harness.Editor.Revert(change);

        harness.RunLayout();
        Assert.Null(DesignGroup.GetId(harness.Nested(0)));

        foreach (var change in edit.Changes)
            harness.Editor.Reapply(change);

        harness.RunLayout();
        Assert.Equal(id, DesignGroup.GetId(harness.Nested(0)));
    }

    /// <summary>
    /// Отмена и повтор не дописывают стек.
    /// </summary>
    [AvaloniaFact]
    public void Applying_A_Group_Change_Produces_No_New_Edit()
    {
        var harness = CreateGrouped();
        var edits = new List<DesignEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);

        harness.Editor.ApplyGroup(harness.Nested(0), null);
        harness.RunLayout();

        Assert.Empty(edits);
    }

    [AvaloniaFact]
    public void ApplyGroup_Rejects_Null_Target()
    {
        var harness = Create();

        Assert.Throws<ArgumentNullException>(() => harness.Editor.ApplyGroup(null!, "x"));
    }

    // ---- Состав группы ----------------------------------------------------------

    /// <summary>Панель-корень формы: обход её выдаёт, а выделение — нет.</summary>
    private static Control HostPanel(EditorHarness harness) =>
        (Control)harness.Nested(0).GetVisualParent()!;

    /// <summary>
    /// Участником считается только то, что редактор даёт выбрать.
    /// </summary>
    /// <remarks>
    /// В режиме <see cref="DesignContentMode.Annotated"/> target'ом становится контрол
    /// с designer-метаданными, и корневая панель формы им не является. Пометку на ней
    /// хост поставить может — руками или из разметки, — но раскрытие по клику обязано
    /// остаться в пределах того, что вообще выбирается: иначе в выделении оказывается
    /// то, чего указатель выбрать не может, и рамка рисуется вокруг чужой площади.
    /// </remarks>
    [AvaloniaFact]
    public void A_Mark_On_A_Non_Element_Is_Not_A_Member()
    {
        var harness = CreateGrouped();
        var panel = HostPanel(harness);
        DesignGroup.SetId(panel, DesignGroup.GetId(harness.Nested(0)));

        Click(harness, NestedCentre);

        Assert.DoesNotContain(harness.Editor.SelectedDesignTargets, t => ReferenceEquals(t.Target, panel));
        Assert.Equal(2, harness.Editor.SelectedDesignTargetsCount);
    }

    /// <summary>
    /// Невидимая редактору пометка всё равно занимает идентификатор.
    /// </summary>
    /// <remarks>
    /// Состав группы фильтруется, а сбор занятых номеров — нет, и это не
    /// непоследовательность: новая группа, вставшая на чужой номер, слилась бы с ней
    /// при первом же сохранении.
    /// </remarks>
    [AvaloniaFact]
    public void A_Hidden_Mark_Still_Reserves_Its_Identifier()
    {
        var harness = Create();
        DesignGroup.SetId(HostPanel(harness), "group-1");

        Click(harness, NestedCentre);
        Click(harness, SiblingCentre, RawInputModifiers.Shift);
        Assert.True(harness.Editor.GroupSelection());
        harness.RunLayout();

        Assert.NotEqual("group-1", DesignGroup.GetId(harness.Nested(0)));
    }

    /// <summary>
    /// Группировка уже выбранного набора публикуется.
    /// </summary>
    /// <remarks>
    /// Контролы те же и в том же порядке, поэтому сравнение снимка по одним target'ам
    /// признало бы его неизменившимся. Изменилась группа, а её редактор публикует, —
    /// значит и сравнивать обязан: публикуется всё, что сравнивается.
    /// </remarks>
    [AvaloniaFact]
    public void Grouping_The_Current_Selection_Publishes_It()
    {
        var harness = Create();
        Click(harness, NestedCentre);
        Click(harness, SiblingCentre, RawInputModifiers.Shift);

        var events = 0;
        harness.Editor.DesignSelectionChanged += (_, _) => events++;

        Assert.True(harness.Editor.GroupSelection());
        harness.RunLayout();

        Assert.Equal(1, events);
    }

    // ---- Чтение групп -----------------------------------------------------------

    [AvaloniaFact]
    public void Groups_Are_Read_Back_From_The_Container()
    {
        var harness = CreateGrouped();
        var container = harness.Container(0);

        var groups = harness.Editor.GetGroups(container);

        var group = Assert.Single(groups);
        Assert.Same(container, group.Container);
        Assert.Equal(DesignGroup.GetId(harness.Nested(0)), group.Id);
        Assert.Equal(
            new Control[] { harness.Nested(0), harness.Named(0, "Sibling") },
            group.Members);
    }

    /// <summary>
    /// Неизвестный идентификатор — это ответ, а не ошибка.
    /// </summary>
    /// <remarks>
    /// Состав меняется под хостом: группа могла быть распущена между двумя его
    /// запросами, и исключение на это заставило бы оборачивать каждое чтение.
    /// </remarks>
    [AvaloniaFact]
    public void An_Unknown_Group_Reads_As_Empty()
    {
        var harness = CreateGrouped();

        Assert.Empty(harness.Editor.GetGroupMembers(harness.Container(0), "group-404"));
    }

    /// <summary>
    /// Порядок участников — порядок разметки, а не порядок выделения.
    /// </summary>
    /// <remarks>
    /// Хост рисует по этому списку панель слоёв, и она не должна переставляться от того,
    /// в каком порядке участников выделили перед группировкой.
    /// </remarks>
    [AvaloniaFact]
    public void Members_Keep_The_Order_Of_The_Markup()
    {
        var harness = Create();
        Click(harness, SiblingCentre);
        Click(harness, NestedCentre, RawInputModifiers.Shift);
        Assert.True(harness.Editor.GroupSelection());
        harness.RunLayout();

        var id = DesignGroup.GetId(harness.Nested(0))!;

        Assert.Equal(
            new Control[] { harness.Nested(0), harness.Named(0, "Sibling") },
            harness.Editor.GetGroupMembers(harness.Container(0), id));
    }

    [AvaloniaFact]
    public void The_Selection_Target_Carries_Its_Group()
    {
        var harness = Create();
        Click(harness, NestedCentre);
        Assert.Null(harness.Editor.PrimarySelectionTarget!.GroupId);

        Click(harness, SiblingCentre, RawInputModifiers.Shift);
        Assert.True(harness.Editor.GroupSelection());
        harness.RunLayout();

        Assert.All(
            harness.Editor.SelectedDesignTargets,
            t => Assert.Equal(DesignGroup.GetId(t.Target), t.GroupId));
        Assert.NotNull(harness.Editor.PrimarySelectionTarget!.GroupId);
    }

    [AvaloniaFact]
    public void Reading_Groups_Rejects_Null_Arguments()
    {
        var harness = CreateGrouped();

        Assert.Throws<ArgumentNullException>(() => harness.Editor.GetGroups(null!));
        Assert.Throws<ArgumentNullException>(() => harness.Editor.GetGroupMembers(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => harness.Editor.GetGroupMembers(harness.Container(0), null!));
    }

    // ---- Переименование ---------------------------------------------------------

    [AvaloniaFact]
    public void Renaming_Moves_Every_Member()
    {
        var harness = CreateGrouped();
        var id = DesignGroup.GetId(harness.Nested(0))!;

        Assert.True(harness.Editor.RenameGroup(harness.Container(0), id, "toolbar"));
        harness.RunLayout();

        Assert.Equal("toolbar", DesignGroup.GetId(harness.Nested(0)));
        Assert.Equal("toolbar", DesignGroup.GetId(harness.Named(0, "Sibling")));
        Assert.Equal("toolbar", Assert.Single(harness.Editor.GetGroups(harness.Container(0))).Id);
    }

    /// <summary>
    /// Переименование — одна правка, и она отменяется.
    /// </summary>
    /// <remarks>
    /// Идёт через тот же шов, что группировка: смена пометки без него не попала бы
    /// в контракт изменений, и <c>Ctrl + Z</c> вернул бы всё, кроме имени группы.
    /// </remarks>
    [AvaloniaFact]
    public void Renaming_Is_One_Edit_And_Undoes()
    {
        var harness = CreateGrouped();
        var id = DesignGroup.GetId(harness.Nested(0))!;

        var edits = new List<DesignEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);

        Assert.True(harness.Editor.RenameGroup(harness.Container(0), id, "toolbar"));
        harness.RunLayout();

        var edit = Assert.Single(edits);
        Assert.Equal(DesignEditKind.Group, edit.Kind);
        Assert.Equal(2, edit.Changes.Count);

        foreach (var change in edit.Changes)
            harness.Editor.Revert(change);

        harness.RunLayout();
        Assert.Equal(id, DesignGroup.GetId(harness.Nested(0)));
    }

    /// <summary>
    /// Занятый идентификатор переименование отклоняет.
    /// </summary>
    /// <remarks>
    /// Идентификатор — это личность группы внутри формы, поэтому переезд на занятый
    /// означал бы слияние двух групп. Слияние — отдельное действие (выбрать обе и
    /// сгруппировать), а не побочный эффект набора текста.
    /// </remarks>
    [AvaloniaFact]
    public void Renaming_Onto_A_Taken_Identifier_Is_Refused()
    {
        var harness = CreateGrouped();
        var id = DesignGroup.GetId(harness.Nested(0))!;
        DesignGroup.SetId(HostPanel(harness), "toolbar");

        Assert.False(harness.Editor.RenameGroup(harness.Container(0), id, "toolbar"));
        Assert.Equal(id, DesignGroup.GetId(harness.Nested(0)));
    }

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_Empty_Identifier_Is_Refused(string candidate)
    {
        var harness = CreateGrouped();
        var id = DesignGroup.GetId(harness.Nested(0))!;

        Assert.False(harness.Editor.RenameGroup(harness.Container(0), id, candidate));
        Assert.Equal(id, DesignGroup.GetId(harness.Nested(0)));
    }

    [AvaloniaFact]
    public void Renaming_An_Unknown_Group_Changes_Nothing()
    {
        var harness = CreateGrouped();
        var edits = new List<DesignEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);

        Assert.False(harness.Editor.RenameGroup(harness.Container(0), "group-404", "toolbar"));

        Assert.Empty(edits);
    }

    /// <summary>
    /// Открытая двойным кликом группа переезжает вместе с именем.
    /// </summary>
    /// <remarks>
    /// Вход в группу держится её идентификатором. Оставь его прежним — и группа
    /// осталась бы «открытой» по имени, которого больше нет: клик внутри неё снова
    /// начал бы выбирать её целиком.
    /// </remarks>
    [AvaloniaFact]
    public void The_Entered_Group_Follows_The_Rename()
    {
        var harness = CreateGrouped();
        var id = DesignGroup.GetId(harness.Nested(0))!;

        // Вход в группу: двойной клик по участнику.
        harness.Window.MouseDown(NestedCentre, MouseButton.Left);
        harness.Window.MouseUp(NestedCentre, MouseButton.Left);
        harness.Window.MouseDown(NestedCentre, MouseButton.Left);
        harness.Window.MouseUp(NestedCentre, MouseButton.Left);
        harness.RunLayout();

        Assert.True(harness.Editor.RenameGroup(harness.Container(0), id, "toolbar"));
        harness.RunLayout();

        // Внутри открытой группы клик выбирает участника поодиночке.
        Click(harness, SiblingCentre);
        Assert.Equal(1, harness.Editor.SelectedDesignTargetsCount);
    }

    [AvaloniaFact]
    public void Renaming_Rejects_Null_Arguments()
    {
        var harness = CreateGrouped();
        var container = harness.Container(0);

        Assert.Throws<ArgumentNullException>(() => harness.Editor.RenameGroup(null!, "a", "b"));
        Assert.Throws<ArgumentNullException>(() => harness.Editor.RenameGroup(container, null!, "b"));
        Assert.Throws<ArgumentNullException>(() => harness.Editor.RenameGroup(container, "a", null!));
    }
}
