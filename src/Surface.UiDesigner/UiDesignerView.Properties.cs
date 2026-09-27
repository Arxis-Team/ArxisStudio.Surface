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
using Avalonia.Utilities;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SurfaceLayout = ArxisStudio.Surface.UiDesigner.Layout;
using SurfaceInteraction = ArxisStudio.Surface.Editing.SurfaceInteraction;
using ArxisStudio.Surface.Editing;
using ArxisStudio.Surface.UiDesigner.Placement;
using ArxisStudio.Surface;

namespace ArxisStudio.Surface.UiDesigner;

// Свойства зависимостей, их обёртки и публичные события.
// Часть UiDesignerView; общее описание типа — в UiDesignerView.cs.
public partial class UiDesignerView
{
    /// <summary>
    /// Идентификатор темы для прямоугольника выделения.
    /// </summary>
    public static readonly StyledProperty<ControlTheme> SelectionRectangleStyleProperty =
        AvaloniaProperty.Register<UiDesignerView, ControlTheme>(nameof(SelectionRectangleStyle));

    /// <summary>
    /// Идентификатор свойства имени раскладки primary target.
    /// </summary>
    public static readonly DirectProperty<UiDesignerView, string?> PrimarySelectionPlacementProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerView, string?>(
            nameof(PrimarySelectionPlacement), o => o.PrimarySelectionPlacement);

    /// <summary>
    /// Идентификатор свойства действующей политики перемещения primary target.
    /// </summary>
    public static readonly DirectProperty<UiDesignerView, ArxisStudio.Surface.MovePolicy> PrimarySelectionMovePolicyProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerView, ArxisStudio.Surface.MovePolicy>(
            nameof(PrimarySelectionMovePolicy), o => o.PrimarySelectionMovePolicy);

    /// <summary>
    /// Идентификатор свойства действующей политики изменения размера primary target.
    /// </summary>
    public static readonly DirectProperty<UiDesignerView, ArxisStudio.Surface.ResizePolicy> PrimarySelectionResizePolicyProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerView, ArxisStudio.Surface.ResizePolicy>(
            nameof(PrimarySelectionResizePolicy), o => o.PrimarySelectionResizePolicy);

    /// <summary>
    /// Идентификатор свойства, показывающего активна ли перестановка среди соседей.
    /// </summary>
    public static readonly DirectProperty<UiDesignerView, bool> IsReorderingProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerView, bool>(nameof(IsReordering), o => o.IsReordering);

    /// <summary>
    /// Идентификатор свойства индикатора вставки.
    /// </summary>
    public static readonly DirectProperty<UiDesignerView, Rect> ReorderIndicatorProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerView, Rect>(nameof(ReorderIndicator), o => o.ReorderIndicator);

    /// <summary>
    /// Идентификатор свойства прямоугольника, охватывающего текущее выделение.
    /// </summary>
    public static readonly DirectProperty<UiDesignerView, Rect> SelectionBoundsProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerView, Rect>(nameof(SelectionBounds), o => o.SelectionBounds, (o, v) => o.SelectionBounds = v);

    /// <summary>
    /// Идентификатор коллекции per-target secondary outlines для multi-selection.
    /// </summary>
    internal static readonly DirectProperty<UiDesignerView, IReadOnlyList<SelectionAdornerInfo>> SecondarySelectionAdornersProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerView, IReadOnlyList<SelectionAdornerInfo>>(
            nameof(SecondarySelectionAdorners),
            o => o.SecondarySelectionAdorners,
            (o, v) => o.SecondarySelectionAdorners = v);

    /// <summary>
    /// Идентификатор количества secondary selection adorner'ов.
    /// </summary>
    private static readonly DirectProperty<UiDesignerView, int> SecondarySelectionAdornersCountProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerView, int>(
            nameof(SecondarySelectionAdornersCount),
            o => o.SecondarySelectionAdornersCount);

    /// <summary>
    /// Идентификатор свойства, указывающего наличие ровно одного выбранного элемента.
    /// </summary>
    public static readonly DirectProperty<UiDesignerView, bool> HasSingleSelectionProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerView, bool>(nameof(HasSingleSelection), o => o.HasSingleSelection, (o, v) => o.HasSingleSelection = v);

    /// <summary>
    /// Идентификатор свойства, указывающего наличие множественного выделения.
    /// </summary>
    public static readonly DirectProperty<UiDesignerView, bool> HasMultipleSelectionProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerView, bool>(nameof(HasMultipleSelection), o => o.HasMultipleSelection, (o, v) => o.HasMultipleSelection = v);

