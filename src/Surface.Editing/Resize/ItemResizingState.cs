using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ArxisStudio.Surface.States;

namespace ArxisStudio.Surface.Editing;

/// <summary>
/// Состояние изменения размера элемента.
/// </summary>
internal class ItemResizingState : SurfaceItemState
{
    /// <inheritdoc />
    public override string? PseudoClass => ":resizing";

    private readonly Control _target;
    private readonly ResizeDirection _direction;
    private Point _location;
    private Size _size;

    /// <summary>
    /// Указатель, которым начали жест, и насколько он отстоял от двигающегося края.
    /// </summary>
    /// <remarks>
    /// Ручку берут не за край, а за её середину плюс-минус несколько пикселей. Без этой
    /// поправки край на первом же движении прыгнул бы к указателю на величину захвата.
    /// </remarks>
    private PointerSample? _grabSample;
    private Vector _grabOffset;

    /// <summary>
    /// Размер на входе в жест: выше него предел редактора не поднимается.
    /// </summary>
    private Size _startSize;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ItemResizingState"/>.
    /// </summary>
    /// <param name="container">Контейнер, размер которого изменяется.</param>
    /// <param name="target">Visual target, к которому применяется изменение размера.</param>
    /// <param name="direction">Направление активной ручки изменения размера.</param>
    public ItemResizingState(SurfaceItem container, Control target, ResizeDirection direction) : base(container)
    {
        _target = target;
        _direction = direction;
    }

    /// <inheritdoc />
    public override void Enter(SurfaceItemState from)
    {
        var editor = Container.FindAncestorOfType<SurfaceView>();
        _location = editor?.GetTargetPosition(_target) ?? Container.Location;

        var size = editor?.GetTargetSize(_target) ?? new Size(
            double.IsNaN(Container.Width) ? Container.Bounds.Width : Container.Width,
            double.IsNaN(Container.Height) ? Container.Bounds.Height : Container.Height);

        editor?.SetTargetSize(_target, size);
        _size = size;
        _startSize = size;

        _grabSample = editor?.PointerSample;
        if (_grabSample is { } sample)
            _grabOffset = sample.World - SurfaceView.MovingEdge(_direction, new Rect(_location, _size));

        // Соседи снимаются здесь по той же причине, что и при перетаскивании:
        // внутри жеста они не двигаются, и линия обязана стоять там, где её
        // увидел пользователь.
        editor?.BeginSnapGuides(_target);
    }

    /// <inheritdoc />
    public override void Exit()
    {
        Container.FindAncestorOfType<SurfaceView>()?.EndSnapGuides();
    }

