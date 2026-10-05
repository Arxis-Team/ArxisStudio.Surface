using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Tests;

/// <summary>
/// Положение элемента из привязки: <see cref="SurfaceView.ItemLocationBinding"/> и запись ядра, не
/// снимающая привязок хоста (ADR 0007).
/// </summary>
/// <remarks>
/// Стенд: окно 800 × 600, голый <see cref="SurfaceView"/> над двумя моделями с уведомляющим
/// <c>Location</c> — (100, 100) и (300, 100), — контейнеры 100 × 60. Привязка задана без режима:
/// <see cref="SurfaceItem.Location"/> двусторонний по умолчанию.
/// </remarks>
public class ItemLocationBindingTests
{
    private static readonly Size ItemSize = new(100, 60);

    private sealed class Place(Point location) : INotifyPropertyChanged
    {
        private Point _location = location;

        public Point Location
        {
            get => _location;
            set
            {
                if (_location == value)
                    return;

                _location = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Location)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed record Stand(Window Window, SurfaceView View, ObservableCollection<object> Items)
    {
        public SurfaceItem Item(int index) => (SurfaceItem)View.ContainerFromIndex(index)!;

        public Place Model(int index) => (Place)Items[index];

        public Point CentreOf(int index) => Item(index).Location + new Vector(ItemSize.Width / 2, ItemSize.Height / 2);

        public void RunLayout()
        {
            var manager = Window.GetLayoutManager();
            manager?.ExecuteInitialLayoutPass();
            manager?.ExecuteLayoutPass();
        }
    }

    private static Stand Create(SurfaceView view, params object[] items)
    {
        var source = new ObservableCollection<object>(items);
        view.ItemsSource = source;
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();

        var stand = new Stand(window, view, source);
        stand.RunLayout();
        for (var i = 0; i < source.Count; i++)
        {
            stand.Item(i).Width = ItemSize.Width;
            stand.Item(i).Height = ItemSize.Height;
        }

        stand.RunLayout();
        return stand;
    }

    private static Stand Create() => Create(
        new SurfaceView { ItemLocationBinding = new Binding(nameof(Place.Location)) },
        new Place(new Point(100, 100)),
        new Place(new Point(300, 100)));

    private static void Drag(Stand stand, Point from, Vector by)
    {
        stand.Window.MouseDown(from, MouseButton.Left);
        stand.Window.MouseMove(from + new Vector(10, 5));
        stand.Window.MouseMove(from + by);
        stand.Window.MouseUp(from + by, MouseButton.Left);
        stand.RunLayout();
    }

    [AvaloniaFact]
    public void A_Container_Stands_Where_Its_Item_Says()
    {
        var stand = Create();

        Assert.Equal(new Point(100, 100), stand.Item(0).Location);
        Assert.Equal(new Point(300, 100), stand.Item(1).Bounds.Position);
    }

    [AvaloniaFact]
    public void Editing_The_Model_Moves_The_Container()
    {
        var stand = Create();

        stand.Model(0).Location = new Point(150, 400);
        stand.RunLayout();

        Assert.Equal(new Point(150, 400), stand.Item(0).Bounds.Position);
    }

    [AvaloniaFact]
    public void A_Drag_Goes_Into_The_Model_And_The_Model_Still_Leads()
    {
        // Привязка без режима: пишет в модель, потому что Location двусторонний по умолчанию. После
        // жеста она жива — правка модели по-прежнему двигает контейнер.
        var stand = Create();

        Drag(stand, stand.CentreOf(0), new Vector(40, 30));
        Assert.Equal(new Point(140, 130), stand.Model(0).Location);

        stand.Model(0).Location = new Point(500, 400);
        stand.RunLayout();
        Assert.Equal(new Point(500, 400), stand.Item(0).Bounds.Position);
    }

    [AvaloniaFact]
    public void Undo_Moves_The_Model_Back()
    {
        var stand = Create();
        var edits = new List<SurfaceEditCompletedEventArgs>();
        stand.View.EditCompleted += (_, e) => edits.Add(e);

        Drag(stand, stand.CentreOf(0), new Vector(40, 30));
        Assert.Equal(new Point(140, 130), stand.Model(0).Location);
        foreach (var change in Assert.Single(edits).Changes)
            stand.View.Revert(change);

        Assert.Equal(new Point(100, 100), stand.Model(0).Location);
    }

    [AvaloniaFact]
    public void An_Item_That_Is_Its_Own_Container_Keeps_Its_Place()
    {
        // Готовый контейнер из коллекции своего контекста данных не получает, и привязка, применённая
        // к нему, читала бы контекст поверхности — модель хоста, у которой тоже может быть Location.
        var own = new SurfaceItem { Location = new Point(250, 300) };
        var stand = Create(
            new SurfaceView
            {
                DataContext = new Place(new Point(0, 0)),
                ItemLocationBinding = new Binding(nameof(Place.Location))
            },
            new Place(new Point(100, 100)),
            own);

        Assert.Equal(new Point(250, 300), own.Location);
    }

    [AvaloniaFact]
    public void A_Style_Bound_Item_Follows_Its_Model_After_A_Drag()
    {
        // Так привязывает положение демо дизайнера: сеттером стиля. Запись ядра не
        // снимает эту привязку — локальное значение затенило бы её, и после первого же жеста правка
        // модели контейнер бы не двигала.
        var view = new SurfaceView();
        var style = new Style(x => x.OfType<SurfaceItem>());
        style.Setters.Add(new Setter(SurfaceItem.LocationProperty, new Binding(nameof(Place.Location)) { Mode = BindingMode.TwoWay }));
        view.Styles.Add(style);
        var stand = Create(view, new Place(new Point(100, 100)));

        Drag(stand, stand.CentreOf(0), new Vector(40, 30));
        Assert.Equal(new Point(140, 130), stand.Model(0).Location);

        stand.Model(0).Location = new Point(500, 400);
        stand.RunLayout();
        Assert.Equal(new Point(500, 400), stand.Item(0).Bounds.Position);
    }

    [AvaloniaFact]
    public void Outside_An_Editor_The_Drag_Keeps_A_Style_Binding_Too()
    {
        // Без редактора жест пишет положение сам (ItemStateTests), и правило записи у него то же.
        var place = new Place(new Point(10, 10));
        var item = new UiDesignerItem { DataContext = place, Width = 80, Height = 60, Content = new Border() };
        var style = new Style(x => x.OfType<UiDesignerItem>());
        style.Setters.Add(new Setter(SurfaceItem.LocationProperty, new Binding(nameof(Place.Location)) { Mode = BindingMode.TwoWay }));
        var window = new Window { Width = 400, Height = 300, Content = new AbsolutePanel { Children = { item } } };
        window.Styles.Add(style);
        window.Show();
        window.UpdateLayout();

        var from = new Point(40, 30);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Vector(6, 4));
        window.MouseMove(from + new Vector(50, 40));
        window.MouseUp(from + new Vector(50, 40), MouseButton.Left);
        Assert.Equal(new Point(60, 50), place.Location);

        place.Location = new Point(200, 150);
        Assert.Equal(new Point(200, 150), item.Location);
        window.Close();
    }

