using System.Collections.Generic;
using Avalonia;

namespace ArxisStudio.Surface;

// Упрощённый вид (ADR 0008): что панель отдаёт слою, который рисует свёрнутые элементы карточками, и
// когда она говорит ему, что свёрнутое сменилось. Часть VirtualizingSurfacePanel; общее описание
// типа — в VirtualizingSurfacePanel.cs.
public partial class VirtualizingSurfacePanel
{
    /// <summary>
    /// Прямоугольники свёрнутых элементов в мировых координатах; без площади — не отдаются.
    /// </summary>
    /// <remarks>
    /// Развёрнутые рисуют себя сами и сюда не входят.
    /// </remarks>
    internal IEnumerable<Rect> EnumerateCollapsedBounds()
    {
        for (var i = 0; i < _slots.Count; i++)
        {
            if (_realized.ContainsKey(i))
                continue;

            var bounds = BoundsOf(_slots[i]);
            if (bounds.Width > 0 && bounds.Height > 0)
                yield return bounds;
        }
    }

    /// <summary>
    /// Свёрнутое сменилось — геометрия его элемента, коллекция или состав развёрнутых: слой упрощённого
    /// вида соберёт карточки заново.
    /// </summary>
    private void OnCollapsedChanged() => _view?.OnSimplifiedContentChanged();
}
