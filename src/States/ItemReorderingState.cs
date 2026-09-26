using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using ArxisStudio.Controls;
using ArxisStudio.Surface;

namespace ArxisStudio.States;

/// <summary>
/// Состояние перестановки контрола среди соседей по родительской панели.
/// </summary>
/// <remarks>
/// Работает там, где позицией распоряжается раскладка: в такой панели
/// перетаскивание не может задать координату, но может изменить порядок.
/// <para>
/// Перестановка применяется на отпускании, а не покадрово: если двигать контрол
/// прямо во время жеста, панель переливается под курсором и элемент прыгает.
/// Пока идёт протяжка, показывается только индикатор точки вставки.
/// </para>
/// </remarks>
internal sealed class ItemReorderingState : DesignEditorItemState
{
    private readonly Point _initialPointerPosition;
    private readonly GestureCursorScope _cursor = new GestureCursorScope();
    private Control? _target;
    private Panel? _panel;
    private int _initialIndex = -1;
    private int _insertBefore = -1;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ItemReorderingState"/>.
    /// </summary>
    /// <param name="container">Контейнер, внутри которого идёт перестановка.</param>
    /// <param name="initialPointerPosition">Начальная позиция указателя в координатах редактора.</param>
    public ItemReorderingState(DesignEditorItem container, Point initialPointerPosition)
        : base(container)
    {
        _initialPointerPosition = initialPointerPosition;
    }

    /// <inheritdoc />
    public override void Enter(DesignEditorItemState from)
    {
        var editor = Container.FindAncestorOfType<DesignEditor>();
        if (editor == null)
            return;

        _target = editor.ResolveInteractionTarget(Container);
        _panel = _target.GetVisualParent() as Panel;
        _initialIndex = _panel?.Children.IndexOf(_target) ?? -1;
        _insertBefore = _initialIndex;

        UpdateIndicator(editor, _initialPointerPosition);

        // Курсор ставится контейнеру: захват взял он, ещё в ItemIdleState.
        _cursor.Apply(Container, editor.Cursors.ResolveReorder());
    }

    /// <inheritdoc />
    public override void OnPointerMoved(PointerEventArgs e)
    {
        var editor = Container.FindAncestorOfType<DesignEditor>();
        if (editor == null)
            return;

        UpdateIndicator(editor, e.GetPosition(editor));
        e.Handled = true;
    }

    /// <inheritdoc />
    public override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var editor = Container.FindAncestorOfType<DesignEditor>();

        var handled = false;

        if (editor != null && _target != null && _initialIndex >= 0)
        {
            // Точка вставки уходит как есть: перевод в индекс переноса делает
            // редактор, там же, где читает текущую позицию. Обе половины запроса
            // обязаны быть сняты за одно чтение.
            handled = editor.CommitReorder(_target, _insertBefore);
        }
        else
        {
            editor?.CancelReorder();
        }

        Container.PopState();
        e.Pointer.Capture(null);

        // Отказ обработчика оставляет нажатие необработанным — как и у Delete.
        e.Handled = handled;
    }

    /// <inheritdoc />
    public override void Exit()
    {
        Container.FindAncestorOfType<DesignEditor>()?.CancelReorder();
        _cursor.Restore();
    }

    /// <summary>
    /// Расстояние от точки до прямоугольника; ноль, если точка внутри.
    /// </summary>
    /// <remarks>
    /// По прямоугольнику, а не по центру: тогда поперечная ось лишь отсекает чужие
    /// ряды, а внутри ряда выбор идёт вдоль потока — как и задумано. По центрам
    /// поперечная ось начинала соревноваться с продольной, и ширина соседа влияла
    /// на то, между какими элементами встанет перетаскиваемый.
    /// </remarks>
    private static double DistanceTo(Rect bounds, Point point)
    {
        var dx = Math.Max(0, Math.Max(bounds.X - point.X, point.X - bounds.Right));
        var dy = Math.Max(0, Math.Max(bounds.Y - point.Y, point.Y - bounds.Bottom));
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private void UpdateIndicator(DesignEditor editor, Point pointerInEditor)
    {
        if (_panel == null || _panel.Children.Count == 0)
            return;

        var world = editor.GetWorldPosition(pointerInEditor);
        var vertical = IsVertical(_panel);

        // Ближайший сосед ищется по обеим осям, а не по одной вдоль потока.
        // В панели с переносом одной оси мало: точка в нижнем ряду сравнивалась бы
        // с серединами верхнего, и элемент уезжал в чужой ряд. В однорядной
        // раскладке вторая ось у всех детей общая и на выбор не влияет,
        // поэтому правило остаётся одно на оба случая.
        //
        // Расстояние считается до прямоугольника ребёнка, а не до его центра.
        // По центрам ответ зависел от того, за какое место схватили элемент:
        // в колонке из широких строк узкая кнопка внизу прижата влево, её центр
        // по X близок к левому краю строк — и указатель у левого края оказывался
        // ближе к ней, чем к центрам самих строк. Точка вставки прыгала в конец
        // списка, стоило взять элемент слева, и вела себя верно, если взять справа.
        var nearest = -1;
        var nearestDistance = double.PositiveInfinity;
        var nearestBounds = default(Rect);

        for (var i = 0; i < _panel.Children.Count; i++)
        {
            if (!editor.TryGetDesignBounds(_panel.Children[i], out var childBounds))
                continue;

            var distance = DistanceTo(childBounds, world);
            if (distance >= nearestDistance)
                continue;

            nearest = i;
            nearestDistance = distance;
            nearestBounds = childBounds;
        }

        if (nearest < 0)
            return;

        // Сторона выбирается уже вдоль потока: до середины соседа — перед ним,
        // после — за ним.
        var position = vertical ? world.Y : world.X;
        var middle = vertical
            ? nearestBounds.Y + (nearestBounds.Height / 2)
            : nearestBounds.X + (nearestBounds.Width / 2);

        _insertBefore = position < middle ? nearest : nearest + 1;

        if (TryGetIndicatorBounds(editor, vertical, out var indicator))
            editor.UpdateReorderIndicator(indicator);
    }

    private bool TryGetIndicatorBounds(DesignEditor editor, bool vertical, out Rect bounds)
    {
        bounds = default;

        if (_panel == null)
            return false;

        var atEnd = _insertBefore >= _panel.Children.Count;
        var anchor = atEnd ? _panel.Children.Count - 1 : _insertBefore;

        if (anchor < 0 || !editor.TryGetDesignBounds(_panel.Children[anchor], out var anchorBounds))
            return false;

        // Линия натянута по соседу, а не по всей панели: в раскладке с переносом
        // она обязана оставаться в своём ряду, иначе показывает точку вставки
        // сразу во всех. В однорядной раскладке сосед занимает всю ширину,
        // поэтому видимо ничего не меняется.
        // Толщина нулевая: видимую задаёт шаблон, поэтому линия остаётся
        // одинаково тонкой на любом масштабе.
        bounds = vertical
            ? new Rect(anchorBounds.X, atEnd ? anchorBounds.Bottom : anchorBounds.Y, anchorBounds.Width, 0)
            : new Rect(atEnd ? anchorBounds.Right : anchorBounds.X, anchorBounds.Y, 0, anchorBounds.Height);

        return true;
    }

    private static bool IsVertical(Panel panel) => panel switch
    {
        StackPanel stack => stack.Orientation == Orientation.Vertical,
        WrapPanel wrap => wrap.Orientation == Orientation.Vertical,
        _ => true
    };
}
