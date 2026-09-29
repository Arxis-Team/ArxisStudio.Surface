using System.Collections.ObjectModel;
using System.ComponentModel;
using ArxisStudio.Surface.Nodes;
using Avalonia;
using Avalonia.Media;

namespace Nodes.Demo;

/// <summary>
/// Граф, как его держит приложение: узлы и связи. Редактору он отдаёт обе коллекции и больше ничего.
/// </summary>
/// <remarks>
/// Связь знает свои концы данными портов — <see cref="GraphPort"/>, — а не координатами (ADR 0004):
/// где порт на холсте, редактор находит сам.
/// </remarks>
public sealed class GraphDocument
{
    /// <summary>
    /// Шаг сетки <see cref="CreateGrid"/>. Узел с двумя входами и выходом в теме библиотеки — 178 × 76,
    /// тот же размер окно называет редактору предполагаемым; остальное — просвет для связей.
    /// </summary>
    private static readonly Vector GridStep = new(240, 150);

    private int _reroutes;

    public ObservableCollection<GraphNode> Nodes { get; } = new();

    public ObservableCollection<GraphLink> Links { get; } = new();

    /// <summary>
    /// Что уйдёт вместе с этими связями: узлы перенаправления, оставшиеся без входа или без выхода, и
    /// их прочие связи.
    /// </summary>
    /// <remarks>
    /// Провод через узлы перенаправления — один провод: сняли его кусок — уходит он целиком, и
    /// повисшего кольца на холсте не остаётся. Цепочка проходится до конца; узел перенаправления с
    /// живым входом и живым выходом остаётся — у разветвления уходит только снятая ветка.
    /// </remarks>
    /// <param name="links">Связи, которые снимают.</param>
    /// <param name="nodes">Узлы, которые снимают вместе с ними.</param>
    /// <returns>Все связи к снятию — и данные, и потянутые — и узлы перенаправления сверх данных.</returns>
    public (List<GraphLink> Links, List<GraphNode> Knots) Stranded(IEnumerable<GraphLink> links, IEnumerable<GraphNode> nodes)
    {
        var gone = links.ToHashSet();
        var goneNodes = nodes.ToHashSet();
        var knots = new List<GraphNode>();
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
    /// Можно ли соединить эти порты по правилам приложения.
    /// </summary>
    /// <param name="source">Выход.</param>
    /// <param name="target">Вход.</param>
    /// <param name="moving">Связь, чей конец перецепляют, или <see langword="null"/> для новой.</param>
    /// <remarks>
    /// Направления проверяет сам редактор; здесь — правила графа, как в Blueprint (ADR 0016 библиотеки):
    /// в свой же узел нельзя; выполнение соединяется только с выполнением, данные — с данными, узел
    /// перенаправления — с любым; делегат — только с делегатом; из выхода выполнения — одна связь, во
    /// вход выполнения — сколько угодно; во вход данных и делегата — одна, из выхода делегата — сколько
    /// угодно: одно событие подписывают на многих. Связь, что перецепляет свой конец, в правилах не
    /// считается: её концы заняты ею же.
    /// </remarks>
    public bool CanConnect(GraphPort source, GraphPort target, GraphLink? moving)
    {
        if (ReferenceEquals(source.Node, target.Node))
            return false;

        var role = source.Kind is PortKind.Any ? target.Role : source.Role;
        var other = target.Kind is PortKind.Any ? source.Role : target.Role;
        if (source.Kind is not PortKind.Any && target.Kind is not PortKind.Any && role != other)
            return false;

        var others = Links.Where(link => !ReferenceEquals(link, moving)).ToList();
        if (role is PinRole.Execution)
            return !others.Any(link => ReferenceEquals(link.From, source));

        return !others.Any(link => ReferenceEquals(link.To, target));
    }

    /// <summary>
    /// Новый узел перенаправления — узел с одним входом и одним выходом (ADR 0005 библиотеки).
    /// </summary>
    public RerouteNode CreateReroute(Point location) => new($"Узел перенаправления {++_reroutes}", location);

    public static GraphDocument CreateSample()
    {
        var document = new GraphDocument();

        var image = document.Add("Изображение", new Point(60, 80), inputs: [], outputs: [Color("Цвет"), Number("Альфа")], NodeKinds.Source);
        var number = document.Add("Число", new Point(60, 300), inputs: [], outputs: [Number("Значение")], NodeKinds.Source);
        var blur = document.Add("Размытие", new Point(360, 60), inputs: [Color("Цвет"), Number("Радиус")], outputs: [Color("Цвет")], NodeKinds.Filter);
        var mix = document.Add("Смешивание", new Point(660, 150), inputs: [Color("A"), Color("B"), Number("Доля")], outputs: [Color("Цвет")], NodeKinds.Blend);
        var output = document.Add("Вывод", new Point(960, 190), inputs: [Execution(), Color("Цвет")], outputs: [Execution()], NodeKinds.Output);

        // Поток выполнения, как в Blueprint: событие запускает «Вывод». Узлы данных пинов выполнения не
        // имеют: их значения считаются, когда до «Вывода» доходит выполнение.
        var tick = document.Add("Каждый кадр", new Point(660, 10), inputs: [], outputs: [Execution()], NodeKinds.Event);
        document.Links.Add(new GraphLink(tick.Outputs[0], output.Inputs[0]));

        // Наблюдатель, как Event Dispatcher Blueprint: «Вывод» вызывает «Готово», ничего не зная о
        // подписчиках; на старте событие «По «Готово»» отдаёт себя делегатом в «Подписаться», а когда
        // «Готово» вызвано, оно запускает «Журнал».
        var call = document.Add("Вызвать «Готово»", new Point(1260, 190), inputs: [Execution()], outputs: [Execution()], NodeKinds.Utility);
        var begin = document.Add("Начало", new Point(60, 460), inputs: [], outputs: [Execution()], NodeKinds.Event);
        var bind = document.Add("Подписаться на «Готово»", new Point(360, 460), inputs: [Execution(), Delegate("Событие")], outputs: [Execution()], NodeKinds.Utility);
        var ready = document.Add("По «Готово»", new Point(60, 620), inputs: [], outputs: [Delegate(string.Empty), Execution()], NodeKinds.Event);
        var log = document.Add("Журнал", new Point(960, 560), inputs: [Execution(), Color("Цвет")], outputs: [], NodeKinds.Utility);
        document.Links.Add(new GraphLink(output.Outputs[0], call.Inputs[0]));
        document.Links.Add(new GraphLink(begin.Outputs[0], bind.Inputs[0]));
        document.Links.Add(new GraphLink(ready.Outputs[0], bind.Inputs[1]));
        document.Links.Add(new GraphLink(ready.Outputs[1], log.Inputs[0]));

        document.Links.Add(new GraphLink(image.Outputs[0], blur.Inputs[0]));
        document.Links.Add(new GraphLink(number.Outputs[0], blur.Inputs[1]));
        document.Links.Add(new GraphLink(blur.Outputs[0], mix.Inputs[0]));
        document.Links.Add(new GraphLink(image.Outputs[0], mix.Inputs[1]));
        document.Links.Add(new GraphLink(mix.Outputs[0], output.Inputs[1]));
        document.Links.Add(new GraphLink(mix.Outputs[0], log.Inputs[1]));

        return document;
    }

    /// <summary>
    /// Граф на <paramref name="count"/> узлов — проверка виртуализации (ADR 0007 библиотеки).
    /// </summary>
    /// <remarks>
    /// Узлы стоят квадратной сеткой, каждый с входами «A» (цвет), «B» (число) и выходом-цветом. Цепочка
    /// идёт по ряду во входы «A»; из каждого десятого столбца, начиная со второго, дальняя связь уходит
    /// на десять рядов вниз во вход «B» — её второй конец у узла, который ни разу не показывался, и
    /// редактор ставит его оценкой по краю, пока узел не покажут. Цвет провода — по типу входа, поэтому
    /// дальние связи видны другим цветом. Документ наполняется, пока его никто не слушает: окно отдаёт
    /// редактору готовые коллекции, а не десять тысяч добавлений.
    /// </remarks>
    public static GraphDocument CreateGrid(int count)
    {
        var document = new GraphDocument();
        var nodes = document.Nodes;
        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));
        for (var i = 0; i < count; i++)
        {
            var (row, column) = Math.DivRem(i, columns);
            nodes.Add(new GraphNode($"Узел {i}", new Point(column * GridStep.X, row * GridStep.Y), [Color("A"), Number("B")], [Color("Выход")])
            {
                Accent = NodeKinds.All[(row + column) % NodeKinds.All.Length]
            });
        }

