using System;
using System.Collections;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Metadata;
using Avalonia.VisualTree;
using ArxisStudio.Surface.States;

namespace ArxisStudio.Surface;

/// <summary>
/// Бесконечная поверхность: основа любого редактора на холсте.
/// </summary>
/// <remarks>
/// Ядро «глупое» и не знает, что лежит на холсте, — форма, узел графа или фигура
/// (ADR 0003). Всё, что относится к содержимому, добавляют слои выше: инструменты
/// редактирования — службами, дизайнер интерфейса — наследником <c>UiDesignerView</c>.
/// <para>
/// Члены переезжают сюда из дизайнера интерфейса по шагам, и каждый шаг оставляет
/// поведение прежним; пока класс — точка, от которой наследуется редактор форм.
/// </para>
/// </remarks>
public partial class SurfaceView : SelectingItemsControl
{
    static SurfaceView()
    {
        // Фокус поверхность просит сама, на нажатии (OnPointerPressed), но получает его
        // только фокусируемый элемент. Разрешение стояло у дизайнера интерфейса, и голая
        // поверхность щелчком по пустому холсту клавиатуры не получала.
        FocusableProperty.OverrideDefaultValue<SurfaceView>(true);

        ViewportLocationProperty.Changed.AddClassHandler<SurfaceView>((x, _) => x.UpdateTransforms());
        ViewportZoomProperty.Changed.AddClassHandler<SurfaceView>((x, _) => x.UpdateTransforms());

        SurfaceItem.DragStartedEvent.AddClassHandler<SurfaceView>((x, e) => x.OnItemsDragStarted(e));
        SurfaceItem.DragDeltaEvent.AddClassHandler<SurfaceView>((x, e) => x.OnItemsDragDelta(e));
        SurfaceItem.DragCompletedEvent.AddClassHandler<SurfaceView>((x, e) => x.OnItemsDragCompleted(e));

        // Индексный слой пишут и мимо редактора — SelectedIndex, SelectedItems хоста.
        // Выделение обязано опубликоваться и тогда, а сдвиг выбранного — пересобрать его.
        SurfaceItem.IsSelectedProperty.Changed.AddClassHandler<SurfaceItem>((item, _) =>
            item.FindAncestorOfType<SurfaceView>()?.RefreshSelectionOverlay());
        SurfaceItem.LocationProperty.Changed.AddClassHandler<SurfaceItem>((item, _) =>
        {
            if (item.IsSelected)
                item.FindAncestorOfType<SurfaceView>()?.RefreshSelectionOverlay();
        });

        // Контейнер встал на новое место, сменил размер или спрятан — сменилось содержимое холста.
        // Спрятанный своих границ не меняет, поэтому видимость слушается отдельно.
        SurfaceItem.BoundsProperty.Changed.AddClassHandler<SurfaceItem>((item, _) =>
            item.FindAncestorOfType<SurfaceView>()?.OnContentChanged());
        SurfaceItem.IsVisibleProperty.Changed.AddClassHandler<SurfaceItem>((item, _) =>
            item.FindAncestorOfType<SurfaceView>()?.OnContentChanged());
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SurfaceView"/>.
    /// </summary>
    public SurfaceView()
    {
        // Набор создан инициализатором поля и здесь только подхватывается: прежде
        // конструктор заводил второй и первый выбрасывал, а подписан оказывался
        // ровно один из двух — ошибиться в такой паре легко и молча.
        _inputGestureBridge = new InputGestureBridge(this);
        _containerInteractionModifiers = _inputGestures.ContainerInteractionModifiers;
        _additiveSelectionModifiers = _inputGestures.AdditiveSelectionModifiers;
        AttachInputGestures(_inputGestures);

        // Рамка и Shift + клик набирают несколько элементов: одиночный режим
        // SelectingItemsControl заменял бы каждый следующий выбор предыдущим.
        SelectionMode = SelectionMode.Multiple;

        _states.Push(new EditorIdleState(this));

        // Положение указателя нужно изменению размера, а ручка о нём не сообщает.
        // Туннель и handledEventsToo: во время жеста указатель захвачен ручкой,
        // и она помечает движение обработанным.
        AddHandler(PointerMovedEvent, OnTrackPointer, RoutingStrategies.Tunnel, handledEventsToo: true);

        AttachPinchZoom();

        var contentGroup = new TransformGroup();
        contentGroup.Children.Add(_scaleTransform);
        contentGroup.Children.Add(_translateTransform);
        SetCurrentValue(ViewportTransformProperty, contentGroup);

        var dpiGroup = new TransformGroup();
        dpiGroup.Children.Add(_scaleTransform);
        dpiGroup.Children.Add(_dpiTranslateTransform);
        SetCurrentValue(DpiScaledViewportTransformProperty, dpiGroup);

        // Нажатие в упрощённом виде разворачивает элемент под собой раньше всех, кто решает по
        // нажатию мимо контейнеров, — рамки и попадания по связям редактора узлов (ADR 0008).
        AddHandler(PointerPressedEvent, OnSimplifiedPointerPressed, RoutingStrategies.Tunnel);

        ItemsView.CollectionChanged += (_, e) =>
        {
            OnContentChanged();
            ForgetPressedItem(e);
        };
        WatchSelection(Selection);
    }

    /// <summary>
    /// Возникает, когда сменилось содержимое холста: контейнер сдвинулся, сменил размер, спрятан,
    /// появился или ушёл, — или слой выше сменил своё, как связи редактора узлов.
    /// </summary>
    /// <remarks>
    /// Для тех, кто рисует холст целиком, как миникарта (ADR 0007). Проход раскладки окна
    /// поднимается и от чужих контролов, а это событие — только от своего содержимого.
    /// </remarks>
    internal event EventHandler? ContentChanged;

    /// <summary>
    /// Сообщает, что содержимое холста сменилось.
    /// </summary>
    internal void OnContentChanged() => ContentChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Возникает, когда сменилась геометрия элемента без контейнера — его модель сдвинулась, пока он
    /// свёрнут, или он ушёл из коллекции; <see langword="null"/> — геометрия прочитана заново целиком.
    /// </summary>
    /// <remarks>
    /// Для слоёв, которые держат на элементах своё, как редактор узлов — концы связей: у развёрнутого
    /// об этом говорит его контейнер, у свёрнутого — только этот сигнал (ADR 0007).
    /// </remarks>
    internal event Action<object?>? ItemGeometryChanged;

    /// <summary>
    /// Сообщает, что сменилась геометрия элемента без контейнера.
    /// </summary>
    internal void OnItemGeometryChanged(object? item) => ItemGeometryChanged?.Invoke(item);

    /// <summary>
    /// Прямоугольник элемента в мировых координатах, есть у него контейнер или нет.
    /// </summary>
    /// <remarks>
    /// Без контейнера его знает только виртуализирующая панель: положение — привязкой, размер —
    /// последний измеренный или предполагаемый.
    /// </remarks>
    internal bool TryGetItemBounds(object? item, out Rect bounds)
    {
        if (ItemsPanelRoot is VirtualizingSurfacePanel panel)
            return panel.TryGetItemBounds(item, out bounds);

        // Без панели контейнеров нет, и искать элемент проходом по коллекции незачем.
        if (ItemsPanelRoot != null && item != null && ContainerFromItem(item) is SurfaceItem { IsVisible: true } container)
        {
            bounds = new Rect(container.Location, container.Bounds.Size);
            return true;
        }

        bounds = default;
        return false;
    }

    /// <summary>
    /// Прямоугольники элементов верхнего уровня в мировых координатах.
    /// </summary>
    /// <remarks>
    /// У виртуализирующей панели — из её геометрии, и у свёрнутых тоже: миникарта показывает холст
    /// целиком, а не то, что сейчас развёрнуто.
    /// </remarks>
    internal IEnumerable<Rect> EnumerateItemBounds() =>
        ItemsPanelRoot is VirtualizingSurfacePanel panel ? panel.EnumerateItemBounds() : EnumerateContainerBounds();

    private IEnumerable<Rect> EnumerateContainerBounds()
    {
        for (var i = 0; i < ItemCount; i++)
        {
            if (ContainerFromIndex(i) is SurfaceItem { IsVisible: true } item)
                yield return new Rect(item.Location, item.Bounds.Size);
        }
    }

    /// <summary>
    /// Идентификатор свойства модели выделения, повторно экспортированный из базового класса.
    /// </summary>
    public new static readonly DirectProperty<SelectingItemsControl, ISelectionModel> SelectionProperty =
        SelectingItemsControl.SelectionProperty;

    /// <summary>
    /// Идентификатор свойства коллекции выбранных элементов, повторно экспортированный из базового класса.
    /// </summary>
    public new static readonly DirectProperty<SelectingItemsControl, IList?> SelectedItemsProperty =
        SelectingItemsControl.SelectedItemsProperty;

    /// <summary>
    /// Идентификатор свойства режима выделения.
    /// </summary>
    public new static readonly StyledProperty<SelectionMode> SelectionModeProperty =
        SelectingItemsControl.SelectionModeProperty.AddOwner<SurfaceView>();

    /// <summary>
    /// Получает или задает модель выделения редактора.
    /// </summary>
    public new ISelectionModel Selection
    {
        get => base.Selection;
        set => base.Selection = value;
    }

    /// <summary>
    /// Получает или задает внешнюю коллекцию выбранных элементов.
    /// </summary>
    public new IList? SelectedItems
    {
        get => base.SelectedItems;
        set => base.SelectedItems = value;
    }

    /// <summary>
    /// Получает или задает режим выделения элементов.
    /// </summary>
    public new SelectionMode SelectionMode
    {
        get => base.SelectionMode;
        set => base.SelectionMode = value;
    }

    /// <summary>
    /// Определяет, нужен ли элементу коллекции контейнер <see cref="SurfaceItem"/>.
    /// </summary>
    /// <param name="item">Элемент источника данных.</param>
    /// <param name="index">Индекс элемента.</param>
    /// <param name="recycleKey">Ключ повторного использования контейнера.</param>
    /// <returns><see langword="true"/>, если элемент сам контейнером не является.</returns>
    /// <remarks>
    /// Выделение, жесты и геометрия ядра работают с <see cref="SurfaceItem"/>; без своего
    /// контейнера поверхность получила бы от <see cref="ItemsControl"/> голый
    /// <c>ContentPresenter</c>, и ни один элемент нельзя было бы ни выбрать, ни сдвинуть.
    /// </remarks>
    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
        => NeedsContainer<SurfaceItem>(item, out recycleKey);

    /// <summary>
    /// Создаёт контейнер элемента поверхности.
    /// </summary>
    /// <param name="item">Элемент источника данных.</param>
    /// <param name="index">Индекс элемента.</param>
    /// <param name="recycleKey">Ключ повторного использования контейнера.</param>
    /// <returns>Новый <see cref="SurfaceItem"/>.</returns>
    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
        => new SurfaceItem();

    /// <summary>
    /// Идентификатор свойства привязки положения элемента.
    /// </summary>
    public static readonly StyledProperty<BindingBase?> ItemLocationBindingProperty =
        AvaloniaProperty.Register<SurfaceView, BindingBase?>(nameof(ItemLocationBinding));

    /// <summary>
    /// Получает или задает привязку, дающую контейнеру положение его элемента на холсте.
    /// </summary>
    /// <remarks>
    /// Применяется к <see cref="SurfaceItem.Location"/>, когда контейнер готовится, с элементом
    /// коллекции в качестве контекста данных — тем же приёмом, что <c>DisplayMemberBinding</c>:
    /// <c>ItemLocationBinding="{Binding Location}"</c>. <see cref="SurfaceItem.Location"/>
    /// привязывается в обе стороны по умолчанию, а ядро пишет его, не снимая привязки: перетаскивание,
    /// смещение клавиатурой и отмена уходят в модель, а правка модели двигает контейнер.
    /// <para>
    /// Готовый <see cref="SurfaceItem"/> из коллекции не трогается: его положение задал тот, кто его
    /// создал. Не заданная привязка тоже ничего не ставит — положение тогда вправе задать стиль или
    /// обработчик <c>ContainerPrepared</c>. Смена привязки действует на контейнеры, подготовленные после
    /// неё.
    /// </para>
    /// </remarks>
    [AssignBinding]
    [InheritDataTypeFromItems(nameof(ItemsSource))]
    public BindingBase? ItemLocationBinding
    {
        get => GetValue(ItemLocationBindingProperty);
        set => SetValue(ItemLocationBindingProperty, value);
    }

    /// <summary>
    /// Идентификатор свойства привязки полосы заголовка элемента в упрощённом виде.
    /// </summary>
    public static readonly StyledProperty<BindingBase?> ItemAccentBindingProperty =
        AvaloniaProperty.Register<SurfaceView, BindingBase?>(nameof(ItemAccentBinding));

    /// <summary>
    /// Получает или задает привязку, дающую цвет полосы заголовка элемента в упрощённом виде.
    /// </summary>
    /// <remarks>
    /// ADR 0008. Ниже <see cref="SimplifiedZoom"/> элемент без контейнера рисуется карточкой, и эта
    /// привязка красит её верхнюю полосу — как заголовок узла в Blueprint. Применяется с элементом
    /// коллекции в качестве контекста данных, тем же приёмом, что <see cref="ItemLocationBinding"/>:
    /// <c>ItemAccentBinding="{Binding Category.Color}"</c>. Значение — кисть или цвет; иное, как и
    /// <see langword="null"/>, полосы не даёт. Читается без контейнера, когда панель читает положение
    /// элемента, и заново — по <c>INotifyPropertyChanged</c> свёрнутого элемента и при сворачивании.
    /// </remarks>
    [AssignBinding]
    [InheritDataTypeFromItems(nameof(ItemsSource))]
    public BindingBase? ItemAccentBinding
    {
        get => GetValue(ItemAccentBindingProperty);
        set => SetValue(ItemAccentBindingProperty, value);
    }

    /// <summary>
    /// Идентификатор свойства предполагаемого размера элемента.
    /// </summary>
    public static readonly StyledProperty<Size> EstimatedItemSizeProperty =
        AvaloniaProperty.Register<SurfaceView, Size>(nameof(EstimatedItemSize), new Size(100, 60));

    /// <summary>
    /// Получает или задает размер элемента, контейнер которого ещё ни разу не мерялся.
    /// </summary>
    /// <remarks>
    /// Нужен виртуализирующей панели, то есть когда задана <see cref="ItemLocationBinding"/>: элемент
    /// без контейнера известен ей положением, а размер у него появляется после первого показа. До того
    /// по этому размеру решается, пересекает ли элемент видимую область, и с ним элемент входит в охват,
    /// миникарту и рамку выделения. Ставить его близким к типичному элементу: меньший — элемент у края развернётся
    /// позже, чем станет виден; больший — раньше, чем нужно.
    /// </remarks>
    public Size EstimatedItemSize
    {
        get => GetValue(EstimatedItemSizeProperty);
        set => SetValue(EstimatedItemSizeProperty, value);
    }

    /// <summary>
    /// Даёт контейнеру положение его элемента привязкой <see cref="ItemLocationBinding"/>.
    /// </summary>
    /// <param name="container">Контейнер.</param>
    /// <param name="item">Элемент источника данных.</param>
    /// <param name="index">Индекс элемента.</param>
    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);

