using System;
using System.Collections.Generic;
using Avalonia;

namespace ArxisStudio.Surface.Editing;

/// <summary>
/// Считает выравнивание перетаскиваемого прямоугольника по краям и центрам соседей.
/// </summary>
/// <remarks>
/// Здесь только арифметика: ни визуального дерева, ни редактора. Соседи приходят
/// готовыми прямоугольниками в мировых координатах, поэтому правило одинаково
/// работает и для вложенного контрола, и для контейнера верхнего уровня.
/// <para>
/// Кандидатов по каждой оси три — ближний край, центр, дальний край, — и сравнивается
/// каждый с каждым. Выравнивание «правый край к левому краю соседа» получается само,
/// отдельного правила под него не нужно.
/// </para>
/// </remarks>
internal static class DesignSnapGuideResolver
{
    /// <summary>
    /// Допуск совпадения при сборе линий. Совпадение здесь уже точное:
    /// смещение применено, и остаётся только погрешность double.
    /// </summary>
    private const double Epsilon = 0.01;

    private static readonly DesignSnapGuideAlignment[] Alignments =
    {
        DesignSnapGuideAlignment.Near,
        DesignSnapGuideAlignment.Centre,
        DesignSnapGuideAlignment.Far
    };

    /// <summary>
    /// Ищет смещение, которое ставит прямоугольник на ближайшую направляющую.
    /// </summary>
    /// <param name="moving">Прямоугольник в предполагаемой позиции.</param>
    /// <param name="neighbours">Соседи в мировых координатах.</param>
    /// <param name="tolerance">Радиус захвата в мировых единицах.</param>
    /// <param name="offset">Смещение, приводящее к выравниванию.</param>
    /// <param name="snappedX">Признак того, что выравнивание найдено по оси X.</param>
    /// <param name="snappedY">Признак того, что выравнивание найдено по оси Y.</param>
    /// <returns><see langword="true"/>, если найдено выравнивание хотя бы по одной оси.</returns>
    /// <remarks>
    /// Оси считаются независимо: элемент может встать на направляющую по X и при этом
    /// остаться на сетке по Y. Поэтому и результат — два отдельных признака, а не один.
    /// </remarks>
    internal static bool TryResolveOffset(
        Rect moving,
        IReadOnlyList<Rect> neighbours,
        double tolerance,
        out Vector offset,
        out bool snappedX,
        out bool snappedY)
    {
        snappedX = TryResolveAxis(moving, neighbours, tolerance, xAxis: true, out var deltaX);
        snappedY = TryResolveAxis(moving, neighbours, tolerance, xAxis: false, out var deltaY);

        offset = new Vector(snappedX ? deltaX : 0, snappedY ? deltaY : 0);
        return snappedX || snappedY;
    }

