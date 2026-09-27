using Avalonia.Controls;
using ArxisStudio.Surface;

namespace UiDesigner.Demo;

/// <summary>
/// Перестановка среди соседей как запись истории.
/// </summary>
/// <remarks>
/// Перестановку выполняет приложение — редактор только просит о ней (ADR 0001), — значит,
/// и записывает её приложение. Кладётся она в тот же <see cref="SurfaceHistory"/>, что и
/// правки редактора: иначе структурная правка не только не отменялась бы, но и не сбрасывала
/// повтор, и повтор возвращал бы геометрию поверх дерева, которое с тех пор изменилось.
/// </remarks>
public sealed class ReorderChange : ISurfaceChange
{
    private readonly Panel _panel;
    private readonly int _oldIndex;
    private readonly int _newIndex;

    public ReorderChange(Panel panel, int oldIndex, int newIndex)
    {
        _panel = panel;
        _oldIndex = oldIndex;
        _newIndex = newIndex;
    }

    public void Revert() => _panel.Children.Move(_newIndex, _oldIndex);

    public void Reapply() => _panel.Children.Move(_oldIndex, _newIndex);
}
