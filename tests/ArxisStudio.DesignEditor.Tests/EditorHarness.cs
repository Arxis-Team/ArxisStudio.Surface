using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Xunit;
using DesignLayout = ArxisStudio.Surface.UiDesigner.Layout;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Tests;

/// <summary>
/// Узел тестовой модели. Позиция задаётся контейнеру напрямую после реализации,
/// чтобы не тянуть в тесты биндинги и MVVM.
/// </summary>
internal sealed class TestNode
{
    public TestNode(string name) => Name = name;

    public string Name { get; }

    public override string ToString() => Name;
}

/// <summary>
/// Сборка реализованного <see cref="DesignEditor"/> в headless-окне.
/// </summary>
/// <remarks>
/// Шаблон элемента даёт <see cref="AbsolutePanel"/> с одним вложенным
/// <see cref="Border"/>, помеченным designer-метаданными <c>Layout.X</c>/<c>Layout.Y</c>.
/// Только такие контролы редактор считает nested target.
/// </remarks>
internal sealed class EditorHarness
{
    public const double NestedOffset = 10;
    public const double NestedWidth = 60;
    public const double NestedHeight = 40;
    public const double SiblingOffset = 100;

    /// <summary>Отступ карточки в стенде <see cref="CreateStackHosted"/>.</summary>
    public const double CardPadding = 30;

    private EditorHarness(Window window, DesignEditor editor, IReadOnlyList<TestNode> nodes)
    {
        Window = window;
        Editor = editor;
        Nodes = nodes;
    }

    public Window Window { get; }
    public DesignEditor Editor { get; }
    public IReadOnlyList<TestNode> Nodes { get; }

    /// <summary>
    /// Собирает редактор для тестов.
    /// </summary>
    /// <param name="snapToGrid">
    /// Привязка к сетке. По умолчанию выключена: большинство тестов проверяют
    /// механику перемещения, а не привязку, и не должны от неё зависеть.
    /// Сам факт того, что в боевой конфигурации она включена, закреплён отдельно
    /// в <c>SnapToGridTests</c>.
    /// </param>
    /// <param name="snapToGuides">
    /// Выравнивание по краям и центрам соседей. Выключено по тем же причинам:
    /// в шаблоне рядом с <c>Nested</c> стоит <c>Sibling</c>, и любой сдвиг,
    /// подошедший к нему ближе радиуса захвата, менял бы результат тестов,
    /// проверяющих совсем другое. Умолчание закреплено в <c>SnapGuideTests</c>.
    /// </param>
    public static EditorHarness Create(int nodeCount = 1, double width = 800, double height = 600, bool snapToGrid = false, bool snapToGuides = false)
    {
        var nodes = Enumerable.Range(0, nodeCount)
            .Select(i => new TestNode("node" + i))
            .ToList();

        var editor = new DesignEditor
        {
            ItemsSource = nodes,
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<TestNode>((_, _) =>
            {
                var nested = new Border
                {
                    Name = "Nested",
                    Width = NestedWidth,
                    Height = NestedHeight
                };
                DesignLayout.SetX(nested, NestedOffset);
                DesignLayout.SetY(nested, NestedOffset);

                // Второй сосед в том же host'е — для проверки группового выделения.
                var sibling = new Border
                {
                    Name = "Sibling",
                    Width = NestedWidth,
                    Height = NestedHeight
                };
                DesignLayout.SetX(sibling, SiblingOffset);
                DesignLayout.SetY(sibling, NestedOffset);

                var panel = new AbsolutePanel();
                panel.Children.Add(nested);
                panel.Children.Add(sibling);
                return panel;
            }, supportsRecycling: false)
        };

        var window = new Window
        {
            Width = width,
            Height = height,
            Content = editor
        };

        editor.InteractionOptions.IsSnapToGridEnabled = snapToGrid;
        editor.InteractionOptions.IsSnapToGuidesEnabled = snapToGuides;

        window.Show();

        var harness = new EditorHarness(window, editor, nodes);
        harness.RunLayout();
        return harness;
    }

