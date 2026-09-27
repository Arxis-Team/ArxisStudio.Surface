using System.Reflection;
using System.Reflection.Emit;

namespace ArxisStudio.Tests;

/// <summary>
/// Собирает типы, на которые ссылается тип: из объявлений и из тел методов.
/// </summary>
/// <remarks>
/// Тело метода читается обязательно. Самые важные ссылки между слоями не видны ни в одной
/// сигнатуре: <c>AvaloniaProperty.Register&lt;UiDesignerView, …&gt;</c> в инициализаторе поля,
/// <c>FindAncestorOfType&lt;UiDesignerView&gt;()</c> в середине метода. Поэтому IL разбирается
/// по опкодам, а токены операндов переводятся в члены через <see cref="Module.ResolveMember(int, Type[], Type[])"/>.
/// <para>
/// Новых пакетов для этого не нужно: таблица опкодов берётся из <see cref="OpCodes"/> рефлексией.
/// </para>
/// <para>
/// Слепые зоны известны и записаны в ADR 0003: константы и значения параметров по умолчанию
/// встраиваются в место вызова, <c>nameof</c> ссылки не оставляет, <c>cref</c> в IL не попадает.
/// </para>
/// </remarks>
internal static class IlReferenceScanner
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly OpCode[] OneByte = new OpCode[256];
    private static readonly OpCode[] TwoByte = new OpCode[256];

    static IlReferenceScanner()
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode code)
                continue;

            var value = (ushort)code.Value;
            if (code.Size == 1)
                OneByte[value] = code;
            else
                TwoByte[value & 0xFF] = code;
        }
    }

    /// <summary>
    /// Ссылка: на какой тип и через что — для сообщения о нарушении.
    /// </summary>
    public readonly record struct Reference(Type Target, string Via);

    /// <summary>
    /// Все типы, названные типом <paramref name="type"/> и его вложенными типами.
    /// </summary>
    /// <param name="type">Тип верхнего уровня.</param>
    /// <param name="unresolved">Токены, которые не удалось разрешить: тест обязан их показать, а не проглотить.</param>
    public static IEnumerable<Reference> References(Type type, ICollection<string> unresolved)
    {
        foreach (var nested in SelfAndNested(type))
        foreach (var reference in DeclaredReferences(nested, unresolved))
            yield return reference;
    }

    private static IEnumerable<Type> SelfAndNested(Type type)
    {
        yield return type;

        foreach (var nested in type.GetNestedTypes(Declared))
        foreach (var inner in SelfAndNested(nested))
            yield return inner;
    }

    private static IEnumerable<Reference> DeclaredReferences(Type type, ICollection<string> unresolved)
    {
        var name = type.Name;

        foreach (var t in Flatten(type.BaseType))
            yield return new Reference(t, $"{name}: base");

        foreach (var iface in type.GetInterfaces())
        foreach (var t in Flatten(iface))
            yield return new Reference(t, $"{name}: interface");

        foreach (var t in Attributes(type.CustomAttributes))
            yield return new Reference(t, $"{name}: attribute");

        foreach (var field in type.GetFields(Declared))
        {
            foreach (var t in Flatten(field.FieldType))
                yield return new Reference(t, $"{name}.{field.Name}");

            foreach (var t in Attributes(field.CustomAttributes))
                yield return new Reference(t, $"{name}.{field.Name}: attribute");
        }

        foreach (var property in type.GetProperties(Declared))
        foreach (var t in Flatten(property.PropertyType))
            yield return new Reference(t, $"{name}.{property.Name}");

        foreach (var @event in type.GetEvents(Declared))
        foreach (var t in Flatten(@event.EventHandlerType))
            yield return new Reference(t, $"{name}.{@event.Name}");

        var methods = type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared));

        foreach (var method in methods)
        {
            var via = $"{name}.{method.Name}";

            foreach (var t in Signature(method))
                yield return new Reference(t, via);

            foreach (var t in Attributes(method.CustomAttributes))
                yield return new Reference(t, via + ": attribute");

            var body = method.GetMethodBody();
            if (body is null)
                continue;

            foreach (var local in body.LocalVariables)
            foreach (var t in Flatten(local.LocalType))
                yield return new Reference(t, via + ": local");

            foreach (var clause in body.ExceptionHandlingClauses)
            {
                if (clause.Flags != ExceptionHandlingClauseOptions.Clause)
                    continue;

                foreach (var t in Flatten(clause.CatchType))
                    yield return new Reference(t, via + ": catch");
            }

            var il = body.GetILAsByteArray();
            if (il is null)
                continue;

            foreach (var member in Operands(method, il, unresolved))
            foreach (var t in FlattenMember(member))
                yield return new Reference(t, via + ": body");
        }
    }

    private static IEnumerable<Type> Attributes(IEnumerable<CustomAttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            yield return attribute.AttributeType;

            foreach (var argument in attribute.ConstructorArguments.Concat(attribute.NamedArguments.Select(n => n.TypedValue)))
            {
                if (argument.Value is Type typeArgument)
                    foreach (var t in Flatten(typeArgument))
                        yield return t;
            }
        }
    }

    private static IEnumerable<Type> Signature(MethodBase method)
    {
        foreach (var parameter in method.GetParameters())
        foreach (var t in Flatten(parameter.ParameterType))
            yield return t;

        if (method is MethodInfo info)
            foreach (var t in Flatten(info.ReturnType))
                yield return t;

        if (method.IsGenericMethod)
            foreach (var argument in method.GetGenericArguments())
            foreach (var t in Flatten(argument))
                yield return t;
    }

    private static IEnumerable<Type> FlattenMember(MemberInfo member)
    {
        switch (member)
        {
            case Type type:
                foreach (var t in Flatten(type))
                    yield return t;
                break;

            case FieldInfo field:
                foreach (var t in Flatten(field.DeclaringType))
                    yield return t;
                foreach (var t in Flatten(field.FieldType))
                    yield return t;
                break;

            case MethodBase method:
                foreach (var t in Flatten(method.DeclaringType))
                    yield return t;
                foreach (var t in Signature(method))
                    yield return t;
                break;
        }
    }

    /// <summary>
    /// Разворачивает массивы, ссылки, указатели и аргументы генериков до составляющих типов.
    /// </summary>
    public static IEnumerable<Type> Flatten(Type? type)
    {
        if (type is null || type.IsGenericParameter)
            yield break;

        if (type.HasElementType)
        {
            foreach (var t in Flatten(type.GetElementType()))
                yield return t;
            yield break;
        }

        if (type.IsGenericType)
        {
            yield return type.GetGenericTypeDefinition();

            foreach (var argument in type.GetGenericArguments())
            foreach (var t in Flatten(argument))
                yield return t;

            yield break;
        }

        yield return type;
    }

    private static IEnumerable<MemberInfo> Operands(MethodBase method, byte[] il, ICollection<string> unresolved)
    {
        var typeArguments = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;

        var position = 0;
        while (position < il.Length)
        {
            OpCode code;
            var first = il[position++];

            if (first == 0xFE)
                code = TwoByte[il[position++]];
            else
                code = OneByte[first];

            switch (code.OperandType)
            {
                case OperandType.InlineNone:
                    break;

                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    position += 1;
                    break;

                case OperandType.InlineVar:
                    position += 2;
                    break;

                case OperandType.InlineBrTarget:
                case OperandType.InlineI:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.ShortInlineR:
                    position += 4;
                    break;

                case OperandType.InlineI8:
                case OperandType.InlineR:
                    position += 8;
                    break;

                case OperandType.InlineSwitch:
                    var count = BitConverter.ToInt32(il, position);
                    position += 4 + 4 * count;
                    break;

                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                    var token = BitConverter.ToInt32(il, position);
                    position += 4;

                    MemberInfo? member = null;
                    try
                    {
                        member = method.Module.ResolveMember(token, typeArguments, methodArguments);
                    }
                    catch (Exception e) when (e is ArgumentException or MissingMemberException or TypeLoadException or BadImageFormatException)
                    {
                        unresolved.Add($"{method.DeclaringType?.FullName}.{method.Name}: 0x{token:X8} ({e.GetType().Name})");
                    }

                    if (member is not null)
                        yield return member;
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Неизвестный тип операнда {code.OperandType} у {code.Name} в {method.DeclaringType?.FullName}.{method.Name}.");
            }
        }
    }
}
