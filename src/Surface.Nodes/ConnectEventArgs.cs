using System;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Вопрос приложению: можно ли соединить эти порты.
/// </summary>
/// <remarks>
/// Задаётся, когда протягиваемая связь приходит на новый порт, — ещё до отпускания, чтобы порт
/// успел показать ответ. Направления редактор уже проверил: источник — всегда выход, цель — вход.
/// Остальные правила графа — один вход на порт, без петель, совместимость типов — здесь.
/// <para>
/// Спрашивается и о новой связи, и об отцеплённом конце существующей — с той парой портов,
/// которая получится. Во втором случае <see cref="Link"/> называет саму связь: правило «во вход —
/// одна связь» иначе отказало бы связи, перецепляющей свой выход, — её вход занят ею же.
/// </para>
/// </remarks>
public sealed class ConnectValidatingEventArgs : EventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ConnectValidatingEventArgs"/>.
    /// </summary>
    /// <param name="source">Данные порта-выхода.</param>
    /// <param name="target">Данные порта-входа.</param>
    /// <param name="link">
    /// Элемент <see cref="NodeEditor.Links"/>, чей конец перецепляют, или <see langword="null"/>
    /// для новой связи.
    /// </param>
    public ConnectValidatingEventArgs(object source, object target, object? link = null)
    {
        Source = source;
        Target = target;
        Link = link;
    }

    /// <summary>
    /// Получает элемент коллекции связей, чей конец перецепляют, или <see langword="null"/>,
    /// если тянут новую связь.
    /// </summary>
    public object? Link { get; }

    /// <summary>
    /// Получает данные порта, из которого выйдет связь.
    /// </summary>
    public object Source { get; }

    /// <summary>
    /// Получает данные порта, в который войдёт связь.
    /// </summary>
    public object Target { get; }

    /// <summary>
    /// Получает или задает ответ. Любой подписчик вправе запретить; разрешить запрещённое другим
    /// нельзя.
    /// </summary>
    public bool IsAllowed { get; set; } = true;
}

/// <summary>
/// Просьба приложению соединить два порта.
/// </summary>
/// <remarks>
/// Коллекцией связей владеет приложение (ADR 0001, 0004): редактор связь не добавит, а попросит.
/// Выполнивший обработчик выставляет <see cref="Handled"/>, и обход подписчиков на нём
/// останавливается — как у <c>DeleteRequested</c>.
/// </remarks>
public sealed class ConnectRequestedEventArgs : EventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ConnectRequestedEventArgs"/>.
    /// </summary>
    /// <param name="source">Данные порта-выхода.</param>
    /// <param name="target">Данные порта-входа.</param>
    public ConnectRequestedEventArgs(object source, object target)
    {
        Source = source;
        Target = target;
    }

    /// <summary>
    /// Получает данные порта, из которого выйдет связь.
    /// </summary>
    public object Source { get; }

    /// <summary>
    /// Получает данные порта, в который войдёт связь.
    /// </summary>
    public object Target { get; }

    /// <summary>
    /// Получает или задает признак того, что приложение соединило порты.
    /// </summary>
    public bool Handled { get; set; }
}
