using Avalonia.Controls;

namespace ArxisStudio.Surface;

/// <summary>
/// Участник единицы редактирования сверх геометрии и порядка перекрытия.
/// </summary>
/// <remarks>
/// Ядро знает две вещи, которые пишет в чужие контролы, — геометрию и <c>ZIndex</c>.
/// Всё остальное, что слой выше записывает через свой шов и хочет видеть в
/// <see cref="SurfaceView.EditCompleted"/>, приходит участником: его значение
/// снимается на первом касании target'а, как и геометрия, и отбрасывается, если
/// к концу жеста вернулось к исходному.
/// </remarks>
internal interface IEditFacet
{
    /// <summary>
    /// Читает текущее значение у target'а.
    /// </summary>
    object? Read(Control target);

    /// <summary>
    /// Совпадают ли два значения — то есть не было ли изменения.
    /// </summary>
    bool AreEqual(object? before, object? after);

    /// <summary>
    /// Описывает изменение для контракта.
    /// </summary>
    DesignChange CreateChange(Control target, object? before, object? after);
}
