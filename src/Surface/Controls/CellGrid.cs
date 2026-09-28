using System;
using Avalonia;

namespace ArxisStudio.Surface;

/// <summary>
/// Равномерная сетка ячеек над прямоугольниками в мировых координатах — отбор видимого (ADR 0011).
/// </summary>
/// <remarks>
/// Собирается один раз на смену содержимого и дальше только читается, поэтому годится и потоку
/// отрисовки. Прямоугольник лежит во всех ячейках, которые задел, а запрос отдаёт его один раз: в
/// первой ячейке пересечения его ячеек с ячейками запроса — без отметок, которые пришлось бы
/// сбрасывать. Ячеек по большей стороне не больше <see cref="MaxCellsPerSide"/>: сетка мельче
/// стоила бы памяти больше, чем экономит обхода.
/// </remarks>
internal sealed class CellGrid
{
    /// <summary>
    /// Больше ячеек по большей стороне охвата сетка не заводит.
    /// </summary>
    internal const int MaxCellsPerSide = 64;

    /// <summary>
    /// Мельче этого ячейка не бывает, в мировых единицах.
    /// </summary>
    private const double MinCellSize = 64;

    private readonly Rect[] _rects;
    private readonly Point _origin;
    private readonly double _cell;
    private readonly int _columns;
    private readonly int _rows;

    // Ячейка c — элементы _items[_starts[c] .. _starts[c + 1]).
    private readonly int[] _starts;
    private readonly int[] _items;

    private CellGrid(Rect[] rects, Point origin, double cell, int columns, int rows, int[] starts, int[] items)
    {
        _rects = rects;
        _origin = origin;
        _cell = cell;
        _columns = columns;
        _rows = rows;
        _starts = starts;
        _items = items;
    }

    /// <summary>
    /// Прямоугольники, по которым собрана сетка; номер элемента — индекс здесь.
    /// </summary>
    public Rect[] Rects => _rects;

    /// <summary>
    /// Раскладывает прямоугольники по ячейкам.
    /// </summary>
    public static CellGrid Build(Rect[] rects)
    {
        if (rects.Length == 0)
            return new CellGrid(rects, default, MinCellSize, 1, 1, new int[2], Array.Empty<int>());

        var extent = rects[0];
        for (var i = 1; i < rects.Length; i++)
            extent = extent.Union(rects[i]);

        var cell = Math.Max(MinCellSize, Math.Max(extent.Width, extent.Height) / MaxCellsPerSide);
        var columns = Math.Max(1, (int)Math.Ceiling(extent.Width / cell) + 1);
        var rows = Math.Max(1, (int)Math.Ceiling(extent.Height / cell) + 1);
        var grid = new CellGrid(rects, extent.TopLeft, cell, columns, rows, new int[(columns * rows) + 1], Array.Empty<int>());

        // Два прохода: сперва сколько в ячейке, потом сами элементы — один массив на всю сетку.
        var counts = grid._starts;
        foreach (var rect in rects)
        {
            var (c0, r0, c1, r1) = grid.Cells(rect);
            for (var r = r0; r <= r1; r++)
            {
                for (var c = c0; c <= c1; c++)
                    counts[(r * columns) + c + 1]++;
            }
        }

        for (var i = 1; i < counts.Length; i++)
            counts[i] += counts[i - 1];

        var items = new int[counts[^1]];
        var fill = new int[columns * rows];
        for (var i = 0; i < rects.Length; i++)
        {
            var (c0, r0, c1, r1) = grid.Cells(rects[i]);
            for (var r = r0; r <= r1; r++)
            {
                for (var c = c0; c <= c1; c++)
                {
                    var index = (r * columns) + c;
                    items[counts[index] + fill[index]++] = i;
                }
            }
        }

        return new CellGrid(rects, grid._origin, cell, columns, rows, counts, items);
    }

    /// <summary>
    /// Элементы, чей прямоугольник пересекает <paramref name="world"/>, — каждый один раз.
    /// </summary>
    public Query Within(Rect world) => new(this, world);

    /// <summary>
    /// Ячейки, которые задевает прямоугольник, — в пределах сетки.
    /// </summary>
    private (int C0, int R0, int C1, int R1) Cells(Rect rect) =>
        (Column(rect.Left), Row(rect.Top), Column(rect.Right), Row(rect.Bottom));

    /// <summary>
    /// Пересекает ли прямоугольник область: касание краем не в счёт, как у <see cref="Rect.Intersects"/>,
    /// а прямоугольник без ширины или высоты — отрезок — считается, если лежит в области с краями.
    /// </summary>
    private static bool Crosses(Rect rect, Rect world) =>
        rect.Width > 0 && rect.Height > 0
            ? rect.Intersects(world)
            : rect.Left <= world.Right && world.Left <= rect.Right && rect.Top <= world.Bottom && world.Top <= rect.Bottom;

    private int Column(double x) => Math.Clamp((int)Math.Floor((x - _origin.X) / _cell), 0, _columns - 1);

    private int Row(double y) => Math.Clamp((int)Math.Floor((y - _origin.Y) / _cell), 0, _rows - 1);

    /// <summary>
    /// Обход видимого — без выделения памяти: перечислитель — структура.
    /// </summary>
    public struct Query
    {
        private readonly CellGrid _grid;
        private readonly Rect _world;
        private readonly int _c0;
        private readonly int _r0;
        private readonly int _c1;
        private readonly int _r1;
        private readonly bool _empty;
        private int _c;
        private int _r;
        private int _next;
        private int _end;

        internal Query(CellGrid grid, Rect world)
        {
            _grid = grid;
            _world = world;
            (_c0, _r0, _c1, _r1) = grid.Cells(world);

            // Запрос мимо охвата сетки не задевает ничего, а зажатые края дали бы крайние ячейки.
            var extent = new Rect(grid._origin, new Size(grid._columns * grid._cell, grid._rows * grid._cell));
            _empty = grid._rects.Length == 0 || world.Width < 0 || world.Height < 0 || !extent.Intersects(world);
            _c = _c0 - 1;
            _r = _r0;
            _next = 0;
            _end = 0;
            Current = -1;
        }

        /// <summary>
        /// Номер текущего элемента.
        /// </summary>
        public int Current { get; private set; }

        /// <summary>
        /// Для <c>foreach</c>.
        /// </summary>
        public readonly Query GetEnumerator() => this;

        /// <summary>
        /// Переходит к следующему видимому элементу.
        /// </summary>
        public bool MoveNext()
        {
            if (_empty)
                return false;

            while (true)
            {
                while (_next < _end)
                {
                    var item = _grid._items[_next++];
                    var rect = _grid._rects[item];
                    if (!Crosses(rect, _world))
                        continue;

                    // Один раз: в первой ячейке, где ячейки элемента встречают ячейки запроса.
                    var (ic0, ir0, _, _) = _grid.Cells(rect);
                    if (_c != Math.Max(ic0, _c0) || _r != Math.Max(ir0, _r0))
                        continue;

                    Current = item;
                    return true;
                }

                if (++_c > _c1)
                {
                    _c = _c0;
                    if (++_r > _r1)
                        return false;
                }

                var cell = (_r * _grid._columns) + _c;
                _next = _grid._starts[cell];
                _end = _grid._starts[cell + 1];
            }
        }
    }
}
