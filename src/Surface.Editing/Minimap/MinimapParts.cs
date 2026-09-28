using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering.SceneGraph;
using SkiaSharp;

namespace ArxisStudio.Surface.Editing;

/// <summary>
/// Содержимое миникарты, собранное на UI-потоке: прямоугольники контейнеров и кривые слоя выше, в
/// мировых координатах. Неизменяемо — его читает поток отрисовки.
/// </summary>
internal sealed class MinimapSnapshot(Rect[] items, MinimapCurve[] curves)
{
    public Rect[] Items { get; } = items;

    public MinimapCurve[] Curves { get; } = curves;

    /// <summary>
    /// Картинка содержимого — кэш потока отрисовки для холста Skia (ADR 0012).
    /// </summary>
    public MinimapPicture? Picture { get; set; }
}

/// <summary>
/// Содержимое карты, растеризованное один раз, и то, для чего оно растеризовано.
/// </summary>
/// <remarks>
/// Карту приходится рисовать на каждом кадре панорамы — под ней едет холст, — а десять тысяч
/// прямоугольников мельче пикселя рисуются только со сглаживанием: без него те, что не накрыли центр
/// пикселя, пропадают полосами. Путь со сглаживанием Skia на каждом кадре растеризует заново, и это
/// стоило кадра. Поэтому содержимое растеризуется в картинку по своему охвату в мире, а кадр рисует её
/// одним вызовом через нынешнее соответствие карты. Масштаб картинки округлён вверх до ступени в
/// 2^(1/8), около 9 %: соответствие меняется на каждом кадре панорамы, когда видимое раздвигает карту,
/// и картинка под точный масштаб растеризовалась бы на каждом кадре; с округлением её хватает, пока
/// масштаб не ушёл на ступень, а кадр уменьшает её не больше чем на 9 %.
/// </remarks>
internal sealed class MinimapPicture(SKImage image, Rect world, double step, SKColor fill, SKColor stroke)
{
    public SKImage Image { get; } = image;

    /// <summary>
    /// Охват содержимого в мире, который картинка покрывает.
    /// </summary>
    public Rect World { get; } = world;

    public bool Fits(double step2, SKColor fill2, SKColor stroke2) => step == step2 && fill == fill2 && stroke == stroke2;

    /// <summary>
    /// Ступень масштаба: пикселей картинки на единицу мира, округлённо вверх до 2^(1/8).
    /// </summary>
    public static double Step(double pixelsPerUnit) => Math.Pow(2, Math.Ceiling(Math.Log2(Math.Max(pixelsPerUnit, 1e-6)) * 8) / 8);
}

/// <summary>
/// Содержимое миникарты — отдельный визуал (ADR 0011, 0012).
/// </summary>
/// <remarks>
/// Панорама двигает только рамку видимой области, и содержимое между сменами самого содержимого или
/// соответствия карты не записывается заново. Рисует оно своей операцией, чьи границы — прямоугольник
/// визуала: границы геометрии на десять тысяч фигур композитор иначе мерил бы на каждой записи. Холст
/// Skia рисует его одной картинкой из кэша снимка (<see cref="MinimapPicture"/>), поэтому кадр, которому
/// надо перерисовать место карты, за неё почти не платит.
/// <para>
/// Кэша в картинку (<c>BitmapCache</c>) здесь нет, хотя ADR 0011 его ставил: в Avalonia 12.1.1 он не
/// обновляется, когда визуал записывает свою операцию заново, и карта показывала содержимое, собранное
/// первым. Счётчики записи этого не видели, снимок инструментов — тоже: он рисует дерево заново, мимо
/// композитора; видно было только снимком самого окна.
/// </para>
/// </remarks>
internal sealed class MinimapContent : Control
{
    private MinimapSnapshot? _snapshot;
    private Matrix _matrix = Matrix.Identity;
    private double _scale = 1;
    private IBrush? _fill;
    private IBrush? _stroke;
    private IImmutableBrush? _immutableFill;
    private IImmutableBrush? _immutableStroke;

