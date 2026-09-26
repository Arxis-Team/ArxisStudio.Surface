namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Контейнер узла графа на поверхности <see cref="NodeEditor"/>.
/// </summary>
/// <remarks>
/// Положение, выделение и перетаскивание — ядра (<see cref="SurfaceItem"/>). Свой тип нужен узлу
/// ради своей темы — Avalonia ищет тему по точному типу — и ради портов, которые лежат в его
/// содержимом: раскладку узла приложение пишет в <c>ItemTemplate</c> редактора, ставя туда
/// порты.
/// </remarks>
public class Node : SurfaceItem
{
}
