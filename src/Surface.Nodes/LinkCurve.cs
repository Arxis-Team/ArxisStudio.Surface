namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Изгиб провода: как длина касательной зависит от расстояния между концами (ADR 0015).
/// </summary>
/// <remarks>
/// Правило и умолчания — Blueprint (<c>UGraphEditorSettings</c>, «Spline tangent»). Касательная —
/// эрмитова, длиной <c>min(|dx|, HorizontalRange) · HorizontalFactor + min(|dy|, VerticalRange) ·
/// VerticalFactor</c>, отдельно для провода вперёд — вход по ходу выхода — и назад; плечо кубической
/// кривой — её треть. Вертикаль в длине нужна, чтобы почти вертикальный провод изгибался плавно, а не
/// шёл прямо с изломом у порта; большие множители назад — чтобы обратный провод обходил узлы широкой
/// петлёй, а не шпилькой у самого порта. Объект читается при пересчёте кривой: правку видно после
/// замены объекта целиком.
/// </remarks>
public sealed class LinkCurve
{
    /// <summary>
    /// Умолчание редактора — числа Blueprint.
    /// </summary>
    internal static readonly LinkCurve Default = new();

    /// <summary>
    /// Получает или задает предел горизонтального расстояния, которое удлиняет касательную провода
    /// вперёд. По умолчанию 1000.
    /// </summary>
    public double ForwardHorizontalRange { get; set; } = 1000;

    /// <summary>
    /// Получает или задает вклад горизонтального расстояния в касательную провода вперёд. По умолчанию 1.
    /// </summary>
    public double ForwardHorizontalFactor { get; set; } = 1;

    /// <summary>
    /// Получает или задает предел вертикального расстояния для провода вперёд. По умолчанию 1000.
    /// </summary>
    public double ForwardVerticalRange { get; set; } = 1000;

    /// <summary>
    /// Получает или задает вклад вертикального расстояния в касательную провода вперёд. По умолчанию 1.
    /// </summary>
    public double ForwardVerticalFactor { get; set; } = 1;

    /// <summary>
    /// Получает или задает предел горизонтального расстояния для провода назад. По умолчанию 200.
    /// </summary>
    public double BackwardHorizontalRange { get; set; } = 200;

    /// <summary>
    /// Получает или задает вклад горизонтального расстояния в касательную провода назад. По умолчанию 3.
    /// </summary>
    public double BackwardHorizontalFactor { get; set; } = 3;

    /// <summary>
    /// Получает или задает предел вертикального расстояния для провода назад. По умолчанию 200.
    /// </summary>
    public double BackwardVerticalRange { get; set; } = 200;

    /// <summary>
    /// Получает или задает вклад вертикального расстояния в касательную провода назад. По умолчанию 1,5.
    /// </summary>
    public double BackwardVerticalFactor { get; set; } = 1.5;
}
