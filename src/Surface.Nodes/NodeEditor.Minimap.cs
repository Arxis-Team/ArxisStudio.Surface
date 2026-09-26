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
    /// Сколько связей миникарта нарисовала в последний раз — для тестов.
    /// </summary>
    internal int LinksOnMinimap { get; private set; }

    /// <summary>
    /// Рисует связи на миникарте — той же кубической кривой, что на холсте, в одну точку толщиной.
    /// </summary>
    /// <remarks>
    /// Рисуются только разрешённые связи: связь, у которой нет порта на поверхности, не видна и на
    /// холсте. Кисть — та же, что у связей, ключ <c>NodeEditor.Link.Stroke</c>.
    /// </remarks>
    private sealed class LinkMinimapLayer(NodeEditor editor) : IMinimapLayer
    {
        public void Render(DrawingContext context, Matrix worldToMinimap, SurfaceMinimap minimap)
        {
            editor.LinksOnMinimap = 0;
            if (!minimap.TryFindResource("NodeEditor.Link.Stroke", minimap.ActualThemeVariant, out var value)
                || value is not IBrush brush)
            {
                return;
            }

            var pen = new Pen(brush, 1);
            foreach (var link in editor._links)
            {
                if (!link.IsResolved)
                    continue;

                var g = link.Geometry;
                var figure = new StreamGeometry();
                using (var ctx = figure.Open())
                {
                    ctx.BeginFigure(g.Source.Transform(worldToMinimap), isFilled: false);
                    ctx.CubicBezierTo(
                        g.SourceControl.Transform(worldToMinimap),
                        g.TargetControl.Transform(worldToMinimap),
                        g.Target.Transform(worldToMinimap));
                    ctx.EndFigure(isClosed: false);
                }

                context.DrawGeometry(null, pen, figure);
                editor.LinksOnMinimap++;
            }
        }
    }
}
