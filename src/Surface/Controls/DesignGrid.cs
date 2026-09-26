using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArxisStudio.Surface;

/// <summary>
/// Фоновая сетка поверхности редактора, отрисованная с точностью до физического пикселя.
/// </summary>
/// <remarks>
/// Контрол не использует tiled <see cref="DrawingBrush"/>, а рисует только видимые линии.
/// Это даёт три свойства, недостижимые для плиточной заливки:
/// <list type="bullet">
/// <item><description>
/// толщина линий задаётся в физических пикселях и не растёт вместе с zoom;
/// </description></item>
/// <item><description>
/// при плотной сетке мелкие линии скрываются (<see cref="MinCellSize"/>), и на малом
/// масштабе сетка не превращается в сплошную заливку;
/// </description></item>
/// <item><description>
/// шаг задаётся числом, а не переписыванием геометрии.
/// </description></item>
/// </list>
/// <para>
/// Положение и масштаб приходят из viewport поверхности (<c>ViewportLocation</c> и
/// <c>ViewportZoom</c>). Собственной трансформации у контрола нет:
/// он рисует в экранных координатах, пересчитывая мировые сам.
/// </para>
/// </remarks>
public class DesignGrid : Control
{
    /// <summary>
    /// Идентификатор свойства кисти фона.
    /// </summary>
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<DesignGrid, IBrush?>(nameof(Background));

    /// <summary>
    /// Идентификатор свойства шага сетки в мировых единицах.
    /// </summary>
    public static readonly StyledProperty<double> CellSizeProperty =
        AvaloniaProperty.Register<DesignGrid, double>(nameof(CellSize), 20.0);

    /// <summary>
    /// Идентификатор свойства периода мажорных линий, в ячейках.
    /// </summary>
    public static readonly StyledProperty<int> MajorIntervalProperty =
        AvaloniaProperty.Register<DesignGrid, int>(nameof(MajorInterval), 5);

    /// <summary>
    /// Идентификатор свойства кисти обычных линий.
    /// </summary>
    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<DesignGrid, IBrush?>(nameof(LineBrush));

    /// <summary>
    /// Идентификатор свойства кисти мажорных линий.
    /// </summary>
    public static readonly StyledProperty<IBrush?> MajorLineBrushProperty =
        AvaloniaProperty.Register<DesignGrid, IBrush?>(nameof(MajorLineBrush));

    /// <summary>
    /// Идентификатор свойства толщины линий в физических пикселях.
    /// </summary>
    public static readonly StyledProperty<double> LineThicknessProperty =
        AvaloniaProperty.Register<DesignGrid, double>(nameof(LineThickness), 1.0);

    /// <summary>
    /// Идентификатор свойства минимального экранного шага, при котором линии ещё рисуются.
    /// </summary>
    public static readonly StyledProperty<double> MinCellSizeProperty =
        AvaloniaProperty.Register<DesignGrid, double>(nameof(MinCellSize), 6.0);

    /// <summary>
    /// Идентификатор свойства положения viewport в мировых координатах.
    /// </summary>
    public static readonly StyledProperty<Point> ViewportLocationProperty =
        AvaloniaProperty.Register<DesignGrid, Point>(nameof(ViewportLocation));

    /// <summary>
    /// Идентификатор свойства масштаба viewport.
    /// </summary>
    public static readonly StyledProperty<double> ViewportZoomProperty =
        AvaloniaProperty.Register<DesignGrid, double>(nameof(ViewportZoom), 1.0);

    static DesignGrid()
    {
        AffectsRender<DesignGrid>(
            BackgroundProperty,
            CellSizeProperty,
            MajorIntervalProperty,
            LineBrushProperty,
            MajorLineBrushProperty,
            LineThicknessProperty,
            MinCellSizeProperty,
            ViewportLocationProperty,
            ViewportZoomProperty);
    }

    /// <summary>
    /// Получает или задает кисть фона под сеткой.
    /// </summary>
    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>
    /// Получает или задает шаг сетки в мировых единицах.
    /// </summary>
    public double CellSize
    {
        get => GetValue(CellSizeProperty);
        set => SetValue(CellSizeProperty, value);
    }

    /// <summary>
    /// Получает или задает, каждая какая по счету линия является мажорной.
    /// </summary>
    public int MajorInterval
    {
        get => GetValue(MajorIntervalProperty);
        set => SetValue(MajorIntervalProperty, value);
    }

    /// <summary>
    /// Получает или задает кисть обычных линий сетки.
    /// </summary>
    public IBrush? LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    /// <summary>
    /// Получает или задает кисть мажорных линий сетки.
    /// </summary>
    public IBrush? MajorLineBrush
    {
        get => GetValue(MajorLineBrushProperty);
        set => SetValue(MajorLineBrushProperty, value);
    }

