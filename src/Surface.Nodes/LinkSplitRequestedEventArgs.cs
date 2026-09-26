using System;
using Avalonia;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Аргументы запроса разрезать связь узлом-перевалкой (ADR 0005).
/// </summary>
/// <remarks>
/// Редактор связь не режет: он просит. Хост ставит в <see cref="Location"/> свою перевалку — узел с
/// <see cref="Reroute"/> в шаблоне — и вместо одной связи заводит две: из прежнего источника во вход
/// перевалки и из её выхода в прежнюю цель. Выполнивший запрос ставит <see cref="Handled"/>.
/// </remarks>
public sealed class LinkSplitRequestedEventArgs : EventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="LinkSplitRequestedEventArgs"/>.
    /// </summary>
    /// <param name="link">Элемент <see cref="NodeEditor.Links"/>, который просят разрезать.</param>
    /// <param name="location">Точка кривой, ближайшая к щелчку, в мировых координатах.</param>
    public LinkSplitRequestedEventArgs(object link, Point location)
    {
        Link = link;
        Location = location;
    }

    /// <summary>
    /// Получает элемент коллекции связей, который просят разрезать.
    /// </summary>
    public object Link { get; }

    /// <summary>
    /// Получает точку кривой, ближайшую к щелчку, в мировых координатах.
    /// </summary>
    /// <remarks>
    /// Это где быть центру перевалки. Контейнер узла ставится левым верхним углом, поэтому хост
    /// вычитает половину размера своей перевалки.
    /// </remarks>
    public Point Location { get; }

    /// <summary>
    /// Получает или задает признак того, что запрос выполнен.
    /// </summary>
    public bool Handled { get; set; }
}
