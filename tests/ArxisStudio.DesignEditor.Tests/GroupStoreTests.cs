using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Xunit;
using DesignLayout = ArxisStudio.Surface.UiDesigner.Layout;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Шов хранения групп: редактор владеет смыслом группы, а местом — тот, кто владеет документом.
/// </summary>
/// <remarks>
/// Причина шва измерима: из четырёх значений, которые редактор пишет в чужие контролы, у трёх
/// есть читатель в рантайме, а у группы — ни одного. Записанная в разметку, она делает документ
/// зависимым от сборки редактора; см. ADR 0002.
/// </remarks>
public class GroupStoreTests
{
    private const double CellWidth = 60;
    private const double CellHeight = 40;

    private static readonly string[] Names = { "A", "B", "C", "D" };

    /// <summary>
    /// Хранилище-словарь: пометка не касается контролов вовсе.
    /// </summary>
    /// <remarks>
    /// Изображает хранилище хоста, которое держит принадлежность рядом с документом. Ключ здесь
    /// всё ещё контрол — переживать перезагрузку формы этому стенду не нужно.
    /// </remarks>
    private sealed class SideStore : IDesignGroupStore
    {
        private readonly Dictionary<Control, string> _paths = new();

        public int Writes { get; private set; }

        public string? GetGroup(Control target) => _paths.TryGetValue(target, out var path) ? path : null;

        public void SetGroup(Control target, string? path)
        {
            Writes++;
            Assign(target, path);
        }

        /// <summary>Правка мимо редактора — то, что делает хост.</summary>
        public void Assign(Control target, string? path)
        {
            if (path is null)
                _paths.Remove(target);
            else
                _paths[target] = path;
        }

        public void Raise() => GroupsChanged?.Invoke(this, EventArgs.Empty);

        public event EventHandler? GroupsChanged;
    }

    private static EditorHarness Create(IDesignGroupStore? store = null)
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

        if (store != null)
            editor.GroupStore = store;

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

    // ---- Умолчание --------------------------------------------------------------

    /// <summary>
    /// Без хранилища пометка по-прежнему живёт в attached-свойстве.
    /// </summary>
    /// <remarks>
    /// Это обещание совместимости: разметка с <c>DesignGroup.Id</c>, чужие макеты и демо
    /// продолжают работать, а шов меняет только то, у кого можно спросить.
    /// </remarks>
    [AvaloniaFact]
    public void The_Default_Store_Is_The_Attached_Property()
    {
        var harness = Create();

        Assert.Same(DesignGroupAttachedStore.Default, harness.Editor.GroupStore);

        Select(harness, "A", "B");
        Assert.True(harness.Editor.GroupSelection());
        harness.RunLayout();

        Assert.NotNull(DesignGroup.GetId(Cell(harness, "A")));
        Assert.Equal(DesignGroup.GetId(Cell(harness, "A")), DesignGroup.GetId(Cell(harness, "B")));
    }

    // ---- Запись -----------------------------------------------------------------

    /// <summary>
    /// С заданным хранилищем группировка не касается контролов.
    /// </summary>
    /// <remarks>
    /// Ради этого шов и заведён: пометка редактора не обязана попадать в документ
    /// пользователя. Проверяется обе половины — что путь появился в хранилище и что
    /// attached-свойство осталось пустым.
    /// </remarks>
    [AvaloniaFact]
    public void Grouping_Writes_Through_The_Store_And_Not_Into_The_Markup()
    {
        var store = new SideStore();
        var harness = Create(store);

        Select(harness, "A", "B");
        Assert.True(harness.Editor.GroupSelection());
        harness.RunLayout();

        var path = store.GetGroup(Cell(harness, "A"));
        Assert.NotNull(path);
        Assert.Equal(path, store.GetGroup(Cell(harness, "B")));
        Assert.Equal(2, store.Writes);

        Assert.Null(DesignGroup.GetId(Cell(harness, "A")));
        Assert.Null(DesignGroup.GetId(Cell(harness, "B")));
    }

    // ---- Чтение -----------------------------------------------------------------

    /// <summary>
    /// Группа, о которой знает только хранилище, читается и выбирается как своя.
    /// </summary>
    /// <remarks>
    /// Публичное чтение, раскрытие клика до кластера и оверлей спрашивают одну и ту же пометку —
    /// значит все трое обязаны увидеть хранилище, а не attached-свойство.
    /// </remarks>
    [AvaloniaFact]
    public void Groups_Are_Read_Through_The_Store()
    {
        var store = new SideStore();
        var harness = Create(store);

        store.Assign(Cell(harness, "A"), "outer");
        store.Assign(Cell(harness, "B"), "outer");

        var groups = harness.Editor.GetGroups(harness.Container(0));
        var group = Assert.Single(groups);
        Assert.Equal("outer", group.Path);
        Assert.Equal(2, group.Members.Count);

        // Клик по участнику раскрывается до всей группы.
        Click(harness, "A");
        Assert.Equal(2, harness.Editor.SelectedDesignTargetsCount);
        Assert.True(harness.Editor.HasGroupSelection);
    }

