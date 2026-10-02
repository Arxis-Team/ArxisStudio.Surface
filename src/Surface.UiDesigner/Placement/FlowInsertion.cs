using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ArxisStudio.Surface.UiDesigner.Placement;

/// <summary>
/// Место вставки в панель, которая расставляет детей потоком: перед каким ребёнком встать и где
/// это показать.
/// </summary>
/// <remarks>
/// Правило одно на перестановку среди соседей и на бросок: оба спрашивают «между какими детьми окажется
/// указатель», и разные ответы на один вопрос значили бы, что элемент, перенесённый тягой из тулбокса,
/// встаёт не туда, куда встал бы тот же элемент, переставленный мышью.
/// </remarks>
internal static class FlowInsertion
{
    /// <summary>
    /// Находит, перед каким ребёнком панели встанет то, что отпустят в точке.
    /// </summary>
    /// <param name="editor">Редактор, в координатах поверхности которого измерены дети.</param>
    /// <param name="panel">Панель-поток.</param>
    /// <param name="world">Точка в мировых координатах.</param>
    /// <param name="insertBefore">Индекс в <c>Children</c>; число детей — в конец.</param>
    /// <param name="indicator">
    /// Линия нулевой толщины у соседа, перед которым встанет элемент, а в конце — у последнего;
    /// <see langword="null"/>, когда у него нет рамки.
    /// </param>
    /// <returns><see langword="false"/>, когда в панели не нашлось ни одного измеренного ребёнка.</returns>
    public static bool TryResolve(
        UiDesignerView editor,
        Panel panel,
        Point world,
        out int insertBefore,
        out Rect? indicator)
    {
        insertBefore = -1;
        indicator = null;

        var vertical = IsVertical(panel);

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

        for (var i = 0; i < panel.Children.Count; i++)
        {
            if (!editor.TryGetTargetBounds(panel.Children[i], out var childBounds))
                continue;

            var distance = DistanceTo(childBounds, world);
            if (distance >= nearestDistance)
                continue;

            nearest = i;
            nearestDistance = distance;
            nearestBounds = childBounds;
        }

        if (nearest < 0)
            return false;

        // Сторона выбирается уже вдоль потока: до середины соседа — перед ним,
        // после — за ним.
        var position = vertical ? world.Y : world.X;
        var middle = vertical
            ? nearestBounds.Y + (nearestBounds.Height / 2)
            : nearestBounds.X + (nearestBounds.Width / 2);

        insertBefore = position < middle ? nearest : nearest + 1;
        indicator = TryGetIndicatorBounds(editor, panel, insertBefore, vertical, out var bounds) ? bounds : null;
        return true;
    }

    /// <summary>
    /// Ориентация потока панели: вдоль неё выбирается сторона соседа и ложится линия.
    /// </summary>
    public static bool IsVertical(Panel panel) => panel switch
    {
        StackPanel stack => stack.Orientation == Orientation.Vertical,
        WrapPanel wrap => wrap.Orientation == Orientation.Vertical,
        _ => true
    };

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

    private static bool TryGetIndicatorBounds(
        UiDesignerView editor,
        Panel panel,
        int insertBefore,
        bool vertical,
        out Rect bounds)
    {
        bounds = default;

        var atEnd = insertBefore >= panel.Children.Count;
        var anchor = atEnd ? panel.Children.Count - 1 : insertBefore;

        if (anchor < 0 || !editor.TryGetTargetBounds(panel.Children[anchor], out var anchorBounds))
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
}
