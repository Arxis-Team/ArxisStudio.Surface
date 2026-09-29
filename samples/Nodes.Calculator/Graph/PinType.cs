using System.Globalization;
using ArxisStudio.Surface.Nodes;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Nodes.Calculator.Graph;

/// <summary>
/// Тип пина, как в Blueprint: выполнение задаёт порядок, остальные несут значение.
/// </summary>
public enum PinType
{
    /// <summary>Выполнение — «когда», а не «что».</summary>
    Exec,

    /// <summary>Число с плавающей точкой (Float).</summary>
    Float,

    /// <summary>Целое (Integer).</summary>
    Integer,

    /// <summary>Логическое (Boolean).</summary>
    Boolean,

    /// <summary>Строка (String).</summary>
    String,

    /// <summary>Любой — у узла перенаправления, пока к нему ничего не подключено.</summary>
    Wildcard
}

/// <summary>
/// Цвета типов — данные графа, а не тема редактора: у Blueprint цвет пина и провода называет тип.
/// </summary>
/// <remarks>
/// Оттенки — с пинов Unreal Engine 5, поднятые для тёмного холста библиотеки (<c>#1E1E1E</c>): число —
/// салатовый, целое — бирюзовый, логическое — тёмно-красный, строка — пурпурный. Вид выполнения даёт роль
/// пина (ADR 0017 библиотеки), своей кисти у него нет.
/// </remarks>
public static class PinTypes
{
    public static readonly Color FloatColor = Color.Parse("#9CE63A");

    public static readonly Color IntegerColor = Color.Parse("#1FD8A6");

    public static readonly Color BooleanColor = Color.Parse("#C8272B");

    public static readonly Color StringColor = Color.Parse("#EE3FC8");

    private static readonly Dictionary<Color, IBrush> Brushes = new();

    public static Color? ColorOf(PinType type) => type switch
    {
        PinType.Float => FloatColor,
        PinType.Integer => IntegerColor,
        PinType.Boolean => BooleanColor,
        PinType.String => StringColor,
        _ => null
    };

    public static IBrush? BrushOf(PinType type)
    {
        if (ColorOf(type) is not { } color)
            return null;

        if (!Brushes.TryGetValue(color, out var brush))
            Brushes[color] = brush = new ImmutableSolidColorBrush(color);

        return brush;
    }

    public static PinRole RoleOf(PinType type) => type == PinType.Exec ? PinRole.Execution : PinRole.Data;

    public static string NameOf(PinType type) => type switch
    {
        PinType.Exec => "выполнение",
        PinType.Float => "число",
        PinType.Integer => "целое",
        PinType.Boolean => "логическое",
        PinType.String => "строка",
        _ => "любой"
    };
}

/// <summary>
/// Значения графа: <see cref="double"/>, <see cref="int"/>, <see cref="bool"/> и <see cref="string"/> — по
/// типу пина. Разбор и показ — по-русски, но запятую и точку разбор принимает обе.
/// </summary>
public static class Values
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    public static object DefaultOf(PinType type) => type switch
    {
        PinType.Float => 0.0,
        PinType.Integer => 0,
        PinType.Boolean => false,
        PinType.String => string.Empty,
        _ => 0.0
    };

    /// <summary>
    /// Текст значения — на выходе узла, в строке и в журнале.
    /// </summary>
    public static string Format(object? value) => value switch
    {
        double d when double.IsNaN(d) => "не число",
        double d when double.IsPositiveInfinity(d) => "∞",
        double d when double.IsNegativeInfinity(d) => "−∞",
        double d => d.ToString("0.#####", Russian),
        int i => i.ToString(Russian),
        bool b => b ? "истина" : "ложь",
        string s => s,
        _ => "—"
    };

    /// <summary>
    /// Значение литерала — текста, набранного у неподключённого входа.
    /// </summary>
    public static object Parse(string? text, PinType type) => type switch
    {
        PinType.Float => ParseDouble(text),
        PinType.Integer => (int)Math.Clamp(Math.Truncate(ParseDouble(text)), int.MinValue, int.MaxValue),
        PinType.Boolean => text is "истина" or "true" or "1",
        PinType.String => text ?? string.Empty,
        _ => ParseDouble(text)
    };

    /// <summary>
    /// Приводит значение к типу входа. Провода разных типов соединяются только через узел
    /// преобразования, поэтому приведение нужно узлу перенаправления: он несёт значение как есть.
    /// </summary>
    public static object Coerce(object? value, PinType type) => type switch
    {
        PinType.Float => value switch
        {
            double d => d,
            int i => i,
            bool b => b ? 1.0 : 0.0,
            string s => ParseDouble(s),
            _ => 0.0
        },
        PinType.Integer => value switch
        {
            int i => i,
            double d => double.IsFinite(d) ? (int)Math.Clamp(Math.Truncate(d), int.MinValue, int.MaxValue) : 0,
            bool b => b ? 1 : 0,
            string s => (int)Math.Clamp(Math.Truncate(ParseDouble(s)), int.MinValue, int.MaxValue),
            _ => 0
        },
        PinType.Boolean => value switch
        {
            bool b => b,
            double d => d != 0,
            int i => i != 0,
            string s => s is "истина" or "true" or "1",
            _ => false
        },
        PinType.String => Format(value),
        _ => value ?? 0.0
    };

    /// <summary>
    /// Читается ли текст как значение типа: нечитаемый литерал считается нулём, а поле показывает ошибку.
    /// </summary>
    public static bool IsValid(string? text, PinType type) =>
        type is not (PinType.Float or PinType.Integer) || string.IsNullOrWhiteSpace(text) || TryParseDouble(text, out _);

    private static double ParseDouble(string? text) =>
        !string.IsNullOrWhiteSpace(text) && TryParseDouble(text, out var value) ? value : 0;

    private static bool TryParseDouble(string text, out double value)
    {
        var normal = text.Trim().Replace(" ", string.Empty).Replace('.', ',').Replace('−', '-');
        return double.TryParse(normal, NumberStyles.Float, Russian, out value);
    }
}