    [AvaloniaFact]
    public void The_Ui_Designer_Keeps_A_Style_Binding_Too()
    {
        // У дизайнера интерфейса положение контейнера пишет своя стратегия размещения, и правило у
        // неё то же.
        var place = new Place(new Point(100, 100));
        var editor = new UiDesignerView { ItemsSource = new[] { place } };
        var style = new Style(x => x.OfType<UiDesignerItem>());
        style.Setters.Add(new Setter(SurfaceItem.LocationProperty, new Binding(nameof(Place.Location)) { Mode = BindingMode.TwoWay }));
        style.Setters.Add(new Setter(Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Left));
        style.Setters.Add(new Setter(Layoutable.VerticalAlignmentProperty, VerticalAlignment.Top));
        editor.Styles.Add(style);
        editor.InteractionOptions.IsSnapToGridEnabled = false;
        editor.InteractionOptions.IsSnapToGuidesEnabled = false;
        var window = new Window { Width = 800, Height = 600, Content = editor };
        window.Show();
        window.GetLayoutManager()?.ExecuteInitialLayoutPass();
        var container = (UiDesignerItem)editor.ContainerFromIndex(0)!;

        Assert.True(editor.SetTargetGeometry(container, new Rect(new Point(140, 130), ItemSize)));
        Assert.Equal(new Point(140, 130), place.Location);

        place.Location = new Point(500, 400);
        window.GetLayoutManager()?.ExecuteLayoutPass();
        Assert.Equal(new Point(500, 400), container.Location);
    }

    /// <summary>
    /// Место (0, 0) совпадает с умолчанием <see cref="SurfaceItem.Location"/>, и перемены привязка не
    /// поднимает: контейнер обязан встать в начало координат и так, а не туда, куда его поставит
    /// выравнивание панели.
    /// </summary>
    [AvaloniaFact]
    public void The_Ui_Designer_Puts_A_Container_Bound_To_The_Origin_At_The_Origin()
    {
        var items = new ObservableCollection<Place> { new(new Point(0, 0)), new(new Point(300, 100)) };
        var editor = new UiDesignerView { ItemLocationBinding = new Binding(nameof(Place.Location)), ItemsSource = items };
        var window = new Window { Width = 800, Height = 600, Content = editor };

        window.Show();

        var manager = window.GetLayoutManager();
        manager?.ExecuteInitialLayoutPass();

        for (var index = 0; index < items.Count; index++)
        {
            var container = (Control)editor.ContainerFromIndex(index)!;
            container.Width = ItemSize.Width;
            container.Height = ItemSize.Height;
        }

        manager?.ExecuteLayoutPass();

        Assert.Equal(new Point(0, 0), editor.ContainerFromIndex(0)!.Bounds.Position);
        Assert.Equal(new Point(300, 100), editor.ContainerFromIndex(1)!.Bounds.Position);
    }
}