    /// <summary>
    /// Получает или задает толщину линий в физических пикселях.
    /// </summary>
    /// <remarks>
    /// Значение не зависит ни от DPI, ни от <see cref="ViewportZoom"/>: линия толщиной 1
    /// занимает ровно один пиксель устройства на любом масштабе.
    /// </remarks>
    public double LineThickness
    {
        get => GetValue(LineThicknessProperty);
        set => SetValue(LineThicknessProperty, value);
    }

    /// <summary>
    /// Получает или задает минимальный экранный шаг в пикселях, при котором линии рисуются.
    /// </summary>
    /// <remarks>
    /// Когда шаг становится меньше, соответствующий уровень сетки скрывается.
    /// Без этого при отдалении линии сливаются в сплошной фон.
    /// </remarks>
    public double MinCellSize
    {
        get => GetValue(MinCellSizeProperty);
        set => SetValue(MinCellSizeProperty, value);
    }

    /// <summary>
    /// Получает или задает положение viewport в мировых координатах.
    /// </summary>
    public Point ViewportLocation
    {
        get => GetValue(ViewportLocationProperty);
        set => SetValue(ViewportLocationProperty, value);
    }

    /// <summary>
    /// Получает или задает масштаб viewport.
    /// </summary>
    public double ViewportZoom
    {
        get => GetValue(ViewportZoomProperty);
        set => SetValue(ViewportZoomProperty, value);
    }

    /// <summary>
    /// Отрисовывает фон и видимую часть сетки.
    /// </summary>
    /// <param name="context">Контекст отрисовки.</param>
    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
            return;

        if (Background is { } background)
            context.FillRectangle(background, new Rect(size));

        var cell = CellSize;
        var zoom = ViewportZoom;
        if (!(cell > 0) || !(zoom > 0))
            return;

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        if (!(scaling > 0))
            scaling = 1.0;

        var major = Math.Max(1, MajorInterval);
        var minPixels = Math.Max(1.0, MinCellSize);
        var minorStep = cell * zoom;
        var majorStep = minorStep * major;

        // Уровни детализации: мелкая сетка исчезает раньше крупной.
        var drawMinor = IsLevelVisible(minorStep, minPixels);
        var drawMajor = IsLevelVisible(majorStep, minPixels);
        if (!drawMinor && !drawMajor)
            return;

        // Толщина задана в пикселях устройства, поэтому переводим её в DIP.
        var thickness = Math.Max(0.0, LineThickness) / scaling;
        if (!(thickness > 0))
            return;

        var minorPen = LineBrush is { } minorBrush ? new Pen(minorBrush, thickness) : null;
        var majorPen = (MajorLineBrush ?? LineBrush) is { } majorBrush ? new Pen(majorBrush, thickness) : null;
        if (minorPen == null && majorPen == null)
            return;

        var origin = ViewportLocation;

        DrawAxis(context, size.Height, size.Width, origin.X, horizontal: false);
        DrawAxis(context, size.Width, size.Height, origin.Y, horizontal: true);

        void DrawAxis(DrawingContext ctx, double lineLength, double extent, double originWorld, bool horizontal)
        {
            // Первая линия сетки, попадающая в видимую область.
            var firstIndex = (int)Math.Ceiling(originWorld / cell);
            var maxLines = (int)(extent / Math.Max(minorStep, 0.5)) + 2;

            for (var i = 0; i <= maxLines; i++)
            {
                var index = firstIndex + i;
                var screen = ((index * cell) - originWorld) * zoom;
                if (screen > extent)
                    break;

                var isMajor = ((index % major) + major) % major == 0;
                if (isMajor ? !drawMajor : !drawMinor)
                    continue;

                var pen = isMajor ? majorPen : minorPen;
                if (pen == null)
                    continue;

                var position = SnapToDevicePixel(screen, scaling);
                var from = horizontal ? new Point(0, position) : new Point(position, 0);
                var to = horizontal ? new Point(lineLength, position) : new Point(position, lineLength);
                ctx.DrawLine(pen, from, to);
            }
        }
    }

    /// <summary>
    /// Определяет, рисуется ли уровень сетки с указанным экранным шагом.
    /// </summary>
    /// <param name="screenStep">Шаг уровня в пикселях экрана.</param>
    /// <param name="minCellSize">Минимальный допустимый шаг.</param>
    internal static bool IsLevelVisible(double screenStep, double minCellSize)
        => screenStep >= minCellSize;

    /// <summary>
    /// Совмещает координату с центром пикселя устройства.
    /// </summary>
    /// <remarks>
    /// Без этого линия толщиной в один пиксель попадает на границу двух и размывается
    /// на половину интенсивности в каждом — именно это отличает «precision-rendered»
    /// сетку от нарисованной по произвольным координатам.
    /// </remarks>
    internal static double SnapToDevicePixel(double value, double scaling)
        => (Math.Floor(value * scaling) + 0.5) / scaling;
}
