using Avalonia.Controls;

namespace ArxisStudio.Surface.UiDesigner;

/// <summary>
/// Пометка группы как участник единицы редактирования.
/// </summary>
/// <remarks>
/// Ядро не знает о группах (ADR 0003), но правка группы обязана попадать в
/// <see cref="SurfaceView.EditCompleted"/> и отменяться наравне с геометрией. Значение
/// читается через хранилище редактора (<see cref="IDesignGroupStore"/>, ADR 0002).
/// </remarks>
internal sealed class GroupEditFacet : IEditFacet
{
    private readonly DesignEditor _editor;

    public GroupEditFacet(DesignEditor editor) => _editor = editor;

    public object? Read(Control target) => _editor.GetGroupOf(target);

    // Ordinal: идентификатор группы — не текст на языке пользователя.
    public bool AreEqual(object? before, object? after)
        => string.Equals((string?)before, (string?)after, System.StringComparison.Ordinal);

    public TargetChange CreateChange(Control target, object? before, object? after)
        => new DesignGroupChange(target, (string?)before, (string?)after);
}
