using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ArxisStudio.Surface;

// Viewport: положение, масштаб, трансформации и их пересчёт под DPI.
// Часть SurfaceView; общее описание типа — в SurfaceView.cs.
public partial class SurfaceView
{
    private const double FitToViewPadding = 32.0;

    private readonly TranslateTransform _translateTransform = new TranslateTransform();

    private readonly ScaleTransform _scaleTransform = new ScaleTransform();

    private readonly TranslateTransform _dpiTranslateTransform = new TranslateTransform();

    // TopLevel, с которого читается RenderScaling и на который подписан ScalingChanged.
    // Разрешается один раз при подключении к дереву, чтобы подписка и чтение DPI
    // не расходились между собой.
    private TopLevel? _scalingHost;

    /// <summary>
    /// Идентификатор свойства позиции viewport в мировых координатах.
    /// </summary>
    public static readonly StyledProperty<Point> ViewportLocationProperty =
        AvaloniaProperty.Register<SurfaceView, Point>(nameof(ViewportLocation));

    /// <summary>
    /// Идентификатор свойства текущего масштаба viewport.
    /// </summary>
    public static readonly StyledProperty<double> ViewportZoomProperty =
        AvaloniaProperty.Register<SurfaceView, double>(nameof(ViewportZoom), 1.0);

    /// <summary>
    /// Идентификатор свойства минимального допустимого масштаба.
    /// </summary>
    public static readonly StyledProperty<double> MinZoomProperty =
        AvaloniaProperty.Register<SurfaceView, double>(nameof(MinZoom), 0.1);

    /// <summary>
    /// Идентификатор свойства максимального допустимого масштаба.
    /// </summary>
    public static readonly StyledProperty<double> MaxZoomProperty =
        AvaloniaProperty.Register<SurfaceView, double>(nameof(MaxZoom), 5.0);

    /// <summary>
    /// Идентификатор трансформации viewport в логических координатах.
    /// </summary>
    public static readonly StyledProperty<Transform> ViewportTransformProperty =
        AvaloniaProperty.Register<SurfaceView, Transform>(nameof(ViewportTransform), new TransformGroup());

    /// <summary>
    /// Идентификатор трансформации viewport с учетом текущего DPI.
    /// </summary>
    public static readonly StyledProperty<Transform> DpiScaledViewportTransformProperty =
        AvaloniaProperty.Register<SurfaceView, Transform>(nameof(DpiScaledViewportTransform), new TransformGroup());

    /// <summary>
    /// Идентификатор свойства видимости фоновой сетки.
    /// </summary>
    public static readonly StyledProperty<bool> ShowGridProperty =
        AvaloniaProperty.Register<SurfaceView, bool>(nameof(ShowGrid), true);

    /// <summary>
    /// Получает или задает положение viewport в мировых координатах.
    /// </summary>
    /// <remarks>
    /// Значение задает левый верхний угол видимой области в координатах содержимого.
    /// Обычно изменяется автоматически во время панорамирования или программно для перехода к нужной области.
    /// </remarks>
    public Point ViewportLocation
    {
        get => GetValue(ViewportLocationProperty);
        set => SetValue(ViewportLocationProperty, value);
    }

    /// <summary>
    /// Получает или задает текущий коэффициент масштабирования viewport.
    /// </summary>
    /// <remarks>
    /// Значение ограничивается диапазоном между <see cref="MinZoom"/> и <see cref="MaxZoom"/>.
    /// </remarks>
    public double ViewportZoom
    {
        get => GetValue(ViewportZoomProperty);
        set => SetValue(ViewportZoomProperty, value);
    }

    /// <summary>
    /// Получает или задает минимальное значение <see cref="ViewportZoom"/>.
    /// </summary>
    public double MinZoom
    {
        get => GetValue(MinZoomProperty);
        set => SetValue(MinZoomProperty, value);
    }

    /// <summary>
    /// Получает или задает максимальное значение <see cref="ViewportZoom"/>.
    /// </summary>
    public double MaxZoom
    {
        get => GetValue(MaxZoomProperty);
        set => SetValue(MaxZoomProperty, value);
    }

    /// <summary>
    /// Получает или задает трансформацию, применяемую к содержимому viewport.
    /// </summary>
    public Transform ViewportTransform
    {
        get => GetValue(ViewportTransformProperty);
        set => SetValue(ViewportTransformProperty, value);
    }

