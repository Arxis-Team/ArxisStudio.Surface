using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Xunit;
using DesignLayout = ArxisStudio.Surface.UiDesigner.Layout;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Характеризующие тесты вложенных контейнеров.
/// </summary>
/// <remarks>
/// Фиксируют фактическое поведение модели выделения, когда внутри
/// <see cref="DesignEditorItem"/> находится другой <see cref="DesignEditorItem"/>.
/// Задача — не защитить текущее поведение, а зафиксировать точку отсчёта
/// перед переходом на дерево любой глубины.
/// </remarks>
public class NestedContainerTests
{
    private const double OuterSize = 200;
    private const double InnerOffset = 20;
    private const double InnerSize = 80;

    private static readonly Point OuterLocation = new(100, 100);

    /// <summary>
    /// Точка внутри вложенного контейнера, но вне его ребёнка InnerChild.
    /// </summary>
    /// <remarks>
    /// Inner занимает (120,120)-(200,200), InnerChild — (130,130)-(170,170),
    /// поэтому попадание в правый нижний угол Inner адресует именно контейнер.
    /// </remarks>
    private static Point InnerCentre => new(
        OuterLocation.X + InnerOffset + InnerSize - 15,
        OuterLocation.Y + InnerOffset + InnerSize - 15);

    private sealed record Node(string Name);

    private static (Window Window, DesignEditor Editor, Node Item) Create()
    {
        var item = new Node("outer");

        var editor = new DesignEditor
        {
            ItemsSource = new[] { item },
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<Node>((_, _) =>
            {
                var innerChild = new Border { Name = "InnerChild", Width = 40, Height = 40 };
                DesignLayout.SetX(innerChild, 10);
                DesignLayout.SetY(innerChild, 10);

                var innerPanel = new AbsolutePanel();
                innerPanel.Children.Add(innerChild);

                var inner = new DesignEditorItem
                {
                    Name = "Inner",
                    Width = InnerSize,
                    Height = InnerSize,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Content = innerPanel
                };
                DesignLayout.SetX(inner, InnerOffset);
                DesignLayout.SetY(inner, InnerOffset);

                // Сосед вложенного контейнера, лежащий прямо во внешнем.
                var outerChild = new Border { Name = "OuterChild", Width = 40, Height = 40 };
                DesignLayout.SetX(outerChild, 120);
                DesignLayout.SetY(outerChild, 20);

                var panel = new AbsolutePanel();
                panel.Children.Add(inner);
                panel.Children.Add(outerChild);
                return panel;
            }, supportsRecycling: false)
        };

        // Тесты вложенности проверяют механику, а не привязку.
        editor.InteractionOptions.IsSnapToGridEnabled = false;

        var window = new Window { Width = 800, Height = 600, Content = editor };
        window.Show();
        RunLayout(window);

        var outer = (DesignEditorItem)editor.ContainerFromItem(item)!;
        outer.Width = OuterSize;
        outer.Height = OuterSize;
        outer.HorizontalAlignment = HorizontalAlignment.Left;
        outer.VerticalAlignment = VerticalAlignment.Top;
        outer.Location = OuterLocation;
        RunLayout(window);

        return (window, editor, item);
    }

    private static void RunLayout(Window window)
    {
        var manager = window.GetLayoutManager();
        manager?.ExecuteInitialLayoutPass();
        manager?.ExecuteLayoutPass();
    }

    private static DesignEditorItem Inner(DesignEditor editor) =>
        editor.GetVisualDescendants().OfType<DesignEditorItem>().Single(i => i.Name == "Inner");

    private static Border Named(DesignEditor editor, string name) =>
        editor.GetVisualDescendants().OfType<Border>().Single(b => b.Name == name);

    [AvaloniaFact]
    public void Nested_Container_Is_Realised_Inside_Outer_Container()
    {
        var (_, editor, item) = Create();

        var outer = (DesignEditorItem)editor.ContainerFromItem(item)!;
        var inner = Inner(editor);

        Assert.NotSame(outer, inner);
        Assert.Contains(outer, inner.GetVisualAncestors());
    }

