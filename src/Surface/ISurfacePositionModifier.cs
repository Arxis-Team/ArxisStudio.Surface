using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ArxisStudio.Surface;

/// <summary>
/// Поправляет позицию или край, которые предложил жест: привязка к сетке, направляющим
/// и интервалам.
/// </summary>
/// <remarks>
/// Шов между жестами ядра и инструментами редактирования (ADR 0003). Жест ведёт ядро
/// и считает, куда элемент хочет встать; где он встанет, решает модификатор, если он
/// подключён службой. Без модификатора позиция только округляется до целого пикселя —
/// так же, как делает привязка, когда она выключена.
/// </remarks>
internal interface ISurfacePositionModifier
{
    /// <summary>Снимает всё, что нужно на время жеста, для указанного target'а.</summary>
    void Begin(Control movingTarget);

    /// <summary>Закрывает жест и убирает то, что показывалось во время него.</summary>
    void End();

    /// <summary>Ставит прямоугольник на место.</summary>
    Point ResolveOrigin(Point proposed, Size size, KeyModifiers modifiers);

    /// <summary>Может ли сработать поправка двигающегося края при resize.</summary>
    bool CanSnapEdge(KeyModifiers modifiers);

    /// <summary>Поправляет координату двигающегося края.</summary>
    double ResolveEdge(double edge, Rect proposed, bool xAxis, bool farEdge, KeyModifiers modifiers);

    /// <summary>Показывает результат по применённой геометрии resize.</summary>
    void PublishApplied(Rect bounds);
}
