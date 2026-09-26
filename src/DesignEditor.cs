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
using Avalonia.Interactivity;
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

namespace ArxisStudio;

/// <summary>
/// Представляет поверхность визуального редактора с поддержкой панорамирования,
/// масштабирования, множественного выделения, перетаскивания и изменения размеров элементов.
/// </summary>
/// <remarks>
/// Контрол наследуется от <see cref="SelectingItemsControl"/> и использует
/// <see cref="DesignEditorItem"/> в качестве контейнера для элементов коллекции.
/// <para>
/// Для корректной работы визуальных стилей необходимо подключить словари ресурсов
/// из каталога <c>Themes/Styles</c> библиотеки.
/// </para>
/// </remarks>
/// <example>
/// <code language="xml"><![CDATA[
/// <design:DesignEditor ItemsSource="{Binding Nodes}"
///                      SelectedItems="{Binding SelectedNodes}"
///                      SelectionMode="Multiple"
///                      ViewportZoom="{Binding Zoom, Mode=TwoWay}" />
/// ]]></code>
/// </example>
public partial class DesignEditor : SelectingItemsControl
{
    private const double ZoomTolerance = 0.0001;

    private const double FitToViewPadding = 32.0;

    private Point _lastMousePosition;

    internal KeyModifiers LastInputModifiers { get; private set; }

    private SelectionAdorner? _selectionAdorner;

    private SelectionAdorner? _groupSelectionAdorner;

    private SelectionAdornerLayer? _secondarySelectionAdornerLayer;

    private DesignGrid? _grid;

    private DesignEditorItem? _primarySelectionItem;

    private Control? _primarySelectionControl;

    // Выбранные design targets в порядке приоритета: первый — primary.
    // Контейнеры и вложенные контролы лежат вместе: контейнер, выбранный целиком,
    // это просто DesignEditorItem в списке. Владелец каждого target вычисляется
    // по дереву, поэтому структура не привязана к глубине вложенности.
    private readonly List<Control> _selectedTargets = new();

    // Targets, на изменения свойств которых редактор сейчас подписан.
    // Ведётся отдельно от _selectedTargets: следить нужно за разрешёнными
    // targets, включая default'ные для item'ов без явного выбора.
    private readonly List<Control> _subscribedTargets = new();

    private GroupResizeOperation? _groupResizeOperation;

    private GroupDragOperation? _groupDragOperation;

    // Текущая единица редактирования. Живёт от начала жеста до его завершения:
    // все мутации проходят через SetDesignPosition/SetDesignSize и попадают в неё.
    private DesignEditScope? _activeEdit;

    // Подавляет запись на время программного применения геометрии,
    // чтобы отмена не превращалась в новое изменение.
    private bool _suppressEditRecording;

    // Соседи, к которым идёт выравнивание в текущем жесте. Снимаются один раз
    // на входе в жест; null означает, что жест не идёт.
    private IReadOnlyList<Rect>? _snapGuideNeighbours;

    /// <summary>
    /// Снимок контейнеров на время жеста; <c>null</c> вне жеста.
    /// </summary>
    private IReadOnlyList<DesignEditorItem>? _containerSnapshot;

    private readonly TranslateTransform _translateTransform = new TranslateTransform();

    private readonly ScaleTransform _scaleTransform = new ScaleTransform();

    private readonly TranslateTransform _dpiTranslateTransform = new TranslateTransform();

    // TopLevel, с которого читается RenderScaling и на который подписан ScalingChanged.
    // Разрешается один раз при подключении к дереву, чтобы подписка и чтение DPI
    // не расходились между собой.
    private TopLevel? _scalingHost;

