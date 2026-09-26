using System.Collections;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
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
        ViewportLocationProperty.Changed.AddClassHandler<SurfaceView>((x, _) => x.UpdateTransforms());
        ViewportZoomProperty.Changed.AddClassHandler<SurfaceView>((x, _) => x.UpdateTransforms());
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

        _states.Push(new EditorIdleState(this));

        // Положение указателя нужно изменению размера, а ручка о нём не сообщает.
        // Туннель и handledEventsToo: во время жеста указатель захвачен ручкой,
        // и она помечает движение обработанным.
        AddHandler(PointerMovedEvent, OnTrackPointer, RoutingStrategies.Tunnel, handledEventsToo: true);

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
    /// Сетка из шаблона, если она в нём есть.
    /// </summary>
    /// <remarks>
    /// Её шаг — шаг привязки по умолчанию: сетка не может рисовать одну структуру,
    /// а привязка использовать другую.
    /// </remarks>
    internal DesignGrid? Grid { get; private set; }

    /// <summary>
    /// Находит части шаблона ядра.
    /// </summary>
    /// <param name="e">Аргументы применения шаблона.</param>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        Grid = e.NameScope.Find<DesignGrid>("PART_Grid");
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