    public MinimapContent() => IsHitTestVisible = false;

    /// <summary>
    /// Сколько раз содержимое записывалось заново — для тестов.
    /// </summary>
    internal int Renders { get; private set; }

    /// <summary>
    /// Ставит содержимое и соответствие; то же самое записи не трогает.
    /// </summary>
    /// <remarks>
    /// Кисти сравниваются ссылкой на кисть хоста: неизменяемая копия новая на каждом снятии, и по ней
    /// содержимое записывалось бы заново на каждом кадре.
    /// </remarks>
    public void Show(MinimapSnapshot? snapshot, Matrix matrix, double scale, IBrush? fill, IBrush? stroke)
    {
        if (ReferenceEquals(snapshot, _snapshot) && matrix == _matrix && ReferenceEquals(fill, _fill) && ReferenceEquals(stroke, _stroke))
            return;

        _snapshot = snapshot;
        _matrix = matrix;
        _scale = scale;
        _fill = fill;
        _stroke = stroke;
        _immutableFill = fill?.ToImmutable();
        _immutableStroke = stroke?.ToImmutable();
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        Renders++;
        if (_snapshot is { } snapshot && Bounds.Width > 0 && Bounds.Height > 0)
            context.Custom(new Operation(new Rect(Bounds.Size), snapshot, _matrix, _scale, _immutableFill, _immutableStroke));
    }

    /// <summary>
    /// Картинка содержимого из кэша снимка — для тестов.
    /// </summary>
    internal SKImage? PictureImage => _snapshot?.Picture?.Image;

    /// <summary>
    /// Рисует содержимое путём Skia на данный холст — тем, каким операция рисует из аренды; для тестов.
    /// </summary>
    /// <returns><see langword="false"/>, если рисовать нечего или кисть не сплошная.</returns>
    internal bool RenderTo(SKCanvas canvas)
    {
        if (_snapshot is not { } snapshot)
            return false;

        var operation = new Operation(new Rect(Bounds.Size), snapshot, _matrix, _scale, _immutableFill, _immutableStroke);
        return operation.CanPaint && operation.RenderSkia(canvas, 1);
    }

    /// <summary>
    /// Содержимое карты: холстом Skia из аренды — пакетами, без него — по примитиву (ADR 0012).
    /// </summary>
    private sealed class Operation(
        Rect bounds, MinimapSnapshot snapshot, Matrix matrix, double scale, IImmutableBrush? fill, IImmutableBrush? stroke)
        : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        /// <summary>
        /// Все ли кисти холст из аренды нарисует.
        /// </summary>
        public bool CanPaint => SkiaCanvas.CanPaint(fill) && SkiaCanvas.CanPaint(stroke);

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            if (CanPaint)
            {
                using var lease = SkiaCanvas.TryLease(context);
                if (lease != null)
                {
                    RenderSkia(lease.SkCanvas, lease.CurrentOpacity);
                    return;
                }
            }

