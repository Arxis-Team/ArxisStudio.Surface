using System;
using System.Collections.Generic;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Аргументы запроса на удаление связей.
/// </summary>
/// <remarks>
/// Редактор связи не удаляет (ADR 0001, 0004): коллекцией <see cref="NodeEditor.Links"/> владеет
/// приложение. Выполнивший запрос обработчик ставит <see cref="Handled"/>; без него нажатие
/// остаётся необработанным и всплывает дальше.
/// </remarks>
public sealed class LinkDeleteRequestedEventArgs : EventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="LinkDeleteRequestedEventArgs"/>.
    /// </summary>
    /// <param name="links">Элементы <see cref="NodeEditor.Links"/>, которые просят удалить.</param>
    public LinkDeleteRequestedEventArgs(IReadOnlyList<object> links)
    {
        Links = links;
    }

    /// <summary>
    /// Получает элементы коллекции связей, которые просят удалить.
    /// </summary>
    public IReadOnlyList<object> Links { get; }

    /// <summary>
    /// Получает или задает признак того, что запрос выполнен.
    /// </summary>
    public bool Handled { get; set; }
}
