using Avalonia.Media;

namespace Nodes.Calculator.Graph;

/// <summary>
/// Вид узла, как в Blueprint: от него — полоса заголовка и тело.
/// </summary>
public enum NodeLook
{
    /// <summary>Событие — красная полоса, с него начинается выполнение.</summary>
    Event,

    /// <summary>Вызов функции с пинами выполнения — синяя полоса.</summary>
    Function,

    /// <summary>Чистая функция — зелёная полоса, пинов выполнения нет.</summary>
    Pure,

    /// <summary>Управление потоком — серая полоса: ветвление, последовательность, цикл.</summary>
    Flow,

    /// <summary>Компактный узел без заголовка — знак операции посередине, как математика Blueprint.</summary>
    Compact,

    /// <summary>Преобразование типа — маленький компактный узел, который ставится сам.</summary>
    Conversion,

    /// <summary>Чтение переменной — узел-«таблетка» с именем и одним выходом.</summary>
    Getter
}

/// <summary>
/// Порт, каким его объявляет определение: имя, тип и литерал по умолчанию. В имени <c>{0}</c> — имя
/// переменной узла.
/// </summary>
public sealed record PortSpec(string Name, PinType Type, string? Default = null);

/// <summary>
/// Что узел сообщил о своём счёте: у Blueprint деление на ноль даёт ноль и предупреждение на узле.
/// </summary>
public sealed class ComputeContext
{
    public string? Warning { get; private set; }

    public void Warn(string message) => Warning ??= message;
}

/// <summary>
/// Определение узла — строка каталога действий. Узел графа держит ссылку на своё определение: у
/// двух «Сложить» одно определение и разные порты.
/// </summary>
public sealed class NodeDefinition
{
    public required string Id { get; init; }

    /// <summary>
    /// Название в меню действий и на полосе; <c>{0}</c> — имя переменной узла.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Раздел меню действий, уровни через <c>|</c>: «Математика|Число».
    /// </summary>
    public required string Category { get; init; }

    public required NodeLook Look { get; init; }

    /// <summary>
    /// Знак компактного узла: «+», «×», «•».
    /// </summary>
    public string? Symbol { get; init; }

    /// <summary>
    /// Слова поиска сверх названия — как ключевые слова узлов Blueprint: «+» находит «Сложить».
    /// </summary>
    public string Keywords { get; init; } = string.Empty;

    public PortSpec[] Inputs { get; init; } = [];

    public PortSpec[] Outputs { get; init; } = [];

    /// <summary>
    /// Счёт чистого узла: значения входов, приведённые к их типам, — значение единственного выхода.
    /// </summary>
    public Func<object[], ComputeContext, object>? Compute { get; init; }

    /// <summary>
    /// Пин, который добавляет «Добавить пин ⊕»; <c>{0}</c> в имени — его номер. Без него кнопки нет.
    /// </summary>
    public PortSpec? ExtraPin { get; init; }

    /// <summary>
    /// Добавляется выход, а не вход: у последовательности растут «Затем N».
    /// </summary>
    public bool ExtraPinIsOutput { get; init; }

    /// <summary>
    /// Узел переменной: без неё его не создают.
    /// </summary>
    public bool NeedsVariable { get; init; }

    /// <summary>
    /// Цвет полосы заголовка по виду — цвета полос Blueprint; белое название на каждом читается с
    /// контрастом выше 4,5:1. У компактных узлов и узлов переменных полосы нет.
    /// </summary>
    public Color? Accent => Look switch
    {
        NodeLook.Event => EventAccent,
        NodeLook.Function => FunctionAccent,
        NodeLook.Pure => PureAccent,
        NodeLook.Flow => FlowAccent,
        _ => null
    };

    public static readonly Color EventAccent = Color.Parse("#8E2A2A");

    public static readonly Color FunctionAccent = Color.Parse("#2B5C8A");

    public static readonly Color PureAccent = Color.Parse("#3B6A2C");

    public static readonly Color FlowAccent = Color.Parse("#555B66");

    public override string ToString() => Title;
}
