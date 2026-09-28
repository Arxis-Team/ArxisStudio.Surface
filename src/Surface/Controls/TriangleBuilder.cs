using System.Collections.Generic;
using Avalonia;
using SkiaSharp;

namespace ArxisStudio.Surface;

/// <summary>
/// Прямоугольники треугольниками с цветом у вершин — для одного <see cref="SKCanvas.DrawVertices(SKVertices, SKBlendMode, SKPaint)"/>
/// на пакет (ADR 0012).
/// </summary>
/// <remarks>
/// Вызов холста стоит дороже, чем отрисовка десятка треугольников, и десять тысяч <c>DrawRect</c> брали
/// кадр целиком. Края выходят без сглаживания — у прямоугольников вдоль осей это ступень не больше
/// пикселя. Рисовать набор — краской без шейдера и режимом <see cref="SKBlendMode.Dst"/>: цвет берётся
/// у вершин.
/// </remarks>
internal sealed class TriangleBuilder(int quads)
{
    private readonly List<SKPoint> _points = new(quads * 6);
    private readonly List<SKColor> _colors = new(quads * 6);

    /// <summary>
    /// Прямоугольник, залитый цветом.
    /// </summary>
    public void Quad(Rect r, SKColor color) => Quad((float)r.Left, (float)r.Top, (float)r.Right, (float)r.Bottom, color);

    /// <summary>
    /// Рамка толщиной <paramref name="width"/> по краю прямоугольника — как обводка пером: половина внутрь,
    /// половина наружу.
    /// </summary>
    public void Frame(Rect r, double width, SKColor color)
    {
        var h = (float)(width / 2);
        var (left, top, right, bottom) = ((float)r.Left, (float)r.Top, (float)r.Right, (float)r.Bottom);
        Quad(left - h, top - h, right + h, top + h, color);
        Quad(left - h, bottom - h, right + h, bottom + h, color);
        Quad(left - h, top + h, left + h, bottom - h, color);
        Quad(right - h, top + h, right + h, bottom - h, color);
    }

    /// <summary>
    /// Нативный набор вершин; <see langword="null"/>, если рисовать нечего.
    /// </summary>
    public SKVertices? Build() =>
        _points.Count > 0 ? SKVertices.CreateCopy(SKVertexMode.Triangles, _points.ToArray(), _colors.ToArray()) : null;

    private void Quad(float left, float top, float right, float bottom, SKColor color)
    {
        _points.Add(new SKPoint(left, top));
        _points.Add(new SKPoint(right, top));
        _points.Add(new SKPoint(right, bottom));
        _points.Add(new SKPoint(left, top));
        _points.Add(new SKPoint(right, bottom));
        _points.Add(new SKPoint(left, bottom));
        for (var k = 0; k < 6; k++)
            _colors.Add(color);
    }
}
