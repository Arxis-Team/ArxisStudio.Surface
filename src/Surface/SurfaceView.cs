using System.Collections;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using ArxisStudio.Surface.States;

namespace ArxisStudio.Surface;

/// <summary>
/// Бесконечная поверхность: основа любого редактора на холсте.
/// </summary>
/// <remarks>
/// Ядро «глупое» и не знает, что лежит на холсте, — форма, узел графа или фигура
/// (ADR 0003). Всё, что относится к содержимому, добавляют слои выше: инструменты
/// редактирования — службами, дизайнер форм — наследником <c>DesignEditor</c>.
/// <para>
/// Члены переезжают сюда из дизайнера форм по шагам, и каждый шаг оставляет
/// поведение прежним; пока класс — точка, от которой наследуется редактор форм.
/// </para>
/// </remarks>
public partial class SurfaceView : SelectingItemsControl
{
    static SurfaceView()
    {
        // Фокус поверхность просит сама, на нажатии (OnPointerPressed), но получает его
        // только фокусируемый элемент. Разрешение стояло у дизайнера форм, и голая
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
    /// Шов композиции (ADR 0003). Дизайнер форм подключает службы наследованием, а
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
