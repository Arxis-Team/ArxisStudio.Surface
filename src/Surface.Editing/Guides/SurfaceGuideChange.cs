using System;

namespace ArxisStudio.Surface.Editing;

/// <summary>
/// Вид запрошенного изменения набора направляющих.
/// </summary>
public enum SurfaceGuideChangeKind
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
/// у <c>UiDesignerReorderRequestedEventArgs</c>: запрос описывает набор, снятый
/// до правки, и следующему обработчику он говорил бы о состоянии, которого уже нет.
/// </para>
/// </remarks>
public sealed class SurfaceGuideChangeRequestedEventArgs : EventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SurfaceGuideChangeRequestedEventArgs"/>.
    /// </summary>
    /// <param name="kind">Вид изменения.</param>
    /// <param name="guide">Направляющая после изменения.</param>
    /// <param name="original">Направляющая до изменения; <see langword="null"/> для добавления.</param>
    public SurfaceGuideChangeRequestedEventArgs(
        SurfaceGuideChangeKind kind,
        SurfaceGuide guide,
        SurfaceGuide? original)
    {
        Kind = kind;
        Guide = guide;
        Original = original;
    }

    /// <summary>Получает вид изменения.</summary>
    public SurfaceGuideChangeKind Kind { get; }

    /// <summary>
    /// Получает направляющую, которую описывает запрос.
    /// </summary>
    /// <remarks>
    /// Для <see cref="SurfaceGuideChangeKind.Add"/> и <see cref="SurfaceGuideChangeKind.Move"/> —
    /// итоговое положение; для <see cref="SurfaceGuideChangeKind.Remove"/> — убираемая линия.
    /// </remarks>
    public SurfaceGuide Guide { get; }

    /// <summary>
    /// Получает исходную направляющую для <see cref="SurfaceGuideChangeKind.Move"/>
    /// и <see cref="SurfaceGuideChangeKind.Remove"/>.
    /// </summary>
    /// <remarks>
    /// Хост держит набор сам и не обязан искать перемещаемую линию по координате:
    /// сравнивать надо с этим значением, а не с <see cref="Guide"/>.
    /// </remarks>
    public SurfaceGuide? Original { get; }

    /// <summary>
    /// Получает или задает признак того, что запрос выполнен.
    /// </summary>
    public bool Handled { get; set; }
}
