using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Xunit;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Набор клавиатурных команд поверхности: встроенные команды заменяются и снимаются,
/// команда приложения встаёт рядом с ними, а отказавшаяся команда уступает нажатие.
/// </summary>
/// <remarks>
/// Поведение самих встроенных команд держат <see cref="KeyboardTests"/> и
/// <see cref="HistoryKeyTests"/>; здесь — правила набора.
/// </remarks>
public class KeyCommandTests
{
    private static readonly Point ContainerLocation = new(100, 100);
    private static readonly Size ContainerSize = new(200, 150);

    private static Point NestedCentre => new(
        ContainerLocation.X + EditorHarness.NestedOffset + (EditorHarness.NestedWidth / 2),
        ContainerLocation.Y + EditorHarness.NestedOffset + (EditorHarness.NestedHeight / 2));

    private static EditorHarness CreateWithSelection()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);

        harness.Window.MouseDown(NestedCentre, MouseButton.Left);
        harness.Window.MouseUp(NestedCentre, MouseButton.Left);
        harness.RunLayout();

        return harness;
    }

    private static double DesignXOf(EditorHarness harness) => Layout.GetDesignX(harness.Nested(0));

    /// <summary>
    /// Нажимает клавишу и отвечает, дошло ли нажатие до окна необработанным.
    /// </summary>
    private static bool PressReachesWindowUnhandled(EditorHarness harness, PhysicalKey key)
    {
        bool? handled = null;
        void Record(object? sender, KeyEventArgs e) => handled = e.Handled;

        harness.Window.AddHandler(InputElement.KeyDownEvent, Record, RoutingStrategies.Bubble, handledEventsToo: true);
        harness.Window.KeyPressQwerty(key, RawInputModifiers.None);
        harness.Window.RemoveHandler(InputElement.KeyDownEvent, Record);
        harness.RunLayout();

        Assert.True(handled.HasValue, "Нажатие не дошло до окна вовсе.");
        return handled == false;
    }

    [AvaloniaFact]
    public void The_Built_In_Commands_Come_In_Their_Order()
    {
        var harness = EditorHarness.Create();

        // История первой: её сочетание настраивается, и уступать его стрелкам или букве
        // нельзя. Остальной порядок прежнего switch.
        Assert.Equal(
            new[]
            {
                SurfaceKeyCommands.Undo,
                SurfaceKeyCommands.Redo,
                SurfaceKeyCommands.Nudge,
                SurfaceKeyCommands.ClearSelection,
                SurfaceKeyCommands.Delete,
                SurfaceKeyCommands.SelectAll
            },
            harness.Editor.KeyCommands.Select(c => c.Id).ToArray());
    }

    [AvaloniaFact]
    public void A_Host_Command_Runs_On_Its_Key()
    {
        var harness = CreateWithSelection();
        var runs = 0;

        harness.Editor.KeyCommands.Add(new SurfaceKeyCommand(
            "host.rename",
            new KeyGesture(Key.F2),
            _ => { runs++; return true; }));

        var unhandled = PressReachesWindowUnhandled(harness, PhysicalKey.F2);

        Assert.Equal(1, runs);
        Assert.False(unhandled, "Выполненная команда обязана пометить нажатие обработанным.");
    }

    [AvaloniaFact]
    public void Removing_A_Built_In_Command_Gives_The_Key_Back()
    {
        var harness = CreateWithSelection();
        var before = DesignXOf(harness);

        Assert.True(harness.Editor.KeyCommands.Remove(SurfaceKeyCommands.Nudge));
        var unhandled = PressReachesWindowUnhandled(harness, PhysicalKey.ArrowRight);

        Assert.Equal(before, DesignXOf(harness), 1);
        Assert.True(unhandled, "Снятая команда не должна съедать клавишу.");
    }

    [AvaloniaFact]
    public void A_Command_With_A_Taken_Id_Replaces_The_Old_One_In_Its_Place()
    {
        var harness = CreateWithSelection();
        var commands = harness.Editor.KeyCommands;
        var position = IndexOf(commands, SurfaceKeyCommands.Delete);
        var replaced = 0;

        commands.Add(new SurfaceKeyCommand(
            SurfaceKeyCommands.Delete,
            new KeyGesture(Key.Delete),
            _ => { replaced++; return true; }));

        PressReachesWindowUnhandled(harness, PhysicalKey.Delete);

        Assert.Equal(1, replaced);
        Assert.Equal(position, IndexOf(commands, SurfaceKeyCommands.Delete));
        Assert.Equal(6, commands.Count);
    }

    [AvaloniaFact]
    public void A_Refusing_Command_Yields_The_Key_To_The_Next()
    {
        var harness = CreateWithSelection();
        var before = DesignXOf(harness);
        var asked = 0;

        // Команда приложения на стрелке, которой сейчас делать нечего, не должна
        // отнимать у встроенного смещения его нажатие.
        harness.Editor.KeyCommands.Insert(0, new SurfaceKeyCommand(
            "host.idle",
            new KeyGesture(Key.Right),
            _ => { asked++; return false; }));

        PressReachesWindowUnhandled(harness, PhysicalKey.ArrowRight);

        Assert.Equal(1, asked);
        Assert.Equal(before + 1, DesignXOf(harness), 1);
    }

    [AvaloniaFact]
    public void An_Inserted_Command_Hears_The_Key_First()
    {
        var harness = CreateWithSelection();
        var before = DesignXOf(harness);

        harness.Editor.KeyCommands.Insert(0, new SurfaceKeyCommand(
            "host.arrow",
            new KeyGesture(Key.Right),
            _ => true));

        PressReachesWindowUnhandled(harness, PhysicalKey.ArrowRight);

        Assert.Equal(before, DesignXOf(harness), 1);
    }

    [AvaloniaFact]
    public void Inserting_A_Taken_Id_Moves_It_Instead_Of_Doubling_It()
    {
        var commands = EditorHarness.Create().Editor.KeyCommands;
        var selectAll = commands.Find(SurfaceKeyCommands.SelectAll)!;

        commands.Insert(0, selectAll);

        Assert.Equal(6, commands.Count);
        Assert.Equal(SurfaceKeyCommands.SelectAll, commands[0].Id);
        Assert.Equal(SurfaceKeyCommands.Undo, commands[1].Id);
    }

    [AvaloniaFact]
    public void A_Command_May_Change_The_Set_While_It_Runs()
    {
        var harness = CreateWithSelection();
        var commands = harness.Editor.KeyCommands;

        var laterRuns = 0;

        // Команда, снимающая себя и добавляющая другую, не должна ломать обход:
        // нажатие проходит по набору, каким он был в момент нажатия. Команда при этом
        // отказывается — иначе обход закончился бы на ней и перемены никто бы не заметил.
        commands.Insert(0, new SurfaceKeyCommand(
            "host.once",
            new KeyGesture(Key.F3),
            view =>
            {
                view.KeyCommands.Remove("host.once");
                view.KeyCommands.Insert(0, new SurfaceKeyCommand(
                    "host.later",
                    new KeyGesture(Key.F3),
                    _ => { laterRuns++; return true; }));
                return false;
            }));

        var exception = Record.Exception(() => PressReachesWindowUnhandled(harness, PhysicalKey.F3));

        Assert.Null(exception);
        Assert.Equal(0, laterRuns);
        Assert.Null(commands.Find("host.once"));

        // Следующее нажатие уже видит новый набор.
        PressReachesWindowUnhandled(harness, PhysicalKey.F3);
        Assert.Equal(1, laterRuns);
    }

    [AvaloniaFact]
    public void An_Empty_Id_Is_Refused()
    {
        Assert.Throws<ArgumentException>(() => new SurfaceKeyCommand(" ", new KeyGesture(Key.F2), _ => true));
    }

    private static int IndexOf(SurfaceKeyCommands commands, string id)
    {
        for (var i = 0; i < commands.Count; i++)
        {
            if (commands[i].Id == id)
                return i;
        }

        return -1;
    }
}
