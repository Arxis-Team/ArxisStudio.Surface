using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Контейнер как хост формы, загруженной из <c>.axaml</c>.
/// </summary>
/// <remarks>
/// Разметку никто не размечал designer-метаданными, и она интерактивна:
/// это два условия, которых нет у шаблонов, написанных вместе с приложением.
/// </remarks>
public class LoadedContentTests
{
    private const string Markup = """
        <UserControl xmlns='https://github.com/avaloniaui'
                     xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
          <StackPanel x:Name='Root' Spacing='10'>
            <TextBlock x:Name='Title' Text='Loaded Form' Height='24' />
            <TextBox x:Name='Field' Height='30' />
            <Button x:Name='Action' Content='Save' Height='30' />
          </StackPanel>
        </UserControl>
        """;

    private static readonly Point CardLocation = new(100, 100);
    private static readonly Size CardSize = new(300, 240);

    private static (EditorHarness Harness, UiDesignerItem Container) Create(SurfaceContentMode mode)
    {
        var nodes = new[] { new TestNode("loaded") };

        var editor = new UiDesignerView
        {
            ItemsSource = nodes,
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<TestNode>(
                (_, _) => (Control)AvaloniaRuntimeXamlLoader.Parse(Markup),
                supportsRecycling: false)
        };

        editor.InteractionOptions.IsSnapToGridEnabled = false;

        var window = new Window { Width = 800, Height = 600, Content = editor };
        window.Show();

        var harness = EditorHarness.Adopt(window, editor, nodes);
        harness.RunLayout();

        var container = harness.Container(0);
        container.ContentMode = mode;
        harness.PlaceContainer(0, CardLocation, CardSize);

        return (harness, container);
    }

    private static T Find<T>(EditorHarness harness, string name) where T : Control
        => harness.Container(0).GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    [AvaloniaFact]
    public void Live_Content_Swallows_The_Press_Without_The_Loaded_Mode()
    {
        var (harness, _) = Create(SurfaceContentMode.Annotated);
        var action = Find<Button>(harness, "Action");

        var clicked = false;
        action.Click += (_, _) => clicked = true;

        var centre = harness.CentreOf(action);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseUp(centre, MouseButton.Left);
        harness.RunLayout();

        // Ради этих двух строк и появился режим Loaded: загруженная форма
        // интерактивна, кнопка обрабатывает нажатие сама, до контейнера оно
        // не доходит — и выделения не возникает вовсе.
        Assert.True(clicked);
        Assert.Null(harness.Editor.PrimarySelectionTarget);
    }

    [AvaloniaFact]
    public void Loaded_Mode_Selects_The_Element_Under_The_Pointer()
    {
        var (harness, _) = Create(SurfaceContentMode.Loaded);
        var action = Find<Button>(harness, "Action");

        var centre = harness.CentreOf(action);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseUp(centre, MouseButton.Left);
        harness.RunLayout();

        Assert.Same(action, harness.Editor.PrimarySelectionTarget!.Target);
        Assert.Equal(SurfaceSelectionScope.NestedTarget, harness.Editor.PrimarySelectionTarget.Scope);
    }

    /// <summary>
    /// Дорога с той стороны: хост выбирает контрол сам.
    /// </summary>
    /// <remarks>
    /// У приложения со своим деревом клик по строке должен выделять на поверхности. Выделение
    /// в обратную сторону было всегда, а этого не было вовсе, и обойтись без внутренностей
    /// редактора было нечем.
    /// </remarks>
    [AvaloniaFact]
    public void SelectTarget_Selects_A_Control_The_Host_Names()
    {
        var (harness, _) = Create(SurfaceContentMode.Loaded);
        var action = Find<Button>(harness, "Action");

        Assert.True(harness.Editor.SelectTarget(action));
        harness.RunLayout();

        Assert.Same(action, harness.Editor.PrimarySelectionTarget!.Target);
        Assert.Equal(SurfaceSelectionScope.NestedTarget, harness.Editor.PrimarySelectionTarget.Scope);
    }

    [AvaloniaFact]
    public void SelectTarget_Replaces_The_Selection_Unless_Asked_To_Add()
    {
        var (harness, _) = Create(SurfaceContentMode.Loaded);
        var action = Find<Button>(harness, "Action");
        var field = Find<TextBox>(harness, "Field");

        harness.Editor.SelectTarget(action);
        harness.Editor.SelectTarget(field);
        harness.RunLayout();

        Assert.Equal(1, harness.Editor.SelectedTargetsCount);

        harness.Editor.SelectTarget(action, additive: true);
        harness.RunLayout();

        Assert.Equal(2, harness.Editor.SelectedTargetsCount);
    }

