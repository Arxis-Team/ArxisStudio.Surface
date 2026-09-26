using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DesignLayout = ArxisStudio.Surface.UiDesigner.Layout;
using DesignInteraction = ArxisStudio.Surface.Editing.DesignInteraction;
using ArxisStudio.Controls;
using ArxisStudio.Surface.Editing;
using ArxisStudio.Surface.UiDesigner.Placement;
using ArxisStudio.States;

namespace ArxisStudio.Surface.UiDesigner;

// Контракт изменений: единица редактирования, отмена и повтор.
// Часть DesignEditor; общее описание типа — в DesignEditor.cs.
public partial class DesignEditor
{
    /// <summary>
    /// Отменяет изменение, возвращая target в состояние до него.
    /// </summary>
    /// <param name="change">Изменение из <see cref="DesignEditCompletedEventArgs.Changes"/>.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="change"/> равен <see langword="null"/>.</exception>
    /// <remarks>
    /// Разбирать конкретный тип изменения приложению не нужно: стек отмены пишется
    /// одинаково для геометрии и для порядка перекрытия.
    /// </remarks>
    public void Revert(DesignChange change) => Apply(change, revert: true);

    /// <summary>
    /// Повторяет ранее отменённое изменение.
    /// </summary>
    /// <param name="change">Изменение из <see cref="DesignEditCompletedEventArgs.Changes"/>.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="change"/> равен <see langword="null"/>.</exception>
    public void Reapply(DesignChange change) => Apply(change, revert: false);

    private void Apply(DesignChange change, bool revert)
    {
        if (change == null)
            throw new ArgumentNullException(nameof(change));

        switch (change)
        {
            case DesignGeometryChange geometry:
                ApplyGeometry(geometry.Target, revert ? geometry.OldBounds : geometry.NewBounds);
                break;

            case DesignOrderChange order:
                ApplyOrder(order.Target, revert ? order.OldZIndex : order.NewZIndex);
                break;

            case DesignGroupChange group:
                ApplyGroup(group.Target, revert ? group.OldId : group.NewId);
                break;

        }
    }

    /// <summary>
    /// Задаёт порядок перекрытия, не создавая новой единицы редактирования.
    /// </summary>
    /// <param name="target">Контрол, порядок которого нужно задать.</param>
    /// <param name="zIndex">Новое значение порядка.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="target"/> равен <see langword="null"/>.</exception>
    public void ApplyOrder(Control target, int zIndex)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        var previous = _suppressEditRecording;
        _suppressEditRecording = true;
        try
        {
            SetDesignZIndex(target, zIndex);
        }
        finally
        {
            _suppressEditRecording = previous;
        }
    }

    /// <summary>
    /// Применяет геометрию к target, не создавая новой единицы редактирования.
    /// </summary>
    /// <param name="target">Контрол, геометрию которого нужно задать.</param>
    /// <param name="bounds">Целевая геометрия в design-координатах.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="target"/> равен <see langword="null"/>.</exception>
    /// <remarks>
    /// Предназначен для отмены и повтора: принимает <see cref="DesignGeometryChange.OldBounds"/>
    /// или <see cref="DesignGeometryChange.NewBounds"/> напрямую. Запись изменений на время
    /// вызова подавляется, поэтому отмена не порождает новую запись в стеке.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// foreach (var change in edit.Changes)
    ///     editor.ApplyGeometry(change.Target, change.OldBounds);
    /// ]]></code>
    /// </example>
    public void ApplyGeometry(Control target, Rect bounds)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        var previous = _suppressEditRecording;
        _suppressEditRecording = true;
        try
        {
            SetDesignSize(target, bounds.Size);
            SetDesignPosition(target, bounds.Position);
        }
        finally
        {
            _suppressEditRecording = previous;
        }

        UpdateSelectionOverlayState();
    }

    /// <summary>
    /// Открывает единицу редактирования. Вызывается на старте жеста, до первой мутации.
    /// </summary>
    /// <summary>
    /// Признак открытой единицы редактирования.
    /// </summary>
    /// <remarks>
    /// По нему состояние перетаскивания понимает, принят жест или отклонён:
    /// <c>e.Handled</c> для этого не годится — редактор ставит его на всех ветках,
    /// включая успешную. Открытая единица есть только на успешной.
    /// </remarks>
    internal bool HasActiveEdit => _activeEdit != null;

    /// <summary>
    /// Открывает единицу редактирования.
    /// </summary>
    /// <remarks>
    /// Осиротевшая единица не затирается, а фиксируется. Раньше здесь стояло простое
    /// присваивание: если предыдущий жест закончился, не закрыв свою единицу, — а он
    /// может, у трёх завершений resize есть ранние выходы до <see cref="CommitEdit"/>, —
    /// то следующий жест молча уничтожал её, и правка пользователя исчезала из undo
    /// без единого признака. Поздняя запись хуже своевременной, но несравнимо лучше
    /// потерянной.
    /// </remarks>
    private void BeginEdit(DesignEditKind kind)
    {
        if (_activeEdit != null)
            CommitEdit();

        _activeEdit = new DesignEditScope(kind);
    }

    /// <summary>
    /// Закрывает единицу редактирования и публикует изменения, если они есть.
    /// </summary>
    /// <returns><see langword="true"/>, если изменения были опубликованы.</returns>
    /// <remarks>
    /// Ответ нужен точке записи снаружи жеста: <see cref="SetDesignGeometry"/> обязан
    /// сказать хосту, приняли его правку или раскладка её отсекла, а перечитать
    /// design-координаты сразу нельзя — они отстают на проход диспетчера.
    /// </remarks>
    private bool CommitEdit()
    {
        var scope = _activeEdit;
        _activeEdit = null;

        if (scope == null)
            return false;

        var changes = scope.BuildChanges();
        if (changes.Count == 0)
            return false;

        EditCompleted?.Invoke(this, new DesignEditCompletedEventArgs(scope.Kind, changes));
        return true;
    }

    /// <summary>
    /// Отбрасывает единицу редактирования, не публикуя изменения.
    /// </summary>
    private void CancelEdit() => _activeEdit = null;
}
