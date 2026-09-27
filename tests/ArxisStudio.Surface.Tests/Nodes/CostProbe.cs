using System.Diagnostics;

namespace ArxisStudio.Tests;

/// <summary>
/// Замер времени для стендов стоимости: медиана нескольких прогонов после прогрева по времени.
/// </summary>
internal static class CostProbe
{
    /// <summary>
    /// Замеров, из которых берётся медиана: одиночный переживает не каждый чужой квант процессора.
    /// </summary>
    private const int Runs = 5;

    /// <summary>
    /// Прогрев по времени, а не по числу вызовов: оптимизированный код метода среда ставит, когда
    /// JIT какое-то время не занят новыми методами, и короткий прогрев числом вызовов кончался раньше.
    /// </summary>
    private static readonly TimeSpan Warmup = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Сколько микросекунд стоит один вызов: медиана <see cref="Runs"/> прогонов по
    /// <paramref name="calls"/> вызовов.
    /// </summary>
    public static double MicrosecondsPerCall(Action call, int calls)
    {
        var warmup = Stopwatch.StartNew();
        do
        {
            for (var i = 0; i < calls; i++)
                call();
        }
        while (warmup.Elapsed < Warmup);

        var samples = new List<double>();
        for (var run = 0; run < Runs; run++)
        {
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < calls; i++)
                call();

            watch.Stop();
            samples.Add(watch.Elapsed.TotalMilliseconds * 1000 / calls);
        }

        samples.Sort();
        return samples[samples.Count / 2];
    }
}