    /// <summary>
    /// Собирает редактор с карточкой, построенной на вертикальных <see cref="StackPanel"/> —
    /// структурой демо Login.
    /// </summary>
    /// <remarks>
    /// Нужен там, где проверяется поведение редактора с раскладкой, которая
    /// владеет позицией ребёнка. У всех контролов только <c>Layout.IsTracked</c>
    /// и никаких <c>Layout.X</c>/<c>Y</c> — ровно как в демо.
    /// </remarks>
    public static EditorHarness CreateStackHosted(bool snapToGrid = false, bool snapToGuides = false)
    {
        var nodes = new List<TestNode> { new("stack0") };

        var editor = new DesignEditor
        {
            ItemsSource = nodes,
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<TestNode>((_, _) =>
            {
                // IsHitTestVisible=false — как в демо: иначе живой Button помечает
                // нажатие обработанным и оно не доходит до контейнера.
                var title = new TextBlock { Name = "Title", Text = "Login Form", IsHitTestVisible = false };
                DesignLayout.SetIsTracked(title, true);

                var field = new TextBox { Name = "Field", IsHitTestVisible = false };
                DesignLayout.SetIsTracked(field, true);

                var action = new Button
                {
                    Name = "Action",
                    Content = "Sign In",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    IsHitTestVisible = false
                };
                DesignLayout.SetIsTracked(action, true);

                var inner = new StackPanel { Name = "Inner", Spacing = 12 };
                inner.Children.Add(field);
                inner.Children.Add(action);
                DesignLayout.SetIsTracked(inner, true);

                var outer = new StackPanel { Name = "Outer", Spacing = 20 };
                outer.Children.Add(title);
                outer.Children.Add(inner);
                DesignLayout.SetIsTracked(outer, true);

                var card = new Border
                {
                    Name = "Card",
                    Padding = new Thickness(CardPadding),
                    CornerRadius = new CornerRadius(12),
                    Child = outer
                };
                DesignLayout.SetIsTracked(card, true);

                return card;
            }, supportsRecycling: false)
        };

        var window = new Window { Width = 800, Height = 600, Content = editor };
        editor.InteractionOptions.IsSnapToGridEnabled = snapToGrid;
        editor.InteractionOptions.IsSnapToGuidesEnabled = snapToGuides;
        window.Show();

        var harness = new EditorHarness(window, editor, nodes);
        harness.RunLayout();
        return harness;
    }

    /// <summary>
    /// Оборачивает уже собранный редактор, когда тесту нужен собственный шаблон элемента.
    /// </summary>
    public static EditorHarness Adopt(Window window, DesignEditor editor, IReadOnlyList<TestNode> nodes)
        => new(window, editor, nodes);

    /// <summary>
    /// Прогоняет layout: без этого контейнеры не реализованы и Bounds пустые.
    /// </summary>
    public void RunLayout()
    {
        var manager = Window.GetLayoutManager();
        manager?.ExecuteInitialLayoutPass();
        manager?.ExecuteLayoutPass();
    }

    public DesignEditorItem Container(int index)
    {
        var container = Editor.ContainerFromItem(Nodes[index]) as DesignEditorItem;
        Assert.NotNull(container);
        return container!;
    }

    /// <summary>
    /// Размещает контейнер в мировых координатах и задаёт ему размер.
    /// </summary>
    public DesignEditorItem PlaceContainer(int index, Point location, Size size)
    {
        var container = Container(index);
        container.Width = size.Width;
        container.Height = size.Height;
        container.HorizontalAlignment = HorizontalAlignment.Left;
        container.VerticalAlignment = VerticalAlignment.Top;
        container.Location = location;
        RunLayout();
        return container;
    }

    /// <summary>
    /// Находит вложенный <see cref="Border"/> внутри контейнера.
    /// </summary>
    public Border Nested(int index) => Named(index, "Nested");

    public Border Named(int index, string name) => Find<Border>(index, name);

    /// <summary>
    /// Находит вложенный контрол заданного типа по имени.
    /// </summary>
    public T Find<T>(int index, string name) where T : Control
    {
        var control = Container(index).GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(c => c.Name == name);
        Assert.NotNull(control);
        return control!;
    }

    /// <summary>
    /// Возвращает точку в координатах редактора, соответствующую центру контрола.
    /// </summary>
    public Point CentreOf(Control control)
        => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), Editor) ?? default;
}