        if (container is SurfaceItem surfaceItem && !ReferenceEquals(container, item) && ItemLocationBinding is { } location)
        {
            surfaceItem.LocationBinding?.Dispose();
            surfaceItem.LocationBinding = surfaceItem.Bind(SurfaceItem.LocationProperty, location);
        }
    }

    /// <summary>
    /// Отпускает контейнер, который уходит в пул или удаляется: снимает привязку положения и то, что
    /// редактор записал ему сам.
    /// </summary>
    /// <param name="container">Контейнер.</param>
    /// <remarks>
    /// Контейнер в пуле хранит прежний контекст данных, и не снятая привязка продолжала бы двигать его
    /// вслед прежней модели, а отданный другому элементу — слушать обе. Размер и порядок перекрытия
    /// редактор пишет контейнеру локальными значениями — жестом, отменой, <c>SetTargetGeometry</c>, — и
    /// переработанный контейнер отдал бы их следующему элементу; привязка хоста к ним из стиля от этого
    /// не страдает. Положение, которое хост поставил сам, без <see cref="ItemLocationBinding"/>, не
    /// трогается.
    /// </remarks>
    protected override void ClearContainerForItemOverride(Control container)
    {
        base.ClearContainerForItemOverride(container);

        if (container is not SurfaceItem item)
            return;

        if (item.LocationBinding is { } binding)
        {
            binding.Dispose();
            item.LocationBinding = null;
        }

        item.ClearValue(WidthProperty);
        item.ClearValue(HeightProperty);
        item.ClearValue(ZIndexProperty);
    }

    /// <summary>
    /// Сетка из шаблона, если она в нём есть.
    /// </summary>
    /// <remarks>
    /// Её шаг — шаг привязки по умолчанию: сетка не может рисовать одну структуру,
    /// а привязка использовать другую.
    /// </remarks>
    internal SurfaceGrid? Grid { get; private set; }

    /// <summary>
    /// Находит части шаблона ядра.
    /// </summary>
    /// <param name="e">Аргументы применения шаблона.</param>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        Grid = e.NameScope.Find<SurfaceGrid>("PART_Grid");
    }

    private readonly List<object> _services = new();

    /// <summary>
    /// Подключает службу слоя выше: привязку, направляющие и другие инструменты.
    /// </summary>
    /// <remarks>
    /// Шов композиции (ADR 0003). Дизайнер интерфейса подключает службы наследованием, а
    /// инструменты редактирования — службами, чтобы их взял и редактор, который не
    /// наследует семантику форм.
    /// </remarks>
    private protected void AddService(object service) => _services.Add(service);

    /// <summary>
    /// Возвращает подключённую службу указанного типа или <see langword="null"/>.
    /// </summary>
    internal T? GetService<T>() where T : class
    {
        foreach (var service in _services)
        {
            if (service is T typed)
                return typed;
        }

        return null;
    }

    /// <summary>
    /// Запоминает положение и модификаторы нажатия, которое служба забрала себе.
    /// </summary>
    /// <remarks>
    /// Нажатие, перехваченное в фазе туннелирования, до <c>OnPointerPressed</c> не дойдёт,
    /// а жест, который оно начинает, считает от последнего ввода.
    /// </remarks>
    internal void RecordPointerInput(Point position, KeyModifiers modifiers)
    {
        _lastMousePosition = position;
        LastInputModifiers = modifiers;
    }
}
