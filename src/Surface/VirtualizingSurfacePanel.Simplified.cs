using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArxisStudio.Surface;

// Упрощённый вид (ADR 0008): что панель отдаёт слою, который рисует свёрнутые элементы карточками, и
// когда она говорит ему, что свёрнутое сменилось. Часть VirtualizingSurfacePanel; общее описание
// типа — в VirtualizingSurfacePanel.cs.
public partial class VirtualizingSurfacePanel
{
    /// <summary>
    /// Свёрнутые элементы: прямоугольник в мировых координатах и полоса заголовка; без площади — не
    /// отдаются.
    /// </summary>
    /// <remarks>
    /// Развёрнутые рисуют себя сами и сюда не входят.
    /// </remarks>
    internal IEnumerable<(Rect Bounds, IBrush? Accent)> EnumerateCollapsed()
    {
        for (var i = 0; i < _slots.Count; i++)
        {
            if (_realized.ContainsKey(i))
                continue;

            var slot = _slots[i];
            var bounds = BoundsOf(slot);
            if (bounds.Width > 0 && bounds.Height > 0)
                yield return (bounds, slot.Accent);
        }
    }

    /// <summary>
    /// Лежит ли под точкой свёрнутый элемент.
    /// </summary>
    internal bool IsCollapsedAt(Point world)
    {
        SyncSlots(Items);
        for (var i = 0; i < _slots.Count; i++)
        {
            if (!_realized.ContainsKey(i) && BoundsOf(_slots[i]).Contains(world))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Разворачивает верхний свёрнутый элемент под точкой — для нажатия в упрощённом виде.
    /// </summary>
    /// <remarks>
    /// Верхний — с большим индексом: дети стоят в порядке элементов, и позже поставленный рисуется
    /// поверх.
    /// </remarks>
    /// <returns>Контейнер или <see langword="null"/>, если под точкой свёрнутого нет.</returns>
    internal Control? RealizeAt(Point world)
    {
        SyncSlots(Items);
        for (var i = _slots.Count - 1; i >= 0; i--)
        {
            if (!_realized.ContainsKey(i) && BoundsOf(_slots[i]).Contains(world))
                return RealizeNow(i);
        }

        return null;
    }

    /// <summary>
    /// Свёрнутое сменилось — геометрия его элемента, коллекция или состав развёрнутых: слой упрощённого
    /// вида соберёт карточки заново.
    /// </summary>
    private void OnCollapsedChanged() => _view?.OnSimplifiedContentChanged();
}
