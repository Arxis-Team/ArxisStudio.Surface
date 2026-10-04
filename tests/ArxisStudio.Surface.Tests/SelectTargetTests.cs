using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Контракт публичного <see cref="SurfaceView.SelectTarget(Control, bool)"/>.
/// </summary>
/// <remarks>
/// Стенд намеренно на <b>двух</b> контейнерах. Прежние тесты этого метода жили в одном,
/// и это скрывало половину контракта: мутационная проверка показала, что удаление
/// <c>Selection.Clear()</c> оставляет весь набор зелёным — в одном контейнере замена
/// индексного слоя не наблюдаема.
/// <para>
/// Проверяется только публичное состояние: <c>SelectedItems</c>, <c>Selection</c>,
/// <c>SelectedTargets</c>, <c>PrimarySelectionTarget.Scope</c> и счётчик событий.
/// Внутренний список target'ов тесты не трогают — он и есть то, что этап рефакторинга
/// переписывает.
/// </para>
/// </remarks>
public class SelectTargetTests
{
    private static EditorHarness CreateTwo()
    {
        var harness = EditorHarness.Create(nodeCount: 2);
        harness.PlaceContainer(0, new Point(100, 100), new Size(200, 150));
        harness.PlaceContainer(1, new Point(400, 100), new Size(200, 150));
        return harness;
    }

    private static List<SurfaceSelectionChangedEventArgs> Watch(EditorHarness harness)
    {
        var events = new List<SurfaceSelectionChangedEventArgs>();
        harness.Editor.SurfaceSelectionChanged += (_, e) => events.Add(e);
        return events;
    }

    [AvaloniaFact]
    public void A_Container_Asked_For_Whole_Reports_Container_Scope()
    {
        var harness = CreateTwo();

        Assert.True(harness.Editor.SelectTarget(harness.Container(0)));

        var primary = Assert.Single(harness.Editor.SelectedTargets);
        Assert.Equal(SurfaceSelectionScope.Container, primary.Scope);
        Assert.Same(harness.Container(0), primary.Target);
    }

    [AvaloniaFact]
    public void Replacing_Across_Containers_Leaves_One_Selected_Item()
    {
        var harness = CreateTwo();

        harness.Editor.SelectTarget(harness.Nested(0));
        harness.Editor.SelectTarget(harness.Named(1, "Nested"));
        harness.RunLayout();

        // Замена обязана снять и индексный слой. В одном контейнере это не видно:
        // там Selection и так остаётся прежним.
        Assert.Single(harness.Editor.SelectedItems!);
        Assert.Equal(1, harness.Editor.SelectedTargetsCount);
        Assert.Same(harness.Named(1, "Nested"), harness.Editor.PrimarySelectionTarget!.Target);
    }

    [AvaloniaFact]
    public void A_Replacing_Call_Raises_One_Event()
    {
        var harness = CreateTwo();
        harness.Editor.SelectTarget(harness.Nested(0));

        var events = Watch(harness);
        harness.Editor.SelectTarget(harness.Named(1, "Nested"));

        // Два события, первое с пустым выделением, — это не деталь реализации:
        // хост, зеркалящий выделение в своё дерево, гасит подсветку между ними.
        var single = Assert.Single(events);
        Assert.NotEmpty(single.NewTargets);
    }

    [AvaloniaFact]
    public void Re_Selecting_The_Same_Target_Raises_Nothing()
    {
        var harness = CreateTwo();
        harness.Editor.SelectTarget(harness.Nested(0));

        var events = Watch(harness);
        harness.Editor.SelectTarget(harness.Nested(0));

        Assert.Empty(events);
    }

    [AvaloniaFact]
    public void Additive_Builds_A_Two_Container_Selection()
    {
        var harness = CreateTwo();

        Assert.True(harness.Editor.SelectTarget(harness.Container(0)));
        Assert.True(harness.Editor.SelectTarget(harness.Container(1), additive: true));
        harness.RunLayout();

        // Ctrl + Shift + Click строит ровно это состояние — см. ContainerSelectionGestureTests.
        // Единственный публичный сеттер обязан уметь то же.
        Assert.Equal(2, harness.Editor.SelectedTargetsCount);
        Assert.True(harness.Editor.HasMultipleContainerSelection);
    }

    [AvaloniaFact]
    public void An_Index_Layer_Selection_Reports_The_Form_Not_Its_Child()
    {
        var harness = CreateTwo();

        // Унаследованная от SelectingItemsControl дверь: пишет только индексный слой.
        harness.Editor.SelectedIndex = 0;
        harness.RunLayout();

        // Хост выбрал форму — значит выбрана форма, а не её первый ребёнок.
        var primary = harness.Editor.PrimarySelectionTarget;
        Assert.NotNull(primary);
        Assert.Equal(SurfaceSelectionScope.Container, primary!.Scope);
        Assert.Same(harness.Container(0), primary.Target);
    }

