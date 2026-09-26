using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface;
using ArxisStudio.Surface.Editing;

namespace ArxisStudio.Tests;

/// <summary>
/// Миникарта — инструмент любой поверхности (ADR 0005).
/// </summary>
/// <remarks>
/// Стенд: окно 800 × 600, голый <see cref="SurfaceView"/> с двумя элементами 100 × 60 в (100, 100) и
/// (300, 100), поверх него в правом нижнем углу — миникарта 200 × 150. Холст отведён в
/// (1000, 1000): элементы лежат вне видимой области, и на карте рамка стоит отдельно от них.
/// </remarks>
public class SurfaceMinimapTests
{
    private static readonly Size ItemSize = new(100, 60);

    private sealed record Stand(Window Window, SurfaceView View, SurfaceMinimap Map)
    {
        public SurfaceItem Item(int index) => (SurfaceItem)View.ContainerFromIndex(index)!;

        public Point CentreOf(int index) => Item(index).Location + new Vector(ItemSize.Width / 2, ItemSize.Height / 2);

        public Rect Visible => new(View.ViewportLocation, View.Bounds.Size / View.ViewportZoom);

        /// <summary>
        /// Точка миникарты в координатах окна — для ввода.
        /// </summary>
        public Point OnWindow(Point onMap) => Map.TranslatePoint(onMap, Window)!.Value;

        public void RunLayout()
        {
            var manager = Window.GetLayoutManager();
            manager?.ExecuteInitialLayoutPass();
            manager?.ExecuteLayoutPass();
        }

        /// <summary>
        /// Рисует кадр: миникарта пересчитывает соответствие на отрисовке.
        /// </summary>
        public void Render()
        {
            RunLayout();
            Window.CaptureRenderedFrame();
        }
    }

    private static SurfaceMinimap NewMap(SurfaceView? view) => new()
    {
        Editor = view,
        Width = 200,
        Height = 150,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(10)
    };

    private static Stand Create()
    {
        var view = new SurfaceView { ItemsSource = new[] { "Первый", "Второй" } };
        var map = NewMap(view);
        var window = new Window { Width = 800, Height = 600, Content = new Grid { Children = { view, map } } };
        window.Show();

        var stand = new Stand(window, view, map);
        stand.RunLayout();
        for (var i = 0; i < 2; i++)
        {
            var item = stand.Item(i);
            item.Width = ItemSize.Width;
            item.Height = ItemSize.Height;
            item.Location = new Point(100 + (i * 200), 100);
        }

        view.ViewportLocation = new Point(1000, 1000);
        stand.Render();
        return stand;
    }

    private static void Click(Stand stand, Point onMap)
    {
        var at = stand.OnWindow(onMap);
        stand.Window.MouseMove(at);
        stand.Window.MouseDown(at, MouseButton.Left);
        stand.Window.MouseUp(at, MouseButton.Left);
    }

    private static void AssertNear(Point expected, Point actual) =>
        Assert.True(Point.Distance(expected, actual) < 1e-6, $"{actual} против {expected}");

    [AvaloniaFact]
    public void A_Click_Centres_The_Surface_On_What_The_Map_Shows_There()
    {
        var stand = Create();
        var target = stand.CentreOf(1);

        Click(stand, stand.Map.WorldToMinimap(target));

        AssertNear(target, stand.Visible.Center);
    }

    [AvaloniaFact]
    public void Dragging_The_Frame_Moves_The_Surface_Without_A_Jump()
    {
        // Рамку взяли не за центр: на нажатии холст стоять обязан, а дальше идёт вслед указателю.
        var stand = Create();
        var before = stand.View.ViewportLocation;
        var grab = stand.Map.ViewportFrame.Center + new Vector(5, 3);
        var worldPerPixel = stand.Map.MinimapToWorld(grab + new Vector(1, 0)).X - stand.Map.MinimapToWorld(grab).X;

        stand.Window.MouseMove(stand.OnWindow(grab));
        stand.Window.MouseDown(stand.OnWindow(grab), MouseButton.Left);
        Assert.Equal(before, stand.View.ViewportLocation);

        // Между движениями — кадр: карта пересчитывается на отрисовке, и пока тянут, её масштаб стоит.
        stand.Window.MouseMove(stand.OnWindow(grab + new Vector(10, 5)));
        stand.Render();
        stand.Window.MouseMove(stand.OnWindow(grab + new Vector(20, 10)));
        stand.Render();
        stand.Window.MouseUp(stand.OnWindow(grab + new Vector(20, 10)), MouseButton.Left);

        AssertNear(before + (new Vector(20, 10) * worldPerPixel), stand.View.ViewportLocation);
    }

