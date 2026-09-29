using System.Collections.ObjectModel;
using Avalonia;

namespace Nodes.Calculator.Graph;

/// <summary>
/// Как соединяются два пина: напрямую, через узел преобразования или никак.
/// </summary>
public enum Connection
{
    Refused,
    Direct,
    Converted
}

/// <summary>
/// Граф калькулятора: узлы, провода и переменные. Редактору окно отдаёт узлы и провода; правила
/// соединения — здесь, а не в библиотеке (ADR 0016 библиотеки).
/// </summary>
public sealed class CalcDocument
{
    public ObservableCollection<CalcNode> Nodes { get; } = new();

    public ObservableCollection<CalcLink> Links { get; } = new();

    public ObservableCollection<CalcVariable> Variables { get; } = new();

    /// <summary>
    /// Можно ли соединить выход и вход — правила Blueprint.
    /// </summary>
    /// <remarks>
    /// В свой узел нельзя. Выполнение — только с выполнением. Одинаковые типы — напрямую, узел
    /// перенаправления без провода — с любым типом. Разные типы — только если есть узел преобразования:
    /// целое в число, число в целое, число, целое и логическое в строку; его поставит окно. Провод
    /// данных не должен замкнуть кольцо: у чистого узла, зависящего от себя, значения нет. Кольцо
    /// выполнения можно — это цикл, и «Играть» остановит его на тысячном шаге. Занятый вход данных и
    /// занятый выход выполнения не отказывают: новый провод заменит прежний, как у Blueprint.
    /// </remarks>
    /// <param name="source">Выход.</param>
    /// <param name="target">Вход.</param>
    /// <param name="moving">Провод, чей конец перецепляют: в проверке кольца его нет.</param>
    public Connection Check(CalcPort source, CalcPort target, CalcLink? moving = null)
    {
        if (ReferenceEquals(source.Node, target.Node))
            return Connection.Refused;

        var (from, to) = (source.Type, target.Type);
        if (from == PinType.Exec || to == PinType.Exec)
            return from == to || from == PinType.Wildcard || to == PinType.Wildcard ? Connection.Direct : Connection.Refused;

        Connection result;
        if (from == to || from == PinType.Wildcard || to == PinType.Wildcard)
            result = Connection.Direct;
        else if (NodeCatalog.ConversionOf(from, to) != null)
            result = Connection.Converted;
        else
            return Connection.Refused;

        return Reaches(target.Node, source.Node, moving) ? Connection.Refused : result;
    }

    /// <summary>
    /// Провод, который занимает этот вход данных или выход выполнения, — его заменит новый.
    /// </summary>
    public CalcLink? Occupant(CalcPort source, CalcPort target)
    {
        if (source.Type == PinType.Exec || (source.Type == PinType.Wildcard && target.Type == PinType.Exec))
            return Links.FirstOrDefault(link => ReferenceEquals(link.From, source));

        return target.Type == PinType.Exec ? null : Links.FirstOrDefault(link => ReferenceEquals(link.To, target));
    }

    public CalcLink? LinkInto(CalcPort input) => Links.FirstOrDefault(link => ReferenceEquals(link.To, input));

    public CalcLink? LinkFrom(CalcPort output) => Links.FirstOrDefault(link => ReferenceEquals(link.From, output));

    /// <summary>
    /// Что уйдёт вместе с этими проводами: узлы перенаправления, оставшиеся без входа или без выхода, и
    /// их прочие провода — провод через излом один провод.
    /// </summary>
    public (List<CalcLink> Links, List<CalcNode> Knots) Stranded(IEnumerable<CalcLink> links, IEnumerable<CalcNode> nodes)
    {
        var gone = links.ToHashSet();
        var goneNodes = nodes.ToHashSet();
        var knots = new List<CalcNode>();
        var queue = new Queue<RerouteNode>(gone.SelectMany(l => new[] { l.From.Node, l.To.Node }).OfType<RerouteNode>());

        while (queue.TryDequeue(out var knot))
        {
            if (goneNodes.Contains(knot))
                continue;

            var live = Links.Where(l => !gone.Contains(l) && (ReferenceEquals(l.From.Node, knot) || ReferenceEquals(l.To.Node, knot))).ToList();
            if (live.Any(l => ReferenceEquals(l.To.Node, knot)) && live.Any(l => ReferenceEquals(l.From.Node, knot)))
                continue;

            goneNodes.Add(knot);
            knots.Add(knot);
            foreach (var link in live)
            {
                gone.Add(link);
                if ((ReferenceEquals(link.From.Node, knot) ? link.To.Node : link.From.Node) is RerouteNode next)
                    queue.Enqueue(next);
            }
        }

        return (Links.Where(gone.Contains).ToList(), knots);
    }

