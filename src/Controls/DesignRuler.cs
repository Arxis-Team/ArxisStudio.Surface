using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ArxisStudio.Surface;
using ArxisStudio.Surface.Editing;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Controls;

/// <summary>
/// Линейка редактора: шкала мировых координат и источник пользовательских направляющих.
/// </summary>
/// <remarks>
/// Линейка не входит в шаблон <see cref="DesignEditor"/>, а ставится хостом рядом с ним.
/// Причина в координатах: у редактора точка ввода совпадает с точкой viewport'а, и любой
/// слой, занявший место сверху или слева, сдвинул бы это соответствие. Composition
/// снаружи оставляет три системы координат редактора нетронутыми.
/// <para>
/// Единственное, что нужно задать, — <see cref="Editor"/>: масштаб и положение линейка
/// берёт у него сама, поэтому их нельзя рассинхронизировать.
/// </para>
/// <para>
/// Протяжка с линейки создаёт направляющую. Как и её перемещение, создание идёт запросом
/// <see cref="DesignEditor.GuideChangeRequested"/>: набором владеет хост.
/// </para>
/// </remarks>
public class DesignRuler : Control
{
    /// <summary>Минимальный экранный шаг между подписанными делениями.</summary>
    private const double MinLabelledStep = 60.0;

    /// <summary>Длина крупного деления в долях толщины линейки.</summary>
    private const double MajorTickRatio = 0.55;

    /// <summary>Длина мелкого деления в долях толщины линейки.</summary>
    private const double MinorTickRatio = 0.28;

    private IDisposable? _viewportSubscription;
    private bool _dragging;

    /// <summary>
    /// Идентификатор свойства ориентации линейки.
    /// </summary>
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<DesignRuler, Orientation>(nameof(Orientation), Orientation.Horizontal);

    /// <summary>
    /// Идентификатор свойства редактора, которому принадлежит линейка.
    /// </summary>
    public static readonly StyledProperty<DesignEditor?> EditorProperty =
        AvaloniaProperty.Register<DesignRuler, DesignEditor?>(nameof(Editor));

    /// <summary>
    /// Идентификатор свойства видимости шкалы.
    /// </summary>
    public static readonly StyledProperty<bool> IsScaleVisibleProperty =
        AvaloniaProperty.Register<DesignRuler, bool>(nameof(IsScaleVisible), true);

    /// <summary>
    /// Идентификатор свойства кисти делений.
    /// </summary>
    public static readonly StyledProperty<IBrush?> TickBrushProperty =
        AvaloniaProperty.Register<DesignRuler, IBrush?>(nameof(TickBrush));

    /// <summary>
    /// Идентификатор свойства кисти подписей.
    /// </summary>
    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<DesignRuler, IBrush?>(nameof(LabelBrush));

    /// <summary>
    /// Идентификатор свойства кисти фона.
    /// </summary>
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<DesignRuler, IBrush?>(nameof(Background));

    /// <summary>
    /// Идентификатор свойства размера шрифта подписей.
    /// </summary>
    public static readonly StyledProperty<double> LabelFontSizeProperty =
        AvaloniaProperty.Register<DesignRuler, double>(nameof(LabelFontSize), 9.0);

    static DesignRuler()
    {
        AffectsRender<DesignRuler>(
            OrientationProperty,
            IsScaleVisibleProperty,
            TickBrushProperty,
            LabelBrushProperty,
            BackgroundProperty,
            LabelFontSizeProperty);

        EditorProperty.Changed.AddClassHandler<DesignRuler>((ruler, e) => ruler.OnEditorChanged(e));
    }

    /// <summary>
    /// Получает или задает ориентацию линейки.
    /// </summary>
    /// <remarks>
    /// Горизонтальная линейка стоит сверху и порождает горизонтальные направляющие,
    /// вертикальная — слева и порождает вертикальные.
    /// </remarks>
    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>
    /// Получает или задает редактор, которому принадлежит линейка.
    /// </summary>
    public DesignEditor? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    /// <summary>
    /// Получает или задает признак отображения шкалы.
    /// </summary>
    /// <remarks>
    /// Выключенная шкала оставляет полосу линейки на месте: с неё по-прежнему
    /// вытягиваются направляющие, просто без делений и подписей. Это разные вещи —
    /// «шкала мешает читать макет» и «линейка не нужна вовсе», — и второе выключается
    /// через <see cref="DesignEditor.ShowRulers"/>.
    /// </remarks>
    public bool IsScaleVisible
    {
        get => GetValue(IsScaleVisibleProperty);
        set => SetValue(IsScaleVisibleProperty, value);
    }

