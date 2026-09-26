using Avalonia.Controls;

namespace ArxisStudio.Surface;

/// <summary>
/// Участник политики взаимодействия: что он разрешает делать с target'ом.
/// </summary>
/// <remarks>
/// Действующая политика — пересечение всех участников (<c>effective = a &amp; b &amp; …</c>):
/// каждый только сужает, ни один не расширяет другого. Иначе редактор снова начал бы
/// предлагать жест, который ничего не делает. Ядро само ничего не запрещает: запреты
/// приносят слои выше (ADR 0003) — блокировки, поставленные человеком, и то, что
/// физически умеет раскладка.
/// </remarks>
internal interface ISurfaceInteractionPolicy
{
    /// <summary>
    /// Оси, по которым target разрешено двигать.
    /// </summary>
    MovePolicy GetMovePolicy(Control target);

    /// <summary>
    /// Стороны, за которые target разрешено тянуть.
    /// </summary>
    ResizePolicy GetResizePolicy(Control target);
}