    static DesignEditor()
    {
        FocusableProperty.OverrideDefaultValue<DesignEditor>(true);
        ViewportLocationProperty.Changed.AddClassHandler<DesignEditor>((x, e) => x.UpdateTransforms());
        ViewportZoomProperty.Changed.AddClassHandler<DesignEditor>((x, e) => x.UpdateTransforms());
        GuidesProperty.Changed.AddClassHandler<DesignEditor>((x, e) => x.OnGuidesSourceChanged(e));

        DesignEditorItem.DragStartedEvent.AddClassHandler<DesignEditor>((x, e) => x.OnItemsDragStarted(e));
        DesignEditorItem.DragDeltaEvent.AddClassHandler<DesignEditor>((x, e) => x.OnItemsDragDelta(e));
        DesignEditorItem.DragCompletedEvent.AddClassHandler<DesignEditor>((x, e) => x.OnItemsDragCompleted(e));
        DesignEditorItem.ResizeDeltaEvent.AddClassHandler<DesignEditor>((x, e) => x.OnItemsResizeDelta(e));
        DesignEditorItem.IsSelectedProperty.Changed.AddClassHandler<DesignEditorItem>((item, _) =>
        {
            if (item.FindAncestorOfType<DesignEditor>() is { } editor)
                editor.UpdateSelectionOverlayState();
        });
        DesignEditorItem.LocationProperty.Changed.AddClassHandler<DesignEditorItem>((item, _) =>
        {
            if (item.IsSelected && item.FindAncestorOfType<DesignEditor>() is { } editor)
                editor.UpdateSelectionOverlayState();
        });
        // Геометрия и политики выбранных targets отслеживаются точечно —
        // подпиской на сами targets, см. SyncSelectedTargetSubscriptions.
        // Раньше здесь висели AddClassHandler<Control> на Bounds, DesignX/DesignY
        // и политики: они срабатывали на любой Control во всём приложении
        // и на каждое срабатывание поднимались по дереву в поисках редактора.
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignEditor"/>.
    /// </summary>
    public DesignEditor()
    {
        // Нажатие на направляющую перехватывается в фазе туннелирования: линия
        // нарисована поверх всего, значит и жест должна забирать раньше контейнера
        // под ней. Через всплытие это не сделать — контейнер обработает нажатие
        // первым, захватит указатель и начнёт своё перетаскивание.
        AddHandler(PointerPressedEvent, OnTunnelPointerPressed, RoutingStrategies.Tunnel);

        // Положение указателя нужно изменению размера, а ручка о нём не сообщает.
        // Туннель и handledEventsToo: во время жеста указатель захвачен ручкой,
        // и она помечает движение обработанным.
        AddHandler(PointerMovedEvent, OnTrackPointer, RoutingStrategies.Tunnel, handledEventsToo: true);
        SelectionMode = SelectionMode.Multiple;
        // Набор создан инициализатором поля и здесь только подхватывается: прежде
        // конструктор заводил второй и первый выбрасывал, а подписан оказывался
        // ровно один из двух — ошибиться в такой паре легко и молча.
        _inputGestureBridge = new InputGestureBridge(this);
        _groupStoreBridge = new GroupStoreBridge(this);
        AttachGroupStore(_groupStore);
        _containerInteractionModifiers = _inputGestures.ContainerInteractionModifiers;
        _additiveSelectionModifiers = _inputGestures.AdditiveSelectionModifiers;
        AttachInputGestures(_inputGestures);

        var contentGroup = new TransformGroup();
        contentGroup.Children.Add(_scaleTransform);
        contentGroup.Children.Add(_translateTransform);
        SetCurrentValue(ViewportTransformProperty, contentGroup);

        var dpiGroup = new TransformGroup();
        dpiGroup.Children.Add(_scaleTransform);
        dpiGroup.Children.Add(_dpiTranslateTransform);
        SetCurrentValue(DpiScaledViewportTransformProperty, dpiGroup);

        _states.Push(new EditorIdleState(this));
        UpdateSelectionOverlayState();
    }

    /// <summary>
    /// Определяет необходимость создания контейнера <see cref="DesignEditorItem"/> для элемента коллекции.
    /// </summary>
    /// <param name="item">Элемент источника данных.</param>
    /// <param name="index">Индекс элемента.</param>
    /// <param name="recycleKey">Ключ повторного использования контейнера.</param>
    /// <returns><see langword="true"/>, если для элемента требуется контейнер.</returns>
    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        return NeedsContainer<DesignEditorItem>(item, out recycleKey);
    }

    /// <summary>
    /// Создает контейнер визуального элемента редактора.
    /// </summary>
    /// <param name="item">Элемент источника данных.</param>
    /// <param name="index">Индекс элемента.</param>
    /// <param name="recycleKey">Ключ повторного использования контейнера.</param>
    /// <returns>Новый экземпляр <see cref="DesignEditorItem"/>.</returns>
    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
    {
        return new DesignEditorItem();
    }

    /// <summary>
    /// Применяет шаблон редактора и подключает overlay-элементы к обработчикам взаимодействия.
    /// </summary>
    /// <param name="e">Аргументы применения шаблона.</param>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_selectionAdorner != null)
        {
            _selectionAdorner.ResizeStarted -= OnSelectionResizeStarted;
            _selectionAdorner.ResizeDelta -= OnSelectionResizeDelta;
            _selectionAdorner.ResizeCompleted -= OnSelectionResizeCompleted;
        }

        if (_groupSelectionAdorner != null)
        {
            _groupSelectionAdorner.ResizeStarted -= OnGroupSelectionResizeStarted;
            _groupSelectionAdorner.ResizeDelta -= OnGroupSelectionResizeDelta;
            _groupSelectionAdorner.ResizeCompleted -= OnGroupSelectionResizeCompleted;
        }

        if (_secondarySelectionAdornerLayer != null)
        {
            _secondarySelectionAdornerLayer.AdornerResizeStarted -= OnSecondarySelectionResizeStarted;
            _secondarySelectionAdornerLayer.AdornerResizeDelta -= OnSecondarySelectionResizeDelta;
            _secondarySelectionAdornerLayer.AdornerResizeCompleted -= OnSecondarySelectionResizeCompleted;
        }

        _grid = e.NameScope.Find<DesignGrid>("PART_Grid");
        _selectionAdorner = e.NameScope.Find<SelectionAdorner>("PART_SelectionAdorner");
        _groupSelectionAdorner = e.NameScope.Find<SelectionAdorner>("PART_GroupSelectionAdorner");
        _secondarySelectionAdornerLayer = e.NameScope.Find<SelectionAdornerLayer>("PART_SecondarySelectionAdorners");

        if (_selectionAdorner != null)
        {
            _selectionAdorner.ResizeStarted += OnSelectionResizeStarted;
            _selectionAdorner.ResizeDelta += OnSelectionResizeDelta;
            _selectionAdorner.ResizeCompleted += OnSelectionResizeCompleted;
        }

        if (_groupSelectionAdorner != null)
        {
            _groupSelectionAdorner.ResizeStarted += OnGroupSelectionResizeStarted;
            _groupSelectionAdorner.ResizeDelta += OnGroupSelectionResizeDelta;
            _groupSelectionAdorner.ResizeCompleted += OnGroupSelectionResizeCompleted;
        }

        if (_secondarySelectionAdornerLayer != null)
        {
            _secondarySelectionAdornerLayer.AdornerResizeStarted += OnSecondarySelectionResizeStarted;
            _secondarySelectionAdornerLayer.AdornerResizeDelta += OnSecondarySelectionResizeDelta;
            _secondarySelectionAdornerLayer.AdornerResizeCompleted += OnSecondarySelectionResizeCompleted;
        }

        UpdateSelectionAdornerPolicies();
    }
}
