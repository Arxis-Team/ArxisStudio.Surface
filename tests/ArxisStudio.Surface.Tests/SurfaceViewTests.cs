using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Ядро само по себе: голый <see cref="SurfaceView"/>, без дизайнера форм.
/// </summary>
/// <remarks>
/// Ради этого ядро и отделялось — редактор графов или свободной графики возьмёт его, не
/// наследуя семантики форм. До появления своей темы и своей панели это было неправдой:
/// без <c>DesignEditor</c> поверхность не рисовала ничего, и ни один тест этого не видел,
/// потому что все тесты шли через дизайнер форм.
/// <para>
/// Стенд: окно 800 × 600, два элемента 100 × 60 в (100, 100) и (300, 100).
/// </para>
/// </remarks>
public class SurfaceViewTests
{
    private static readonly Size ItemSize = new(100, 60);

    private static Point LocationOf(int index) => new(100 + (index * 200), 100);

    private sealed record Stand(Window Window, SurfaceView View)
    {
        public SurfaceItem Item(int index) => (SurfaceItem)View.ContainerFromIndex(index)!;

        public Point CentreOf(int index) => LocationOf(index) + new Vector(ItemSize.Width / 2, ItemSize.Height / 2);

        public void RunLayout()
        {
            var manager = Window.GetLayoutManager();
            manager?.ExecuteInitialLayoutPass();
            manager?.ExecuteLayoutPass();
        }
    }

    private static Stand Create()
    {
        var view = new SurfaceView { ItemsSource = new[] { "Первый", "Второй" } };
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();

        var stand = new Stand(window, view);
        stand.RunLayout();

        for (var i = 0; i < 2; i++)
        {
            var item = stand.Item(i);
            item.Width = ItemSize.Width;
            item.Height = ItemSize.Height;
            item.Location = LocationOf(i);
        }

        stand.RunLayout();
        return stand;
    }

    private static void Click(Stand stand, Point at, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        stand.Window.MouseDown(at, MouseButton.Left, modifiers);
        stand.Window.MouseUp(at, MouseButton.Left, modifiers);
        stand.RunLayout();
    }

    [AvaloniaFact]
    public void The_Core_Has_Its_Own_Template_And_Containers()
    {
        var stand = Create();

        Assert.NotNull(stand.View.Grid);
        Assert.Equal(typeof(SurfaceItem), stand.Item(0).GetType());
        Assert.Equal(typeof(SurfaceItem), stand.Item(1).GetType());
    }

    [AvaloniaFact]
    public void Items_Stand_At_Their_Location()
    {
        var stand = Create();

        Assert.Equal(LocationOf(1), stand.Item(1).Bounds.Position);

        stand.Item(1).Location = new Point(420, 250);
        stand.RunLayout();

        Assert.Equal(new Point(420, 250), stand.Item(1).Bounds.Position);
    }

    [AvaloniaFact]
    public void The_Extent_Covers_The_Items()
    {
        var stand = Create();

        Assert.Equal(new Rect(100, 100, 300, 60), stand.View.ItemsExtent);
    }

    [AvaloniaFact]
    public void A_Click_Selects_The_Item_And_It_Shows_It()
    {
        var stand = Create();

        Click(stand, stand.CentreOf(1));

        var target = Assert.Single(stand.View.SelectedTargets);
        Assert.Same(stand.Item(1), target.Target);
        Assert.Equal(SurfaceSelectionScope.Container, target.Scope);

        // Ручек у ядра нет: выбранный элемент показывает себя рамкой своей темы.
        var border = stand.Item(1).GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_SelectionBorder");
        Assert.True(border.IsVisible, "Выбранный элемент ядра обязан быть виден выбранным.");
    }

