using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Клавиши отмены и повтора.
/// </summary>
/// <remarks>
/// Стека правок у редактора нет: он сообщает о своих изменениях и умеет применить одно
/// из них, но что отменять и в каком порядке — знает только тот, кто их копил. Поэтому
/// клавиши поднимают запрос, как <c>Delete</c>, а не выполняют отмену сами.
/// <para>
/// Сами сочетания берутся у платформы (<c>PlatformHotkeyConfiguration</c>): на macOS
/// это Cmd, а не Ctrl, и повтор там пишется иначе. Свои свойства под это заводить
/// значило бы разойтись с остальным приложением.
/// </para>
/// </remarks>
public class HistoryKeyTests
{
    private static EditorHarness Create()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, new Point(100, 100), new Size(200, 150));

        // Клавиатура требует фокуса: без нажатия указателем редактор его не получает.
        harness.Window.MouseDown(new Point(600, 420), MouseButton.Left);
        harness.Window.MouseUp(new Point(600, 420), MouseButton.Left);
        harness.RunLayout();
        return harness;
    }

    private static int Count(EditorHarness harness, PhysicalKey key, RawInputModifiers modifiers, bool undo)
    {
        var raised = 0;
        void Handler(object? sender, DesignEditorHistoryRequestedEventArgs e)
        {
            raised++;
            e.Handled = true;
        }

        if (undo)
            harness.Editor.UndoRequested += Handler;
        else
            harness.Editor.RedoRequested += Handler;

        harness.Window.KeyPressQwerty(key, modifiers);
        harness.RunLayout();
        return raised;
    }

    [AvaloniaFact]
    public void Undo_Asks_The_Host()
    {
        var harness = Create();

        Assert.Equal(1, Count(harness, PhysicalKey.Z, RawInputModifiers.Control, undo: true));
    }

    [AvaloniaFact]
    public void Redo_Asks_The_Host()
    {
        var harness = Create();

        Assert.Equal(1, Count(harness, PhysicalKey.Y, RawInputModifiers.Control, undo: false));
    }

    /// <summary>
    /// Второе принятое сочетание повтора тоже работает.
    /// </summary>
    /// <remarks>
    /// Список сочетаний даёт платформа, и в нём их обычно два: Ctrl + Y и
    /// Ctrl + Shift + Z. Редактор перебирает весь список, а не сравнивает с одним.
    /// </remarks>
    [AvaloniaFact]
    public void The_Second_Redo_Combination_Works_Too()
    {
        var harness = Create();

        var raised = Count(
            harness,
            PhysicalKey.Z,
            RawInputModifiers.Control | RawInputModifiers.Shift,
            undo: false);

        Assert.Equal(1, raised);
    }

    /// <summary>
    /// Отмена не путается с повтором.
    /// </summary>
    [AvaloniaFact]
    public void Undo_Does_Not_Fire_On_Redo()
    {
        var harness = Create();

        Assert.Equal(0, Count(harness, PhysicalKey.Y, RawInputModifiers.Control, undo: true));
    }

    /// <summary>
    /// Клавиша без модификатора истории не трогает.
    /// </summary>
    /// <remarks>
    /// Первая версия ветки повтора была написана как <c>case Key.Y:</c> без условия,
    /// и обычная «Y» вызывала бы повтор. Сочетание целиком спрашивается у платформы,
    /// и этот тест сторожит, что клавиши сами по себе ничего не значат.
    /// </remarks>
    [AvaloniaFact]
    public void A_Bare_Key_Does_Nothing()
    {
        var harness = Create();

        Assert.Equal(0, Count(harness, PhysicalKey.Y, RawInputModifiers.None, undo: false));
        Assert.Equal(0, Count(harness, PhysicalKey.Z, RawInputModifiers.None, undo: true));
    }

    /// <summary>
    /// Хост может задать свои сочетания вместо платформенных.
    /// </summary>
    /// <remarks>
    /// Зашивать нестандартное сочетание в библиотеку нельзя: оно досталось бы всем,
    /// включая тех, у кого <c>Ctrl + X</c> это «вырезать». Поэтому список задаётся
    /// снаружи, а <see langword="null"/> означает «взять у платформы».
    /// </remarks>
    [AvaloniaFact]
    public void The_Host_Can_Replace_The_Combinations()
    {
        var harness = Create();
        harness.Editor.InputGestures.RedoGestures = new[] { new KeyGesture(Key.X, KeyModifiers.Control) };

        Assert.Equal(1, Count(harness, PhysicalKey.X, RawInputModifiers.Control, undo: false));
    }

    /// <summary>
    /// Заданный список заменяет платформенный, а не дополняет его.
    /// </summary>
    /// <remarks>
    /// Иначе выключить платформенное сочетание было бы нечем, а «задал своё, а старое
    /// продолжает работать» — худший вид сюрприза в настройке.
    /// </remarks>
    [AvaloniaFact]
    public void A_Custom_List_Replaces_The_Platform_One()
    {
        var harness = Create();
        harness.Editor.InputGestures.RedoGestures = new[] { new KeyGesture(Key.X, KeyModifiers.Control) };

        Assert.Equal(0, Count(harness, PhysicalKey.Y, RawInputModifiers.Control, undo: false));
    }

    /// <summary>
    /// Без подписчика нажатие остаётся необработанным.
    /// </summary>
    /// <remarks>
    /// То же правило, что у <c>Delete</c>: редактор не глотает клавишу, которой некому
    /// распорядиться — иначе внешние сочетания приложения переставали бы работать.
    /// </remarks>
    [AvaloniaFact]
    public void Without_A_Subscriber_The_Key_Is_Not_Swallowed()
    {
        var harness = Create();

        var handled = false;
        harness.Editor.AddHandler(
            InputElement.KeyDownEvent,
            (object? _, KeyEventArgs e) => handled = e.Handled,
            handledEventsToo: true);

        harness.Window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        harness.RunLayout();

        Assert.False(handled);
    }

    /// <summary>
    /// Обход подписчиков останавливается на первом выполнившем.
    /// </summary>
    /// <remarks>
    /// Та же причина, что у удаления и перестановки: второй обработчик отменял бы
    /// уже не то, о чём его спросили.
    /// </remarks>
    [AvaloniaFact]
    public void Only_The_First_Handler_Performs_It()
    {
        var harness = Create();
        var seen = 0;

        harness.Editor.UndoRequested += (_, e) => { seen++; e.Handled = true; };
        harness.Editor.UndoRequested += (_, _) => seen++;

        harness.Window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        harness.RunLayout();

        Assert.Equal(1, seen);
    }

    /// <summary>
    /// Невыполненный запрос не помечает нажатие обработанным.
    /// </summary>
    [AvaloniaFact]
    public void An_Unhandled_Request_Leaves_The_Key_Alone()
    {
        var harness = Create();
        harness.Editor.UndoRequested += (_, _) => { };

        var handled = false;
        harness.Editor.AddHandler(
            InputElement.KeyDownEvent,
            (object? _, KeyEventArgs e) => handled = e.Handled,
            handledEventsToo: true);

        harness.Window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        harness.RunLayout();

        Assert.False(handled);
    }
}
