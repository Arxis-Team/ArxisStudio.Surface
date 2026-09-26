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
using DesignLayout = ArxisStudio.Surface.UiDesigner.Layout;
using DesignInteraction = ArxisStudio.Surface.Editing.DesignInteraction;
using ArxisStudio.Controls;
using ArxisStudio.Surface.Editing;
using ArxisStudio.Surface.UiDesigner.Placement;
using ArxisStudio.States;
using ArxisStudio.Surface;

namespace ArxisStudio.Surface.UiDesigner;

// Свойства зависимостей, их обёртки и публичные события.
// Часть DesignEditor; общее описание типа — в DesignEditor.cs.
public partial class DesignEditor
{
    /// <summary>
    /// Идентификатор свойства видимости пользовательских направляющих.
    /// </summary>
    public static readonly StyledProperty<bool> ShowGuidesProperty =
        AvaloniaProperty.Register<DesignEditor, bool>(nameof(ShowGuides), true);

    /// <summary>
    /// Идентификатор свойства видимости линий выравнивания.
    /// </summary>
    public static readonly StyledProperty<bool> ShowSnapGuidesProperty =
        AvaloniaProperty.Register<DesignEditor, bool>(nameof(ShowSnapGuides), true);

    /// <summary>
    /// Идентификатор свойства видимости линеек.
    /// </summary>
    public static readonly StyledProperty<bool> ShowRulersProperty =
        AvaloniaProperty.Register<DesignEditor, bool>(nameof(ShowRulers), true);

    /// <summary>
    /// Идентификатор темы для прямоугольника выделения.
    /// </summary>
    public static readonly StyledProperty<ControlTheme> SelectionRectangleStyleProperty =
        AvaloniaProperty.Register<DesignEditor, ControlTheme>(nameof(SelectionRectangleStyle));

