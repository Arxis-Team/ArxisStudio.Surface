using System.Reflection;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Держит направление ссылок между слоями библиотеки — ADR 0003.
/// </summary>
/// <remarks>
/// Пока слои лежат в одной сборке, компилятор направление не проверяет, и граница держится
/// только этим тестом. Слой типа определяется по пространству имён; ссылки собирает
/// <see cref="IlReferenceScanner"/> — из объявлений и из тел методов.
/// <para>
/// Пока шло разделение, типы, ещё не разнесённые по слоям, лежали в «наследии» и были
/// перечислены явно; список только сокращался. Он опустел, и правило стало прямым:
/// тип вне трёх слоёв — ошибка.
/// </para>
/// </remarks>
public class LayerDependencyTests
{
    private const string Root = "ArxisStudio.Surface";

    private enum Layer
    {
        Core,
        Editing,
        UiDesigner,
        Outside,
        Generated
    }

    /// <summary>
    /// Три сборки семейства. Ссылка между ними сборкой уже запрещена только вверх по
    /// ссылкам проектов, а тест держит слой и внутри сборки — по пространству имён.
    /// </summary>
    private static IReadOnlyList<Assembly> Libraries => PublicSurface.Assemblies;

    [Fact]
    public void Layers_Reference_Only_Downwards()
    {
        var unresolved = new List<string>();
        var violations = new List<string>();

        foreach (var type in TopLevelTypes())
        {
            var from = LayerOf(type);
            if (from is Layer.Outside or Layer.Generated)
                continue;

            foreach (var reference in IlReferenceScanner.References(type, unresolved))
            {
                if (!Libraries.Contains(reference.Target.Assembly))
                    continue;

                var to = LayerOf(TopLevel(reference.Target));
                if (!Allowed(from, to))
                    violations.Add($"{from} → {to}: {type.FullName} → {TopLevel(reference.Target).FullName} (через {reference.Via})");
            }
        }

        Assert.True(unresolved.Count == 0, "Не разрешились токены IL:\n" + string.Join("\n", unresolved.Distinct()));
        Assert.True(violations.Count == 0,
            "Ссылка идёт вверх по слоям. Ядро не называет инструменты и дизайнер форм, инструменты не называют " +
            "дизайнер форм: нужное сверху ядро получает через шов (ADR 0003).\n" +
            string.Join("\n", violations.Distinct().OrderBy(v => v, StringComparer.Ordinal)));
    }

    [Fact]
    public void Every_Type_Belongs_To_A_Layer()
    {
        var outside = TopLevelTypes()
            .Where(type => LayerOf(type) == Layer.Outside)
            .Select(type => type.FullName!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(outside.Count == 0,
            "Тип вне слоёв. Положите его в ArxisStudio.Surface, .Editing или .UiDesigner (ADR 0003):\n" +
            string.Join("\n", outside));
    }

    [Fact]
    public void Every_Type_Lives_In_The_Assembly_Of_Its_Layer()
    {
        // Слой задаёт пространство имён, пакет — сборка. Разойдись они, и тип ядра,
        // оставленный в сборке дизайнера форм, был бы недоступен Nodes, хотя по
        // имени принадлежит ядру.
        var misplaced = TopLevelTypes()
            .Where(type => LayerOf(type) is not Layer.Outside)
            .Where(type => type.Assembly.GetName().Name != Root + LayerSuffix(LayerOf(type)))
            .Select(type => $"{type.FullName} лежит в {type.Assembly.GetName().Name}")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(misplaced.Count == 0, "Тип лежит не в сборке своего слоя:\n" + string.Join("\n", misplaced));
    }

    private static string LayerSuffix(Layer layer) => layer switch
    {
        Layer.Editing => ".Editing",
        Layer.UiDesigner => ".UiDesigner",
        _ => string.Empty
    };

    [Fact]
    public void Scanner_Sees_References_Made_Only_In_A_Method_Body()
    {
        // Самопроверка сканера. Ссылка на Target есть только внутри тела метода:
        // ни в поле, ни в сигнатуре её нет. Если сканер перестанет читать IL,
        // главный тест начнёт проходить, ничего не проверяя.
        var unresolved = new List<string>();
        var targets = IlReferenceScanner.References(typeof(BodyOnlyProbe), unresolved)
            .Select(reference => reference.Target)
            .ToList();

        Assert.Empty(unresolved);
        Assert.Contains(typeof(BodyOnlyTarget), targets);
        Assert.Contains(typeof(GenericArgumentTarget), targets);
    }

    [Fact]
    public void Public_Short_Names_Are_Unique_Across_Surface_Namespaces()
    {
        // Три пространства открыты под одним адресом разметки, и одинаковое
        // короткое имя в двух из них сделало бы разметку неоднозначной.
        var clashes = Libraries.SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type.Namespace is { } ns && (ns == Root || ns.StartsWith(Root + ".", StringComparison.Ordinal)))
            .GroupBy(type => type.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(t => t.FullName))}")
            .ToList();

        Assert.True(clashes.Count == 0, "Одно короткое имя в нескольких пространствах слоёв:\n" + string.Join("\n", clashes));
    }

    private static IEnumerable<Type> TopLevelTypes() =>
        Libraries.SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.DeclaringType is null && LayerOf(type) != Layer.Generated);

    private static Type TopLevel(Type type)
    {
        while (type.DeclaringType is not null)
            type = type.DeclaringType;

        return type;
    }

    private static Layer LayerOf(Type type)
    {
        var ns = TopLevel(type).Namespace ?? string.Empty;

        if (ns.Length == 0 || ns.StartsWith("CompiledAvaloniaXaml", StringComparison.Ordinal))
            return Layer.Generated;

        if (InNamespace(ns, Root + ".UiDesigner"))
            return Layer.UiDesigner;

        if (InNamespace(ns, Root + ".Editing"))
            return Layer.Editing;

        return InNamespace(ns, Root) ? Layer.Core : Layer.Outside;
    }

    private static bool InNamespace(string ns, string root) =>
        ns == root || ns.StartsWith(root + ".", StringComparison.Ordinal);

    private static bool Allowed(Layer from, Layer to) => to is Layer.Generated || from switch
    {
        Layer.Core => to == Layer.Core,
        Layer.Editing => to is Layer.Core or Layer.Editing,
        _ => true
    };

    private sealed class BodyOnlyTarget;

    private sealed class GenericArgumentTarget;

    private sealed class BodyOnlyProbe
    {
        public override string ToString() =>
            new BodyOnlyTarget().GetHashCode().ToString() + new List<GenericArgumentTarget>().Count;
    }
}
