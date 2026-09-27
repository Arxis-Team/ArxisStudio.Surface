using System.Collections.ObjectModel;
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
/// <para>
/// Часы карты стоят (<see cref="MinimapClock"/>): правка содержимого собирается сразу, только если
/// тест перевёл их на интервал, а иначе ждёт таймера, который тест заменяет
/// <see cref="SurfaceMinimap.FlushContent"/>.
/// </para>
/// </remarks>
public class SurfaceMinimapTests
{
    private static readonly Size ItemSize = new(100, 60);

    private sealed record Stand(
        Window Window, SurfaceView View, SurfaceMinimap Map, MinimapClock Clock, ObservableCollection<string> Items)
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
        var items = new ObservableCollection<string> { "Первый", "Второй" };
        var view = new SurfaceView { ItemsSource = items };
        var map = NewMap(view);
        var clock = MinimapClock.On(map);
        var window = new Window { Width = 800, Height = 600, Content = new Grid { Children = { view, map } } };
        window.Show();

        var stand = new Stand(window, view, map, clock, items);
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
    public void Panning_The_Surface_Redraws_The_Map_Without_Rebuilding_It()
    {
        // Интервал прошёл, и пересборке ничто не мешало бы, — но содержимое не менялось, и карта
        // рисует готовое, сдвинув одну рамку видимой области.
        var stand = Create();
        var drawn = stand.Map.RenderCount;
        var rebuilt = stand.Map.ContentRebuilds;
        stand.Clock.PassInterval();

        // Только кадр: смена видимой области прохода раскладки не вызывает, и перерисовать карту
        // её обязана подписка на сам viewport.
        stand.View.ViewportLocation = new Point(900, 950);
        stand.Window.CaptureRenderedFrame();

        Assert.True(stand.Map.RenderCount > drawn, "Смена видимой области обязана перерисовать карту.");
        Assert.Equal(rebuilt, stand.Map.ContentRebuilds);
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
    public void A_Move_After_A_Quiet_Interval_Is_Drawn_At_Once()
    {
        var stand = Create();
        var drawn = stand.Map.RenderCount;
        stand.Clock.PassInterval();

        stand.Item(0).Location = new Point(150, 400);
        stand.Render();

        Assert.True(stand.Map.RenderCount > drawn, "Сдвиг элемента обязан перерисовать карту.");
        Assert.Equal(new Rect(150, 100, 250, 360), stand.Map.ContentBounds);
    }

    [AvaloniaFact]
    public void A_Hidden_Item_Leaves_The_Map()
    {
        // Спрятанный контейнер своих границ не меняет, и о нём поверхность сообщает отдельно.
        var stand = Create();
        stand.Clock.PassInterval();

        stand.Item(0).IsVisible = false;
        stand.Render();

        Assert.Equal(new Rect(new Point(300, 100), ItemSize), stand.Map.ContentBounds);
    }

    [AvaloniaFact]
    public void A_Removed_Item_Leaves_The_Map()
    {
        // Ушедший контейнер своих границ не меняет: о нём говорит коллекция.
        var stand = Create();
        stand.Clock.PassInterval();

        stand.Items.RemoveAt(0);
        stand.Render();

        Assert.Equal(new Rect(new Point(300, 100), ItemSize), stand.Map.ContentBounds);
    }

    [AvaloniaFact]
    public void Moves_Within_The_Interval_Are_Drawn_Once_When_It_Ends()
    {
        // Часы стоят: все сдвиги — в одном интервале после сборки. Карта не пересобирается и не
        // перерисовывается — без пересборки она показала бы то же самое, — а ждёт таймера; таймер
        // собирает итог один раз.
        var stand = Create();
        var drawn = stand.Map.RenderCount;
        var rebuilt = stand.Map.ContentRebuilds;

        for (var i = 1; i <= 3; i++)
        {
            stand.Item(0).Location = new Point(100 + (i * 50), 400);
            stand.Render();
        }

        Assert.Equal(rebuilt, stand.Map.ContentRebuilds);
        Assert.Equal(drawn, stand.Map.RenderCount);
        Assert.True(stand.Map.IsRebuildScheduled, "Отложенную пересборку обязан назначить таймер.");

        stand.Map.FlushContent();
        stand.Window.CaptureRenderedFrame();

        Assert.Equal(rebuilt + 1, stand.Map.ContentRebuilds);
        Assert.Equal(new Rect(250, 100, 150, 360), stand.Map.ContentBounds);
        Assert.False(stand.Map.IsRebuildScheduled);
    }

    [AvaloniaFact]
    public void A_Layout_Outside_The_Surface_Does_Not_Redraw_The_Map()
    {
        // Проход раскладки у Avalonia общий на окно; карта слушает содержимое поверхности, а не его.
        var stand = Create();
        var label = new TextBlock { Text = "0", HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        ((Grid)stand.Window.Content!).Children.Add(label);
        stand.Render();
        var drawn = stand.Map.RenderCount;

        label.Text = "10";
        stand.Render();

        Assert.Equal(drawn, stand.Map.RenderCount);
    }

    [AvaloniaFact]
    public void Another_Surface_Is_Drawn_At_Once()
    {
        // Часы стоят, и прореживание отложило бы карту до таймера. Но это новое содержимое, а не
        // правка прежнего, и собирается оно сразу.
        var stand = Create();
        var other = new SurfaceView { ItemsSource = new[] { "Третий" } };
        ((Grid)stand.Window.Content!).Children.Insert(0, other);
        stand.RunLayout();
        var item = (SurfaceItem)other.ContainerFromIndex(0)!;
        item.Width = ItemSize.Width;
        item.Height = ItemSize.Height;
        item.Location = new Point(700, 700);
        stand.RunLayout();

        stand.Map.Editor = other;
        stand.Render();

        Assert.Equal(new Rect(new Point(700, 700), ItemSize), stand.Map.ContentBounds);
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
    public void The_Map_Works_On_The_Ui_Designer()
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

    [AvaloniaFact]
    public void A_Map_Removed_While_A_Rebuild_Waits_Is_Not_Held()
    {
        // Назначенный таймер лежит у диспетчера и держал бы карту до своего срабатывания, а в
        // безголовом режиме — навсегда.
        var stand = Create();
        var removed = AddMoveAndRemove(stand);

        stand.Render();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(removed.IsAlive, "Снятая миникарта с отложенной пересборкой осталась в памяти.");
    }

    private static WeakReference AddMoveAndRemove(Stand stand)
    {
        var grid = (Grid)stand.Window.Content!;
        var map = NewMap(stand.View);
        MinimapClock.On(map);
        grid.Children.Add(map);
        stand.Render();

        stand.Item(0).Location = new Point(150, 400);
        stand.Render();
        Assert.True(map.IsRebuildScheduled);

        grid.Children.Remove(map);
        stand.Render();
        return new WeakReference(map);
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
