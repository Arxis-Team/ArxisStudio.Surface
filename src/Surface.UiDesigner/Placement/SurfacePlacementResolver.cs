using Avalonia.Controls;
using Avalonia.VisualTree;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Surface.UiDesigner.Placement;

/// <summary>
/// Выводит стратегию размещения из родительской раскладки контрола.
/// </summary>
/// <remarks>
/// Кеша нет намеренно: это один <c>GetVisualParent()</c> и switch по типу.
/// Жесты и так снимают свой target один раз на жест.
/// </remarks>
internal static class SurfacePlacementResolver
{
    /// <summary>
    /// Возвращает стратегию размещения для контрола.
    /// </summary>
    public static ISurfacePlacementStrategy Resolve(Control target)
    {
        // Контрол вне дерева: положение определяют только координаты поверхности.
        return target.GetVisualParent() switch
        {
            null => AbsolutePlacementStrategy.Instance,
            AbsolutePanel => AbsolutePlacementStrategy.Instance,
            Canvas => CanvasPlacementStrategy.Instance,
            StackPanel => StackPlacementStrategy.Instance,
            WrapPanel => StackPlacementStrategy.Instance,
            Grid => FixedPlacementStrategy.Grid,
            DockPanel => FixedPlacementStrategy.Dock,
            _ => FixedPlacementStrategy.ContentHost
        };
    }
}
