using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ArxisStudio.Surface.Nodes;
using Avalonia;
using Avalonia.Media;

namespace Nodes.Calculator.Graph;

/// <summary>
/// Основа моделей графа: уведомление о смене свойства.
/// </summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Переменная Blueprint — «Мой Blueprint ▸ Переменные». Значение по умолчанию — то, с которым её видит
/// живой счёт и с которого начинает «Играть».
/// </summary>
/// <remarks>
/// Переменные примера — числа: тип переменной меняет тип пинов у всех её узлов, а это правка графа, которой
/// образцу не нужно, чтобы показать узлы «Получить» и «Задать».
/// </remarks>
public sealed class CalcVariable(string name, string value) : Observable
{
    private string _name = name;
    private string _value = value;

    public PinType Type => PinType.Float;

    public IBrush? Brush => PinTypes.BrushOf(Type);

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    /// <summary>
    /// Значение по умолчанию текстом, как его набрали.
    /// </summary>
    public string Value
    {
        get => _value;
        set
        {
            if (Set(ref _value, value))
                Raise(nameof(HasError));
        }
    }

    public bool HasError => !Values.IsValid(_value, Type);

    public object DefaultValue => Values.Parse(_value, Type);

    public override string ToString() => Name;
}

/// <summary>
/// Узел графа: определение из каталога, место на холсте и порты.
/// </summary>
/// <remarks>
/// Порты — наблюдаемые коллекции: «Добавить пин ⊕» дописывает вход или выход, и отмена его снимает.
/// Название и имена портов узла переменной берутся из её имени и следуют за переименованием.
/// </remarks>
public class CalcNode : Observable
{
    private Point _location;
    private string? _warning;

    public CalcNode(NodeDefinition definition, Point location, CalcVariable? variable = null)
    {
        if (definition.NeedsVariable && variable == null)
            throw new ArgumentException($"Узлу «{definition.Title}» нужна переменная.", nameof(variable));

        Definition = definition;
        Variable = variable;
        _location = location;
        Inputs = new ObservableCollection<CalcPort>(definition.Inputs.Select(spec => new CalcPort(this, spec, isInput: true)));
        Outputs = new ObservableCollection<CalcPort>(definition.Outputs.Select(spec => new CalcPort(this, spec, isInput: false)));

        if (variable != null)
        {
            variable.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(CalcVariable.Name))
                    return;

                Raise(nameof(Title));
                Raise(nameof(Header));
                foreach (var port in Inputs.Concat(Outputs))
                    port.Rename();
            };
        }
    }

    public NodeDefinition Definition { get; }

    public CalcVariable? Variable { get; }

    public Point Location
    {
        get => _location;
        set => Set(ref _location, value);
    }

    public string Title => Format(Definition.Title);

    /// <summary>
    /// Заголовок на полосе: у функции — «ƒ», у события — «◆», как значки полос Blueprint. У компактных
    /// узлов и узлов переменных заголовка нет, и библиотека не рисует его области.
    /// </summary>
    public string? Header => Definition.Look switch
    {
        NodeLook.Event => $"◆  {Title}",
        NodeLook.Function or NodeLook.Pure => $"ƒ  {Title}",
        NodeLook.Flow => Title,
        _ => null
    };

    public Color? Accent => Definition.Accent;

    public string? Symbol => Definition.Symbol;

    public bool IsCompact => Definition.Look is NodeLook.Compact or NodeLook.Conversion;

    public bool IsConversion => Definition.Look == NodeLook.Conversion;

    public bool CanAddPin => Definition.ExtraPin != null;

    public ObservableCollection<CalcPort> Inputs { get; }

    public ObservableCollection<CalcPort> Outputs { get; }

    public IEnumerable<CalcPort> Ports => Inputs.Concat(Outputs);

    /// <summary>
    /// Предупреждение счёта — пузырь над телом, как «Warning!» узла Blueprint.
    /// </summary>
    public string? Warning
    {
        get => _warning;
        set
        {
            if (Set(ref _warning, value))
                Raise(nameof(HasWarning));
        }
    }

    public bool HasWarning => _warning != null;

    /// <summary>
    /// Новый пин по определению: у «Сложить» — ещё одно слагаемое, у последовательности — «Затем N».
    /// </summary>
    public CalcPort CreateExtraPin()
    {
        var spec = Definition.ExtraPin ?? throw new InvalidOperationException($"У узла «{Title}» нет добавляемых пинов.");
        var list = Definition.ExtraPinIsOutput ? Outputs : Inputs;
        var number = list.Count(port => port.Type == spec.Type);
        return new CalcPort(this, spec with { Name = string.Format(spec.Name, number) }, isInput: !Definition.ExtraPinIsOutput);
    }

    internal string Format(string text) => Variable != null ? text.Replace("{0}", Variable.Name) : text;

    public override string ToString() => Title;
}

/// <summary>
/// Узел перенаправления — излом провода. Тип его пинов — тип провода, который через него идёт
/// (<see cref="CalcDocument.Retype"/>).
/// </summary>
public sealed class RerouteNode(Point location) : CalcNode(NodeCatalog.Reroute, location);