    [AvaloniaFact]
    public void A_Click_On_The_Empty_Canvas_Gives_The_Surface_Focus()
    {
        // Фокус поверхность просит сама, на нажатии, — но просьбу выполняют только
        // фокусируемому. Разрешение стояло у дизайнера форм и у контейнера, и на голой
        // поверхности щелчок по пустому холсту клавиатуру ей не давал.
        var stand = Create();

        Click(stand, new Point(700, 500));

        Assert.True(stand.View.IsFocused, "Поверхность обязана взять фокус щелчком по пустому холсту.");
    }

    [AvaloniaFact]
    public void Selecting_By_Index_Is_Published_Too()
    {
        var stand = Create();
        var events = 0;
        stand.View.SurfaceSelectionChanged += (_, _) => events++;

        // Хост пишет индексный слой мимо указателя — выделение обязано опубликоваться.
        stand.View.SelectedIndex = 1;

        Assert.Same(stand.Item(1), Assert.Single(stand.View.SelectedTargets).Target);
        Assert.Equal(1, events);
    }

    [AvaloniaFact]
    public void A_Drag_Moves_The_Item_As_One_Edit_That_Undoes()
    {
        var stand = Create();
        var edits = new List<SurfaceEditCompletedEventArgs>();
        stand.View.EditCompleted += (_, e) => edits.Add(e);

        var from = stand.CentreOf(0);
        stand.Window.MouseDown(from, MouseButton.Left);
        stand.Window.MouseMove(from + new Vector(10, 5));
        stand.Window.MouseMove(from + new Vector(40, 30));
        stand.Window.MouseUp(from + new Vector(40, 30), MouseButton.Left);
        stand.RunLayout();

        Assert.Equal(LocationOf(0) + new Vector(40, 30), stand.Item(0).Location);
        Assert.Equal(stand.Item(0).Location, stand.Item(0).Bounds.Position);

        var edit = Assert.Single(edits);
        Assert.Equal(SurfaceEditKind.Move, edit.Kind);
        foreach (var change in edit.Changes)
            stand.View.Revert(change);

        Assert.Equal(LocationOf(0), stand.Item(0).Location);
    }

    [AvaloniaFact]
    public void A_Marquee_On_The_Empty_Canvas_Selects_Both()
    {
        var stand = Create();

        stand.Window.MouseDown(new Point(60, 60), MouseButton.Left);
        stand.Window.MouseMove(new Point(200, 120));
        stand.Window.MouseMove(new Point(500, 250));
        stand.Window.MouseUp(new Point(500, 250), MouseButton.Left);
        stand.RunLayout();

        Assert.Equal(2, stand.View.SelectedTargetsCount);
    }

    [AvaloniaFact]
    public void Arrows_Move_The_Selected_Item()
    {
        var stand = Create();
        Click(stand, stand.CentreOf(0));

        stand.Window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        stand.Window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.Shift);

        Assert.Equal(LocationOf(0) + new Vector(1, 10), stand.Item(0).Location);
    }

    [AvaloniaFact]
    public void The_Core_Theme_Needs_No_Other_Layer()
    {
        // Ключ, который тема ядра называет, обязан быть в ней самой: иначе ядро без
        // инструментов и дизайнера форм рисовало бы молча пустые кисти.
        var themes = Path.Combine(ThisDirectory(), "..", "..", "src", "Surface", "Themes");
        var defined = new HashSet<string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.GetFiles(themes, "*.axaml"))
        {
            var text = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, "x:Key=\"([^\"{]+)\""))
                defined.Add(m.Groups[1].Value);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, "(?:Dynamic|Static)Resource ([A-Za-z0-9_.]+)"))
                used.Add(m.Groups[1].Value);
        }

        Assert.NotEmpty(used);
        var missing = used.Except(defined).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, "Тема ядра называет ключи, которых в ней нет:\n" + string.Join("\n", missing));
    }

    private static string ThisDirectory([System.Runtime.CompilerServices.CallerFilePath] string? thisFile = null)
        => Path.GetDirectoryName(thisFile)!;
}