    /// <summary>
    /// Идентификатор свойства, указывающего, что выбрана design-time группа целиком.
    /// </summary>
    public static readonly DirectProperty<UiDesignerView, bool> HasGroupSelectionProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerView, bool>(nameof(HasGroupSelection), o => o.HasGroupSelection, (o, v) => o.HasGroupSelection = v);

    /// <summary>
    /// Идентификатор свойства, по которому шаблон показывает единую рамку выделения.
    /// </summary>
    /// <remarks>
    /// Internal намеренно: это признак отрисовки, а не состояние выбора. Хосту нужны
    /// <see cref="HasMultipleContainerSelection"/> и <see cref="HasGroupSelection"/>,
    /// а какой рамкой редактор их показывает — его дело.
    /// </remarks>
    internal static readonly DirectProperty<UiDesignerView, bool> ShowsGroupFrameProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerView, bool>(nameof(ShowsGroupFrame), o => o.ShowsGroupFrame, (o, v) => o.ShowsGroupFrame = v);

    /// <summary>
    /// Идентификатор свойства, указывающего на множественное выделение nested targets.
    /// </summary>
    public static readonly DirectProperty<UiDesignerView, bool> HasMultipleNestedSelectionProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerView, bool>(nameof(HasMultipleNestedSelection), o => o.HasMultipleNestedSelection, (o, v) => o.HasMultipleNestedSelection = v);

    /// <summary>
    /// Идентификатор свойства, указывающего на множественное выделение контейнеров.
    /// </summary>
    public static readonly DirectProperty<UiDesignerView, bool> HasMultipleContainerSelectionProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerView, bool>(nameof(HasMultipleContainerSelection), o => o.HasMultipleContainerSelection, (o, v) => o.HasMultipleContainerSelection = v);

    /// <summary>
    /// Получает или задает тему визуализации рамки выделения.
    /// </summary>
    public ControlTheme SelectionRectangleStyle
    {
        get => GetValue(SelectionRectangleStyleProperty);
        set => SetValue(SelectionRectangleStyleProperty, value);
    }

    /// <summary>
    /// Ретранслятор изменений хранилища групп; заведён в конструкторе.
    /// </summary>
    private readonly GroupStoreBridge _groupStoreBridge;

    private string? _primarySelectionPlacement;

    /// <summary>
    /// Получает имя раскладки, которая распоряжается положением primary target.
    /// </summary>
    /// <remarks>
    /// Отвечает на вопрос «почему этот контрол не двигается»: <c>Stack</c> означает,
    /// что панель расставляет детей сама и перетаскивание меняет их порядок, а не
    /// координату; <c>Grid</c> и <c>Dock</c> — что положение задаётся присоединёнными
    /// свойствами раскладки. <see langword="null"/> — выделения нет.
    /// </remarks>
    public string? PrimarySelectionPlacement
    {
        get => _primarySelectionPlacement;
        private set => SetAndRaise(PrimarySelectionPlacementProperty, ref _primarySelectionPlacement, value);
    }

    private ArxisStudio.Surface.MovePolicy _primarySelectionMovePolicy;

    /// <summary>
    /// Получает действующую политику перемещения primary target.
    /// </summary>
    /// <remarks>
    /// Именно действующую, а не заданную: это <c>политика пользователя &amp; возможности
    /// раскладки</c>, то есть то, что редактор реально позволит сделать.
    /// </remarks>
    public ArxisStudio.Surface.MovePolicy PrimarySelectionMovePolicy
    {
        get => _primarySelectionMovePolicy;
        private set => SetAndRaise(PrimarySelectionMovePolicyProperty, ref _primarySelectionMovePolicy, value);
    }

    private ArxisStudio.Surface.ResizePolicy _primarySelectionResizePolicy;

    /// <summary>
    /// Получает действующую политику изменения размера primary target.
    /// </summary>
    /// <remarks>
    /// Раскладка её не сужает: явный размер honours любая панель, потому что
    /// применяется до выравнивания. Ограничивают размер <c>Min</c>/<c>Max</c>
    /// самого контрола и границы формы, а не родительская раскладка.
    /// </remarks>
    public ArxisStudio.Surface.ResizePolicy PrimarySelectionResizePolicy
    {
        get => _primarySelectionResizePolicy;
        private set => SetAndRaise(PrimarySelectionResizePolicyProperty, ref _primarySelectionResizePolicy, value);
    }

    private bool _isReordering;

    /// <summary>
    /// Получает признак активной перестановки контрола среди соседей.
    /// </summary>
    public bool IsReordering
    {
        get => _isReordering;
        private set => SetAndRaise(IsReorderingProperty, ref _isReordering, value);
    }

    private Rect _reorderIndicator;

