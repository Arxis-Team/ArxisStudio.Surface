using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Skia;
using SkiaSharp;

namespace ArxisStudio.Surface;

/// <summary>
/// Холст Skia для больших слоёв — в аренду у контекста операции (ADR 0012).
/// </summary>
/// <remarks>
/// Вызов <see cref="ImmediateDrawingContext"/> заново строит краску Skia на каждый примитив — около
/// 1,2 мкс, и на десяти тысячах карточек это десятки миллисекунд кадра. Холст из аренды рисует теми же
/// примитивами, но краска у пакета одна. Аренды нет — отрисовка не на Skia, безголовый стенд — или кисть
/// не сплошная, и операция рисует прежним путём: он остаётся запасным.
/// </remarks>
internal static class SkiaCanvas
{
    /// <summary>
    /// Берёт холст в аренду; <see langword="null"/>, если отрисовка не на Skia.
    /// </summary>
    /// <remarks>
    /// Пока аренда жива, контекст рисовать не даёт: решать, каким путём рисовать, надо до неё.
    /// </remarks>
    public static ISkiaSharpApiLease? TryLease(ImmediateDrawingContext context) =>
        context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is ISkiaSharpApiLeaseFeature feature ? feature.Lease() : null;

    /// <summary>
    /// Нарисует ли кисть холст из аренды: нет кисти или она сплошная.
    /// </summary>
    public static bool CanPaint(IImmutableBrush? brush) => brush is null or ISolidColorBrush;

    /// <summary>
    /// Цвет сплошной кисти с её прозрачностью и прозрачностью контекста; без кисти — прозрачный.
    /// </summary>
    public static SKColor Color(IImmutableBrush? brush, double opacity)
    {
        if (brush is not ISolidColorBrush solid)
            return SKColors.Transparent;

        var color = solid.Color;
        var alpha = Math.Clamp(color.A * solid.Opacity * opacity, 0, 255);
        return new SKColor(color.R, color.G, color.B, (byte)Math.Round(alpha));
    }

    /// <summary>
    /// Матрица Avalonia в матрице Skia: у Avalonia точка — строка, у Skia — столбец.
    /// </summary>
    public static SKMatrix ToSkia(Matrix m) => new(
        (float)m.M11, (float)m.M21, (float)m.M31,
        (float)m.M12, (float)m.M22, (float)m.M32,
        (float)m.M13, (float)m.M23, (float)m.M33);

    /// <summary>
    /// Прямоугольник Avalonia в прямоугольнике Skia.
    /// </summary>
    public static SKRect ToSkia(Rect r) => new((float)r.Left, (float)r.Top, (float)r.Right, (float)r.Bottom);

    /// <summary>
    /// Ставит обрезку по границам операции и трансформацию её мира; вернуть холст —
    /// <see cref="SKCanvas.Restore"/>.
    /// </summary>
    public static void Enter(SKCanvas canvas, Rect bounds, Matrix world)
    {
        canvas.Save();
        canvas.ClipRect(ToSkia(bounds));
        var matrix = ToSkia(world);
        canvas.Concat(in matrix);
    }

    /// <summary>
    /// Краска заливки.
    /// </summary>
    public static SKPaint Fill(SKColor color) => new() { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };

    /// <summary>
    /// Краска обводки заданной толщины — в единицах мира, куда холст уже переведён.
    /// </summary>
    public static SKPaint Stroke(SKColor color, double width) => new()
    {
        Color = color,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = (float)width,
        StrokeJoin = SKStrokeJoin.Round,
        IsAntialias = true
    };
}