    /// <summary>
    /// Снимок выделения берёт идентификатор группы у хранилища.
    /// </summary>
    /// <remarks>
    /// <see cref="DesignSelectionTarget.GroupId"/> участвует в сравнении снимков: возьми он
    /// пометку не оттуда — и группировка уже выбранного набора снова перестала бы публиковаться,
    /// а хост остался бы со старой группой при верном экране.
    /// </remarks>
    [AvaloniaFact]
    public void A_Selection_Target_Reports_The_Group_From_The_Store()
    {
        var store = new SideStore();
        var harness = Create(store);

        store.Assign(Cell(harness, "C"), "solo");
        Click(harness, "C");

        Assert.Equal("solo", harness.Editor.PrimarySelectionTarget?.GroupId);
        Assert.Null(DesignGroup.GetId(Cell(harness, "C")));
    }

    // ---- Отмена -----------------------------------------------------------------

    /// <summary>
    /// Отмена группировки возвращает хранилище, а не контролы.
    /// </summary>
    /// <remarks>
    /// Единица редактирования снимает состояние «до» тем же чтением, что и запись; иначе отмена
    /// вернула бы значение attached-свойства — то есть <see langword="null"/> у всех — и
    /// совпадение было бы случайным.
    /// </remarks>
    [AvaloniaFact]
    public void Reverting_Dissolves_The_Group_In_The_Store()
    {
        var store = new SideStore();
        var harness = Create(store);

        store.Assign(Cell(harness, "A"), "before");
        store.Assign(Cell(harness, "B"), "before");

        DesignEditCompletedEventArgs? edit = null;
        harness.Editor.EditCompleted += (_, e) => edit = e;

        Select(harness, "A", "C");
        Assert.True(harness.Editor.GroupSelection());
        harness.RunLayout();

        Assert.NotNull(edit);
        Assert.All(edit!.Changes, c => Assert.IsType<DesignGroupChange>(c));

        foreach (var change in edit.Changes)
            harness.Editor.Revert(change);

        harness.RunLayout();

        Assert.Equal("before", store.GetGroup(Cell(harness, "A")));
        Assert.Equal("before", store.GetGroup(Cell(harness, "B")));
        Assert.Null(store.GetGroup(Cell(harness, "C")));
    }

    // ---- Чужая правка -----------------------------------------------------------

    /// <summary>
    /// Событие хранилища пересобирает оверлей.
    /// </summary>
    /// <remarks>
    /// О своих правках редактор знает сам, а о чужих — только отсюда. До появления шва такого
    /// способа не было вовсе: пометку хост ставил мимо, и рамка описывала прежний состав.
    /// </remarks>
    [AvaloniaFact]
    public void A_Foreign_Change_Rebuilds_The_Overlay()
    {
        var store = new SideStore();
        var harness = Create(store);

        Select(harness, "A", "B");
        Assert.Equal(2, harness.Editor.SelectedDesignTargetsCount);
        Assert.False(harness.Editor.HasGroupSelection);

        store.Assign(Cell(harness, "A"), "outer");
        store.Assign(Cell(harness, "B"), "outer");
        Assert.False(harness.Editor.HasGroupSelection);

        store.Raise();
        harness.RunLayout();

        Assert.True(harness.Editor.HasGroupSelection);
    }

    // ---- Время жизни ------------------------------------------------------------

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference MakeEditor(IDesignGroupStore? shared)
    {
        var editor = new DesignEditor();
        editor.GroupStore = shared ?? new SideStore();

        return new WeakReference(editor);
    }

    private static int AliveAfterCollect(Func<WeakReference> make, int count)
    {
        var refs = new List<WeakReference>();
        for (var i = 0; i < count; i++)
            refs.Add(make());

        for (var i = 0; i < 3; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
        }

        return refs.Count(r => r.IsAlive);
    }

    /// <summary>
    /// Общее хранилище не держит редакторы.
    /// </summary>
    /// <remarks>
    /// Хранилище хост заводит одно на документ, а редакторов над одним документом бывает
    /// несколько. Обычная подписка на <see cref="IDesignGroupStore.GroupsChanged"/> уложила бы
    /// делегат в само хранилище, и оно не отпустило бы ни одного редактора — та же ошибка, что
    /// уже была с общим набором жестов, и то же лечение слабым событием.
    /// <para>
    /// Контрольная половина со своим хранилищем нужна, чтобы отличить «не держит» от «сборка
    /// вообще ничего не собрала».
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void A_Shared_Store_Does_Not_Hold_The_Editors()
    {
        var shared = new SideStore();

        var withShared = AliveAfterCollect(() => MakeEditor(shared), 5);
        var withOwn = AliveAfterCollect(() => MakeEditor(null), 5);

        Assert.Equal(0, withOwn);
        Assert.Equal(0, withShared);
        GC.KeepAlive(shared);
    }
}
