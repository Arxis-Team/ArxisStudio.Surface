using Avalonia;
using Avalonia.Controls;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Surface.UiDesigner.Placement;

/// <summary>
/// Определяет, что редактор может сделать с позицией контрола в его родительской раскладке.
/// </summary>
internal enum DesignMoveSemantics
{
    /// <summary>
    /// Позицией распоряжается раскладка, и порядок среди соседей смысла не имеет.
    /// </summary>
    None,

    /// <summary>
    /// Позицию можно задать напрямую.
    /// </summary>
    Reposition,

    /// <summary>
    /// Позицию задать нельзя, но осмыслена перестановка среди соседей.
    /// </summary>
    Reorder
}

/// <summary>
/// Способ работы с позицией контрола, выведенный из его родительской раскладки.
/// </summary>
/// <remarks>
/// Существует потому, что <c>Layout.X</c>/<c>Layout.Y</c> читает единственная панель —
/// <see cref="AbsolutePanel"/>. Для всех остальных родителей
/// запись позиции уходит в пустоту, а редактор обязан либо сменить смысл жеста,
/// либо его не предлагать. Таблица возможностей снята с Avalonia в
/// <c>LayoutHonourProbeTests</c>.
/// <para>
/// Размер в стратегию не входит намеренно: явный <c>Width</c>/<c>Height</c> honours
/// любая панель, потому что применяется до выравнивания. Ограничения размера —
/// это <c>MinWidth</c>/<c>MaxWidth</c> и границы контейнера, а не раскладка.
/// </para>
/// </remarks>
internal interface IDesignPlacementStrategy
{
    /// <summary>
    /// Получает диагностическое имя стратегии.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Получает способ работы с позицией.
    /// </summary>
    DesignMoveSemantics MoveSemantics { get; }

    /// <summary>
    /// Возвращает позицию контрола в design-координатах.
    /// </summary>
    Point GetPosition(Control target, DesignEditor editor);

    /// <summary>
    /// Задаёт позицию контрола в design-координатах.
    /// </summary>
    void SetPosition(Control target, Point designPosition, DesignEditor editor);
}