    [AvaloniaFact]
    public void Additive_Over_An_Index_Layer_Selection_Keeps_What_Was_There()
    {
        var harness = CreateTwo();
        harness.Editor.SelectedIndex = 0;
        harness.RunLayout();

        var before = harness.Editor.SelectedTargetsCount;
        harness.Editor.SelectTarget(harness.Named(0, "Sibling"), additive: true);
        harness.RunLayout();

        // Добавление обязано добавлять: неявный target контейнера не должен
        // молча исчезать от вызова, который просили считать additive.
        Assert.Equal(before + 1, harness.Editor.SelectedTargetsCount);
    }

    [AvaloniaFact]
    public void Clearing_Selected_Items_Clears_Both_Layers()
    {
        var harness = CreateTwo();
        harness.Editor.SelectTarget(harness.Nested(0));
        harness.RunLayout();

        // Документировано как единственный способ снять выделение снаружи,
        // и до сих пор не был закреплён ни одним тестом.
        harness.Editor.SelectedItems!.Clear();
        harness.RunLayout();

        Assert.Equal(0, harness.Editor.SelectedTargetsCount);
        Assert.Null(harness.Editor.PrimarySelectionTarget);
    }

    [AvaloniaFact]
    public void A_Zero_Sized_Target_Is_Never_Reported_As_Someone_Else()
    {
        var harness = CreateTwo();
        var sibling = harness.Named(0, "Sibling");
        sibling.Width = 0;
        sibling.Height = 0;
        harness.RunLayout();

        var applied = harness.Editor.SelectTarget(sibling);
        harness.RunLayout();

        // Отказаться можно. Сказать «выбрано» и выбрать другой контрол — нельзя:
        // следующий нюдж отредактирует не тот элемент.
        if (applied)
            Assert.Same(sibling, harness.Editor.PrimarySelectionTarget!.Target);
        else
            Assert.DoesNotContain(harness.Editor.SelectedTargets, t => ReferenceEquals(t.Target, sibling));
    }

    [AvaloniaFact]
    public void Template_Parts_Are_Not_Selectable_In_Loaded_Mode()
    {
        var harness = CreateTwo();
        var container = harness.Container(0);
        container.ContentMode = SurfaceContentMode.Loaded;
        harness.RunLayout();

        var part = container.GetVisualDescendants()
            .OfType<Control>()
            .First(control => control.Name == "PART_Border");

        // Обход авторской разметки внутрь шаблонов не спускается — именно поэтому
        // клик по кнопке не выбирает её надпись. Публичный вход обязан судить так же.
        Assert.False(harness.Editor.SelectTarget(part));
    }

    /// <summary>
    /// Ставит рядом с редактором поле, которое держит клавиатуру, — дерево или код хоста.
    /// </summary>
    private static (EditorHarness Harness, TextBox Outline) CreateBesideOutline()
    {
        var harness = CreateTwo();
        var editor = harness.Editor;
        var outline = new TextBox { Width = 120 };

        harness.Window.Content = null;

        var host = new DockPanel();

        DockPanel.SetDock(outline, Dock.Left);
        host.Children.Add(outline);
        host.Children.Add(editor);
        harness.Window.Content = host;
        harness.RunLayout();

        Assert.True(outline.Focus(), "поле рядом с редактором не взяло клавиатуру");

        return (harness, outline);
    }

    [AvaloniaFact]
    public void A_Host_Selection_Takes_The_Keyboard_By_Default()
    {
        var (harness, outline) = CreateBesideOutline();

        Assert.True(harness.Editor.SelectTarget(harness.Nested(0)));

        // Как и указатель: выбранное, заданное хостом, сдвигают стрелками.
        Assert.True(harness.Editor.IsKeyboardFocusWithin);
        Assert.False(outline.IsFocused);
    }

    /// <summary>
    /// Хост, сверяющий холст с выбором в своём дереве, клавиатуру редактору не отдаёт: следующая
    /// клавиша — шаг по дереву, а не сдвиг выбранного.
    /// </summary>
    [AvaloniaFact]
    public void A_Host_Can_Select_Without_Taking_The_Keyboard()
    {
        var (harness, outline) = CreateBesideOutline();

        Assert.True(harness.Editor.SelectTarget(harness.Nested(0), additive: false, takeFocus: false));
        Assert.True(harness.Editor.SelectTarget(harness.Named(0, "Sibling"), additive: true, takeFocus: false));

        Assert.Equal(2, harness.Editor.SelectedTargets.Count);
        Assert.True(outline.IsFocused, "выбор хоста увёл клавиатуру из его дерева");
        Assert.False(harness.Editor.IsKeyboardFocusWithin);
    }
}
