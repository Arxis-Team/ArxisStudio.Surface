using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Порт узла: точка, в которую входит или из которой выходит связь.
/// </summary>
/// <remarks>
/// Порт ставит приложение — в разметку узла, в <c>ItemTemplate</c> редактора. Связь находит его
/// по ключу (ADR 0004): это <see cref="Data"/>, а если он не задан — <c>DataContext</c> порта. В
/// обычной раскладке порт стоит в шаблоне элемента списка портов узла, и его <c>DataContext</c> —
/// ровно данные порта; порт, поставленный руками, свой <see cref="Data"/> задаёт сам, иначе он
/// получит ключом данные узла.
/// <para>
/// Конец связи — центр части шаблона <c>PART_Pin</c>, а без неё — середина края порта: левого у
/// входа, правого у выхода.
/// </para>
/// <para>
/// Вид штырька задаёт хост (ADR 0016): форму — <see cref="PinShape"/>, цвет — <see cref="PinBrush"/>. Так
/// различают порты выполнения и данных, как в Blueprint: пятиугольник и круг, белый и цвет типа.
/// Свойства — обычные стилевые: их ставят привязкой к модели порта или стилем по классу порта.
/// </para>
/// </remarks>
public class Port : ContentControl
{
    /// <summary>
    /// Идентификатор свойства направления порта.
    /// </summary>
    public static readonly StyledProperty<PortDirection> DirectionProperty =
        AvaloniaProperty.Register<Port, PortDirection>(nameof(Direction), PortDirection.Input);

    /// <summary>
    /// Идентификатор свойства ключа порта.
    /// </summary>
    public static readonly StyledProperty<object?> DataProperty =
        AvaloniaProperty.Register<Port, object?>(nameof(Data));

    /// <summary>
    /// Идентификатор свойства, показывающего, что к порту идёт хотя бы одна связь.
    /// </summary>
    public static readonly DirectProperty<Port, bool> IsConnectedProperty =
        AvaloniaProperty.RegisterDirect<Port, bool>(nameof(IsConnected), o => o.IsConnected);

    /// <summary>
    /// Идентификатор свойства <see cref="PinShape"/>.
    /// </summary>
    public static readonly StyledProperty<PortShape> PinShapeProperty =
        AvaloniaProperty.Register<Port, PortShape>(nameof(PinShape));

    /// <summary>
    /// Идентификатор свойства <see cref="PinGeometry"/>.
    /// </summary>
    public static readonly StyledProperty<Geometry?> PinGeometryProperty =
        AvaloniaProperty.Register<Port, Geometry?>(nameof(PinGeometry));

    /// <summary>
    /// Идентификатор свойства <see cref="PinBrush"/>.
    /// </summary>
    public static readonly StyledProperty<IBrush?> PinBrushProperty =
        AvaloniaProperty.Register<Port, IBrush?>(nameof(PinBrush));

    /// <summary>
    /// Идентификатор свойства <see cref="PinData"/>.
    /// </summary>
    public static readonly DirectProperty<Port, Geometry> PinDataProperty =
        AvaloniaProperty.RegisterDirect<Port, Geometry>(nameof(PinData), o => o.PinData);

    private Geometry _pinData = Shapes.Circle;
    private NodeEditor? _editor;
    private Node? _node;
    private object? _registeredKey;
    private Control? _pin;
    private bool _isConnected;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Port"/>.
    /// </summary>
    public Port()
    {
        UpdateDirectionClasses();
    }

    /// <summary>
    /// Получает или задает направление порта.
    /// </summary>
    public PortDirection Direction
    {
        get => GetValue(DirectionProperty);
        set => SetValue(DirectionProperty, value);
    }

    /// <summary>
    /// Получает или задает ключ, по которому связь находит этот порт.
    /// </summary>
    /// <remarks>
    /// Не задан — ключом служит <c>DataContext</c> порта.
    /// </remarks>
    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>
    /// Получает или задает форму штырька. По умолчанию — круг.
    /// </summary>
    public PortShape PinShape
    {
        get => GetValue(PinShapeProperty);
        set => SetValue(PinShapeProperty, value);
    }

