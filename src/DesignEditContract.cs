using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio;

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
    /// просит через <see cref="DesignEditor.ReorderRequested"/>. Одно слово на два
    /// разных действия уже путало: обработчик, написанный на «изменился порядок»,
    /// молча ловил половину случаев.
    /// </remarks>
    Order,

    /// <summary>
    /// Изменение принадлежности к design-time группе.
    /// </summary>
    /// <remarks>
    /// Группа — пометка на контролах (<see cref="DesignGroup"/>), а не узел дерева:
    /// редактор его не правит. Поэтому у группировки есть шов записи и единица редактирования,
    /// в отличие от перестановки среди соседей, которая структурна и уходит запросом.
    /// </remarks>
    Group
}

/// <summary>
/// Базовое описание изменения одного design target.
/// </summary>
/// <remarks>
/// Приложению не обязательно разбирать конкретный тип: <see cref="DesignEditor.Revert"/>
/// и <see cref="DesignEditor.Reapply"/> принимают любое изменение, поэтому стек отмены
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
}

/// <summary>
/// Описывает изменение принадлежности одного design target к группе.
/// </summary>
public sealed class DesignGroupChange : DesignChange
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignGroupChange"/>.
    /// </summary>
    /// <param name="target">Изменённый контрол.</param>
    /// <param name="oldId">Группа до изменения.</param>
    /// <param name="newId">Группа после изменения.</param>
    public DesignGroupChange(Control target, string? oldId, string? newId)
        : base(target)
    {
        OldId = oldId;
        NewId = newId;
    }

    /// <summary>
    /// Получает группу до изменения.
    /// </summary>
    public string? OldId { get; }

    /// <summary>
    /// Получает группу после изменения.
    /// </summary>
    public string? NewId { get; }
}

/// <summary>
/// Аргументы запроса на перестановку контрола среди соседей.
/// </summary>
/// <remarks>
/// Деревом контролов редактор не владеет: он распознаёт жест, показывает точку
/// вставки и сообщает намерение. Саму перестановку, её запись и отмену выполняет
/// библиотека разметки. Пока запрос не помечен <see cref="Handled"/>,
/// порядок остаётся прежним.
/// <para>
/// Это структурная правка, поэтому она не попадает в <see cref="DesignEditor.EditCompleted"/>:
/// там живёт геометрия и порядок перекрытия — то, чем редактор распоряжается сам.
/// </para>
/// </remarks>
public sealed class DesignEditorReorderRequestedEventArgs : EventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignEditorReorderRequestedEventArgs"/>.
    /// </summary>
    /// <param name="target">Контрол, который требуется переставить.</param>
    /// <param name="oldIndex">Текущая позиция среди соседей.</param>
    /// <param name="newIndex">Запрошенная позиция среди соседей.</param>
    /// <param name="anchor">Сосед, перед которым встаёт контрол, либо <see langword="null"/>.</param>
    public DesignEditorReorderRequestedEventArgs(Control target, int oldIndex, int newIndex, Control? anchor)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        OldIndex = oldIndex;
        NewIndex = newIndex;
        Anchor = anchor;
    }

    /// <summary>
    /// Получает контрол, который требуется переставить.
    /// </summary>
    public Control Target { get; }

    /// <summary>
    /// Получает текущую позицию среди соседей.
    /// </summary>
    public int OldIndex { get; }

    /// <summary>
    /// Получает запрошенную позицию среди соседей.
    /// </summary>
    /// <remarks>
    /// Индекс задан <b>после</b> удаления контрола из коллекции — так его понимает
    /// <c>Children.Move</c>, и так же им пользуются оба обработчика в репозитории.
    /// Проще говоря, это итоговая позиция контрола среди соседей.
    /// </remarks>
    public int NewIndex { get; }

    /// <summary>
    /// Получает соседа, перед которым встаёт контрол, либо <see langword="null"/>,
    /// если он уходит в конец.
    /// </summary>
    /// <remarks>
    /// Ссылка, а не число, и в этом весь смысл: индекс осмыслен только против той
    /// же коллекции <c>Panel.Children</c>, которую редактор только что измерил.
    /// Библиотека разметки владеет исходным деревом, где узел может не иметь
    /// ровно одного соответствия среди визуальных детей, — по ссылке она найдёт
    /// точку вставки в своих терминах, по индексу не всегда.
    /// </remarks>
    public Control? Anchor { get; }

    /// <summary>
    /// Получает или задает признак того, что перестановка выполнена приложением.
    /// </summary>
    public bool Handled { get; set; }
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
/// <c>Layout.DesignX</c>/<c>DesignY</c> и <see cref="DesignEditor.SelectionBounds"/>.
/// Их достаточно, чтобы вернуть target в прежнее состояние через
/// <see cref="DesignEditor.ApplyGeometry"/>.
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
