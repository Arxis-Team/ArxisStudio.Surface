using System.Collections.ObjectModel;
using Avalonia;

namespace Nodes.StateMachine.Machine;

/// <summary>
/// Машина состояний, как её держит приложение: узлы и связи. Редактору она отдаёт обе коллекции.
/// </summary>
/// <remarks>
/// Состояние и переход — узлы; связь знает концы портами, а не координатами. Правила связи —
/// <see cref="CanConnect"/>: состояние принимает сколько угодно переходов, у перехода один источник и
/// одна цель, из состояния в состояние напрямую ведёт только вход.
/// </remarks>
public sealed class MachineDocument
{
    public ObservableCollection<MachineNode> Nodes { get; } = new();

    public ObservableCollection<FlowLink> Links { get; } = new();

    /// <summary>
    /// Можно ли соединить эти порты.
    /// </summary>
    /// <param name="source">Выход.</param>
    /// <param name="target">Вход.</param>
    /// <param name="moving">Связь, чей конец перецепляют, или <see langword="null"/> для новой: в
    /// правилах она не считается — её концы заняты ею же.</param>
    public bool CanConnect(FlowPort source, FlowPort target, FlowLink? moving)
    {
        if (ReferenceEquals(source.Node, target.Node))
            return false;

        var others = Links.Where(link => !ReferenceEquals(link, moving)).ToList();
        if (others.Any(link => ReferenceEquals(link.From, source) && ReferenceEquals(link.To, target)))
            return false;

        // У перехода и узла перенаправления один вход и один выход, у входа машины — одно начальное состояние:
        // иначе путь из состояния стал бы неоднозначным.
        if (target.Node is TransitionNode or RerouteNode && others.Any(link => ReferenceEquals(link.To, target)))
            return false;

        if (source.Node is TransitionNode or RerouteNode or EntryNode && others.Any(link => ReferenceEquals(link.From, source)))
            return false;

        // Переход ведёт в состояние; из состояния в состояние напрямую — только вход.
        return (source.Node, target.Node) switch
        {
            (TransitionNode, TransitionNode) => false,
            (StateNode or AnyStateNode, StateNode) => false,
            (EntryNode, TransitionNode) => false,
            _ => true
        };
    }

    /// <summary>
    /// Куда ведёт выход: через узлы перенаправления до первого узла, который не узел перенаправления.
    /// </summary>
    /// <param name="output">Выход, с которого начать.</param>
    /// <param name="path">Провода пути — окно зажигает их, когда путь сработал.</param>
    /// <returns>Узлы на концах путей: у состояния их столько, сколько из него переходов.</returns>
    public IEnumerable<(MachineNode End, IReadOnlyList<FlowLink> Path)> Follow(FlowPort output)
    {
        foreach (var first in Links.Where(link => ReferenceEquals(link.From, output)).ToList())
        {
            var path = new List<FlowLink> { first };
            var node = first.To.Node;

            // У узла перенаправления один выход; счётчик — защита от кольца из таких узлов.
            for (var hops = 0; node is RerouteNode reroute && hops < 64; hops++)
            {
                var next = Links.FirstOrDefault(link => ReferenceEquals(link.From, reroute.Outputs[0]));
                if (next == null)
                    break;

                path.Add(next);
                node = next.To.Node;
            }

            if (node is not RerouteNode)
                yield return (node, path);
        }
    }

    /// <summary>
    /// Начальное состояние — куда ведёт вход.
    /// </summary>
    public StateNode? InitialState()
    {
        var entry = Nodes.OfType<EntryNode>().FirstOrDefault();
        return entry == null ? null : Follow(entry.Outputs[0]).Select(end => end.End).OfType<StateNode>().FirstOrDefault();
    }

    /// <summary>
    /// Машина, которая ведёт приложение: вход, проверка пароля, ошибка, главная, карточка и настройки;
    /// пропала сеть — из любого состояния на экран «Нет сети».
    /// </summary>
    /// <remarks>
    /// Провод выходит вправо и входит слева, поэтому машина читается слева направо по главному ряду.
    /// Обратный переход стоит слева от состояния, в которое возвращает, на своей полосе — выше или ниже
    /// главного ряда: длинный провод идёт по полосе, а в состояние входит короткий, и узлов провода не
    /// пересекают. Выход с главной возвращается ко входу узлами перенаправления по нижнему краю.
    /// </remarks>
    public static MachineDocument CreateUiExample()
    {
        var d = new MachineDocument();

        // Верх: любое состояние и сеть.
        var any = d.Add(new AnyStateNode(new Point(40, 40)));
        var offline = d.Add(new StateNode(AppScreen.Offline, new Point(500, 40)));

        // Главный ряд.
        var entry = d.Add(new EntryNode(new Point(40, 300)));
        var login = d.Add(new StateNode(AppScreen.Login, new Point(260, 300)));
        var checking = d.Add(new StateNode(AppScreen.Checking, new Point(740, 300)));
        var home = d.Add(new StateNode(AppScreen.Home, new Point(1220, 300)));
        var details = d.Add(new StateNode(AppScreen.Details, new Point(1700, 220)));
        var settings = d.Add(new StateNode(AppScreen.Settings, new Point(1700, 420)));
        var error = d.Add(new StateNode(AppScreen.LoginError, new Point(1220, 570)));

        d.Connect(entry, login);
        d.Transition(any, offline, Triggers.NetworkLost, new Point(270, 50));
        d.Transition(offline, login, Triggers.NetworkBack, new Point(40, 170));

        d.Transition(login, checking, Triggers.SignIn, new Point(500, 310));
        d.Transition(checking, home, Triggers.PasswordValid, new Point(980, 310));
        d.Transition(checking, error, Triggers.PasswordInvalid, new Point(980, 580));
        d.Transition(error, login, Triggers.Retry, new Point(40, 440));

        d.Transition(home, details, Triggers.Open, new Point(1460, 230));
        d.Transition(details, home, Triggers.Back, new Point(980, 110));
        d.Transition(home, settings, Triggers.OpenSettings, new Point(1460, 430));
        d.Transition(settings, home, Triggers.Back, new Point(980, 470));

        // Выход — к началу узлами перенаправления по низу: переход слева от входа занял бы полосу «Повторить».
        var signOut = d.Add(new TransitionNode(Triggers.SignOut, new Point(1460, 600)));
        var turn = d.Add(new RerouteNode(new Point(1690, 730)));
        var back = d.Add(new RerouteNode(new Point(215, 730)));
        d.Connect(home, signOut);
        d.Connect(signOut, turn);
        d.Connect(turn, back);
        d.Connect(back, login);

        return d;
    }

    private T Add<T>(T node)
        where T : MachineNode
    {
        Nodes.Add(node);
        return node;
    }

    private void Connect(MachineNode from, MachineNode to) => Links.Add(new FlowLink(from.Outputs[0], to.Inputs[0]));

    private void Transition(MachineNode from, StateNode to, Trigger trigger, Point location)
    {
        var transition = Add(new TransitionNode(trigger, location));
        Connect(from, transition);
        Connect(transition, to);
    }
}