    /// <summary>
    /// Получает или задает геометрию штырька для <see cref="PortShape.Custom"/>. Любой размер: шаблон
    /// вписывает её в штырёк с сохранением пропорций.
    /// </summary>
    public Geometry? PinGeometry
    {
        get => GetValue(PinGeometryProperty);
        set => SetValue(PinGeometryProperty, value);
    }

    /// <summary>
    /// Получает или задает цвет штырька: обводка пустого и заливка подключённого, как у пинов Blueprint.
    /// Без значения — кисти темы <c>NodeEditor.Port.*</c>.
    /// </summary>
    /// <remarks>
    /// Протягиваемая связь над портом по-прежнему красит обводку цветами согласия и отказа: обратная связь
    /// жеста важнее цвета вида.
    /// </remarks>
    public IBrush? PinBrush
    {
        get => GetValue(PinBrushProperty);
        set => SetValue(PinBrushProperty, value);
    }

    /// <summary>
    /// Получает геометрию штырька для шаблона: форма <see cref="PinShape"/> или <see cref="PinGeometry"/>.
    /// </summary>
    public Geometry PinData
    {
        get => _pinData;
        private set => SetAndRaise(PinDataProperty, ref _pinData, value);
    }

    /// <summary>
    /// Получает значение, показывающее, что к порту идёт хотя бы одна связь из
    /// <see cref="NodeEditor.Links"/>.
    /// </summary>
    /// <remarks>
    /// Тема показывает его псевдоклассом <c>:connected</c> — закрашенным штырьком.
    /// </remarks>
    public bool IsConnected
    {
        get => _isConnected;
        internal set
        {
            if (SetAndRaise(IsConnectedProperty, ref _isConnected, value))
                PseudoClasses.Set(":connected", value);
        }
    }

    /// <summary>
    /// Ключ порта: <see cref="Data"/> или, без него, <c>DataContext</c>.
    /// </summary>
    internal object? Key => Data ?? DataContext;

    /// <summary>
    /// Редактор, в реестре которого порт сейчас стоит.
    /// </summary>
    internal NodeEditor? Editor => _editor;

    /// <summary>
    /// Узел, внутри которого порт стоит.
    /// </summary>
    internal Node? Node => _node ?? this.FindAncestorOfType<Node>();

    /// <summary>
    /// Где был конец связи внутри узла, когда его сверяли в последний раз.
    /// </summary>
    /// <remarks>
    /// Раскладка узла сверяет с ним нынешнее положение (<see cref="NodeEditor.OnNodeArranged"/>) и
    /// пересчитывает связи порта, только если он сдвинулся.
    /// </remarks>
    internal Point? LastOffset { get; set; }

    /// <summary>
    /// Считает конец связи в мировых координатах.
    /// </summary>
    /// <remarks>
    /// Положение узла — его <see cref="SurfaceItem.Location"/>, а положение порта внутри узла — из
    /// раскладки узла. Поэтому после сдвига узла ответ верен сразу, ещё до прохода раскладки: узел
    /// сдвигается целиком, порт внутри него — нет.
    /// </remarks>
    internal bool TryGetAnchor(out Point world)
    {
        world = default;

        if ((_node ?? this.FindAncestorOfType<Node>()) is not { } node || !TryGetOffsetInNode(node, out var local))
            return false;

        world = node.Location + (Vector)local;
        return true;
    }

    /// <summary>
    /// Считает конец связи в координатах узла: центр штырька, а без него — середину края порта.
    /// </summary>
    internal bool TryGetOffsetInNode(Node node, out Point offset)
    {
        offset = default;
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return false;

        Point? inNode;
        if (_pin is { } pin && pin.Bounds.Width > 0 && pin.Bounds.Height > 0)
        {
            inNode = pin.TranslatePoint(new Point(pin.Bounds.Width / 2, pin.Bounds.Height / 2), node);
        }
        else
        {
            var edge = Direction == PortDirection.Output ? Bounds.Width : 0;
            inNode = this.TranslatePoint(new Point(edge, Bounds.Height / 2), node);
        }

        if (inNode is not { } local)
            return false;

        offset = local;
        return true;
    }

    /// <summary>
    /// Считает рамку порта в мировых координатах.
    /// </summary>
    internal bool TryGetWorldBounds(out Rect world)
    {
        world = default;

        var node = this.FindAncestorOfType<Node>();
        if (node == null || Bounds.Width <= 0 || Bounds.Height <= 0)
            return false;

        if (this.TranslatePoint(default, node) is not { } local)
            return false;

        world = new Rect(node.Location + (Vector)local, Bounds.Size);
        return true;
    }

