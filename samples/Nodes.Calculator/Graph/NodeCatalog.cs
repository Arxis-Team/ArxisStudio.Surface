namespace Nodes.Calculator.Graph;

/// <summary>
/// Каталог узлов — всё, что предлагает меню действий. Состав и разделы — как у Blueprint: события,
/// поток выполнения, математика компактными узлами, строки, преобразования, утилиты, переменные.
/// </summary>
/// <remarks>
/// Узлы выполнения — «Начало игры», «Вывести строку», «Ветвление», «Последовательность», «Цикл For»,
/// «Задать» — считает <see cref="Evaluator"/> по <see cref="NodeDefinition.Id"/>; чистые узлы несут свой
/// счёт в <see cref="NodeDefinition.Compute"/>.
/// </remarks>
public static class NodeCatalog
{
    public static readonly NodeDefinition BeginPlay = new()
    {
        Id = "begin-play", Title = "Начало игры", Category = "События", Look = NodeLook.Event,
        Keywords = "begin play event старт запуск событие",
        Outputs = [Exec()]
    };

    public static readonly NodeDefinition Print = new()
    {
        Id = "print", Title = "Вывести строку", Category = "Утилиты", Look = NodeLook.Function,
        Keywords = "print string печать вывод журнал экран",
        Inputs = [Exec(), Str("Строка", "Привет"), Bool("На экран", true), Bool("В журнал", true), Float("Длительность", "2")],
        Outputs = [Exec()]
    };

    public static readonly NodeDefinition Branch = new()
    {
        Id = "branch", Title = "Ветвление", Category = "Поток выполнения", Look = NodeLook.Flow,
        Keywords = "branch if если условие",
        Inputs = [Exec(), Bool("Условие", true)],
        Outputs = [Exec("Истина"), Exec("Ложь")]
    };

    public static readonly NodeDefinition Sequence = new()
    {
        Id = "sequence", Title = "Последовательность", Category = "Поток выполнения", Look = NodeLook.Flow,
        Keywords = "sequence по порядку затем",
        Inputs = [Exec()],
        Outputs = [Exec("Затем 0"), Exec("Затем 1")],
        ExtraPin = Exec("Затем {0}"),
        ExtraPinIsOutput = true
    };

    public static readonly NodeDefinition ForLoop = new()
    {
        Id = "for-loop", Title = "Цикл For", Category = "Поток выполнения", Look = NodeLook.Flow,
        Keywords = "for loop цикл повтор индекс",
        Inputs = [Exec(), Int("Первый индекс", "0"), Int("Последний индекс", "3")],
        Outputs = [Exec("Тело цикла"), new PortSpec("Индекс", PinType.Integer), Exec("Завершено")]
    };

    public static readonly NodeDefinition Get = new()
    {
        Id = "get", Title = "{0}", Category = "Переменные", Look = NodeLook.Getter, NeedsVariable = true,
        Keywords = "get получить переменная",
        Outputs = [new PortSpec("{0}", PinType.Float)]
    };

    public static readonly NodeDefinition Set = new()
    {
        Id = "set", Title = "Задать {0}", Category = "Переменные", Look = NodeLook.Function, NeedsVariable = true,
        Keywords = "set задать присвоить переменная",
        Inputs = [Exec(), Float("{0}", "0")],
        Outputs = [Exec(), new PortSpec(string.Empty, PinType.Float)]
    };

    public static readonly NodeDefinition Add = Math("add", "Сложить", "+", "add plus сумма плюс", xs => xs.Sum(), extra: true);

    public static readonly NodeDefinition Subtract = Math("subtract", "Вычесть", "−", "subtract minus - разность минус", xs => xs[0] - xs[1]);

    public static readonly NodeDefinition Multiply = Math("multiply", "Умножить", "×", "multiply * произведение умножить", xs => xs.Aggregate(1.0, (a, b) => a * b), extra: true, one: true);

    public static readonly NodeDefinition Divide = new()
    {
        Id = "divide", Title = "Разделить", Symbol = "÷", Category = "Математика|Число", Look = NodeLook.Compact,
        Keywords = "divide / деление частное разделить",
        Inputs = [Float(string.Empty, "0"), Float(string.Empty, "1")],
        Outputs = [new PortSpec(string.Empty, PinType.Float)],
        Compute = (xs, context) =>
        {
            var (a, b) = ((double)xs[0], (double)xs[1]);
            if (b != 0)
                return a / b;

            // Как у Blueprint: не бесконечность, а ноль и предупреждение.
            context.Warn("Деление на ноль");
            return 0.0;
        }
    };

    public static readonly NodeDefinition Remainder = Math("remainder", "Остаток", "%", "modulo % остаток деления", xs => xs[1] == 0 ? 0 : xs[0] % xs[1]);

