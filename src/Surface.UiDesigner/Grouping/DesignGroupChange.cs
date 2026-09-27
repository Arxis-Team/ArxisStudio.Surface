using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Surface.UiDesigner;

/// <summary>
/// Описывает изменение принадлежности одного target к группе.
/// </summary>
public sealed class DesignGroupChange : TargetChange
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignGroupChange"/>.
    /// </summary>
    /// <param name="target">Изменённый контрол.</param>
    /// <param name="oldId">Группа до изменения.</param>
    /// <param name="newId">Группа после изменения.</param>
    public DesignGroupChange(Control target, string? oldId, string? newId)
        : base(target)
    {
        OldId = oldId;
        NewId = newId;
    }

    /// <summary>
    /// Получает группу до изменения.
    /// </summary>
    public string? OldId { get; }

    /// <summary>
    /// Получает группу после изменения.
    /// </summary>
    public string? NewId { get; }

    internal override void ApplyTo(SurfaceView view, bool revert)
    {
        if (view is DesignEditor editor)
            editor.ApplyGroup(Target, revert ? OldId : NewId);
    }
}
