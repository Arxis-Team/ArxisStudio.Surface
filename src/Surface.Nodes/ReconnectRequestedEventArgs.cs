using System;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Аргументы запроса перецепить конец связи к другому порту.
/// </summary>
/// <remarks>
/// Редактор связь не правит (ADR 0001, 0004): поменять конец у элемента
/// <see cref="NodeEditor.Links"/> или заменить элемент новым — дело приложения, как и положить
/// правку в свою историю отмены. Выполнивший запрос обработчик ставит <see cref="Handled"/>.
/// </remarks>
public sealed class ReconnectRequestedEventArgs : EventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ReconnectRequestedEventArgs"/>.
    /// </summary>
    /// <param name="link">Элемент <see cref="NodeEditor.Links"/>, чей конец перецепляют.</param>
    /// <param name="end">Какой конец перецепляют.</param>
    /// <param name="oldPort">Данные порта, к которому конец был прицеплен.</param>
    /// <param name="newPort">Данные порта, к которому его просят прицепить.</param>
    public ReconnectRequestedEventArgs(object link, LinkEnd end, object oldPort, object newPort)
    {
        Link = link;
        End = end;
        OldPort = oldPort;
        NewPort = newPort;
    }

    /// <summary>
    /// Получает элемент коллекции связей, чей конец перецепляют.
    /// </summary>
    public object Link { get; }

    /// <summary>
    /// Получает конец, который перецепляют.
    /// </summary>
    /// <remarks>
    /// Направление при этом не меняется: конец у выхода встаёт на другой выход, конец у входа —
    /// на другой вход.
    /// </remarks>
    public LinkEnd End { get; }

    /// <summary>
    /// Получает данные порта, к которому конец был прицеплен.
    /// </summary>
    public object OldPort { get; }

    /// <summary>
    /// Получает данные порта, к которому конец просят прицепить.
    /// </summary>
    public object NewPort { get; }

    /// <summary>
    /// Получает или задает признак того, что запрос выполнен.
    /// </summary>
    public bool Handled { get; set; }
}
