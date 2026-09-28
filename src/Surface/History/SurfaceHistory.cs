using System;
using System.Collections.Generic;

namespace ArxisStudio.Surface;

/// <summary>
/// Изменение, которое умеет отменить и повторить себя.
/// </summary>
/// <remarks>
/// Контракт истории, общий для поверхности и хоста. Правки поверхности — геометрию,
/// порядок перекрытия и то, что приносят слои выше, — <see cref="SurfaceHistory"/>
/// заворачивает в такие изменения сама. Структурные правки — добавление, удаление,
/// перестановку среди соседей — поверхность не выполняет (ADR 0001): их выполняет хост
/// и кладёт в ту же историю своей реализацией этого интерфейса.
/// </remarks>
public interface ISurfaceChange
{
    /// <summary>
    /// Возвращает состояние до изменения.
    /// </summary>
    void Revert();

    /// <summary>
    /// Возвращает состояние после изменения.
    /// </summary>
    void Reapply();
}

/// <summary>
/// Готовый стек отмены и повтора для поверхности.
/// </summary>
/// <remarks>
/// Необязателен (ADR 0003): стека у поверхности нет и быть не может — она сообщает о своих
/// правках через <see cref="SurfaceView.EditCompleted"/> и умеет применить одну из них,
/// а что отменять и в каком порядке, знает только тот, кто копил. Этот класс и есть такой
/// копящий: одна единица редактирования — одна запись, а сочетания отмены и повтора
/// поверхности (<see cref="SurfaceView.UndoRequested"/>, <see cref="SurfaceView.RedoRequested"/>)
/// он обслуживает сам. Хосту со своей историей он не нужен.
/// <para>
/// Структурные правки хост кладёт через <see cref="Push"/>: тогда отмена возвращает их
/// в том же порядке, что и правки поверхности.
/// </para>
/// <para>
/// Отмена и повтор применяют правки через <see cref="SurfaceView.Revert"/> и
/// <see cref="SurfaceView.Reapply"/>, а те запись подавляют: история не пишет сама в себя.
/// </para>
/// </remarks>
public sealed class SurfaceHistory : IDisposable
{
    private readonly SurfaceView _view;
    private readonly Stack<ISurfaceChange> _undo = new();
    private readonly Stack<ISurfaceChange> _redo = new();
    private bool _disposed;

    /// <summary>
    /// Подключает историю к поверхности.
    /// </summary>
    /// <param name="view">Поверхность, правки которой копятся.</param>
    /// <exception cref="ArgumentNullException"><paramref name="view"/> равен <see langword="null"/>.</exception>
    public SurfaceHistory(SurfaceView view)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _view.EditCompleted += OnEditCompleted;
        _view.UndoRequested += OnUndoRequested;
        _view.RedoRequested += OnRedoRequested;
    }

    /// <summary>
    /// Есть ли что отменить.
    /// </summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>
    /// Есть ли что повторить.
    /// </summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Возникает, когда меняется состав истории.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Кладёт в историю изменение, которое хост выполнил сам.
    /// </summary>
    /// <param name="change">Уже выполненное изменение.</param>
    /// <remarks>
    /// Как и всякая новая правка, очищает повтор: ветка, от которой ушли, больше не
    /// описывает текущее состояние.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="change"/> равен <see langword="null"/>.</exception>
    public void Push(ISurfaceChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        _undo.Push(change);
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Отменяет последнее изменение.
    /// </summary>
    /// <returns><see langword="true"/>, если было что отменить.</returns>
    public bool Undo()
    {
        if (_undo.Count == 0)
            return false;

        var change = _undo.Pop();
        change.Revert();
        _redo.Push(change);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Повторяет последнее отменённое изменение.
    /// </summary>
    /// <returns><see langword="true"/>, если было что повторить.</returns>
    public bool Redo()
    {
        if (_redo.Count == 0)
            return false;

        var change = _redo.Pop();
        change.Reapply();
        _undo.Push(change);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Забывает всю историю — например, после загрузки нового документа.
    /// </summary>
    public void Clear()
    {
        if (_undo.Count == 0 && _redo.Count == 0)
            return;

        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Отключает историю от поверхности.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _view.EditCompleted -= OnEditCompleted;
        _view.UndoRequested -= OnUndoRequested;
        _view.RedoRequested -= OnRedoRequested;
    }

    private void OnEditCompleted(object? sender, SurfaceEditCompletedEventArgs e)
        => Push(new EditTransaction(_view, e.Changes, e.ItemChanges));

    // Нажатие забирается, только если история действительно что-то сделала: иначе
    // сочетание уходит дальше, как у поверхности без подписчика.
    private void OnUndoRequested(object? sender, SurfaceHistoryRequestedEventArgs e)
    {
        if (!e.Handled)
            e.Handled = Undo();
    }

    private void OnRedoRequested(object? sender, SurfaceHistoryRequestedEventArgs e)
    {
        if (!e.Handled)
            e.Handled = Redo();
    }

    /// <summary>
    /// Единица редактирования поверхности как одно изменение истории.
    /// </summary>
    /// <remarks>
    /// Отмена идёт в обратном порядке, повтор — в прямом: так возвращаются и правки,
    /// которые зависят друг от друга, а независимым порядок безразличен.
    /// </remarks>
    private sealed class EditTransaction : ISurfaceChange
    {
        private readonly SurfaceView _view;
        private readonly IReadOnlyList<TargetChange> _changes;
        private readonly IReadOnlyList<ItemMoveChange> _itemChanges;

        public EditTransaction(SurfaceView view, IReadOnlyList<TargetChange> changes, IReadOnlyList<ItemMoveChange> itemChanges)
        {
            _view = view;
            _changes = changes;
            _itemChanges = itemChanges;
        }

        public void Revert()
        {
            for (var i = _itemChanges.Count - 1; i >= 0; i--)
                _view.RevertMove(_itemChanges[i]);

            for (var i = _changes.Count - 1; i >= 0; i--)
                _view.Revert(_changes[i]);
        }

        public void Reapply()
        {
            for (var i = 0; i < _changes.Count; i++)
                _view.Reapply(_changes[i]);

            for (var i = 0; i < _itemChanges.Count; i++)
                _view.ReapplyMove(_itemChanges[i]);
        }
    }
}
