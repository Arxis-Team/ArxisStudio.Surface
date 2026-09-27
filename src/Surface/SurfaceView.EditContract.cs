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
    // все мутации проходят через SetTargetPosition/SetTargetSize и попадают в неё.
    private SurfaceEditScope? _activeEdit;

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
    public event EventHandler<SurfaceEditCompletedEventArgs>? EditCompleted;

    /// <summary>
    /// Геометрия target'ов: где они и можно ли задать им позицию.
    /// </summary>
    /// <remarks>
    /// Шов между ядром и тем, что лежит на холсте (ADR 0003). Ядро знает только
    /// контейнеры и их <see cref="SurfaceItem.Location"/>; дизайнер интерфейса подставляет
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
    /// <see cref="SurfaceEditCompletedEventArgs.Changes"/> после геометрии и порядка.
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
    /// Пересобирает состояние выделения и публикует его.
    /// </summary>
    /// <remarks>
    /// Ядро публикует выделение само: без этого голая поверхность записывала выбор, но
    /// не сообщала о нём — <see cref="SelectedTargets"/> оставался пустым, событие
    /// не приходило, а выбранный контейнер не удерживал нажатие и отдавал его рамке, так
    /// что перетащить ничего было нельзя. Рамки и ручки ядро не рисует: выбранный
    /// контейнер показывает себя своей темой.
    /// <para>
    /// Дизайнер интерфейса переопределяет метод целиком: у него та же публикация плюс оверлей
    /// с рамками, кластеры групп и вход в группу.
    /// </para>
    /// </remarks>
    private protected virtual void RefreshSelectionOverlay()
    {
        if (!RealizeSelectedItems())
            return;

        CleanupSelectionTargets();

        var primary = _selectedTargets.Count > 0 ? _selectedTargets[0] : null;
        var primaryItem = primary as SurfaceItem ?? (primary != null ? FindSurfaceHost(primary) : null);
        ApplySelectionSnapshot(CreateSelectionTargetsSnapshot(primaryItem, primary));
    }

    internal Point GetTargetPosition(Control control)
        => Geometry.GetPosition(control);

    /// <summary>
    /// Задаёт позицию target'а в координатах поверхности.
    /// </summary>
    /// <remarks>
    /// Раскладка, которая владеет позицией ребёнка, отсекается здесь, а не выше:
    /// это единственная точка записи, поэтому только тут можно гарантировать,
    /// что в контракт изменений не попадёт перемещение, которого не произошло.
    /// </remarks>
    internal void SetTargetPosition(Control control, Point position)
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
    /// <param name="bounds">Желаемая рамка в координатах поверхности.</param>
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
    /// ответ берётся у контракта изменений, а не перечитыванием координат поверхности —
    /// те отстают на проход диспетчера и сразу после записи ещё старые.
    /// </para>
    /// </remarks>
    public bool SetTargetGeometry(Control target, Rect bounds)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        // Вид правки — по тому, что изменилось: у размера своя единица, как и у жеста.
        var kind = GetTargetSize(target) == bounds.Size
            ? SurfaceEditKind.Move
            : SurfaceEditKind.Resize;

        BeginEdit(kind);
        SetTargetSize(target, bounds.Size);
        SetTargetPosition(target, bounds.Position);

        var applied = CommitEdit();
        RefreshSelectionOverlay();
        return applied;
    }

    internal Size GetTargetSize(Control control)
    {
        var width = double.IsNaN(control.Width) ? control.Bounds.Width : control.Width;
        var height = double.IsNaN(control.Height) ? control.Bounds.Height : control.Height;
        return new Size(width, height);
    }

    internal void SetTargetSize(Control control, Size size)
    {
        var coerced = CoerceTargetSize(control, size);

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
    /// Минимум редактора (<see cref="SurfaceInteractionOptions.ResizeMinSize"/>) сюда
    /// <b>не входит</b>: он предел жеста, а не свойство контрола. Пока он стоял здесь, его
    /// получала любая запись размера — в том числе та, которой вход в жест фиксирует
    /// текущий размер, и та, которой отмена возвращает записанный. Контрол мельче порога
    /// раздувался от простого нажатия на ручку, а отмена не возвращала его обратно.
    /// </para>
    /// </remarks>
    internal Size CoerceTargetSize(Control control, Size size)
    {
        return new Size(
            ClampSize(size.Width, control.MinWidth, control.MaxWidth),
            ClampSize(size.Height, control.MinHeight, control.MaxHeight));
    }

    private static double ClampSize(double value, double min, double max)
        => Math.Max(Math.Min(value, max), min);

    private protected void SetTargetZIndex(Control control, int zIndex)
    {
        if (!_suppressEditRecording)
            _activeEdit?.RecordZIndex(this, control, zIndex);

        control.ZIndex = zIndex;
    }

    /// <summary>
    /// Отменяет изменение, возвращая target в состояние до него.
    /// </summary>
    /// <param name="change">Изменение из <see cref="SurfaceEditCompletedEventArgs.Changes"/>.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="change"/> равен <see langword="null"/>.</exception>
    /// <remarks>
    /// Разбирать конкретный тип изменения приложению не нужно: стек отмены пишется
    /// одинаково для геометрии и для порядка перекрытия.
    /// </remarks>
    public void Revert(TargetChange change) => Apply(change, revert: true);

    /// <summary>
    /// Повторяет ранее отменённое изменение.
    /// </summary>
    /// <param name="change">Изменение из <see cref="SurfaceEditCompletedEventArgs.Changes"/>.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="change"/> равен <see langword="null"/>.</exception>
    public void Reapply(TargetChange change) => Apply(change, revert: false);

    private void Apply(TargetChange change, bool revert)
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
            SetTargetZIndex(target, zIndex);
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
    /// <param name="bounds">Целевая геометрия в координатах поверхности.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="target"/> равен <see langword="null"/>.</exception>
    /// <remarks>
    /// Предназначен для отмены и повтора: принимает <see cref="GeometryChange.OldBounds"/>
    /// или <see cref="GeometryChange.NewBounds"/> напрямую. Запись изменений на время
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
            SetTargetSize(target, bounds.Size);
            SetTargetPosition(target, bounds.Position);
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
    private protected void BeginEdit(SurfaceEditKind kind)
    {
        if (_activeEdit != null)
            CommitEdit();

        _activeEdit = new SurfaceEditScope(kind);
    }

    /// <summary>
    /// Закрывает единицу редактирования и публикует изменения, если они есть.
    /// </summary>
    /// <returns><see langword="true"/>, если изменения были опубликованы.</returns>
    /// <remarks>
    /// Ответ нужен точке записи снаружи жеста: <see cref="SetTargetGeometry"/> обязан
    /// сказать хосту, приняли его правку или раскладка её отсекла, а перечитать
    /// координаты поверхности сразу нельзя — они отстают на проход диспетчера.
    /// </remarks>
    private protected bool CommitEdit()
    {
        var scope = _activeEdit;
        _activeEdit = null;

        if (scope == null)
            return false;

        var changes = scope.BuildChanges(this);
        if (changes.Count == 0)
            return false;

        EditCompleted?.Invoke(this, new SurfaceEditCompletedEventArgs(scope.Kind, changes));
        return true;
    }

    /// <summary>
    /// Отбрасывает единицу редактирования, не публикуя изменения.
    /// </summary>
    private protected void CancelEdit() => _activeEdit = null;

    /// <summary>
    /// Возвращает прямоугольник, за который target не должен выходить при изменении размера.
    /// </summary>
    /// <remarks>
    /// Границей выбран владеющий <see cref="SurfaceItem"/>, а не прямой родитель.
    /// Панель, которая растёт по содержимому, границей быть не может: ограничивать
    /// ребёнка её же высотой — рассуждение по кругу. Форма же всегда имеет размер,
    /// и правило формулируется одной фразой: содержимое не выходит за свой контейнер.
    /// <para>
    /// У контейнера верхнего уровня владельца нет, поэтому его размер не ограничен —
    /// он лежит на бесконечном холсте.
    /// </para>
    /// </remarks>
    internal bool TryGetContainmentBounds(Control target, out Rect bounds)
    {
        bounds = default;

        if (!InteractionOptions.IsResizeContainedToParent)
            return false;

        var host = FindSurfaceHost(target);
        return host != null && Geometry.TryGetBounds(host, out bounds);
    }

    /// <summary>
    /// Точка двигающегося края: угол для диагоналей, край для сторон.
    /// </summary>
    /// <remarks>
    /// У односторонних направлений вторая координата в арифметику не входит, поэтому
    /// берётся верхний левый угол — лишь бы согласованно с местом, где считают поправку.
    /// </remarks>
    internal static Point MovingEdge(ResizeDirection direction, Rect bounds)
    {
        var x = direction switch
        {
            ResizeDirection.Right or ResizeDirection.TopRight or ResizeDirection.BottomRight => bounds.Right,
            _ => bounds.X
        };

        var y = direction switch
        {
            ResizeDirection.Bottom or ResizeDirection.BottomLeft or ResizeDirection.BottomRight => bounds.Bottom,
            _ => bounds.Y
        };

        return new Point(x, y);
    }

    /// <summary>
    /// Отдаёт положение указателя, если жест вправе им пользоваться.
    /// </summary>
    /// <param name="current">Последнее движение, какое видел редактор.</param>
    /// <param name="grab">Движение, на котором жест начался.</param>
    /// <param name="world">Положение в мировых координатах.</param>
    /// <returns><see langword="true"/>, если то же устройство двинулось после начала жеста.</returns>
    /// <remarks>
    /// Два условия, и оба нужны. <b>То же устройство</b> — иначе перо, зависшее над
    /// холстом, пока мышь тянет ручку, увело бы край к перу. <b>После начала жеста</b> —
    /// иначе снимок, оставшийся от прошлого раза, выдавался бы за движение внутри
    /// текущего, и жест, поднятый программно, получал бы нулевую поправку вместо своей
    /// дельты. Обе ошибки уже были сделаны и обе молчаливы.
    /// </remarks>
    internal static bool TryGetGesturePointer(PointerSample? current, PointerSample? grab, out Point world)
    {
        world = default;

        if (current is not { } now || grab is not { } start)
            return false;

        if (now.PointerId != start.PointerId || now.MoveCount <= start.MoveCount)
            return false;

        world = now.World;
        return true;
    }

    /// <summary>
    /// Возникает, когда пользователь просит отменить последнюю правку.
    /// </summary>
    /// <remarks>
    /// Стек правок принадлежит хосту, поэтому редактор ничего не отменяет сам.
    /// Без подписчика нажатие остаётся необработанным и всплывает дальше.
    /// </remarks>
    public event EventHandler<SurfaceHistoryRequestedEventArgs>? UndoRequested;

    /// <summary>
    /// Возникает, когда пользователь просит повторить отменённую правку.
    /// </summary>
    public event EventHandler<SurfaceHistoryRequestedEventArgs>? RedoRequested;

    /// <summary>
    /// Поднимает запрос к истории правок.
    /// </summary>
    /// <param name="handler">Подписчики запроса.</param>
    /// <returns><see langword="true"/>, если запрос выполнен.</returns>
    /// <remarks>
    /// Обход останавливается на первом выполнившем — по той же причине, что у удаления
    /// и перестановки: второй обработчик отменял бы уже не то, о чём его спросили.
    /// </remarks>
    private protected bool TryRequestHistory(EventHandler<SurfaceHistoryRequestedEventArgs>? handler)
    {
        if (handler == null)
            return false;

        var args = new SurfaceHistoryRequestedEventArgs();
        foreach (var invocation in handler.GetInvocationList())
        {
            ((EventHandler<SurfaceHistoryRequestedEventArgs>)invocation)(this, args);

            if (args.Handled)
                break;
        }

        return args.Handled;
    }

    /// <summary>
    /// Просит подписчиков отменить последнюю правку.
    /// </summary>
    /// <returns><see langword="true"/>, если кто-то выполнил отмену.</returns>
    private protected bool RequestUndo() => TryRequestHistory(UndoRequested);

    /// <summary>
    /// Просит подписчиков повторить отменённую правку.
    /// </summary>
    /// <returns><see langword="true"/>, если кто-то выполнил повтор.</returns>
    private protected bool RequestRedo() => TryRequestHistory(RedoRequested);
}
