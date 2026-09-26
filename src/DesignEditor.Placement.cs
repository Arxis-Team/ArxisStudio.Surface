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
using ArxisStudio.Surface;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio;

// Геометрия: шов записи, политики, стратегии размещения и перестановка.
// Часть DesignEditor; общее описание типа — в DesignEditor.cs.
public partial class DesignEditor
{
    internal Point GetDesignPosition(Control control)
        => GetPlacementStrategy(control).GetPosition(control, this);

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
        var strategy = GetPlacementStrategy(control);
        if (strategy.MoveSemantics != DesignMoveSemantics.Reposition)
            return;

        if (!_suppressEditRecording)
            _activeEdit?.RecordPosition(this, control, position);

        strategy.SetPosition(control, position, this);
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
        UpdateSelectionOverlayState();
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

    /// <summary>
    /// Возвращает прямоугольник, за который target не должен выходить при изменении размера.
    /// </summary>
    /// <remarks>
    /// Границей выбран владеющий <see cref="DesignEditorItem"/>, а не прямой родитель.
    /// Панель, которая растёт по содержимому, границей быть не может: ограничивать
    /// ребёнка её же высотой — рассуждение по кругу. Форма же всегда имеет размер,
    /// и правило формулируется одной фразой: контрол не выходит за свою форму.
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

        var host = FindDesignHost(target);