    /// <summary>
    /// Показывает, примет ли порт протягиваемую связь: <see langword="true"/> — да,
    /// <see langword="false"/> — нет, <see langword="null"/> — связь не над ним.
    /// </summary>
    internal void SetAcceptance(bool? accepts)
    {
        PseudoClasses.Set(":accepting", accepts == true);
        PseudoClasses.Set(":refusing", accepts == false);
    }

    /// <summary>
    /// Нажатие на порт начинает протяжку связи, а не перетаскивание узла.
    /// </summary>
    /// <remarks>
    /// Нажатие помечается обработанным, поэтому до узла оно не доходит: ни выбора, ни
    /// перетаскивания узла из порта не бывает.
    /// </remarks>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.Handled || _editor == null || Key is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        e.Handled = _editor.BeginPendingLink(this, e);
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _pin = e.NameScope.Find<Control>("PART_Pin");
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _editor = this.FindAncestorOfType<NodeEditor>();
        _node = this.FindAncestorOfType<Node>();

        if (_editor != null && _node != null)
            _editor.AttachPort(this, _node);

        Rekey();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_editor != null && _registeredKey != null)
            _editor.Ports.Unregister(_registeredKey, this);

        if (_editor != null && _node != null)
            _editor.DetachPort(this, _node);

        _registeredKey = null;
        _node = null;
        _editor = null;
        IsConnected = false;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DirectionProperty)
            UpdateDirectionClasses();
        else if (change.Property == PinShapeProperty || change.Property == PinGeometryProperty)
            PinData = DataOf(PinShape, PinGeometry);
        else if (change.Property == PinBrushProperty)
            PseudoClasses.Set(":pin-brush", change.GetNewValue<IBrush?>() != null);
        else if (change.Property == DataProperty || change.Property == DataContextProperty)
            Rekey();
        else if (change.Property == BoundsProperty && _editor != null && _node != null)
            _editor.OnPortMoved(this, _node);
    }

    /// <summary>
    /// Ставит порт в реестр под его нынешним ключом, сняв с прежнего.
    /// </summary>
    private void Rekey()
    {
        if (_editor == null)
            return;

        var key = Key;
        if (Equals(key, _registeredKey))
            return;

        if (_registeredKey != null)
            _editor.Ports.Unregister(_registeredKey, this);

        _registeredKey = key;

        if (key != null)
            _editor.Ports.Register(key, this);
    }

    private static Geometry DataOf(PortShape shape, Geometry? custom) => shape switch
    {
        PortShape.Execution => Shapes.Execution,
        PortShape.Square => Shapes.Square,
        PortShape.Diamond => Shapes.Diamond,
        PortShape.Triangle => Shapes.Triangle,
        PortShape.Custom when custom != null => custom,
        _ => Shapes.Circle
    };

    /// <summary>
    /// Формы в квадрате 10 × 10; шаблон вписывает их в штырёк с сохранением пропорций.
    /// </summary>
    /// <remarks>
    /// Вложенный класс, а не поля <see cref="Port"/>: геометрия требует платформы отрисовки, а тип
    /// порта грузят и без неё — слепок публичной поверхности читает умолчания его свойств.
    /// </remarks>
    private static class Shapes
    {
        public static readonly Geometry Circle = new EllipseGeometry(new Rect(0, 0, 10, 10));
        public static readonly Geometry Execution = Geometry.Parse("M 0,0 L 6,0 L 10,5 L 6,10 L 0,10 Z");
        public static readonly Geometry Square = Geometry.Parse("M 0.5,0.5 L 9.5,0.5 L 9.5,9.5 L 0.5,9.5 Z");
        public static readonly Geometry Diamond = Geometry.Parse("M 5,0 L 10,5 L 5,10 L 0,5 Z");
        public static readonly Geometry Triangle = Geometry.Parse("M 0,0 L 10,5 L 0,10 Z");
    }

    private void UpdateDirectionClasses()
    {
        PseudoClasses.Set(":input", Direction == PortDirection.Input);
        PseudoClasses.Set(":output", Direction == PortDirection.Output);
    }
}
