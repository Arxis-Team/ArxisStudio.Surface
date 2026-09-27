using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Surface;

/// <summary>
/// Определяет вид завершённого изменения в редакторе.
/// </summary>
public enum SurfaceEditKind
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
    /// просит через <c>UiDesignerView.ReorderRequested</c>. Одно слово на два
    /// разных действия уже путало: обработчик, написанный на «изменился порядок»,
    /// молча ловил половину случаев.
    /// </remarks>
    Order,

    /// <summary>
    /// Изменение принадлежности к design-time группе.
    /// </summary>
    /// <remarks>
    /// Группа — пометка на контролах (<c>SurfaceGroup</c>), а не узел дерева:
    /// редактор его не правит. Поэтому у группировки есть шов записи и единица редактирования,
    /// в отличие от перестановки среди соседей, которая структурна и уходит запросом.
    /// </remarks>
    Group
}

/// <summary>
/// Базовое описание изменения одного target.
/// </summary>
/// <remarks>
/// Приложению не обязательно разбирать конкретный тип: <see cref="SurfaceView.Revert"/>
/// и <see cref="SurfaceView.Reapply"/> принимают любое изменение, поэтому стек отмены
/// пишется единообразно.
/// </remarks>
public abstract class TargetChange
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="TargetChange"/>.
    /// </summary>
    /// <param name="target">Изменённый контрол.</param>
    protected TargetChange(Control target)
        => Target = target ?? throw new ArgumentNullException(nameof(target));

    /// <summary>
    /// Получает изменённый контрол.
    /// </summary>
    /// <remarks>
    /// У виртуализирующей поверхности контейнер элемента живёт, пока элемент виден: к отмене он может
    /// лежать в пуле или стоять на другом элементе. Отмена и повтор поэтому ищут контейнер по элементу,
    /// которому принадлежал этот (ADR 0007), а свойство остаётся тем контролом, что менялся.
    /// </remarks>
    public Control Target { get; }

    /// <summary>
    /// Элемент коллекции, чьим контейнером был <see cref="Target"/>, если он контейнер верхнего уровня.
    /// </summary>
    internal object? Item { get; private set; }

    /// <summary>
    /// Запомнен ли элемент: он сам бывает <see langword="null"/>.
    /// </summary>
    internal bool HasItem { get; private set; }

    /// <summary>
    /// Запоминает элемент, чьим контейнером сейчас служит <see cref="Target"/>.
    /// </summary>
    internal void RememberItem(SurfaceView view)
    {
        if (view.IndexFromContainer(Target) < 0)
            return;

        Item = view.ItemFromContainer(Target);
        HasItem = true;
    }

    /// <summary>
    /// Находит, к чему применять изменение: к контейнеру элемента, а не к контролу, который им был.
    /// </summary>
    /// <returns>
    /// Контейнер элемента — развёрнутый сейчас, если был свёрнут; <see langword="null"/>, если
    /// элемента в коллекции больше нет.
    /// </returns>
    internal Control? ResolveTarget(SurfaceView view)
    {
        if (!HasItem)
            return Target;

        if (view.IndexFromContainer(Target) >= 0 && Equals(view.ItemFromContainer(Target), Item))
            return Target;

        return view.RealizeItem(Item);
    }

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
/// Описывает изменение порядка перекрытия одного target.
/// </summary>
public sealed class OrderChange : TargetChange
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="OrderChange"/>.
    /// </summary>
    public OrderChange(Control target, int oldZIndex, int newZIndex)
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
    {
        if (ResolveTarget(view) is { } target)
            view.ApplyOrder(target, revert ? OldZIndex : NewZIndex);
    }
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
public sealed class SurfaceDeleteRequestedEventArgs : EventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SurfaceDeleteRequestedEventArgs"/>.
    /// </summary>
    /// <param name="targets">Выделенные targets на момент запроса.</param>
    public SurfaceDeleteRequestedEventArgs(IReadOnlyList<SurfaceSelectionTarget> targets)
    {
        Targets = targets ?? throw new ArgumentNullException(nameof(targets));
    }

    /// <summary>
    /// Получает выделенные targets на момент запроса.
    /// </summary>
    public IReadOnlyList<SurfaceSelectionTarget> Targets { get; }

    /// <summary>
    /// Получает или задает признак того, что удаление выполнено приложением.
    /// </summary>
    public bool Handled { get; set; }
}

/// <summary>
/// Описывает изменение геометрии одного target.
/// </summary>
/// <remarks>
/// Границы заданы в координатах поверхности: тех же, в которых работают
/// <c>Layout.SurfaceX</c>/<c>SurfaceY</c> и <c>UiDesignerView.SelectionBounds</c>.
/// Их достаточно, чтобы вернуть target в прежнее состояние через
/// <see cref="SurfaceView.ApplyGeometry"/>.
/// </remarks>
public sealed class GeometryChange : TargetChange
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="GeometryChange"/>.
    /// </summary>
    /// <param name="target">Изменённый контрол.</param>
    /// <param name="oldBounds">Геометрия до изменения.</param>
    /// <param name="newBounds">Геометрия после изменения.</param>
    public GeometryChange(Control target, Rect oldBounds, Rect newBounds)
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
    {
        if (ResolveTarget(view) is { } target)
            view.ApplyGeometry(target, revert ? OldBounds : NewBounds);
    }
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
public sealed class SurfaceEditCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SurfaceEditCompletedEventArgs"/>.
    /// </summary>
    /// <param name="kind">Вид изменения.</param>
    /// <param name="changes">Изменения геометрии, вошедшие в единицу редактирования.</param>
    public SurfaceEditCompletedEventArgs(SurfaceEditKind kind, IReadOnlyList<TargetChange> changes)
    {
        Kind = kind;
        Changes = changes ?? throw new ArgumentNullException(nameof(changes));
    }

    /// <summary>
    /// Получает вид изменения.
    /// </summary>
    public SurfaceEditKind Kind { get; }

    /// <summary>
    /// Получает изменения геометрии, вошедшие в единицу редактирования.
    /// </summary>
    /// <remarks>
    /// Содержит только те targets, геометрия которых действительно изменилась:
    /// жест, вернувший элемент на исходное место, записи не создаёт.
    /// </remarks>
    public IReadOnlyList<TargetChange> Changes { get; }
}
