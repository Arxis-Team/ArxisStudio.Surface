using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Tests;

/// <summary>
/// Куда ляжет брошенный контрол: <see cref="UiDesignerView.TryResolveDropPlacement"/> (ADR 0024).
/// </summary>
/// <remarks>
/// Формы — настоящие окна из разметки, которые держит элемент формы, как у живого хоста: так проверяется
/// и то, что окно отдаёт содержимое элементу, и то, что бросок находит его там.
/// </remarks>
public class DropPlacementTests
{
    private const string Avalonia = "https://github.com/avaloniaui";
    private const string Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly Point FormLocation = new(100, 100);

    private sealed class FormModel(object root)
    {
        public object Root { get; } = root;

        public Point Location { get; } = FormLocation;
    }

    private static Window Form(string content) =>
        (Window)AvaloniaRuntimeXamlLoader.Parse(
            $"<Window xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\" Width=\"400\" Height=\"300\">{content}</Window>");

    private static (UiDesignerView Editor, UiDesignerFormItem Item) Designer(Window form)
    {
        var editor = new UiDesignerView
        {
            ItemsSource = new[] { new FormModel(form) },
            ItemLocationBinding = new Binding(nameof(FormModel.Location)),
            ItemRootBinding = new Binding(nameof(FormModel.Root)),
        };

        var host = new Window { Width = 900, Height = 700, Content = editor };
        host.Show();
        host.UpdateLayout();

        return (editor, Assert.IsType<UiDesignerFormItem>(editor.ContainerFromIndex(0)));
    }

    private static T Named<T>(UiDesignerFormItem item, string name) where T : Control =>
        item.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    /// <summary>Точка внутри контрола, в координатах редактора.</summary>
    private static Point At(UiDesignerView editor, Control control, double x, double y) =>
        control.TranslatePoint(new Point(x, y), editor)!.Value;

    /// <summary>Рамка контрола в координатах поверхности.</summary>
    private static Rect WorldBounds(UiDesignerView editor, Control control) =>
        new(editor.GetWorldPosition(At(editor, control, 0, 0)), control.Bounds.Size);

    private static SurfaceDropPlacement Resolve(UiDesignerView editor, Point at, Control? dragged = null)
    {
        Assert.True(editor.TryResolveDropPlacement(at, out var placement, dragged), "Бросок обязан найти место.");
        return placement!;
    }

    private const string Stack = """
        <StackPanel x:Name="Stack" Margin="20">
          <Button x:Name="A" Content="A" Height="30" />
          <Button x:Name="B" Content="B" Height="30" />
          <Button x:Name="C" Content="C" Height="30" />
        </StackPanel>
        """;

    [AvaloniaFact]
    public void Drop_Over_StackPanel_Uses_The_Reorder_Rule()
    {
        var (editor, item) = Designer(Form(Stack));
        var stack = Named<StackPanel>(item, "Stack");
        var b = Named<Button>(item, "B");
        var c = Named<Button>(item, "C");

        // До середины соседа — перед ним.
        var before = Resolve(editor, At(editor, b, 10, 5));

        Assert.Equal(SurfaceDropKind.Insert, before.Kind);
        Assert.Same(stack, before.Parent);
        Assert.Equal(1, before.Index);
        Assert.Same(b, before.Anchor);
        Assert.True(before.IsLine, "В потоке место между соседями — линия.");
        Assert.Equal(WorldBounds(editor, b).Top, before.Indicator.Top, 3);
        Assert.Equal(b.Bounds.Width, before.Indicator.Width, 3);

        // После середины — за ним.
        var after = Resolve(editor, At(editor, b, 10, 25));

        Assert.Equal(2, after.Index);
        Assert.Same(c, after.Anchor);

        // За последним — в конец, и линия под последним.
        var end = Resolve(editor, At(editor, c, 10, 25));

        Assert.Equal(3, end.Index);
        Assert.Null(end.Anchor);
        Assert.Equal(WorldBounds(editor, c).Bottom, end.Indicator.Top, 3);
    }

    [AvaloniaFact]
    public void Drop_Over_Grid_Resolves_A_Cell()
    {
        var (editor, item) = Designer(Form("""
            <Grid x:Name="Table" ColumnDefinitions="100,150" RowDefinitions="50,60"
                  ColumnSpacing="10" RowSpacing="5" Background="Transparent"
                  HorizontalAlignment="Left" VerticalAlignment="Top" />
            """));
        var table = Named<Grid>(item, "Table");
        var origin = WorldBounds(editor, table).TopLeft;

        var far = Resolve(editor, At(editor, table, 130, 70));

        Assert.Equal(SurfaceDropKind.Cell, far.Kind);
        Assert.Same(table, far.Parent);
        Assert.Equal(1, far.Row);
        Assert.Equal(1, far.Column);
        Assert.Equal(new Rect(origin.X + 110, origin.Y + 55, 150, 60), far.Indicator);

        // Промежуток между столбцами отдан следующему: указатель целится уже за край предыдущего.
        var gap = Resolve(editor, At(editor, table, 105, 20));

        Assert.Equal(0, gap.Row);
        Assert.Equal(1, gap.Column);
    }

