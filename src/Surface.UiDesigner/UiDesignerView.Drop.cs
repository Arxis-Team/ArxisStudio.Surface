using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using ArxisStudio.Surface.UiDesigner.Placement;
using AvaloniaGrid = Avalonia.Controls.Grid;

namespace ArxisStudio.Surface.UiDesigner;

// Бросок: куда ляжет контрол, принесённый тягой, и индикатор этого места.
// Часть UiDesignerView; общее описание типа — в UiDesignerView.cs.
public partial class UiDesignerView
{
    /// <summary>
    /// Идентификатор свойства <see cref="DropIndicator"/>.
    /// </summary>
    public static readonly DirectProperty<UiDesignerView, SurfaceDropPlacement?> DropIndicatorProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerView, SurfaceDropPlacement?>(nameof(DropIndicator), o => o.DropIndicator);

    // Части индикатора для шаблона: линия между соседями и область, которая примет контрол. Internal —
    // шаблон в той же сборке; хосту хватает DropIndicator.
    internal static readonly DirectProperty<UiDesignerView, Rect> DropLineProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerView, Rect>(nameof(DropLine), o => o.DropLine);

    internal static readonly DirectProperty<UiDesignerView, bool> HasDropLineProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerView, bool>(nameof(HasDropLine), o => o.HasDropLine);

    internal static readonly DirectProperty<UiDesignerView, Rect> DropAreaProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerView, Rect>(nameof(DropArea), o => o.DropArea);

    internal static readonly DirectProperty<UiDesignerView, bool> HasDropAreaProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerView, bool>(nameof(HasDropArea), o => o.HasDropArea);

    private SurfaceDropPlacement? _dropIndicator;
    private Rect _dropLine;
    private bool _hasDropLine;
    private Rect _dropArea;
    private bool _hasDropArea;

    /// <summary>
    /// Получает размещение, которое сейчас показывает индикатор броска, или <see langword="null"/>.
    /// </summary>
    public SurfaceDropPlacement? DropIndicator
    {
        get => _dropIndicator;
        private set => SetAndRaise(DropIndicatorProperty, ref _dropIndicator, value);
    }

    internal Rect DropLine
    {
        get => _dropLine;
        private set => SetAndRaise(DropLineProperty, ref _dropLine, value);
    }

    internal bool HasDropLine
    {
        get => _hasDropLine;
        private set => SetAndRaise(HasDropLineProperty, ref _hasDropLine, value);
    }

    internal Rect DropArea
    {
        get => _dropArea;
        private set => SetAndRaise(DropAreaProperty, ref _dropArea, value);
    }

    internal bool HasDropArea
    {
        get => _hasDropArea;
        private set => SetAndRaise(HasDropAreaProperty, ref _hasDropArea, value);
    }

