using System.Diagnostics;
using ArxisStudio.Surface.Editing;

namespace ArxisStudio.Tests;

/// <summary>
/// Часы миникарты, которые идут только по команде теста.
/// </summary>
/// <remarks>
/// Карта пересобирает содержимое не чаще раза в <see cref="SurfaceMinimap.RebuildInterval"/>, а
/// отложенное дособирает таймером. В безголовом режиме таймер не срабатывает, а настоящие часы
/// идут, и без подмены исход теста зависел бы от скорости машины. Ставить часы надо до первой
/// отрисовки карты: первая сборка запоминает их показание.
/// </remarks>
internal sealed class MinimapClock
{
    private long _now = Stopwatch.GetTimestamp();

    private MinimapClock()
    {
    }

    /// <summary>
    /// Ставит карте часы, которые стоят, пока их не переведут.
    /// </summary>
    public static MinimapClock On(SurfaceMinimap map)
    {
        var clock = new MinimapClock();
        map.Clock = () => clock._now;
        return clock;
    }

    /// <summary>
    /// Переводит часы вперёд на интервал пересборки: следующая правка соберётся сразу.
    /// </summary>
    public void PassInterval() =>
        _now += (long)(SurfaceMinimap.RebuildInterval.TotalSeconds * Stopwatch.Frequency);
}
