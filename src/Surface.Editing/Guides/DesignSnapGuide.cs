using System;

namespace ArxisStudio.Surface.Editing;

/// <summary>
/// Ось, вдоль которой работает направляющая.
/// </summary>
public enum DesignSnapGuideOrientation
{
    /// <summary>Вертикальная линия: выравнивает по оси X.</summary>
    Vertical,

    /// <summary>Горизонтальная линия: выравнивает по оси Y.</summary>
    Horizontal
}

/// <summary>
/// Вид выравнивания, породившего направляющую.
/// </summary>
/// <remarks>
/// Нужен только для оформления: по краю и по центру принято показывать разным цветом,
/// потому что это разные отношения — «встал вплотную» и «встал симметрично».
/// </remarks>
public enum DesignSnapGuideKind
{
    /// <summary>Выравнивание, в котором участвует хотя бы один край.</summary>
    Edge,

    /// <summary>Выравнивание центра к центру.</summary>
    Centre
}

/// <summary>
/// Направляющая, показанная во время жеста, в мировых координатах.
/// </summary>
/// <remarks>
/// <see cref="Position"/> — координата самой линии по той оси, которую она
/// выравнивает; <see cref="Start"/> и <see cref="End"/> задают протяжённость
/// по другой оси.
/// <para>
/// Линия намеренно не бесконечная: она натянута между перетаскиваемым элементом
/// и соседом, с которым он совпал. По длине видно, к чему именно идёт
/// выравнивание, — линия во весь холст этого не показывает.
/// </para>
/// </remarks>
public readonly struct DesignSnapGuide : IEquatable<DesignSnapGuide>
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignSnapGuide"/>.
    /// </summary>
    /// <param name="orientation">Ось, вдоль которой работает направляющая.</param>
    /// <param name="position">Координата линии по выравниваемой оси.</param>
    /// <param name="start">Начало линии по другой оси.</param>
    /// <param name="end">Конец линии по другой оси.</param>
    /// <param name="kind">Вид выравнивания, породившего линию.</param>
    public DesignSnapGuide(
        DesignSnapGuideOrientation orientation,
        double position,
        double start,
        double end,
        DesignSnapGuideKind kind = DesignSnapGuideKind.Edge)
    {
        Orientation = orientation;
        Position = position;
        Start = start;
        End = end;
        Kind = kind;
    }

    /// <summary>Получает ось, вдоль которой работает направляющая.</summary>
    public DesignSnapGuideOrientation Orientation { get; }

    /// <summary>Получает координату линии по выравниваемой оси.</summary>
    public double Position { get; }

    /// <summary>Получает начало линии по другой оси.</summary>
    public double Start { get; }

    /// <summary>Получает конец линии по другой оси.</summary>
    public double End { get; }

    /// <summary>Получает вид выравнивания, породившего линию.</summary>
    public DesignSnapGuideKind Kind { get; }

    /// <inheritdoc />
    public bool Equals(DesignSnapGuide other)
        => Orientation == other.Orientation
           && Position.Equals(other.Position)
           && Start.Equals(other.Start)
           && End.Equals(other.End)
           && Kind == other.Kind;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DesignSnapGuide other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Orientation, Position, Start, End, Kind);
}