    [AvaloniaFact]
    public void Panning_The_Surface_Redraws_The_Map()
    {
        var stand = Create();
        var drawn = stand.Map.RenderCount;

        // Только кадр: смена видимой области прохода раскладки не вызывает, и перерисовать карту
        // её обязана подписка на сам viewport.
        stand.View.ViewportLocation = new Point(900, 950);
        stand.Window.CaptureRenderedFrame();

        Assert.True(stand.Map.RenderCount > drawn, "Смена видимой области обязана перерисовать карту.");
    }

    [AvaloniaFact]
    public void A_Map_Given_Its_Surface_Later_Follows_It()
    {
        var stand = Create();
        var grid = (Grid)stand.Window.Content!;
        var late = NewMap(null);
        grid.Children.Add(late);
        stand.Render();

        late.Editor = stand.View;
        stand.Render();
        var drawn = late.RenderCount;
        stand.View.ViewportLocation = new Point(900, 950);
        stand.Window.CaptureRenderedFrame();

        Assert.True(late.RenderCount > drawn, "Карта, получившая редактор после входа в дерево, обязана за ним следить.");
    }

    [AvaloniaFact]
    public void Moving_An_Item_Redraws_The_Map()
    {
        var stand = Create();
        var drawn = stand.Map.RenderCount;

        stand.Item(0).Location = new Point(150, 400);
        stand.Render();

        Assert.True(stand.Map.RenderCount > drawn, "Сдвиг элемента обязан перерисовать карту.");
    }

    [AvaloniaFact]
    public void An_Idle_Surface_Does_Not_Redraw_The_Map()
    {
        // Одни кадры, без принудительной раскладки: в живом окне без перемен проходов раскладки нет.
        var stand = Create();
        var drawn = stand.Map.RenderCount;

        stand.Window.CaptureRenderedFrame();
        stand.Window.CaptureRenderedFrame();

        Assert.Equal(drawn, stand.Map.RenderCount);
    }

    [AvaloniaFact]
    public void The_Map_Works_On_A_Form_Designer()
    {
        var harness = EditorHarness.Create(nodeCount: 2);
        var editor = harness.Editor;
        harness.Window.Content = null;
        var map = NewMap(editor);
        harness.Window.Content = new Grid { Children = { editor, map } };
        harness.RunLayout();
        editor.ViewportLocation = new Point(2000, 2000);
        harness.RunLayout();
        harness.Window.CaptureRenderedFrame();

        var container = harness.Container(1);
        var target = container.Location + new Vector(container.Bounds.Width / 2, container.Bounds.Height / 2);
        var at = map.TranslatePoint(map.WorldToMinimap(target), harness.Window)!.Value;
        harness.Window.MouseMove(at);
        harness.Window.MouseDown(at, MouseButton.Left);
        harness.Window.MouseUp(at, MouseButton.Left);

        var visible = new Rect(editor.ViewportLocation, editor.Bounds.Size / editor.ViewportZoom);
        AssertNear(target, visible.Center);
    }

    [AvaloniaFact]
    public void A_Removed_Map_Is_Not_Held_By_Its_Surface()
    {
        // Редактор живёт дольше миникарты, и подписка на него держала бы её. Сравнение с картой,
        // которая осталась на месте, — чтобы тест не проходил там, где сборка не собрала ничего.
        var stand = Create();
        var removed = AddAndRemove(stand);
        var kept = new WeakReference(stand.Map);

        stand.Render();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(removed.IsAlive, "Снятая миникарта осталась в памяти.");
        Assert.True(kept.IsAlive);
    }

    private static WeakReference AddAndRemove(Stand stand)
    {
        var grid = (Grid)stand.Window.Content!;
        var map = NewMap(stand.View);
        grid.Children.Add(map);
        stand.Render();
        grid.Children.Remove(map);
        stand.Render();
        return new WeakReference(map);
    }
}
