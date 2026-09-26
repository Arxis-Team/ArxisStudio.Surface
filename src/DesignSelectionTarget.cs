using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio;

/// <summary>
/// Определяет уровень выбранного target в редакторе.
/// </summary>
public enum DesignSelectionScope
{
    /// <summary>
    /// Выбран весь контейнер <see cref="DesignEditorItem"/>.
    /// </summary>
    Container = 0,

    /// <summary>
    /// Выбран nested control внутри <see cref="DesignEditorItem"/>.
    /// </summary>
    NestedTarget = 1
}

/// <summary>
/// Аргументы изменения набора выбранных design targets.
/// </summary>
/// <remarks>
/// Событие отличается от <c>SelectingItemsControl.SelectionChanged</c>: тот работает
/// на уровне элементов <c>ItemsSource</c>, а этот — на уровне design targets, включая
/// вложенные контролы и вложенные контейнеры.
/// <para>
/// Наборы сравниваются по <see cref="DesignSelectionTarget.Target"/>, а не по самим
/// экземплярам <see cref="DesignSelectionTarget"/>: они пересоздаются при каждой
/// пересборке overlay и сравнивать их по ссылке бессмысленно.
/// </para>
/// </remarks>
public sealed class DesignSelectionChangedEventArgs : EventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignSelectionChangedEventArgs"/>.
    /// </summary>
    public DesignSelectionChangedEventArgs(
        IReadOnlyList<DesignSelectionTarget> oldTargets,
        IReadOnlyList<DesignSelectionTarget> newTargets,
        IReadOnlyList<DesignSelectionTarget> added,
        IReadOnlyList<DesignSelectionTarget> removed,
        DesignSelectionTarget? oldPrimary,
        DesignSelectionTarget? newPrimary)
    {
        OldTargets = oldTargets ?? throw new ArgumentNullException(nameof(oldTargets));
        NewTargets = newTargets ?? throw new ArgumentNullException(nameof(newTargets));
        Added = added ?? throw new ArgumentNullException(nameof(added));
        Removed = removed ?? throw new ArgumentNullException(nameof(removed));
        OldPrimary = oldPrimary;
        NewPrimary = newPrimary;
    }

    /// <summary>
    /// Получает набор targets до изменения.
    /// </summary>
    public IReadOnlyList<DesignSelectionTarget> OldTargets { get; }

    /// <summary>
    /// Получает набор targets после изменения.
    /// </summary>
    public IReadOnlyList<DesignSelectionTarget> NewTargets { get; }

    /// <summary>
    /// Получает targets, появившиеся в выделении.
    /// </summary>
    public IReadOnlyList<DesignSelectionTarget> Added { get; }

    /// <summary>
    /// Получает targets, выбывшие из выделения.
    /// </summary>
    public IReadOnlyList<DesignSelectionTarget> Removed { get; }

    /// <summary>
    /// Получает primary target до изменения.
    /// </summary>
    public DesignSelectionTarget? OldPrimary { get; }

    /// <summary>
    /// Получает primary target после изменения.
    /// </summary>
    public DesignSelectionTarget? NewPrimary { get; }

    /// <summary>
    /// Получает значение, указывающее, что сменился именно primary target.
    /// </summary>
    /// <remarks>
    /// Обычный клик по участнику группы не меняет её состав, но переносит target
    /// в начало — для инспектора свойств это и есть значимое событие.
    /// </remarks>
    public bool IsPrimaryChanged =>
        !ReferenceEquals(OldPrimary?.Target, NewPrimary?.Target);
}

