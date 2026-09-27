using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Необязательный стек отмены поверх контракта изменений.
/// </summary>
/// <remarks>
/// Стека у поверхности нет: она сообщает о правках и умеет применить одну из них.
/// <see cref="SurfaceHistory"/> — готовый копящий для хоста, которому своя история не нужна,
/// и он обязан вести себя так же, как история, собранная хостом руками: одна единица
/// редактирования — одна запись, структурные правки хоста в том же стеке, новая правка
/// стирает повтор, сочетания поверхности обслуживаются, а отмена не пишет сама в себя.
/// </remarks>
public class SurfaceHistoryTests
{
    private static EditorHarness Create()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, new Point(100, 100), new Size(200, 150));
        return harness;
    }

    private static Rect Bounds(EditorHarness harness)
    {
        Assert.True(harness.Editor.TryGetDesignBounds(harness.Nested(0), out var bounds));
        return bounds;
    }

    private static void Edit(EditorHarness harness, Rect bounds)
    {
        Assert.True(harness.Editor.SetTargetGeometry(harness.Nested(0), bounds));
        harness.RunLayout();
    }

    [AvaloniaFact]
    public void An_Edit_Is_Undone_And_Redone_As_One_Entry()
    {
        var harness = Create();
        using var history = new SurfaceHistory(harness.Editor);
        var before = Bounds(harness);
        var after = new Rect(before.X + 30, before.Y + 20, before.Width + 10, before.Height);

        Edit(harness, after);
        Assert.True(history.CanUndo);

        Assert.True(history.Undo());
        harness.RunLayout();
        Assert.Equal(before, Bounds(harness));
        Assert.False(history.CanUndo);

        Assert.True(history.Redo());
        harness.RunLayout();
        Assert.Equal(after, Bounds(harness));
    }

    [AvaloniaFact]
    public void Undo_Does_Not_Record_Itself()
    {
        // Отмена применяет правку через Revert, а тот запись подавляет. Иначе каждая
        // отмена порождала бы новую запись и стек никогда не пустел бы.
        var harness = Create();
        using var history = new SurfaceHistory(harness.Editor);
        var before = Bounds(harness);

        Edit(harness, new Rect(before.X + 30, before.Y, before.Width, before.Height));
        history.Undo();

        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
    }

    [AvaloniaFact]
    public void A_Host_Change_Shares_The_Stack_And_A_New_Edit_Drops_Redo()
    {
        var harness = Create();
        using var history = new SurfaceHistory(harness.Editor);
        var host = new RecordedChange();
        var before = Bounds(harness);

        Edit(harness, new Rect(before.X + 30, before.Y, before.Width, before.Height));
        history.Push(host);

        // Отмена идёт в обратном порядке: сначала правка хоста, потом поверхности.
        history.Undo();
        Assert.Equal(1, host.Reverted);
        Assert.True(history.CanUndo);

        Assert.True(history.CanRedo);
        Edit(harness, new Rect(before.X + 60, before.Y, before.Width, before.Height));
        Assert.False(history.CanRedo);
    }

    [AvaloniaFact]
    public void The_Surface_Shortcuts_Drive_The_History()
    {
        var harness = Create();
        using var history = new SurfaceHistory(harness.Editor);
        var before = Bounds(harness);
        var after = new Rect(before.X + 30, before.Y, before.Width, before.Height);
        Edit(harness, after);

        // Клавиатура требует фокуса: без нажатия указателем редактор его не получает.
        harness.Window.MouseDown(new Point(600, 420), MouseButton.Left);
        harness.Window.MouseUp(new Point(600, 420), MouseButton.Left);

        harness.Window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        harness.RunLayout();
        Assert.Equal(before, Bounds(harness));

        harness.Window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.Control);
        harness.RunLayout();
        Assert.Equal(after, Bounds(harness));
    }

    [AvaloniaFact]
    public void A_Disposed_History_Stops_Listening()
    {
        var harness = Create();
        var history = new SurfaceHistory(harness.Editor);
        history.Dispose();
        var before = Bounds(harness);

        Edit(harness, new Rect(before.X + 30, before.Y, before.Width, before.Height));

        Assert.False(history.CanUndo);
    }

    private sealed class RecordedChange : ISurfaceChange
    {
        public int Reverted { get; private set; }

        public int Reapplied { get; private set; }

        public void Revert() => Reverted++;

        public void Reapply() => Reapplied++;
    }
}
