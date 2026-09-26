using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ArxisStudio.Attached;
using Xunit;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Контракт публичных событий перетаскивания контейнера.
/// </summary>
/// <remarks>
/// Три события закреплены в публичной поверхности, но подписан на них не был
/// ни один тест — при том что контракт неочевиден ровно там, где потребитель
/// ошибётся: <c>DragDelta</c> несёт <b>применённую</b> дельту за кадр, то есть
/// уже после привязки к сетке, направляющих и политик перемещения, а не
/// движение указателя. Совпадают они только когда ничто ничего не поправило.
/// </remarks>
public class DragEventTests
{
    private static readonly Point Origin = new(103, 107);
    private static readonly Size ContainerSize = new(220, 170);

    private sealed record Log(
        List<DragStartedEventArgs> Started,
        List<DragDeltaEventArgs> Delta,
        List<DragCompletedEventArgs> Completed)
    {
        public Vector AppliedTotal => new(
            Delta.Sum(d => d.HorizontalChange),
            Delta.Sum(d => d.VerticalChange));
    }

    private static (EditorHarness Harness, Log Log) Create(bool snapToGrid = false)
    {
        var harness = EditorHarness.Create(snapToGrid: snapToGrid);
        harness.PlaceContainer(0, Origin, ContainerSize);

        var log = new Log(new(), new(), new());
        var container = harness.Container(0);
        container.DragStarted += (_, e) => log.Started.Add(e);
        container.DragDelta += (_, e) => log.Delta.Add(e);
        container.DragCompleted += (_, e) => log.Completed.Add(e);

        return (harness, log);
    }

    /// <summary>
    /// Позиция так, как её видит редактор.
    /// </summary>
    /// <remarks>
    /// Через <c>Layout.GetDesignX/Y</c> её здесь читать нельзя: они пересчитываются
    /// отложенным постом диспетчера и гарантированно отстают на проход, так что
    /// «до» и «после» оказались бы взяты из разных моментов.
    /// </remarks>
    private static Point Position(EditorHarness harness) =>
        harness.Editor.GetDesignPosition(harness.Nested(0));

    private static void Drag(EditorHarness harness, Vector delta)
    {
        var from = harness.CentreOf(harness.Nested(0));
        harness.Window.MouseDown(from, MouseButton.Left);
        harness.Window.MouseMove(from + new Vector(6, 4));
        harness.Window.MouseMove(from + delta);
        harness.Window.MouseUp(from + delta, MouseButton.Left);
        harness.RunLayout();
    }

    [AvaloniaFact]
    public void Started_Reports_The_Press_Point()
    {
        var (harness, log) = Create();
        var from = harness.CentreOf(harness.Nested(0));

        Drag(harness, new Vector(40, 30));

        var started = Assert.Single(log.Started);
        Assert.Equal(from.X, started.HorizontalOffset);
        Assert.Equal(from.Y, started.VerticalOffset);
    }

    [AvaloniaFact]
    public void Deltas_Add_Up_To_The_Movement_That_Happened()
    {
        var (harness, log) = Create();
        var before = Position(harness);

        Drag(harness, new Vector(40, 30));

        var after = Position(harness);
        Assert.Equal(after.X - before.X, log.AppliedTotal.X);
        Assert.Equal(after.Y - before.Y, log.AppliedTotal.Y);
    }

    [AvaloniaFact]
    public void Completed_Reports_The_Same_Total_As_The_Deltas()
    {
        var (harness, log) = Create();
        var before = Position(harness);

        Drag(harness, new Vector(40, 30));

        var completed = Assert.Single(log.Completed);
        var after = Position(harness);
        Assert.Equal(after.X - before.X, completed.HorizontalChange);
        Assert.Equal(after.Y - before.Y, completed.VerticalChange);
        Assert.False(completed.Canceled);
    }

    [AvaloniaFact]
    public void Snapping_Changes_The_Deltas_Not_Just_The_Result()
    {
        // Контейнер стоит мимо узлов сетки, значит привязка обязана съесть остаток.
        var (harness, log) = Create(snapToGrid: true);
        var before = Position(harness);
        var pointerMovement = new Vector(34, 18);

        Drag(harness, pointerMovement);

        var applied = Position(harness) - before;

        // Вот та разница, из-за которой контракт стоит закрепить: указателем
        // провели на 34/18, а элемент проехал на другое число, и события несут
        // именно проехавшее.
        Assert.NotEqual(pointerMovement.X, applied.X);
        Assert.Equal(applied.X, log.AppliedTotal.X);
        Assert.Equal(applied.Y, log.AppliedTotal.Y);
    }

    [AvaloniaFact]
    public void A_Blocked_Axis_Never_Appears_In_The_Deltas()
    {
        var (harness, log) = Create();
        DesignInteraction.SetMovePolicy(harness.Nested(0), MovePolicy.X);

        Drag(harness, new Vector(40, 30));

        // Политика отсекает ось до записи геометрии, и события об этой оси
        // не сообщают ничего: сдвига по ней не было.
        Assert.Equal(0, log.AppliedTotal.Y);
        Assert.NotEqual(0, log.AppliedTotal.X);
    }
}