    [AvaloniaFact]
    public void Drop_Over_Empty_Border_Resolves_Content()
    {
        var (editor, item) = Designer(Form("""
            <Canvas x:Name="Board" Background="Transparent">
              <Border x:Name="Empty" Canvas.Left="40" Canvas.Top="40" Width="120" Height="80" />
            </Canvas>
            """));
        var empty = Named<Border>(item, "Empty");

        var placement = Resolve(editor, At(editor, empty, 60, 40));

        Assert.Equal(SurfaceDropKind.Content, placement.Kind);
        Assert.Same(empty, placement.Parent);
        Assert.Equal(WorldBounds(editor, empty), placement.Indicator);
        Assert.False(placement.IsLine);
    }

    [AvaloniaFact]
    public void A_Border_With_A_Child_Passes_The_Drop_To_Its_Parent()
    {
        var (editor, item) = Designer(Form("""
            <Canvas x:Name="Board" Background="Transparent">
              <Border x:Name="Full" Canvas.Left="40" Canvas.Top="40" Width="120" Height="80" Padding="10">
                <TextBlock Text="taken" />
              </Border>
            </Canvas>
            """));
        var board = Named<Canvas>(item, "Board");
        var full = Named<Border>(item, "Full");

        // Заменять содержимое молча значило бы удалить то, что там было: бросок уходит родителю.
        var placement = Resolve(editor, At(editor, full, 60, 40));

        Assert.Equal(SurfaceDropKind.Position, placement.Kind);
        Assert.Same(board, placement.Parent);
    }

    [AvaloniaFact]
    public void Drop_Over_Canvas_Resolves_A_Position_In_Its_Own_Coordinates()
    {
        var (editor, item) = Designer(Form("""
            <StackPanel>
              <Canvas x:Name="Board" Margin="30,50,0,0" Width="200" Height="150" Background="Transparent" />
            </StackPanel>
            """));
        var board = Named<Canvas>(item, "Board");

        var placement = Resolve(editor, At(editor, board, 40, 25));

        Assert.Equal(SurfaceDropKind.Position, placement.Kind);
        Assert.Same(board, placement.Parent);
        Assert.Equal(40, placement.Position.X, 3);
        Assert.Equal(25, placement.Position.Y, 3);
        Assert.Equal(WorldBounds(editor, board), placement.Indicator);
    }

    private const string Tabs = """
        <TabControl x:Name="Tabs">
          <TabItem Header="One">
            <StackPanel x:Name="Page">
              <TextBlock x:Name="First" Text="first page" Height="20" />
            </StackPanel>
          </TabItem>
          <TabItem x:Name="Second" Header="Two" />
        </TabControl>
        """;

    [AvaloniaFact]
    public void Drop_Into_A_TabControl_Lands_In_The_Selected_Page()
    {
        var (editor, item) = Designer(Form(Tabs));
        var page = Named<StackPanel>(item, "Page");
        var first = Named<TextBlock>(item, "First");

        // Ниже текста, но на странице: страница растянута на всю область содержимого.
        var placement = Resolve(editor, At(editor, page, 20, first.Bounds.Height + 30));

        Assert.Equal(SurfaceDropKind.Insert, placement.Kind);
        Assert.Same(page, placement.Parent);
        Assert.Equal(1, placement.Index);
        Assert.Null(placement.Anchor);
        Assert.Equal(WorldBounds(editor, first).Bottom, placement.Indicator.Top, 3);
    }

    [AvaloniaFact]
    public void An_Empty_Tab_Page_Takes_The_Drop_As_Its_Content()
    {
        var (editor, item) = Designer(Form(Tabs));
        var tabs = Named<TabControl>(item, "Tabs");

        tabs.SelectedIndex = 1;
        editor.UpdateLayout();

        var second = Assert.IsType<TabItem>(tabs.ContainerFromIndex(1));
        var area = tabs.GetVisualDescendants()
            .OfType<Control>()
            .Single(control => control.Name == "PART_SelectedContentHost");

        var placement = Resolve(editor, At(editor, area, area.Bounds.Width / 2, area.Bounds.Height / 2));

        Assert.Equal(SurfaceDropKind.Content, placement.Kind);
        Assert.Same(second, placement.Parent);
        Assert.Equal(WorldBounds(editor, area), placement.Indicator);
    }

