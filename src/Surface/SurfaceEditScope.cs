using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Surface;

/// <summary>
/// Накапливает изменения геометрии в пределах одного жеста.
/// </summary>
/// <remarks>
/// Запись ведётся не по факту чтения текущего состояния, а по значениям, которые
/// редактор задаёт: иначе итог зависел бы от того, успел ли пройти layout к моменту
/// фиксации. Исходное состояние снимается один раз, при первом обращении к target.
/// </remarks>
internal sealed class SurfaceEditScope
{
    private sealed class Entry
    {
        public Rect Before;
        public Point Position;
        public Size Size;

        public int BeforeZIndex;
        public int ZIndex;

        // Значения участников: до жеста и заданное. Порядок — порядок участников у поверхности.
        public (object? Before, object? After)[] Facets = [];
    }

    private const double Tolerance = 0.01;

    private readonly Dictionary<Control, Entry> _entries = new();
    private readonly List<Control> _order = new();

    public SurfaceEditScope(SurfaceEditKind kind) => Kind = kind;

    public SurfaceEditKind Kind { get; }

    private IReadOnlyList<IEditFacet> _facets = [];

    public void RecordPosition(SurfaceView view, Control target, Point position)
        => Touch(view, target).Position = position;

    public void RecordSize(SurfaceView view, Control target, Size size)
        => Touch(view, target).Size = size;

    public void RecordZIndex(SurfaceView view, Control target, int zIndex)
        => Touch(view, target).ZIndex = zIndex;

    public void RecordFacet(SurfaceView view, IEditFacet facet, Control target, object? value)
    {
        var entry = Touch(view, target);
        for (var i = 0; i < _facets.Count; i++)
        {
            if (ReferenceEquals(_facets[i], facet))
            {
                entry.Facets[i].After = value;
                return;
            }
        }

        throw new InvalidOperationException("Участник не зарегистрирован у поверхности.");
    }

    /// <summary>
    /// Собирает итоговый список изменений, отбрасывая те, что вернулись к исходному.
    /// </summary>
    public IReadOnlyList<TargetChange> BuildChanges(SurfaceView view)
    {
        List<TargetChange>? changes = null;

        foreach (var target in _order)
        {
            var entry = _entries[target];
            var after = new Rect(entry.Position, entry.Size);

            if (!AreClose(entry.Before, after))
            {
                (changes ??= new List<TargetChange>()).Add(
                    new GeometryChange(target, entry.Before, after));
            }

            if (entry.ZIndex != entry.BeforeZIndex)
            {
                (changes ??= new List<TargetChange>()).Add(
                    new OrderChange(target, entry.BeforeZIndex, entry.ZIndex));
            }

            for (var i = 0; i < _facets.Count; i++)
            {
                var (was, now) = entry.Facets[i];
                if (!_facets[i].AreEqual(was, now))
                {
                    (changes ??= new List<TargetChange>()).Add(
                        _facets[i].CreateChange(target, was, now));
                }
            }
        }

        if (changes == null)
            return Array.Empty<TargetChange>();

        // Элемент — на конец жеста: участники жеста выбраны, и контейнеры у них те же, что на входе.
        foreach (var change in changes)
            change.RememberItem(view);

        return changes;
    }

    private Entry Touch(SurfaceView view, Control target)
    {
        if (_entries.TryGetValue(target, out var existing))
            return existing;

        // Состав участников снимается на первом касании: он принадлежит поверхности
        // и за время жеста не меняется.
        if (_entries.Count == 0)
            _facets = view.EditFacets;

        var position = view.GetTargetPosition(target);
        var size = view.GetTargetSize(target);

        var facets = new (object? Before, object? After)[_facets.Count];
        for (var i = 0; i < facets.Length; i++)
        {
            var value = _facets[i].Read(target);
            facets[i] = (value, value);
        }

        var entry = new Entry
        {
            Before = new Rect(position, size),
            Position = position,
            Size = size,
            BeforeZIndex = target.ZIndex,
            ZIndex = target.ZIndex,
            Facets = facets
        };

        _entries[target] = entry;
        _order.Add(target);
        return entry;
    }

    private static bool AreClose(Rect a, Rect b)
        => Math.Abs(a.X - b.X) < Tolerance
           && Math.Abs(a.Y - b.Y) < Tolerance
           && Math.Abs(a.Width - b.Width) < Tolerance
           && Math.Abs(a.Height - b.Height) < Tolerance;
}
