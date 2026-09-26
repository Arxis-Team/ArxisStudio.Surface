using Avalonia;

namespace ArxisStudio.Surface;

/// <summary>
/// Снимок движения указателя: чьё, куда и каким по счёту.
/// </summary>
/// <param name="PointerId">Устройство, которое двинулось.</param>
/// <param name="World">Положение в мировых координатах.</param>
/// <param name="MoveCount">Номер движения; строго растёт на каждое.</param>
/// <remarks>
/// Номер нужен, чтобы отличить «указатель двинулся внутри жеста» от «снимок остался
/// с прошлого раза»: без него жест, поднятый программно, читал бы чужое положение
/// и получал нулевую поправку. Устройство нужно, чтобы движение пера или второго
/// касания не уводило край, который тянут мышью.
/// </remarks>
internal readonly record struct PointerSample(int PointerId, Point World, int MoveCount);
