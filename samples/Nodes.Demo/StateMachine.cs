using Avalonia;

namespace Nodes.Demo;

/// <summary>
/// Машина состояний персонажа — второй пример графа, как State Machine в Unreal и Animator в Unity.
/// </summary>
/// <remarks>
/// Состояние — узел, переход — тоже узел, а не подпись на проводе: условие перехода — его название, и
/// на холсте его видно так же, как название состояния. Провода — поток управления (<see cref="PortKind.Flow"/>),
/// у них свой цвет. Правила связи — в <see cref="GraphDocument.CanConnect"/>: состояние принимает
/// сколько угодно переходов, у перехода один источник и одна цель, из состояния в состояние напрямую
/// ведёт только вход.
/// </remarks>
public sealed partial class GraphDocument
{
    /// <summary>
    /// Персонаж: покой, ходьба, бег, прыжок, падение, приземление и смерть из любого состояния.
    /// </summary>
    public static GraphDocument CreateStateMachine()
    {
        var document = new GraphDocument();

        // Провод выходит вправо и входит слева, поэтому граф читается слева направо: переходы стоят
        // между состояниями, обратные — под прямыми, а возврат из приземления в покой идёт двумя
        // перевалками вдоль нижнего края, а не через весь граф. Шаг — состояние, зазор, переход, зазор:
        // «показать всё» оставляет масштаб выше порога упрощённого вида, и узлы видны целиком.
        var any = document.AddState("Любое состояние", new Point(40, 40), StateRole.Any, clip: null);
        var death = document.AddState("Смерть", new Point(480, 40), StateRole.Terminal, "death");

        var entry = document.AddState("Вход", new Point(40, 250), StateRole.Entry, clip: null);
        var idle = document.AddState("Покой", new Point(260, 240), StateRole.State, "idle_loop");
        var walk = document.AddState("Ходьба", new Point(700, 240), StateRole.State, "walk_cycle");
        var run = document.AddState("Бег", new Point(1140, 240), StateRole.State, "run_cycle");

        var jump = document.AddState("Прыжок", new Point(700, 490), StateRole.State, "jump_start");
        var fall = document.AddState("Падение", new Point(1140, 490), StateRole.State, "fall_loop");
        var land = document.AddState("Приземление", new Point(1580, 490), StateRole.State, "land");

        document.Connect(entry, idle);
        document.Transition(any, death, "здоровье ≤ 0", new Point(260, 50));

        document.Transition(idle, walk, "скорость > 0,1", new Point(480, 180));
        document.Transition(walk, idle, "скорость < 0,1", new Point(480, 350));
        document.Transition(walk, run, "скорость > 3", new Point(920, 180));
        document.Transition(run, walk, "скорость < 3", new Point(920, 350));

        document.Transition(idle, jump, "прыжок", new Point(480, 500));
        document.Transition(jump, fall, "скорость по Y < 0", new Point(920, 500));
        document.Transition(fall, land, "на земле", new Point(1360, 500));

        // Возврат в покой — перевалками по низу: прямой провод прошёл бы через весь граф.
        var landed = new TransitionNode("клип окончен", new Point(1800, 500));
        var turn = document.CreateReroute(new Point(1990, 680));
        var back = document.CreateReroute(new Point(180, 680));
        document.Nodes.Add(landed);
        document.Nodes.Add(turn);
        document.Nodes.Add(back);
        document.Connect(land, landed);
        document.Connect(landed, turn);
        document.Connect(turn, back);
        document.Connect(back, idle);

        return document;
    }

    private StateNode AddState(string title, Point location, StateRole role, string? clip)
    {
        var state = new StateNode(title, location, role, clip);
        Nodes.Add(state);
        return state;
    }

    private void Connect(GraphNode from, GraphNode to) => Links.Add(new GraphLink(from.Outputs[0], to.Inputs[0]));

    private void Transition(StateNode from, StateNode to, string condition, Point location)
    {
        var transition = new TransitionNode(condition, location);
        Nodes.Add(transition);
        Connect(from, transition);
        Connect(transition, to);
    }

    /// <summary>
    /// Правила потока управления: состояние — переход — состояние.
    /// </summary>
    /// <param name="source">Выход.</param>
    /// <param name="target">Вход.</param>
    /// <param name="others">Связи графа, кроме той, чей конец перецепляют.</param>
    private static bool AllowsFlow(GraphPort source, GraphPort target, IReadOnlyCollection<GraphLink> others)
    {
        // Поток соединяется только с потоком — или с перевалкой, которая берёт что угодно.
        if (source.Kind is not (PortKind.Flow or PortKind.Any) || target.Kind is not (PortKind.Flow or PortKind.Any))
            return false;

        if (others.Any(link => ReferenceEquals(link.From, source) && ReferenceEquals(link.To, target)))
            return false;

        // У перехода один источник и одна цель; у входа — одно начальное состояние.
        if (target.Node is TransitionNode or RerouteNode && others.Any(link => ReferenceEquals(link.To, target)))
            return false;

        if (source.Node is TransitionNode or StateNode { Role: StateRole.Entry } && others.Any(link => ReferenceEquals(link.From, source)))
            return false;

        // Переход ведёт в состояние, а из состояния в состояние напрямую — только вход.
        return (source.Node, target.Node) switch
        {
            (TransitionNode, TransitionNode) => false,
            (StateNode { Role: not StateRole.Entry }, StateNode) => false,
            (StateNode { Role: StateRole.Entry }, TransitionNode) => false,
            _ => true
        };
    }
}

/// <summary>
/// Чем состояние служит машине.
/// </summary>
public enum StateRole
{
    /// <summary>Обычное состояние: переходы входят и выходят.</summary>
    State,

    /// <summary>Вход машины: одна связь — прямо в начальное состояние.</summary>
    Entry,

    /// <summary>Любое состояние: его переходы срабатывают, где бы машина ни стояла.</summary>
    Any,

    /// <summary>Конечное состояние: переходы только входят.</summary>
    Terminal
}

/// <summary>
/// Состояние машины: название, клип анимации и порты потока.
/// </summary>
public sealed class StateNode : GraphNode
{
    public StateNode(string title, Point location, StateRole role, string? clip)
        : base(
            title,
            location,
            role is StateRole.Entry or StateRole.Any ? [] : [new GraphPortSpec("вход", PortKind.Flow)],
            role is StateRole.Terminal ? [] : [new GraphPortSpec("выход", PortKind.Flow)])
    {
        Role = role;
        Clip = clip;
        Accent = role switch
        {
            StateRole.Entry => NodeKinds.Filter,
            StateRole.Any => NodeKinds.Utility,
            StateRole.Terminal => NodeKinds.Output,
            _ => NodeKinds.Source
        };
    }

    public StateRole Role { get; }

    /// <summary>
    /// Клип анимации, который играет состояние; у входа и «любого» — нет.
    /// </summary>
    public string? Clip { get; }
}

/// <summary>
/// Переход между состояниями: его название — условие, порты — вход из состояния и выход в состояние.
/// </summary>
public sealed class TransitionNode : GraphNode
{
    /// <remarks>
    /// Цвет перехода — оранжевый вид, тот же, что у смешивания в графе изображения: переход
    /// «смешивает» два состояния.
    /// </remarks>
    public TransitionNode(string condition, Point location)
        : base(condition, location, [new GraphPortSpec(string.Empty, PortKind.Flow)], [new GraphPortSpec(string.Empty, PortKind.Flow)])
    {
        Accent = NodeKinds.Blend;
    }

    /// <summary>
    /// Условие перехода — оно же название узла.
    /// </summary>
    public string Condition => Title;
}
