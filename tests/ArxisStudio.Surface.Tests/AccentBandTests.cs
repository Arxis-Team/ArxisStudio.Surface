using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Полоса заголовка у развёрнутого контейнера (ADR 0009): та же, что у карточки в упрощённом виде.
/// </summary>
/// <remarks>
/// Стенд: окно 800 × 600, поверхность над рядом из трёх моделей через 150 — у первой полоса цветом, у
/// второй кистью, у третьей её нет, — с привязками положения и полосы; элемент 100 × 60, масштаб 1.
/// </remarks>
public class AccentBandTests
{
    private static readonly Size ItemSize = new(100, 60);
    private static readonly IBrush Blue = new ImmutableSolidColorBrush(Colors.Blue);

    private sealed class Card(Point location, object? accent) : INotifyPropertyChanged
    {
        private Point _location = location;
        private object? _accent = accent;

        public Point Location
        {
            get => _location;
            set => Set(ref _location, value);
        }

        public object? Accent
        {
            get => _accent;
            set => Set(ref _accent, value);
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

    private sealed record Stand(Window Window, SurfaceView View, ObservableCollection<object> Items)
    {
        public SurfaceItem? Container(int index) => View.ContainerFromIndex(index) as SurfaceItem;

        public Card Model(int index) => (Card)Items[index];

        public void RunLayout()
        {
            var manager = Window.GetLayoutManager();
            manager?.ExecuteInitialLayoutPass();
            manager?.ExecuteLayoutPass();
        }
    }

    private static Stand Create()
    {
        var items = new ObservableCollection<object>
        {
            new Card(new Point(0, 0), Colors.Red),
            new Card(new Point(150, 0), Blue),
            new Card(new Point(300, 0), null)
        };

        var view = new SurfaceView
        {
            ItemsSource = items,
            ItemLocationBinding = new Binding(nameof(Card.Location)),
            ItemAccentBinding = new Binding(nameof(Card.Accent)),
            EstimatedItemSize = ItemSize,
            ItemTemplate = new FuncDataTemplate<Card>((_, _) => new Border { Width = ItemSize.Width, Height = ItemSize.Height })
        };

        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        var stand = new Stand(window, view, items);
        stand.RunLayout();
        return stand;
    }

    private static Control Part(SurfaceItem container, string name) =>
        container.GetVisualDescendants().OfType<Control>().Single(c => c.Name == name);

    [AvaloniaFact]
    public void A_Container_Takes_The_Same_Band_As_Its_Card()
    {
        // Цвет — кистью из общего кеша, той же, что у карточки; кисть — как есть; без значения — пусто.
        var stand = Create();

        Assert.Same(AccentBrushes.From(Colors.Red), stand.Container(0)!.Accent);
        Assert.Same(Blue, stand.Container(1)!.Accent);
        Assert.Null(stand.Container(2)!.Accent);
    }

    [AvaloniaFact]
    public void Editing_The_Model_Repaints_The_Band()
    {
        var stand = Create();

        stand.Model(2).Accent = Colors.Green;
        stand.Model(0).Accent = null;

        Assert.Same(AccentBrushes.From(Colors.Green), stand.Container(2)!.Accent);
        Assert.Null(stand.Container(0)!.Accent);
    }

    [AvaloniaFact]
    public void A_Recycled_Container_Lets_Go_Of_The_Band()
    {
        // Контейнер в пуле хранит прежний контекст данных; не снятая привязка красила бы его полосой
        // прежней модели и после того, как он достался другому элементу.
        var stand = Create();
        var container = stand.Container(0)!;

        stand.View.ViewportLocation = new Point(5000, 5000);
        stand.RunLayout();
        Assert.Null(stand.Container(0));
        Assert.Null(container.Accent);

        stand.Model(0).Accent = Colors.Green;
        Assert.Null(container.Accent);

        stand.View.ViewportLocation = default;
        stand.RunLayout();
        Assert.Same(AccentBrushes.From(Colors.Green), stand.Container(0)!.Accent);
    }

    [AvaloniaFact]
    public void An_Own_Container_Keeps_Its_Band()
    {
        // Готовому контейнеру полосу задал тот, кто его создал. Контекст данных он наследует от
        // поверхности, и привязка, поставленная ему, нашла бы полосу там.
        var stand = Create();
        stand.View.DataContext = new Card(default, Colors.Green);
        var own = new SurfaceItem { Location = new Point(450, 0), Width = 100, Height = 60, Accent = Blue };
        stand.Items.Add(own);
        stand.RunLayout();

        Assert.Same(Blue, own.Accent);
    }

    [AvaloniaFact]
    public void A_New_Binding_Repaints_Realized_Containers()
    {
        var stand = Create();

        stand.View.ItemAccentBinding = null;
        Assert.Null(stand.Container(0)!.Accent);

        stand.View.ItemAccentBinding = new Binding(nameof(Card.Accent));
        Assert.Same(AccentBrushes.From(Colors.Red), stand.Container(0)!.Accent);
    }

    [AvaloniaFact]
    public void The_Theme_Draws_The_Band_Above_The_Content()
    {
        // Полоса — высотой ключа темы, над содержимым; без полосы содержимое стоит у верхнего края.
        var stand = Create();
        stand.View.TryFindResource("Surface.Item.AccentHeight", out var resource);
        var height = Assert.IsType<double>(resource);

        var banded = stand.Container(0)!;
        Assert.True(Part(banded, "PART_Accent").IsVisible);
        Assert.Equal(height, Part(banded, "PART_Accent").Bounds.Height);
        Assert.Equal(height, Part(banded, "PART_ContentPresenter").Bounds.Y);

        var plain = stand.Container(2)!;
        Assert.False(Part(plain, "PART_Accent").IsVisible);
        Assert.Equal(0, Part(plain, "PART_ContentPresenter").Bounds.Y);
    }
}
