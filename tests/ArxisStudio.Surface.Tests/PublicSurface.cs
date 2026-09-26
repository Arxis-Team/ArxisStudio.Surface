using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Tests;

/// <summary>
/// Строит слепок публичной поверхности сборки.
/// </summary>
/// <remarks>
/// Прежняя версия закрепляла список <b>типов</b> и больше ничего. Этого хватало
/// ровно до первого нового публичного <b>члена</b>: `SelectDesignTarget` приехал
/// с девятью дефектами, не уронив ни одного теста, — список типов не изменился.
/// <para>
/// Поэтому слепок идёт до члена и включает значения по умолчанию у
/// <see cref="AvaloniaProperty"/>: смена умолчания — это ломающее изменение,
/// которое не видно ни в одной сигнатуре.
/// </para>
/// </remarks>
internal static class PublicSurface
{
    /// <summary>
    /// Сборки семейства, у каждой свой слепок (ADR 0003, 0004).
    /// </summary>
    public static IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(ArxisStudio.Surface.SurfaceView).Assembly,
        typeof(ArxisStudio.Surface.Editing.SurfaceGuides).Assembly,
        typeof(DesignEditor).Assembly,
        typeof(ArxisStudio.Surface.Nodes.NodeEditor).Assembly
    ];

    /// <summary>
    /// Собирает слепок сборки в детерминированном порядке.
    /// </summary>
    public static string Build(Assembly assembly)
    {
        var builder = new StringBuilder();

        foreach (var type in ExportedTypes(assembly))
        {
            builder.Append(TypeHeader(type)).Append('\n');

            foreach (var member in DescribeMembers(type).OrderBy(line => line, StringComparer.Ordinal))
                builder.Append("    ").Append(member).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Экспортируемые типы всех трёх сборок — для стражей, которым всё равно, где тип лежит.
    /// </summary>
    public static IEnumerable<Type> ExportedTypes() => Assemblies.SelectMany(ExportedTypes);

    /// <summary>
    /// Экспортируемые типы сборки без сгенерированных компилятором AXAML.
    /// </summary>
    public static IEnumerable<Type> ExportedTypes(Assembly assembly) =>
        assembly
            .GetExportedTypes()
            .Where(type => !type.FullName!.StartsWith("CompiledAvaloniaXaml", StringComparison.Ordinal))
            .OrderBy(type => type.FullName, StringComparer.Ordinal);

    private static string TypeHeader(Type type)
    {
        var kind = type.IsEnum ? "enum"
            : type.IsInterface ? "interface"
            : type.IsValueType ? "struct"
            : type.IsAbstract && type.IsSealed ? "static class"
            : type.IsAbstract ? "abstract class"
            : type.IsSealed ? "sealed class"
            : "class";

        var baseType = type.BaseType is { } b && b != typeof(object) && !type.IsEnum
            ? " : " + Name(b)
            : string.Empty;

        return $"{kind} {type.FullName}{baseType}";
    }

    private static IEnumerable<string> DescribeMembers(Type type)
    {
        if (type.IsEnum)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                yield return $"value {field.Name} = {Convert.ToInt64(field.GetRawConstantValue(), CultureInfo.InvariantCulture)}";

            yield break;
        }

        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic
                                   | BindingFlags.Instance | BindingFlags.Static
                                   | BindingFlags.DeclaredOnly;

        // Аксессоры свойств и событий описываются своими членами, отдельно их
        // перечислять незачем — иначе слепок распухнет вдвое без новой информации.
        var accessors = new HashSet<MethodInfo>();
        foreach (var property in type.GetProperties(Flags))
            accessors.UnionWith(property.GetAccessors(nonPublic: true));
        foreach (var evt in type.GetEvents(Flags))
        {
            if (evt.GetAddMethod(nonPublic: true) is { } add) accessors.Add(add);
            if (evt.GetRemoveMethod(nonPublic: true) is { } remove) accessors.Add(remove);
        }

        foreach (var ctor in type.GetConstructors(Flags).Where(IsVisible))
            yield return $"ctor .ctor({Parameters(ctor)})";

        foreach (var method in type.GetMethods(Flags).Where(m => IsVisible(m) && !accessors.Contains(m) && !IsGenerated(m)))
            yield return $"method {Name(method.ReturnType)} {method.Name}({Parameters(method)})";

        foreach (var property in type.GetProperties(Flags).Where(p => p.GetAccessors(nonPublic: true).Any(IsVisible)))
            yield return $"property {Name(property.PropertyType)} {property.Name} {{ {Accessors(property)}}}";

        foreach (var evt in type.GetEvents(Flags).Where(e => e.GetAddMethod(nonPublic: true) is { } a && IsVisible(a)))
            yield return $"event {Name(evt.EventHandlerType!)} {evt.Name}";

        foreach (var field in type.GetFields(Flags).Where(f => IsVisible(f) && !IsGenerated(f)))
            yield return DescribeField(field);
    }

    private static string DescribeField(FieldInfo field)
    {
        if (typeof(AvaloniaProperty).IsAssignableFrom(field.FieldType) &&
            field.IsStatic &&
            field.GetValue(null) is AvaloniaProperty property)
        {
            var kind = property.IsDirect ? "direct" : property.IsAttached ? "attached" : "styled";
            return $"avalonia {field.Name} -> {property.Name} : {Name(property.PropertyType)} ({kind}){DefaultValue(property, field.DeclaringType!)}";
        }

        var constant = field is { IsLiteral: true, IsInitOnly: false }
            ? " = " + Literal(field.GetRawConstantValue())
            : string.Empty;

        return $"field {Name(field.FieldType)} {field.Name}{constant}";
    }

    /// <summary>
    /// Значение по умолчанию через рефлексию по метаданным.
    /// </summary>
    /// <remarks>
    /// <c>StyledPropertyMetadata&lt;T&gt;.DefaultValue</c> обобщённое, и добираться до него
    /// по типу пришлось бы собирать обобщённый вызов через рефлексию. Чтение свойства
    /// по имени даёт то же самое и переживает смену формы метаданных в следующей версии.
    /// </remarks>
    private static string DefaultValue(AvaloniaProperty property, Type owner)
    {
        if (property.IsDirect)
            return string.Empty;

        var metadata = property.GetMetadata(owner);
        var accessor = metadata.GetType().GetProperty("DefaultValue");
        if (accessor == null)
            return string.Empty;

        var value = accessor.GetValue(metadata);

        // Умолчание ссылочного типа печатается типом, а не значением, и это не
        // упрощение. У `ViewportTransform` умолчание — живой `TransformGroup`,
        // который редактор мутирует на каждом кадре панорамирования: его `ToString()`
        // отдал бы текущую матрицу, то есть baseline менялся бы от того, куда сдвинут
        // viewport. Хуже: `ToString()` у `AvaloniaObject` проверяет поток, а этот тест
        // обычный `[Fact]` и идёт вне UI-потока, — из-за чего он падал через раз,
        // в зависимости от порядка запуска классов.
        if (value is not null && !value.GetType().IsValueType && value is not string)
            return " = <" + Name(value.GetType()) + ">";

        return " = " + Literal(value);
    }

    private static string Accessors(PropertyInfo property)
    {
        var text = new StringBuilder();
        if (property.GetGetMethod(nonPublic: true) is { } getter && IsVisible(getter))
            text.Append(getter.IsPublic ? "get; " : "protected get; ");
        if (property.GetSetMethod(nonPublic: true) is { } setter && IsVisible(setter))
            text.Append(setter.IsPublic ? "set; " : "protected set; ");
        return text.ToString();
    }

    private static string Parameters(MethodBase method) =>
        string.Join(", ", method.GetParameters().Select(p => Name(p.ParameterType)));

    private static bool IsVisible(MethodBase method) =>
        method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

    private static bool IsVisible(FieldInfo field) =>
        field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;

    private static bool IsGenerated(MemberInfo member) =>
        member.Name.Contains('<', StringComparison.Ordinal) ||
        member.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

    /// <summary>
    /// Короткое имя типа с раскрытыми обобщёнными аргументами.
    /// </summary>
    private static string Name(Type type)
    {
        if (type.IsByRef)
            return Name(type.GetElementType()!) + "&";

        if (type.IsArray)
            return Name(type.GetElementType()!) + "[]";

        if (!type.IsGenericType)
            return type.Name;

        var definition = type.Name;
        var tick = definition.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0)
            definition = definition[..tick];

        return $"{definition}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>";
    }

    private static string Literal(object? value) => value switch
    {
        null => "null",
        string text => "\"" + text + "\"",
        bool flag => flag ? "true" : "false",
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        float number => number.ToString("R", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null"
    };
}
