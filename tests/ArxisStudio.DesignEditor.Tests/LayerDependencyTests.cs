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
/// Типы, ещё не разнесённые по слоям, лежат в наследии и перечислены в <see cref="Legacy"/>
/// явно. Список обязан совпадать с действительностью в обе стороны: тип, покинувший наследие,
/// вычёркивается тем же коммитом, что его перенёс, — иначе список перестаёт говорить, сколько
/// работы осталось. Пустой список — конец первого этапа.
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
        Legacy,
        Generated
    }

    /// <summary>
    /// Типы верхнего уровня, ещё не разнесённые по слоям.
    /// </summary>
    private static readonly string[] Legacy =
    [
        "ArxisStudio.ContainerEmptyAreaDragGesture",
        "ArxisStudio.ContextMenuContextPresenter",
        "ArxisStudio.Controls.DesignRuler",
        "ArxisStudio.Controls.SelectionAdorner",
        "ArxisStudio.Controls.SelectionAdornerLayer",
        "ArxisStudio.Controls.SelectionAdornerResizeCompletedEventArgs",
        "ArxisStudio.Controls.SelectionAdornerResizeDeltaEventArgs",
        "ArxisStudio.Controls.SelectionAdornerResizeEventArgs",
        "ArxisStudio.Controls.SelectionAdornerResizeStartedEventArgs",
        "ArxisStudio.Controls.SelectionAdornerRole",
        "ArxisStudio.Controls.SnapGuideLayer",
        "ArxisStudio.DesignEditorContextAction",
        "ArxisStudio.DesignEditorContextRequest",
        "ArxisStudio.DesignEditorContextRequestedEventArgs",
        "ArxisStudio.DesignEditorContextRequestingEventArgs",
        "ArxisStudio.DesignEditorContextScope",
        "ArxisStudio.DesignEditorContextSource",
        "ArxisStudio.DesignEditorCursors",
        "ArxisStudio.DesignEditorInputGestures",
        "ArxisStudio.DesignEditorInteractionOptions",
        "ArxisStudio.DesignEditorPointerButton",
        "ArxisStudio.DesignSelectionChangedEventArgs",
        "ArxisStudio.DesignSelectionScope",
        "ArxisStudio.DesignSelectionTarget",
        "ArxisStudio.GroupDragOperation",
        "ArxisStudio.GroupDragTarget",
        "ArxisStudio.GroupResizeOperation",
        "ArxisStudio.GroupResizeTarget",
        "ArxisStudio.IDesignEditorContextActionProvider",
        "ArxisStudio.IDesignEditorContextPresenter",
        "ArxisStudio.IInteractionOperation",
        "ArxisStudio.SelectionAdornerInfo",
        "ArxisStudio.SelectionInteractionCapabilities",
        "ArxisStudio.States.DesignEditorItemState",
        "ArxisStudio.States.EditorGuideDraggingState",
        "ArxisStudio.States.EditorIdleState",
        "ArxisStudio.States.EditorPanningState",
        "ArxisStudio.States.EditorSelectingState",
        "ArxisStudio.States.EditorState",
        "ArxisStudio.States.ItemDraggingState",
        "ArxisStudio.States.ItemIdleState",
        "ArxisStudio.States.ItemReorderingState",
        "ArxisStudio.States.ItemResizingState",
    ];

    private static Assembly Library => typeof(ArxisStudio.Surface.UiDesigner.DesignEditor).Assembly;

    [Fact]
    public void Layers_Reference_Only_Downwards()
    {
        var unresolved = new List<string>();
        var violations = new List<string>();

        foreach (var type in TopLevelTypes())
        {
            var from = LayerOf(type);
            if (from is Layer.Legacy or Layer.Generated)
                continue;

            foreach (var reference in IlReferenceScanner.References(type, unresolved))
            {
                if (reference.Target.Assembly != Library)
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
    public void Legacy_List_Matches_The_Code()
    {
        var actual = TopLevelTypes()
            .Where(type => LayerOf(type) == Layer.Legacy)
            .Select(type => type.FullName!)
            .ToHashSet(StringComparer.Ordinal);

        var listed = Legacy.ToHashSet(StringComparer.Ordinal);

        var unlisted = actual.Except(listed).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var stale = listed.Except(actual).OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.True(unlisted.Count == 0,
            "Новый тип вне слоёв. Наследие только сокращается: положите тип в ArxisStudio.Surface, " +
            ".Editing или .UiDesigner.\n" + string.Join("\n", unlisted.Select(n => $"        \"{n}\",")));

        Assert.True(stale.Count == 0,
            "Тип покинул наследие — вычеркните его из списка тем же коммитом:\n" + string.Join("\n", stale));
    }

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
        var clashes = Library.GetExportedTypes()
            .Where(type => type.Namespace is { } ns && (ns == Root || ns.StartsWith(Root + ".", StringComparison.Ordinal)))
            .GroupBy(type => type.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(t => t.FullName))}")
            .ToList();

        Assert.True(clashes.Count == 0, "Одно короткое имя в нескольких пространствах слоёв:\n" + string.Join("\n", clashes));
    }

    private static IEnumerable<Type> TopLevelTypes() =>
        Library.GetTypes().Where(type => type.DeclaringType is null && LayerOf(type) != Layer.Generated);

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

        return InNamespace(ns, Root) ? Layer.Core : Layer.Legacy;
    }

    private static bool InNamespace(string ns, string root) =>
        ns == root || ns.StartsWith(root + ".", StringComparison.Ordinal);

    private static bool Allowed(Layer from, Layer to) => to is Layer.Legacy or Layer.Generated || from switch
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
