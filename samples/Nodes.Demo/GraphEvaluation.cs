using System.Diagnostics;
using Avalonia.Threading;

namespace Nodes.Demo;

/// <summary>
/// Волна вычисления графа: значения идут от источников по связям, узел за узлом. Ею демо зажигает
/// импульсы редактора (ADR 0014 библиотеки) — как пузыри на проводах Blueprint во время Play.
/// </summary>
/// <remarks>
/// План строится один раз на запуск, обходом Кана: связь срабатывает, когда посчитан её источник, а
/// узел считается через <see cref="Step"/> после последнего пришедшего значения. Узел перенаправления
/// задержки не добавляет — провод через него один провод, и оба его куска вспыхивают вместе. Связи
/// внутри кольца в план не попадают: у кольца нет узла, который посчитался бы первым
/// (<see cref="Skipped"/>). Граф правят и во время волны; план этого не видит, а ушедшую связь
/// редактор просто не зажжёт.
/// </remarks>
public sealed class GraphEvaluation
{
    /// <summary>
    /// Время счёта одного узла.
    /// </summary>
    public static readonly TimeSpan Step = TimeSpan.FromMilliseconds(300);

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = new();
    private List<(TimeSpan At, GraphLink Link)> _plan = [];
    private int _next;

    public GraphEvaluation()
    {
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>
    /// Связь сработала: её источник посчитан, значение пошло во вход.
    /// </summary>
    public event Action<GraphLink>? LinkFired;

    /// <summary>
    /// Волна дошла до конца или остановлена.
    /// </summary>
    public event Action? Finished;

    public bool IsRunning => _timer.IsEnabled;

    /// <summary>
    /// Сколько связей последнего плана лежит в кольцах и не сработает.
    /// </summary>
    public int Skipped { get; private set; }

    /// <summary>
    /// Сколько длится волна последнего плана.
    /// </summary>
    public TimeSpan Duration { get; private set; }

    public void Start(GraphDocument document)
    {
        _plan = Plan(document);
        Skipped = document.Links.Count - _plan.Count;
        Duration = _plan.Count > 0 ? _plan[^1].At : TimeSpan.Zero;
        _next = 0;
        _clock.Restart();
        _timer.Start();
        Tick();
    }

    public void Stop()
    {
        if (!IsRunning)
            return;

        _timer.Stop();
        Finished?.Invoke();
    }

    /// <summary>
    /// План волны: связи в порядке срабатывания и момент каждой от начала.
    /// </summary>
    public static List<(TimeSpan At, GraphLink Link)> Plan(GraphDocument document)
    {
        var waiting = new Dictionary<GraphNode, int>(ReferenceEqualityComparer.Instance);
        foreach (var link in document.Links)
            waiting[link.To.Node] = waiting.GetValueOrDefault(link.To.Node) + 1;

        var outgoing = document.Links.ToLookup(link => link.From.Node, ReferenceEqualityComparer.Instance);
        var ready = new Dictionary<GraphNode, TimeSpan>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<GraphNode>();
        foreach (var node in document.Nodes.Where(node => !waiting.ContainsKey(node)))
        {
            ready[node] = TimeSpan.Zero;
            queue.Enqueue(node);
        }

        var plan = new List<(TimeSpan At, GraphLink Link)>(document.Links.Count);
        while (queue.TryDequeue(out var node))
        {
            var at = ready[node];
            foreach (var link in outgoing[node])
            {
                plan.Add((at, link));

                var target = link.To.Node;
                var arrives = at + (target is RerouteNode ? TimeSpan.Zero : Step);
                if (!ready.TryGetValue(target, out var known) || arrives > known)
                    ready[target] = arrives;

                if (--waiting[target] == 0)
                    queue.Enqueue(target);
            }
        }

        // Устойчиво: в один момент связи срабатывают в порядке обхода.
        return plan.OrderBy(step => step.At).ToList();
    }

    private void Tick()
    {
        var now = _clock.Elapsed;
        while (_next < _plan.Count && _plan[_next].At <= now)
            LinkFired?.Invoke(_plan[_next++].Link);

        if (_next >= _plan.Count)
            Stop();
    }
}
