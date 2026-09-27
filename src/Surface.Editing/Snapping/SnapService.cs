using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace ArxisStudio.Surface.Editing;

/// <summary>
/// Привязка к сетке, направляющим выравнивания и равным интервалам.
/// </summary>
/// <remarks>
/// Служба слоя редактирования (ADR 0003): ядро предлагает позицию или край, служба
/// ставит их на место и публикует линии. Порядок один на всю привязку и не дробится —
/// направляющая, потом интервал, потом сетка; ось занимается независимо.
/// <para>
/// Правила описаны в CLAUDE.md, разделы «Привязка к сетке», «Направляющие выравнивания»
/// и «Равные интервалы»; здесь они живут без изменений, поменялось только то, откуда
/// служба берёт соседей и куда кладёт результат.
/// </para>
/// </remarks>
internal sealed class SnapService : ISurfacePositionModifier
{
    private readonly SurfaceView _view;
    private readonly Func<IEnumerable<Rect>> _extraNeighbours;

    // Соседи, к которым идёт выравнивание в текущем жесте. Снимаются один раз
    // на входе в жест; null означает, что жест не идёт.
    private IReadOnlyList<Rect>? _neighbours;

    /// <param name="view">Поверхность, к которой подключена служба.</param>
    /// <param name="extraNeighbours">
    /// Дополнительные прямоугольники выравнивания — например, пользовательские
    /// направляющие, которые приходят сюда прямоугольником нулевой толщины.
    /// </param>
    public SnapService(SurfaceView view, Func<IEnumerable<Rect>> extraNeighbours)
    {
        _view = view;
        _extraNeighbours = extraNeighbours;
    }

    private SurfaceInteractionOptions Options => _view.InteractionOptions;

    /// <summary>
    /// Определяет, должна ли действовать привязка к сетке при текущих модификаторах.
    /// </summary>
    public bool ShouldSnap(KeyModifiers modifiers)
    {
        if (!Options.IsSnapToGridEnabled)
            return false;

        if (IsBypassed(modifiers))
            return false;

        return ResolveStep() > 0;
    }

    /// <summary>
    /// Определяет, отключил ли пользователь привязку модификатором.
    /// </summary>
    /// <remarks>
    /// Модификатор отключает привязку целиком — и к сетке, и к направляющим.
    /// Обещание одно: «держу нажатым — ставлю куда хочу», и делить его между
    /// двумя видами привязки было бы нечестно.
    /// </remarks>
    public bool IsBypassed(KeyModifiers modifiers)
    {
        var bypass = _view.InputGestures.SnapBypassModifiers;
        return bypass != KeyModifiers.None && modifiers.HasFlag(bypass);
    }

