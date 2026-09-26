using Avalonia.Interactivity;

namespace ArxisStudio.Surface;

/// <summary>
/// Содержит данные о начале операции перетаскивания.
/// </summary>
public class DragStartedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Получает горизонтальное смещение от стартовой точки.
    /// </summary>
    public double HorizontalOffset { get; }

    /// <summary>
    /// Получает вертикальное смещение от стартовой точки.
    /// </summary>
    public double VerticalOffset { get; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DragStartedEventArgs"/>.
    /// </summary>
    /// <param name="horizontalOffset">Начальное горизонтальное смещение.</param>
    /// <param name="verticalOffset">Начальное вертикальное смещение.</param>
    public DragStartedEventArgs(double horizontalOffset, double verticalOffset)
    {
        HorizontalOffset = horizontalOffset;
        VerticalOffset = verticalOffset;
    }
}

/// <summary>
/// Содержит данные о промежуточном шаге перетаскивания.
/// </summary>
public class DragDeltaEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Получает изменение по горизонтали за текущий шаг.
    /// </summary>
    public double HorizontalChange { get; }

    /// <summary>
    /// Получает изменение по вертикали за текущий шаг.
    /// </summary>
    public double VerticalChange { get; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DragDeltaEventArgs"/>.
    /// </summary>
    /// <param name="horizontalChange">Изменение по горизонтали.</param>
    /// <param name="verticalChange">Изменение по вертикали.</param>
    public DragDeltaEventArgs(double horizontalChange, double verticalChange)
    {
        HorizontalChange = horizontalChange;
        VerticalChange = verticalChange;
    }
}

/// <summary>
/// Содержит данные о завершении операции перетаскивания.
/// </summary>
public class DragCompletedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Получает суммарное изменение по горизонтали.
    /// </summary>
    public double HorizontalChange { get; }

    /// <summary>
    /// Получает суммарное изменение по вертикали.
    /// </summary>
    public double VerticalChange { get; }

    /// <summary>
    /// Получает значение, указывающее, была ли операция отменена.
    /// </summary>
    public bool Canceled { get; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DragCompletedEventArgs"/>.
    /// </summary>
    /// <param name="horizontalChange">Суммарное изменение по горизонтали.</param>
    /// <param name="verticalChange">Суммарное изменение по вертикали.</param>
    /// <param name="canceled"><see langword="true"/>, если операция была отменена; иначе <see langword="false"/>.</param>
    public DragCompletedEventArgs(double horizontalChange, double verticalChange, bool canceled)
    {
        HorizontalChange = horizontalChange;
        VerticalChange = verticalChange;
        Canceled = canceled;
    }
}
