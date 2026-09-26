using System;
using Avalonia;

namespace ArxisStudio.Surface.Nodes;

// Излом: двойной щелчок по связи просит хоста разрезать её перевалкой (ADR 0005).
// Часть NodeEditor; общее описание типа — в NodeEditor.cs.
public partial class NodeEditor
{
    /// <summary>
    /// Возникает при двойном щелчке по связи: разрежьте её узлом-перевалкой.
    /// </summary>
    /// <remarks>
    /// Излом связи — узел, а не точка на связи (ADR 0005): связь в библиотеке остаётся одной кривой
    /// от порта к порту. Обход подписчиков останавливается на первом выполнившем, как у остальных
    /// запросов редактора.
    /// </remarks>
    public event EventHandler<LinkSplitRequestedEventArgs>? LinkSplitRequested;

    /// <summary>
    /// Просит разрезать связь в точке кривой, ближайшей к <paramref name="world"/>.
    /// </summary>
    internal bool RequestLinkSplit(Link link, Point world)
    {
        var handler = LinkSplitRequested;
        if (handler == null)
            return false;

        var geometry = link.Geometry;
        var args = new LinkSplitRequestedEventArgs(link.ItemOrSelf, geometry.At(geometry.ParameterAt(world)));
        foreach (var invocation in handler.GetInvocationList())
        {
            ((EventHandler<LinkSplitRequestedEventArgs>)invocation)(this, args);
            if (args.Handled)
                break;
        }

        return args.Handled;
    }
}