        for (var i = 0; i < count; i++)
        {
            var column = i % columns;
            if (column + 1 < columns && i + 1 < count)
                document.Links.Add(new GraphLink(nodes[i].Outputs[0], nodes[i + 1].Inputs[0]));

            var far = i + (10 * columns) + 1;
            if (column % 10 == 2 && column + 1 < columns && far < count)
                document.Links.Add(new GraphLink(nodes[i].Outputs[0], nodes[far].Inputs[1]));
        }

        return document;
    }

    private GraphNode Add(string title, Point location, GraphPortSpec[] inputs, GraphPortSpec[] outputs, Color accent)
    {
        var node = new GraphNode(title, location, inputs, outputs) { Accent = accent };
        Nodes.Add(node);
        return node;
    }

    private static GraphPortSpec Color(string name) => new(name, PortKind.Color);

    private static GraphPortSpec Number(string name) => new(name, PortKind.Number);

    private static GraphPortSpec Execution() => new(string.Empty, PortKind.Execution);

    private static GraphPortSpec Delegate(string name) => new(name, PortKind.Delegate);
}

/// <summary>
/// Цвета видов узла — данные графа, как категории узлов в Blueprint: ими окрашена полоса заголовка
/// узла и его упрощённой карточки (ADR 0009 библиотеки).
/// </summary>
/// <remarks>
/// Белое название на каждом из них читается с контрастом не ниже 4,5:1; на светлую полосу библиотека
/// поставила бы тёмный текст сама.
/// </remarks>
public static class NodeKinds
{
    public static readonly Color Source = Color.Parse("#3B6FB6");

