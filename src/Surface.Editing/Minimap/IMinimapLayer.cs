using System.Collections.Generic;
using Avalonia;
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
/// Слой отдаёт кривые в мировых координатах, а миникарта держит их вместе с контейнерами, пока
/// содержимое не сменится (ADR 0007), и рисует своей операцией, а не геометрией (ADR 0011): границы
/// геометрии на десять тысяч фигур композитор мерил бы на каждой перерисовке. О смене своего
/// содержимого слой сообщает поверхности — <see cref="SurfaceView.OnContentChanged"/>.
/// </para>
/// </remarks>
internal interface IMinimapLayer
{
    /// <summary>
    /// Добавляет свои кривые, в мировых координатах.
    /// </summary>
    /// <param name="curves">Кривые, которые собирает миникарта.</param>
    void Build(List<MinimapCurve> curves);

    /// <summary>
    /// Находит кисть обводки своих кривых.
    /// </summary>
    /// <param name="minimap">Миникарта — за ресурсами её темы.</param>
    /// <returns>Кисть, или <see langword="null"/>, если слой не рисуется.</returns>
    IBrush? FindStroke(SurfaceMinimap minimap);
}

/// <summary>
/// Кубическая кривая слоя на миникарте, в мировых координатах.
/// </summary>
internal readonly record struct MinimapCurve(Point Source, Point SourceControl, Point TargetControl, Point Target);
