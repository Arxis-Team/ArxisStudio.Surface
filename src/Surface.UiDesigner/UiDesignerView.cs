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
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Metadata;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SurfaceLayout = ArxisStudio.Surface.UiDesigner.Layout;
using SurfaceInteraction = ArxisStudio.Surface.Editing.SurfaceInteraction;
using ArxisStudio.Surface.Editing;
using ArxisStudio.Surface.UiDesigner.Placement;
using ArxisStudio.Surface;

namespace ArxisStudio.Surface.UiDesigner;

/// <summary>
/// Представляет поверхность визуального редактора с поддержкой панорамирования,
/// масштабирования, множественного выделения, перетаскивания и изменения размеров элементов.
/// </summary>
/// <remarks>
/// Контрол наследуется от <see cref="SelectingItemsControl"/> и использует
/// <see cref="UiDesignerItem"/> в качестве контейнера для элементов коллекции.
/// <para>
/// Для корректной работы визуальных стилей необходимо подключить словари ресурсов
/// из каталога <c>Themes/Styles</c> библиотеки.
/// </para>
/// </remarks>
/// <example>
/// <code language="xml"><![CDATA[
/// <design:UiDesignerView ItemsSource="{Binding Nodes}"
///                      SelectedItems="{Binding SelectedNodes}"
///                      SelectionMode="Multiple"
///                      ViewportZoom="{Binding Zoom, Mode=TwoWay}" />
/// ]]></code>
/// </example>
public partial class UiDesignerView : SurfaceView
{
    private SelectionAdorner? _selectionAdorner;

    private SelectionAdorner? _groupSelectionAdorner;

    private SelectionAdornerLayer? _secondarySelectionAdornerLayer;

    private readonly GroupEditFacet _groupFacet;

    private UiDesignerItem? _primarySelectionItem;

    private Control? _primarySelectionControl;

    // Targets, на изменения свойств которых редактор сейчас подписан.
    // Ведётся отдельно от _selectedTargets: следить нужно за разрешёнными
    // targets, включая default'ные для item'ов без явного выбора.
    private readonly List<Control> _subscribedTargets = new();

    private GroupResizeOperation? _groupResizeOperation;


    static UiDesignerView()
    {
        UiDesignerItem.ResizeDeltaEvent.AddClassHandler<UiDesignerView>((x, e) => x.OnItemsResizeDelta(e));
        // Геометрия и политики выбранных targets отслеживаются точечно —
        // подпиской на сами targets, см. SyncSelectedTargetSubscriptions.
        // Раньше здесь висели AddClassHandler<Control> на Bounds, SurfaceX/SurfaceY
        // и политики: они срабатывали на любой Control во всём приложении
        // и на каждое срабатывание поднимались по дереву в поисках редактора.
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="UiDesignerView"/>.
    /// </summary>
    public UiDesignerView()
    {
        // Инструменты редактирования подключаются службами (ADR 0003). Служба
        // направляющих ставит свой туннельный обработчик нажатия первой — так же,
        // как стоял обработчик редактора до выделения службы.
        _snap = new SnapService(this, () => GetService<UserGuideService>()?.CollectNeighbours() ?? Array.Empty<Rect>());
        AddService(_snap);
        AddService(new UserGuideService(this));

        // Позицию target'а знает его панель, а не ядро: стратегия размещения решает,
        // примет ли содержимое запись. Пометка группы — участник единицы редактирования
        // сверх геометрии, и в контракт изменений она попадает через него.
        Geometry = new SurfacePlacementGeometry(this);
        TargetResolver = NestedTargetResolver.Instance;
        _groupFacet = new GroupEditFacet(this);
        AddEditFacet(_groupFacet);

        // Действующая политика — пересечение: блокировки человека и то, что умеет
        // раскладка. Ни одна не расширяет другую.
        AddInteractionPolicy(SurfaceInteractionLockPolicy.Instance);
        AddInteractionPolicy(PlacementMovePolicy.Instance);

        _groupStoreBridge = new GroupStoreBridge(this);
        AttachGroupStore(_groupStore);

        UpdateSelectionOverlayState();
    }

    /// <summary>
    /// Определяет необходимость создания контейнера <see cref="UiDesignerItem"/> для элемента коллекции.
    /// </summary>
    /// <param name="item">Элемент источника данных.</param>
    /// <param name="index">Индекс элемента.</param>
    /// <param name="recycleKey">Ключ повторного использования контейнера.</param>
    /// <returns><see langword="true"/>, если для элемента требуется контейнер.</returns>
    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        return NeedsContainer<UiDesignerItem>(item, out recycleKey);
    }