    [AvaloniaFact]
    public void An_Empty_Window_Takes_The_Drop_As_Its_Content()
    {
        var window = Form(string.Empty);
        var (editor, item) = Designer(window);

        var placement = Resolve(editor, At(editor, item, 50, 50));

        Assert.Equal(SurfaceDropKind.Content, placement.Kind);
        Assert.Same(window, placement.Parent);
        Assert.Same(item, placement.Container);
        Assert.Equal(new Rect(FormLocation, new Size(400, 300)), placement.Indicator);
    }

    [AvaloniaFact]
    public void A_Window_With_Content_Is_Not_Taken_For_An_Empty_One()
    {
        // Пока элемент держит окно, Content окна пуст у всякого: содержимое занято элементом.
        var (editor, item) = Designer(Form("<TextBlock x:Name=\"Only\" Text=\"only child\" />"));

        Assert.False(editor.TryResolveDropPlacement(At(editor, item, 50, 50), out var placement));
        Assert.Null(placement);
    }

    [AvaloniaFact]
    public void Nothing_Takes_A_Drop_Beside_The_Forms()
    {
        var (editor, _) = Designer(Form(Stack));

        Assert.False(editor.TryResolveDropPlacement(new Point(20, 20), out var placement));
        Assert.Null(placement);
    }

    [AvaloniaFact]
    public void A_Carried_Control_Does_Not_Take_Itself()
    {
        var (editor, item) = Designer(Form("""
            <StackPanel x:Name="Stack" Margin="20">
              <Button x:Name="A" Content="A" Height="30" />
              <Border x:Name="Carried" Height="40" Background="Gray" />
              <Button x:Name="C" Content="C" Height="30" />
            </StackPanel>
            """));
        var stack = Named<StackPanel>(item, "Stack");
        var carried = Named<Border>(item, "Carried");
        var c = Named<Button>(item, "C");

        // Без него пустая рамка приняла бы бросок содержимым.
        Assert.Equal(SurfaceDropKind.Content, Resolve(editor, At(editor, carried, 10, 30)).Kind);

        // С ним она сама себя не принимает. Место перед ней — место, откуда её уносят, и якорем она не
        // становится: место называется соседом за ней.
        var placement = Resolve(editor, At(editor, carried, 10, 5), dragged: carried);

        Assert.Equal(SurfaceDropKind.Insert, placement.Kind);
        Assert.Same(stack, placement.Parent);
        Assert.Equal(1, placement.Index);
        Assert.Same(c, placement.Anchor);
    }

    [AvaloniaFact]
    public void A_Frozen_Surface_Takes_No_Drop()
    {
        var (editor, item) = Designer(Form(Stack));
        var b = Named<Button>(item, "B");

        using (editor.Freeze())
            Assert.False(editor.TryResolveDropPlacement(At(editor, b, 10, 5), out _));

        Assert.True(editor.TryResolveDropPlacement(At(editor, b, 10, 5), out _));
    }

    [AvaloniaFact]
    public void The_Indicator_Draws_A_Line_Or_An_Area()
    {
        var (editor, item) = Designer(Form("""
            <StackPanel x:Name="Stack" Margin="20">
              <Button x:Name="A" Content="A" Height="30" />
              <Border x:Name="Empty" Height="40" />
            </StackPanel>
            """));
        var line = editor.GetVisualDescendants().OfType<Rectangle>().Single(shape => shape.Name == "PART_DropLine");
        var area = editor.GetVisualDescendants().OfType<Rectangle>().Single(shape => shape.Name == "PART_DropArea");

        var changes = 0;
        editor.PropertyChanged += (_, e) =>
        {
            if (e.Property == UiDesignerView.DropIndicatorProperty)
                changes++;
        };

        var between = Resolve(editor, At(editor, Named<Button>(item, "A"), 10, 5));
        editor.ShowDropIndicator(between);
        editor.ShowDropIndicator(between with { });

        Assert.Same(between, editor.DropIndicator);
        Assert.True(line.IsVisible);
        Assert.False(area.IsVisible);
        Assert.Equal(1, changes);

        var inside = Resolve(editor, At(editor, Named<Border>(item, "Empty"), 10, 20));
        editor.ShowDropIndicator(inside);

        Assert.False(line.IsVisible);
        Assert.True(area.IsVisible);

        editor.HideDropIndicator();

        Assert.Null(editor.DropIndicator);
        Assert.False(line.IsVisible);
        Assert.False(area.IsVisible);
    }

    [AvaloniaFact]
    public void Controls_On_The_Selected_Tab_Page_Are_Selectable()
    {
        // Бросок и выбор берут одних кандидатов: страница, принимающая бросок, обязана и выбираться.
        var (editor, item) = Designer(Form(Tabs));
        var first = Named<TextBlock>(item, "First");
        var host = (Window)TopLevel.GetTopLevel(editor)!;

        var point = At(editor, first, 10, 10);
        host.MouseDown(point, MouseButton.Left);
        host.MouseUp(point, MouseButton.Left);

        Assert.Same(first, editor.PrimarySelectionTarget?.Target);
    }
}
