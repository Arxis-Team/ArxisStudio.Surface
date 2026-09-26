using System.Collections.ObjectModel;
using Avalonia;

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
    /// Новая перевалка — узел с одним входом и одним выходом (ADR 0005 библиотеки).
    /// </summary>
    public RerouteNode CreateReroute(Point location) => new($"Перевалка {++_reroutes}", location);

    public static GraphDocument CreateSample()
    {
        var document = new GraphDocument();

        var image = document.Add("Изображение", new Point(60, 80), inputs: [], outputs: ["Цвет", "Альфа"]);
        var number = document.Add("Число", new Point(60, 300), inputs: [], outputs: ["Значение"]);
        var blur = document.Add("Размытие", new Point(360, 60), inputs: ["Цвет", "Радиус"], outputs: ["Цвет"]);
        var mix = document.Add("Смешивание", new Point(660, 150), inputs: ["A", "B", "Доля"], outputs: ["Цвет"]);
        var output = document.Add("Вывод", new Point(960, 190), inputs: ["Цвет"], outputs: []);

        document.Links.Add(new GraphLink(image.Outputs[0], blur.Inputs[0]));
        document.Links.Add(new GraphLink(number.Outputs[0], blur.Inputs[1]));
        document.Links.Add(new GraphLink(blur.Outputs[0], mix.Inputs[0]));
        document.Links.Add(new GraphLink(image.Outputs[0], mix.Inputs[1]));
        document.Links.Add(new GraphLink(mix.Outputs[0], output.Inputs[0]));

        return document;
    }

    private GraphNode Add(string title, Point location, string[] inputs, string[] outputs)
    {
        var node = new GraphNode(title, location, inputs, outputs);
        Nodes.Add(node);
        return node;
    }
}

/// <summary>
/// Узел графа.
/// </summary>
/// <remarks>
/// <see cref="Location"/> — то место, где узел поставить, когда его контейнер появится. Во время
/// жизни контейнера положением распоряжается редактор, и приложение забирает его обратно, когда
/// узел уходит (<c>MainWindow.RemoveNodes</c>): тогда отмена удаления вернёт узел туда, где он стоял.
/// </remarks>
public class GraphNode
{
    public GraphNode(string title, Point location, IEnumerable<string> inputs, IEnumerable<string> outputs)
    {
        Title = title;
        Location = location;
        Inputs = inputs.Select(name => new GraphPort(this, name, isInput: true)).ToList();
        Outputs = outputs.Select(name => new GraphPort(this, name, isInput: false)).ToList();
    }

    public string Title { get; }

    public Point Location { get; set; }

    public IReadOnlyList<GraphPort> Inputs { get; }

    public IReadOnlyList<GraphPort> Outputs { get; }

    public override string ToString() => Title;
}

/// <summary>
/// Перевалка: излом связи — это узел, а не точка на связи. Шаблон у неё — <c>Reroute</c> библиотеки.
/// </summary>
public sealed class RerouteNode(string title, Point location) : GraphNode(title, location, ["вход"], ["выход"]);

/// <summary>
/// Порт узла. Им же связь называет свой конец, поэтому сравнивается он по ссылке: одноимённый порт
/// другого узла — другой порт.
/// </summary>
public sealed class GraphPort(GraphNode node, string name, bool isInput)
{
    public GraphNode Node { get; } = node;

    public string Name { get; } = name;

    public bool IsInput { get; } = isInput;

    public override string ToString() => $"{Node.Title}.{Name}";
}

/// <summary>
/// Связь графа: из выхода <see cref="From"/> во вход <see cref="To"/>.
/// </summary>
public sealed record GraphLink(GraphPort From, GraphPort To)
{
    public override string ToString() => $"{From} → {To}";
}
