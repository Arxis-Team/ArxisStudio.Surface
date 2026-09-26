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

namespace ArxisStudio;

// Привязка к сетке и направляющие выравнивания.
// Часть DesignEditor; общее описание типа — в DesignEditor.cs.
public partial class DesignEditor
{
    /// <summary>
    /// Определяет, должна ли действовать привязка при текущих модификаторах.
    /// </summary>
    internal bool ShouldSnap(KeyModifiers modifiers)
    {
        if (!InteractionOptions.IsSnapToGridEnabled)
            return false;

        if (IsSnapBypassed(modifiers))
            return false;

        return ResolveSnapStep() > 0;
    }

    /// <summary>
    /// Определяет, отключил ли пользователь привязку модификатором.
    /// </summary>
    /// <remarks>
    /// Модификатор отключает привязку целиком — и к сетке, и к направляющим.
    /// Обещание одно: «держу нажатым — ставлю куда хочу», и делить его между
    /// двумя видами привязки было бы нечестно.
    /// </remarks>
    private bool IsSnapBypassed(KeyModifiers modifiers)
    {
        var bypass = InputGestures.SnapBypassModifiers;
        return bypass != KeyModifiers.None && modifiers.HasFlag(bypass);
    }

    /// <summary>
    /// Возвращает действующий шаг привязки.
    /// </summary>
    /// <remarks>
    /// Явно заданный <see cref="DesignEditorInteractionOptions.SnapStep"/> имеет приоритет;
    /// иначе шаг берётся у сетки шаблона. Это не даёт сетке рисовать одну структуру,
    /// а привязке использовать другую.
    /// </remarks>
    internal double ResolveSnapStep()
    {
        var configured = InteractionOptions.SnapStep;
        if (!double.IsNaN(configured))
            return configured;

        return _grid?.CellSize ?? 0;
    }

    /// <summary>
    /// Округляет координату до ближайшего узла сетки.
    /// </summary>
    /// <remarks>
    /// Ровно посередине между узлами привязка уходит вверх — всегда и везде.
    /// <see cref="Math.Round(double)"/> здесь не годится: он округляет к чётному,
    /// поэтому на середине направление зависело бы от чётности узла, и при
    /// медленной протяжке край прыгал бы то вперёд, то назад.
    /// </remarks>
    internal double SnapCoordinate(double value)
    {
        var step = ResolveSnapStep();
        return step > 0 ? Math.Floor((value / step) + 0.5) * step : Math.Round(value);
    }

    /// <summary>
    /// Приводит позицию к сетке, если привязка активна.
    /// </summary>
    /// <remarks>
    /// Привязывается именно результат, а не смещение: округление дельты сохранило бы
    /// исходный сдвиг элемента, и на узел сетки он бы так и не встал.
    /// </remarks>
    internal Point SnapPosition(Point position, KeyModifiers modifiers)
    {
        if (!ShouldSnap(modifiers))
            return new Point(Math.Round(position.X), Math.Round(position.Y));

        return new Point(SnapCoordinate(position.X), SnapCoordinate(position.Y));
    }

    /// <summary>
    /// Снимает соседей, к которым будет идти выравнивание, на время жеста.
    /// </summary>
    /// <param name="movingTarget">Target, который ведёт жест.</param>
    /// <remarks>
    /// Соседи снимаются один раз, а не пересчитываются покадрово, и это не
    /// оптимизация: во время жеста они не двигаются, а вот layout-проход внутри
    /// жеста мог бы сдвинуть те самые линии, в которые пользователь целится.
    /// Снимок делает направляющую неподвижной ровно настолько, насколько она
    /// выглядит неподвижной.
    /// </remarks>
    internal void BeginSnapGuides(Control movingTarget)
    {
        _snapGuideNeighbours = InteractionOptions.IsSnapToGuidesEnabled
            ? CollectSnapGuideNeighbours(movingTarget)
            : Array.Empty<Rect>();
    }

    /// <summary>
    /// Закрывает жест: сбрасывает снимок соседей и убирает линии.
    /// </summary>
    internal void EndSnapGuides()
    {
        PublishSpacingHints(Array.Empty<DesignSpacingHint>());
        _snapGuideNeighbours = null;
        PublishSnapGuides(Array.Empty<DesignSnapGuide>());
    }

