using System;
using System.Collections.Generic;
using Avalonia.Controls;
using ArxisStudio;

namespace DesignEditor.Demo;

/// <summary>
/// Стек отмены поверх контракта изменений редактора.
/// </summary>
/// <remarks>
/// Намеренно живёт в демо, а не в библиотеке: редактор отдаёт поток изменений,
/// а как их хранить и когда объединять — решает приложение.
/// <para>
/// Шагов истории два вида, и это прямое следствие границы ответственности.
/// Геометрию и порядок перекрытия правит редактор, поэтому они приходят
/// в <see cref="ArxisStudio.DesignEditor.EditCompleted"/> и откатываются через
/// <see cref="ArxisStudio.DesignEditor.Revert"/>. Перестановку среди соседей
/// выполняет приложение — значит, и записывает её оно же. Один стек на оба
/// вида обязателен: иначе структурная правка не только не отменялась бы,
/// но и не сбрасывала redo, и повтор возвращал бы геометрию поверх дерева,
/// которое с тех пор изменилось.
/// </para>
/// </remarks>
public sealed class EditHistory
{
    private readonly ArxisStudio.Surface.UiDesigner.DesignEditor _editor;
    private readonly Stack<HistoryEntry> _undo = new();
    private readonly Stack<HistoryEntry> _redo = new();

    public EditHistory(ArxisStudio.Surface.UiDesigner.DesignEditor editor)
    {
        _editor = editor;
        _editor.EditCompleted += OnEditCompleted;
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Возникает при изменении доступности отмены и повтора.
    /// </summary>
    public event EventHandler? Changed;

    public void Undo()
    {
        if (_undo.Count == 0)
            return;

        var entry = _undo.Pop();
        entry.Undo();
        _redo.Push(entry);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (_redo.Count == 0)
            return;

        var entry = _redo.Pop();
        entry.Redo();
        _undo.Push(entry);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Записывает перестановку, выполненную приложением по запросу редактора.
    /// </summary>
    /// <param name="panel">Панель, в которой изменился порядок.</param>
    /// <param name="oldIndex">Позиция до перестановки.</param>
    /// <param name="newIndex">Позиция после перестановки.</param>
    /// <remarks>
    /// <c>Move</c> обратен сам себе с переставленными аргументами, поэтому
    /// отмена и повтор — это один и тот же вызов с разным порядком индексов.
    /// </remarks>
    public void RecordReorder(Panel panel, int oldIndex, int newIndex)
    {
        if (panel == null)
            throw new ArgumentNullException(nameof(panel));

        Push(new HistoryEntry(
            () => panel.Children.Move(newIndex, oldIndex),
            () => panel.Children.Move(oldIndex, newIndex)));
    }

    private void OnEditCompleted(object? sender, DesignEditCompletedEventArgs e)
    {
        // ApplyGeometry не поднимает EditCompleted, поэтому отмена сюда не возвращается
        // и очистка redo безопасна.
        // Тип изменения разбирать не нужно: геометрия и порядок перекрытия
        // откатываются одинаково.
        Push(new HistoryEntry(
            () =>
            {
                foreach (var change in e.Changes)
                    _editor.Revert(change);
            },
            () =>
            {
                foreach (var change in e.Changes)
                    _editor.Reapply(change);
            }));
    }

    private void Push(HistoryEntry entry)
    {
        _undo.Push(entry);
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Шаг истории. Хранит действие и обратное к нему, а не описание правки:
    /// геометрию откатывает редактор, структуру — тот, кто её выполнил.
    /// </summary>
    private sealed class HistoryEntry
    {
        private readonly Action _undo;
        private readonly Action _redo;

        public HistoryEntry(Action undo, Action redo)
        {
            _undo = undo;
            _redo = redo;
        }

        public void Undo() => _undo();

        public void Redo() => _redo();
    }
}