    public static readonly Color Filter = Color.Parse("#4D7A2A");

    public static readonly Color Blend = Color.Parse("#B2560D");

    public static readonly Color Output = Color.Parse("#A33A36");

    public static readonly Color Utility = Color.Parse("#6A4C93");

    /// <summary>
    /// Событие — узел, с которого начинается выполнение, как красные события Blueprint.
    /// </summary>
    public static readonly Color Event = Color.Parse("#8E2A2A");

    public static readonly Color[] All = [Source, Filter, Blend, Output, Utility];
}

/// <summary>
/// Тип значения порта: по нему красится провод, как пины и провода по типу данных в Blueprint.
/// </summary>
public enum PortKind
{
    /// <summary>Любое значение — у узла перенаправления; провод берёт цвет другого конца.</summary>
    Any,

    /// <summary>Цвет.</summary>
    Color,

    /// <summary>Число.</summary>
    Number,

    /// <summary>
    /// Выполнение: не значение, а порядок — «когда», а не «что», как Exec-пины Blueprint.
    /// </summary>
    Execution,

    /// <summary>
    /// Делегат: ссылка на событие, переданная как значение, чтобы вызвать его позже, — как красные пины
    /// Event Dispatcher в Blueprint.
    /// </summary>
    Delegate
}

/// <summary>
/// Цвета типов значения — данные графа, как и цвета видов узла.
/// </summary>
/// <remarks>
/// Подобраны под тёмный холст демо: 7,7:1 и 6,4:1. На светлом они слабее, и хосту с обеими темами цвет
/// провода стоит брать по теме.
/// </remarks>
public static class PortKinds
{
    public static readonly Color ColorWire = Color.Parse("#D4A017");

    public static readonly Color NumberWire = Color.Parse("#2BA8A8");

    /// <summary>
    /// Маркер провода цвета — стрелка посередине. Кисти нет: маркер берёт цвет провода.
    /// </summary>
    public static readonly LinkMarker ColorMarker = new() { Shape = LinkShape.Arrow };

    /// <summary>
    /// Маркер провода числа — шевроны через 90 единиц: числовой провод отличим и без цвета.
    /// </summary>
    public static readonly LinkMarker NumberMarker = new() { Shape = LinkShape.Chevron, Spacing = 90, Size = 12 };

    public static Color? ColorOf(PortKind kind) => kind switch
    {
        PortKind.Color => ColorWire,
        PortKind.Number => NumberWire,
        _ => null
    };

    /// <summary>
    /// Роль пина и провода (ADR 0017 библиотеки): вид выполнения и делегата даёт тема редактора, цвет
    /// данных — эта модель.
    /// </summary>
    public static PinRole RoleOf(PortKind kind) => kind switch
    {
        PortKind.Execution => PinRole.Execution,
        PortKind.Delegate => PinRole.Delegate,
        _ => PinRole.Data
    };

    public static LinkMarker? MarkerOf(PortKind kind) => kind switch
    {
        PortKind.Color => ColorMarker,
        PortKind.Number => NumberMarker,
        _ => null
    };
}

/// <summary>
/// Порт, каким его объявляет узел: имя и тип значения.
/// </summary>
public readonly record struct GraphPortSpec(string Name, PortKind Kind);