    /// <summary>
    /// Возвращает позицию перетаскиваемого target'а с учётом направляющих и сетки.
    /// </summary>
    /// <param name="target">Перетаскиваемый target.</param>
    /// <param name="proposed">Позиция, предложенная жестом.</param>
    /// <param name="modifiers">Модификаторы текущего ввода.</param>
    /// <remarks>
    /// Композиция та же, что у остальных правил редактора: направляющая занимает ось,
    /// сетка получает всё остальное. Оси независимы — элемент может встать на край
    /// соседа по X и на узел сетки по Y, и это ровно то, чего ждёт пользователь.
    /// <para>
    /// Как и привязка к сетке, направляющие работают с <b>результатом</b>, а не с
    /// дельтой: смещение считается от предложенной позиции, поэтому элемент встаёт
    /// на линию, а не сохраняет прежний зазор до неё.
    /// </para>
    /// </remarks>
    internal Point ResolveDragPosition(Control target, Point proposed, KeyModifiers modifiers)
    {
        // Группа притягивается рамкой выделения, а не тем элементом, за который её
        // схватили: так уже устроен групповой resize, и так же выглядит происходящее
        // на экране — пользователь ведёт рамку, её и надо ставить на место.
        //
        // Отображение стоит снаружи всего разрешения, а не внутри ветки направляющих:
        // правило одно на всю привязку, и сетка обязана ставить на узел ту же рамку.
        if (_groupDragOperation is { } group && ReferenceEquals(target, group.SourceTarget))
        {
            var frame = ResolveOrigin(proposed + group.FrameOffset, group.Frame.Size, modifiers);
            return frame - group.FrameOffset;
        }

        return ResolveOrigin(proposed, GetDesignSize(target), modifiers);
    }

    /// <summary>
    /// Ставит прямоугольник на место по направляющим, интервалам и сетке.
    /// </summary>
    /// <param name="proposed">Предполагаемое положение левого верхнего угла.</param>
    /// <param name="size">Размер прямоугольника.</param>
    /// <param name="modifiers">Модификаторы текущего ввода.</param>
    private Point ResolveOrigin(Point proposed, Size size, KeyModifiers modifiers)
    {
        var neighbours = _snapGuideNeighbours;

        if (neighbours is not { Count: > 0 } || IsSnapBypassed(modifiers))
        {
            PublishSnapGuides(Array.Empty<DesignSnapGuide>());
            PublishSpacingHints(Array.Empty<DesignSpacingHint>());
            return SnapPosition(proposed, modifiers);
        }

        var snapToGrid = ShouldSnap(modifiers);

        DesignSnapGuideResolver.TryResolveOffset(
            new Rect(proposed, size),
            neighbours,
            ResolveSnapGuideTolerance(),
            out var offset,
            out var snappedX,
            out var snappedY);

        // Равные интервалы стоят между выравниванием и сеткой. Выравнивание точнее:
        // оно связывает элемент с конкретным краем соседа, а интервал — с расстоянием,
        // которого на макете не видно. Сетка же остаётся тем, что забирает всё,
        // на что не нашлось отношения.
        var spacedX = false;
        var spacedY = false;
        var spacing = default(Vector);

        if (InteractionOptions.IsEqualSpacingEnabled && (!snappedX || !snappedY))
        {
            DesignSpacingResolver.TryResolveOffset(
                new Rect(proposed, size),
                neighbours,
                ResolveSnapGuideTolerance(),
                out spacing,
                out spacedX,
                out spacedY);
        }

        var x = Axis(proposed.X, offset.X, spacing.X, snappedX, spacedX);
        var y = Axis(proposed.Y, offset.Y, spacing.Y, snappedY, spacedY);

        var result = new Point(x, y);
        var bounds = new Rect(result, size);

        PublishSnapGuides(DesignSnapGuideResolver.CollectGuides(bounds, neighbours));
        PublishSpacingHints(InteractionOptions.IsEqualSpacingEnabled
            ? DesignSpacingResolver.CollectHints(bounds, neighbours)
            : Array.Empty<DesignSpacingHint>());

        return result;

        double Axis(double value, double guide, double space, bool snapped, bool spaced)
        {
            if (snapped)
                return value + guide;

            return spaced ? value + space : GridCoordinate(value, snapToGrid);
        }

        double GridCoordinate(double value, bool snap) => snap ? SnapCoordinate(value) : Math.Round(value);
    }

