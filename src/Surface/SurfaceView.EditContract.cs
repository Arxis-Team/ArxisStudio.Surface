using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Surface;

// Контракт изменений: швы записи геометрии, единица редактирования, отмена и повтор.
// Часть SurfaceView; общее описание типа — в SurfaceView.cs.
public partial class SurfaceView
{
    // Текущая единица редактирования. Живёт от начала жеста до его завершения:
    // все мутации проходят через SetDesignPosition/SetDesignSize и попадают в неё.
    private DesignEditScope? _activeEdit;

    private ISurfaceGeometry? _geometry;

    private readonly List<IEditFacet> _editFacets = new();

    // Подавляет запись на время программного применения геометрии,
    // чтобы отмена не превращалась в новое изменение.
    private protected bool _suppressEditRecording;

    /// <summary>
    /// Возникает после завершения единицы редактирования — перемещения или изменения размера.
    /// </summary>
    /// <remarks>
    /// Одно событие на жест целиком, а не на кадр: это та гранулярность, в которой
    /// изменения кладутся в стек undo. Жест, не изменивший геометрию, события не вызывает.
    /// <para>
    /// Стек отмены библиотека не ведёт: она отдаёт поток изменений, а хранит его приложение.
    /// Вернуть состояние можно через <see cref="ApplyGeometry"/>.
    /// </para>
    /// </remarks>
    public event EventHandler<DesignEditCompletedEventArgs>? EditCompleted;

    /// <summary>
    /// Геометрия target'ов: где они и можно ли задать им позицию.
    /// </summary>
    /// <remarks>
    /// Шов между ядром и тем, что лежит на холсте (ADR 0003). Ядро знает только
    /// контейнеры и их <see cref="SurfaceItem.Location"/>; дизайнер форм подставляет
    /// геометрию, которая спрашивает стратегию размещения у родительской панели.
    /// </remarks>
    internal ISurfaceGeometry Geometry
    {
        get => _geometry ??= new SurfaceItemGeometry();
        private protected set => _geometry = value;
    }

    /// <summary>
    /// Участники единицы редактирования сверх геометрии и порядка перекрытия.
    /// </summary>
    internal IReadOnlyList<IEditFacet> EditFacets => _editFacets;

    /// <summary>
    /// Добавляет участника единицы редактирования.
    /// </summary>
    /// <remarks>
    /// Порядок добавления — порядок, в котором изменения участников попадают в
    /// <see cref="DesignEditCompletedEventArgs.Changes"/> после геометрии и порядка.
    /// </remarks>
    private protected void AddEditFacet(IEditFacet facet) => _editFacets.Add(facet);

    /// <summary>
    /// Записывает значение участника в открытую единицу редактирования.
    /// </summary>
    private protected void RecordEdit(IEditFacet facet, Control target, object? value)
    {
        if (!_suppressEditRecording)
            _activeEdit?.RecordFacet(this, facet, target, value);
    }

    /// <summary>
    /// Пересобирает оверлей выделения после того, как геометрия применена снаружи жеста.
    /// </summary>
    /// <remarks>
    /// Оверлей пока живёт у дизайнера форм; ядро только зовёт его в тех же местах,
    /// что и раньше.
    /// </remarks>
    private protected virtual void RefreshSelectionOverlay()
    {
    }

    internal Point GetDesignPosition(Control control)
        => Geometry.GetPosition(control);

    /// <summary>
    /// Задаёт позицию target'а в design-координатах.
    /// </summary>
    /// <remarks>
    /// Раскладка, которая владеет позицией ребёнка, отсекается здесь, а не выше:
    /// это единственная точка записи, поэтому только тут можно гарантировать,
    /// что в контракт изменений не попадёт перемещение, которого не произошло.
    /// </remarks>
    internal void SetDesignPosition(Control control, Point position)
    {
        if (!Geometry.CanSetPosition(control))
            return;

        if (!_suppressEditRecording)
            _activeEdit?.RecordPosition(this, control, position);

        Geometry.SetPosition(control, position);
    }

