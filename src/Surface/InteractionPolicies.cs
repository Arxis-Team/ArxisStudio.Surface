using System;

namespace ArxisStudio.Surface;

/// <summary>
/// Политика изменения размера design target.
/// </summary>
[Flags]
public enum ResizePolicy
{
    /// <summary>
    /// Изменение размера запрещено.
    /// </summary>
    None = 0,

    /// <summary>
    /// Разрешено изменение размера по левой стороне.
    /// </summary>
    Left = 1 << 0,

    /// <summary>
    /// Разрешено изменение размера по верхней стороне.
    /// </summary>
    Top = 1 << 1,

    /// <summary>
    /// Разрешено изменение размера по правой стороне.
    /// </summary>
    Right = 1 << 2,

    /// <summary>
    /// Разрешено изменение размера по нижней стороне.
    /// </summary>
    Bottom = 1 << 3,

    /// <summary>
    /// Разрешено изменение размера только по горизонтали.
    /// </summary>
    Horizontal = Left | Right,

    /// <summary>
    /// Разрешено изменение размера только по вертикали.
    /// </summary>
    Vertical = Top | Bottom,

    /// <summary>
    /// Разрешено изменение размера по всем сторонам.
    /// </summary>
    All = Left | Top | Right | Bottom
}

/// <summary>
/// Политика перемещения design target.
/// </summary>
[Flags]
public enum MovePolicy
{
    /// <summary>
    /// Перемещение запрещено.
    /// </summary>
    None = 0,

    /// <summary>
    /// Разрешено перемещение по оси X.
    /// </summary>
    X = 1 << 0,

    /// <summary>
    /// Разрешено перемещение по оси Y.
    /// </summary>
    Y = 1 << 1,

    /// <summary>
    /// Разрешено перемещение по обеим осям.
    /// </summary>
    Both = X | Y
}