    /// <summary>
    /// Определяет, может ли сработать привязка двигающегося края при resize.
    /// </summary>
    /// <remarks>
    /// Спрашивается на входе в блок привязки, чтобы не трогать геометрию там,
    /// где ни сетка, ни направляющие не действуют: в остальном resize оставляет
    /// координаты как есть и не округляет их, в отличие от перетаскивания.
    /// </remarks>
    internal bool CanSnapResizeEdge(KeyModifiers modifiers)
    {
        if (IsSnapBypassed(modifiers))
            return false;

        return ShouldSnap(modifiers) || HasSnapGuideNeighbours;
    }

    private bool HasSnapGuideNeighbours
        => InteractionOptions.IsSnapToGuidesEnabled && _snapGuideNeighbours is { Count: > 0 };

    /// <summary>
    /// Возвращает координату двигающегося края с учётом направляющих и сетки.
    /// </summary>
    /// <param name="edge">Координата края по своей оси.</param>
    /// <param name="proposed">Предполагаемая геометрия элемента целиком.</param>
    /// <param name="farEdge">Признак того, что двигается дальний край.</param>
    /// <param name="xAxis">Признак горизонтальной оси.</param>
    /// <param name="modifiers">Модификаторы текущего ввода.</param>
    /// <remarks>
    /// Та же композиция, что и при перетаскивании: направляющая занимает ось,
    /// сетка получает всё остальное. Разница лишь в том, что здесь снимается
    /// один край, а не позиция целиком, — накапливать тут нечего, потому что
    /// край приходит уже посчитанным от применённой геометрии.
    /// </remarks>
    internal double ResolveResizeEdge(double edge, Rect proposed, bool xAxis, bool farEdge, KeyModifiers modifiers)
    {
        if (IsSnapBypassed(modifiers))
            return edge;

        if (HasSnapGuideNeighbours &&
            DesignSnapGuideResolver.TryResolveEdge(
                edge, _snapGuideNeighbours!, ResolveSnapGuideTolerance(), xAxis, out var guided))
        {
            return guided;
        }

        // Порядок тот же, что при перетаскивании: выравнивание, потом интервал,
        // потом сетка. Разница только во входе — при resize неподвижный край задаёт
        // свой зазор, и вопрос один: где должен встать двигающийся.
        if (HasSnapGuideNeighbours &&
            InteractionOptions.IsEqualSpacingEnabled &&
            DesignSpacingResolver.TryResolveEdge(
                proposed, _snapGuideNeighbours!, ResolveSnapGuideTolerance(), xAxis, farEdge, out var spaced))
        {
            return spaced;
        }

        return ShouldSnap(modifiers) ? SnapCoordinate(edge) : edge;
    }

    /// <summary>
    /// Публикует направляющие по итоговой геометрии жеста изменения размера.
    /// </summary>
    internal void PublishResizeGuides(Rect bounds)
    {
        PublishSnapGuides(HasSnapGuideNeighbours
            ? DesignSnapGuideResolver.CollectGuides(bounds, _snapGuideNeighbours!)
            : Array.Empty<DesignSnapGuide>());

        PublishSpacingHints(HasSnapGuideNeighbours && InteractionOptions.IsEqualSpacingEnabled
            ? DesignSpacingResolver.CollectHints(bounds, _snapGuideNeighbours!)
            : Array.Empty<DesignSpacingHint>());
    }

    /// <summary>
    /// Возвращает радиус захвата направляющей в мировых единицах.
    /// </summary>
    private double ResolveSnapGuideTolerance()
    {
        var tolerance = InteractionOptions.SnapGuideTolerance;
        if (!(tolerance > 0))
            return 0;

        var zoom = ViewportZoom;
        return zoom > 0 ? tolerance / zoom : tolerance;
    }

