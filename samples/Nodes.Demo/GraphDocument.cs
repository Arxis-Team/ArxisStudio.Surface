using System.Collections.ObjectModel;
using System.ComponentModel;
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
    /// Можно ли соединить эти порты по правилам приложения.
    /// </summary>
    /// <param name="source">Выход.</param>
    /// <param name="target">Вход.</param>
    /// <param name="moving">Связь, чей конец перецепляют, или <see langword="null"/> для новой.</param>
    /// <remarks>
    /// Направления проверяет сам редактор; здесь — правила графа: в свой же узел нельзя, и во вход
    /// приходит одна связь — не считая той, что перецепляет свой конец: её вход занят ею же.
    /// </remarks>
    public bool CanConnect(GraphPort source, GraphPort target, GraphLink? moving) =>
        !ReferenceEquals(source.Node, target.Node)
        && !Links.Any(link => !ReferenceEquals(link, moving) && ReferenceEquals(link.To, target));

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
        var output = document.Add("Вывод", new Point(960, 190), inputs: [Color("Цвет")], outputs: [], NodeKinds.Output);

        document.Links.Add(new GraphLink(image.Outputs[0], blur.Inputs[0]));
        document.Links.Add(new GraphLink(number.Outputs[0], blur.Inputs[1]));
        document.Links.Add(new GraphLink(blur.Outputs[0], mix.Inputs[0]));
        document.Links.Add(new GraphLink(image.Outputs[0], mix.Inputs[1]));
        document.Links.Add(new GraphLink(mix.Outputs[0], output.Inputs[0]));

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
    Number
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

    public static Color? ColorOf(PortKind kind) => kind switch
    {
        PortKind.Color => ColorWire,
        PortKind.Number => NumberWire,
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

    public override string ToString() => $"{Node.Title}.{Name}";
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

    public override string ToString() => $"{From} → {To}";
}