/// <summary>
/// Представляет публичную запись о выбранном target редактора.
/// </summary>
public sealed class DesignSelectionTarget
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignSelectionTarget"/>.
    /// </summary>
    /// <param name="container">Контейнер, которому принадлежит выбранный target.</param>
    /// <param name="target">Выбранный visual target.</param>
    public DesignSelectionTarget(DesignEditorItem container, Control target)
        : this(container, target, null)
    {
    }

    /// <summary>
    /// Инициализирует новый экземпляр с уже известным хранилищем групп.
    /// </summary>
    /// <param name="container">Контейнер, которому принадлежит выбранный target.</param>
    /// <param name="target">Выбранный visual target.</param>
    /// <param name="groupStore">Хранилище групп или <see langword="null"/>, чтобы найти его по дереву.</param>
    /// <remarks>
    /// Снимок выделения пересобирается на каждом кадре жеста, а редактор своё хранилище знает:
    /// искать его подъёмом по дереву на каждый target значило бы платить за это в жесте.
    /// </remarks>
    internal DesignSelectionTarget(DesignEditorItem container, Control target, IDesignGroupStore? groupStore)
    {
        Container = container ?? throw new ArgumentNullException(nameof(container));
        Target = target ?? throw new ArgumentNullException(nameof(target));

        // Scope определяется типом самого target, а не совпадением с владельцем:
        // вложенный DesignEditorItem — это контейнер, даже если владеющий item
        // верхнего уровня другой.
        Scope = target is DesignEditorItem
            ? DesignSelectionScope.Container
            : DesignSelectionScope.NestedTarget;

        Depth = CalculateDepth(target);
        DisplayName = CreateDisplayName(target);
        GroupId = (groupStore ?? ResolveGroupStore(container)).GetGroup(target);
    }

    /// <summary>
    /// Находит хранилище групп у редактора, которому принадлежит контейнер.
    /// </summary>
    /// <remarks>
    /// Публичный конструктор зовут снаружи — например панель, которой нужна одна подпись, — и
    /// редактор там известен только по дереву. Контейнер вне дерева читается библиотечным
    /// хранилищем: это ровно то, что было до появления шва.
    /// </remarks>
    private static IDesignGroupStore ResolveGroupStore(DesignEditorItem container) =>
        container.FindAncestorOfType<DesignEditor>()?.GroupStore ?? DesignGroupAttachedStore.Default;

    /// <summary>
    /// Получает контейнер выбранного target.
    /// </summary>
    public DesignEditorItem Container { get; }

    /// <summary>
    /// Получает выбранный visual target.
    /// </summary>
    public Control Target { get; }

    /// <summary>
    /// Получает уровень выбора: контейнер или nested target.
    /// </summary>
    public DesignSelectionScope Scope { get; }

    /// <summary>
    /// Получает глубину вложенности target в дереве контейнеров.
    /// </summary>
    /// <remarks>
    /// Считается число <see cref="DesignEditorItem"/>-предков строго выше target:
    /// <list type="bullet">
    /// <item><description><c>0</c> — контейнер верхнего уровня;</description></item>
    /// <item><description><c>1</c> — контрол внутри контейнера верхнего уровня либо вложенный контейнер;</description></item>
    /// <item><description><c>2</c> — контрол внутри вложенного контейнера, и так далее.</description></item>
    /// </list>
    /// Вместе со <see cref="Scope"/> однозначно описывает положение target в дереве.
    /// </remarks>
    public int Depth { get; }

    /// <summary>
    /// Получает краткое диагностическое имя выбранного target.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Получает идентификатор design-time группы target или <see langword="null"/>, если он не сгруппирован.
    /// </summary>
    /// <remarks>
    /// Значение прочитано при создании снимка, поэтому свежо ровно настолько же,
    /// насколько сам снимок. Идентификатор осмыслен в пределах <see cref="Container"/>:
    /// одинаковый идентификатор в двух формах означает две разные группы. Состав —
    /// <see cref="DesignEditor.GetGroupMembers"/>.
    /// </remarks>
    public string? GroupId { get; }

    private static int CalculateDepth(Control target)
    {
        var depth = 0;
        var current = target.GetVisualParent();

        while (current != null)
        {
            if (current is DesignEditorItem)
                depth++;

            current = current.GetVisualParent();
        }

        return depth;
    }

    private static string CreateDisplayName(Control control)
    {
        var typeName = control.GetType().Name;
        return !string.IsNullOrWhiteSpace(control.Name)
            ? $"{typeName} ({control.Name})"
            : typeName;
    }
}
