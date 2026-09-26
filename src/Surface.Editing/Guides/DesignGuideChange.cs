using System;

namespace ArxisStudio.Surface.Editing;

/// <summary>
/// Вид запрошенного изменения набора направляющих.
/// </summary>
public enum DesignGuideChangeKind
{
    /// <summary>Добавить направляющую.</summary>
    Add,

    /// <summary>Переместить существующую направляющую.</summary>
    Move,

    /// <summary>Убрать направляющую.</summary>
    Remove
}

/// <summary>
/// Запрос на изменение набора пользовательских направляющих.
/// </summary>
/// <remarks>
/// Набором владеет хост, поэтому редактор его не правит, а просит — тем же способом,
/// что и при удалении элементов и перестановке среди соседей. Пока обработчик не
/// выставил <see cref="Handled"/>, не происходит ничего: направляющая остаётся там,
/// где была.
/// <para>
/// Обход подписчиков останавливается на первом выполнившем запрос. Причина та же, что
/// у <c>DesignEditorReorderRequestedEventArgs</c>: запрос описывает набор, снятый
/// до правки, и следующему обработчику он говорил бы о состоянии, которого уже нет.
/// </para>
/// </remarks>
public sealed class DesignGuideChangeRequestedEventArgs : EventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignGuideChangeRequestedEventArgs"/>.
    /// </summary>
    /// <param name="kind">Вид изменения.</param>
    /// <param name="guide">Направляющая после изменения.</param>
    /// <param name="original">Направляющая до изменения; <see langword="null"/> для добавления.</param>
    public DesignGuideChangeRequestedEventArgs(
        DesignGuideChangeKind kind,
        DesignGuide guide,
        DesignGuide? original)
    {
        Kind = kind;
        Guide = guide;
        Original = original;
    }

    /// <summary>Получает вид изменения.</summary>
    public DesignGuideChangeKind Kind { get; }

    /// <summary>
    /// Получает направляющую, которую описывает запрос.
    /// </summary>
    /// <remarks>
    /// Для <see cref="DesignGuideChangeKind.Add"/> и <see cref="DesignGuideChangeKind.Move"/> —
    /// итоговое положение; для <see cref="DesignGuideChangeKind.Remove"/> — убираемая линия.
    /// </remarks>
    public DesignGuide Guide { get; }

    /// <summary>
    /// Получает исходную направляющую для <see cref="DesignGuideChangeKind.Move"/>
    /// и <see cref="DesignGuideChangeKind.Remove"/>.
    /// </summary>
    /// <remarks>
    /// Хост держит набор сам и не обязан искать перемещаемую линию по координате:
    /// сравнивать надо с этим значением, а не с <see cref="Guide"/>.
    /// </remarks>
    public DesignGuide? Original { get; }

    /// <summary>
    /// Получает или задает признак того, что запрос выполнен.
    /// </summary>
    public bool Handled { get; set; }
}
