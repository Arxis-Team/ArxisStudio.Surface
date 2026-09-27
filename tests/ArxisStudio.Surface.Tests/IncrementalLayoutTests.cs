using System.Collections.ObjectModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Раскладка по изменениям в панели ядра (ADR 0007): сдвиг элемента переставляет его одного,
/// а охват остаётся точным и тогда, когда сжимается.
/// </summary>
/// <remarks>
/// Стенд: три элемента 100 × 60 в (100, 100), (300, 200) и (500, 300); средний края охвата не
/// касается, поэтому его сдвиг наружу растит охват объединением, а не пересчётом.
/// </remarks>
public class IncrementalLayoutTests
{
    private static readonly Size ItemSize = new(100, 60);

    private static readonly Point[] Locations = [new(100, 100), new(300, 200), new(500, 300)];

    private sealed record Stand(Window Window, SurfaceView View, ObservableCollection<string> Items)
    {
        public SurfaceItem Item(int index) => (SurfaceItem)View.ContainerFromIndex(index)!;

        public SurfacePanel Panel => View.GetVisualDescendants().OfType<SurfacePanel>().Single();

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

    private static Stand Create()
    {
        var items = new ObservableCollection<string> { "Первый", "Второй", "Третий" };
        var view = new SurfaceView { ItemsSource = items };
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();

        var stand = new Stand(window, view, items);
        stand.RunLayout();

        for (var i = 0; i < Locations.Length; i++)
            stand.Place(i, Locations[i]);

        stand.RunLayout();
        return stand;
    }

    [AvaloniaFact]
    public void A_Moved_Item_Is_Arranged_Alone_And_Nothing_Is_Measured()
    {
        var stand = Create();
        var measured = stand.Panel.MeasuredChildren;
        var arranged = stand.Panel.ArrangedChildren;

        stand.Item(1).Location = new Point(320, 140);
        stand.RunLayout();

        Assert.Equal(new Point(320, 140), stand.Item(1).Bounds.Position);
        Assert.Equal(0, stand.Panel.MeasuredChildren - measured);
        Assert.Equal(1, stand.Panel.ArrangedChildren - arranged);
    }

    [AvaloniaFact]
    public void An_Item_Moved_Out_Grows_The_Extent()
    {
        var stand = Create();
        Assert.Equal(new Rect(100, 100, 500, 260), stand.View.ItemsExtent);

        stand.Item(1).Location = new Point(300, 500);
        stand.RunLayout();

        Assert.Equal(new Rect(100, 100, 500, 460), stand.View.ItemsExtent);
    }

    [AvaloniaFact]
    public void The_Edge_Item_Moved_In_Shrinks_The_Extent()
    {
        // Край задавал третий элемент; ушёл он внутрь — охват обязан сжаться, а не остаться
        // объединением прежнего и нового.
        var stand = Create();

        stand.Item(2).Location = new Point(200, 120);
        stand.RunLayout();

        Assert.Equal(new Rect(100, 100, 300, 160), stand.View.ItemsExtent);
    }

    [AvaloniaFact]
    public void A_Resized_Item_Is_Arranged_With_Its_New_Size()
    {
        var stand = Create();

        stand.Item(0).Width = 180;
        stand.RunLayout();

        Assert.Equal(new Rect(100, 100, 180, 60), stand.Item(0).Bounds);
        Assert.Equal(new Rect(100, 100, 500, 260), stand.View.ItemsExtent);

        stand.Item(2).Height = 200;
        stand.RunLayout();

        Assert.Equal(new Rect(100, 100, 500, 400), stand.View.ItemsExtent);
    }

    [AvaloniaFact]
    public void Added_And_Removed_Items_Are_Arranged_And_Counted()
    {
        var stand = Create();

        stand.Items.Add("Четвёртый");
        stand.RunLayout();
        stand.Place(3, new Point(700, 500));
        stand.RunLayout();

        Assert.Equal(new Point(700, 500), stand.Item(3).Bounds.Position);
        Assert.Equal(new Rect(100, 100, 700, 460), stand.View.ItemsExtent);

        stand.Items.RemoveAt(3);
        stand.RunLayout();

        Assert.Equal(new Rect(100, 100, 500, 260), stand.View.ItemsExtent);
    }
}
