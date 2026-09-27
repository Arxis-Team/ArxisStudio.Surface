using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ArxisStudio.Surface.Editing;

namespace ArxisStudio.Surface.Nodes;

// Связи на миникарте: слой, который миникарта находит службой редактора (ADR 0005).
// Часть NodeEditor; общее описание типа — в NodeEditor.cs.
public partial class NodeEditor
{
    /// <summary>
    /// Сколько связей попало на миникарту при последней сборке её содержимого — для тестов.
    /// </summary>
    internal int LinksOnMinimap { get; private set; }

    /// <summary>
    /// Отдаёт миникарте связи — той же кубической кривой, что на холсте; карта рисует их в одну
    /// точку толщиной.
    /// </summary>
    /// <remarks>
    /// Отдаются только разрешённые связи: связь, у которой нет порта на поверхности, не видна и на
    /// холсте. Кисть — та же, что у связей, ключ <c>NodeEditor.Link.Stroke</c>. Кривые строятся на
    /// сборке содержимого карты, а не на каждой её перерисовке (ADR 0007).
    /// </remarks>
    private sealed class LinkMinimapLayer(NodeEditor editor) : IMinimapLayer
    {
        public void Build(StreamGeometryContext context)
        {
            var built = 0;
            foreach (var link in editor._links)
            {
                if (!link.IsResolved)
                    continue;

                var g = link.Geometry;
                context.BeginFigure(g.Source, isFilled: false);
                context.CubicBezierTo(g.SourceControl, g.TargetControl, g.Target);
                context.EndFigure(isClosed: false);
                built++;
            }

            editor.LinksOnMinimap = built;
        }

        public IBrush? FindStroke(SurfaceMinimap minimap) =>
            minimap.TryFindResource("NodeEditor.Link.Stroke", minimap.ActualThemeVariant, out var value)
                ? value as IBrush
                : null;
    }
}