    /// <summary>
    /// Задает геометрию контрола одной единицей редактирования.
    /// </summary>
    /// <param name="target">Контрол.</param>
    /// <param name="bounds">Желаемая рамка в design-координатах.</param>
    /// <returns><see langword="true"/>, если изменение принято и опубликовано.</returns>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="target"/> равен <see langword="null"/>.</exception>
    /// <remarks>
    /// Это способ изменить геометрию <b>снаружи жеста</b> — из панели свойств, из
    /// команды приложения. Идёт через те же швы, что и перетаскивание, поэтому правка
    /// попадает в <see cref="EditCompleted"/> и отменяется наравне с ней.
    /// <para>
    /// Отличие от <see cref="ApplyGeometry"/> принципиальное: тот применяет уже
    /// записанное изменение и запись подавляет — им отмена и повтор возвращают
    /// геометрию, не дописывая стек.
    /// </para>
    /// <para>
    /// Приняли не всё: положением может распоряжаться раскладка, а размер ограничивают
    /// <c>Min</c>/<c>Max</c> контрола и границы формы. Отсекается это на швах, поэтому
    /// ответ берётся у контракта изменений, а не перечитыванием design-координат —
    /// те отстают на проход диспетчера и сразу после записи ещё старые.
    /// </para>
    /// </remarks>
    public bool SetDesignGeometry(Control target, Rect bounds)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        // Вид правки — по тому, что изменилось: у размера своя единица, как и у жеста.
        var kind = GetDesignSize(target) == bounds.Size
            ? DesignEditKind.Move
            : DesignEditKind.Resize;

        BeginEdit(kind);
        SetDesignSize(target, bounds.Size);
        SetDesignPosition(target, bounds.Position);

        var applied = CommitEdit();
        RefreshSelectionOverlay();
        return applied;
    }

    internal Size GetDesignSize(Control control)
    {
        var width = double.IsNaN(control.Width) ? control.Bounds.Width : control.Width;
        var height = double.IsNaN(control.Height) ? control.Bounds.Height : control.Height;
        return new Size(width, height);
    }

    internal void SetDesignSize(Control control, Size size)
    {
        var coerced = CoerceDesignSize(control, size);

        if (!_suppressEditRecording)
            _activeEdit?.RecordSize(this, control, coerced);

        control.Width = coerced.Width;
        control.Height = coerced.Height;
    }

    /// <summary>
    /// Приводит запрошенный размер к ограничениям самого контрола.
    /// </summary>
    /// <remarks>
    /// До появления этого метода редактор писал <c>Width</c>/<c>Height</c> мимо
    /// <c>MinWidth</c>/<c>MaxWidth</c>: раскладка применяла ограничение уже после,
    /// и запрошенный размер расходился с фактическим — редактор считал от одного,
    /// а пользователь видел другое.
    /// <para>
    /// При <c>Max &lt; Min</c> побеждает минимум — так же, как в самой Avalonia.
    /// </para>
    /// <para>
    /// Минимум редактора (<see cref="DesignEditorInteractionOptions.ResizeMinSize"/>) сюда
    /// <b>не входит</b>: он предел жеста, а не свойство контрола. Пока он стоял здесь, его
    /// получала любая запись размера — в том числе та, которой вход в жест фиксирует
    /// текущий размер, и та, которой отмена возвращает записанный. Контрол мельче порога
    /// раздувался от простого нажатия на ручку, а отмена не возвращала его обратно.
    /// </para>
    /// </remarks>
    internal Size CoerceDesignSize(Control control, Size size)
    {
        return new Size(
            ClampSize(size.Width, control.MinWidth, control.MaxWidth),
            ClampSize(size.Height, control.MinHeight, control.MaxHeight));
    }

    private static double ClampSize(double value, double min, double max)
        => Math.Max(Math.Min(value, max), min);

    private protected void SetDesignZIndex(Control control, int zIndex)
    {
        if (!_suppressEditRecording)
            _activeEdit?.RecordZIndex(this, control, zIndex);

        control.ZIndex = zIndex;
    }

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

        // Изменение применяет себя само: у ядра нет закрытого списка видов правки,
        // и правка, которую завёл слой выше, отменяется той же дорогой.
        change.ApplyTo(this, revert);
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

        RefreshSelectionOverlay();
    }

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
    private protected void BeginEdit(DesignEditKind kind)
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
    private protected bool CommitEdit()
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
    private protected void CancelEdit() => _activeEdit = null;
}
