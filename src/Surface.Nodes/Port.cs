using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

    private NodeEditor? _editor;
    private object? _registeredKey;
    private Control? _pin;

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
    /// Ключ порта: <see cref="Data"/> или, без него, <c>DataContext</c>.
    /// </summary>
    internal object? Key => Data ?? DataContext;

    /// <summary>
    /// Редактор, в реестре которого порт сейчас стоит.
    /// </summary>
    internal NodeEditor? Editor => _editor;

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

        var node = this.FindAncestorOfType<Node>();
        if (node == null || Bounds.Width <= 0 || Bounds.Height <= 0)
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

        world = node.Location + (Vector)local;
        return true;
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
        Rekey();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_editor != null && _registeredKey != null)
            _editor.Ports.Unregister(_registeredKey, this);

        _registeredKey = null;
        _editor = null;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DirectionProperty)
            UpdateDirectionClasses();
        else if (change.Property == DataProperty || change.Property == DataContextProperty)
            Rekey();
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

    private void UpdateDirectionClasses()
    {
        PseudoClasses.Set(":input", Direction == PortDirection.Input);
        PseudoClasses.Set(":output", Direction == PortDirection.Output);
    }
}
