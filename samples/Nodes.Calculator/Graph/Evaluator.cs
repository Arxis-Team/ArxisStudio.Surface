namespace Nodes.Calculator.Graph;

/// <summary>
/// Что сделала «Вывести строку»: текст и куда он пошёл, как параметры Print String в Blueprint.
/// </summary>
public sealed record PrintMessage(string Text, bool ToScreen, bool ToLog, double Duration);

/// <summary>
/// Шаг прогона: в миг <see cref="Moment"/> сработал провод <see cref="Link"/> или узел вывел строку.
/// </summary>
public sealed record TraceEvent(int Moment, CalcLink? Link, PrintMessage? Print);

/// <summary>
/// Прогон «Играть»: шаги по порядку и ошибка, если выполнение остановлено.
/// </summary>
public sealed record Trace(IReadOnlyList<TraceEvent> Events, string? Error, CalcNode? Failed);

/// <summary>
/// Счёт графа по правилам Blueprint.
/// </summary>
/// <remarks>
/// Выполнение идёт по проводам выполнения от события; чистый узел своего хода не имеет и считается,
/// когда его значение спросит узел, который выполняется, — каждый раз заново, как в Blueprint. Живой
/// счёт (<see cref="Preview"/>) спрашивает каждый выход без выполнения, с переменными по умолчанию:
/// поэтому значения видно на холсте ещё до запуска. «Играть» (<see cref="Run"/>) записывает прогон
/// шагами: провод выполнения — новый миг, провода данных, прочитанные узлом, — тот же миг, что и узел.
/// Окно проигрывает запись импульсами на проводах.
/// </remarks>
public sealed class Evaluator
{
    /// <summary>
    /// Сколько узлов выполнения проходит прогон, прежде чем счесть кольцо бесконечным, — как защита
    /// Blueprint от бесконечного цикла.
    /// </summary>
    public const int StepLimit = 1000;

    private const int DepthLimit = 256;

    private readonly CalcDocument _document;
    private readonly bool _running;
    private readonly Dictionary<CalcVariable, object> _variables = new();
    private readonly Dictionary<CalcNode, int> _loopIndex = new();
    private readonly Dictionary<CalcNode, object> _assigned = new();
    private readonly Dictionary<CalcNode, string> _warnings = new();
    private readonly List<TraceEvent> _trace = new();
    private readonly HashSet<CalcLink> _readNow = new();
    private int _moment;
    private int _steps;
    private int _depth;

    private Evaluator(CalcDocument document, bool running)
    {
        _document = document;
        _running = running;
    }

    /// <summary>
    /// Живой счёт: значение каждого выхода данных и предупреждения узлов.
    /// </summary>
    public static void Preview(CalcDocument document)
    {
        var evaluator = new Evaluator(document, running: false);
        foreach (var node in document.Nodes)
        {
            foreach (var output in node.Outputs)
            {
                output.Display = output.Type is PinType.Exec or PinType.Wildcard || node is RerouteNode
                    ? null
                    : Values.Format(evaluator.OutputValue(output));
            }
        }

        foreach (var node in document.Nodes)
            node.Warning = evaluator._warnings.GetValueOrDefault(node);
    }

    /// <summary>
    /// Прогон от каждого «Начала игры» в порядке узлов.
    /// </summary>
    public static Trace Run(CalcDocument document)
    {
        var evaluator = new Evaluator(document, running: true);
        try
        {
            foreach (var begin in document.Nodes.Where(n => n.Definition == NodeCatalog.BeginPlay).ToList())
                evaluator.Fire(begin.Outputs[0]);
        }
        catch (LoopLimitException limit)
        {
            return new Trace(evaluator._trace, $"Бесконечный цикл: выполнение остановлено после {StepLimit} шагов.", limit.Node);
        }

        return new Trace(evaluator._trace, null, null);
    }

    /// <summary>
    /// Выполнение уходит из выхода по его проводу — в следующий узел.
    /// </summary>
    /// <param name="output">Выход выполнения.</param>
    /// <param name="advance">Начинать ли новый миг: излом провода своего мига не занимает.</param>
    private void Fire(CalcPort output, bool advance = true)
    {
        if (_document.LinkFrom(output) is not { } link)
            return;

        if (advance)
        {
            _moment++;
            _readNow.Clear();
        }

        Record(link);
        if (link.To.Node is RerouteNode knot)
        {
            Fire(knot.Outputs[0], advance: false);
            return;
        }

        Execute(link.To.Node);
    }

    private void Execute(CalcNode node)
    {
        if (++_steps > StepLimit)
            throw new LoopLimitException(node);

        var inputs = node.Inputs;
        var outputs = node.Outputs;
        switch (node.Definition.Id)
        {
            case "print":
                var message = new PrintMessage(
                    (string)InputValue(inputs[1]), (bool)InputValue(inputs[2]), (bool)InputValue(inputs[3]), (double)InputValue(inputs[4]));
                _trace.Add(new TraceEvent(_moment, null, message));
                Fire(outputs[0]);
                break;

            case "branch":
                Fire((bool)InputValue(inputs[1]) ? outputs[0] : outputs[1]);
                break;

            case "sequence":
                foreach (var then in outputs.ToList())
                    Fire(then);
                break;

            case "for-loop":
                var (first, last) = ((int)InputValue(inputs[1]), (int)InputValue(inputs[2]));
                for (var index = first; index <= last; index++)
                {
                    _loopIndex[node] = index;
                    Fire(outputs[0]);
                }

                Fire(outputs[2]);
                break;

            case "set":
                var value = InputValue(inputs[1]);
                _variables[node.Variable!] = value;
                _assigned[node] = value;
                Fire(outputs[0]);
                break;
        }
    }

    /// <summary>
    /// Значение входа: из провода, приведённое к типу входа, или литерал неподключённого.
    /// </summary>
    private object InputValue(CalcPort input)
    {
        if (_document.LinkInto(input) is not { } link)
            return input.Literal;

        Record(link);
        var value = OutputValue(link.From);
        return input.Type == PinType.Wildcard ? value : Values.Coerce(value, input.Type);
    }

    private object OutputValue(CalcPort output)
    {
        var node = output.Node;
        if (++_depth > DepthLimit)
        {
            _depth--;
            return Values.DefaultOf(output.Type);
        }

        try
        {
            if (node is RerouteNode)
                return InputValue(node.Inputs[0]);

            switch (node.Definition.Id)
            {
                case "get":
                    return _running && _variables.TryGetValue(node.Variable!, out var current) ? current : node.Variable!.DefaultValue;

                case "set":
                    return _assigned.TryGetValue(node, out var assigned) ? assigned : InputValue(node.Inputs[1]);

                case "for-loop":
                    return _loopIndex.TryGetValue(node, out var index) ? index : InputValue(node.Inputs[1]);
            }

            if (node.Definition.Compute is not { } compute)
                return Values.DefaultOf(output.Type);

            var context = new ComputeContext();
            var result = compute(node.Inputs.Select(InputValue).ToArray(), context);
            if (context.Warning is { } warning)
                _warnings[node] = warning;

            return result;
        }
        finally
        {
            _depth--;
        }
    }

    /// <summary>
    /// Провод сработал в этот миг — один раз, сколько бы узлов его ни спросило.
    /// </summary>
    private void Record(CalcLink link)
    {
        if (_running && _readNow.Add(link))
            _trace.Add(new TraceEvent(_moment, link, null));
    }

    private sealed class LoopLimitException(CalcNode node) : Exception
    {
        public CalcNode Node { get; } = node;
    }
}