        // Приведение к Control обязательно: перегрузка для DesignEditorItem
        // возвращает границы выбранного внутри него target'а, а не самой формы.
        return host != null && TryGetDesignBounds((Control)host, out bounds);
    }

    /// <summary>
    /// Определяет, есть ли кому выполнить перестановку.
    /// </summary>
    /// <remarks>
    /// Жест не предлагается, когда исполнить его некому: иначе редактор вёл бы
    /// точку вставки за курсором и обещал результат, которого не будет. Правило
    /// то же, что и у политик размещения, — заблокированный жест не начинается.
    /// </remarks>
    internal bool CanRequestReorder => ReorderRequested != null;

    /// <summary>
    /// Просит приложение переставить контрол перед указанным соседом.
    /// </summary>
    /// <param name="control">Контрол, который требуется переставить.</param>
    /// <param name="insertBefore">
    /// Позиция вставки в <b>текущем</b> списке детей; значение, равное их числу,
    /// означает «в конец». Выход за эти границы означает, что дерево изменилось
    /// внутри жеста, и запрос отклоняется.
    /// </param>
    /// <returns><see langword="true"/>, если перестановку выполнил обработчик.</returns>
    /// <remarks>
    /// Редактор сам дерево не правит: он распознаёт жест и сообщает намерение.
    /// Структурная правка, её запись и отмена — зона библиотеки разметки.
    /// <para>
    /// Обе половины запроса считаются от <b>одного</b> чтения коллекции. Раньше
    /// состояние жеста переводило точку вставки в индекс переноса по своему снимку,
    /// снятому на входе в жест, а текущую позицию редактор перечитывал заново, —
    /// и стоило дереву измениться во время протяжки, как хост получал пару индексов,
    /// описывающих разные состояния панели.
    /// </para>
    /// </remarks>
    private bool RequestChildIndex(Control control, int insertBefore)
    {
        if (control.GetVisualParent() is not Panel panel)
            return false;

        var current = panel.Children.IndexOf(control);
        if (current < 0)
            return false;

        var handler = ReorderRequested;
        if (handler == null)
            return false;

        // Точка вставки описывает ту же коллекцию, которую редактор только что
        // прочитал: от нуля до числа детей включительно, где верхняя граница
        // означает «в конец». Всё, что вне, — снимок панели, которой уже нет:
        // дерево изменилось внутри жеста. Такой запрос отклоняется, а не
        // подгоняется под новый размер. Подгонка ставила бы контрол туда, куда
        // пользователь не целился, и хост не смог бы это заметить: индексы
        // приходили бы к нему валидными на вид.
        if (insertBefore < 0 || insertBefore > panel.Children.Count)
            return false;

        // Перенос удаляет контрол и только потом вставляет, поэтому позиции
        // правее источника сдвигаются на одну. Из проверки выше следует, что
        // результат уже лежит в границах коллекции, и ограничивать его нечем.
        var requested = insertBefore > current ? insertBefore - 1 : insertBefore;
        if (requested == current)
            return false;

        var anchor = insertBefore < panel.Children.Count
            ? panel.Children[insertBefore]
            : null;

        var args = new DesignEditorReorderRequestedEventArgs(
            control,
            current,
            requested,
            ReferenceEquals(anchor, control) ? null : anchor);

        // Обработчики обходятся по одному, и первый же выполнивший правку
        // останавливает обход. Индексы сняты до правки, поэтому следующему
        // они описывали бы дерево, которого уже нет: на двух подписчиках
        // одинаковой формы перестановка молча отменяла сама себя.
        foreach (var invocation in handler.GetInvocationList())
        {
            ((EventHandler<DesignEditorReorderRequestedEventArgs>)invocation)(this, args);

            if (args.Handled)
                break;
        }

        if (!args.Handled)
            return false;

        // Обработчик мог не переставить контрол, а пересобрать разметку. Выделение,
        // оставшееся на контроле вне дерева, рисовало бы рамку по его последним
        // координатам и принимало бы на него нюдж. Пересборка оверлея этим и
        // занимается: сверка выделения отбрасывает target, у которого больше нет
        // владеющего item'а, — отдельная зачистка рядом с ней ничего не добавляла
        // и ни одним тестом не держалась.
        UpdateSelectionOverlayState();
        return true;
    }

    /// <summary>
    /// Распределяет выбранные элементы по горизонтали с равными зазорами.
    /// </summary>
    /// <returns><see langword="true"/>, если распределение выполнено.</returns>
    /// <remarks>
    /// Крайние элементы остаются на месте — они задают отрезок, — а промежуточные
    /// расставляются так, чтобы зазоры между соседями стали равны.
    /// </remarks>
    public bool DistributeHorizontally() => TryDistribute(xAxis: true);

    /// <summary>
    /// Распределяет выбранные элементы по вертикали с равными зазорами.
    /// </summary>
    /// <returns><see langword="true"/>, если распределение выполнено.</returns>
    public bool DistributeVertically() => TryDistribute(xAxis: false);

    /// <summary>
    /// Расставляет выбранные элементы с равными зазорами вдоль оси.
    /// </summary>
    /// <remarks>
    /// Равными делаются <b>зазоры</b>, а не расстояния между центрами: тем же словарём
    /// описан весь остальной интервал в библиотеке, и при разной ширине элементов
    /// ровные центры выглядят неровно именно потому, что зазоры при них разные.
    /// <para>
    /// Меньше трёх элементов распределять нечего: два уже задают отрезок и никуда
    /// не двигаются.
    /// </para>
    /// <para>
    /// Заблокированный политикой промежуточный элемент отменяет операцию целиком.
    /// Расставить часть значило бы выдать за распределение то, что им не является, —
    /// то же правило, по которому смешанная группа не двигается вовсе.
    /// </para>
    /// </remarks>
    private bool TryDistribute(bool xAxis)
    {
        var selection = SelectedDesignTargets;
        if (selection.Count < 3)
            return false;

        var items = new List<(Control Target, Rect Bounds)>(selection.Count);
        for (var i = 0; i < selection.Count; i++)
        {
            var target = selection[i].Target;
            if (!TryGetDesignBounds(target, out var bounds))
                return false;

            items.Add((target, bounds));
        }

        items.Sort((a, b) => Position(a.Bounds).CompareTo(Position(b.Bounds)));

        // Крайние задают отрезок и не двигаются, поэтому их политика роли не играет.
        var axis = xAxis ? ArxisStudio.Surface.MovePolicy.X : ArxisStudio.Surface.MovePolicy.Y;
        for (var i = 1; i < items.Count - 1; i++)
        {
            if (!GetEffectiveMovePolicy(items[i].Target).HasFlag(axis))
                return false;
        }

        var first = items[0].Bounds;
        var last = items[^1].Bounds;

        var occupied = 0.0;
        for (var i = 0; i < items.Count; i++)
            occupied += Size(items[i].Bounds);

        var gap = (Extent(last) - Position(first) - occupied) / (items.Count - 1);

        BeginEdit(DesignEditKind.Move);

        var cursor = Extent(first);
        for (var i = 1; i < items.Count - 1; i++)
        {
            cursor += gap;

            var (target, bounds) = items[i];
            SetDesignPosition(target, xAxis
                ? new Point(cursor, bounds.Y)
                : new Point(bounds.X, cursor));

            cursor += Size(bounds);
        }

        CommitEdit();
        UpdateSelectionOverlayState();
        return true;

        double Position(Rect rect) => xAxis ? rect.X : rect.Y;
        double Extent(Rect rect) => xAxis ? rect.Right : rect.Bottom;
        double Size(Rect rect) => xAxis ? rect.Width : rect.Height;
    }


    internal ArxisStudio.Surface.ResizePolicy GetResizePolicy(Control control)
    {
        return DesignInteraction.GetResizePolicy(control);
    }

    internal ArxisStudio.Surface.MovePolicy GetMovePolicy(Control control)
    {
        return DesignInteraction.GetMovePolicy(control);
    }

    /// <summary>
    /// Возвращает стратегию размещения контрола.
    /// </summary>
    internal static IDesignPlacementStrategy GetPlacementStrategy(Control control)
        => DesignPlacementResolver.Resolve(control);

    /// <summary>
    /// Возвращает политику перемещения с учётом того, что реально умеет родительская раскладка.
    /// </summary>
    /// <remarks>
    /// Правило одно: <c>effective = user &amp; layout</c>. Раскладка задаёт потолок —
    /// что физически работает; политика пользователя только сужает. Ни одна не расширяет
    /// другую, иначе редактор снова начал бы предлагать жест, который ничего не делает.
    /// </remarks>
    internal ArxisStudio.Surface.MovePolicy GetEffectiveMovePolicy(Control control)
    {
        var user = GetMovePolicy(control);
        if (user == ArxisStudio.Surface.MovePolicy.None)
            return ArxisStudio.Surface.MovePolicy.None;

        return GetPlacementStrategy(control).MoveSemantics == DesignMoveSemantics.Reposition
            ? user
            : ArxisStudio.Surface.MovePolicy.None;
    }

    internal Vector ApplyMovePolicy(Control control, Vector delta)
    {
        return ApplyMovePolicy(delta, GetEffectiveMovePolicy(control));
    }

    internal bool IsResizeAllowed(Control control, ResizeDirection direction)
    {
        return IsResizeAllowed(GetResizePolicy(control), direction);
    }

    private SelectionInteractionCapabilities GetSelectionInteractionCapabilities()
    {
        var selectedTargets = SelectedDesignTargets;
        var selectedTargetCount = selectedTargets.Count;
        // Группа ведёт себя как множественный выбор вложенных: одна рамка, один жест.
        var isNestedGroupSelection = (HasMultipleNestedSelection || HasGroupSelection) && selectedTargetCount > 1;
        var hasAnyMoveLockedTarget = false;
        var hasAnyMoveEnabledTarget = false;

        for (var i = 0; i < selectedTargetCount; i++)
        {
            var movePolicy = GetEffectiveMovePolicy(selectedTargets[i].Target);
            if (movePolicy == ArxisStudio.Surface.MovePolicy.None)
                hasAnyMoveLockedTarget = true;
            else
                hasAnyMoveEnabledTarget = true;

            if (hasAnyMoveLockedTarget && hasAnyMoveEnabledTarget)
                break;
        }

        return new SelectionInteractionCapabilities(
            selectedTargetCount,
            isNestedGroupSelection,
            hasAnyMoveLockedTarget,
            hasAnyMoveEnabledTarget);
    }

    internal bool ShouldBlockNestedGroupDrag(SelectionInteractionCapabilities capabilities)
    {
        if (!capabilities.IsNestedGroupSelection)
            return false;

        return capabilities.HasAnyMoveLockedTarget;
    }

    internal bool ShouldBlockNestedGroupDrag()
    {
        return ShouldBlockNestedGroupDrag(GetSelectionInteractionCapabilities());
    }

    private static Vector ApplyMovePolicy(Vector delta, ArxisStudio.Surface.MovePolicy policy)
    {
        var x = policy.HasFlag(ArxisStudio.Surface.MovePolicy.X) ? delta.X : 0d;
        var y = policy.HasFlag(ArxisStudio.Surface.MovePolicy.Y) ? delta.Y : 0d;
        return new Vector(x, y);
    }

    private static bool IsResizeAllowed(ArxisStudio.Surface.ResizePolicy policy, ResizeDirection direction)
    {
        return direction switch
        {
            ResizeDirection.Left => policy.HasFlag(ArxisStudio.Surface.ResizePolicy.Left),
            ResizeDirection.Top => policy.HasFlag(ArxisStudio.Surface.ResizePolicy.Top),
            ResizeDirection.Right => policy.HasFlag(ArxisStudio.Surface.ResizePolicy.Right),
            ResizeDirection.Bottom => policy.HasFlag(ArxisStudio.Surface.ResizePolicy.Bottom),
            ResizeDirection.TopLeft => policy.HasFlag(ArxisStudio.Surface.ResizePolicy.Top) &&
                                       policy.HasFlag(ArxisStudio.Surface.ResizePolicy.Left),
            ResizeDirection.TopRight => policy.HasFlag(ArxisStudio.Surface.ResizePolicy.Top) &&
                                        policy.HasFlag(ArxisStudio.Surface.ResizePolicy.Right),
            ResizeDirection.BottomLeft => policy.HasFlag(ArxisStudio.Surface.ResizePolicy.Bottom) &&
                                          policy.HasFlag(ArxisStudio.Surface.ResizePolicy.Left),
            ResizeDirection.BottomRight => policy.HasFlag(ArxisStudio.Surface.ResizePolicy.Bottom) &&
                                           policy.HasFlag(ArxisStudio.Surface.ResizePolicy.Right),
            _ => false
        };
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

    internal bool TryGetDesignBounds(Control control, out Rect bounds)
    {
        if (!ReferenceEquals(control.FindAncestorOfType<DesignEditor>(), this))
        {
            bounds = default;
            return false;
        }

        if (control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
        {
            bounds = default;
            return false;
        }

        EnsureTracked(control);

        Visual? reference = control.FindAncestorOfType<DesignSurface>()
                            ?? control.FindAncestorOfType<DesignEditor>() as Visual;

        var position = reference != null
            ? control.TranslatePoint(new Point(0, 0), reference)
            : null;

        double x;
        double y;

        if (position.HasValue)
        {
            x = position.Value.X;
            y = position.Value.Y;
        }
        else
        {
            x = DesignLayout.GetDesignX(control);
            y = DesignLayout.GetDesignY(control);

            if (double.IsNaN(x) || double.IsNaN(y))
            {
                bounds = default;
                return false;
            }
        }

        bounds = new Rect(new Point(x, y), control.Bounds.Size);
        return true;
    }

    /// <summary>
    /// Обновляет индикатор точки вставки.
    /// </summary>
    internal void UpdateReorderIndicator(Rect designBounds)
    {
        ReorderIndicator = designBounds;
        IsReordering = true;
    }

    /// <summary>
    /// Сообщает намерение переставить контрол и убирает индикатор.
    /// </summary>
    /// <returns><see langword="true"/>, если перестановку выполнил обработчик.</returns>
    /// <remarks>
    /// Результат не декоративный: им помечается само нажатие. Отказ обработчика
    /// оставляет событие необработанным, и оно всплывает дальше — так же, как
    /// у <see cref="DeleteRequested"/>. Иначе <c>Handled</c> оказался бы
    /// свойством, которое никто не читает.
    /// </remarks>
    internal bool CommitReorder(Control target, int insertBefore)
    {
        var handled = RequestChildIndex(target, insertBefore);
        ClearReorderIndicator();
        return handled;
    }

    /// <summary>
    /// Прерывает перестановку, ничего не сообщая.
    /// </summary>
    internal void CancelReorder() => ClearReorderIndicator();

    private void ClearReorderIndicator()
    {
        IsReordering = false;
        ReorderIndicator = default;
    }
}
