using Avalonia;
using Avalonia.Media;

namespace ArxisStudio.Surface.Editing;

/// <summary>
/// Что слой выше рисует на миникарте сверх контейнеров.
/// </summary>
/// <remarks>
/// Шов, а не событие: миникарта находит слой службой своего редактора
/// (<see cref="SurfaceView.GetService{T}"/>), как дизайнер форм находит <c>SnapService</c>. Так
/// редактор узлов дорисовывает связи, а миникарта о связях не знает ничего.
/// </remarks>
internal interface IMinimapLayer
{
    /// <summary>
    /// Рисует свой слой.
    /// </summary>
    /// <param name="context">Контекст отрисовки миникарты.</param>
    /// <param name="worldToMinimap">Перевод мировых координат в координаты миникарты.</param>
    /// <param name="minimap">Миникарта — за ресурсами её темы.</param>
    void Render(DrawingContext context, Matrix worldToMinimap, SurfaceMinimap minimap);
}