/// <summary>
/// Порт узла. Им же связь называет свой конец, поэтому он сравнивается по ссылке.
/// </summary>
/// <remarks>
/// У неподключённого входа данных — литерал, как поле значения у пина Blueprint: текст у числа, целого
/// и строки, флажок у логического. Поле прячет стиль окна у подключённого порта (<c>:connected</c>
/// библиотеки). У выхода данных — <see cref="Display"/>, значение живого счёта.
/// </remarks>
public sealed class CalcPort : Observable
{
    private readonly PortSpec _spec;
    private PinType _type;
    private string _text;
    private string? _display;

    public CalcPort(CalcNode node, PortSpec spec, bool isInput)
    {
        Node = node;
        _spec = spec;
        IsInput = isInput;
        _type = spec.Type;
        _text = spec.Default ?? Values.Format(Values.DefaultOf(spec.Type));
    }

    public CalcNode Node { get; }

    public bool IsInput { get; }

    public string Name => Node.Format(_spec.Name);

    public bool HasName => Name.Length > 0;

    /// <summary>
    /// Тип пина; меняется только у узла перенаправления — вслед за проводом.
    /// </summary>
    public PinType Type
    {
        get => _type;
        internal set
        {
            if (!Set(ref _type, value))
                return;

            Raise(nameof(Role));
            Raise(nameof(Brush));
        }
    }

    /// <summary>
    /// Роль пина: пятиугольник выполнения даёт тема редактора (ADR 0017 библиотеки).
    /// </summary>
    public PinRole Role => PinTypes.RoleOf(_type);

    /// <summary>
    /// Цвет штырька — цвет типа; у выполнения своей кисти нет, её даёт роль.
    /// </summary>
    public IBrush? Brush => PinTypes.BrushOf(_type);

    public bool HasLiteral => IsInput && _type is PinType.Float or PinType.Integer or PinType.String or PinType.Boolean && Node is not RerouteNode;

    public bool HasTextLiteral => HasLiteral && _type != PinType.Boolean;

    public bool HasFlagLiteral => HasLiteral && _type == PinType.Boolean;

    /// <summary>
    /// Литерал текстом — у числа, целого и строки.
    /// </summary>
    public string Text
    {
        get => _text;
        set
        {
            if (!Set(ref _text, value))
                return;

            Raise(nameof(Flag));
            Raise(nameof(HasError));
        }
    }

    /// <summary>
    /// Литерал логического входа — флажок; хранится тем же текстом.
    /// </summary>
    public bool Flag
    {
        get => (bool)Values.Parse(_text, PinType.Boolean);
        set => Text = Values.Format(value);
    }

    public bool HasError => HasTextLiteral && !Values.IsValid(_text, _type);

    public object Literal => Values.Parse(_text, _type);

    /// <summary>
    /// Значение выхода по живому счёту; у выполнения и входов — <see langword="null"/>.
    /// </summary>
    public string? Display
    {
        get => _display;
        set
        {
            if (Set(ref _display, value))
            {
                Raise(nameof(HasDisplay));
                Raise(nameof(ShowsValue));
            }
        }
    }

    public bool HasDisplay => _display != null;

    /// <summary>
    /// Показывать ли пузырь значения. У узла преобразования его нет, как у Blueprint: то же значение видно
    /// на следующем узле, а место под него вдвое раздувало бы маленький «•».
    /// </summary>
    public bool ShowsValue => HasDisplay && !Node.IsConversion;

    /// <summary>
    /// Значение выхода — текст: его место шире числового.
    /// </summary>
    public bool IsTextValue => _type == PinType.String;

    /// <summary>
    /// Значение выхода — «истина» или «ложь».
    /// </summary>
    public bool IsFlagValue => _type == PinType.Boolean;

    internal void Rename()
    {
        Raise(nameof(Name));
        Raise(nameof(HasName));
    }

    public override string ToString() => $"{Node.Title}.{(Name.Length > 0 ? Name : Type.ToString())}";
}

/// <summary>
/// Провод: из выхода <see cref="From"/> во вход <see cref="To"/>. Цвет — цвет типа, роль — выполнение
/// или данные; редактор читает их привязками <c>LinkStrokeBinding</c> и <c>LinkRoleBinding</c>.
/// </summary>
/// <remarks>
/// Не запись, а класс с уведомлением: тип узла перенаправления выясняется, когда к нему подключают
/// провод, и уже лежащий провод через него перекрашивается (<see cref="Retyped"/>).
/// </remarks>
public sealed class CalcLink(CalcPort from, CalcPort to) : Observable
{
    public CalcPort From { get; } = from;

    public CalcPort To { get; } = to;

    public PinType Type => From.Type != PinType.Wildcard ? From.Type : To.Type;

    public Color? Color => PinTypes.ColorOf(Type);

    public PinRole Role => PinTypes.RoleOf(Type);

    internal void Retyped()
    {
        Raise(nameof(Type));
        Raise(nameof(Color));
        Raise(nameof(Role));
    }

    public override string ToString() => $"{From} → {To}";
}
