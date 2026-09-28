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
        Settle(stand);
        return stand;
    }

    private static void Drag(Stand stand, Point from, Vector by)
    {
        stand.Window.MouseDown(from, MouseButton.Left);
        stand.Window.MouseMove(from + new Vector(10, 5));
        stand.Window.MouseMove(from + by);
        stand.Window.MouseUp(from + by, MouseButton.Left);
        stand.RunLayout();
    }

    /// <summary>
    /// Даёт панели дойти до остатка запаса: он разворачивается порциями, по кадру на порцию (ADR 0011).
    /// </summary>
    private static void Settle(Stand stand)
    {
        for (var i = 0; i < 200 && stand.Panel.IsRealizationDeferred; i++)
        {
            stand.Window.CaptureRenderedFrame();
            stand.RunLayout();
        }

        Assert.False(stand.Panel.IsRealizationDeferred, "запас не развернулся и за 200 кадров");
    }

    /// <summary>
    /// Развёрнуто ровно то, что пересекает видимую область с запасом, и закреплённое, — когда запас
    /// развернулся весь.
    /// </summary>
    private static void AssertRealizedExactlyWhatIsSeen(Stand stand, params int[] pinned)
    {
        Settle(stand);
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
    public void The_Visible_Comes_At_Once_And_The_Margin_In_Portions()
    {
        // Прыжок в новое место (ADR 0011): всё видимое развёрнуто в той же мере, из запаса — не больше
        // бюджета, остальное — следующими кадрами.
        var stand = Create(columns: 60, rows: 60);
        stand.Pan(new Point(3000, 2000));

        var visible = Enumerable.Range(0, stand.Items.Count)
            .Where(i => new Rect(stand.Model(i).Location, ItemSize).Intersects(stand.Visible)).ToList();
        Assert.All(visible, i => Assert.NotNull(stand.Container(i)));
        Assert.InRange(stand.Panel.RealizedCount - visible.Count, 1, stand.Panel.RealizationBudget);
        Assert.True(stand.Panel.IsRealizationDeferred, "остаток запаса обязан ждать кадра");

        AssertRealizedExactlyWhatIsSeen(stand);
    }

    [AvaloniaFact]
    public void Realizing_And_Recycling_On_A_Pan_Is_No_Content_Change()
    {
        // Содержимое холста — ячейки панели, а не контейнеры: панорама разворачивает и сворачивает, а
        // миникарта и прочие слушатели не пересобираются (ADR 0011). Сдвиг модели — перемена.
        var stand = Create();
        var changes = 0;
        stand.View.ContentChanged += (_, _) => changes++;

        stand.Pan(new Point(1500, 1000));
        Settle(stand);
        stand.Pan(new Point(0, 0));
        Settle(stand);
        Assert.Equal(0, changes);

        stand.Model(0).Location = new Point(40, 30);
        stand.RunLayout();
        Assert.True(changes > 0, "сдвиг элемента обязан сменить содержимое");
    }

    [AvaloniaFact]
    public void A_Selected_Container_Given_Back_Selects_Nothing_It_Is_Reused_For()
    {
        // Выбор с контейнера в пуле снят: оставшийся, он выбрал бы элемент, которому контейнер достанется.
        var stand = Create();
        stand.View.Selection.Select(0);
        stand.View.Selection.Select(1);
        stand.RunLayout();

        stand.Pan(new Point(1500, 1000));
        Settle(stand);

        Assert.Equal(new[] { 0, 1 }, stand.View.Selection.SelectedIndexes.OrderBy(i => i));
        Assert.All(stand.View.GetRealizedContainers().Cast<SurfaceItem>(), container =>
            Assert.Equal(stand.View.Selection.IsSelected(stand.View.IndexFromContainer(container)), container.IsSelected));
    }

    [AvaloniaFact]
    public void Without_A_Budget_The_Margin_Comes_At_Once()
    {
        var stand = Create(columns: 60, rows: 60);
        stand.Panel.RealizationBudget = 0;

        stand.Pan(new Point(3000, 2000));

        Assert.False(stand.Panel.IsRealizationDeferred);
        AssertRealizedExactlyWhatIsSeen(stand);
    }

    [AvaloniaFact]
    public void A_Pan_Within_A_Quarter_Of_The_Margin_Keeps_The_Window()
    {
        // Запас 200 при масштабе 1: четверть — 50. Сдвиг на 40 окна не пересматривает, и видимое всё
        // равно развёрнуто — запас его накрывает; ещё 20 — и окно пересмотрено.
        var stand = Create();
        var passes = stand.Panel.WindowPasses;

        stand.Pan(new Point(40, 0));
        Assert.Equal(passes, stand.Panel.WindowPasses);
        Assert.All(Enumerable.Range(0, stand.Items.Count)
                .Where(i => new Rect(stand.Model(i).Location, ItemSize).Intersects(stand.Visible)),
            i => Assert.NotNull(stand.Container(i)));

        stand.Pan(new Point(60, 0));
        Assert.Equal(passes + 1, stand.Panel.WindowPasses);
        AssertRealizedExactlyWhatIsSeen(stand);
    }

    [AvaloniaFact]
    public void Entering_The_Window_Checks_Cells_Not_The_Collection()
    {
        // Одно и то же окно у начала координат над сеткой 20 × 20 и 100 × 100: вошедших ищут по его
        // ячейкам, и проверок столько же — за все меры, пока запас доразворачивается.
        int ChecksOf(Stand stand)
        {
            var checks = stand.Panel.WindowChecks;
            stand.Pan(new Point(400, 300));
            Settle(stand);
            return stand.Panel.WindowChecks - checks;
        }

        var small = ChecksOf(Create());
        var large = ChecksOf(Create(columns: 100, rows: 100));

        Assert.Equal(small, large);
        Assert.InRange(large, 1, 100 * 100 / 4);
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
    public void The_Primary_And_The_Focused_Stay_Realized_And_Other_Selected_Collapse()
    {
        // Выбор — данные (ADR 0010): контейнер держат главный выбранный и фокус, тот же, а не взятый из
        // пула заново; прочий выбранный за окном сворачивается и остаётся выбранным.
        var stand = Create();
        using (stand.View.Selection.BatchUpdate())
        {
            stand.View.Selection.Select(1);
            stand.View.Selection.Select(3);
        }

        var primary = stand.Container(1)!;
        Assert.Same(primary, stand.View.PrimarySelectionTarget!.Container);
        var focused = stand.Container(2)!;
        focused.Focusable = true;
        focused.Focus();

        stand.Pan(new Point(1500, 1000));
        AssertRealizedExactlyWhatIsSeen(stand, 1, 2);
        Assert.Same(primary, stand.Container(1));
        Assert.Same(focused, stand.Container(2));
        Assert.Equal(new[] { 1, 3 }, stand.View.Selection.SelectedIndexes.OrderBy(i => i));
        Assert.Same(primary, Assert.Single(stand.View.SelectedTargets).Container);

        // Снятый выбор и ушедший фокус отпускают контейнеры на ближайшей мере.
        stand.View.Selection.Clear();
        stand.View.Focus();
        stand.Pan(new Point(1510, 1000));
        AssertRealizedExactlyWhatIsSeen(stand);
    }

    [AvaloniaFact]
    public void Panning_Over_A_Selection_Raises_No_Event_But_Follows_It()
    {
        // Выбранный, въехавший в окно, развёрнут выбранным, а уехавший — свёрнут; выбор тот же, и
        // событие выделения молчит, а SelectedTargets описывает развёрнутое.
        var stand = Create();
        stand.View.Focus();
        stand.Window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        var events = 0;
        stand.View.SurfaceSelectionChanged += (_, _) => events++;

        stand.Pan(new Point(1500, 1000));
        stand.Pan(new Point(2200, 1500));

        Assert.Equal(0, events);
        Assert.Equal(stand.Items.Count, stand.View.Selection.Count);
        var realized = Enumerable.Range(0, stand.Items.Count).Where(i => stand.Container(i) != null).ToList();
        Assert.All(realized, i => Assert.True(stand.Container(i)!.IsSelected, $"элемент {i} развёрнут невыбранным"));
        Assert.Equal(realized.Count, stand.View.SelectedTargets.Count);
        AssertConsistent(stand);
    }

    [AvaloniaFact]
    public void Collapsed_Items_Selected_By_The_Host_Raise_One_Event()
    {
        // Выбор свёрнутых на контейнерах не отмечается, и сравнение по target'ам его пропускало:
        // событие приходит по смене индексного слоя, одно на пакет.
        var stand = Create();
        var far = stand.Items.Count - 1;
        var events = new List<SurfaceSelectionChangedEventArgs>();
        stand.View.SurfaceSelectionChanged += (_, e) => events.Add(e);

        using (stand.View.Selection.BatchUpdate())
        {
            stand.View.Selection.Select(far - 1);
            stand.View.Selection.Select(far);
        }

        Assert.Single(events);
        Assert.Equal(new[] { stand.Items[far - 1], stand.Items[far] }, stand.View.SelectedItems!.Cast<object>());

        // Снятие свёрнутых — тоже смена выбора.
        events.Clear();
        stand.View.Selection.Clear();
        stand.RunLayout();

        Assert.Single(events);
        Assert.Empty(stand.View.SelectedTargets);
    }

    [AvaloniaFact]
    public void A_Marquee_Selects_The_Collapsed_Items_It_Covers_Without_Realizing_Them()
    {
        var stand = Create();
        var bounds = new Rect(2000, 1200, 700, 500);
        var covered = Enumerable.Range(0, stand.Items.Count)
            .Where(i => bounds.Intersects(new Rect(stand.Model(i).Location, ItemSize)))
            .ToList();
        Assert.All(covered, i => Assert.Null(stand.Container(i)));
        var events = 0;
        stand.View.SurfaceSelectionChanged += (_, _) => events++;

        stand.View.CommitSelection(bounds, isCtrlPressed: false, useContainerSelection: true);
        stand.RunLayout();

        Assert.Equal(covered, stand.View.Selection.SelectedIndexes.OrderBy(i => i));
        Assert.All(covered, i => Assert.Null(stand.Container(i)));
        Assert.Empty(stand.View.SelectedTargets);
        Assert.Equal(1, events);

        // Выбор без единого развёрнутого снимается, как и любой. Командой, а не клавишей: фокус
        // поверхности разворачивает выбранный элемент.
        Assert.True(stand.View.TryClearSelection());
        Assert.Equal(0, stand.View.Selection.Count);
        Assert.Equal(2, events);
    }

    [AvaloniaFact]
    public void A_Deselected_Primary_Leaves_The_Targets()
    {
        // Слой target'ов чистится по индексному: снятый с выбора главный не остаётся первым в нём, иначе
        // снимок назвал бы главным невыбранный контейнер.
        var stand = Create();
        stand.View.Focus();
        stand.Window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        var primary = stand.View.PrimarySelectionTarget!.Container;

        stand.View.Selection.Deselect(stand.View.IndexFromContainer(primary));

        Assert.NotSame(primary, stand.View.PrimarySelectionTarget!.Container);
        Assert.DoesNotContain(stand.View.SelectedTargets, t => ReferenceEquals(t.Container, primary));
        Assert.All(stand.View.SelectedTargets, t => Assert.True(t.Container.IsSelected));
    }

    [AvaloniaFact]
    public void Select_All_Selects_Everything_And_Realizes_Only_What_Is_Seen()
    {
        var stand = Create();
        var realized = stand.Panel.RealizedCount;
        stand.View.Focus();

        stand.Window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        stand.RunLayout();

        Assert.Equal(stand.Items.Count, stand.View.Selection.Count);
        Assert.Equal(realized, stand.Panel.RealizedCount);
        Assert.Equal(realized, stand.View.SelectedTargets.Count);
        AssertRealizedExactlyWhatIsSeen(stand);

        // Холст ушёл туда, где элементов нет: развёрнутым остался один главный.
        var primary = stand.View.PrimarySelectionTarget!.Container;
        stand.Pan(new Point(-5000, -5000));
        Assert.Same(primary, Assert.Single(stand.View.SelectedTargets).Container);
        Assert.Equal(stand.Items.Count, stand.View.Selection.Count);
    }

    [AvaloniaFact]
    public void Select_All_Of_Two_Thousand_Costs_One_Event_And_No_Containers()
    {
        // Прежде «выбрать всё» разворачивало весь выбор, и без пропуска вложенных пересборок стек рос с
        // числом свёрнутых; теперь разворачивать нечего, а снимок — один.
        var stand = Create(columns: 50, rows: 40);
        var realized = stand.Panel.RealizedCount;
        var events = 0;
        stand.View.SurfaceSelectionChanged += (_, _) => events++;
        stand.View.Focus();

        stand.Window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        stand.RunLayout();

        Assert.Equal(stand.Items.Count, stand.View.SelectedItems!.Count);
        Assert.Equal(realized, stand.Panel.RealizedCount);
        Assert.Equal(1, events);
    }

    [AvaloniaFact]
    public void Removing_Items_One_By_One_Keeps_The_Index_Of_Items()
    {
        // Удаление каждого элемента сообщает о нём, и держащие на нём своё спрашивают индекс: указатель
        // «элемент → индекс», строившийся заново на каждой правке, делал удаление большого выбора
        // квадратом коллекции. Он правится с места правки, а элемент, стоящий дважды, его сбрасывает.
        var stand = Create();
        var panel = stand.Panel;
        Assert.True(panel.TryGetItemBounds(stand.Items[0], out _));
        var rebuilds = panel.IndexRebuilds;

        for (var i = 0; i < 50; i++)
        {
            stand.Items.RemoveAt(stand.Items.Count - 1);
            Assert.True(panel.TryGetItemBounds(stand.Items[0], out _));
        }

        Assert.Equal(rebuilds, panel.IndexRebuilds);

        var inserted = new Place(new Point(7, 7));
        stand.Items.Insert(10, inserted);
        stand.Items.RemoveAt(3);
        Assert.Equal(rebuilds, panel.IndexRebuilds);
        for (var i = 0; i < stand.Items.Count; i++)
        {
            Assert.True(panel.TryGetItemBounds(stand.Items[i], out var bounds));
            Assert.Equal(stand.Model(i).Location, bounds.Position);
        }

        // Элемент дважды — указатель строится заново и называет первое место: развёрнутое, а не
        // свёрнутую копию в конце.
        stand.Items.Add(stand.Items[0]);
        Assert.NotNull(stand.Container(0));
        Assert.Equal(-1, panel.CollapsedIndexOf(stand.Items[0]));
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
    public void A_Drag_Measures_Nothing_Though_It_Writes_The_Model()
    {
        // Жест пишет положение в модель, и модель об этом сообщает; развёрнутому элементу мера от
        // этого не нужна — его контейнер уже на месте.
        var stand = Create();
        var from = new Point(50, 30);
        stand.Window.MouseDown(from, MouseButton.Left);
        stand.Window.MouseMove(from + new Vector(10, 5));
        stand.RunLayout();

        var measured = stand.Panel.MeasuredChildren;
        stand.Window.MouseMove(from + new Vector(40, 30));
        stand.RunLayout();

        Assert.Equal(new Point(40, 30), stand.Model(0).Location);
        Assert.Equal(measured, stand.Panel.MeasuredChildren);
        stand.Window.MouseUp(from + new Vector(40, 30), MouseButton.Left);
    }

    [AvaloniaFact]
    public void Undo_And_Redo_Move_The_Same_Item_Whatever_Became_Of_Its_Container()
    {
        // Холст сдвинут так, что сворачивается один первый столбец, а входят четыре новых: контейнеры
        // первого столбца уходят к новым элементам все. Отмена обязана найти элемент, а не двигать
        // того, кому достался контейнер.
        var stand = Create();
        var edits = new List<SurfaceEditCompletedEventArgs>();
        stand.View.EditCompleted += (_, e) => edits.Add(e);
        var moved = stand.Container(0)!;
        Drag(stand, new Point(50, 30), new Vector(40, 30));
        Assert.Equal(new Point(40, 30), stand.Model(0).Location);
        var edit = Assert.Single(edits);

        // Щелчок отдал контейнеру выбор и фокус клавиатуры, а такой контейнер не сворачивается.
        stand.View.Selection.Clear();
        stand.View.Focus();
        stand.Pan(new Point(550, 0));
        Assert.Null(stand.Container(0));
        Assert.NotEqual(-1, stand.View.IndexFromContainer(moved));

        foreach (var change in edit.Changes)
            stand.View.Revert(change);

        Assert.Equal(new Point(0, 0), stand.Model(0).Location);
        for (var i = 1; i < stand.Items.Count; i++)
            Assert.Equal(LocationOf(i, 20), stand.Model(i).Location);

        // Повтор так же находит элемент, где бы ни был его контейнер.
        stand.Pan(new Point(-5000, -5000));
        foreach (var change in edit.Changes)
            stand.View.Reapply(change);

        Assert.Equal(new Point(40, 30), stand.Model(0).Location);
    }

    [AvaloniaFact]
    public void An_Order_Change_Reaches_Its_Item_Not_The_Container()
    {
        // Порядок перекрытия пишет только дизайнер интерфейса, а он не виртуализирует; правка здесь
        // собрана руками и помнит элемент так же, как её помнила бы сборка правок жеста.
        var stand = Create();
        var container = stand.Container(0)!;
        var change = new OrderChange(container, 0, 5);
        change.RememberItem(stand.View);

        stand.Pan(new Point(550, 0));
        Assert.NotEqual(-1, stand.View.IndexFromContainer(container));

        stand.View.Reapply(change);

        Assert.Equal(0, container.ZIndex);
        Assert.Equal(5, stand.Container(0)!.ZIndex);
    }

    [AvaloniaFact]
    public void A_Container_Given_Back_Keeps_No_Size_Or_Order_Of_Its_Item()
    {
        // Размер и перекрытие редактор пишет контейнеру сам, локальными значениями; переработанный
        // контейнер отдал бы их следующему элементу.
        var stand = Create();
        var container = stand.Container(0)!;
        Assert.True(stand.View.SetTargetGeometry(container, new Rect(0, 0, 180, 90)));
        stand.View.ApplyOrder(container, 5);

        stand.Pan(new Point(-5000, -5000));

        Assert.Equal(-1, stand.View.IndexFromContainer(container));
        Assert.True(double.IsNaN(container.Width));
        Assert.True(double.IsNaN(container.Height));
        Assert.Equal(0, container.ZIndex);
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
