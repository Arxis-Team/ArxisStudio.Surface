using ArxisStudio.Surface;

namespace Nodes.StateMachine.Machine;

/// <summary>
/// Правка коллекции приложения, положенная в ту же историю, что и правки редактора, — как у демо узлов.
/// </summary>
/// <remarks>
/// Структуру графа правит приложение (ADR 0001 библиотеки): редактор просит, а выполняет и кладёт в
/// <see cref="SurfaceHistory"/> тот, кто владеет коллекциями. Места запоминаются, поэтому отмена
/// возвращает элемент туда, где он стоял.
/// </remarks>
internal sealed class ListEdit<T> : ISurfaceChange
{
    private readonly IList<T> _list;
    private readonly IReadOnlyList<(int Index, T Item)> _removed;
    private readonly IReadOnlyList<(int Index, T Item)> _added;

    public ListEdit(IList<T> list, IReadOnlyList<(int Index, T Item)> removed, IReadOnlyList<(int Index, T Item)> added)
    {
        _list = list;
        _removed = removed;
        _added = added;
    }

    public static ListEdit<T> Added(IList<T> list, int index, T item) => new(list, [], [(index, item)]);

    public static ListEdit<T> Replaced(IList<T> list, int index, T before, T after) =>
        new(list, [(index, before)], [(index, after)]);

    /// <summary>
    /// Убирает элементы из коллекции и возвращает правку, которая это помнит.
    /// </summary>
    public static ListEdit<T> Remove(IList<T> list, IEnumerable<T> items)
    {
        var doomed = new HashSet<T>(items);
        var removed = new List<(int Index, T Item)>(doomed.Count);
        for (var i = 0; i < list.Count; i++)
        {
            if (doomed.Contains(list[i]))
                removed.Add((i, list[i]));
        }

        var edit = new ListEdit<T>(list, removed, []);
        edit.Reapply();
        return edit;
    }

    public bool IsEmpty => _removed.Count == 0 && _added.Count == 0;

    public void Revert()
    {
        for (var i = _added.Count - 1; i >= 0; i--)
            _list.RemoveAt(_added[i].Index);

        foreach (var (index, item) in _removed)
            _list.Insert(index, item);
    }

    public void Reapply()
    {
        for (var i = _removed.Count - 1; i >= 0; i--)
            _list.RemoveAt(_removed[i].Index);

        foreach (var (index, item) in _added)
            _list.Insert(index, item);
    }
}

/// <summary>
/// Несколько правок одной записью: отмена идёт в обратном порядке.
/// </summary>
/// <remarks>
/// Удаление узла уносит и его связи: связи убираются первыми, а возвращаются последними — пока узла
/// нет, их концам не к чему прицепиться.
/// </remarks>
internal sealed class CompositeChange(params ISurfaceChange[] changes) : ISurfaceChange
{
    public void Revert()
    {
        for (var i = changes.Length - 1; i >= 0; i--)
            changes[i].Revert();
    }

    public void Reapply()
    {
        foreach (var change in changes)
            change.Reapply();
    }
}

/// <summary>
/// Смена свойства узла из контекстного меню — экрана состояния или условия перехода.
/// </summary>
internal sealed class PropertyChange<T>(Action<T> set, T before, T after) : ISurfaceChange
{
    public void Revert() => set(before);

    public void Reapply() => set(after);
}