    /// <summary>
    /// Возвращает действующий шаг привязки.
    /// </summary>
    /// <remarks>
    /// Явно заданный <see cref="SurfaceInteractionOptions.SnapStep"/> имеет приоритет;
    /// иначе шаг берётся у сетки шаблона. Это не даёт сетке рисовать одну структуру,
    /// а привязке использовать другую.
    /// </remarks>
    public double ResolveStep()
    {
        var configured = Options.SnapStep;
        if (!double.IsNaN(configured))
            return configured;

        return _view.Grid?.CellSize ?? 0;
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
    public double SnapCoordinate(double value)
    {
        var step = ResolveStep();
        return step > 0 ? Math.Floor((value / step) + 0.5) * step : Math.Round(value);
    }

    /// <summary>
    /// Приводит позицию к сетке, если привязка активна.
    /// </summary>
    /// <remarks>
    /// Привязывается именно результат, а не смещение: округление дельты сохранило бы
    /// исходный сдвиг элемента, и на узел сетки он бы так и не встал.
    /// </remarks>
    public Point SnapPosition(Point position, KeyModifiers modifiers)
    {
        if (!ShouldSnap(modifiers))
            return new Point(Math.Round(position.X), Math.Round(position.Y));

        return new Point(SnapCoordinate(position.X), SnapCoordinate(position.Y));
    }

    /// <summary>
    /// Снимает соседей, к которым будет идти выравнивание, на время жеста.
    /// </summary>
    /// <remarks>
    /// Соседи снимаются один раз, а не пересчитываются покадрово, и это не
    /// оптимизация: во время жеста они не двигаются, а вот layout-проход внутри
    /// жеста мог бы сдвинуть те самые линии, в которые пользователь целится.
    /// </remarks>
    public void Begin(Control movingTarget)
    {
        _neighbours = Options.IsSnapToGuidesEnabled
            ? CollectNeighbours(movingTarget)
            : Array.Empty<Rect>();
    }

    /// <summary>
    /// Закрывает жест: сбрасывает снимок соседей и убирает линии.
    /// </summary>
    public void End()
    {
        PublishSpacingHints(Array.Empty<SurfaceSpacingHint>());
        _neighbours = null;
        PublishSnapGuides(Array.Empty<SurfaceSnapGuide>());
    }

    /// <summary>
    /// Ставит прямоугольник на место по направляющим, интервалам и сетке.
    /// </summary>
    /// <param name="proposed">Предполагаемое положение левого верхнего угла.</param>
    /// <param name="size">Размер прямоугольника.</param>
    /// <param name="modifiers">Модификаторы текущего ввода.</param>
    /// <remarks>
    /// Направляющая занимает ось, сетка получает всё остальное. Оси независимы —
    /// элемент может встать на край соседа по X и на узел сетки по Y.
    /// </remarks>
    public Point ResolveOrigin(Point proposed, Size size, KeyModifiers modifiers)
    {
        var neighbours = _neighbours;

        if (neighbours is not { Count: > 0 } || IsBypassed(modifiers))
        {
            PublishSnapGuides(Array.Empty<SurfaceSnapGuide>());
            PublishSpacingHints(Array.Empty<SurfaceSpacingHint>());
            return SnapPosition(proposed, modifiers);
        }

        var snapToGrid = ShouldSnap(modifiers);

        SurfaceSnapGuideResolver.TryResolveOffset(
            new Rect(proposed, size),
            neighbours,
            ResolveTolerance(),
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

        if (Options.IsEqualSpacingEnabled && (!snappedX || !snappedY))
        {
            SurfaceSpacingResolver.TryResolveOffset(
                new Rect(proposed, size),
                neighbours,
                ResolveTolerance(),
                out spacing,
                out spacedX,
                out spacedY);
        }

        var x = Axis(proposed.X, offset.X, spacing.X, snappedX, spacedX);
        var y = Axis(proposed.Y, offset.Y, spacing.Y, snappedY, spacedY);

        var result = new Point(x, y);
        var bounds = new Rect(result, size);

        PublishSnapGuides(SurfaceSnapGuideResolver.CollectGuides(bounds, neighbours));
        PublishSpacingHints(Options.IsEqualSpacingEnabled
            ? SurfaceSpacingResolver.CollectHints(bounds, neighbours)
            : Array.Empty<SurfaceSpacingHint>());

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
    public bool CanSnapEdge(KeyModifiers modifiers)
    {
        if (IsBypassed(modifiers))
            return false;

        return ShouldSnap(modifiers) || HasNeighbours;
    }

    private bool HasNeighbours
        => Options.IsSnapToGuidesEnabled && _neighbours is { Count: > 0 };

    /// <summary>
    /// Возвращает координату двигающегося края с учётом направляющих и сетки.
    /// </summary>
    /// <param name="edge">Координата края по своей оси.</param>
    /// <param name="proposed">Предполагаемая геометрия элемента целиком.</param>
    /// <param name="xAxis">Признак горизонтальной оси.</param>
    /// <param name="farEdge">Признак того, что двигается дальний край.</param>
    /// <param name="modifiers">Модификаторы текущего ввода.</param>
    /// <remarks>
    /// Та же композиция, что и при перетаскивании: направляющая занимает ось,
    /// сетка получает всё остальное. Разница лишь в том, что здесь снимается
    /// один край, а не позиция целиком, — накапливать тут нечего, потому что
    /// край приходит уже посчитанным от применённой геометрии.
    /// </remarks>
    public double ResolveEdge(double edge, Rect proposed, bool xAxis, bool farEdge, KeyModifiers modifiers)
    {
        if (IsBypassed(modifiers))
            return edge;

        if (HasNeighbours &&
            SurfaceSnapGuideResolver.TryResolveEdge(
                edge, _neighbours!, ResolveTolerance(), xAxis, out var guided))
        {
            return guided;
        }

        // Порядок тот же, что при перетаскивании: выравнивание, потом интервал,
        // потом сетка. Разница только во входе — при resize неподвижный край задаёт
        // свой зазор, и вопрос один: где должен встать двигающийся.
        if (HasNeighbours &&
            Options.IsEqualSpacingEnabled &&
            SurfaceSpacingResolver.TryResolveEdge(
                proposed, _neighbours!, ResolveTolerance(), xAxis, farEdge, out var spaced))
        {
            return spaced;
        }

        return ShouldSnap(modifiers) ? SnapCoordinate(edge) : edge;
    }

    /// <summary>
    /// Публикует направляющие по итоговой геометрии жеста изменения размера.
    /// </summary>
    public void PublishApplied(Rect bounds)
    {
        PublishSnapGuides(HasNeighbours
            ? SurfaceSnapGuideResolver.CollectGuides(bounds, _neighbours!)
            : Array.Empty<SurfaceSnapGuide>());

        PublishSpacingHints(HasNeighbours && Options.IsEqualSpacingEnabled
            ? SurfaceSpacingResolver.CollectHints(bounds, _neighbours!)
            : Array.Empty<SurfaceSpacingHint>());
    }

    /// <summary>
    /// Возвращает радиус захвата направляющей в мировых единицах.
    /// </summary>
    private double ResolveTolerance()
    {
        var tolerance = Options.SnapGuideTolerance;
        if (!(tolerance > 0))
            return 0;

        var zoom = _view.ViewportZoom;
        return zoom > 0 ? tolerance / zoom : tolerance;
    }

    /// <summary>
    /// Собирает прямоугольники, по которым идёт выравнивание.
    /// </summary>
    /// <remarks>
    /// Соседями считается то же, что поверхность разрешает выбрать: правило одно,
    /// и выровняться можно ровно по тому, что видно как отдельный элемент.
    /// К ним добавляются границы самого контейнера — по его краям и центру выравнивают
    /// чаще всего, а отдельным элементом он не является.
    /// </remarks>
    private IReadOnlyList<Rect> CollectNeighbours(Control movingTarget)
    {
        var neighbours = new List<Rect>();
        var host = SurfaceView.FindSurfaceHost(movingTarget);

        if (host == null)
        {
            // Двигают контейнер верхнего уровня: соседи — остальные контейнеры.
            foreach (var container in _view.EnumerateContainers())
            {
                if (IsExcluded(container, movingTarget))
                    continue;

                if (_view.Geometry.TryGetBounds(container, out var containerBounds))
                    neighbours.Add(containerBounds);
            }

            neighbours.AddRange(_extraNeighbours());
            return neighbours;
        }

        foreach (var candidate in _view.TargetResolver.EnumerateCandidates(host))
        {
            if (!_view.TargetResolver.IsSelectable(candidate, host))
                continue;

            if (IsExcluded(candidate, movingTarget))
                continue;

            if (_view.Geometry.TryGetBounds(candidate, out var bounds))
                neighbours.Add(bounds);
        }

        if (_view.Geometry.TryGetBounds(host, out var hostBounds))
            neighbours.Add(hostBounds);

        neighbours.AddRange(_extraNeighbours());
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
    private bool IsExcluded(Control candidate, Control movingTarget)
    {
        if (IsSameOrRelated(candidate, movingTarget))
            return true;

        foreach (var selected in _view.SelectedTargetList)
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
    /// Та же дисциплина, что у снимка выделения: метод вызывается на каждом кадре
    /// протяжки, а линии меняются редко. Без сравнения слой перерисовывался бы
    /// каждый кадр впустую.
    /// </remarks>
    private void PublishSnapGuides(IReadOnlyList<SurfaceSnapGuide> guides)
    {
        if (AreSame(SurfaceSnapping.GetSnapGuides(_view), guides))
            return;

        SurfaceSnapping.SetSnapGuides(_view, guides);
    }

    /// <summary>
    /// Публикует подсказки о равных интервалах, если набор изменился.
    /// </summary>
    private void PublishSpacingHints(IReadOnlyList<SurfaceSpacingHint> hints)
    {
        if (AreSame(SurfaceSnapping.GetSpacingHints(_view), hints))
            return;

        SurfaceSnapping.SetSpacingHints(_view, hints);
    }

    private static bool AreSame<T>(IReadOnlyList<T> first, IReadOnlyList<T> second)
        where T : IEquatable<T>
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
