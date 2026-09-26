using System;
using Avalonia;

namespace ArxisStudio.Surface.States;

/// <summary>
/// Что делать с протяжкой контейнера: тащить, отказать или отдать жест своему состоянию.
/// </summary>
internal enum ItemDragKind
{
    /// <summary>Перетаскивать target.</summary>
    Drag,

    /// <summary>Отказать: жест ничего бы не сделал, и курсор говорит об этом.</summary>
    Refuse,

    /// <summary>Отдать жест состоянию, которое создаёт слой выше.</summary>
    Custom
}

/// <summary>
/// Решение поверхности о протяжке контейнера.
/// </summary>
/// <param name="Kind">Что делать.</param>
/// <param name="CreateState">Фабрика состояния для <see cref="ItemDragKind.Custom"/>: контейнер и точка нажатия.</param>
/// <param name="MarkHandled">Пометить движение обработанным, когда отказ показан.</param>
internal readonly record struct ItemDragPlan(
    ItemDragKind Kind,
    Func<SurfaceItem, Point, SurfaceItemState>? CreateState = null,
    bool MarkHandled = false)
{
    public static ItemDragPlan Drag => new(ItemDragKind.Drag);

    public static ItemDragPlan Refuse => new(ItemDragKind.Refuse);
}
