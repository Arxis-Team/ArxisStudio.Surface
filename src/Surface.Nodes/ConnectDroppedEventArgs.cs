using System;
using Avalonia;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Аргументы события: новую связь отпустили мимо портов (ADR 0018).
/// </summary>
/// <remarks>
/// Редактор ничего не создаёт: у Blueprint отпущенный в пустоту провод открывает меню действий,
/// отобранных по типу пина, и новый узел встаёт в точку отпускания уже подключённым, — но что
/// предложить и что с чем соединить, решает хост. Меню хост вызывает сам, например
/// <see cref="SurfaceView.RequestContextAsync(SurfaceContextSource, Point, Avalonia.Input.KeyModifiers, System.Threading.CancellationToken)"/>
/// в точке <see cref="ViewportPoint"/>.
/// </remarks>
public sealed class ConnectDroppedEventArgs : EventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ConnectDroppedEventArgs"/>.
    /// </summary>
    /// <param name="port">Данные порта, от которого тянули связь.</param>
    /// <param name="direction">Направление этого порта.</param>
    /// <param name="location">Точка отпускания в мировых координатах.</param>
    /// <param name="viewportPoint">Точка отпускания в координатах редактора.</param>
    public ConnectDroppedEventArgs(object port, PortDirection direction, Point location, Point viewportPoint)
    {
        Port = port;
        Direction = direction;
        Location = location;
        ViewportPoint = viewportPoint;
    }

    /// <summary>
    /// Получает данные порта, от которого тянули связь.
    /// </summary>
    public object Port { get; }

    /// <summary>
    /// Получает направление порта: от выхода новый узел подключают своим входом, от входа — выходом.
    /// </summary>
    public PortDirection Direction { get; }

    /// <summary>
    /// Получает точку отпускания в мировых координатах — где ставить новый узел.
    /// </summary>
    public Point Location { get; }

    /// <summary>
    /// Получает точку отпускания в координатах редактора — где показывать меню.
    /// </summary>
    public Point ViewportPoint { get; }
}
