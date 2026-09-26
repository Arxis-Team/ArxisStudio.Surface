using System;
using System.Collections.Generic;
using Avalonia;

namespace ArxisStudio.Surface.Editing;

/// <summary>
/// Подсказка о равном зазоре: отрезок, которым он показан.
/// </summary>
/// <remarks>
/// <see cref="Start"/> и <see cref="End"/> — границы зазора по его оси,
/// <see cref="Position"/> — координата, на которой отрезок рисуется по другой оси.
/// </remarks>
public readonly struct DesignSpacingHint : IEquatable<DesignSpacingHint>
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignSpacingHint"/>.
    /// </summary>
    /// <param name="orientation">Ось, вдоль которой измерен зазор.</param>
    /// <param name="start">Начало зазора.</param>
    /// <param name="end">Конец зазора.</param>
    /// <param name="position">Координата отрисовки по другой оси.</param>
    public DesignSpacingHint(DesignSnapGuideOrientation orientation, double start, double end, double position)
    {
        Orientation = orientation;
        Start = start;
        End = end;
        Position = position;
    }

    /// <summary>Получает ось, вдоль которой измерен зазор.</summary>
    /// <remarks>
    /// <see cref="DesignSnapGuideOrientation.Vertical"/> означает зазор по оси X —
    /// та же конвенция, что и у направляющей, которая при этом вертикальна.
    /// </remarks>
    public DesignSnapGuideOrientation Orientation { get; }

    /// <summary>Получает начало зазора.</summary>
    public double Start { get; }

    /// <summary>Получает конец зазора.</summary>
    public double End { get; }

    /// <summary>Получает координату отрисовки по другой оси.</summary>
    public double Position { get; }

    /// <inheritdoc />
    public bool Equals(DesignSpacingHint other)
        => Orientation == other.Orientation
           && Start.Equals(other.Start)
           && End.Equals(other.End)
           && Position.Equals(other.Position);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DesignSpacingHint other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Orientation, Start, End, Position);
}

/// <summary>
/// Считает положение, при котором зазоры вокруг элемента становятся равными.
/// </summary>
/// <remarks>
/// Здесь, как и в <see cref="DesignSnapGuideResolver"/>, только арифметика: соседи
/// приходят готовыми прямоугольниками в мировых координатах.
/// <para>
/// Отличие от выравнивания в том, что сравниваются <b>зазоры</b>, а не координаты.
/// Отсюда два требования, которых у выравнивания нет. Соседом считается только тот,
/// кто перекрывается с элементом по <b>другой</b> оси: «слева» и «справа» осмысленны
/// внутри одного ряда, а коробка этажом выше в ряд не входит. И соседи с нулевой
/// площадью исключаются — интервал бывает между элементами, а не до линии.
/// </para>
/// <para>
/// Равенство достигается двумя разными способами, и оба нужны. <b>Посередине</b> —
/// зазоры до ближайших соседей слева и справа равны друг другу. <b>Повтор шага</b> —
/// зазор до ближайшего соседа равен тому, который уже стоит между двумя соседями
/// дальше по ряду; так элемент продолжает существующий ритм, даже когда с другой
/// стороны от него никого нет. Кандидатов на ось поэтому несколько, и побеждает
/// ближайший — то же правило, что и у выравнивания.
/// </para>
/// </remarks>
internal static class DesignSpacingResolver
{
    /// <summary>
    /// Допуск совпадения зазоров при сборе подсказок: смещение уже применено,
    /// и остаётся только погрешность double.
    /// </summary>
    private const double Epsilon = 0.01;

