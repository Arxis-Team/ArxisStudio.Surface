using System;

namespace ArxisStudio.Surface.Editing;

/// <summary>
/// Ось, вдоль которой работает пользовательская направляющая.
/// </summary>
public enum DesignGuideOrientation
{
    /// <summary>Вертикальная линия: задаёт координату по оси X.</summary>
    Vertical,

    /// <summary>Горизонтальная линия: задаёт координату по оси Y.</summary>
    Horizontal
}

/// <summary>
/// Пользовательская направляющая — линия, к которой притягиваются элементы.
/// </summary>
/// <remarks>
/// В отличие от направляющих выравнивания, которые редактор находит сам и показывает
/// только на время жеста, эта задаётся снаружи, видна всегда и живёт, пока её не убрали.
/// <para>
/// Хранится одной координатой: линия бесконечна вдоль своей оси. Протяжённости у неё нет
/// намеренно — отрезок описывает отношение двух элементов, а направляющая описывает
/// координату и ничего больше.
/// </para>
/// <para>
/// Координата — <b>мировая</b>, то есть та же, в которой лежат <c>Layout.DesignX</c>
/// и <c>Layout.DesignY</c> и которую видно на фоновой сетке.
/// </para>
/// </remarks>
public readonly struct DesignGuide : IEquatable<DesignGuide>
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignGuide"/>.
    /// </summary>
    /// <param name="orientation">Ось, вдоль которой работает направляющая.</param>
    /// <param name="position">Координата линии в мировых единицах.</param>
    public DesignGuide(DesignGuideOrientation orientation, double position)
    {
        Orientation = orientation;
        Position = position;
    }

    /// <summary>Получает ось, вдоль которой работает направляющая.</summary>
    public DesignGuideOrientation Orientation { get; }

    /// <summary>Получает координату линии в мировых единицах.</summary>
    public double Position { get; }

    /// <summary>Создаёт вертикальную направляющую.</summary>
    /// <param name="x">Координата по оси X в мировых единицах.</param>
    public static DesignGuide Vertical(double x) => new(DesignGuideOrientation.Vertical, x);

    /// <summary>Создаёт горизонтальную направляющую.</summary>
    /// <param name="y">Координата по оси Y в мировых единицах.</param>
    public static DesignGuide Horizontal(double y) => new(DesignGuideOrientation.Horizontal, y);

    /// <inheritdoc />
    public bool Equals(DesignGuide other)
        => Orientation == other.Orientation && Position.Equals(other.Position);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DesignGuide other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Orientation, Position);

    /// <summary>Сравнивает две направляющие.</summary>
    /// <param name="left">Левый операнд.</param>
    /// <param name="right">Правый операнд.</param>
    public static bool operator ==(DesignGuide left, DesignGuide right) => left.Equals(right);

    /// <summary>Сравнивает две направляющие на неравенство.</summary>
    /// <param name="left">Левый операнд.</param>
    /// <param name="right">Правый операнд.</param>
    public static bool operator !=(DesignGuide left, DesignGuide right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString()
        => Orientation == DesignGuideOrientation.Vertical
            ? $"X={Position}"
            : $"Y={Position}";
}
