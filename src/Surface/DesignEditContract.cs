using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Surface;

/// <summary>
/// Определяет вид завершённого изменения в редакторе.
/// </summary>
public enum DesignEditKind
{
    /// <summary>
    /// Перемещение одного или нескольких targets.
    /// </summary>
    Move,

    /// <summary>
    /// Изменение размера одного или нескольких targets.
    /// </summary>
    Resize,

    /// <summary>
    /// Изменение порядка перекрытия одного или нескольких targets.
    /// </summary>
    /// <remarks>
    /// Именно перекрытия, то есть <c>ZIndex</c>. Перестановка среди детей панели
    /// сюда не попадает вовсе — это структурная правка, и редактор о ней только
    /// просит через <c>DesignEditor.ReorderRequested</c>. Одно слово на два
    /// разных действия уже путало: обработчик, написанный на «изменился порядок»,
    /// молча ловил половину случаев.
    /// </remarks>
    Order,

    /// <summary>
    /// Изменение принадлежности к design-time группе.
    /// </summary>
    /// <remarks>
    /// Группа — пометка на контролах (<c>DesignGroup</c>), а не узел дерева:
    /// редактор его не правит. Поэтому у группировки есть шов записи и единица редактирования,
    /// в отличие от перестановки среди соседей, которая структурна и уходит запросом.
    /// </remarks>
    Group
}

/// <summary>
/// Базовое описание изменения одного design target.
/// </summary>
/// <remarks>
/// Приложению не обязательно разбирать конкретный тип: <see cref="SurfaceView.Revert"/>
/// и <see cref="SurfaceView.Reapply"/> принимают любое изменение, поэтому стек отмены
/// пишется единообразно.
/// </remarks>
public abstract class DesignChange
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignChange"/>.
    /// </summary>
    /// <param name="target">Изменённый контрол.</param>
    protected DesignChange(Control target)
        => Target = target ?? throw new ArgumentNullException(nameof(target));

    /// <summary>
    /// Получает изменённый контрол.
    /// </summary>
    public Control Target { get; }

    /// <summary>
    /// Применяет изменение к поверхности: возвращает состояние до него или после.
    /// </summary>
    /// <remarks>
    /// Изменение применяет себя само, поэтому у ядра нет закрытого списка видов правки:
    /// правку, которую завёл слой выше, <see cref="SurfaceView.Revert"/> отменяет той же
    /// дорогой. Вид, которого поверхность не знает, ничего не делает — так же, как и
    /// до этого шва.
    /// </remarks>
    internal virtual void ApplyTo(SurfaceView view, bool revert)
    {
    }
}

/// <summary>
/// Описывает изменение порядка перекрытия одного design target.
/// </summary>
public sealed class DesignOrderChange : DesignChange
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignOrderChange"/>.
    /// </summary>
    public DesignOrderChange(Control target, int oldZIndex, int newZIndex)
        : base(target)
    {
        OldZIndex = oldZIndex;
        NewZIndex = newZIndex;
    }

    /// <summary>
    /// Получает порядок перекрытия до изменения.
    /// </summary>
    public int OldZIndex { get; }

    /// <summary>
    /// Получает порядок перекрытия после изменения.
    /// </summary>
    public int NewZIndex { get; }

    internal override void ApplyTo(SurfaceView view, bool revert)
        => view.ApplyOrder(Target, revert ? OldZIndex : NewZIndex);
}

/// <summary>
/// Аргументы запроса на удаление выделения.
/// </summary>
/// <remarks>
/// Редактор не владеет коллекцией элементов — она приходит через <c>ItemsSource</c>,
/// поэтому удалять он не может и не должен. Клавиша Delete превращается в запрос,
/// который выполняет приложение. Пока запрос не помечен <see cref="Handled"/>,
/// нажатие считается необработанным и продолжает всплывать.
/// </remarks>
public sealed class DesignEditorDeleteRequestedEventArgs : EventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignEditorDeleteRequestedEventArgs"/>.
    /// </summary>
    /// <param name="targets">Выделенные targets на момент запроса.</param>
    public DesignEditorDeleteRequestedEventArgs(IReadOnlyList<DesignSelectionTarget> targets)
    {
        Targets = targets ?? throw new ArgumentNullException(nameof(targets));
    }

    /// <summary>
    /// Получает выделенные targets на момент запроса.
    /// </summary>
    public IReadOnlyList<DesignSelectionTarget> Targets { get; }

    /// <summary>
    /// Получает или задает признак того, что удаление выполнено приложением.
    /// </summary>
    public bool Handled { get; set; }
}

/// <summary>
/// Описывает изменение геометрии одного design target.
/// </summary>
/// <remarks>
/// Границы заданы в design-координатах: тех же, в которых работают
/// <c>Layout.DesignX</c>/<c>DesignY</c> и <c>DesignEditor.SelectionBounds</c>.
/// Их достаточно, чтобы вернуть target в прежнее состояние через
/// <see cref="SurfaceView.ApplyGeometry"/>.
/// </remarks>
public sealed class DesignGeometryChange : DesignChange
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignGeometryChange"/>.
    /// </summary>
    /// <param name="target">Изменённый контрол.</param>
    /// <param name="oldBounds">Геометрия до изменения.</param>
    /// <param name="newBounds">Геометрия после изменения.</param>
    public DesignGeometryChange(Control target, Rect oldBounds, Rect newBounds)
        : base(target)
    {
        OldBounds = oldBounds;
        NewBounds = newBounds;
    }

    /// <summary>
    /// Получает геометрию до изменения.
    /// </summary>
    public Rect OldBounds { get; }

    /// <summary>
    /// Получает геометрию после изменения.
    /// </summary>
    public Rect NewBounds { get; }

    internal override void ApplyTo(SurfaceView view, bool revert)
        => view.ApplyGeometry(Target, revert ? OldBounds : NewBounds);
}

/// <summary>
/// Аргументы завершённой единицы редактирования.
/// </summary>
/// <remarks>
/// Событие возникает один раз на жест целиком: перетаскивание пяти элементов
/// даёт одну запись с пятью изменениями, а не пять записей и не по одной на кадр.
/// Именно эта гранулярность нужна стеку undo.
/// <para>
/// Библиотека стек не ведёт — это состояние приложения. Она отвечает за то,
/// чтобы поток изменений был полным и правильно сгруппированным.
/// </para>
/// </remarks>
public sealed class DesignEditCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignEditCompletedEventArgs"/>.
    /// </summary>
    /// <param name="kind">Вид изменения.</param>
    /// <param name="changes">Изменения геометрии, вошедшие в единицу редактирования.</param>
    public DesignEditCompletedEventArgs(DesignEditKind kind, IReadOnlyList<DesignChange> changes)
    {
        Kind = kind;
        Changes = changes ?? throw new ArgumentNullException(nameof(changes));
    }

    /// <summary>
    /// Получает вид изменения.
    /// </summary>
    public DesignEditKind Kind { get; }

    /// <summary>
    /// Получает изменения геометрии, вошедшие в единицу редактирования.
    /// </summary>
    /// <remarks>
    /// Содержит только те targets, геометрия которых действительно изменилась:
    /// жест, вернувший элемент на исходное место, записи не создаёт.
    /// </remarks>
    public IReadOnlyList<DesignChange> Changes { get; }
}