    /// <summary>
    /// Решает, куда ляжет контрол, отпущенный в точке холста.
    /// </summary>
    /// <param name="positionInEditor">Точка в координатах редактора — то, что даёт <c>e.GetPosition(editor)</c>.</param>
    /// <param name="placement">Размещение, когда бросок принимается.</param>
    /// <param name="dragged">
    /// Контрол формы, который несут, если несут его, а не новый: ни он, ни его содержимое не примут сами
    /// себя, и якорем вставки он не станет — место рядом с ним называется соседом за ним.
    /// </param>
    /// <returns>
    /// <see langword="false"/>, когда под точкой нет формы, когда ни один контрол под ней не может
    /// принять ребёнка или когда холст стоит на стоп-кадре.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Бросок решает дизайнер, а исполняет хост (ADR 0024). Редактор называет родителя и место, хост пишет
    /// документ и рисует индикатор через <see cref="ShowDropIndicator"/>, если согласен. Форматы данных тяги
    /// редактор не знает: что несут и можно ли это бросить, решает хост.
    /// </para>
    /// <para>
    /// Родитель — самый глубокий контрол формы под точкой, который может принять ребёнка. Попадание
    /// считается по прямоугольникам, как выбор: загруженная форма ввода не берёт, а панель без фона
    /// не попадает под указатель вовсе.
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="Canvas"/> и <see cref="AbsolutePanel"/> — <see cref="SurfaceDropKind.Position"/>: точка в их
    /// координатах.</item>
    /// <item><see cref="StackPanel"/> и <see cref="WrapPanel"/> — <see cref="SurfaceDropKind.Insert"/> по правилу
    /// перестановки: ближайший сосед, сторона — вдоль потока.</item>
    /// <item><see cref="AvaloniaGrid"/> — <see cref="SurfaceDropKind.Cell"/>: строка и столбец под точкой, с учётом
    /// промежутков.</item>
    /// <item>Прочие панели — <see cref="SurfaceDropKind.Insert"/> в конец.</item>
    /// <item>Пустые <see cref="Decorator"/> и <see cref="ContentControl"/>, пустая выбранная страница
    /// <see cref="TabControl"/>, окно без содержимого — <see cref="SurfaceDropKind.Content"/>.</item>
    /// </list>
    /// <para>
    /// Контрол с содержимым ребёнка не примет: кнопка с текстом, рамка с ребёнком. Бросок на них уходит
    /// родителю, рядом с ними. Заменять содержимое молча значило бы удалить то, что там было.
    /// </para>
    /// </remarks>
    public bool TryResolveDropPlacement(
        Point positionInEditor,
        [NotNullWhen(true)] out SurfaceDropPlacement? placement,
        Control? dragged = null)
    {
        placement = null;

        if (IsFrozen)
            return false;

        var world = GetWorldPosition(positionInEditor);
        if (FindContainerAtWorldPoint(world) is not UiDesignerItem container)
            return false;

        if (!TryFindDropHost(container, world, dragged, out var host))
        {
            // Окно без содержимого не даёт формы, в которой искать: принимает оно само. Спрашивается
            // элемент, а не окно: пока элемент держит окно, содержимое окна занято им, и Content пуст
            // у всякого окна.
            if (container is UiDesignerFormItem { Root: ContentControl root, IsTopLevel: true, HasContent: false }
                && TryGetContainerWorldBounds(container, out var form)
                && form.Contains(world))
            {
                placement = new SurfaceDropPlacement(
                    container, root, SurfaceDropKind.Content, 0, default, 0, 0, null, form);
                return true;
            }

            return false;
        }

        var position = this.TranslatePoint(positionInEditor, host.Parent) ?? default;

        placement = host.Kind switch
        {
            SurfaceDropKind.Insert => Insert(container, host, world, position, dragged),
            SurfaceDropKind.Cell => Cell(container, host, position),
            _ => new SurfaceDropPlacement(
                container, host.Parent, host.Kind, 0, position, 0, 0, null, host.Bounds),
        };

        return true;
    }

    /// <summary>
    /// Показывает место броска: линию между соседями или область, которая примет контрол.
    /// </summary>
    /// <param name="placement">Размещение, которое вернул <see cref="TryResolveDropPlacement"/>.</param>
    /// <remarks>
    /// Хост зовёт его на каждом движении тяги, а равное размещение уведомления не поднимает.
    /// Убирает индикатор <see cref="HideDropIndicator"/> — на уходе тяги и на броске.
    /// </remarks>
    public void ShowDropIndicator(SurfaceDropPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);

        DropIndicator = placement;

