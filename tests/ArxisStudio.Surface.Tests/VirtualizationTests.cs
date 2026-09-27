using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Контейнеры следуют видимой области (ADR 0007): <see cref="VirtualizingSurfacePanel"/> с привязкой
/// положения.
/// </summary>
/// <remarks>
/// Стенд: окно 800 × 600, поверхность над сеткой моделей — столбцы через 150, ряды через 100,
/// элемент 100 × 60 — с привязкой положения и предполагаемым размером, равным настоящему. Холст стоит
/// в начале координат, масштаб 1. Развёрнутым обязан быть ровно тот элемент, чей прямоугольник
/// пересекает видимую область, расширенную на запас панели, — это правило стенд и сверяет.
/// </remarks>
public class VirtualizationTests
{
    private static readonly Size ItemSize = new(100, 60);

    private sealed class Place(Point location) : INotifyPropertyChanged
    {
        private Point _location = location;
        private Point _other;

        public Point Location
        {
            get => _location;
            set => Set(ref _location, value);
        }

        public Point Other
        {
            get => _other;
            set => Set(ref _other, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set(ref Point field, Point value, [CallerMemberName] string? name = null)
        {
            if (field == value)
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    private sealed record Stand(Window Window, SurfaceView View, ObservableCollection<object> Items)
    {
        public VirtualizingSurfacePanel Panel => View.GetVisualDescendants().OfType<VirtualizingSurfacePanel>().Single();

        public Place Model(int index) => (Place)Items[index];

        public SurfaceItem? Container(int index) => View.ContainerFromIndex(index) as SurfaceItem;

        public Rect Visible => new(View.ViewportLocation, View.Bounds.Size / View.ViewportZoom);

        public void RunLayout()
        {
            var manager = Window.GetLayoutManager();
            manager?.ExecuteInitialLayoutPass();
            manager?.ExecuteLayoutPass();
        }

        public void Pan(Point location)
        {
            View.ViewportLocation = location;
            RunLayout();
        }
    }

    private static Point LocationOf(int index, int columns) => new(index % columns * 150, index / columns * 100);

    private static Stand Create(int columns = 20, int rows = 20)
    {
        var items = new ObservableCollection<object>();
        for (var i = 0; i < columns * rows; i++)
            items.Add(new Place(LocationOf(i, columns)));

        var view = new SurfaceView
        {
            ItemsSource = items,
            ItemLocationBinding = new Binding(nameof(Place.Location)),
            EstimatedItemSize = ItemSize,
            ItemTemplate = new FuncDataTemplate<Place>((_, _) => new Border { Width = ItemSize.Width, Height = ItemSize.Height })
        };

        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();

        var stand = new Stand(window, view, items);
        stand.RunLayout();
        return stand;
    }

    /// <summary>
    /// Развёрнуто ровно то, что пересекает видимую область с запасом, и закреплённое.
    /// </summary>
    private static void AssertRealizedExactlyWhatIsSeen(Stand stand, params int[] pinned)
    {
        var window = stand.Visible.Inflate(stand.Panel.RealizationMargin / stand.View.ViewportZoom);
        for (var i = 0; i < stand.Items.Count; i++)
        {
            var bounds = stand.Items[i] is Place place ? new Rect(place.Location, ItemSize) : default;
            var seen = window.Intersects(bounds) || pinned.Contains(i);
            Assert.True(seen == (stand.Container(i) != null), $"элемент {i} в {bounds}: ожидался {(seen ? "развёрнутым" : "свёрнутым")}");
        }

        AssertConsistent(stand);
    }

    /// <summary>
    /// Каждый развёрнутый контейнер стоит на своём элементе, в панели, по порядку элементов.
    /// </summary>
    private static void AssertConsistent(Stand stand)
    {
        var panel = stand.Panel;
        var realized = 0;
        for (var i = 0; i < stand.Items.Count; i++)
        {
            if (stand.Container(i) is not { } container)
                continue;

            realized++;
            Assert.Same(panel, container.GetVisualParent());
            Assert.Equal(i, stand.View.IndexFromContainer(container));
            if (stand.Items[i] is Place place)
            {
                Assert.Same(place, container.DataContext);
                Assert.Equal(place.Location, container.Location);
                Assert.Equal(new Rect(place.Location, ItemSize), container.Bounds);
            }
        }

        Assert.Equal(realized, panel.RealizedCount);
        Assert.Equal(realized, panel.Children.Count);

        // Дети идут в порядке элементов: от него зависит, кто рисуется поверх.
        var order = panel.Children.Select(stand.View.IndexFromContainer).ToList();
        Assert.Equal(order.OrderBy(index => index), order);
    }

    [AvaloniaFact]
    public void Of_Ten_Thousand_Only_What_Is_Seen_Is_Realized()
    {
        var stand = Create(columns: 100, rows: 100);

        AssertRealizedExactlyWhatIsSeen(stand);
        Assert.InRange(stand.Panel.RealizedCount, 1, 100);
    }

    [AvaloniaFact]
    public void A_Pan_Realizes_What_Comes_Into_View_And_Reuses_What_Left()
    {
        // Прыжок дальше двойного запаса: из прежнего окна не остаётся ничего, и каждый новый
        // контейнер берётся из пула, а не создаётся.
        var stand = Create();
        var containers = new HashSet<Control>(stand.View.GetRealizedContainers());
        stand.View.ContainerPrepared += (_, e) => containers.Add(e.Container);
        var before = stand.Panel.RealizedCount;

        stand.Pan(new Point(1500, 1000));

        AssertRealizedExactlyWhatIsSeen(stand);
        Assert.Null(stand.Container(0));
        Assert.True(containers.Count <= Math.Max(before, stand.Panel.RealizedCount),
            $"подготовлено {containers.Count} контейнеров на окна по {before} и {stand.Panel.RealizedCount}");
    }

    [AvaloniaFact]
    public void Near_The_Edge_A_Container_Waits_For_Twice_The_Margin()
    {
        // Запас 200: левый верхний элемент (0..100) выходит из окна разворачивания, когда холст
        // уехал на 350, но сворачивается только за двойным запасом — на 550.
        var stand = Create();
        Assert.Equal(200, stand.Panel.RealizationMargin);
        Assert.NotNull(stand.Container(0));

        stand.Pan(new Point(350, 0));
        Assert.NotNull(stand.Container(0));

        stand.Pan(new Point(550, 0));
        Assert.Null(stand.Container(0));
        AssertConsistent(stand);
    }

    [AvaloniaFact]
    public void A_Model_Moved_Into_View_Is_Realized_There()
    {
        var stand = Create();
        var far = stand.Items.Count - 1;
        Assert.Null(stand.Container(far));

        stand.Model(far).Location = new Point(320, 220);
        stand.RunLayout();

        Assert.Equal(new Rect(320, 220, 100, 60), stand.Container(far)?.Bounds);
        AssertConsistent(stand);
    }

    [AvaloniaFact]
    public void The_Extent_Covers_Items_That_Were_Never_Shown()
    {
        var stand = Create();
        Assert.Equal(new Rect(0, 0, 2950, 1960), stand.View.ItemsExtent);

        // Свёрнутый элемент с края уходит наружу — охват растёт; уходит внутрь — сжимается.
        var edge = stand.Items.Count - 1;
        stand.Model(edge).Location = new Point(4000, 3000);
        stand.RunLayout();
        Assert.Equal(new Rect(0, 0, 4100, 3060), stand.View.ItemsExtent);

        stand.Model(edge).Location = new Point(1000, 1000);
        stand.RunLayout();
        Assert.Equal(new Rect(0, 0, 2950, 1960), stand.View.ItemsExtent);
    }

    [AvaloniaFact]
    public void The_Minimap_Sees_Items_Without_Containers()
    {
        var stand = Create();

        Assert.True(stand.Panel.RealizedCount < stand.Items.Count);
        Assert.Equal(stand.Items.Count, stand.View.EnumerateItemBounds().Count());
    }

    [AvaloniaFact]
    public void Collection_Changes_Keep_Every_Container_On_Its_Item()
    {
        var stand = Create();

        stand.Items.Insert(0, new Place(new Point(5000, 5000)));
        stand.RunLayout();
        AssertRealizedExactlyWhatIsSeen(stand);

        stand.Items.RemoveAt(2);
        stand.RunLayout();
        AssertRealizedExactlyWhatIsSeen(stand);

        stand.Items.Move(1, stand.Items.Count - 1);
        stand.RunLayout();
        AssertRealizedExactlyWhatIsSeen(stand);

        stand.Items[3] = new Place(new Point(40, 400));
        stand.RunLayout();
        AssertRealizedExactlyWhatIsSeen(stand);

        var kept = stand.Items.Take(50).ToList();
        stand.Items.Clear();
        foreach (var item in kept)
            stand.Items.Add(item);

        stand.RunLayout();
        AssertRealizedExactlyWhatIsSeen(stand);
    }

    [AvaloniaFact]
    public void A_Container_Given_Back_No_Longer_Follows_Its_Item()
    {
        // Холст уезжает туда, где элементов нет, — свёрнутые контейнеры остаются в пуле, никому не
        // отданные, и прежнюю модель уже не слушают.
        var stand = Create();
        var container = stand.Container(0)!;

        stand.Pan(new Point(-5000, -5000));
        Assert.Equal(0, stand.Panel.RealizedCount);
        stand.Model(0).Location = new Point(5, 5);

        Assert.NotEqual(new Point(5, 5), container.Location);
    }

    [AvaloniaFact]
    public void Selected_And_Focused_Items_Stay_Realized()
    {
        // Закреплённый не переготавливается: у него тот же контейнер, а не взятый из пула заново.
        var stand = Create();
        stand.View.Selection.Select(1);
        var selected = stand.Container(1)!;
        var focused = stand.Container(2)!;
        focused.Focusable = true;
        focused.Focus();

        stand.Pan(new Point(1500, 1000));
        AssertRealizedExactlyWhatIsSeen(stand, 1, 2);
        Assert.Same(selected, stand.Container(1));
        Assert.Same(focused, stand.Container(2));

        // Снятый выбор и ушедший фокус отпускают контейнеры на ближайшей мере.
        stand.View.Selection.Clear();
        stand.View.Focus();
        stand.Pan(new Point(1510, 1000));
        AssertRealizedExactlyWhatIsSeen(stand);
    }

    [AvaloniaFact]
    public void A_Collapsed_Item_Selected_By_The_Host_Is_Realized_With_One_Event()
    {
        // Индексный слой отмечает выбор только на развёрнутых; свёрнутый обязан получить контейнер
        // раньше, чем снимок выделения уйдёт наружу, — иначе событий было бы два, и первое без него.
        var stand = Create();
        var far = stand.Items.Count - 1;
        var events = new List<SurfaceSelectionChangedEventArgs>();
        stand.View.SurfaceSelectionChanged += (_, e) => events.Add(e);

        using (stand.View.Selection.BatchUpdate())
        {
            stand.View.Selection.Select(0);
            stand.View.Selection.Select(far);
        }

        var selected = Assert.Single(events).NewTargets.Select(t => t.Container).ToList();
        Assert.Equal(new[] { stand.Container(0), stand.Container(far) }, selected);
        Assert.Equal(new Rect(stand.Model(far).Location, ItemSize), stand.Container(far)!.Bounds);

        // Одно свёрнутое: развёрнутых среди выбранного нет, и индексный слой не отмечает ничего —
        // до слоя target'ов выбор доходит только через саму модель выбора.
        stand.View.Selection.Clear();
        events.Clear();
        stand.View.Selection.Select(far - 1);

        Assert.Same(stand.Container(far - 1), Assert.Single(Assert.Single(events).NewTargets).Container);
    }

    [AvaloniaFact]
    public void A_Marquee_Selects_The_Collapsed_Items_It_Covers()
    {
        var stand = Create();
        var bounds = new Rect(2000, 1200, 700, 500);
        var covered = Enumerable.Range(0, stand.Items.Count)
            .Where(i => bounds.Intersects(new Rect(stand.Model(i).Location, ItemSize)))
            .ToList();
        Assert.All(covered, i => Assert.Null(stand.Container(i)));

        stand.View.CommitSelection(bounds, isCtrlPressed: false, useContainerSelection: true);

        Assert.Equal(covered, stand.View.Selection.SelectedIndexes.OrderBy(i => i));
        Assert.Equal(covered.Count, stand.View.SelectedTargets.Count);
        Assert.All(covered, i => Assert.NotNull(stand.Container(i)));
    }

    [AvaloniaFact]
    public void Select_All_Realizes_Everything_And_Deselecting_Lets_It_Go()
    {
        var stand = Create();
        stand.View.Focus();

        stand.Window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);

        Assert.Equal(stand.Items.Count, stand.View.SelectedTargets.Count);
        Assert.Equal(stand.Items.Count, stand.Panel.RealizedCount);
        AssertConsistent(stand);

        // Снятый выбор отпускает контейнеры на ближайшей мере, а не на ближайшей панораме. Развёрнуты
        // были все, поэтому остаются ровно те, кого держит двойной запас.
        stand.View.Selection.Clear();
        stand.RunLayout();

        var keep = stand.Visible.Inflate(2 * stand.Panel.RealizationMargin);
        Assert.All(Enumerable.Range(0, stand.Items.Count), i =>
            Assert.Equal(keep.Intersects(new Rect(stand.Model(i).Location, ItemSize)), stand.Container(i) != null));
        AssertConsistent(stand);
    }

    [AvaloniaFact]
    public void Select_All_Of_Two_Thousand_Realizes_Them_Without_Recursing()
    {
        // Каждый развёрнутый выбранный контейнер отмечает выбор и зовёт пересборку снимка; без
        // пропуска вложенных вызовов она разворачивала бы следующий, и глубина стека росла бы с числом
        // свёрнутых выбранных — на двух тысячах процесс падал переполнением стека. «Выбрать всё» —
        // честная цена: развёрнуто всё, снимок — один.
        var stand = Create(columns: 50, rows: 40);
        var events = 0;
        stand.View.SurfaceSelectionChanged += (_, _) => events++;
        stand.View.Focus();

        stand.Window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);

        Assert.Equal(stand.Items.Count, stand.Panel.RealizedCount);
        Assert.Equal(stand.Items.Count, stand.View.SelectedTargets.Count);
        Assert.Equal(1, events);
    }

    [AvaloniaFact]
    public void A_Held_Surface_Recycles_Nothing_Until_Released()
    {
        var stand = Create();
        var hold = stand.View.HoldRealization();

        stand.Pan(new Point(1500, 1000));
        Assert.NotNull(stand.Container(0));
        AssertConsistent(stand);

        hold.Dispose();
        stand.RunLayout();
        AssertRealizedExactlyWhatIsSeen(stand);
    }

    [AvaloniaFact]
    public void The_Marquee_Holds_What_It_Took_Until_It_Ends()
    {
        // Рамка снимает контейнеры на входе и читает снимок до отпускания; холст, уехавший посреди
        // жеста, их не сворачивает, а после жеста они уходят на ближайшей мере.
        var stand = Create();
        var from = new Point(120, 80);
        stand.Window.MouseDown(from, MouseButton.Left);
        stand.Window.MouseMove(from + new Vector(10, 10));
        Assert.True(stand.View.IsSelecting);

        stand.Pan(new Point(1500, 1000));
        Assert.NotNull(stand.Container(0));

        stand.Window.MouseUp(from + new Vector(10, 10), MouseButton.Left);
        stand.RunLayout();
        Assert.Null(stand.Container(0));
        AssertConsistent(stand);
    }

    [AvaloniaFact]
    public void Without_A_Binding_Everything_Is_Realized()
    {
        // Положения невидимого элемента взять неоткуда — панель разворачивает всё, как SurfacePanel.
        var items = Enumerable.Range(0, 50).Select(i => (object)("элемент " + i)).ToList();
        var view = new SurfaceView { ItemsSource = items };
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        var stand = new Stand(window, view, new ObservableCollection<object>(items));
        stand.RunLayout();
        for (var i = 0; i < items.Count; i++)
            ((SurfaceItem)view.ContainerFromIndex(i)!).Location = new Point(3000 + (i * 150), 3000);

        stand.Pan(new Point(-2000, -2000));
        stand.Panel.InvalidateMeasure();
        stand.RunLayout();

        Assert.Equal(items.Count, stand.Panel.RealizedCount);
    }

    [AvaloniaFact]
    public void An_Item_That_Is_Its_Own_Container_Is_Never_Recycled()
    {
        var stand = Create();
        var own = new SurfaceItem { Location = new Point(3000, 3000), Width = 100, Height = 60 };
        stand.Items.Add(own);
        stand.RunLayout();

        Assert.Same(own, stand.Container(stand.Items.Count - 1));
        Assert.Same(stand.Panel, own.GetVisualParent());

        stand.Pan(new Point(1500, 1000));
        Assert.Same(own, stand.Container(stand.Items.Count - 1));
    }

    [AvaloniaFact]
    public void Another_Binding_Rereads_Every_Place()
    {
        // Все Other стоят в начале координат: после смены привязки там же встают все элементы, и
        // развёрнуты все — они в окне.
        var stand = Create();

        stand.View.ItemLocationBinding = new Binding(nameof(Place.Other));
        stand.RunLayout();

        Assert.Equal(stand.Items.Count, stand.Panel.RealizedCount);
        Assert.All(Enumerable.Range(0, stand.Items.Count), i => Assert.Equal(default, stand.Container(i)!.Location));
    }

    [AvaloniaFact]
    public void Scrolling_Into_View_Leaves_The_Surface_Where_It_Was()
    {
        // Выбор зовёт ScrollIntoView сам; холст от этого не едет — показать элемент умеет CenterOnItem.
        var stand = Create();
        var far = stand.Items.Count - 1;

        stand.View.ScrollIntoView(far);

        Assert.Equal(default, stand.View.ViewportLocation);
        Assert.Equal(new Rect(stand.Model(far).Location, ItemSize), stand.Container(far)?.Bounds);
    }
}