    /// <summary>
    /// Получает или задает кисть делений.
    /// </summary>
    public IBrush? TickBrush
    {
        get => GetValue(TickBrushProperty);
        set => SetValue(TickBrushProperty, value);
    }

    /// <summary>
    /// Получает или задает кисть подписей.
    /// </summary>
    public IBrush? LabelBrush
    {
        get => GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    /// <summary>
    /// Получает или задает кисть фона.
    /// </summary>
    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>
    /// Получает или задает размер шрифта подписей.
    /// </summary>
    public double LabelFontSize
    {
        get => GetValue(LabelFontSizeProperty);
        set => SetValue(LabelFontSizeProperty, value);
    }

    /// <summary>Ось направляющих, которые порождает эта линейка.</summary>
    private DesignGuideOrientation GuideOrientation => Orientation == Orientation.Horizontal
        ? DesignGuideOrientation.Horizontal
        : DesignGuideOrientation.Vertical;

    /// <summary>
    /// Пересобирает подписку на viewport редактора.
    /// </summary>
    /// <remarks>
    /// Линейка следит за редактором сама, а не получает положение и масштаб отдельными
    /// привязками: два числа, которые обязаны совпадать, не должны задаваться порознь.
    /// </remarks>
    private void OnEditorChanged(AvaloniaPropertyChangedEventArgs e)
    {
        _viewportSubscription?.Dispose();
        _viewportSubscription = null;

        if (e.NewValue is not DesignEditor editor)
        {
            InvalidateVisual();
            return;
        }

        _viewportSubscription = new CompositeSubscription(
            editor.GetObservable(DesignEditor.ViewportLocationProperty).Subscribe(new Sink<Point>(this)),
            editor.GetObservable(DesignEditor.ViewportZoomProperty).Subscribe(new Sink<double>(this)),
            editor.GetObservable(DesignEditor.ShowRulersProperty).Subscribe(new VisibilitySink(this)));

        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (Editor is not { } editor || !editor.CanRequestGuideChange)
            return;

        // Вытягивать линию, которую не покажут, незачем.
        if (!editor.ShowGuides)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        _dragging = true;
        e.Pointer.Capture(this);
        UpdatePreview(editor, e);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_dragging || Editor is not { } editor)
            return;

        UpdatePreview(editor, e);
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!_dragging || Editor is not { } editor)
            return;

        _dragging = false;
        editor.SetGuidePreview(null);

        var point = e.GetPosition(editor);

        // Отпускание мимо холста не создаёт ничего: это тот же жест, которым линию
        // убирают, поэтому «вытянул и передумал» обязан быть отменой, а не добавлением
        // направляющей на краю.
        if (new Rect(editor.Bounds.Size).Contains(point))
        {
            var position = editor.ResolveGuidePosition(point, GuideOrientation, e.KeyModifiers);
            editor.RequestGuideChange(DesignGuideChangeKind.Add, new DesignGuide(GuideOrientation, position), null);
        }

        if (ReferenceEquals(e.Pointer.Captured, this))
            e.Pointer.Capture(null);
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        if (!_dragging)
            return;

