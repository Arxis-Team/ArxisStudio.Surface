using Avalonia;
using Avalonia.Controls;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Surface.UiDesigner.Placement;

/// <summary>
/// Определяет, что редактор может сделать с позицией контрола в его родительской раскладке.
/// </summary>
internal enum SurfaceMoveSemantics
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
internal interface ISurfacePlacementStrategy
{
    /// <summary>
    /// Получает диагностическое имя стратегии.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Получает способ работы с позицией.
    /// </summary>
    SurfaceMoveSemantics MoveSemantics { get; }

    /// <summary>
    /// Возвращает позицию контрола в координатах поверхности.
    /// </summary>
    Point GetPosition(Control target, UiDesignerView editor);

    /// <summary>
    /// Задаёт позицию контрола в координатах поверхности.
    /// </summary>
    void SetPosition(Control target, Point surfacePosition, UiDesignerView editor);
}
