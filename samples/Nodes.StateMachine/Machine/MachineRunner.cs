using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;

namespace Nodes.StateMachine.Machine;

/// <summary>
/// Запущенная машина: ведёт приложение по графу, который лежит на холсте.
/// </summary>
/// <remarks>
/// <para>
/// Такт — 50 мс, как кадр игры для правил переходов Unreal: на каждом такте проверяются переходы
/// активного состояния и «любого состояния», по порядку коллекции, и срабатывает первый, чьё условие
/// выполнено. Событие интерфейса живёт один такт: не нужное ни одному переходу, оно пропадает — нажатие
/// «Назад» на экране входа ничего не делает.
/// </para>
/// <para>
/// Граф машина читает живым: правка на холсте во время работы действует со следующего такта, а
/// удалённое активное состояние останавливает машину. Показ — как в отладке State Machine Unreal:
/// активное состояние горит (<see cref="RunLook.Active"/>) и считает время, сработавший переход и его
/// провода вспыхивают и гаснут за секунду, покинутое состояние гаснет так же.
/// </para>
/// </remarks>
public sealed class MachineRunner : Observable
{
    private const double TickSeconds = 0.05;

    /// <summary>
    /// Сколько тактов гаснет след перехода и покинутого состояния.
    /// </summary>
    private const int TraceTicks = 20;

    private readonly MachineDocument _document;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private readonly HashSet<string> _events = new();
    private readonly Dictionary<MachineNode, int> _traces = new();
    private readonly HashSet<FlowLink> _hot = new();
    private StateNode? _current;
    private double _secondsInState;
    private TimeSpan _last;
    private int _ticks;

    public MachineRunner(MachineDocument document, MachineContext context)
    {
        _document = document;
        Context = context;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(TickSeconds) };
        _timer.Tick += (_, _) => Tick();
    }

    public MachineContext Context { get; }

    /// <summary>
    /// Журнал: сверху — последнее.
    /// </summary>
    public ObservableCollection<string> Log { get; } = new();

    public bool IsRunning => _timer.IsEnabled;

    /// <summary>
    /// Активное состояние, или <see langword="null"/>, пока машина стоит.
    /// </summary>
    public StateNode? Current
    {
        get => _current;
        private set
        {
            if (Set(ref _current, value))
                Raise(nameof(Screen));
        }
    }

    /// <summary>
    /// Экран приложения, который показывает активное состояние.
    /// </summary>
    public AppScreen? Screen => _current?.Screen;

    public void Start()
    {
        if (IsRunning)
            return;

        if (_document.InitialState() is not { } initial)
        {
            Write("Нет начального состояния: соедините «Вход» с состоянием.");
            return;
        }

        _events.Clear();
        _clock.Restart();
        _last = TimeSpan.Zero;
        _timer.Start();
        Raise(nameof(IsRunning));
        Write($"Запуск: {initial.Title}");
        Enter(initial);
    }

    public void Stop()
    {
        if (!IsRunning)
            return;

        _timer.Stop();
        foreach (var node in _document.Nodes)
        {
            node.Look = RunLook.None;
            node.RunText = null;
        }

        foreach (var link in _hot)
            link.Heat = 0;

        _hot.Clear();
        _traces.Clear();
        Current = null;
        Raise(nameof(IsRunning));
        Write("Остановлено");
    }

    /// <summary>
    /// Событие интерфейса: его увидит следующий такт.
    /// </summary>
    public void Fire(string name)
    {
        if (IsRunning)
            _events.Add(name);
    }

    private void Tick()
    {
        var now = _clock.Elapsed;
        var dt = (now - _last).TotalSeconds;
        _last = now;

        if (_current is not { } current || !_document.Nodes.Contains(current))
        {
            Write("Активное состояние удалено с холста.");
            Stop();
            return;
        }

        _secondsInState += dt;
        current.RunText = $"▶ {_secondsInState:0.0} с";

        if (Choose(current) is { } step)
            Take(current, step.Transition, step.Target, step.Path);

        _events.Clear();
        Fade();
    }

    /// <summary>
    /// Первый переход, чьё условие выполнено: сперва из активного состояния, потом из любого — по
    /// порядку проводов в коллекции.
    /// </summary>
    private (TransitionNode Transition, StateNode Target, List<FlowLink> Path)? Choose(StateNode current)
    {
        var sources = new List<MachineNode> { current };
        sources.AddRange(_document.Nodes.OfType<AnyStateNode>());

        foreach (var source in sources)
        {
            foreach (var (end, into) in _document.Follow(source.Outputs[0]))
            {
                if (end is not TransitionNode transition || !transition.Trigger.IsMet(Context, _events, _secondsInState))
                    continue;

                foreach (var (target, outOf) in _document.Follow(transition.Outputs[0]))
                {
                    // Из любого состояния в то, где машина уже стоит, не переходят: условие «нет сети»
                    // держится, и машина входила бы в «Нет сети» на каждом такте.
                    if (target is StateNode state && !(source is AnyStateNode && ReferenceEquals(state, current)))
                        return (transition, state, [.. into, .. outOf]);
                }
            }
        }

        return null;
    }

    private void Take(StateNode from, TransitionNode transition, StateNode target, List<FlowLink> path)
    {
        Write($"{from.Title} → {target.Title}  ({transition.Title}, {_secondsInState:0.0} с)");

        from.RunText = null;
        Trace(from);
        Trace(transition);
        transition.RunText = "сработал";
        foreach (var link in path)
        {
            link.Heat = 10;
            _hot.Add(link);
        }

        Enter(target);
    }

    private void Enter(StateNode state)
    {
        _traces.Remove(state);
        _secondsInState = 0;
        state.Look = RunLook.Active;
        state.RunText = "▶ 0,0 с";
        Current = state;
    }

    private void Trace(MachineNode node)
    {
        node.Look = RunLook.Trace;
        _traces[node] = TraceTicks;
    }

    /// <summary>
    /// Гасит следы: провода — ступенью накала через такт, узлы — по счёту тактов; и то и другое — за
    /// секунду.
    /// </summary>
    private void Fade()
    {
        foreach (var link in _ticks++ % 2 == 0 ? _hot.ToList() : [])
        {
            link.Heat -= 1;
            if (link.Heat == 0)
                _hot.Remove(link);
        }

        foreach (var (node, left) in _traces.ToList())
        {
            if (left > 1)
            {
                _traces[node] = left - 1;
                continue;
            }

            _traces.Remove(node);
            if (!ReferenceEquals(node, _current))
            {
                node.Look = RunLook.None;
                if (node is TransitionNode)
                    node.RunText = null;
            }
        }
    }

    private void Write(string line)
    {
        Log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {line}");
        while (Log.Count > 200)
            Log.RemoveAt(Log.Count - 1);
    }
}