    /// <inheritdoc />
    public override void OnResizeDelta(ResizeDeltaEventArgs e)
    {
        // Отсчёт идёт от УЖЕ ПРИМЕНЁННОЙ геометрии, а не от исходной, и дельты
        // не накапливаются. Thumb отдаёт смещение относительно самой ручки, а
        // ручка стоит на применённом крае: всё, что съели привязка, Max или
        // границы формы, вернётся в следующей дельте. Накопление складывало бы
        // этот остаток снова и снова — рамка и контрол начинали прыгать.
        double newW = _size.Width;
        double newH = _size.Height;
        double newX = _location.X;
        double newY = _location.Y;
        var editor = Container.FindAncestorOfType<SurfaceView>();
        // Предел редактора не увеличивает: он существует, чтобы контрол нельзя было
        // схлопнуть в точку и потерять, а про контрол, который автор сделал мельче,
        // он ничего не знает. Иначе первая же протяжка за нижний край подбрасывала бы
        // высоту вверх — и именно так это и выглядело.
        double editorMinSize = Math.Max(0.0, editor?.InteractionOptions.ResizeMinSize ?? 10.0);
        double minWidth = Math.Max(Math.Min(editorMinSize, _startSize.Width), _target.MinWidth);
        double minHeight = Math.Max(Math.Min(editorMinSize, _startSize.Height), _target.MinHeight);

        // Неподвижный край берётся из текущей геометрии, поэтому он остаётся
        // на месте на всём жесте, даже когда размер во что-нибудь упёрся.
        double fixedRight = _location.X + _size.Width;
        double fixedBottom = _location.Y + _size.Height;

        // Дельта считается от указателя, а не от того, успела ли переехать ручка.
        // Смысл величины прежний — «насколько указатель убежал от применённого края», —
        // поэтому остаток, съеденный привязкой или ограничением, по-прежнему возвращается
        // следующим движением, а элемент не отстаёт от курсора. Изменилось только то,
        // чем этот остаток измеряется: раньше положением ручки, теперь самим указателем.
        var delta = e.Delta;
        if (SurfaceView.TryGetGesturePointer(editor?.PointerSample, _grabSample, out var pointer))
            delta = (pointer - _grabOffset) - SurfaceView.MovingEdge(_direction, new Rect(_location, _size));

        double dx = delta.X;
        double dy = delta.Y;

        switch (_direction)
        {
            case ResizeDirection.Right: newW += dx; break;
            case ResizeDirection.Bottom: newH += dy; break;
            case ResizeDirection.Left: newW -= dx; newX += dx; break;
            case ResizeDirection.Top: newH -= dy; newY += dy; break;
            case ResizeDirection.BottomRight: newW += dx; newH += dy; break;
            case ResizeDirection.BottomLeft: newW -= dx; newX += dx; newH += dy; break;
            case ResizeDirection.TopRight: newW += dx; newH -= dy; newY += dy; break;
            case ResizeDirection.TopLeft: newW -= dx; newX += dx; newH -= dy; newY += dy; break;
        }

        // Привязывается двигающийся край, а не размер: иначе у элемента,
        // стоящего мимо сетки, край так и остался бы вне узла. Направляющая
        // занимает свою ось первой, сетка получает остальное — та же композиция,
        // что и при перетаскивании.
        // Модификатор читается на момент нажатия — в аргументах resize его нет.
        if (editor != null && editor.CanSnapResizeEdge(editor.LastInputModifiers))
        {
            var modifiers = editor.LastInputModifiers;

            // Равные интервалы считаются по всему прямоугольнику, а не по одной
            // координате: неподвижный край задаёт зазор, с которым сравнивается второй.
            var proposed = new Rect(newX, newY, newW, newH);

            if (_direction is ResizeDirection.Right or ResizeDirection.TopRight or ResizeDirection.BottomRight)
                newW = editor.ResolveResizeEdge(newX + newW, proposed, xAxis: true, farEdge: true, modifiers) - newX;
            else if (_direction is ResizeDirection.Left or ResizeDirection.TopLeft or ResizeDirection.BottomLeft)
                newW = fixedRight - editor.ResolveResizeEdge(fixedRight - newW, proposed, xAxis: true, farEdge: false, modifiers);

            if (_direction is ResizeDirection.Bottom or ResizeDirection.BottomLeft or ResizeDirection.BottomRight)
                newH = editor.ResolveResizeEdge(newY + newH, proposed, xAxis: false, farEdge: true, modifiers) - newY;
            else if (_direction is ResizeDirection.Top or ResizeDirection.TopLeft or ResizeDirection.TopRight)
                newH = fixedBottom - editor.ResolveResizeEdge(fixedBottom - newH, proposed, xAxis: false, farEdge: false, modifiers);
        }

        // Приведение к Min/Max делается здесь, а не только внутри SetTargetSize:
        // неподвижный край считается из итогового размера, иначе он уплывал бы
        // на разницу между запрошенным и разрешённым.
        if (editor != null)
        {
            // Ограничение по форме применяется до Min/Max: минимум обязан
            // побеждать, иначе контрол схлопнулся бы в ноль у края формы.
            if (editor.TryGetContainmentBounds(_target, out var limit))
            {
                if (ChangesWidth(_direction))
                {
                    var maxW = _direction is ResizeDirection.Left or ResizeDirection.TopLeft or ResizeDirection.BottomLeft
                        ? fixedRight - limit.Left
                        : limit.Right - newX;

                    if (maxW > 0)
                        newW = Math.Min(newW, maxW);
                }

                if (ChangesHeight(_direction))
                {
                    var maxH = _direction is ResizeDirection.Top or ResizeDirection.TopLeft or ResizeDirection.TopRight
                        ? fixedBottom - limit.Top
                        : limit.Bottom - newY;

                    if (maxH > 0)
                        newH = Math.Min(newH, maxH);
                }
            }

            // Предел жеста применяется здесь, а не на шве записи: шов проходят и
            // фиксация текущего размера на входе в жест, и отмена, и им предел
            // не адресован. Min/Max самого контрола сильнее — их накладывает
            // CoerceTargetSize следом.
            var coerced = editor.CoerceTargetSize(
                _target,
                new Size(Math.Max(minWidth, newW), Math.Max(minHeight, newH)));

            newW = coerced.Width;
            newH = coerced.Height;
        }
        else
        {
            newW = Math.Max(minWidth, newW);
            newH = Math.Max(minHeight, newH);
        }

        if (_direction is ResizeDirection.Left or ResizeDirection.TopLeft or ResizeDirection.BottomLeft)
            newX = fixedRight - newW;

        if (_direction is ResizeDirection.Top or ResizeDirection.TopLeft or ResizeDirection.TopRight)
            newY = fixedBottom - newH;

        if (editor != null)
            editor.SetTargetSize(_target, new Size(newW, newH));
        else
        {
            Container.Width = newW;
            Container.Height = newH;
        }

        // Обновляем позицию только если она изменилась (при ресайзе слева/сверху)
        if (Math.Abs(newX - _location.X) > 0.1 || Math.Abs(newY - _location.Y) > 0.1)
        {
            if (editor != null)
                editor.SetTargetPosition(_target, new Point(newX, newY));
            else
                Container.SetCurrentValue(SurfaceItem.LocationProperty, new Point(newX, newY));
        }

        // Запоминаем применённое, а не запрошенное: следующая дельта придёт
        // относительно ручки, которая встанет именно сюда.
        _location = new Point(newX, newY);
        _size = new Size(newW, newH);

        // Линии считаются по применённой геометрии, а не по запрошенной: край
        // мог упереться в минимум или в границу формы, и показывать выравнивание,
        // которого не случилось, нельзя.
        editor?.PublishResizeGuides(new Rect(_location, _size));

        Container.RaiseEvent(new ResizeDeltaEventArgs(e.Delta, _direction, SurfaceItem.ResizeDeltaEvent));
    }

    /// <summary>
    /// Определяет, меняет ли направление ширину.
    /// </summary>
    /// <remarks>
    /// Ограничение применяется только к той оси, которую тянут: иначе оно задним
    /// числом ужимало бы уже вылезший контрол при перетаскивании соседнего края.
    /// </remarks>
    private static bool ChangesWidth(ResizeDirection direction)
        => direction is ResizeDirection.Left or ResizeDirection.Right
            or ResizeDirection.TopLeft or ResizeDirection.TopRight
            or ResizeDirection.BottomLeft or ResizeDirection.BottomRight;

    /// <summary>
    /// Определяет, меняет ли направление высоту.
    /// </summary>
    private static bool ChangesHeight(ResizeDirection direction)
        => direction is ResizeDirection.Top or ResizeDirection.Bottom
            or ResizeDirection.TopLeft or ResizeDirection.TopRight
            or ResizeDirection.BottomLeft or ResizeDirection.BottomRight;
}