    [AvaloniaFact]
    public void Nested_Container_Is_Not_An_Item_Of_The_Editor()
    {
        var (_, editor, item) = Create();

        var outer = (DesignEditorItem)editor.ContainerFromItem(item)!;
        var inner = Inner(editor);

        // Корень проблемы: индексная модель выбора Avalonia знает только
        // контейнеры собственного ItemsSource. Для вложенного индекса нет.
        Assert.Equal(0, editor.IndexFromContainer(outer));
        Assert.Equal(-1, editor.IndexFromContainer(inner));
    }

    [AvaloniaFact]
    public void Editor_Only_Realises_Top_Level_Containers_As_Items()
    {
        var (_, editor, _) = Create();

        // ItemsSource порождает ровно один контейнер: вложенный редактору не принадлежит.
        Assert.Equal(1, editor.ItemCount);
    }

    [AvaloniaFact]
    public void Click_On_Nested_Container_Selects_It_As_Nested_Target()
    {
        var (window, editor, _) = Create();
        var outer = (DesignEditorItem)editor.ContainerFromItem(editor.ItemsSource!.Cast<object>().First())!;
        var inner = Inner(editor);

        window.MouseDown(InnerCentre, MouseButton.Left);
        window.MouseUp(InnerCentre, MouseButton.Left);
        RunLayout(window);

        // Вложенный контейнер выбирается как target внутри владеющего item'а:
        // индексная модель Avalonia продолжает оперировать верхним уровнем,
        // а сам вложенный DesignEditorItem становится target.
        var primary = editor.PrimarySelectionTarget;
        Assert.NotNull(primary);
        Assert.Same(inner, primary!.Target);
        Assert.Same(outer, primary.Container);
        Assert.True(editor.HasSingleSelection);
    }

    [AvaloniaFact]
    public void Selecting_Nested_Container_Marks_The_Owning_Item_Selected()
    {
        var (window, editor, item) = Create();
        var outer = (DesignEditorItem)editor.ContainerFromItem(item)!;

        window.MouseDown(InnerCentre, MouseButton.Left);
        window.MouseUp(InnerCentre, MouseButton.Left);
        RunLayout(window);

        // Индексная модель Avalonia продолжает работать: выбран владеющий item.
        Assert.True(outer.IsSelected);
        Assert.Equal(1, editor.Selection.Count);
    }

    [AvaloniaFact]
    public void Nested_Container_Reports_Container_Scope()
    {
        var (window, editor, _) = Create();

        window.MouseDown(InnerCentre, MouseButton.Left);
        window.MouseUp(InnerCentre, MouseButton.Left);
        RunLayout(window);

        var primary = editor.PrimarySelectionTarget!;

        // Scope определяется типом target: вложенный DesignEditorItem — контейнер,
        // хотя владеющий item верхнего уровня другой.
        Assert.Equal(SurfaceSelectionScope.Container, primary.Scope);
        Assert.NotSame(primary.Container, primary.Target);
    }

    [AvaloniaFact]
    public void Depth_Reflects_Position_In_The_Container_Tree()
    {
        var (_, editor, item) = Create();
        var outer = (DesignEditorItem)editor.ContainerFromItem(item)!;
        var inner = Inner(editor);

        Assert.Equal(0, new SurfaceSelectionTarget(outer, outer).Depth);
        Assert.Equal(1, new SurfaceSelectionTarget(outer, inner).Depth);
    }

    [AvaloniaFact]
    public void Marquee_Contained_By_Nested_Container_Scopes_To_It()
    {
        var (window, editor, _) = Create();

        // Рамка целиком внутри вложенного контейнера: (125,125)-(175,175),
        // при Inner на (120,120)-(200,200).
        // Вызов напрямую: через UI рамка пока не может начаться внутри контейнера.
        editor.CommitSelection(new Rect(125, 125, 50, 50), isCtrlPressed: false);
        RunLayout(window);

        var targets = editor.SelectedTargets.Select(t => t.Target).ToList();

        Assert.Contains(Named(editor, "InnerChild"), targets);

        // Сам вложенный контейнер в область поиска не попадает — рамка работает
        // в его пределах. До перехода на поиск контейнеров по глубине владельцем
        // становился внешний item, и Inner выбирался вместе со своим ребёнком.
        Assert.DoesNotContain(Inner(editor), targets);
    }