    /// <summary>
    /// Получает или задает DPI-aware трансформацию viewport.
    /// </summary>
    public Transform DpiScaledViewportTransform
    {
        get => GetValue(DpiScaledViewportTransformProperty);
        set => SetValue(DpiScaledViewportTransformProperty, value);
    }

    /// <summary>
    /// Получает или задает признак отображения фоновой сетки.
    /// </summary>
    /// <remarks>
    /// Сетка входит в шаблон редактора и настраивается через тему
    /// <see cref="DesignGrid"/> и ресурсы <c>DesignEditor.Grid.*</c>.
    /// Для собственного фона достаточно выключить её и задать <see cref="TemplatedControl.Background"/>.
    /// </remarks>
    public bool ShowGrid
    {
        get => GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    /// <summary>
    /// Подключает обработчики, зависящие от visual tree, после присоединения редактора к дереву.
    /// </summary>
    /// <param name="e">Аргументы присоединения к visual tree.</param>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // e.RootVisual в Avalonia 12 не гарантированно TopLevel, поэтому host ищется
        // подъемом по дереву, а не приведением корня.
        SetScalingHost(TopLevel.GetTopLevel(this));
        UpdateTransforms();
    }

    /// <summary>
    /// Освобождает обработчики, зависящие от visual tree, перед отсоединением редактора.
    /// </summary>
    /// <param name="e">Аргументы отсоединения от visual tree.</param>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        SetScalingHost(null);
    }

    private void SetScalingHost(TopLevel? topLevel)
    {
        if (ReferenceEquals(_scalingHost, topLevel))
            return;

        if (_scalingHost != null)
            _scalingHost.ScalingChanged -= OnScreenScalingChanged;

        _scalingHost = topLevel;

        if (_scalingHost != null)
            _scalingHost.ScalingChanged += OnScreenScalingChanged;
    }

    private void OnScreenScalingChanged(object? sender, EventArgs e) => UpdateTransforms();

    private void UpdateTransforms()
    {
        _scaleTransform.ScaleX = ViewportZoom;
        _scaleTransform.ScaleY = ViewportZoom;

        double x = -ViewportLocation.X * ViewportZoom;
        double y = -ViewportLocation.Y * ViewportZoom;

        _translateTransform.X = x;
        _translateTransform.Y = y;

        // В Avalonia 12 IRenderRoot больше не публичный: RenderScaling берется с TopLevel.
        // Проверка через "не > 0" отсекает и 0, и NaN: иначе деление ниже дало бы
        // нечисловой transform для фона и сетки.
        double renderScaling = _scalingHost?.RenderScaling ?? 1.0;
        if (!(renderScaling > 0))
            renderScaling = 1.0;

        _dpiTranslateTransform.X = Math.Round(x * renderScaling) / renderScaling;
        _dpiTranslateTransform.Y = Math.Round(y * renderScaling) / renderScaling;

        // Группы собраны в конструкторе и содержат эти же трансформации, поэтому
        // мутации выше видны через них сразу. Пересобирать их заново на каждом кадре
        // приходилось только ради обратного масштаба оверлеев: тот конвертер снимал
        // матрицу в момент преобразования и перевычислялся лишь при смене
        // идентичности значения. Теперь оверлеи привязаны к ViewportZoom напрямую.
    }

    /// <summary>
    /// Преобразует экранную точку в мировые координаты холста.
    /// </summary>
    /// <param name="screenPoint">Точка в координатах контрола.</param>
    /// <returns>Точка в координатах содержимого редактора.</returns>
    /// <example>
    /// Это полезно, когда нужно разместить новый элемент в позиции курсора с учетом текущего зума и панорамирования.
    /// </example>
    public Point GetWorldPosition(Point screenPoint)
        => (screenPoint / ViewportZoom) + ViewportLocation;

    /// <summary>
    /// Смещает viewport так, чтобы указанная мировая точка оказалась в центре видимой области редактора.
    /// </summary>
    /// <param name="worldPoint">Точка в координатах содержимого редактора.</param>
    /// <remarks>
    /// Метод не изменяет <see cref="ViewportZoom"/> и пересчитывает только <see cref="ViewportLocation"/>.
    /// </remarks>
    public void CenterOn(Point worldPoint)
    {
        var visibleWorldSize = new Size(Bounds.Width / ViewportZoom, Bounds.Height / ViewportZoom);
        ViewportLocation = new Point(
            worldPoint.X - (visibleWorldSize.Width / 2),
            worldPoint.Y - (visibleWorldSize.Height / 2));
    }

    /// <summary>
    /// Смещает viewport так, чтобы центр указанной области оказался в центре видимой области редактора.
    /// </summary>
    /// <param name="bounds">Прямоугольная область в мировых координатах.</param>
    /// <remarks>
    /// Метод не изменяет <see cref="ViewportZoom"/> и использует геометрический центр переданного прямоугольника.
    /// </remarks>
    public void CenterOn(Rect bounds)
    {
        CenterOn(bounds.Center);
    }

    /// <summary>
    /// Изменяет положение и масштаб viewport так, чтобы указанная область целиком поместилась в видимой области редактора.
    /// </summary>
    /// <param name="bounds">Прямоугольная область в мировых координатах, которую необходимо вписать в окно.</param>
    /// <remarks>
    /// Метод изменяет <see cref="ViewportLocation"/> и <see cref="ViewportZoom"/>.
    /// <para>
    /// Для более аккуратного отображения вокруг области добавляется внутренний отступ.
    /// Итоговый масштаб ограничивается значениями <see cref="MinZoom"/> и <see cref="MaxZoom"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// editor.FitToView(new Rect(100, 100, 640, 360));
    /// ]]></code>
    /// </example>
    public void FitToView(Rect bounds)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var paddedBounds = bounds.Inflate(FitToViewPadding);
        var targetWidth = Math.Max(1.0, paddedBounds.Width);
        var targetHeight = Math.Max(1.0, paddedBounds.Height);

        var zoomX = Bounds.Width / targetWidth;
        var zoomY = Bounds.Height / targetHeight;
        var newZoom = Math.Min(zoomX, zoomY);
        newZoom = Math.Max(MinZoom, Math.Min(MaxZoom, newZoom));

        ViewportZoom = newZoom;
        CenterOn(paddedBounds.Center);
    }

    /// <summary>
    /// Возвращает последнюю известную позицию указателя для текущего ввода.
    /// </summary>
    /// <param name="relativeTo">Параметр сохранен для совместимости с будущими реализациями.</param>
    /// <returns>Последняя позиция указателя в координатах редактора.</returns>
    public Point GetPositionForInput(Visual relativeTo)
        => _lastMousePosition;

    /// <summary>
    /// Выполняет масштабирование относительно текущей позиции курсора.
    /// </summary>
    /// <param name="e">Аргументы колесика мыши.</param>
    public void HandleZoom(PointerWheelEventArgs e) => TryHandleZoom(e);

    /// <summary>
    /// Масштабирует viewport и сообщает, состоялось ли масштабирование.
    /// </summary>
    /// <remarks>
    /// Публичный <see cref="HandleZoom"/> остаётся void ради совместимости;
    /// ответ нужен только редактору, чтобы решить судьбу <c>e.Handled</c>.
    /// </remarks>
    internal bool TryHandleZoom(PointerWheelEventArgs e)
    {
        if (!ShouldHandleZoom(e.KeyModifiers))
            return false;

        var zoomStep = InteractionOptions.ZoomStep > 1.0 ? InteractionOptions.ZoomStep : 1.1;
        var zoom = e.Delta.Y > 0 ? ViewportZoom * zoomStep : ViewportZoom / zoomStep;
        ZoomAt(zoom, e.GetPosition(this));

        // Колесо потреблено даже когда масштаб упёрся в Min/Max: жест был наш,
        // и отдавать его наружу на границе диапазона значило бы, что у края
        // зума страница вдруг начинает прокручиваться.
        return true;
    }

    /// <summary>
    /// Меняет масштаб так, что точка холста под указанной точкой экрана остаётся на месте.
    /// </summary>
    /// <param name="zoom">Новый масштаб; ограничивается <see cref="MinZoom"/> и <see cref="MaxZoom"/>.</param>
    /// <param name="origin">Неподвижная точка в координатах редактора.</param>
    /// <remarks>
    /// Правило одно на колесо, щипок тачпада и щипок пальцами: масштаб растёт вокруг того
    /// места, на которое смотрит человек, а не вокруг угла холста.
    /// </remarks>
    public void ZoomAt(double zoom, Point origin)
    {
        var prevZoom = ViewportZoom;
        var newZoom = Math.Max(MinZoom, Math.Min(MaxZoom, zoom));
        if (Math.Abs(newZoom - prevZoom) <= ZoomTolerance)
            return;

        var correction = (Vector)origin / prevZoom - (Vector)origin / newZoom;
        ViewportZoom = newZoom;
        ViewportLocation += correction;
    }

    /// <summary>
    /// Смещает viewport так, чтобы указанный элемент оказался в центре видимой области редактора.
    /// </summary>
    /// <param name="item">Элемент, который необходимо центрировать в области просмотра.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="item"/> равен <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Выбрасывается, если <paramref name="item"/> не принадлежит текущему экземпляру <see cref="SurfaceView"/>.
    /// </exception>
    /// <remarks>
    /// Метод изменяет только <see cref="SurfaceView.ViewportLocation"/> и не изменяет <see cref="SurfaceView.ViewportZoom"/>.
    /// <para>
    /// Если размер элемента превышает размер видимой области, элемент не масштабируется и не вписывается целиком:
    /// в центр видимой области помещается только геометрический центр элемента.
    /// </para>
    /// <para>
    /// Метод использует текущие <see cref="SurfaceItem.Location"/> и <see cref="Visual.Bounds"/> элемента.
    /// Для корректного результата элемент должен принадлежать текущему редактору и иметь актуальный layout.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// editor.CenterOnItem(container);
    /// ]]></code>
    /// </example>
    public void CenterOnItem(SurfaceItem item)
    {
        if (item == null)
            throw new ArgumentNullException(nameof(item));

        if (!ReferenceEquals(item.FindAncestorOfType<SurfaceView>(), this))
            throw new InvalidOperationException("The specified item does not belong to this SurfaceView.");

        if (Geometry.TryGetBounds(item, out var bounds))
        {
            CenterOn(bounds.Center);
            return;
        }

        var fallbackCenter = new Point(
            item.Location.X + (item.Bounds.Width / 2),
            item.Location.Y + (item.Bounds.Height / 2));

        CenterOn(fallbackCenter);
    }

    /// <summary>
    /// Изменяет положение и масштаб viewport так, чтобы указанный элемент целиком поместился в видимой области редактора.
    /// </summary>
    /// <param name="item">Элемент, который необходимо вписать в окно редактора.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="item"/> равен <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Выбрасывается, если <paramref name="item"/> не принадлежит текущему экземпляру <see cref="SurfaceView"/>.
    /// </exception>
    /// <remarks>
    /// Метод использует текущие <see cref="SurfaceItem.Location"/> и <see cref="Visual.Bounds"/> элемента
    /// и делегирует расчет геометрии перегрузке <see cref="SurfaceView.FitToView(Rect)"/>.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// editor.FitToView(container);
    /// ]]></code>
    /// </example>
    public void FitToView(SurfaceItem item)
    {
        if (item == null)
            throw new ArgumentNullException(nameof(item));

        if (!ReferenceEquals(item.FindAncestorOfType<SurfaceView>(), this))
            throw new InvalidOperationException("The specified item does not belong to this SurfaceView.");

        if (Geometry.TryGetBounds(item, out var bounds))
        {
            FitToView(bounds);
            return;
        }

        FitToView(new Rect(item.Location, item.Bounds.Size));
    }

    /// <summary>
    /// Идентификатор свойства прямоугольника, охватывающего все размещенные элементы.
    /// </summary>
    public static readonly DirectProperty<SurfaceView, Rect> ItemsExtentProperty =
        AvaloniaProperty.RegisterDirect<SurfaceView, Rect>(nameof(ItemsExtent), o => o.ItemsExtent, (o, v) => o.ItemsExtent = v);

    private Rect _itemsExtent;

    /// <summary>
    /// Получает или задает прямоугольник, охватывающий все дочерние элементы редактора.
    /// </summary>
    public Rect ItemsExtent
    {
        get => _itemsExtent;
        set => SetAndRaise(ItemsExtentProperty, ref _itemsExtent, value);
    }
}