    /// <summary>
    /// Идентификатор свойства имени раскладки primary target.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, string?> PrimarySelectionPlacementProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, string?>(
            nameof(PrimarySelectionPlacement), o => o.PrimarySelectionPlacement);

    /// <summary>
    /// Идентификатор свойства действующей политики перемещения primary target.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, ArxisStudio.Surface.MovePolicy> PrimarySelectionMovePolicyProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, ArxisStudio.Surface.MovePolicy>(
            nameof(PrimarySelectionMovePolicy), o => o.PrimarySelectionMovePolicy);

    /// <summary>
    /// Идентификатор свойства действующей политики изменения размера primary target.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, ArxisStudio.Surface.ResizePolicy> PrimarySelectionResizePolicyProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, ArxisStudio.Surface.ResizePolicy>(
            nameof(PrimarySelectionResizePolicy), o => o.PrimarySelectionResizePolicy);

    /// <summary>
    /// Идентификатор свойства, показывающего активна ли перестановка среди соседей.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, bool> IsReorderingProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, bool>(nameof(IsReordering), o => o.IsReordering);

    /// <summary>
    /// Идентификатор свойства индикатора вставки.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, Rect> ReorderIndicatorProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, Rect>(nameof(ReorderIndicator), o => o.ReorderIndicator);

    /// <summary>
    /// Идентификатор свойства набора активных направляющих.
    /// </summary>
    /// <remarks>
    /// Свойство internal: форму направляющих ещё рано фиксировать публично, а шаблон
    /// библиотеки компилируется в ту же сборку и привязывается к нему без ограничений.
    /// </remarks>
    /// <summary>
    /// Идентификатор свойства пользовательских направляющих.
    /// </summary>
    public static readonly StyledProperty<IEnumerable<DesignGuide>?> GuidesProperty =
        AvaloniaProperty.Register<DesignEditor, IEnumerable<DesignGuide>?>(nameof(Guides));

    /// <summary>
    /// Идентификатор свойства направляющей, показываемой во время её перемещения.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, DesignGuide?> GuidePreviewProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, DesignGuide?>(
            nameof(GuidePreview),
            o => o.GuidePreview);

    /// <summary>
    /// Идентификатор свойства снимка пользовательских направляющих.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, IReadOnlyList<DesignGuide>> UserGuidesProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, IReadOnlyList<DesignGuide>>(
            nameof(UserGuides),
            o => o.UserGuides);

    /// <summary>
    /// Идентификатор свойства подсказок о равных интервалах.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, IReadOnlyList<DesignSpacingHint>> SpacingHintsProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, IReadOnlyList<DesignSpacingHint>>(
            nameof(SpacingHints),
            o => o.SpacingHints);

    /// <summary>
    /// Идентификатор свойства линий выравнивания, найденных во время жеста.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, IReadOnlyList<DesignSnapGuide>> SnapGuidesProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, IReadOnlyList<DesignSnapGuide>>(
            nameof(SnapGuides),
            o => o.SnapGuides);

    /// <summary>
    /// Идентификатор свойства контейнера, в пределах которого работает текущая рамка.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, DesignEditorItem?> MarqueeScopeProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, DesignEditorItem?>(
            nameof(MarqueeScope),
            o => o.MarqueeScope);

    /// <summary>
    /// Идентификатор свойства прямоугольника, охватывающего все размещенные элементы.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, Rect> ItemsExtentProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, Rect>(nameof(ItemsExtent), o => o.ItemsExtent, (o, v) => o.ItemsExtent = v);

    /// <summary>
    /// Идентификатор свойства прямоугольника, охватывающего текущее выделение.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, Rect> SelectionBoundsProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, Rect>(nameof(SelectionBounds), o => o.SelectionBounds, (o, v) => o.SelectionBounds = v);

    /// <summary>
    /// Идентификатор коллекции per-target secondary outlines для multi-selection.
    /// </summary>
    internal static readonly DirectProperty<DesignEditor, IReadOnlyList<SelectionAdornerInfo>> SecondarySelectionAdornersProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, IReadOnlyList<SelectionAdornerInfo>>(
            nameof(SecondarySelectionAdorners),
            o => o.SecondarySelectionAdorners,
            (o, v) => o.SecondarySelectionAdorners = v);

    /// <summary>
    /// Идентификатор количества secondary selection adorner'ов.
    /// </summary>
    private static readonly DirectProperty<DesignEditor, int> SecondarySelectionAdornersCountProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, int>(
            nameof(SecondarySelectionAdornersCount),
            o => o.SecondarySelectionAdornersCount);

    /// <summary>
    /// Идентификатор primary selection target.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, DesignSelectionTarget?> PrimarySelectionTargetProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, DesignSelectionTarget?>(
            nameof(PrimarySelectionTarget),
            o => o.PrimarySelectionTarget);

    /// <summary>
    /// Идентификатор коллекции всех выбранных design targets.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, IReadOnlyList<DesignSelectionTarget>> SelectedDesignTargetsProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, IReadOnlyList<DesignSelectionTarget>>(
            nameof(SelectedDesignTargets),
            o => o.SelectedDesignTargets,
            (o, v) => o.SelectedDesignTargets = v);

    /// <summary>
    /// Идентификатор количества выбранных design targets.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, int> SelectedDesignTargetsCountProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, int>(
            nameof(SelectedDesignTargetsCount),
            o => o.SelectedDesignTargetsCount);

    /// <summary>
    /// Идентификатор свойства, указывающего наличие ровно одного выбранного элемента.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, bool> HasSingleSelectionProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, bool>(nameof(HasSingleSelection), o => o.HasSingleSelection, (o, v) => o.HasSingleSelection = v);

    /// <summary>
    /// Идентификатор свойства, указывающего наличие множественного выделения.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, bool> HasMultipleSelectionProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, bool>(nameof(HasMultipleSelection), o => o.HasMultipleSelection, (o, v) => o.HasMultipleSelection = v);

    /// <summary>
    /// Идентификатор свойства, указывающего, что выбрана design-time группа целиком.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, bool> HasGroupSelectionProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, bool>(nameof(HasGroupSelection), o => o.HasGroupSelection, (o, v) => o.HasGroupSelection = v);

    /// <summary>
    /// Идентификатор свойства, по которому шаблон показывает единую рамку выделения.
    /// </summary>
    /// <remarks>
    /// Internal намеренно: это признак отрисовки, а не состояние выбора. Хосту нужны
    /// <see cref="HasMultipleContainerSelection"/> и <see cref="HasGroupSelection"/>,
    /// а какой рамкой редактор их показывает — его дело.
    /// </remarks>
    internal static readonly DirectProperty<DesignEditor, bool> ShowsGroupFrameProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, bool>(nameof(ShowsGroupFrame), o => o.ShowsGroupFrame, (o, v) => o.ShowsGroupFrame = v);

    /// <summary>
    /// Идентификатор свойства, указывающего на множественное выделение nested targets.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, bool> HasMultipleNestedSelectionProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, bool>(nameof(HasMultipleNestedSelection), o => o.HasMultipleNestedSelection, (o, v) => o.HasMultipleNestedSelection = v);

    /// <summary>
    /// Идентификатор свойства, указывающего на множественное выделение контейнеров.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, bool> HasMultipleContainerSelectionProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, bool>(nameof(HasMultipleContainerSelection), o => o.HasMultipleContainerSelection, (o, v) => o.HasMultipleContainerSelection = v);

    /// <summary>
    /// Получает или задает признак отображения пользовательских направляющих.
    /// </summary>
    /// <remarks>
    /// Прячет линии, но не трогает набор: <see cref="Guides"/> остаётся как был,
    /// и включение возвращает всё на место. Это выключатель показа, а не удаление.
    /// <para>
    /// Спрятанную линию нельзя ни подвинуть, ни вытянуть новую с линейки: жест по
    /// невидимому — худший вид сюрприза. А вот <b>притяжение</b> к ней продолжает
    /// работать, ровно как у сетки, которую <see cref="SurfaceView.ShowGrid"/> тоже только прячет;
    /// выключается оно отдельно, через <c>InteractionOptions.IsSnapToGuidesEnabled</c>.
    /// </para>
    /// </remarks>
    public bool ShowGuides
    {
        get => GetValue(ShowGuidesProperty);
        set => SetValue(ShowGuidesProperty, value);
    }

    /// <summary>
    /// Получает или задает признак отображения линий выравнивания и подсказок об интервалах.
    /// </summary>
    /// <remarks>
    /// Прячет только показ: сами выравнивание и интервалы продолжают работать, как сетка
    /// при выключенном <see cref="SurfaceView.ShowGrid"/>. Отключаются они через
    /// <c>InteractionOptions.IsSnapToGuidesEnabled</c> и <c>IsEqualSpacingEnabled</c>.
    /// <para>
    /// Вместе с <see cref="ShowGuides"/> это способ погасить встроенный слой целиком —
    /// то, что нужно хосту, который рисует направляющие сам, поставив свой
    /// <see cref="Controls.SnapGuideLayer"/> или собственный контрол поверх редактора.
    /// </para>
    /// </remarks>
    public bool ShowSnapGuides
    {
        get => GetValue(ShowSnapGuidesProperty);
        set => SetValue(ShowSnapGuidesProperty, value);
    }

    /// <summary>
    /// Получает или задает признак отображения линеек.
    /// </summary>
    /// <remarks>
    /// Линейка в шаблон редактора не входит — её ставит хост, — поэтому свойство
    /// не прячет её напрямую, а служит общим выключателем: <see cref="Controls.DesignRuler"/>
    /// следит за ним у своего <c>Editor</c> так же, как за масштабом и положением.
    /// Одна настройка гасит обе линейки, и хосту не нужно держать свой флаг.
    /// <para>
    /// Видимость ставится через <c>SetCurrentValue</c>, поэтому собственная привязка
    /// хоста к <c>IsVisible</c> переживает переключение.
    /// </para>
    /// </remarks>
    public bool ShowRulers
    {
        get => GetValue(ShowRulersProperty);
        set => SetValue(ShowRulersProperty, value);
    }

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

    private IReadOnlyList<DesignGuide> _userGuides = Array.Empty<DesignGuide>();

    private DesignGuide? _guidePreview;

    /// <summary>
    /// Получает направляющую, показываемую во время её перемещения.
    /// </summary>
    public DesignGuide? GuidePreview
    {
        get => _guidePreview;
        private set => SetAndRaise(GuidePreviewProperty, ref _guidePreview, value);
    }

    /// <summary>
    /// Получает или задает пользовательские направляющие.
    /// </summary>
    /// <remarks>
    /// Набором владеет хост: редактор его читает, показывает и притягивает к нему элементы,
    /// но не создаёт и не удаляет записи сам — как и с деревом контролов.
    /// <para>
    /// Коллекция, реализующая <see cref="System.Collections.Specialized.INotifyCollectionChanged"/>,
    /// отслеживается: добавленная направляющая появляется и в отрисовке, и в притяжении
    /// без переприсваивания свойства.
    /// </para>
    /// </remarks>
    public IEnumerable<DesignGuide>? Guides
    {
        get => GetValue(GuidesProperty);
        set => SetValue(GuidesProperty, value);
    }

    /// <summary>
    /// Получает снимок пользовательских направляющих для шаблона.
    /// </summary>
    /// <remarks>
    /// Отдельное свойство нужно по той же причине, что и у выделения: привязка
    /// перевычисляется только при смене идентичности значения, а хост вправе держать
    /// одну и ту же коллекцию и менять её содержимое.
    /// </remarks>
    public IReadOnlyList<DesignGuide> UserGuides
    {
        get => _userGuides;
        private set => SetAndRaise(UserGuidesProperty, ref _userGuides, value);
    }

    private IReadOnlyList<DesignSpacingHint> _spacingHints = Array.Empty<DesignSpacingHint>();

    /// <summary>
    /// Получает подсказки о равных интервалах, показанные во время жеста.
    /// </summary>
    public IReadOnlyList<DesignSpacingHint> SpacingHints
    {
        get => _spacingHints;
        private set => SetAndRaise(SpacingHintsProperty, ref _spacingHints, value);
    }

    private IReadOnlyList<DesignSnapGuide> _snapGuides = Array.Empty<DesignSnapGuide>();

    /// <summary>
    /// Получает направляющие, действующие в текущем жесте, в мировых координатах.
    /// </summary>
    /// <remarks>
    /// Набор пуст вне жеста и всегда, когда выравнивания не нашлось. Публикуется он
    /// только при фактическом изменении — см. <see cref="PublishSnapGuides"/>.
    /// </remarks>
    public IReadOnlyList<DesignSnapGuide> SnapGuides
    {
        get => _snapGuides;
        private set => SetAndRaise(SnapGuidesProperty, ref _snapGuides, value);
    }

    private DesignEditorItem? _marqueeScope;

    /// <summary>
    /// Получает контейнер, в пределах которого сейчас работает рамка выделения,
    /// либо <see langword="null"/>, если рамка не активна или работает на уровне контейнеров.
    /// </summary>
    /// <remarks>
    /// Значение пересчитывается на каждом шаге протяжки, поэтому по нему можно
    /// подсвечивать целевой контейнер прямо во время жеста: пользователь видит,
    /// что именно попадёт в выборку, ещё до отпускания кнопки.
    /// <para>
    /// Библиотека не навязывает визуал подсветки — это решение конкретного продукта.
    /// </para>
    /// </remarks>
    public DesignEditorItem? MarqueeScope
    {
        get => _marqueeScope;
        private set => SetAndRaise(MarqueeScopeProperty, ref _marqueeScope, value);
    }

    private Rect _itemsExtent;

    /// <summary>
    /// Получает или задает прямоугольник, охватывающий все дочерние элементы редактора.
    /// </summary>
    public Rect ItemsExtent
    {
        get => _itemsExtent;
        set => SetAndRaise(ItemsExtentProperty, ref _itemsExtent, value);
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

    private DesignSelectionTarget? _primarySelectionTarget;

    /// <summary>
    /// Получает primary selection target редактора.
    /// </summary>
    public DesignSelectionTarget? PrimarySelectionTarget
    {
        get => _primarySelectionTarget;
        private set => SetAndRaise(PrimarySelectionTargetProperty, ref _primarySelectionTarget, value);
    }

    private IReadOnlyList<DesignSelectionTarget> _selectedDesignTargets = Array.Empty<DesignSelectionTarget>();

    /// <summary>
    /// Получает снимок всех выбранных design targets.
    /// </summary>
    public IReadOnlyList<DesignSelectionTarget> SelectedDesignTargets
    {
        get => _selectedDesignTargets;
        private set
        {
            SetAndRaise(SelectedDesignTargetsProperty, ref _selectedDesignTargets, value);
            SetAndRaise(SelectedDesignTargetsCountProperty, ref _selectedDesignTargetsCount, value.Count);
        }
    }

    private int _selectedDesignTargetsCount;

    /// <summary>
    /// Получает количество выбранных design targets.
    /// </summary>
    public int SelectedDesignTargetsCount => _selectedDesignTargetsCount;

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
    /// Получает значение, указывающее, что выбрано несколько контейнеров <see cref="DesignEditorItem"/>.
    /// </summary>
    public bool HasMultipleContainerSelection
    {
        get => _hasMultipleContainerSelection;
        private set => SetAndRaise(HasMultipleContainerSelectionProperty, ref _hasMultipleContainerSelection, value);
    }

    /// <summary>
    /// Получает коллекцию провайдеров действий контекстного меню.
    /// </summary>
    public IList<IDesignEditorContextActionProvider> ContextActionProviders { get; } = new List<IDesignEditorContextActionProvider>();

    /// <summary>
    /// Возникает перед показом контекстного меню.
    /// </summary>
    public event EventHandler<DesignEditorContextRequestingEventArgs>? ContextMenuRequesting;

    /// <summary>
    /// Возникает после разрешения контекста и списка действий.
    /// </summary>
    public event EventHandler<DesignEditorContextRequestedEventArgs>? ContextMenuResolved;

    /// <summary>
    /// Возникает при запросе удаления выделения с клавиатуры.
    /// </summary>
    /// <remarks>
    /// Редактор не владеет коллекцией элементов и удалять их не может: обработчик
    /// должен выполнить удаление сам и выставить
    /// <see cref="DesignEditorDeleteRequestedEventArgs.Handled"/>.
    /// </remarks>
    public event EventHandler<DesignEditorDeleteRequestedEventArgs>? DeleteRequested;

    /// <summary>
    /// Возникает, когда пользователь перетащил контрол на новое место среди соседей.
    /// </summary>
    /// <remarks>
    /// Деревом контролов редактор не владеет: структурную правку выполняет
    /// библиотека разметки. Обработчик должен переставить контрол сам и выставить
    /// <see cref="DesignEditorReorderRequestedEventArgs.Handled"/>.
    /// </remarks>
    public event EventHandler<DesignEditorReorderRequestedEventArgs>? ReorderRequested;

    /// <summary>
    /// Возникает, когда пользователь просит отменить последнюю правку.
    /// </summary>
    /// <remarks>
    /// Стек правок принадлежит хосту, поэтому редактор ничего не отменяет сам.
    /// Без подписчика нажатие остаётся необработанным и всплывает дальше.
    /// </remarks>
    public event EventHandler<DesignEditorHistoryRequestedEventArgs>? UndoRequested;

    /// <summary>
    /// Возникает, когда пользователь просит повторить отменённую правку.
    /// </summary>
    public event EventHandler<DesignEditorHistoryRequestedEventArgs>? RedoRequested;

    /// <summary>
    /// Возникает, когда пользователь просит изменить набор направляющих.
    /// </summary>
    /// <remarks>
    /// Набором владеет хост, поэтому редактор его не правит сам. Пока обработчик
    /// не выставил <c>Handled</c>, направляющая остаётся там, где была.
    /// <para>
    /// Без подписчика жест перемещения направляющей не начинается вовсе.
    /// </para>
    /// </remarks>
    public event EventHandler<DesignGuideChangeRequestedEventArgs>? GuideChangeRequested;

    /// <summary>
    /// Возникает при изменении набора выбранных design targets.
    /// </summary>
    /// <remarks>
    /// Это не то же, что унаследованное <see cref="SelectingItemsControl.SelectionChanged"/>:
    /// то работает на уровне элементов <c>ItemsSource</c>, а это — на уровне design targets,
    /// включая вложенные контролы и вложенные контейнеры.
    /// <para>
    /// Событие возникает только при фактической смене набора или primary target.
    /// Перетаскивание и изменение размера его не поднимают, хотя внутренний снимок
    /// пересобирается на каждом кадре.
    /// </para>
    /// </remarks>
    public event EventHandler<DesignSelectionChangedEventArgs>? DesignSelectionChanged;

    /// <summary>
    /// Получает или задает presenter контекстных действий.
    /// </summary>
    public IDesignEditorContextPresenter ContextPresenter { get; set; } = new ContextMenuContextPresenter();
}