    [AvaloniaFact]
    public void Marquee_Crossing_Nested_Boundary_Scopes_To_The_Owner()
    {
        var (window, editor, _) = Create();

        // Рамка выходит за Inner и накрывает соседа во внешнем контейнере,
        // поэтому владельцем становится внешний item.
        editor.CommitSelection(new Rect(125, 125, 125, 50), isCtrlPressed: false);
        RunLayout(window);

        var targets = editor.SelectedTargets.Select(t => t.Target).ToList();

        Assert.Contains(Named(editor, "OuterChild"), targets);
        Assert.Contains(Inner(editor), targets);

        // Содержимое вложенного контейнера не подмешивается: рамка выбирает
        // соседей одного уровня, а не смесь контейнера и его детей.
        Assert.DoesNotContain(Named(editor, "InnerChild"), targets);
    }

    [AvaloniaFact]
    public void Additive_Click_Does_Not_Group_Targets_From_Different_Hosts()
    {
        var (window, editor, _) = Create();

        // Сначала выбираем ребёнка вложенного контейнера.
        var innerChildPoint = new Point(OuterLocation.X + InnerOffset + 30, OuterLocation.Y + InnerOffset + 30);
        window.MouseDown(innerChildPoint, MouseButton.Left);
        window.MouseUp(innerChildPoint, MouseButton.Left);
        RunLayout(window);
        Assert.Same(Named(editor, "InnerChild"), editor.PrimarySelectionTarget!.Target);

        // Shift-клик по соседу из внешнего контейнера: другой design host.
        var outerChildPoint = new Point(OuterLocation.X + 140, OuterLocation.Y + 40);
        window.MouseDown(outerChildPoint, MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(outerChildPoint, MouseButton.Left, RawInputModifiers.Shift);
        RunLayout(window);

        var targets = editor.SelectedTargets.Select(t => t.Target).ToList();
        Assert.DoesNotContain(Named(editor, "OuterChild"), targets);
    }

    [AvaloniaFact]
    public void Nested_Container_Can_Be_Dragged()
    {
        var (window, editor, _) = Create();
        var inner = Inner(editor);
        var start = inner.Location;

        window.MouseDown(InnerCentre, MouseButton.Left);
        // Первый сдвиг переводит контейнер в состояние перетаскивания,
        // последующие — двигают его.
        window.MouseMove(InnerCentre + new Vector(10, 6));
        window.MouseMove(InnerCentre + new Vector(50, 30));
        window.MouseUp(InnerCentre + new Vector(50, 30), MouseButton.Left);
        RunLayout(window);

        Assert.Equal(start.X + 50, inner.Location.X, 1);
        Assert.Equal(start.Y + 30, inner.Location.Y, 1);
    }

    /// <summary>
    /// Хост выбирает контрол во вложенном контейнере — то же, что делает клик.
    /// </summary>
    /// <remarks>
    /// Ближайший design host у такого контрола вложенный, а индекс есть только у item'а верхнего
    /// уровня. Резолвер владельца это и разводит; без него метод отвергал контрол, который
    /// указателем выбирается без вопросов.
    /// </remarks>
    [AvaloniaFact]
    public void SelectTarget_Selects_A_Control_Inside_A_Nested_Container()
    {
        var (window, editor, _) = Create();

        var innerChild = Named(editor, "InnerChild");

        Assert.True(editor.SelectTarget(innerChild));

        RunLayout(window);

        Assert.Same(innerChild, editor.PrimarySelectionTarget!.Target);
    }

    [AvaloniaFact]
    public void SelectTarget_Selects_A_Container_Whole()
    {
        var (window, editor, _) = Create();

        var container = (DesignEditorItem)editor.ContainerFromIndex(0)!;

        Assert.True(editor.SelectTarget(container));

        RunLayout(window);

        Assert.Same(container, editor.PrimarySelectionTarget!.Target);
        Assert.Equal(SurfaceSelectionScope.Container, editor.PrimarySelectionTarget.Scope);
    }
}