/// <summary>
/// Узел графа.
/// </summary>
/// <remarks>
/// <see cref="Location"/> — место узла на холсте, и держит его модель: редактор привязан к нему
/// <c>ItemLocationBinding</c> в обе стороны. Перетаскивание и отмена пишут сюда, правка отсюда двигает
/// узел, а у свёрнутого узла — вне окна, без контейнера — панель берёт положение отсюда же и узнаёт о
/// правке по <see cref="PropertyChanged"/>. Поэтому удалённый узел помнит, где стоял, и отмена удаления
/// возвращает его на место без помощи окна.
/// </remarks>
public class GraphNode : INotifyPropertyChanged
{
    private Point _location;

    public GraphNode(string title, Point location, IEnumerable<GraphPortSpec> inputs, IEnumerable<GraphPortSpec> outputs)
    {
        Title = title;
        _location = location;
        Inputs = inputs.Select(spec => new GraphPort(this, spec.Name, isInput: true, spec.Kind)).ToList();
        Outputs = outputs.Select(spec => new GraphPort(this, spec.Name, isInput: false, spec.Kind)).ToList();
    }

    public string Title { get; }

    public Point Location
    {
        get => _location;
        set
        {
            if (_location == value)
                return;

            _location = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Location)));
        }
    }

    public IReadOnlyList<GraphPort> Inputs { get; }

    public IReadOnlyList<GraphPort> Outputs { get; }

    /// <summary>
    /// Цвет вида узла: полоса под названием узла и под его упрощённой карточкой. У узла перенаправления — нет.
    /// </summary>
    public Color? Accent { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() => Title;
}

/// <summary>
/// Узел перенаправления: излом связи — это узел, а не точка на связи. Шаблон у него — <c>Reroute</c> библиотеки.
/// </summary>
public sealed class RerouteNode(string title, Point location)
    : GraphNode(title, location, [new GraphPortSpec("вход", PortKind.Any)], [new GraphPortSpec("выход", PortKind.Any)]);

/// <summary>
/// Порт узла. Им же связь называет свой конец, поэтому сравнивается он по ссылке: одноимённый порт
/// другого узла — другой порт.
/// </summary>
public sealed class GraphPort(GraphNode node, string name, bool isInput, PortKind kind)
{
    public GraphNode Node { get; } = node;

    public string Name { get; } = name;

    public bool IsInput { get; } = isInput;

    public PortKind Kind { get; } = kind;

    /// <summary>
    /// Роль пина: форму и цвет выполнения и делегата даёт тема редактора.
    /// </summary>
    public PinRole Role => PortKinds.RoleOf(Kind);

    /// <summary>
    /// Цвет штырька данных — цвет его типа; у выполнения, делегата и узла перенаправления значения нет, и
    /// порт берёт вид роли или темы.
    /// </summary>
    public IBrush? Brush => PortKinds.ColorOf(Kind) is { } color ? PortBrushes.Of(color) : null;

    public override string ToString() => $"{Node.Title}.{Name}";
}

/// <summary>
/// Кисть на цвет — одна на цвет, а не новая на каждый штырёк.
/// </summary>
internal static class PortBrushes
{
    private static readonly Dictionary<Color, IBrush> Brushes = new();

    public static IBrush Of(Color color)
    {
        if (!Brushes.TryGetValue(color, out var brush))
            Brushes[color] = brush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(color);

        return brush;
    }
}

/// <summary>
/// Связь графа: из выхода <see cref="From"/> во вход <see cref="To"/>.
/// </summary>
public sealed record GraphLink(GraphPort From, GraphPort To)
{
    /// <summary>
    /// Цвет провода — по типу входа, а у входа узла перенаправления — по типу выхода; редактор берёт его
    /// привязкой <c>LinkStrokeBinding</c> в обоих видах (ADR 0009 библиотеки).
    /// </summary>
    public Color? Color => PortKinds.ColorOf(To.Kind) ?? PortKinds.ColorOf(From.Kind);

    /// <summary>
    /// Маркер — по тому же типу, что и цвет; редактор берёт его привязкой <c>LinkMarkerBinding</c>
    /// (ADR 0014 библиотеки).
    /// </summary>
    public LinkMarker? Marker => PortKinds.MarkerOf(To.Kind is PortKind.Any ? From.Kind : To.Kind);

    /// <summary>
    /// Роль провода — по типу, как цвет: цвет и толщину выполнения и делегата даёт тема редактора
    /// привязкой <c>LinkRoleBinding</c>.
    /// </summary>
    public PinRole Role => PortKinds.RoleOf(To.Kind is PortKind.Any ? From.Kind : To.Kind);

    public override string ToString() => $"{From} → {To}";
}