        _dragging = false;
        Editor?.SetGuidePreview(null);
    }

    private void UpdatePreview(DesignEditor editor, PointerEventArgs e)
    {
        var position = editor.ResolveGuidePosition(e.GetPosition(editor), GuideOrientation, e.KeyModifiers);
        editor.SetGuidePreview(new DesignGuide(GuideOrientation, position));
    }

    /// <summary>
    /// Отрисовывает шкалу.
    /// </summary>
    /// <param name="context">Контекст отрисовки.</param>
    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
            return;

        if (Background is { } background)
            context.FillRectangle(background, new Rect(size));

        if (!IsScaleVisible)
            return;

        if (Editor is not { } editor || TickBrush is not { } tickBrush)
            return;

        var zoom = editor.ViewportZoom;
        if (!(zoom > 0))
            return;

        var horizontal = Orientation == Orientation.Horizontal;
        var origin = horizontal ? editor.ViewportLocation.X : editor.ViewportLocation.Y;
        var length = horizontal ? size.Width : size.Height;
        var thickness = horizontal ? size.Height : size.Width;

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        if (!(scaling > 0))
            scaling = 1.0;

        var pen = new Pen(tickBrush, 1.0 / scaling);
        var step = ResolveStep(zoom);
        var minorStep = step / 5.0;

        var first = Math.Floor(origin / minorStep) * minorStep;
        var last = origin + (length / zoom);

        var majorLength = thickness * MajorTickRatio;
        var minorLength = thickness * MinorTickRatio;
        var typeface = new Typeface(FontFamily.Default);
        var labelBrush = LabelBrush ?? tickBrush;

        for (var world = first; world <= last; world += minorStep)
        {
            var screen = DesignGrid.SnapToDevicePixel((world - origin) * zoom, scaling);
            if (screen < 0 || screen > length)
                continue;

            // Крупное деление определяется по номеру шага, а не сравнением остатка:
            // накопление world += minorStep уводит остаток от нуля, и подписи начинали
            // бы пропадать тем чаще, чем дальше уехал viewport.
            var index = (int)Math.Round(world / minorStep);
            var major = index % 5 == 0;
            var tick = major ? majorLength : minorLength;

            context.DrawLine(
                pen,
                horizontal ? new Point(screen, thickness - tick) : new Point(thickness - tick, screen),
                horizontal ? new Point(screen, thickness) : new Point(thickness, screen));

            if (!major)
                continue;

            var value = Math.Round(index * minorStep, 6);
            var text = new FormattedText(
                value.ToString("0.###", CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                LabelFontSize,
                labelBrush);

            context.DrawText(
                text,
                horizontal
                    ? new Point(screen + 2, 1)
                    : new Point(1, screen + 2));
        }
    }

    /// <summary>
    /// Подбирает шаг подписанных делений так, чтобы они не слипались на экране.
    /// </summary>
    /// <param name="zoom">Текущий масштаб.</param>
    /// <remarks>
    /// Шаг берётся из ряда 1-2-5 на степенях десяти: только такие числа читаются
    /// как круглые на любом масштабе, а шкала с шагом 37 бесполезна.
    /// </remarks>
    private static double ResolveStep(double zoom)
    {
        var raw = MinLabelledStep / zoom;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(raw, 1e-6))));
        var normalized = raw / magnitude;

        var factor = normalized switch
        {
            <= 1 => 1.0,
            <= 2 => 2.0,
            <= 5 => 5.0,
            _ => 10.0
        };

        return factor * magnitude;
    }

    /// <summary>Приёмник переключателя линеек: правит видимость.</summary>
    private sealed class VisibilitySink : IObserver<bool>
    {
        private readonly DesignRuler _ruler;

        public VisibilitySink(DesignRuler ruler) => _ruler = ruler;

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        // SetCurrentValue, а не SetValue: привязка хоста к IsVisible должна пережить
        // переключение, иначе одно выключение линеек навсегда её обрывало бы.
        public void OnNext(bool value) => _ruler.SetCurrentValue(IsVisibleProperty, value);
    }

    /// <summary>Приёмник изменений viewport: перерисовывает линейку.</summary>
    private sealed class Sink<T> : IObserver<T>
    {
        private readonly DesignRuler _ruler;

        public Sink(DesignRuler ruler) => _ruler = ruler;

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(T value) => _ruler.InvalidateVisual();
    }

    /// <summary>Набор подписок, снимаемых вместе.</summary>
    private sealed class CompositeSubscription : IDisposable
    {
        private readonly IDisposable[] _subscriptions;

        public CompositeSubscription(params IDisposable[] subscriptions) => _subscriptions = subscriptions;

        public void Dispose()
        {
            foreach (var subscription in _subscriptions)
                subscription.Dispose();
        }
    }
}
