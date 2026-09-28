using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Упрощённый вид при малом масштабе (ADR 0008): ниже <see cref="SurfaceView.SimplifiedZoom"/>
/// развёрнуто только закреплённое.
/// </summary>
/// <remarks>
/// Стенд: окно 800 × 600, поверхность над сеткой моделей 20 × 20 — столбцы через 150, ряды через 100,
/// элемент 100 × 60 — с привязкой положения и предполагаемым размером, равным настоящему. Холст стоит
/// в начале координат, масштаб 1; порог — по умолчанию, 0,5.
/// </remarks>
public class SimplifiedViewTests
{
    private static readonly Size ItemSize = new(100, 60);

    private sealed class Card(Point location) : INotifyPropertyChanged
    {
        private Point _location = location;

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

    private sealed record Stand(Window Window, SurfaceView View, ObservableCollection<object> Items)
    {
        public VirtualizingSurfacePanel Panel => View.GetVisualDescendants().OfType<VirtualizingSurfacePanel>().Single();

        public SurfaceItem? Container(int index) => View.ContainerFromIndex(index) as SurfaceItem;

        public int Realized => Enumerable.Range(0, Items.Count).Count(i => Container(i) != null);

        public void RunLayout()
        {
            var manager = Window.GetLayoutManager();
            manager?.ExecuteInitialLayoutPass();
            manager?.ExecuteLayoutPass();
        }

        public void Zoom(double zoom)
        {
            View.ViewportZoom = zoom;
            RunLayout();
        }

        public void Pan(Point location)
        {
            View.ViewportLocation = location;
            RunLayout();
        }
    }

    private static Stand Create(int columns = 20, int rows = 20)
    {
        var items = new ObservableCollection<object>();
        for (var i = 0; i < columns * rows; i++)
            items.Add(new Card(new Point(i % columns * 150, i / columns * 100)));

        var view = new SurfaceView
        {
            ItemsSource = items,
            ItemLocationBinding = new Binding(nameof(Card.Location)),
            EstimatedItemSize = ItemSize,
            ItemTemplate = new FuncDataTemplate<Card>((_, _) => new Border { Width = ItemSize.Width, Height = ItemSize.Height })
        };

        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();

        var stand = new Stand(window, view, items);
        stand.RunLayout();
        return stand;
    }

    [AvaloniaFact]
    public void IsSimplified_Follows_The_Zoom_The_Threshold_And_The_Binding()
    {
        var stand = Create();
        Assert.False(stand.View.IsSimplified);

        stand.View.ViewportZoom = 0.4;
        Assert.True(stand.View.IsSimplified);

        stand.View.SimplifiedZoom = 0.3;
        Assert.False(stand.View.IsSimplified);

        // Ноль выключает упрощённый вид при любом масштабе.
        stand.View.SimplifiedZoom = 0;
        stand.View.ViewportZoom = 0.1;
        Assert.False(stand.View.IsSimplified);

        stand.View.SimplifiedZoom = 0.5;
        Assert.True(stand.View.IsSimplified);

        // Без привязки положения свёрнутое не нарисовать — развёрнуто всё, и вид обычный.
        stand.View.ItemLocationBinding = null;
        Assert.False(stand.View.IsSimplified);
    }

    [AvaloniaFact]
    public void Below_The_Threshold_Only_Pinned_Items_Keep_Containers()
    {
        // Окна нет вовсе: остаются выбранный, контейнер с фокусом и элемент, который сам SurfaceItem,
        // — те же контейнеры, а не взятые из пула заново.
        var stand = Create();
        var own = new SurfaceItem { Location = new Point(50, 50), Width = 100, Height = 60 };
        stand.Items.Add(own);
        stand.RunLayout();

        stand.View.Selection.Select(1);
        var selected = stand.Container(1)!;
        var focused = stand.Container(2)!;
        focused.Focusable = true;
        focused.Focus();
        Assert.True(stand.Realized > 3);

        stand.Zoom(0.4);

        Assert.Equal(3, stand.Realized);
        Assert.Same(selected, stand.Container(1));
        Assert.Same(focused, stand.Container(2));
        Assert.Same(own, stand.Container(stand.Items.Count - 1));
    }

    [AvaloniaFact]
    public void Crossing_Back_Realizes_What_Is_Visible()
    {
        // Выше порога окно возвращается: развёрнуто ровно то же, что до ухода под порог.
        var stand = Create();
        var before = Enumerable.Range(0, stand.Items.Count).Where(i => stand.Container(i) != null).ToList();

        stand.Zoom(0.4);
        Assert.Equal(0, stand.Realized);

        stand.Zoom(1);
        Assert.Equal(before, Enumerable.Range(0, stand.Items.Count).Where(i => stand.Container(i) != null).ToList());
    }

    [AvaloniaFact]
    public void Panning_Below_The_Threshold_Measures_Nothing()
    {
        // Разворачивать в упрощённом виде нечего: панорама и масштаб под порогом панель не перемеряют.
        var stand = Create();
        stand.View.Selection.Select(0);
        stand.Zoom(0.4);
        var measured = stand.Panel.MeasuredChildren;

        stand.Pan(new Point(300, 200));
        stand.Zoom(0.3);
        stand.Pan(new Point(900, 700));

        Assert.Equal(measured, stand.Panel.MeasuredChildren);
        Assert.Equal(1, stand.Realized);
    }
}