    /// <summary>
    /// Собирает прямоугольники, по которым идёт выравнивание.
    /// </summary>
    /// <remarks>
    /// Соседями считается то же, что редактор разрешает выбрать: правило одно,
    /// и выровняться можно ровно по тому, что видно как отдельный элемент.
    /// К ним добавляются границы самой формы — по её краям и центру выравнивают чаще
    /// всего, а отдельным элементом она не является.
    /// </remarks>
    private IReadOnlyList<Rect> CollectSnapGuideNeighbours(Control movingTarget)
    {
        var neighbours = new List<Rect>();
        var host = FindDesignHost(movingTarget);

        if (host == null)
        {
            // Двигают контейнер верхнего уровня: соседи — остальные контейнеры.
            foreach (var container in EnumerateContainers())
            {
                if (IsExcludedFromSnapGuides(container, movingTarget))
                    continue;

                if (TryGetDesignBounds((Control)container, out var containerBounds))
                    neighbours.Add(containerBounds);
            }

            AddUserGuideNeighbours(neighbours);
            return neighbours;
        }

        foreach (var candidate in EnumerateSelectionCandidates(host))
        {
            if (!IsSelectableTarget(candidate, host))
                continue;

            if (IsExcludedFromSnapGuides(candidate, movingTarget))
                continue;

            if (TryGetDesignBounds(candidate, out var bounds))
                neighbours.Add(bounds);
        }

        // Приведение к Control обязательно: перегрузка для DesignEditorItem
        // вернула бы геометрию его выбранного target'а, а не самой формы.
        if (TryGetDesignBounds((Control)host, out var hostBounds))
            neighbours.Add(hostBounds);

        AddUserGuideNeighbours(neighbours);
        return neighbours;
    }

    /// <summary>
    /// Определяет, участвует ли кандидат в выравнивании.
    /// </summary>
    /// <remarks>
    /// Исключается всё, что двигается вместе с жестом, — сам target, остальное
    /// выделение при групповом перетаскивании и их родня по дереву. Иначе элемент
    /// выравнивался бы сам по себе и линия висела бы на нём всю протяжку.
    /// </remarks>
    private bool IsExcludedFromSnapGuides(Control candidate, Control movingTarget)
    {
        if (IsSameOrRelated(candidate, movingTarget))
            return true;

        foreach (var selected in _selectedTargets)
        {
            if (IsSameOrRelated(candidate, selected))
                return true;
        }

        return false;
    }

    private static bool IsSameOrRelated(Control first, Control second)
    {
        if (ReferenceEquals(first, second))
            return true;

        foreach (var ancestor in first.GetVisualAncestors())
        {
            if (ReferenceEquals(ancestor, second))
                return true;
        }

        foreach (var ancestor in second.GetVisualAncestors())
        {
            if (ReferenceEquals(ancestor, first))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Публикует набор направляющих, если он действительно изменился.
    /// </summary>
    /// <remarks>
    /// Та же дисциплина, что у <see cref="ApplySelectionSnapshot"/>: метод вызывается
    /// на каждом кадре протяжки, а линии меняются редко. Без сравнения слой
    /// перерисовывался бы каждый кадр впустую.
    /// </remarks>
    private void PublishSnapGuides(IReadOnlyList<DesignSnapGuide> guides)
    {
        if (AreSameGuides(_snapGuides, guides))
            return;

        SnapGuides = guides;
    }

    /// <summary>
    /// Публикует подсказки о равных интервалах, если набор изменился.
    /// </summary>
    /// <param name="hints">Новый набор подсказок.</param>
    /// <remarks>
    /// Та же дисциплина, что у линий выравнивания: метод зовётся на каждом кадре
    /// протяжки, а меняются подсказки редко.
    /// </remarks>
    private void PublishSpacingHints(IReadOnlyList<DesignSpacingHint> hints)
    {
        if (AreSameHints(SpacingHints, hints))
            return;

        SpacingHints = hints;
    }

    private static bool AreSameHints(IReadOnlyList<DesignSpacingHint> first, IReadOnlyList<DesignSpacingHint> second)
    {
        if (first.Count != second.Count)
            return false;

        for (var i = 0; i < first.Count; i++)
        {
            if (!first[i].Equals(second[i]))
                return false;
        }

        return true;
    }

    private static bool AreSameGuides(IReadOnlyList<DesignSnapGuide> first, IReadOnlyList<DesignSnapGuide> second)
    {
        if (ReferenceEquals(first, second))
            return true;

        if (first.Count != second.Count)
            return false;

        for (var i = 0; i < first.Count; i++)
        {
            if (!first[i].Equals(second[i]))
                return false;
        }

        return true;
    }
}