    /// <summary>
    /// Даёт узлам перенаправления тип провода, который через них идёт, и перекрашивает их провода.
    /// </summary>
    /// <remarks>
    /// Тип ищется по цепочке изломов в обе стороны до первого пина с типом; не нашёлся — излом снова
    /// «любой» и примет что угодно.
    /// </remarks>
    public void Retype()
    {
        foreach (var knot in Nodes.OfType<RerouteNode>())
        {
            var type = Resolve(knot, new HashSet<CalcNode>());
            if (knot.Inputs[0].Type == type)
                continue;

            knot.Inputs[0].Type = type;
            knot.Outputs[0].Type = type;
            foreach (var link in Links.Where(l => ReferenceEquals(l.From.Node, knot) || ReferenceEquals(l.To.Node, knot)))
                link.Retyped();
        }
    }

    private PinType Resolve(CalcNode knot, HashSet<CalcNode> seen)
    {
        if (!seen.Add(knot))
            return PinType.Wildcard;

        foreach (var link in Links)
        {
            CalcPort? other = ReferenceEquals(link.To.Node, knot) ? link.From : ReferenceEquals(link.From.Node, knot) ? link.To : null;
            if (other == null)
                continue;

            var type = other.Node is RerouteNode next ? Resolve(next, seen) : other.Type;
            if (type != PinType.Wildcard)
                return type;
        }

        return PinType.Wildcard;
    }

    /// <summary>
    /// Доходит ли значение от узла <paramref name="from"/> до узла <paramref name="to"/> по проводам данных.
    /// </summary>
    private bool Reaches(CalcNode from, CalcNode to, CalcLink? moving)
    {
        var seen = new HashSet<CalcNode>();
        var stack = new Stack<CalcNode>([from]);
        while (stack.TryPop(out var node))
        {
            if (ReferenceEquals(node, to))
                return true;

            if (!seen.Add(node))
                continue;

            foreach (var link in Links)
            {
                if (!ReferenceEquals(link, moving) && ReferenceEquals(link.From.Node, node) && link.Type != PinType.Exec)
                    stack.Push(link.To.Node);
            }
        }

        return false;
    }

