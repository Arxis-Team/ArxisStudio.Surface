using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Threading;

namespace ArxisStudio.Surface;

// Автопрокрутка у края: перетаскивание и рамка, доведённые до края, сдвигают холст.
// Часть SurfaceView; общее описание типа — в SurfaceView.cs.
public partial class SurfaceView
{
    // Кадр таймера. Шаг считается по прошедшему времени, а не по числу тиков, поэтому
    // скорость не зависит от того, успевает ли диспетчер.
    private static readonly TimeSpan AutoPanInterval = TimeSpan.FromMilliseconds(16);

    // Больше этого за один шаг не прокручивается: после заминки диспетчера холст иначе
    // прыгнул бы на всё накопленное время разом.
    private static readonly TimeSpan AutoPanMaxStep = TimeSpan.FromMilliseconds(50);

    private DispatcherTimer? _autoPanTimer;
    private readonly Stopwatch _autoPanClock = new();
    private Point _autoPanPoint;
    private Action<Point>? _autoPanReapply;

    /// <summary>
    /// Идёт ли сейчас автопрокрутка.
    /// </summary>
    internal bool IsAutoPanning => _autoPanTimer?.IsEnabled == true;

    /// <summary>
    /// Насколько сдвинулся холст на последнем шаге, в мировых координатах.
    /// </summary>
    /// <remarks>
    /// Относительно холста указатель ушёл ровно на столько же в обратную сторону — это и
    /// сообщает жест, у которого событие шага несёт смещение указателя.
    /// </remarks>
    internal Vector LastAutoPanShift { get; private set; }

    /// <summary>
    /// Сообщает положение указателя в жесте, который умеет автопрокрутку.
    /// </summary>
    /// <param name="screenPoint">Указатель в координатах редактора.</param>
    /// <param name="reapply">
    /// Пересчитывает жест по той же точке экрана после сдвига холста: указатель стоит, а
    /// под ним уже другое место холста, и жест обязан это увидеть.
    /// </param>
    /// <remarks>
    /// Зовётся на движении указателя, а не на входе в жест: нажатие у самого края не
    /// должно само по себе поехать холстом, пока человек ещё ничего не тянул.
    /// </remarks>
    internal void TrackAutoPan(Point screenPoint, Action<Point> reapply)
    {
        _autoPanPoint = screenPoint;
        _autoPanReapply = reapply;

        if (GetAutoPanVelocity(screenPoint) == default)
        {
            StopAutoPan();
            return;
        }

        if (IsAutoPanning)
            return;

        _autoPanTimer ??= CreateAutoPanTimer();
        _autoPanClock.Restart();
        _autoPanTimer.Start();
    }

    /// <summary>
    /// Останавливает автопрокрутку. Жест зовёт это на выходе — и на отпускании, и на
    /// потере захвата.
    /// </summary>
    internal void StopAutoPan()
    {
        _autoPanTimer?.Stop();
        _autoPanClock.Reset();
        _autoPanReapply = null;
    }

    /// <summary>
    /// Скорость прокрутки для точки экрана, в пикселях экрана в секунду по каждой оси.
    /// </summary>
    /// <remarks>
    /// Оси независимы, как и везде в редакторе: в углу холст едет по диагонали. В полосе
    /// у края скорость растёт от нуля до <see cref="SurfaceInteractionOptions.AutoPanSpeed"/>
    /// линейно с глубиной, за краем она наибольшая — захваченный указатель уходит
    /// за пределы редактора, и там человек просит ехать быстрее, а не останавливаться.
    /// </remarks>
    internal Vector GetAutoPanVelocity(Point screenPoint)
    {
        var options = InteractionOptions;
        if (!options.IsAutoPanEnabled)
            return default;

        var edge = options.AutoPanEdge;
        var speed = options.AutoPanSpeed;
        if (edge <= 0 || speed <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
            return default;

        return new Vector(
            Axis(screenPoint.X, Bounds.Width, edge) * speed,
            Axis(screenPoint.Y, Bounds.Height, edge) * speed);

        static double Axis(double position, double length, double edge)
        {
            // Полоса не шире половины редактора: в узком окне полосы с двух сторон
            // иначе перекрылись бы и тянули в обе стороны сразу.
            edge = Math.Min(edge, length / 2);

            if (position < edge)
                return -Math.Min(1, (edge - position) / edge);

            if (position > length - edge)
                return Math.Min(1, (position - (length - edge)) / edge);

            return 0;
        }
    }

    /// <summary>
    /// Один шаг автопрокрутки: сдвигает холст и пересчитывает жест.
    /// </summary>
    /// <param name="elapsed">Время с прошлого шага.</param>
    /// <returns><see langword="true"/>, если холст сдвинулся.</returns>
    /// <remarks>
    /// Таймер зовёт его сам; тесты — напрямую, потому что в безголовом режиме время стоит.
    /// </remarks>
    internal bool StepAutoPan(TimeSpan elapsed)
    {
        var reapply = _autoPanReapply;
        if (reapply == null)
            return false;

        if (elapsed > AutoPanMaxStep)
            elapsed = AutoPanMaxStep;

        var velocity = GetAutoPanVelocity(_autoPanPoint);
        if (velocity == default || elapsed <= TimeSpan.Zero)
            return false;

        // Скорость задана на экране, а холст сдвигается в своих единицах: на отдалении
        // та же скорость проносит больше холста — ровно то, что видит человек.
        LastAutoPanShift = velocity * elapsed.TotalSeconds / ViewportZoom;
        ViewportLocation += LastAutoPanShift;

        // Изменение размера считает от снимка указателя на холсте, а не от точки экрана:
        // снимок обязан увидеть сдвиг раньше, чем жест пересчитается.
        RefreshPointerSample();
        reapply(_autoPanPoint);
        return true;
    }

    private DispatcherTimer CreateAutoPanTimer()
    {
        var timer = new DispatcherTimer { Interval = AutoPanInterval };
        timer.Tick += (_, _) =>
        {
            var elapsed = _autoPanClock.Elapsed;
            _autoPanClock.Restart();

            if (!StepAutoPan(elapsed))
                StopAutoPan();
        };

        return timer;
    }
}