            RenderImmediate(context);
        }

        public bool RenderSkia(SKCanvas canvas, double opacity)
        {
            // Пикселей устройства на единицу мира: масштаб карты на плотность холста, который её нарисует.
            var density = Math.Max(Math.Abs(canvas.TotalMatrix.ScaleX), 1e-3f);
            var step = MinimapPicture.Step(scale * density);
            var (fillColor, strokeColor) = (SkiaCanvas.Color(fill, 1), SkiaCanvas.Color(stroke, 1));
            if (snapshot.Picture is not { } picture || !picture.Fits(step, fillColor, strokeColor))
                snapshot.Picture = picture = Rasterize(step, fillColor, strokeColor);

            if (picture.World.Width <= 0 || picture.World.Height <= 0)
                return true;

            using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(255 * Math.Clamp(opacity, 0, 1))) };
            SkiaCanvas.Enter(canvas, bounds, matrix);
            try
            {
                canvas.DrawImage(picture.Image, SkiaCanvas.ToSkia(picture.World), new SKSamplingOptions(SKFilterMode.Linear), paint);
            }
            finally
            {
                canvas.Restore();
            }

            return true;
        }

        /// <summary>
        /// Рисует содержимое в картинку со сглаживанием — по охвату содержимого, на ступени масштаба.
        /// </summary>
        private MinimapPicture Rasterize(double step, SKColor fillColor, SKColor strokeColor)
        {
            // Охват — элементы и контрольные точки кривых, с полем на толщину линии.
            var world = default(Rect);
            var any = false;
            foreach (var rect in snapshot.Items)
            {
                world = any ? world.Union(rect) : rect;
                any = true;
            }

            foreach (var c in snapshot.Curves)
            {
                foreach (var point in new[] { c.Source, c.SourceControl, c.TargetControl, c.Target })
                {
                    var dot = new Rect(point, default(Size));
                    world = any ? world.Union(dot) : dot;
                    any = true;
                }
            }

            world = world.Inflate(2 / step);
            var size = new SKSizeI(Math.Max(1, (int)Math.Ceiling(world.Width * step)), Math.Max(1, (int)Math.Ceiling(world.Height * step)));
            world = new Rect(world.Position, new Size(size.Width / step, size.Height / step));

            using var surface = SKSurface.Create(new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.Scale((float)step);
            canvas.Translate((float)-world.X, (float)-world.Y);

            if (fillColor.Alpha > 0)
            {
                using var path = new SKPath();
                foreach (var rect in snapshot.Items)
                    path.AddRect(SkiaCanvas.ToSkia(rect));

                using var paint = SkiaCanvas.Fill(fillColor);
                canvas.DrawPath(path, paint);
            }

            if (strokeColor.Alpha > 0 && snapshot.Curves.Length > 0)
            {
                using var path = new SKPath();
                foreach (var c in snapshot.Curves)
                    CurveSegments.Append(path, c.Source, c.SourceControl, c.TargetControl, c.Target, scale);

                // Точка толщиной: пиксель картинки, то есть почти пиксель карты.
                using var paint = SkiaCanvas.Stroke(strokeColor, 1 / step);
                canvas.DrawPath(path, paint);
            }

            return new MinimapPicture(surface.Snapshot(), world, step, fillColor, strokeColor);
        }

        private void RenderImmediate(ImmediateDrawingContext context)
        {
            using (context.PushClip(bounds))
            using (context.PushPreTransform(matrix))
            {
                if (fill != null)
                {
                    foreach (var rect in snapshot.Items)
                        context.FillRectangle(fill, rect);
                }

                if (stroke != null && snapshot.Curves.Length > 0)
                {
                    // Точка толщиной: после трансформации в пиксель карты.
                    var pen = new ImmutablePen(stroke, 1 / Math.Max(scale, 1e-9));
                    foreach (var c in snapshot.Curves)
                        CurveSegments.Draw(context, pen, c.Source, c.SourceControl, c.TargetControl, c.Target, scale);
                }
            }
        }
    }
}

/// <summary>
/// Рамка видимой области поверх содержимого миникарты.
/// </summary>
internal sealed class MinimapFrame : Control
{
    private Rect _frame;
    private IBrush? _fill;
    private IBrush? _stroke;

    public MinimapFrame() => IsHitTestVisible = false;

    /// <summary>
    /// Сколько раз рамка рисовалась — для тестов.
    /// </summary>
    internal int Renders { get; private set; }

    public void Show(Rect frame, IBrush? fill, IBrush? stroke)
    {
        if (frame == _frame && ReferenceEquals(fill, _fill) && ReferenceEquals(stroke, _stroke))
            return;

        _frame = frame;
        _fill = fill;
        _stroke = stroke;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        Renders++;
        context.DrawRectangle(_fill, _stroke is { } stroke ? new Pen(stroke) : null, _frame);
    }
}
