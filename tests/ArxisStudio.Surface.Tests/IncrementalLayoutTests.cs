using System.Collections.ObjectModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Раскладка по изменениям в панелях ядра (ADR 0007): сдвиг элемента переставляет его одного,
/// а охват остаётся точным и тогда, когда сжимается.
/// </summary>
/// <remarks>
/// Стенд: три элемента 100 × 60 в (100, 100), (300, 200) и (500, 300); средний края охвата не
/// касается, поэтому его сдвиг наружу растит охват объединением, а не пересчётом. Каждый тест идёт на
/// обеих панелях: <see cref="VirtualizingSurfacePanel"/> поверхности — без привязки положения она
/// разворачивает всё — и <see cref="SurfacePanel"/>, стоящей в окне сама: тема её больше не ставит, а
/// тип остаётся открытым.
/// </remarks>
public class IncrementalLayoutTests
{
    private static readonly Size ItemSize = new(100, 60);

    private static readonly Point[] Locations = [new(100, 100), new(300, 200), new(500, 300)];

    private sealed class Stand
    {
        public required Window Window { get; init; }

        public required Func<int, SurfaceItem> Item { get; init; }

        public required Func<int> Measured { get; init; }

        public required Func<int> Arranged { get; init; }

        public required Func<Rect> Extent { get; init; }

        public required Action Add { get; init; }

        public required Action RemoveLast { get; init; }

        public void RunLayout()
        {
            var manager = Window.GetLayoutManager();
            manager?.ExecuteInitialLayoutPass();
            manager?.ExecuteLayoutPass();
        }

        public void Place(int index, Point location)
        {
            var item = Item(index);
            item.Width = ItemSize.Width;
            item.Height = ItemSize.Height;
            item.Location = location;
        }
    }

    private static Stand Create(bool onSurface)
    {
        var stand = onSurface ? OnSurface() : Alone();
        stand.Window.Show();
        stand.RunLayout();

        for (var i = 0; i < Locations.Length; i++)
            stand.Place(i, Locations[i]);

        stand.RunLayout();
        return stand;
    }

    private static Stand OnSurface()
    {
        var items = new ObservableCollection<string> { "Первый", "Второй", "Третий" };
        var view = new SurfaceView { ItemsSource = items };
        VirtualizingSurfacePanel Panel() => view.GetVisualDescendants().OfType<VirtualizingSurfacePanel>().Single();

        return new Stand
        {
            Window = new Window { Width = 800, Height = 600, Content = view },
            Item = index => (SurfaceItem)view.ContainerFromIndex(index)!,
            Measured = () => Panel().MeasuredChildren,
            Arranged = () => Panel().ArrangedChildren,
            Extent = () => view.ItemsExtent,
            Add = () => items.Add("Ещё один"),
            RemoveLast = () => items.RemoveAt(items.Count - 1)
        };
    }

    private static Stand Alone()
    {
        var panel = new SurfacePanel();
        for (var i = 0; i < Locations.Length; i++)
            panel.Children.Add(new SurfaceItem());

        return new Stand
        {
            Window = new Window { Width = 800, Height = 600, Content = panel },
            Item = index => (SurfaceItem)panel.Children[index],
            Measured = () => panel.MeasuredChildren,
            Arranged = () => panel.ArrangedChildren,
            Extent = () => panel.Extent,
            Add = () => panel.Children.Add(new SurfaceItem()),
            RemoveLast = () => panel.Children.RemoveAt(panel.Children.Count - 1)
        };
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_Moved_Item_Is_Arranged_Alone_And_Nothing_Is_Measured(bool onSurface)
    {
        var stand = Create(onSurface);
        var measured = stand.Measured();
        var arranged = stand.Arranged();

        stand.Item(1).Location = new Point(320, 140);
        stand.RunLayout();

        Assert.Equal(new Point(320, 140), stand.Item(1).Bounds.Position);
        Assert.Equal(0, stand.Measured() - measured);
        Assert.Equal(1, stand.Arranged() - arranged);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_Item_Moved_Out_Grows_The_Extent(bool onSurface)
    {
        var stand = Create(onSurface);
        Assert.Equal(new Rect(100, 100, 500, 260), stand.Extent());

        stand.Item(1).Location = new Point(300, 500);
        stand.RunLayout();

        Assert.Equal(new Rect(100, 100, 500, 460), stand.Extent());
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_Edge_Item_Moved_In_Shrinks_The_Extent(bool onSurface)
    {
        // Край задавал третий элемент; ушёл он внутрь — охват обязан сжаться, а не остаться
        // объединением прежнего и нового.
        var stand = Create(onSurface);

        stand.Item(2).Location = new Point(200, 120);
        stand.RunLayout();

        Assert.Equal(new Rect(100, 100, 300, 160), stand.Extent());
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_Resized_Item_Is_Arranged_With_Its_New_Size(bool onSurface)
    {
        var stand = Create(onSurface);

        stand.Item(0).Width = 180;
        stand.RunLayout();

        Assert.Equal(new Rect(100, 100, 180, 60), stand.Item(0).Bounds);
        Assert.Equal(new Rect(100, 100, 500, 260), stand.Extent());

        stand.Item(2).Height = 200;
        stand.RunLayout();

        Assert.Equal(new Rect(100, 100, 500, 400), stand.Extent());
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Added_And_Removed_Items_Are_Arranged_And_Counted(bool onSurface)
    {
        var stand = Create(onSurface);

        stand.Add();
        stand.RunLayout();
        stand.Place(3, new Point(700, 500));
        stand.RunLayout();

        Assert.Equal(new Point(700, 500), stand.Item(3).Bounds.Position);
        Assert.Equal(new Rect(100, 100, 700, 460), stand.Extent());

        stand.RemoveLast();
        stand.RunLayout();

        Assert.Equal(new Rect(100, 100, 500, 260), stand.Extent());
    }
}
