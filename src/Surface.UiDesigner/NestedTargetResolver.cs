using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace ArxisStudio.Surface.UiDesigner;

/// <summary>
/// Резолвер дизайнера форм: target'ами становятся контролы внутри формы.
/// </summary>
/// <remarks>
/// В режиме <see cref="DesignContentMode.Annotated"/> — те, что размечены designer-метаданными,
/// в режиме <see cref="DesignContentMode.Loaded"/> — вся авторская разметка, без
/// внутренностей шаблонов.
/// </remarks>
internal sealed class NestedTargetResolver : ISurfaceTargetResolver
{
    public static readonly NestedTargetResolver Instance = new();

    private NestedTargetResolver()
    {
    }

    public IEnumerable<Control> EnumerateCandidates(SurfaceItem host)
        => host is DesignEditorItem item
            ? DesignEditor.EnumerateSelectionCandidates(item)
            : Array.Empty<Control>();

    public bool IsSelectable(Control target, SurfaceItem host)
        => host is DesignEditorItem item && DesignEditor.IsSelectableTarget(target, item);
}