    public static readonly NodeDefinition Power = Pure("power", "Степень", "Математика|Число", "power pow степень возвести",
        [Float("Основание", "2"), Float("Показатель", "2")], PinType.Float, (xs, _) => System.Math.Pow((double)xs[0], (double)xs[1]));

    public static readonly NodeDefinition SquareRoot = Pure("sqrt", "Квадратный корень", "Математика|Число", "sqrt корень квадратный",
        [Float("Значение", "0")], PinType.Float, (xs, context) =>
        {
            var x = (double)xs[0];
            if (x >= 0)
                return System.Math.Sqrt(x);

            context.Warn("Корень из отрицательного");
            return 0.0;
        });

    public static readonly NodeDefinition Absolute = Pure("abs", "Модуль", "Математика|Число", "abs absolute модуль абсолютное",
        [Float("Значение", "0")], PinType.Float, (xs, _) => System.Math.Abs((double)xs[0]));

    public static readonly NodeDefinition Min = Pure("min", "Мин", "Математика|Число", "min минимум меньшее",
        [Float("A", "0"), Float("B", "0")], PinType.Float, (xs, _) => System.Math.Min((double)xs[0], (double)xs[1]));

    public static readonly NodeDefinition Max = Pure("max", "Макс", "Математика|Число", "max максимум большее",
        [Float("A", "0"), Float("B", "0")], PinType.Float, (xs, _) => System.Math.Max((double)xs[0], (double)xs[1]));

