using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Связь между двумя портами: кривая из выхода во вход, под узлами.
/// </summary>
/// <remarks>
/// Концы связи — данные портов (<see cref="Source"/>, <see cref="Target"/>); где они на холсте,
/// редактор находит сам, по живым портам (ADR 0004). Обычно связи создаёт редактор из
/// <see cref="NodeEditor.Links"/>, задавая концы привязками <see cref="NodeEditor.LinkSourceBinding"/>
/// и <see cref="NodeEditor.LinkTargetBinding"/>; готовые <see cref="Link"/> в той же коллекции
/// тоже годятся.
/// <para>
/// Пока хотя бы одного порта нет на поверхности, связь не рисуется: её узел мог ещё не прийти или
/// уже уйти, и она появится сама, когда порт вернётся.
/// </para>
/// </remarks>
public class Link : Control
{
    /// <summary>
    /// Идентификатор свойства данных порта-источника.
    /// </summary>
    public static readonly StyledProperty<object?> SourceProperty =
        AvaloniaProperty.Register<Link, object?>(nameof(Source));

    /// <summary>
    /// Идентификатор свойства данных порта-цели.
    /// </summary>
    public static readonly StyledProperty<object?> TargetProperty =
        AvaloniaProperty.Register<Link, object?>(nameof(Target));

    /// <summary>
    /// Идентификатор свойства кисти линии.
    /// </summary>
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<Link, IBrush?>(nameof(Stroke));

    /// <summary>
    /// Идентификатор свойства толщины линии в мировых единицах.
    /// </summary>
    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<Link, double>(nameof(StrokeThickness), 2.0);

    /// <summary>
    /// Идентификатор свойства конца связи у источника.
    /// </summary>
    public static readonly DirectProperty<Link, Point> SourceAnchorProperty =
        AvaloniaProperty.RegisterDirect<Link, Point>(nameof(SourceAnchor), o => o.SourceAnchor);

    /// <summary>
    /// Идентификатор свойства конца связи у цели.
    /// </summary>
    public static readonly DirectProperty<Link, Point> TargetAnchorProperty =
        AvaloniaProperty.RegisterDirect<Link, Point>(nameof(TargetAnchor), o => o.TargetAnchor);

    private Point _sourceAnchor;
    private Point _targetAnchor;
    private NodeEditor? _editor;
    private object? _registeredSource;
    private object? _registeredTarget;

    static Link()
    {
        AffectsRender<Link>(StrokeProperty, StrokeThicknessProperty);
        AffectsMeasure<Link>(StrokeThicknessProperty);
    }

    /// <summary>
    /// Получает или задает данные порта, из которого выходит связь.
    /// </summary>
    public object? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// Получает или задает данные порта, в который входит связь.
    /// </summary>
    public object? Target
    {
        get => GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>
    /// Получает или задает кисть линии.
    /// </summary>
    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>
    /// Получает или задает толщину линии в мировых единицах: при приближении она растёт вместе
    /// с узлами.
    /// </summary>
    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>
    /// Получает конец связи у источника в мировых координатах.
    /// </summary>
    public Point SourceAnchor
    {
        get => _sourceAnchor;
        private set => SetAndRaise(SourceAnchorProperty, ref _sourceAnchor, value);
    }

    /// <summary>
    /// Получает конец связи у цели в мировых координатах.
    /// </summary>
    public Point TargetAnchor
    {
        get => _targetAnchor;
        private set => SetAndRaise(TargetAnchorProperty, ref _targetAnchor, value);
    }

    /// <summary>
    /// Найдены ли оба порта: только такая связь рисуется.
    /// </summary>
    internal bool IsResolved { get; private set; }

    /// <summary>
    /// Кривая связи в мировых координатах.
    /// </summary>
    internal LinkGeometry Geometry { get; private set; }

    /// <summary>
    /// Прямоугольник, который связь занимает на холсте, с запасом на толщину линии.
    /// </summary>
    internal Rect WorldBounds { get; private set; }

    /// <summary>
    /// Пересчитывает концы по живым портам.
    /// </summary>
    internal void Refresh()
    {
        if (_editor == null)
            return;

        _editor.CountLinkUpdate();

        Point source = default, target = default;
        var resolved = Source != null && Target != null
            && _editor.TryGetPortAnchor(Source, out source)
            && _editor.TryGetPortAnchor(Target, out target);

        if (resolved)
        {
            SourceAnchor = source;
            TargetAnchor = target;
            Geometry = new LinkGeometry(source, target);
            WorldBounds = Geometry.Bounds.Inflate(StrokeThickness);
        }

        IsResolved = resolved;
        IsVisible = resolved;

        InvalidateMeasure();
        (this.GetVisualParent() as Layoutable)?.InvalidateArrange();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) =>
        IsResolved ? WorldBounds.Size : default;

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        if (!IsResolved || Stroke is not { } stroke)
            return;

        // Связь стоит в своём прямоугольнике, а кривая посчитана в мировых координатах, поэтому
        // рисуется со сдвигом на угол прямоугольника.
        var offset = WorldBounds.Position;
        var g = Geometry;
        var figure = new StreamGeometry();
        using (var ctx = figure.Open())
        {
            ctx.BeginFigure(g.Source - offset, isFilled: false);
            ctx.CubicBezierTo(g.SourceControl - offset, g.TargetControl - offset, g.Target - offset);
            ctx.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, new Pen(stroke, StrokeThickness, lineCap: PenLineCap.Round), figure);
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _editor = this.FindAncestorOfType<NodeEditor>();
        Reregister();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _editor?.UnregisterLink(this, _registeredSource, _registeredTarget);
        _registeredSource = null;
        _registeredTarget = null;
        _editor = null;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SourceProperty || change.Property == TargetProperty)
            Reregister();
        else if (change.Property == StrokeThicknessProperty && IsResolved)
            WorldBounds = Geometry.Bounds.Inflate(StrokeThickness);
    }

    private void Reregister()
    {
        if (_editor == null)
            return;

        _editor.UnregisterLink(this, _registeredSource, _registeredTarget);
        _registeredSource = Source;
        _registeredTarget = Target;
        _editor.RegisterLink(this, _registeredSource, _registeredTarget);
        Refresh();
    }
}