    /// <summary>
    /// Получает прямоугольник индикатора вставки в мировых координатах.
    /// </summary>
    /// <remarks>
    /// Толщина линии намеренно нулевая: на экране её задаёт шаблон, поэтому
    /// индикатор остаётся одинаково тонким на любом масштабе.
    /// </remarks>
    public Rect ReorderIndicator
    {
        get => _reorderIndicator;
        private set => SetAndRaise(ReorderIndicatorProperty, ref _reorderIndicator, value);
    }

    private Rect _selectionBounds;

    /// <summary>
    /// Получает или задает прямоугольник, охватывающий текущее выделение.
    /// </summary>
    public Rect SelectionBounds
    {
        get => _selectionBounds;
        private set => SetAndRaise(SelectionBoundsProperty, ref _selectionBounds, value);
    }

    private IReadOnlyList<SelectionAdornerInfo> _secondarySelectionAdorners = Array.Empty<SelectionAdornerInfo>();

    /// <summary>
    /// Получает коллекцию per-target secondary adorner'ов для multi-selection.
    /// </summary>
    internal IReadOnlyList<SelectionAdornerInfo> SecondarySelectionAdorners
    {
        get => _secondarySelectionAdorners;
        private set
        {
            SetAndRaise(SecondarySelectionAdornersProperty, ref _secondarySelectionAdorners, value);
            SetAndRaise(SecondarySelectionAdornersCountProperty, ref _secondarySelectionAdornersCount, value.Count);
        }
    }

    private int _secondarySelectionAdornersCount;

    /// <summary>
    /// Получает количество secondary adorner'ов в текущем multi-selection overlay.
    /// </summary>
    private int SecondarySelectionAdornersCount => _secondarySelectionAdornersCount;

    private bool _hasSingleSelection;

    /// <summary>
    /// Получает значение, указывающее, что в редакторе выбран ровно один элемент.
    /// </summary>
    public bool HasSingleSelection
    {
        get => _hasSingleSelection;
        private set => SetAndRaise(HasSingleSelectionProperty, ref _hasSingleSelection, value);
    }

    private bool _hasMultipleSelection;

    /// <summary>
    /// Получает значение, указывающее, что в редакторе выбрано более одного элемента.
    /// </summary>
    public bool HasMultipleSelection
    {
        get => _hasMultipleSelection;
        private set => SetAndRaise(HasMultipleSelectionProperty, ref _hasMultipleSelection, value);
    }

    private bool _hasGroupSelection;

    /// <summary>
    /// Получает значение, указывающее, что выделение — это ровно одна design-time группа.
    /// </summary>
    /// <remarks>
    /// Группа рисуется одной рамкой и ведёт себя как один элемент. Признак снимается,
    /// когда в группу вошли двойным кликом: внутри неё выбран уже конкретный контрол.
    /// </remarks>
    public bool HasGroupSelection
    {
        get => _hasGroupSelection;
        private set => SetAndRaise(HasGroupSelectionProperty, ref _hasGroupSelection, value);
    }

    private bool _showsGroupFrame;

    /// <summary>
    /// Получает значение, указывающее, что выделение показывается единой рамкой.
    /// </summary>
    internal bool ShowsGroupFrame
    {
        get => _showsGroupFrame;
        private set => SetAndRaise(ShowsGroupFrameProperty, ref _showsGroupFrame, value);
    }

    private bool _hasMultipleNestedSelection;

    /// <summary>
    /// Получает значение, указывающее, что выбрано несколько nested targets внутри одного контейнера.
    /// </summary>
    public bool HasMultipleNestedSelection
    {
        get => _hasMultipleNestedSelection;
        private set => SetAndRaise(HasMultipleNestedSelectionProperty, ref _hasMultipleNestedSelection, value);
    }

    private bool _hasMultipleContainerSelection;

    /// <summary>
    /// Получает значение, указывающее, что выбрано несколько контейнеров <see cref="UiDesignerItem"/>.
    /// </summary>
    public bool HasMultipleContainerSelection
    {
        get => _hasMultipleContainerSelection;
        private set => SetAndRaise(HasMultipleContainerSelectionProperty, ref _hasMultipleContainerSelection, value);
    }

    /// <summary>
    /// Возникает, когда пользователь перетащил контрол на новое место среди соседей.
    /// </summary>
    /// <remarks>
    /// Деревом контролов редактор не владеет: структурную правку выполняет
    /// библиотека разметки. Обработчик должен переставить контрол сам и выставить
    /// <see cref="UiDesignerReorderRequestedEventArgs.Handled"/>.
    /// </remarks>
    public event EventHandler<UiDesignerReorderRequestedEventArgs>? ReorderRequested;
}
