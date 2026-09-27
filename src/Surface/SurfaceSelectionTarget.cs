using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace ArxisStudio.Surface;

/// <summary>
/// Определяет уровень выбранного target в редакторе.
/// </summary>
public enum SurfaceSelectionScope
{
    /// <summary>
    /// Выбран весь контейнер <see cref="SurfaceItem"/>.
    /// </summary>
    Container = 0,

    /// <summary>
    /// Выбран nested control внутри <see cref="SurfaceItem"/>.
    /// </summary>
    NestedTarget = 1
}

/// <summary>
/// Аргументы изменения набора выбранных targets.
/// </summary>
/// <remarks>
/// Событие отличается от <c>SelectingItemsControl.SelectionChanged</c>: тот работает
/// на уровне элементов <c>ItemsSource</c>, а этот — на уровне targets, включая
/// вложенные контролы и вложенные контейнеры.
/// <para>
/// Наборы сравниваются по <see cref="SurfaceSelectionTarget.Target"/>, а не по самим
/// экземплярам <see cref="SurfaceSelectionTarget"/>: они пересоздаются при каждой
/// пересборке overlay и сравнивать их по ссылке бессмысленно.
/// </para>
/// </remarks>
public sealed class SurfaceSelectionChangedEventArgs : EventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SurfaceSelectionChangedEventArgs"/>.
    /// </summary>
    public SurfaceSelectionChangedEventArgs(
        IReadOnlyList<SurfaceSelectionTarget> oldTargets,
        IReadOnlyList<SurfaceSelectionTarget> newTargets,
        IReadOnlyList<SurfaceSelectionTarget> added,
        IReadOnlyList<SurfaceSelectionTarget> removed,
        SurfaceSelectionTarget? oldPrimary,
        SurfaceSelectionTarget? newPrimary)
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
    public IReadOnlyList<SurfaceSelectionTarget> OldTargets { get; }

    /// <summary>
    /// Получает набор targets после изменения.
    /// </summary>
    public IReadOnlyList<SurfaceSelectionTarget> NewTargets { get; }

    /// <summary>
    /// Получает targets, появившиеся в выделении.
    /// </summary>
    public IReadOnlyList<SurfaceSelectionTarget> Added { get; }

    /// <summary>
    /// Получает targets, выбывшие из выделения.
    /// </summary>
    public IReadOnlyList<SurfaceSelectionTarget> Removed { get; }

    /// <summary>
    /// Получает primary target до изменения.
    /// </summary>
    public SurfaceSelectionTarget? OldPrimary { get; }

    /// <summary>
    /// Получает primary target после изменения.
    /// </summary>
    public SurfaceSelectionTarget? NewPrimary { get; }

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
public sealed class SurfaceSelectionTarget
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SurfaceSelectionTarget"/>.
    /// </summary>
    /// <param name="container">Контейнер, которому принадлежит выбранный target.</param>
    /// <param name="target">Выбранный visual target.</param>
    public SurfaceSelectionTarget(SurfaceItem container, Control target)
        : this(container, target, ResolveGroupKey(container, target))
    {
    }

    /// <summary>
    /// Инициализирует новый экземпляр с уже известным ключом группы.
    /// </summary>
    /// <param name="container">Контейнер, которому принадлежит выбранный target.</param>
    /// <param name="target">Выбранный visual target.</param>
    /// <param name="groupId">Ключ группы target'а, который отдала поверхность.</param>
    /// <remarks>
    /// Снимок выделения пересобирается на каждом кадре жеста, а поверхность ключ знает сама:
    /// искать её подъёмом по дереву на каждый target значило бы платить за это в жесте.
    /// </remarks>
    internal SurfaceSelectionTarget(SurfaceItem container, Control target, string? groupId)
    {
        Container = container ?? throw new ArgumentNullException(nameof(container));
        Target = target ?? throw new ArgumentNullException(nameof(target));

        // Scope определяется типом самого target, а не совпадением с владельцем:
        // вложенный SurfaceItem — это контейнер, даже если владеющий item
        // верхнего уровня другой.
        Scope = target is SurfaceItem
            ? SurfaceSelectionScope.Container
            : SurfaceSelectionScope.NestedTarget;

        Depth = CalculateDepth(target);
        DisplayName = CreateDisplayName(target);
        GroupId = groupId;
    }

    /// <summary>
    /// Находит хранилище групп у редактора, которому принадлежит контейнер.
    /// </summary>
    /// <remarks>
    /// Публичный конструктор зовут снаружи — например панель, которой нужна одна подпись, — и
    /// редактор там известен только по дереву. Контейнер вне дерева читается библиотечным
    /// хранилищем: это ровно то, что было до появления шва.
    /// </remarks>
    // Ключ группы отдаёт поверхность, которой принадлежит контейнер: ядро о группах
    // не знает (ADR 0003), а слой, который знает, переопределяет SurfaceView.GetGroupKey.
    private static string? ResolveGroupKey(SurfaceItem container, Control target) =>
        container.FindAncestorOfType<SurfaceView>()?.GetGroupKey(target);

    /// <summary>
    /// Получает контейнер выбранного target.
    /// </summary>
    public SurfaceItem Container { get; }

    /// <summary>
    /// Получает выбранный visual target.
    /// </summary>
    public Control Target { get; }

    /// <summary>
    /// Получает уровень выбора: контейнер или nested target.
    /// </summary>
    public SurfaceSelectionScope Scope { get; }

    /// <summary>
    /// Получает глубину вложенности target в дереве контейнеров.
    /// </summary>
    /// <remarks>
    /// Считается число <see cref="SurfaceItem"/>-предков строго выше target:
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
    /// <c>UiDesignerView.GetGroupMembers</c>.
    /// <para>
    /// Ключ отдаёт слой, который знает о группах (<see cref="SurfaceView.GetGroupKey"/>);
    /// для контейнера вне поверхности его спросить не у кого, и значение пусто.
    /// </para>
    /// </remarks>
    public string? GroupId { get; }

    private static int CalculateDepth(Control target)
    {
        var depth = 0;
        var current = target.GetVisualParent();

        while (current != null)
        {
            if (current is SurfaceItem)
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
