using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using ArxisStudio.Surface.Nodes;

namespace Nodes.Demo.Automation;

/// <summary>
/// Панорама по кадрам настоящего рендерера — замер того, что безголовый стенд не видит.
/// </summary>
/// <remarks>
/// На каждом кадре анимации холст сдвигается на шаг, как при протяжке средней кнопкой, и
/// замеряется: время раскладки на UI-потоке, байты, выделенные потоком, и промежуток между кадрами.
/// Промежуток дольше кадра монитора значит пропущенный кадр — чей бы он ни был: раскладки, отрисовки
/// или композиции. Отвечает канал, когда замер кончится (<c>panBench</c> запускает, <c>benchResult</c>
/// спрашивает).
/// </remarks>
internal sealed class PanBenchmark
{
    private readonly TopLevel _top;
    private readonly NodeEditor _editor;
    private readonly Action<int> _tick;
    private readonly Vector _step;
    private readonly int _frames;
    private readonly List<double> _layoutMs = new();
    private readonly List<double> _intervalMs = new();
    private readonly List<long> _allocated = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _last = double.NaN;
    private readonly int[] _collections = { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
    private readonly TimeSpan _pause = GC.GetTotalPauseDuration();

    private PanBenchmark(TopLevel top, NodeEditor editor, Action<int> tick, Vector step, int frames)
    {
        _top = top;
        _editor = editor;
        _tick = tick;
        _step = step;
        _frames = frames;
    }

    /// <summary>Результат, когда замер кончился; иначе <see langword="null"/>.</summary>
    public Dictionary<string, object?>? Result { get; private set; }

    /// <summary>
    /// Запускает замер: <paramref name="frames"/> кадров со сдвигом на <paramref name="screenStep"/>
    /// пикселей экрана за кадр.
    /// </summary>
    /// <remarks>
    /// <paramref name="tick"/> меняет на каждом кадре что-то видимое вне редактора: кадр, в котором ничего
    /// не изменилось, рендерер пропускает, и кадры анимации тогда приходят реже — замер мерил бы простой.
    /// </remarks>
    public static PanBenchmark Start(TopLevel top, NodeEditor editor, Action<int> tick, Vector screenStep, int frames)
    {
        var bench = new PanBenchmark(top, editor, tick, screenStep / editor.ViewportZoom, frames);
        top.RequestAnimationFrame(bench.OnFrame);
        return bench;
    }

    private void OnFrame(TimeSpan _)
    {
        var now = _clock.Elapsed.TotalMilliseconds;
        if (!double.IsNaN(_last))
            _intervalMs.Add(now - _last);

        _last = now;

        if (_layoutMs.Count >= _frames)
        {
            Finish();
            return;
        }

        _tick(_layoutMs.Count);
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        _editor.ViewportLocation += _step;
        _top.UpdateLayout();
        watch.Stop();
        _allocated.Add(GC.GetAllocatedBytesForCurrentThread() - bytes);
        _layoutMs.Add(watch.Elapsed.TotalMilliseconds);

        _top.RequestAnimationFrame(OnFrame);
    }

    private void Finish()
    {
        static double Percentile(List<double> values, double p)
        {
            if (values.Count == 0)
                return 0;

            var sorted = values.OrderBy(v => v).ToList();
            return sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p * sorted.Count) - 1)];
        }

        // Первый промежуток — от запуска до первого кадра, он не о панораме.
        var intervals = _intervalMs.Skip(1).ToList();
        Result = new()
        {
            ["frames"] = _layoutMs.Count,
            ["zoom"] = Math.Round(_editor.ViewportZoom, 3),
            ["layoutMsMedian"] = Math.Round(Percentile(_layoutMs, 0.5), 3),
            ["layoutMsP95"] = Math.Round(Percentile(_layoutMs, 0.95), 3),
            ["intervalMsMedian"] = Math.Round(Percentile(intervals, 0.5), 2),
            ["intervalMsP95"] = Math.Round(Percentile(intervals, 0.95), 2),
            ["dropped"] = intervals.Count(i => i > 20),
            ["gc"] = $"{GC.CollectionCount(0) - _collections[0]}/{GC.CollectionCount(1) - _collections[1]}/{GC.CollectionCount(2) - _collections[2]}",
            ["gcPauseMs"] = Math.Round((GC.GetTotalPauseDuration() - _pause).TotalMilliseconds, 1),
            ["bytesPerFrameMedian"] = (long)Percentile(_allocated.Select(b => (double)b).ToList(), 0.5),
            ["realizedNodes"] = _editor.GetRealizedContainers().Count()
        };
    }
}
