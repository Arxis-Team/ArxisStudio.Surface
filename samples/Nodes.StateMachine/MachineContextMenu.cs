using System.Windows.Input;
using ArxisStudio.Surface;
using Avalonia;
using Nodes.StateMachine.Machine;

namespace Nodes.StateMachine;

/// <summary>
/// Контекстное меню холста: новые узлы по правой кнопке и правка узла под курсором.
/// </summary>
/// <remarks>
/// Поставщик действий библиотеки (<see cref="ISurfaceContextActionProvider"/>): редактор спрашивает его
/// при каждом вызове меню и сам показывает то, что тот вернул. По пустому холсту меню ставит узел в
/// точку щелчка — состояние с выбранным экраном, переход с выбранным условием, «любое состояние», вход
/// и перевалку; по узлу — меняет экран или условие, делает состояние начальным и удаляет. Каждая правка
/// — запись истории окна, и Ctrl + Z её отменяет.
/// </remarks>
internal sealed class MachineContextMenu(MainWindow window) : ISurfaceContextActionProvider
{
    public ValueTask<IReadOnlyList<SurfaceContextAction>> GetActionsAsync(
        SurfaceView editor, SurfaceContextRequest request, CancellationToken cancellationToken = default)
    {
        var node = request.Target?.Container is { } container ? editor.ItemFromContainer(container) as MachineNode : null;
        IReadOnlyList<SurfaceContextAction> actions = node switch
        {
            StateNode state => ForState(state),
            TransitionNode transition => ForTransition(transition),
            MachineNode other => [Remove(other)],
            _ => ForSurface(request.WorldPoint)
        };

        return ValueTask.FromResult(actions);
    }

    private IReadOnlyList<SurfaceContextAction> ForSurface(Point at) =>
    [
        Menu("Состояние", AppScreens.All.Select(screen =>
            Item(AppScreens.Title(screen), () => window.AddNode(new StateNode(screen, at)))).ToList()),
        Menu("Переход", Triggers.All.Select(trigger =>
            Item(trigger.Title, () => window.AddNode(new TransitionNode(trigger, at)))).ToList()),
        Item("Любое состояние", () => window.AddNode(new AnyStateNode(at))),
        Item("Вход", () => window.AddNode(new EntryNode(at)), enabled: !window.Document.Nodes.OfType<EntryNode>().Any()),
        Item("Перевалка", () => window.AddNode(new RerouteNode(at))),
        Separator(),
        Item("Показать всё", window.FitAll)
    ];

    private IReadOnlyList<SurfaceContextAction> ForState(StateNode state) =>
    [
        Menu("Экран", AppScreens.All.Select(screen =>
            Item(Mark(AppScreens.Title(screen), screen == state.Screen), () => window.ChangeScreen(state, screen))).ToList()),
        Item("Сделать начальным", () => window.MakeInitial(state), enabled: !ReferenceEquals(window.Document.InitialState(), state)),
        Separator(),
        Remove(state)
    ];

    private IReadOnlyList<SurfaceContextAction> ForTransition(TransitionNode transition) =>
    [
        Menu("Условие", Triggers.All.Select(trigger =>
            Item(Mark(trigger.Title, ReferenceEquals(trigger, transition.Trigger)), () => window.ChangeTrigger(transition, trigger))).ToList()),
        Separator(),
        Remove(transition)
    ];

    private SurfaceContextAction Remove(MachineNode node) => Item("Удалить", () => window.RemoveNodes([node]));

    private static string Mark(string title, bool current) => current ? $"✓ {title}" : title;

    private static SurfaceContextAction Item(string header, Action action, bool enabled = true) =>
        new() { Header = header, Command = new Command(action), IsEnabled = enabled };

    private static SurfaceContextAction Menu(string header, IReadOnlyList<SurfaceContextAction> items) =>
        new() { Header = header, Items = items };

    private static SurfaceContextAction Separator() => new() { IsSeparator = true };

    private sealed class Command(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => action();
    }
}