    /// <summary>
    /// Ищет смещение, уравнивающее зазоры вокруг элемента.
    /// </summary>
    /// <param name="moving">Прямоугольник в предполагаемой позиции.</param>
    /// <param name="neighbours">Соседи в мировых координатах.</param>
    /// <param name="tolerance">Радиус захвата в мировых единицах.</param>
    /// <param name="offset">Смещение, уравнивающее зазоры.</param>
    /// <param name="spacedX">Признак того, что интервал найден по оси X.</param>
    /// <param name="spacedY">Признак того, что интервал найден по оси Y.</param>
    /// <returns><see langword="true"/>, если интервал найден хотя бы по одной оси.</returns>
    internal static bool TryResolveOffset(
        Rect moving,
        IReadOnlyList<Rect> neighbours,
        double tolerance,
        out Vector offset,
        out bool spacedX,
        out bool spacedY)
    {
        spacedX = TryResolveAxis(moving, neighbours, tolerance, xAxis: true, out var dx);
        spacedY = TryResolveAxis(moving, neighbours, tolerance, xAxis: false, out var dy);

        offset = new Vector(spacedX ? dx : 0, spacedY ? dy : 0);
        return spacedX || spacedY;
    }

    /// <summary>
    /// Ищет координату двигающегося края, при которой зазоры становятся равными.
    /// </summary>
    /// <param name="proposed">Предполагаемая геометрия элемента.</param>
    /// <param name="neighbours">Соседи в мировых координатах.</param>
    /// <param name="tolerance">Радиус захвата в мировых единицах.</param>
    /// <param name="xAxis">Признак горизонтальной оси.</param>
    /// <param name="farEdge">Признак того, что двигается дальний край.</param>
    /// <param name="resolved">Найденная координата края.</param>
    /// <returns><see langword="true"/>, если координата найдена.</returns>
    /// <remarks>
    /// При перетаскивании элемент двигается целиком, и оба зазора считаются заново.
    /// При resize неподвижный край остаётся на месте, поэтому его зазор задан
    /// и остаётся один вопрос: где должен оказаться двигающийся край. Отсюда
    /// отдельная точка входа — ровно так же устроена разница между перетаскиванием
    /// и resize у направляющих.
    /// </remarks>
    internal static bool TryResolveEdge(
        Rect proposed,
        IReadOnlyList<Rect> neighbours,
        double tolerance,
        bool xAxis,
        bool farEdge,
        out double resolved)
    {
        resolved = 0;

        var row = BuildRow(proposed, neighbours, xAxis);
        var near = xAxis ? proposed.X : proposed.Y;
        var far = xAxis ? proposed.Right : proposed.Bottom;
        var edge = farEdge ? far : near;

        var best = double.PositiveInfinity;
        var winner = 0.0;
        var found = false;

        void Consider(double target)
        {
            // Схлопывать элемент ради равенства нельзя: это уже не изменение размера.
            if (farEdge ? target <= near : target >= far)
                return;

            var distance = Math.Abs(target - edge);
            if (distance > tolerance || distance >= best)
                return;

            best = distance;
            winner = target;
            found = true;
        }

        if (farEdge)
        {
            // Неподвижен ближний край: его зазор задан, дальний обязан стать таким же.
            if (row.HasBefore && row.HasAfter && near - row.BeforeEnd >= 0)
                Consider(row.AfterStart - (near - row.BeforeEnd));

            // Либо край подхватывает шаг, который уже стоит дальше по ряду.
            if (row.TryGetAfterStep(out var afterStep))
                Consider(row.AfterStart - afterStep);
        }
        else
        {
            if (row.HasBefore && row.HasAfter && row.AfterStart - far >= 0)
                Consider(row.BeforeEnd + (row.AfterStart - far));

            if (row.TryGetBeforeStep(out var beforeStep))
                Consider(row.BeforeEnd + beforeStep);
        }

        resolved = winner;
        return found;
    }

    /// <summary>
    /// Собирает подсказки о равных зазорах вокруг применённой позиции.
    /// </summary>
    /// <param name="bounds">Применённая геометрия элемента.</param>
    /// <param name="neighbours">Соседи в мировых координатах.</param>
    /// <remarks>
    /// Показывается не «зазор рядом с элементом», а цепочка равных зазоров, в которую
    /// он попал: два подряд при положении посередине, два и больше при повторе шага.
    /// Одинокий зазор ничего не значит и не рисуется.
    /// </remarks>
    internal static IReadOnlyList<DesignSpacingHint> CollectHints(Rect bounds, IReadOnlyList<Rect> neighbours)
    {
        List<DesignSpacingHint>? hints = null;

        CollectAxis(bounds, neighbours, xAxis: true, ref hints);
        CollectAxis(bounds, neighbours, xAxis: false, ref hints);

        return (IReadOnlyList<DesignSpacingHint>?)hints ?? Array.Empty<DesignSpacingHint>();
    }

