using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Xunit;
using DesignLayout = ArxisStudio.Surface.UiDesigner.Layout;

namespace ArxisStudio.Tests;

/// <summary>
/// Учёт подписки на отслеживание позиции.
/// </summary>
/// <remarks>
/// Инвариант важен не сам по себе: на нём держится дешёвый отсев в глобальном
/// обработчике <c>BoundsProperty</c> и идемпотентность <c>Track</c>, который
/// вызывается из <c>AbsolutePanel.ArrangeOverride</c> для каждого ребёнка
/// на каждом arrange.
/// </remarks>
public class LayoutTrackingTests
{
    [AvaloniaFact]
    public void Control_Is_Not_Tracked_By_Default()
    {
        Assert.False(DesignLayout.IsTracking(new Border()));
    }

    [AvaloniaFact]
    public void Track_Marks_Control_As_Tracked()
    {
        var control = new Border();

        DesignLayout.Track(control);

        Assert.True(DesignLayout.IsTracking(control));
    }

    [AvaloniaFact]
    public void Track_Is_Idempotent()
    {
        var control = new Border();

        DesignLayout.Track(control);
        DesignLayout.Track(control);
        DesignLayout.Track(control);

        Assert.True(DesignLayout.IsTracking(control));

        // Здесь проверяется только флаг: он снимается одним Untrack. Что за флагом
        // стоит настоящая отписка, показывает An_Untracked_Control_Stops_Following_Its_Position —
        // прежняя редакция этого комментария утверждала «одна подписка — одна отписка»,
        // а проверяла лишь то, что флаг снялся, и молчала о снятой строке отписки.
        DesignLayout.Untrack(control);
        Assert.False(DesignLayout.IsTracking(control));
    }

    [AvaloniaFact]
    public void Untrack_On_Untracked_Control_Is_Safe()
    {
        var control = new Border();

        DesignLayout.Untrack(control);

        Assert.False(DesignLayout.IsTracking(control));
    }

    [AvaloniaFact]
    public void Setting_Layout_X_Starts_Tracking()
    {
        var control = new Border();

        DesignLayout.SetX(control, 42);

        Assert.True(DesignLayout.IsTracking(control));
    }

    [AvaloniaFact]
    public void IsTracked_Property_Toggles_Tracking()
    {
        var control = new Border();

        DesignLayout.SetIsTracked(control, true);
        Assert.True(DesignLayout.IsTracking(control));

        DesignLayout.SetIsTracked(control, false);
        Assert.False(DesignLayout.IsTracking(control));
    }

    // ---- Подписка, а не флаг ----------------------------------------------------

    /// <summary>
    /// Стенд: <see cref="Canvas"/> внутри редактора.
    /// </summary>
    /// <remarks>
    /// Редактор нужен, потому что <c>UpdateDesignPosition</c> считает координаты
    /// относительно <c>DesignSurface</c> и без него не делает ничего. А панель —
    /// именно <see cref="Canvas"/>, а не <c>AbsolutePanel</c>: та зовёт <c>Track</c>
    /// для каждого ребёнка на каждом arrange и отменила бы отписку следующим проходом.
    /// </remarks>
    private static (Window Window, Border Child) CreateStand()
    {
        var nodes = new List<TestNode> { new("track0") };
        Border? child = null;

        var editor = new DesignEditor
        {
            ItemsSource = nodes,
            ItemTemplate = new FuncDataTemplate<TestNode>((_, _) =>
            {
                child = new Border { Width = 20, Height = 10 };
                Canvas.SetLeft(child, 10);
                Canvas.SetTop(child, 10);

                var canvas = new Canvas();
                canvas.Children.Add(child);
                return canvas;
            }, supportsRecycling: false)
        };

        var window = new Window { Width = 600, Height = 500, Content = editor };
        window.Show();

        var harness = EditorHarness.Adopt(window, editor, nodes);
        harness.RunLayout();
        harness.PlaceContainer(0, new Point(100, 100), new Size(200, 150));

        Assert.NotNull(child);
        return (window, child!);
    }

    private static void Settle(Window window)
    {
        var manager = window.GetLayoutManager();
        manager?.ExecuteInitialLayoutPass();
        manager?.ExecuteLayoutPass();

        // DesignX/DesignY всегда идут через Dispatcher.Post на Render.
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
    }

    /// <summary>
    /// Отслеживаемый контрол обновляет design-координаты при движении.
    /// </summary>
    /// <remarks>
    /// Контрольная половина: без неё соседний тест проходил бы и в случае, когда
    /// координаты не обновляются вовсе, — а именно это он и должен отличать. Первая
    /// версия стенда стояла вне редактора, и половина честно упала: без
    /// <c>DesignSurface</c> над контролом пересчёт не делает ничего.
    /// </remarks>
    [AvaloniaFact]
    public void A_Tracked_Control_Follows_Its_Position()
    {
        var (window, child) = CreateStand();
        DesignLayout.Track(child);
        Settle(window);
        var before = DesignLayout.GetDesignX(child);

        Canvas.SetLeft(child, 90);
        Settle(window);

        Assert.NotEqual(before, DesignLayout.GetDesignX(child));
    }

    /// <summary>
    /// После <c>Untrack</c> координаты замирают.
    /// </summary>
    /// <remarks>
    /// Проверяется подписка, а не флаг. Прежние тесты смотрели только на
    /// <c>IsTracking</c>, а он снимается в <c>Untrack</c> отдельной строкой: убрав
    /// <c>LayoutUpdated -= OnLayoutUpdated</c>, можно было оставить контрол подписанным
    /// навсегда, не уронив ни одного теста. Комментарий в <c>Track_Is_Idempotent</c>
    /// при этом утверждал, что «одна подписка — одна отписка» проверена.
    /// </remarks>
    [AvaloniaFact]
    public void An_Untracked_Control_Stops_Following_Its_Position()
    {
        var (window, child) = CreateStand();
        DesignLayout.Track(child);
        Settle(window);
        var before = DesignLayout.GetDesignX(child);

        DesignLayout.Untrack(child);
        Canvas.SetLeft(child, 90);
        Settle(window);

        Assert.Equal(before, DesignLayout.GetDesignX(child));
    }
}
