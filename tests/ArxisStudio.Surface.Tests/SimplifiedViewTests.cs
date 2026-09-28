using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;
using SkiaSharp;
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
        private object? _accent;

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
        public VirtualizingSurfacePanel Panel => View.GetVisualDescendants().OfType<VirtualizingSurfacePanel>().Single();

        public SurfaceSimplifiedLayer Layer => View.GetVisualDescendants().OfType<SurfaceSimplifiedLayer>().Single();

        public SurfaceItem? Container(int index) => View.ContainerFromIndex(index) as SurfaceItem;

        public void Render() => Window.CaptureRenderedFrame();

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

    /// <summary>
    /// Кисть, которой красятся полосы каждого третьего элемента начиная со второго; каждый третий
    /// начиная с первого красится цветом, у остальных полосы нет.
    /// </summary>
    private static readonly IBrush Blue = new ImmutableSolidColorBrush(Colors.Blue);

    private static object? AccentOf(int index) => (index % 3) switch
    {
        0 => Colors.Red,
        1 => Blue,
        _ => null
    };

    private static Stand Create(int columns = 20, int rows = 20, bool accentBinding = false)
    {
        var items = new ObservableCollection<object>();
        for (var i = 0; i < columns * rows; i++)
            items.Add(new Card(new Point(i % columns * 150, i / columns * 100)) { Accent = AccentOf(i) });

        var view = new SurfaceView
        {
            ItemsSource = items,
            ItemLocationBinding = new Binding(nameof(Card.Location)),
            ItemAccentBinding = accentBinding ? new Binding(nameof(Card.Accent)) : null,
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

    [AvaloniaFact]
    public void The_Layer_Draws_A_Card_For_Every_Collapsed_Item()
    {
        // Развёрнутый рисует себя сам: карточек ровно столько, сколько свёрнутых, а охватывают они
        // всю сетку — выбранный лежит внутри неё.
        var stand = Create();
        stand.View.Selection.Select(1);
        stand.Zoom(0.4);
        stand.Render();

        Assert.Equal(stand.Items.Count - 1, stand.Layer.Cards);
        Assert.Equal(new Rect(0, 0, (19 * 150) + 100, (19 * 100) + 60), stand.Layer.CardsBounds);
        Assert.Equal(1, stand.Layer.Draws);
    }

    [AvaloniaFact]
    public void Only_A_Collapsed_Change_Rebuilds_The_Cards()
    {
        // Сдвиг развёрнутого карточек не касается; сдвиг модели свёрнутого — пересобирает их, и
        // карточка встаёт на новое место.
        var stand = Create();
        stand.View.Selection.Select(1);
        stand.Zoom(0.4);
        stand.Render();
        var rebuilds = stand.Layer.Rebuilds;

        stand.Container(1)!.SetCurrentValue(SurfaceItem.LocationProperty, new Point(4000, 4000));
        stand.Pan(new Point(100, 100));
        stand.Render();
        Assert.Equal(rebuilds, stand.Layer.Rebuilds);

        ((Card)stand.Items[^1]).Location = new Point(5000, 5000);
        stand.RunLayout();
        stand.Render();
        Assert.Equal(rebuilds + 1, stand.Layer.Rebuilds);
        Assert.Equal(new Point(5100, 5060), stand.Layer.CardsBounds.BottomRight);
    }

    [AvaloniaFact]
    public void Selected_Cards_Show_The_Selection()
    {
        // Выбранный без контейнера остаётся выбранным (ADR 0010), и карточка несёт рамку выбора; смена
        // выбора пересобирает карточки.
        var stand = Create();
        stand.Zoom(0.4);
        stand.View.Selection.SelectAll();
        stand.RunLayout();
        stand.Render();

        Assert.True(stand.Layer.Cards > 0);
        Assert.Equal(stand.Layer.Cards, stand.Layer.SelectedCards);

        // Снятие выбора со свёрнутого не трогает ни одного контейнера: пересобирает карточки сама смена
        // выбора.
        var collapsed = Enumerable.Range(0, stand.Items.Count).First(i => stand.Container(i) == null);
        stand.View.Selection.Deselect(collapsed);
        stand.RunLayout();
        stand.Render();
        Assert.Equal(stand.Layer.Cards - 1, stand.Layer.SelectedCards);
    }

    [AvaloniaFact]
    public void A_Card_Goes_When_Its_Item_Is_Realized_And_Comes_With_The_Collection()
    {
        // Нажатая карточка разворачивается и уходит — иначе она осталась бы под контейнером и выдала
        // бы его, когда тот сдвинется. Коллекция приносит и уносит карточки.
        var stand = Create();
        stand.Zoom(0.4);
        stand.Render();
        Assert.Equal(stand.Items.Count, stand.Layer.Cards);

        Click(stand, Card21);
        stand.Render();
        Assert.Equal(stand.Items.Count - 1, stand.Layer.Cards);

        stand.Items.Add(new Card(new Point(6000, 6000)));
        stand.RunLayout();
        stand.Render();
        Assert.Equal(stand.Items.Count - 1, stand.Layer.Cards);
        Assert.Equal(new Point(6100, 6060), stand.Layer.CardsBounds.BottomRight);

        stand.Items.RemoveAt(stand.Items.Count - 1);
        stand.RunLayout();
        stand.Render();
        Assert.Equal(stand.Items.Count - 1, stand.Layer.Cards);
    }

    [AvaloniaFact]
    public void An_Accent_Band_Comes_From_A_Brush_Or_A_Color()
    {
        // Кисть — как есть, цвет — кистью, одной на цвет; у элемента без значения полосы нет. Полосы
        // идут по геометрии на кисть, и высота полосы — свойство слоя.
        var stand = Create(accentBinding: true);
        stand.Zoom(0.4);
        stand.Render();

        var withAccent = Enumerable.Range(0, stand.Items.Count).Count(i => AccentOf(i) != null);
        Assert.Equal(withAccent, stand.Layer.Bands);
        Assert.Equal(2, stand.Layer.AccentGroups);

        var rebuilds = stand.Layer.Rebuilds;
        stand.Layer.AccentHeight = 8;
        stand.Render();
        Assert.Equal(rebuilds + 1, stand.Layer.Rebuilds);
    }

    [AvaloniaFact]
    public void A_Collapsed_Model_Changing_Its_Accent_Repaints_Its_Band()
    {
        var stand = Create(accentBinding: true);
        stand.Zoom(0.4);
        stand.Render();
        var bands = stand.Layer.Bands;

        ((Card)stand.Items[2]).Accent = Colors.Green;
        stand.RunLayout();
        stand.Render();

        Assert.Equal(bands + 1, stand.Layer.Bands);
        Assert.Equal(3, stand.Layer.AccentGroups);
    }

    [AvaloniaFact]
    public void An_Accent_Changed_While_Realized_Comes_Back_With_The_Card()
    {
        // Модель развёрнутого панель не слушает — положение ему переносит привязка; полосу она
        // перечитывает, когда элемент сворачивается.
        var stand = Create(accentBinding: true);
        stand.Zoom(0.4);
        Click(stand, Card2);
        Assert.NotNull(stand.Container(2));

        ((Card)stand.Items[2]).Accent = Colors.Green;
        stand.RunLayout();
        Click(stand, Gap);
        stand.Render();

        Assert.Null(stand.Container(2));
        Assert.Equal(3, stand.Layer.AccentGroups);
    }

    [AvaloniaFact]
    public void Without_The_Binding_There_Are_No_Bands_Until_It_Is_Set()
    {
        var stand = Create();
        stand.Zoom(0.4);
        stand.Render();
        Assert.Equal(0, stand.Layer.Bands);

        stand.View.ItemAccentBinding = new Binding(nameof(Card.Accent));
        stand.RunLayout();
        stand.Render();

        Assert.Equal(Enumerable.Range(0, stand.Items.Count).Count(i => AccentOf(i) != null), stand.Layer.Bands);
    }

    /// <summary>
    /// Центр карточки элемента 21 — (150, 100) в мире — на экране при масштабе 0,4 и холсте в начале
    /// координат.
    /// </summary>
    private static readonly Point Card21 = new(80, 52);

    /// <summary>Карточка элемента 2 при масштабе 0,4 — (300, 0) в мире.</summary>
    private static readonly Point Card2 = new(140, 12);

    /// <summary>Пустое место между карточками при масштабе 0,4.</summary>
    private static readonly Point Gap = new(50, 12);

    private static void Click(Stand stand, Point at, MouseButton button = MouseButton.Left)
    {
        stand.Window.MouseDown(at, button);
        stand.Window.MouseUp(at, button);
        stand.RunLayout();
    }

    [AvaloniaFact]
    public void A_Press_Realizes_The_Top_Card_Under_It_And_Selects_It()
    {
        // Две карточки в одном месте: разворачивается верхняя — поставленная позже.
        var stand = Create();
        stand.Items.Add(new Card(new Point(150, 100)));
        var top = stand.Items.Count - 1;
        stand.Zoom(0.4);
        Assert.Equal(0, stand.Realized);

        Click(stand, Card21);

        Assert.NotNull(stand.Container(top));
        Assert.Null(stand.Container(21));
        Assert.True(stand.View.Selection.IsSelected(top));
        Assert.Same(stand.Container(top), Assert.Single(stand.View.SelectedTargets).Container);
    }

    [AvaloniaFact]
    public void A_Drag_From_A_Card_Moves_Its_Model()
    {
        // Сдвиг на экране делится на масштаб: 40 × 20 пикселей при 0,4 — это 100 × 50 в мире.
        var stand = Create();
        stand.Zoom(0.4);

        stand.Window.MouseDown(Card21, MouseButton.Left);
        stand.Window.MouseMove(Card21 + new Vector(10, 5));
        stand.Window.MouseMove(Card21 + new Vector(40, 20));
        stand.Window.MouseUp(Card21 + new Vector(40, 20), MouseButton.Left);
        stand.RunLayout();

        Assert.Equal(new Point(250, 150), ((Card)stand.Items[21]).Location);
    }

    [AvaloniaFact]
    public void The_Second_Click_Of_A_Double_Click_Reaches_The_Realized_Container()
    {
        var stand = Create();
        stand.Zoom(0.4);
        Click(stand, Card21);
        var container = stand.Container(21)!;
        var doubleTaps = 0;
        container.DoubleTapped += (_, _) => doubleTaps++;

        Click(stand, Card21);

        Assert.Same(container, stand.Container(21));
        Assert.Equal(1, doubleTaps);
    }

    [AvaloniaFact]
    public void A_Right_Press_Realizes_The_Item_For_The_Context_And_Keeps_It()
    {
        // Контекст находит контейнер по точке; невыбранный, он держится нажатием до следующего
        // нажатия мимо него.
        var stand = Create();
        stand.Zoom(0.4);
        SurfaceContextRequest? request = null;
        stand.View.ContextMenuRequesting += (_, e) => request = e.Request;

        Click(stand, Card21, MouseButton.Right);

        var container = stand.Container(21);
        Assert.NotNull(container);
        Assert.Same(container, request?.Target?.Container);
        Assert.False(stand.View.Selection.IsSelected(21));

        // Мимо карточек: между столбцами 100…150 мира — 40…60 экрана.
        Click(stand, new Point(50, 52), MouseButton.Right);
        Assert.Null(stand.Container(21));
    }

    [AvaloniaFact]
    public void A_Pan_Press_Realizes_Nothing()
    {
        var stand = Create();
        stand.View.InputGestures.PanButton = SurfacePointerButton.Left;
        stand.Zoom(0.4);

        Click(stand, Card21);

        Assert.Equal(0, stand.Realized);
    }

    [AvaloniaFact]
    public void A_Press_On_A_Realized_Container_Is_Left_To_It()
    {
        // Под развёрнутым выбранным лежит свёрнутая карточка: нажатие берёт контейнер, а не её.
        var stand = Create();
        ((Card)stand.Items[0]).Location = new Point(150, 100);
        stand.RunLayout();
        stand.View.Selection.Select(21);
        stand.Zoom(0.4);
        Assert.Null(stand.Container(0));

        Click(stand, Card21);

        Assert.Null(stand.Container(0));
        Assert.True(stand.View.Selection.IsSelected(21));
        Assert.False(stand.View.Selection.IsSelected(0));
    }

    [AvaloniaFact]
    public void Leaving_The_Simplified_View_Stops_Drawing()
    {
        // Над порогом слой отрисовывается заново — пустым: нарисованные карточки иначе остались бы
        // под развёрнутыми узлами.
        var stand = Create();
        stand.Zoom(0.4);
        stand.Render();
        var (renders, draws) = (stand.Layer.Renders, stand.Layer.Draws);

        stand.Zoom(1);
        stand.Render();

        Assert.True(stand.Layer.Renders > renders, "слой не отрисован заново");
        Assert.Equal(draws, stand.Layer.Draws);
    }

    [AvaloniaFact]
    public void The_Layer_Spans_The_Surface_And_Follows_The_Pan()
    {
        // Слой во весь размер поверхности и переводит мир в экран сам. Слой без размера в холсте под
        // трансформацией рендерер отсекал, когда его начало ложилось на край окна: живая проверка так
        // потеряла все карточки на 20 % при холсте в начале координат. Панорама поэтому перерисовывает
        // слой, не пересобирая карточек.
        var stand = Create();
        stand.Zoom(0.4);
        stand.Render();
        Assert.Equal(new Rect(stand.View.Bounds.Size), stand.Layer.Bounds);

        var (draws, rebuilds) = (stand.Layer.Draws, stand.Layer.Rebuilds);
        stand.Pan(new Point(200, 100));
        stand.Render();

        Assert.Equal(draws + 1, stand.Layer.Draws);
        Assert.Equal(rebuilds, stand.Layer.Rebuilds);
    }

    /// <summary>
    /// Сколько карточек пересекает видимую часть мира — прямым проходом, мимо сетки слоя.
    /// </summary>
    private static int VisibleByHand(Stand stand)
    {
        var world = new Rect(stand.View.ViewportLocation, stand.View.Bounds.Size / stand.View.ViewportZoom);
        return stand.Items.Cast<Card>().Count(card => new Rect(card.Location, ItemSize).Intersects(world));
    }

    [AvaloniaFact]
    public void A_Frame_Draws_Only_The_Visible_Cards()
    {
        // Карточек в снимке — все свёрнутые, а кадр берёт из сетки только видимые (ADR 0011): на
        // 0,4 в окне часть сетки 20 × 20, за краем — ничего, на «всё» — все.
        var stand = Create();
        stand.Zoom(0.4);
        stand.Render();
        Assert.Equal(stand.Items.Count, stand.Layer.Cards);
        Assert.InRange(VisibleByHand(stand), 1, stand.Items.Count - 100);
        Assert.Equal(VisibleByHand(stand), stand.Layer.CountVisible().Visible);

        stand.Pan(new Point(1234, 567));
        stand.Render();
        Assert.Equal(VisibleByHand(stand), stand.Layer.CountVisible().Visible);

        stand.Pan(new Point(-5000, -5000));
        stand.Render();
        Assert.Equal(0, stand.Layer.CountVisible().Visible);

        stand.Pan(new Point(0, 0));
        stand.Zoom(0.1);
        stand.Render();
        Assert.Equal(stand.Items.Count, stand.Layer.CountVisible().Visible);
    }

    [AvaloniaFact]
    public void A_Card_Smaller_Than_Four_Pixels_Has_No_Outline_And_Its_Band_Stays_A_Pixel()
    {
        // Карточка 100 × 60: на 0,1 меньшая сторона — 6 пикселей, на 0,05 — 3. Полоса 16 мира на
        // 0,05 — меньше пикселя, и рисуется в пиксель, то есть в 20 мира.
        var stand = Create();
        stand.Zoom(0.1);
        stand.Render();
        Assert.Equal((stand.Items.Count, stand.Items.Count), stand.Layer.CountVisible());

        stand.Zoom(0.05);
        stand.Render();
        Assert.Equal((stand.Items.Count, 0), stand.Layer.CountVisible());

        var card = new Rect(ItemSize);
        Assert.Equal(16, SurfaceSimplifiedLayer.BandHeight(card, 16, 0.4), 6);
        Assert.Equal(20, SurfaceSimplifiedLayer.BandHeight(card, 16, 0.05), 6);
        Assert.Equal(20, SurfaceSimplifiedLayer.BandHeight(card, 40, 0.4), 6);
        Assert.Equal(60, SurfaceSimplifiedLayer.BandHeight(card, 16, 0.01), 6);
    }

    /// <summary>
    /// Рисует слой путём Skia на растровый холст размером со слой.
    /// </summary>
    private static SKBitmap RenderSkia(Stand stand)
    {
        var size = stand.Layer.Bounds.Size;
        var bitmap = new SKBitmap((int)size.Width, (int)size.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        Assert.True(stand.Layer.RenderTo(canvas), "путь Skia отказался рисовать сплошными кистями");
        canvas.Flush();
        return bitmap;
    }

    [AvaloniaFact]
    public void The_Skia_Path_Draws_The_Card_Its_Band_And_Leaves_The_Gaps()
    {
        // 0,4 у начала координат: элемент 0 — 40 × 24 пикселя в (0, 0), полоса 16 мира — 6,4 пикселя
        // сверху, красная; между столбцами — пусто (ADR 0012).
        var stand = Create(accentBinding: true);
        stand.Zoom(0.4);
        stand.Render();

        using var bitmap = RenderSkia(stand);
        Assert.Equal(SkiaCanvas.Color(stand.Layer.Fill!.ToImmutable(), 1), bitmap.GetPixel(20, 16));
        Assert.Equal(new SKColor(255, 0, 0), bitmap.GetPixel(20, 3));
        Assert.Equal(0, bitmap.GetPixel(50, 12).Alpha);
    }

    [AvaloniaFact]
    public void The_Skia_Path_Outlines_By_Zoom_And_Frames_The_Selected_On_Top()
    {
        // 0,4: карточка 0 — (0, 0)…(40, 24), рамка в пиксель по краю — без сглаживания она ложится на
        // одну сторону края, здесь столбец 40. Рамка выбора свёрнутой выбранной второго ряда — в два
        // пикселя по обе стороны края, её цвета поверх обычной. 0,05: карточка 5 × 3 пикселя — мельче
        // четырёх, рамки нет, край — заливка.
        var stand = Create();
        stand.Zoom(0.4);
        stand.Render();
        using (var bitmap = RenderSkia(stand))
        {
            Assert.Equal(SkiaCanvas.Color(stand.Layer.Stroke!.ToImmutable(), 1), bitmap.GetPixel(40, 12));
            Assert.Equal(SkiaCanvas.Color(stand.Layer.Fill!.ToImmutable(), 1), bitmap.GetPixel(37, 16));
        }

        stand.View.Selection.SelectAll();
        stand.RunLayout();
        stand.Render();
        var collapsed = Enumerable.Range(20, 20).First(i => stand.Container(i) == null);
        var right = (int)Math.Round((((collapsed % 20) * 150) + 100) * 0.4);
        using (var bitmap = RenderSkia(stand))
            Assert.Equal(SkiaCanvas.Color(stand.Layer.SelectedStroke!.ToImmutable(), 1), bitmap.GetPixel(right - 1, 52));

        stand.View.Selection.Clear();
        stand.Zoom(0.05);
        stand.Render();
        using (var bitmap = RenderSkia(stand))
            Assert.Equal(SkiaCanvas.Color(stand.Layer.Fill!.ToImmutable(), 1), bitmap.GetPixel(4, 2));
    }

    [AvaloniaFact]
    public void The_Skia_Path_Draws_Only_What_Is_Seen()
    {
        // Холст сдвинут: карточка у начала координат ушла за левый край, и на её месте на экране —
        // другая, а не она; край окна обрезан.
        var stand = Create();
        stand.Zoom(0.4);
        stand.Pan(new Point(150, 100));
        stand.Render();

        using var bitmap = RenderSkia(stand);
        var fill = SkiaCanvas.Color(stand.Layer.Fill!.ToImmutable(), 1);
        Assert.Equal(fill, bitmap.GetPixel(20, 16));
        Assert.Equal(0, bitmap.GetPixel(50, 12).Alpha);
    }

    [AvaloniaFact]
    public void A_Brush_That_Is_Not_Solid_Keeps_The_Old_Path()
    {
        // Холст из аренды рисует сплошным цветом; градиент рисует прежний путь (ADR 0012).
        var stand = Create();
        stand.Zoom(0.4);
        stand.Render();
        stand.Layer.Fill = new LinearGradientBrush { GradientStops = { new GradientStop(Colors.Red, 0), new GradientStop(Colors.Blue, 1) } };

        using var bitmap = new SKBitmap(10, 10);
        using var canvas = new SKCanvas(bitmap);
        Assert.False(stand.Layer.RenderTo(canvas));
    }

    [AvaloniaFact]
    public void The_Card_Stroke_Stays_One_Screen_Pixel()
    {
        var stand = Create();
        stand.Zoom(0.4);
        stand.Render();
        Assert.Equal(2.5, stand.Layer.StrokeThickness, 6);

        stand.Zoom(0.25);
        stand.Render();
        Assert.Equal(4, stand.Layer.StrokeThickness, 6);
    }
}
