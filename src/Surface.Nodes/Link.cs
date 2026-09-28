using Avalonia;
using Avalonia.Controls;
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
/// Связь редактор держит записью, а контрол — только у развёрнутой (ADR 0007): при виртуализации
/// связь вне окна контрола не имеет, а контрол, созданный редактором, переходит от связи к связи и
/// показывает то, что в записи. Готовая связь из коллекции сама себе контрол и развёрнута всегда.
/// </para>
/// <para>
/// Конец без живого порта берётся у свёрнутого узла — смещением порта с его последнего показа — и у
/// ни разу не показанного — оценкой по краю через <see cref="NodeEditor.PortNodeBinding"/>. Если
/// взять его неоткуда — узел ещё не пришёл или уже ушёл, — связь не рисуется и появится сама, когда
/// конец найдётся.
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

    /// <summary>
    /// Идентификатор свойства признака выбора.
    /// </summary>
    public static readonly DirectProperty<Link, bool> IsSelectedProperty =
        AvaloniaProperty.RegisterDirect<Link, bool>(nameof(IsSelected), o => o.IsSelected);

    private Point _sourceAnchor;
    private Point _targetAnchor;
    private bool _isSelected;
    private NodeEditor? _editor;

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
    /// Получает признак того, что связь выбрана.
    /// </summary>
    /// <remarks>
    /// Выбирает связь редактор — щелчком или <see cref="NodeEditor.SelectLink"/>; выбранная
    /// показывает псевдокласс <c>:selected</c>.
    /// </remarks>
    public bool IsSelected
    {
        get => _isSelected;
        private set
        {
            if (SetAndRaise(IsSelectedProperty, ref _isSelected, value))
                PseudoClasses.Set(":selected", value);
        }
    }

    /// <summary>
    /// Запись, которую контрол сейчас показывает.
    /// </summary>
    internal LinkRecord? Record { get; private set; }

    /// <summary>
    /// Элемент коллекции, которым связь называют приложению.
    /// </summary>
    internal object ItemOrSelf => Record?.Item ?? this;

    /// <summary>
    /// Найдены ли оба конца: только такая связь рисуется.
    /// </summary>
    internal bool IsResolved => Record?.IsResolved == true;

    /// <summary>
    /// Кривая связи в мировых координатах.
    /// </summary>
    internal LinkGeometry Geometry => Record?.Geometry ?? default;

    /// <summary>
    /// Прямоугольник, который связь занимает на холсте, с запасом на толщину линии.
    /// </summary>
    internal Rect WorldBounds => Record?.WorldBounds ?? default;

    /// <summary>
    /// Начинает показывать запись: контрол развёрнут для этой связи.
    /// </summary>
    /// <remarks>
    /// Контролу, созданному редактором, концы и контекст данных ставит запись — стилю хоста и тому,
    /// кто читает <see cref="Source"/> с контрола, они видны так же, как прежде; у готовой связи их
    /// задал хост.
    /// </remarks>
    internal void Show(NodeEditor editor, LinkRecord record)
    {
        _editor = editor;
        Record = record;

        if (record.Own == null)
            DataContext = record.Item;

        Sync();
    }

    /// <summary>
    /// Перестаёт показывать запись: контрол свёрнут и уходит в пул или остаётся хозяину.
    /// </summary>
    internal void Hide()
    {
        if (Record is { Own: null })
        {
            ClearValue(DataContextProperty);
            ClearValue(SourceProperty);
            ClearValue(TargetProperty);
        }

        Record = null;
        _editor = null;
        IsSelected = false;
        PseudoClasses.Set(":highlighted", false);
        PseudoClasses.Set(":cutting", false);
        PseudoClasses.Set(":detaching", false);
    }

    /// <summary>
    /// Переносит на контрол то, что сейчас в записи: концы, видимость, состояния.
    /// </summary>
    /// <summary>
    /// Кисть, которой провод рисуется сейчас.
    /// </summary>
    /// <remarks>
    /// Цвет модели (<see cref="NodeEditor.LinkStrokeBinding"/>) — обычное состояние провода; выбор,
    /// наведение и разрез красят его цветом темы: обратная связь жеста важнее цвета типа (ADR 0009).
    /// </remarks>
    internal IBrush? EffectiveStroke =>
        IsSelected || PseudoClasses.Contains(":highlighted") || PseudoClasses.Contains(":cutting")
            ? Stroke
            : Record?.Stroke ?? Stroke;

    internal void Sync()
    {
        if (Record is not { } record)
            return;

        if (record.IsResolved)
        {
            SourceAnchor = record.Geometry.Source;
            TargetAnchor = record.Geometry.Target;
        }

        if (record.Own == null)
        {
            Source = record.Source;
            Target = record.Target;
        }

        IsVisible = record.IsResolved;
        IsSelected = record.IsSelected;
        PseudoClasses.Set(":highlighted", record.IsHighlighted);
        PseudoClasses.Set(":cutting", record.IsCutting);
        PseudoClasses.Set(":detaching", record.IsDetaching);

        // Мера от кривой не зависит: связь только просит панель переставить её — одну её, а не
        // все связи холста (ADR 0007).
        (this.GetVisualParent() as LinkPanel)?.OnLinkMoved(this);
        InvalidateVisual();
    }

    /// <summary>
    /// Отвечает постоянным нулевым размером: размер связи — её рамка, и ставит её туда расстановка
    /// панели связей.
    /// </summary>
    /// <remarks>
    /// Желаемый размер не зависит от кривой намеренно (ADR 0007). Его смена поднимает перемер
    /// родителя, то есть всех связей холста, а связь, растянутая в свой прямоугольник, берёт размер
    /// из него, а не из меры.
    /// </remarks>
    /// <param name="availableSize">Доступный размер.</param>
    /// <returns>Нулевой размер.</returns>
    protected override Size MeasureOverride(Size availableSize) => default;

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        if (Record is not { IsResolved: true } record || EffectiveStroke is not { } stroke)
            return;

        // Связь стоит в своём прямоугольнике, а кривая посчитана в мировых координатах, поэтому
        // рисуется со сдвигом на угол прямоугольника.
        var offset = record.WorldBounds.Position;
        var g = record.Geometry;
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
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (_editor == null || Record is not { } record)
            return;

        // Концы готовой связи задаёт хост на самом контроле, и о смене говорит редактору она сама;
        // контролу, созданному редактором, их ставит запись.
        if ((change.Property == SourceProperty || change.Property == TargetProperty) && ReferenceEquals(record.Own, this))
            _editor.OnOwnLinkEndsChanged(record);
        else if (change.Property == StrokeThicknessProperty)
            _editor.OnLinkThicknessChanged(this);
    }
}