    private static bool TryResolveAxis(
        Rect moving,
        IReadOnlyList<Rect> neighbours,
        double tolerance,
        bool xAxis,
        out double delta)
    {
        delta = 0;

        var row = BuildRow(moving, neighbours, xAxis);
        var start = xAxis ? moving.X : moving.Y;
        var size = xAxis ? moving.Width : moving.Height;

        var best = double.PositiveInfinity;
        var winner = 0.0;
        var found = false;

        void Consider(double target)
        {
            var candidate = target - start;
            var distance = Math.Abs(candidate);
            if (distance > tolerance || distance >= best)
                return;

            best = distance;
            winner = candidate;
            found = true;
        }

        // Посередине: свободное место между ближайшими соседями делится пополам.
        if (row.HasBefore && row.HasAfter)
        {
            var free = row.AfterStart - row.BeforeEnd - size;
            if (free >= 0)
                Consider(row.BeforeEnd + (free / 2));
        }

        // Повтор шага, который уже стоит слева или справа по ряду.
        if (row.TryGetBeforeStep(out var beforeStep))
            Consider(row.BeforeEnd + beforeStep);

        if (row.TryGetAfterStep(out var afterStep))
            Consider(row.AfterStart - afterStep - size);

        delta = winner;
        return found;
    }

    private static void CollectAxis(
        Rect bounds,
        IReadOnlyList<Rect> neighbours,
        bool xAxis,
        ref List<DesignSpacingHint>? hints)
    {
        var row = BuildRow(bounds, neighbours, xAxis);

        // Элемент встаёт в ряд между соседями до него и после него: получается
        // последовательность, у которой можно взять зазоры подряд.
        var items = new List<Rect>(row.Before.Count + row.After.Count + 1);
        items.AddRange(row.Before);
        var index = items.Count;
        items.Add(bounds);
        items.AddRange(row.After);

        if (items.Count < 3)
            return;

        var gaps = new double[items.Count - 1];
        for (var i = 0; i < gaps.Length; i++)
            gaps[i] = Start(items[i + 1], xAxis) - End(items[i], xAxis);

        var shown = new bool[gaps.Length];

        // Цепочка ищется от зазоров, прилегающих к элементу: равенство где-то
        // в стороне к нему не относится и подсказкой быть не должно.
        MarkChain(gaps, shown, index - 1);
        MarkChain(gaps, shown, index);

        for (var i = 0; i < gaps.Length; i++)
        {
            if (!shown[i])
                continue;

            var orientation = xAxis
                ? DesignSnapGuideOrientation.Vertical
                : DesignSnapGuideOrientation.Horizontal;

            hints ??= new List<DesignSpacingHint>();
            hints.Add(new DesignSpacingHint(
                orientation,
                End(items[i], xAxis),
                Start(items[i + 1], xAxis),
                CrossCentre(bounds, items[i], items[i + 1], xAxis)));
        }
    }

    /// <summary>
    /// Помечает максимальную цепочку равных зазоров, содержащую указанный.
    /// </summary>
    /// <remarks>
    /// Цепочка короче двух не показывается: одинокий зазор ничего не сообщает,
    /// равенство начинается со второго.
    /// </remarks>
    private static void MarkChain(double[] gaps, bool[] shown, int seed)
    {
        if (seed < 0 || seed >= gaps.Length || gaps[seed] < 0)
            return;

        var value = gaps[seed];
        var from = seed;
        var to = seed;

        while (from > 0 && Math.Abs(gaps[from - 1] - value) <= Epsilon)
            from--;

        while (to < gaps.Length - 1 && Math.Abs(gaps[to + 1] - value) <= Epsilon)
            to++;

        if (to - from < 1)
            return;

        for (var i = from; i <= to; i++)
            shown[i] = true;
    }

