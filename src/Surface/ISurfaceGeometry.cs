using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Surface;

/// <summary>
/// Геометрия target'ов поверхности: где они лежат и можно ли задать им позицию.
/// </summary>
/// <remarks>
/// Шов между «глупым» ядром и тем, что лежит на холсте (ADR 0003). Ядро пишет позицию
/// только через него, поэтому только здесь решается, примет ли содержимое запись: у
/// формы Avalonia позицией ребёнка может владеть его панель, у узла графа — никто,
/// кроме самого узла.
/// <para>
/// Пока internal: форма шва должна отлежаться, прежде чем стать обещанием.
/// </para>
/// </remarks>
internal interface ISurfaceGeometry
{
    /// <summary>
    /// Возвращает позицию target'а в design-координатах.
    /// </summary>
    Point GetPosition(Control target);

    /// <summary>
    /// Можно ли задать target'у позицию.
    /// </summary>
    /// <remarks>
    /// Ответ «нет» означает, что запись ушла бы в пустоту, и ядро её не делает — а
    /// значит, и не записывает в контракт изменений перемещение, которого не было.
    /// </remarks>
    bool CanSetPosition(Control target);

    /// <summary>
    /// Задаёт позицию target'а в design-координатах.
    /// </summary>
    void SetPosition(Control target, Point position);

    /// <summary>
    /// Возвращает рамку target'а в design-координатах, если она определена.
    /// </summary>
    bool TryGetBounds(Control target, out Rect bounds);
}

/// <summary>
/// Геометрия ядра: позицию держит <see cref="SurfaceItem.Location"/> контейнера.
/// </summary>
/// <remarks>
/// Ядро знает только свои контейнеры. Всё, что лежит внутри них, — дело слоя, который
/// знает, что это такое.
/// </remarks>
internal sealed class SurfaceItemGeometry : ISurfaceGeometry
{
    public Point GetPosition(Control target) => target is SurfaceItem item ? item.Location : default;

    public bool CanSetPosition(Control target) => target is SurfaceItem;

    public void SetPosition(Control target, Point position)
    {
        if (target is SurfaceItem item)
            item.Location = position;
    }

    public bool TryGetBounds(Control target, out Rect bounds)
    {
        if (target is SurfaceItem item && item.Bounds.Width > 0 && item.Bounds.Height > 0)
        {
            bounds = new Rect(item.Location, item.Bounds.Size);
            return true;
        }

        bounds = default;
        return false;
    }
}
