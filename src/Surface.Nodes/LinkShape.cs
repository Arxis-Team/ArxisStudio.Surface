namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Фигура маркера или импульса на проводе (ADR 0014).
/// </summary>
/// <remarks>
/// Направленные фигуры смотрят по проводу — от выхода ко входу; круг и квадрат направления не
/// показывают. Своя фигура — <see cref="Custom"/> с геометрией в <see cref="LinkMarker.Geometry"/> или
/// <see cref="LinkPulse.Geometry"/>.
/// </remarks>
public enum LinkShape
{
    /// <summary>Круг — пузырь отладки, как в Blueprint.</summary>
    Circle,

    /// <summary>Закрашенная стрелка-треугольник, остриём по проводу.</summary>
    Arrow,

    /// <summary>Шеврон — незакрашенный угол, остриём по проводу.</summary>
    Chevron,

    /// <summary>Ромб, вытянутый по проводу.</summary>
    Diamond,

    /// <summary>Квадрат, повёрнутый по проводу.</summary>
    Square,

    /// <summary>Геометрия хоста в единичном квадрате от −1 до 1, остриём по оси X.</summary>
    Custom
}
