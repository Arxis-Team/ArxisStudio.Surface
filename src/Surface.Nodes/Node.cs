using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Контейнер узла графа на поверхности <see cref="NodeEditor"/>.
/// </summary>
/// <remarks>
/// Положение, выделение и перетаскивание — ядра (<see cref="SurfaceItem"/>). Свой тип нужен узлу
/// ради своей темы — Avalonia ищет тему по точному типу — и ради портов, которые лежат в его
/// содержимом: раскладку узла приложение пишет в <c>ItemTemplate</c> редактора, ставя туда
/// <see cref="Port"/>. Узел, в котором лежит <see cref="Reroute"/>, получает псевдокласс
/// <c>:reroute</c>: тема снимает с него карточку.
/// </remarks>
public class Node : SurfaceItem
{
    /// <summary>
    /// Раскладывает содержимое узла и сверяет, не сдвинулись ли в нём порты.
    /// </summary>
    /// <param name="finalSize">Размер, отведённый узлу.</param>
    /// <returns>Размер, который узел занял.</returns>
    /// <remarks>
    /// Сверка стоит здесь, а не на границах порта: когда границы получает порт, его предки внутри
    /// узла — список, его контейнер, сетка — своих ещё не получили, и конец, посчитанный в этот
    /// миг, ложится мимо. К концу раскладки узла всё его поддерево разложено окончательно. Сдвиг
    /// самого узла портов внутри него не двигает, поэтому кадр перетаскивания здесь ничего не
    /// пересчитывает.
    /// </remarks>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        this.FindAncestorOfType<NodeEditor>()?.OnNodeArranged(this);
        return size;
    }

    /// <summary>
    /// Отмечает узел перевалкой — его ставит и снимает сам <see cref="Reroute"/>.
    /// </summary>
    internal void SetReroute(bool value) => PseudoClasses.Set(":reroute", value);
}