    /// <summary>
    /// Пример: гипотенуза по катетам A и B, сравнение её с порогом и квадраты индекса цикла.
    /// </summary>
    /// <remarks>
    /// «Начало игры» запускает «Последовательность». «Затем 0» выводит «Гипотенуза: 5» — строку
    /// собирают «Соединить» и узел преобразования числа в строку из √(A × A + B × B) — и ветвится по
    /// сравнению гипотенузы с 4,5. «Затем 1» — цикл от 1 до 3: индекс, целое, проходит в «×» через
    /// преобразование в число, и тело цикла выводит его квадрат. «Завершено» выводит «Готово». Все
    /// значения видно на выходах сразу, без запуска; «Играть» прогоняет выполнение и пишет в журнал.
    /// </remarks>
    public static CalcDocument CreateSample()
    {
        var document = new CalcDocument();
        var a = new CalcVariable("A", "3");
        var b = new CalcVariable("B", "4");
        document.Variables.Add(a);
        document.Variables.Add(b);

        var begin = document.Add(NodeCatalog.BeginPlay, 0, 0);
        var sequence = document.Add(NodeCatalog.Sequence, 170, 0);

        // Гипотенуза — чистые узлы: считаются, когда их спросит «Вывести строку».
        var getA = document.Add(NodeCatalog.Get, -40, 170, a);
        var getB = document.Add(NodeCatalog.Get, -40, 250, b);
        var squareA = document.Add(NodeCatalog.Multiply, 100, 150);
        var squareB = document.Add(NodeCatalog.Multiply, 100, 235);
        var sum = document.Add(NodeCatalog.Add, 400, 190);
        var root = document.Add(NodeCatalog.SquareRoot, 556, 200);
        var text = document.Add(NodeCatalog.FloatToString, 772, 150);
        var append = document.Add(NodeCatalog.Append, 890, 100);
        append.Inputs[0].Text = "Гипотенуза: ";
        var greater = document.Add(NodeCatalog.Greater, 772, 260);
        greater.Inputs[1].Text = "4,5";

        var printHypotenuse = document.Add(NodeCatalog.Print, 1210, -20);
        var branch = document.Add(NodeCatalog.Branch, 1440, 130);
        var printMore = document.Add(NodeCatalog.Print, 1650, 0);
        printMore.Inputs[1].Text = "Больше 4,5";
        var printLess = document.Add(NodeCatalog.Print, 1650, 210);
        printLess.Inputs[1].Text = "Не больше 4,5";

        document.Connect(begin.Outputs[0], sequence.Inputs[0]);
        document.Connect(sequence.Outputs[0], printHypotenuse.Inputs[0]);
        document.Connect(getA.Outputs[0], squareA.Inputs[0]);
        document.Connect(getA.Outputs[0], squareA.Inputs[1]);
        document.Connect(getB.Outputs[0], squareB.Inputs[0]);
        document.Connect(getB.Outputs[0], squareB.Inputs[1]);
        document.Connect(squareA.Outputs[0], sum.Inputs[0]);
        document.Connect(squareB.Outputs[0], sum.Inputs[1]);
        document.Connect(sum.Outputs[0], root.Inputs[0]);
        document.Connect(root.Outputs[0], text.Inputs[0]);
        document.Connect(text.Outputs[0], append.Inputs[1]);
        document.Connect(append.Outputs[0], printHypotenuse.Inputs[1]);
        document.Connect(root.Outputs[0], greater.Inputs[0]);
        document.Connect(printHypotenuse.Outputs[0], branch.Inputs[0]);
        document.Connect(greater.Outputs[0], branch.Inputs[1]);
        document.Connect(branch.Outputs[0], printMore.Inputs[0]);
        document.Connect(branch.Outputs[1], printLess.Inputs[0]);

        // Цикл: индекс — целое, «×» — число; между ними стоит узел преобразования, как в Blueprint.
        var loop = document.Add(NodeCatalog.ForLoop, 380, 470);
        loop.Inputs[1].Text = "1";
        var toFloat = document.Add(NodeCatalog.IntegerToFloat, 730, 420);
        var square = document.Add(NodeCatalog.Multiply, 850, 400);
        var squareText = document.Add(NodeCatalog.FloatToString, 1005, 420);
        var line = document.Add(NodeCatalog.Append, 1120, 360);
        line.Inputs[0].Text = "Квадрат: ";
        var printSquare = document.Add(NodeCatalog.Print, 1440, 470);
        var printDone = document.Add(NodeCatalog.Print, 1440, 690);
        printDone.Inputs[1].Text = "Готово";

        document.Connect(sequence.Outputs[1], loop.Inputs[0]);
        document.Connect(loop.Outputs[0], printSquare.Inputs[0]);
        document.Connect(loop.Outputs[1], toFloat.Inputs[0]);
        document.Connect(toFloat.Outputs[0], square.Inputs[0]);
        document.Connect(toFloat.Outputs[0], square.Inputs[1]);
        document.Connect(square.Outputs[0], squareText.Inputs[0]);
        document.Connect(squareText.Outputs[0], line.Inputs[1]);
        document.Connect(line.Outputs[0], printSquare.Inputs[1]);
        document.Connect(loop.Outputs[2], printDone.Inputs[0]);

        return document;
    }

    private CalcNode Add(NodeDefinition definition, double x, double y, CalcVariable? variable = null)
    {
        var node = new CalcNode(definition, new Point(x, y), variable);
        Nodes.Add(node);
        return node;
    }

    private void Connect(CalcPort from, CalcPort to) => Links.Add(new CalcLink(from, to));
}
