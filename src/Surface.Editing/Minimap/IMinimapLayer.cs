using Avalonia.Media;

namespace ArxisStudio.Surface.Editing;

/// <summary>
/// Что слой выше рисует на миникарте сверх контейнеров.
/// </summary>
/// <remarks>
/// Шов, а не событие: миникарта находит слой службой своего редактора
/// (<see cref="SurfaceView.GetService{T}"/>), как дизайнер интерфейса находит <c>SnapService</c>. Так
/// редактор узлов дорисовывает связи, а миникарта о связях не знает ничего.
/// <para>
/// Слой отдаёт фигуры в мировых координатах, а миникарта собирает их вместе с контейнерами и держит,
/// пока содержимое не сменится (ADR 0007): кривые не строятся заново на каждой перерисовке. О смене
/// своего содержимого слой сообщает поверхности — <see cref="SurfaceView.OnContentChanged"/>.
/// </para>
/// </remarks>
internal interface IMinimapLayer
{
    /// <summary>
    /// Добавляет свои фигуры, в мировых координатах.
    /// </summary>
    /// <param name="context">Геометрия, которую собирает миникарта.</param>
    void Build(StreamGeometryContext context);

    /// <summary>
    /// Находит кисть обводки своих фигур.
    /// </summary>
    /// <param name="minimap">Миникарта — за ресурсами её темы.</param>
    /// <returns>Кисть, или <see langword="null"/>, если слой не рисуется.</returns>
    IBrush? FindStroke(SurfaceMinimap minimap);
}
