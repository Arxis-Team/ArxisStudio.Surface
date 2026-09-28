using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface;
using ArxisStudio.Surface.Nodes;

namespace ArxisStudio.Tests;

/// <summary>
/// Заголовок узла и его базовый вид (ADR 0009): название на полосе, область — по заголовку или полосе.
/// </summary>
/// <remarks>
/// Стенд: окно 800 × 600, редактор узлов над рядом моделей через 200 — с названием и полосой, с одним
/// названием, с одной полосой и без обоих — с привязками положения, заголовка и полосы; тело узла —
/// прямоугольник 120 × 40.
/// </remarks>
public class NodeHeaderTests
{
    /// <summary>Тёмная полоса: светлый текст на ней контрастнее.</summary>
    private static readonly Color Dark = Color.Parse("#3B6FB6");

    /// <summary>Светлая полоса: тёмный текст на ней контрастнее.</summary>
    private static readonly Color Light = Color.Parse("#F7D046");

    private sealed class Card(string? title, Color? accent, Point location) : INotifyPropertyChanged
    {
        private string? _title = title;
        private Color? _accent = accent;
        private Point _location = location;

        public string? Title
        {
            get => _title;
            set => Set(ref _title, value);
        }

        public Color? Accent
        {
            get => _accent;
            set => Set(ref _accent, value);
        }

        public Point Location
        {
            get => _location;
            set => Set(ref _location, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    private sealed record Stand(Window Window, NodeEditor Editor, ObservableCollection<object> Items)
    {
        public Node Node(int index) => (Node)Editor.ContainerFromIndex(index)!;

        public Card Model(int index) => (Card)Items[index];

        public void RunLayout()
        {
            var manager = Window.GetLayoutManager();
            manager?.ExecuteInitialLayoutPass();
            manager?.ExecuteLayoutPass();
        }

        public object? Resource(string key) =>
            Editor.TryFindResource(key, Editor.ActualThemeVariant, out var value) ? value : null;
    }

    private static Stand Create(IDataTemplate? template = null)
    {
        var items = new ObservableCollection<object>
        {
            new Card("Размытие", Dark, new Point(0, 0)),
            new Card("Число", null, new Point(200, 0)),
            new Card(null, Dark, new Point(400, 0)),
            new Card(null, null, new Point(600, 0))
        };

        var editor = new NodeEditor
        {
            ItemsSource = items,
            ItemTemplate = template ?? new FuncDataTemplate<Card>((_, _) => new Border { Width = 120, Height = 40 }),
            ItemLocationBinding = new Binding(nameof(Card.Location)),
            ItemHeaderBinding = new Binding(nameof(Card.Title)),
            ItemAccentBinding = new Binding(nameof(Card.Accent))
        };

        var window = new Window { Width = 800, Height = 600, Content = editor };
        window.Show();
        var stand = new Stand(window, editor, items);
        stand.RunLayout();
        return stand;
    }

    private static T Part<T>(Node node, string name)
        where T : Control =>
        node.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    [AvaloniaFact]
    public void A_Node_Takes_Its_Header_From_The_Binding()
    {
        var stand = Create();
        Assert.Equal("Размытие", stand.Node(0).Header);

        stand.Model(0).Title = "Резкость";
        Assert.Equal("Резкость", stand.Node(0).Header);
    }

    [AvaloniaFact]
    public void The_Header_Area_Shows_With_A_Title_Or_A_Band()
    {
        // Название, полоса, одно из двух — область есть; ни того ни другого — её нет. Залита она
        // полосой, а высотой — ключ темы.
        var stand = Create();
        var height = Assert.IsType<double>(stand.Resource("NodeEditor.Node.HeaderHeight"));

        Assert.True(Part<Border>(stand.Node(0), "PART_Header").IsVisible);
        Assert.True(Part<Border>(stand.Node(1), "PART_Header").IsVisible);
        Assert.True(Part<Border>(stand.Node(2), "PART_Header").IsVisible);
        Assert.False(Part<Border>(stand.Node(3), "PART_Header").IsVisible);

        var header = Part<Border>(stand.Node(0), "PART_Header");
        Assert.Same(stand.Node(0).Accent, header.Background);
        Assert.Equal(height, header.Bounds.Height);
        Assert.Null(Part<Border>(stand.Node(1), "PART_Header").Background);
    }

    [AvaloniaFact]
    public void The_Title_Is_Light_On_A_Dark_Band_And_Dark_On_A_Light_One()
    {
        // Контраст держится при любом цвете хоста; без полосы название — цветом текста карточки.
        var stand = Create();
        Brush(stand, 0, "NodeEditor.Node.HeaderForeground");

        stand.Model(0).Accent = Light;
        Brush(stand, 0, "NodeEditor.Node.HeaderForegroundOnLight");

        Brush(stand, 1, "NodeEditor.Node.Foreground");
    }

    private static void Brush(Stand stand, int index, string key)
    {
        var presenter = Part<ContentPresenter>(stand.Node(index), "PART_HeaderPresenter");
        Assert.Same(stand.Resource(key), presenter.Foreground);
    }

    [AvaloniaFact]
    public void A_Recycled_Node_Lets_Go_Of_Its_Header()
    {
        var stand = Create();
        var node = stand.Node(0);

        stand.Editor.ViewportLocation = new Point(5000, 5000);
        stand.RunLayout();
        Assert.Null(stand.Editor.ContainerFromIndex(0));
        Assert.Null(node.Header);

        stand.Model(0).Title = "Резкость";
        stand.Editor.ViewportLocation = default;
        stand.RunLayout();
        Assert.Equal("Резкость", stand.Node(0).Header);
    }

    [AvaloniaFact]
    public void A_New_Header_Binding_Retitles_Realized_Nodes()
    {
        var stand = Create();

        stand.Editor.ItemHeaderBinding = null;
        Assert.Null(stand.Node(0).Header);

        stand.Editor.ItemHeaderBinding = new Binding(nameof(Card.Title));
        Assert.Equal("Размытие", stand.Node(0).Header);
    }

    [AvaloniaFact]
    public void A_Reroute_Has_No_Header()
    {
        // Перевалка сама себе карточка: заголовка у неё нет, даже если хост его дал.
        var stand = Create(new FuncDataTemplate<Card>((_, _) => new Reroute()));

        Assert.False(Part<Border>(stand.Node(0), "PART_Header").IsVisible);
    }

    [AvaloniaFact]
    public void The_Simplified_Band_Is_As_High_As_The_Header()
    {
        // Упрощённая карточка — копия узла: её полоса той же высоты, что область заголовка.
        var stand = Create();
        var layer = stand.Editor.GetVisualDescendants().OfType<SurfaceSimplifiedLayer>().Single();

        Assert.Equal(Part<Border>(stand.Node(0), "PART_Header").Bounds.Height, layer.AccentHeight);
    }
}
