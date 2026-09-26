using Avalonia;
using Avalonia.Interactivity;

namespace ArxisStudio.Surface;

/// <summary>
/// Определяет направление изменения размера элемента.
/// </summary>
public enum ResizeDirection
{
    /// <summary>
    /// Изменение размера по верхней стороне.
    /// </summary>
    Top,

    /// <summary>
    /// Изменение размера по нижней стороне.
    /// </summary>
    Bottom,

    /// <summary>
    /// Изменение размера по левой стороне.
    /// </summary>
    Left,

    /// <summary>
    /// Изменение размера по правой стороне.
    /// </summary>
    Right,

    /// <summary>
    /// Изменение размера по верхнему левому углу.
    /// </summary>
    TopLeft,

    /// <summary>
    /// Изменение размера по верхнему правому углу.
    /// </summary>
    TopRight,

    /// <summary>
    /// Изменение размера по нижнему левому углу.
    /// </summary>
    BottomLeft,

    /// <summary>
    /// Изменение размера по нижнему правому углу.
    /// </summary>
    BottomRight
}

/// <summary>
/// Содержит данные о текущем шаге изменения размера.
/// </summary>
public class ResizeDeltaEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Получает вектор изменения размера.
    /// </summary>
    public Vector Delta { get; }

    /// <summary>
    /// Получает направление активной ручки изменения размера.
    /// </summary>
    public ResizeDirection Direction { get; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ResizeDeltaEventArgs"/>.
    /// </summary>
    public ResizeDeltaEventArgs(Vector delta, ResizeDirection direction, RoutedEvent routedEvent)
        : base(routedEvent)
    {
        Delta = delta;
        Direction = direction;
    }
}

/// <summary>
/// Содержит данные о начале изменения размера.
/// </summary>
public class ResizeStartedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Получает направление активной ручки.
    /// </summary>
    public ResizeDirection Direction { get; }

    /// <summary>
    /// Получает начальный вектор изменения.
    /// </summary>
    public Vector Vector { get; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ResizeStartedEventArgs"/>.
    /// </summary>
    public ResizeStartedEventArgs(Vector vector, ResizeDirection direction, RoutedEvent routedEvent)
        : base(routedEvent)
    {
        Vector = vector;
        Direction = direction;
    }
}