    public static readonly NodeDefinition Round = Pure("round", "Округлить", "Математика|Число", "round округлить целое",
        [Float("Значение", "0")], PinType.Integer, (xs, _) => (int)System.Math.Clamp(System.Math.Round((double)xs[0], MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue));

    public static readonly NodeDefinition Greater = Compare("greater", "Больше", ">", "greater > больше", (a, b) => a > b);

    public static readonly NodeDefinition Less = Compare("less", "Меньше", "<", "less < меньше", (a, b) => a < b);

    public static readonly NodeDefinition GreaterOrEqual = Compare("greater-equal", "Больше или равно", "≥", "greater equal >= больше или равно", (a, b) => a >= b);

    public static readonly NodeDefinition LessOrEqual = Compare("less-equal", "Меньше или равно", "≤", "less equal <= меньше или равно", (a, b) => a <= b);

    public static readonly NodeDefinition Equal = Compare("equal", "Равно", "==", "equal == равно", (a, b) => a == b);

    public static readonly NodeDefinition And = Logic("and", "И", "and && и", [Bool(string.Empty, false), Bool(string.Empty, false)], xs => xs.All(x => x), extra: true);

    public static readonly NodeDefinition Or = Logic("or", "ИЛИ", "or || или", [Bool(string.Empty, false), Bool(string.Empty, false)], xs => xs.Any(x => x), extra: true);

    public static readonly NodeDefinition Not = Logic("not", "НЕ", "not ! не отрицание", [Bool(string.Empty, false)], xs => !xs[0]);

    public static readonly NodeDefinition Select = Pure("select", "Выбрать", "Математика|Логика", "select выбрать если",
        [Bool("Выбрать A", true), Float("A", "0"), Float("B", "0")], PinType.Float, (xs, _) => (bool)xs[0] ? xs[1] : xs[2]);

    public static readonly NodeDefinition Append = new()
    {
        Id = "append", Title = "Соединить", Category = "Строки", Look = NodeLook.Pure,
        Keywords = "append concat string соединить склеить строка",
        Inputs = [Str("A", string.Empty), Str("B", string.Empty)],
        Outputs = [new PortSpec("Итог", PinType.String)],
        Compute = (xs, _) => string.Concat(xs.Cast<string>()),
        ExtraPin = Str("{0}", string.Empty)
    };

    public static readonly NodeDefinition IntegerToFloat = Conversion("int-to-float", PinType.Integer, PinType.Float, (x, _) => (double)(int)x);

    public static readonly NodeDefinition FloatToInteger = Conversion("float-to-int", PinType.Float, PinType.Integer, (x, _) => Values.Coerce(x, PinType.Integer));

    public static readonly NodeDefinition FloatToString = Conversion("float-to-string", PinType.Float, PinType.String, (x, _) => Values.Format(x));

    public static readonly NodeDefinition IntegerToString = Conversion("int-to-string", PinType.Integer, PinType.String, (x, _) => Values.Format(x));

    public static readonly NodeDefinition BooleanToString = Conversion("bool-to-string", PinType.Boolean, PinType.String, (x, _) => Values.Format(x));

    /// <summary>
    /// Узел перенаправления: у него свой шаблон — <c>Reroute</c> библиотеки, — и в меню он не показывается.
    /// </summary>
    public static readonly NodeDefinition Reroute = new()
    {
        Id = "reroute", Title = "Узел перенаправления", Category = string.Empty, Look = NodeLook.Compact,
        Inputs = [new PortSpec("вход", PinType.Wildcard)],
        Outputs = [new PortSpec("выход", PinType.Wildcard)]
    };

    /// <summary>
    /// Всё, что меню действий предлагает без переменной, в порядке показа.
    /// </summary>
    public static readonly NodeDefinition[] All =
    [
        BeginPlay,
        Branch, Sequence, ForLoop,
        Add, Subtract, Multiply, Divide, Remainder, Power, SquareRoot, Absolute, Min, Max, Round,
        Greater, Less, GreaterOrEqual, LessOrEqual, Equal,
        And, Or, Not, Select,
        Append,
        IntegerToFloat, FloatToInteger, FloatToString, IntegerToString, BooleanToString,
        Print
    ];

    /// <summary>
    /// Узлы переменной — «Получить» и «Задать».
    /// </summary>
    public static readonly NodeDefinition[] ForVariables = [Get, Set];

    private static readonly NodeDefinition[] Conversions = [IntegerToFloat, FloatToInteger, FloatToString, IntegerToString, BooleanToString];

    /// <summary>
    /// Узел преобразования между типами — его редактор ставит сам, когда провод соединяет разные типы,
    /// как Blueprint при соединении целого с числом.
    /// </summary>
    public static NodeDefinition? ConversionOf(PinType from, PinType to) =>
        Conversions.FirstOrDefault(c => c.Inputs[0].Type == from && c.Outputs[0].Type == to);

    private static PortSpec Exec(string name = "") => new(name, PinType.Exec);

    private static PortSpec Float(string name, string value) => new(name, PinType.Float, value);

    private static PortSpec Int(string name, string value) => new(name, PinType.Integer, value);

    private static PortSpec Bool(string name, bool value) => new(name, PinType.Boolean, value ? "истина" : "ложь");

    private static PortSpec Str(string name, string value) => new(name, PinType.String, value);

    private static NodeDefinition Math(string id, string title, string symbol, string keywords, Func<double[], double> compute, bool extra = false, bool one = false) => new()
    {
        Id = id, Title = title, Symbol = symbol, Category = "Математика|Число", Look = NodeLook.Compact,
        Keywords = keywords,
        Inputs = [Float(string.Empty, one ? "1" : "0"), Float(string.Empty, one ? "1" : "0")],
        Outputs = [new PortSpec(string.Empty, PinType.Float)],
        Compute = (xs, _) => compute(xs.Cast<double>().ToArray()),
        ExtraPin = extra ? Float(string.Empty, one ? "1" : "0") : null
    };

    private static NodeDefinition Compare(string id, string title, string symbol, string keywords, Func<double, double, bool> compare) => new()
    {
        Id = id, Title = title, Symbol = symbol, Category = "Математика|Сравнение", Look = NodeLook.Compact,
        Keywords = keywords,
        Inputs = [Float(string.Empty, "0"), Float(string.Empty, "0")],
        Outputs = [new PortSpec(string.Empty, PinType.Boolean)],
        Compute = (xs, _) => compare((double)xs[0], (double)xs[1])
    };

    private static NodeDefinition Logic(string id, string symbol, string keywords, PortSpec[] inputs, Func<bool[], bool> compute, bool extra = false) => new()
    {
        Id = id, Title = symbol, Symbol = symbol, Category = "Математика|Логика", Look = NodeLook.Compact,
        Keywords = keywords,
        Inputs = inputs,
        Outputs = [new PortSpec(string.Empty, PinType.Boolean)],
        Compute = (xs, _) => compute(xs.Cast<bool>().ToArray()),
        ExtraPin = extra ? Bool(string.Empty, false) : null
    };

    private static NodeDefinition Pure(string id, string title, string category, string keywords, PortSpec[] inputs, PinType output, Func<object[], ComputeContext, object> compute) => new()
    {
        Id = id, Title = title, Category = category, Look = NodeLook.Pure,
        Keywords = keywords,
        Inputs = inputs,
        Outputs = [new PortSpec("Итог", output)],
        Compute = compute
    };

    private static NodeDefinition Conversion(string id, PinType from, PinType to, Func<object, ComputeContext, object> convert) => new()
    {
        Id = id, Title = $"{Capital(PinTypes.NameOf(from))} в {Accusative(to)}", Symbol = "•", Category = "Преобразования",
        Look = NodeLook.Conversion,
        Keywords = $"to {to} convert преобразовать {PinTypes.NameOf(from)} {PinTypes.NameOf(to)}",
        Inputs = [new PortSpec(string.Empty, from, Values.Format(Values.DefaultOf(from)))],
        Outputs = [new PortSpec(string.Empty, to)],
        Compute = (xs, context) => convert(xs[0], context)
    };

    private static string Capital(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    private static string Accusative(PinType type) => type switch
    {
        PinType.Float => "число",
        PinType.Integer => "целое",
        PinType.String => "строку",
        _ => PinTypes.NameOf(type)
    };
}