    /// <summary>
    /// Создает контейнер визуального элемента редактора.
    /// </summary>
    /// <param name="item">Элемент источника данных.</param>
    /// <param name="index">Индекс элемента.</param>
    /// <param name="recycleKey">Ключ повторного использования контейнера.</param>
    /// <returns>Новый <see cref="UiDesignerItem"/>, а с привязкой корня — <see cref="UiDesignerFormItem"/>.</returns>
    /// <remarks>
    /// С <see cref="ItemRootBinding"/> контейнер — <see cref="UiDesignerFormItem"/>: элемент коллекции
    /// называет корень документа, и держать его должен элемент формы (ADR 0020). Свой тип контейнера
    /// наследник редактора создаёт здесь же; привязку корня получит любой наследник
    /// <see cref="UiDesignerFormItem"/>.
    /// </remarks>
    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
    {
        return ItemRootBinding != null ? new UiDesignerFormItem() : new UiDesignerItem();
    }

    /// <summary>
    /// Идентификатор свойства привязки корня документа элемента.
    /// </summary>
    public static readonly StyledProperty<BindingBase?> ItemRootBindingProperty =
        AvaloniaProperty.Register<UiDesignerView, BindingBase?>(nameof(ItemRootBinding));

    /// <summary>
    /// Получает или задает привязку, дающую контейнеру корень документа его элемента.
    /// </summary>
    /// <remarks>
    /// Тот же приём, что <see cref="SurfaceView.ItemLocationBinding"/>: привязка применяется к
    /// <see cref="UiDesignerFormItem.Root"/>, когда контейнер готовится, с элементом коллекции в качестве
    /// контекста данных — <c>ItemRootBinding="{Binding Root}"</c>. Заданная, она же решает, каким быть
    /// контейнеру: редактор создаёт <see cref="UiDesignerFormItem"/> вместо <see cref="UiDesignerItem"/>,
    /// и хосту не нужно класть элементы формы в коллекцию самому.
    /// <para>
    /// Корень, пересобранный обновлением, модель отдаёт тем же свойством — привязка ставит его элементу
    /// заново, а элемент остаётся тем же и держит место и выбор. Контейнер, который отпускают, корень
    /// возвращает: привязка снимается, и всё взятое у корня идёт обратно.
    /// </para>
    /// <para>
    /// Готовый контейнер из коллекции не трогается. Смена привязки действует на контейнеры,
    /// подготовленные после неё.
    /// </para>
    /// </remarks>
    [AssignBinding]
    [InheritDataTypeFromItems(nameof(ItemsSource))]
    public BindingBase? ItemRootBinding
    {
        get => GetValue(ItemRootBindingProperty);
        set => SetValue(ItemRootBindingProperty, value);
    }

    /// <summary>
    /// Даёт элементу формы корень его документа привязкой <see cref="ItemRootBinding"/>.
    /// </summary>
    /// <param name="container">Контейнер.</param>
    /// <param name="item">Элемент источника данных.</param>
    /// <param name="index">Индекс элемента.</param>
    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);

        if (container is not UiDesignerFormItem form || ReferenceEquals(container, item) || ItemRootBinding is not { } root)
            return;

        form.RootBinding?.Dispose();
        form.RootBinding = form.Bind(UiDesignerFormItem.RootProperty, root);
    }

    /// <summary>
    /// Отпускает контейнер: элемент формы возвращает корню всё взятое.
    /// </summary>
    /// <param name="container">Контейнер.</param>
    /// <remarks>
    /// Корень, оставленный у отпущенного контейнера, так и стоял бы без содержимого, ресурсов и стилей, а
    /// следующий элемент формы получил бы от него отказ: корень держит один элемент. Корень, который хост
    /// поставил контейнеру сам, без привязки, не трогается.
    /// </remarks>
    protected override void ClearContainerForItemOverride(Control container)
    {
        base.ClearContainerForItemOverride(container);

        if (container is not UiDesignerFormItem { RootBinding: { } binding } form)
            return;

        binding.Dispose();
        form.RootBinding = null;
        form.Root = null;
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

    /// <inheritdoc />
    private protected override void RefreshSelectionOverlay() => UpdateSelectionOverlayState();

    /// <inheritdoc />
    internal override string? GetGroupKey(Control target) => _groupStore.GetGroup(target);
}