    /// <summary>
    /// Соседи одного ряда, разделённые на стоящих до элемента и после него.
    /// </summary>
    private readonly struct Row
    {
        public Row(List<Rect> before, List<Rect> after, bool xAxis)
        {
            Before = before;
            After = after;
            XAxis = xAxis;
        }

        /// <summary>Соседи до элемента, по возрастанию дальнего края.</summary>
        public List<Rect> Before { get; }

        /// <summary>Соседи после элемента, по возрастанию ближнего края.</summary>
        public List<Rect> After { get; }

        private bool XAxis { get; }

        public bool HasBefore => Before.Count > 0;

        public bool HasAfter => After.Count > 0;

        /// <summary>Дальний край ближайшего соседа до элемента.</summary>
        public double BeforeEnd => End(Before[^1], XAxis);

        /// <summary>Ближний край ближайшего соседа после элемента.</summary>
        public double AfterStart => Start(After[0], XAxis);

        /// <summary>Шаг, уже стоящий между двумя ближайшими соседями до элемента.</summary>
        public bool TryGetBeforeStep(out double step)
        {
            step = 0;
            if (Before.Count < 2)
                return false;

            step = Start(Before[^1], XAxis) - End(Before[^2], XAxis);
            return step >= 0;
        }

        /// <summary>Шаг, уже стоящий между двумя ближайшими соседями после элемента.</summary>
        public bool TryGetAfterStep(out double step)
        {
            step = 0;
            if (After.Count < 2)
                return false;

            step = Start(After[1], XAxis) - End(After[0], XAxis);
            return step >= 0;
        }
    }

    /// <summary>
    /// Собирает ряд: соседей, лежащих с элементом на одной линии и не пересекающих его.
    /// </summary>
    private static Row BuildRow(Rect moving, IReadOnlyList<Rect> neighbours, bool xAxis)
    {
        var before = new List<Rect>();
        var after = new List<Rect>();

        var start = xAxis ? moving.X : moving.Y;
        var end = xAxis ? moving.Right : moving.Bottom;

        for (var i = 0; i < neighbours.Count; i++)
        {
            var neighbour = neighbours[i];

            // Интервал бывает между элементами: линия нулевой площади соседом не считается.
            if (neighbour.Width <= 0 || neighbour.Height <= 0)
                continue;

            if (!OverlapsAcross(moving, neighbour, xAxis))
                continue;

            if (End(neighbour, xAxis) <= start)
                before.Add(neighbour);
            else if (Start(neighbour, xAxis) >= end)
                after.Add(neighbour);
        }

        before.Sort((a, b) => End(a, xAxis).CompareTo(End(b, xAxis)));
        after.Sort((a, b) => Start(a, xAxis).CompareTo(Start(b, xAxis)));

        return new Row(before, after, xAxis);
    }

    private static double Start(Rect rect, bool xAxis) => xAxis ? rect.X : rect.Y;

    private static double End(Rect rect, bool xAxis) => xAxis ? rect.Right : rect.Bottom;

    private static bool OverlapsAcross(Rect moving, Rect neighbour, bool xAxis) => xAxis
        ? neighbour.Y < moving.Bottom && neighbour.Bottom > moving.Y
        : neighbour.X < moving.Right && neighbour.Right > moving.X;

    /// <summary>
    /// Координата, на которой рисуется отрезок зазора.
    /// </summary>
    /// <remarks>
    /// Берётся середина полосы, общей у элемента и двух ограничивающих зазор соседей:
    /// только там отрезок читается как измерение, а не как ещё одна линия у края.
    /// Если общей полосы нет, остаётся середина самого элемента — он в кадре всегда.
    /// </remarks>
    private static double CrossCentre(Rect bounds, Rect first, Rect second, bool xAxis)
    {
        var low = xAxis
            ? Math.Max(bounds.Y, Math.Max(first.Y, second.Y))
            : Math.Max(bounds.X, Math.Max(first.X, second.X));

        var high = xAxis
            ? Math.Min(bounds.Bottom, Math.Min(first.Bottom, second.Bottom))
            : Math.Min(bounds.Right, Math.Min(first.Right, second.Right));

        if (low > high)
        {
            return xAxis
                ? bounds.Y + (bounds.Height / 2)
                : bounds.X + (bounds.Width / 2);
        }

        return low + ((high - low) / 2);
    }
}