    /// <summary>
    /// Ищет выравнивание для одного двигающегося края.
    /// </summary>
    /// <param name="edge">Координата края по своей оси.</param>
    /// <param name="neighbours">Соседи в мировых координатах.</param>
    /// <param name="tolerance">Радиус захвата в мировых единицах.</param>
    /// <param name="xAxis">Признак горизонтальной оси.</param>
    /// <param name="snapped">Координата края после выравнивания.</param>
    /// <returns><see langword="true"/>, если выравнивание найдено.</returns>
    /// <remarks>
    /// При изменении размера у элемента нет трёх кандидатов, как при перетаскивании:
    /// пользователь тянет конкретный край, и выравнивается именно он. Кандидатов
    /// у соседа по-прежнему три — край, центр, край, — поэтому потянутая сторона
    /// садится и на край соседа, и на его центральную ось.
    /// </remarks>
    internal static bool TryResolveEdge(
        double edge,
        IReadOnlyList<Rect> neighbours,
        double tolerance,
        bool xAxis,
        out double snapped)
    {
        snapped = edge;

        if (!(tolerance > 0))
            return false;

        var found = false;
        var best = double.PositiveInfinity;

        for (var i = 0; i < neighbours.Count; i++)
        {
            foreach (var alignment in Alignments)
            {
                var candidate = Coordinate(neighbours[i], alignment, xAxis);
                var distance = Math.Abs(candidate - edge);

                if (distance > tolerance || distance >= best)
                    continue;

                best = distance;
                snapped = candidate;
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// Собирает линии, совпавшие с уже применённой геометрией.
    /// </summary>
    /// <param name="bounds">Итоговый прямоугольник перетаскиваемого элемента.</param>
    /// <param name="neighbours">Соседи в мировых координатах.</param>
    /// <remarks>
    /// Считается по итоговой позиции, а не по найденному смещению: ось, которую
    /// направляющая не заняла, могла уйти на сетку, и линия должна быть натянута
    /// по фактическому положению элемента.
    /// <para>
    /// Совпадение проверяется у всех соседей, а не только у победившего: когда
    /// у трёх контролов один левый край, выравнивание идёт ко всем трём, и линия
    /// обязана дотянуться до самого дальнего.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<DesignSnapGuide> CollectGuides(Rect bounds, IReadOnlyList<Rect> neighbours)
    {
        List<DesignSnapGuide>? guides = null;

        CollectAxis(bounds, neighbours, xAxis: true, ref guides);
        CollectAxis(bounds, neighbours, xAxis: false, ref guides);

        return guides ?? (IReadOnlyList<DesignSnapGuide>)Array.Empty<DesignSnapGuide>();
    }

    /// <summary>
    /// Ищет ближайшее выравнивание по одной оси.
    /// </summary>
    /// <remarks>
    /// Побеждает наименьшее по модулю смещение. При равенстве остаётся найденный
    /// раньше, а порядок перебора фиксирован — ближний край, центр, дальний край, —
    /// поэтому результат не зависит от порядка соседей в списке.
    /// </remarks>
    private static bool TryResolveAxis(
        Rect moving,
        IReadOnlyList<Rect> neighbours,
        double tolerance,
        bool xAxis,
        out double delta)
    {
        delta = 0;

        if (!(tolerance > 0))
            return false;

        var found = false;
        var best = double.PositiveInfinity;

        foreach (var alignment in Alignments)
        {
            var movingCoordinate = Coordinate(moving, alignment, xAxis);

            for (var i = 0; i < neighbours.Count; i++)
            {
                foreach (var neighbourAlignment in Alignments)
                {
                    var candidate = Coordinate(neighbours[i], neighbourAlignment, xAxis) - movingCoordinate;
                    var distance = Math.Abs(candidate);

                    if (distance > tolerance || distance >= best)
                        continue;

                    best = distance;
                    delta = candidate;
                    found = true;
                }
            }
        }

        return found;
    }

    private static void CollectAxis(
        Rect bounds,
        IReadOnlyList<Rect> neighbours,
        bool xAxis,
        ref List<DesignSnapGuide>? guides)
    {
        foreach (var alignment in Alignments)
        {
            var line = Coordinate(bounds, alignment, xAxis);
            var start = xAxis ? bounds.Y : bounds.X;
            var end = xAxis ? bounds.Bottom : bounds.Right;
            var matched = false;

            // Центральной линия считается, только когда центры совпали с обеих сторон.
            // Совпадение центра с чужим краем — это всё ещё выравнивание по краю:
            // симметрии, ради которой центр и красят иначе, в нём нет.
            var centre = false;

            for (var i = 0; i < neighbours.Count; i++)
            {
                var neighbour = neighbours[i];

                // Перебираются все три выравнивания соседа, а не до первого совпавшего:
                // у прямоугольника нулевой толщины — а это пользовательская направляющая —
                // край и центр совпадают, и выход по первому навсегда прятал бы центр.
                // Накопление start/end при этом идемпотентно, повтор ничего не портит.
                foreach (var neighbourAlignment in Alignments)
                {
                    if (Math.Abs(Coordinate(neighbour, neighbourAlignment, xAxis) - line) > Epsilon)
                        continue;

                    matched = true;
                    centre |= alignment == DesignSnapGuideAlignment.Centre
                              && neighbourAlignment == DesignSnapGuideAlignment.Centre;
                    start = Math.Min(start, xAxis ? neighbour.Y : neighbour.X);
                    end = Math.Max(end, xAxis ? neighbour.Bottom : neighbour.Right);
                }
            }

            if (!matched)
                continue;

            var orientation = xAxis
                ? DesignSnapGuideOrientation.Vertical
                : DesignSnapGuideOrientation.Horizontal;

            var kind = centre ? DesignSnapGuideKind.Centre : DesignSnapGuideKind.Edge;
            (guides ??= new List<DesignSnapGuide>()).Add(new DesignSnapGuide(orientation, line, start, end, kind));
        }
    }

    private static double Coordinate(Rect rect, DesignSnapGuideAlignment alignment, bool xAxis) => alignment switch
    {
        DesignSnapGuideAlignment.Near => xAxis ? rect.X : rect.Y,
        DesignSnapGuideAlignment.Centre => xAxis ? rect.X + (rect.Width / 2) : rect.Y + (rect.Height / 2),
        _ => xAxis ? rect.Right : rect.Bottom
    };

    /// <summary>
    /// Точка прямоугольника, участвующая в выравнивании по одной оси.
    /// </summary>
    private enum DesignSnapGuideAlignment
    {
        Near,
        Centre,
        Far
    }
}