        var line = placement.IsLine;
        DropLine = line ? placement.Indicator : default;
        DropArea = line ? default : placement.Indicator;
        HasDropLine = line;
        HasDropArea = !line;
    }

    /// <summary>
    /// Убирает индикатор броска.
    /// </summary>
    public void HideDropIndicator()
    {
        DropIndicator = null;
        HasDropLine = false;
        HasDropArea = false;
        DropLine = default;
        DropArea = default;
    }

    /// <summary>Родитель броска: кто примет, как и в каком прямоугольнике.</summary>
    private readonly record struct DropHost(Control Parent, SurfaceDropKind Kind, Rect Bounds);

    /// <summary>
    /// Самый глубокий контрол формы под точкой, который может принять ребёнка.
    /// </summary>
    /// <remarks>
    /// Кандидаты те же, что у выбора: что редактор даёт выбрать, то и принимает бросок. При равной
    /// глубине побеждает меньший — так же, как при выборе.
    /// </remarks>
    private bool TryFindDropHost(UiDesignerItem container, Point world, Control? dragged, out DropHost host)
    {
        host = default;
        var found = false;
        var bestDepth = -1;

        foreach (var candidate in TargetResolver.EnumerateCandidates(container))
        {
            if (!TargetResolver.IsSelectable(candidate, container) || IsCarried(candidate, dragged))
                continue;

            if (!TryGetDropHost(candidate, world, out var offer))
                continue;

            var depth = GetVisualDepth(candidate, container) + (ReferenceEquals(offer.Parent, candidate) ? 0 : 1);
            var smaller = offer.Bounds.Width * offer.Bounds.Height < host.Bounds.Width * host.Bounds.Height;

            if (!found || depth > bestDepth || (depth == bestDepth && smaller))
            {
                host = offer;
                bestDepth = depth;
                found = true;
            }
        }

        return found;
    }

    /// <summary>Несут ли этот контрол или то, в чём он лежит.</summary>
    private static bool IsCarried(Control candidate, Control? dragged) =>
        dragged is not null && (ReferenceEquals(candidate, dragged) || dragged.IsVisualAncestorOf(candidate));

    /// <summary>
    /// Может ли контрол принять ребёнка в этой точке, и как.
    /// </summary>
    private bool TryGetDropHost(Control candidate, Point world, out DropHost host)
    {
        host = default;

        // Страница TabControl показывается не там, где стоит её вкладка, а в области содержимого самого
        // TabControl. Пустая выбранная страница принимает бросок в этой области.
        if (candidate is TabControl { SelectedIndex: >= 0 } tabs
            && tabs.ContainerFromIndex(tabs.SelectedIndex) is TabItem { Content: null } page
            && SelectedContentArea(tabs) is { } area
            && area.Contains(world))
        {
            host = new DropHost(page, SurfaceDropKind.Content, area);
            return true;
        }

        if (DropKindOf(candidate) is not { } kind
            || !TryGetTargetBounds(candidate, out var bounds)
            || !bounds.Contains(world))
        {
            return false;
        }

        host = new DropHost(candidate, kind, bounds);
        return true;
    }

    /// <summary>Как контрол принимает ребёнка, по его раскладке; <see langword="null"/> — никак.</summary>
    private static SurfaceDropKind? DropKindOf(Control control) => control switch
    {
        Canvas or AbsolutePanel => SurfaceDropKind.Position,
        AvaloniaGrid => SurfaceDropKind.Cell,
        Panel => SurfaceDropKind.Insert,
        Decorator { Child: null } => SurfaceDropKind.Content,
        ContentControl { Content: null } => SurfaceDropKind.Content,
        _ => null,
    };

    /// <summary>Область, где TabControl показывает выбранную страницу, в координатах поверхности.</summary>
    private Rect? SelectedContentArea(TabControl tabs)
    {
        var presenter = tabs.GetVisualDescendants()
            .OfType<ContentPresenter>()
            .FirstOrDefault(candidate => candidate.Name == "PART_SelectedContentHost"
                && ReferenceEquals(candidate.TemplatedParent, tabs));

        return presenter is not null && TryGetTargetBounds(presenter, out var bounds) ? bounds : null;
    }

    /// <summary>Вставка в панель: в потоке — по правилу перестановки, в прочих — в конец.</summary>
    private SurfaceDropPlacement Insert(
        UiDesignerItem container,
        DropHost host,
        Point world,
        Point position,
        Control? dragged)
    {
        var panel = (Panel)host.Parent;

        if (panel is StackPanel or WrapPanel
            && FlowInsertion.TryResolve(this, panel, world, out var insertBefore, out var line))
        {
            // Сосед без рамки линии не даёт: место то же, а показывается вся панель.
            return new SurfaceDropPlacement(
                container,
                panel,
                SurfaceDropKind.Insert,
                insertBefore,
                position,
                0,
                0,
                AnchorAt(panel, insertBefore, dragged),
                line ?? host.Bounds);
        }

        // Пустой поток и панель, которая сама решает, где ребёнку стоять: в конец, и показывается вся панель.
        return new SurfaceDropPlacement(
            container, panel, SurfaceDropKind.Insert, panel.Children.Count, position, 0, 0, null, host.Bounds);
    }

    /// <summary>
    /// Сосед, перед которым встанет контрол: ребёнок в позиции вставки, а если это тот, кого несут, —
    /// следующий за ним.
    /// </summary>
    /// <remarks>
    /// Место перед несомым и место за ним — одно и то же место: несомый уйдёт оттуда. Якорем он быть не
    /// может — вставка «перед самим собой» хосту ничего не говорит, — поэтому место называется соседом,
    /// который останется на месте.
    /// </remarks>
    private static Control? AnchorAt(Panel panel, int insertBefore, Control? dragged)
    {
        for (var i = insertBefore; i < panel.Children.Count; i++)
        {
            if (!ReferenceEquals(panel.Children[i], dragged))
                return panel.Children[i];
        }

        return null;
    }

    /// <summary>Ячейка сетки под точкой.</summary>
    private static SurfaceDropPlacement Cell(UiDesignerItem container, DropHost host, Point position)
    {
        var grid = (AvaloniaGrid)host.Parent;

        var (column, x, width) = Track(
            grid.ColumnDefinitions.Select(static definition => definition.ActualWidth).ToList(),
            grid.ColumnSpacing,
            position.X,
            grid.Bounds.Width);

        var (row, y, height) = Track(
            grid.RowDefinitions.Select(static definition => definition.ActualHeight).ToList(),
            grid.RowSpacing,
            position.Y,
            grid.Bounds.Height);

        var cell = new Rect(host.Bounds.X + x, host.Bounds.Y + y, width, height);

        return new SurfaceDropPlacement(
            container, grid, SurfaceDropKind.Cell, grid.Children.Count, position, row, column, null, cell);
    }

    /// <summary>
    /// Дорожка сетки, в которую попадает координата: индекс, начало и размер её ячейки.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ActualWidth</c> и <c>ActualHeight</c> дорожки включают промежуток после неё — у последней тоже:
    /// так сетка Avalonia 12 исполняет <c>ColumnSpacing</c> и <c>RowSpacing</c>. Замер на 12.1.1: столбцы
    /// 100 и 150 с промежутком 10 отвечают 110 и 160, а дети встают на 0 и 110 шириной 100 и 150. Ячейка
    /// поэтому начинается на сумме прежних дорожек и меньше своей дорожки на промежуток. Поменяет Avalonia
    /// это правило — первым упадёт <c>DropPlacementTests.Drop_Over_Grid_Resolves_A_Cell</c>.
    /// </para>
    /// <para>
    /// Промежуток отдаётся следующей дорожке: указатель между столбцами целится уже за край предыдущего.
    /// За последней дорожкой — последняя: точка у края сетки всё ещё в ней.
    /// </para>
    /// </remarks>
    private static (int Index, double Start, double Size) Track(
        IReadOnlyList<double> sizes,
        double spacing,
        double at,
        double total)
    {
        if (sizes.Count == 0)
            return (0, 0, total);

        var start = 0.0;
        for (var i = 0; i < sizes.Count; i++)
        {
            var cell = Math.Max(0, sizes[i] - spacing);
            var next = start + sizes[i];

            if (at < start + cell || i == sizes.Count - 1)
                return (i, start, cell);

            if (at < next)
                return (i + 1, next, Math.Max(0, sizes[i + 1] - spacing));

            start = next;
        }

        return (sizes.Count - 1, start, Math.Max(0, sizes[^1] - spacing));
    }
}