    /// <summary>
    /// Добавление добавляет, и повторное добавление ничего не снимает.
    /// </summary>
    /// <remarks>
    /// Хост, отражающий выделение своего дерева построчно, вызывает метод на каждую строку. Если бы
    /// добавление переключало — как это делает повторный клик со Shift, где снятие осознанно, —
    /// повторная отправка того же набора снимала бы выбор ровно с тех строк, которые подтверждала.
    /// </remarks>
    [AvaloniaFact]
    public void SelectTarget_Additive_Is_Idempotent()
    {
        var (harness, _) = Create(SurfaceContentMode.Loaded);
        var action = Find<Button>(harness, "Action");
        var field = Find<TextBox>(harness, "Field");

        harness.Editor.SelectTarget(action);
        harness.Editor.SelectTarget(field, additive: true);
        harness.RunLayout();

        Assert.Equal(2, harness.Editor.SelectedTargetsCount);

        // Тот же набор ещё раз.
        harness.Editor.SelectTarget(action, additive: true);
        harness.Editor.SelectTarget(field, additive: true);
        harness.RunLayout();

        Assert.Equal(2, harness.Editor.SelectedTargetsCount);
    }

    [AvaloniaFact]
    public void SelectTarget_Declines_A_Control_That_Is_Not_Editable()
    {
        // Annotated, и ничего не размечено: редактировать нечего, и метод об этом говорит,
        // а не делает вид, что выбрал.
        var (harness, _) = Create(SurfaceContentMode.Annotated);
        var action = Find<Button>(harness, "Action");

        Assert.False(harness.Editor.SelectTarget(action));
        Assert.Null(harness.Editor.PrimarySelectionTarget);
    }

    [AvaloniaFact]
    public void Loaded_Mode_Does_Not_Expose_Control_Internals()
    {
        var (harness, _) = Create(SurfaceContentMode.Loaded);
        var action = Find<Button>(harness, "Action");

        var centre = harness.CentreOf(action);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseUp(centre, MouseButton.Left);
        harness.RunLayout();

        // Выбирается сама кнопка, а не её внутренний ContentPresenter:
        // у частей шаблона задан TemplatedParent.
        var selected = harness.Editor.PrimarySelectionTarget!.Target;
        Assert.Null(selected.TemplatedParent);
        Assert.IsType<Button>(selected);
    }

    [AvaloniaFact]
    public void Loaded_Content_Does_Not_React_To_Input()
    {
        var (harness, _) = Create(SurfaceContentMode.Loaded);
        var action = Find<Button>(harness, "Action");

        var clicked = false;
        action.Click += (_, _) => clicked = true;

        var centre = harness.CentreOf(action);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseUp(centre, MouseButton.Left);
        harness.RunLayout();

        // Форма под редактированием не должна срабатывать: иначе редактор
        // нажимал бы кнопки вместо того, чтобы их выделять.
        Assert.False(clicked);
        Assert.False(action.IsFocused);
    }

    [AvaloniaFact]
    public void Loaded_Mode_Reports_The_Layout_Of_The_Selected_Element()
    {
        var (harness, _) = Create(SurfaceContentMode.Loaded);
        var action = Find<Button>(harness, "Action");

        var centre = harness.CentreOf(action);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseUp(centre, MouseButton.Left);
        harness.RunLayout();

        // Стратегии размещения работают с загруженной формой без изменений:
        // корень разметки — StackPanel, значит перестановка, а не координаты.
        Assert.Equal("Stack", harness.Editor.PrimarySelectionPlacement);
        Assert.Equal(ArxisStudio.Surface.MovePolicy.None, harness.Editor.PrimarySelectionMovePolicy);
    }

    [AvaloniaFact]
    public void Loaded_Element_Can_Be_Resized()
    {
        var (harness, container) = Create(SurfaceContentMode.Loaded);
        var action = Find<Button>(harness, "Action");

        var centre = harness.CentreOf(action);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseUp(centre, MouseButton.Left);
        harness.RunLayout();

        var before = harness.Editor.GetTargetSize(action).Height;

        var state = new ArxisStudio.Surface.Editing.ItemResizingState(container, action, ResizeDirection.Bottom);
        container.PushState(state);
        state.OnResizeDelta(new ResizeDeltaEventArgs(
            new Vector(0, 40), ResizeDirection.Bottom, UiDesignerItem.ResizeDeltaEvent));
        harness.RunLayout();

        // Размер honours любая панель, поэтому загруженная форма редактируется
        // по размеру сразу, без разметки.
        Assert.Equal(before + 40, harness.Editor.GetTargetSize(action).Height, 1);
    }
}
