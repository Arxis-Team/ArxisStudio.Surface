using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DesignLayout = ArxisStudio.Surface.UiDesigner.Layout;
using DesignInteraction = ArxisStudio.Surface.Editing.DesignInteraction;
using ArxisStudio.Surface.Editing;
using ArxisStudio.Surface.UiDesigner.Placement;

namespace ArxisStudio.Surface.UiDesigner;

// Контекстные действия.
// Часть DesignEditor; общее описание типа — в DesignEditor.cs.
public partial class DesignEditor
{
    private void RetargetSelectionForContext(Point viewportPoint, KeyModifiers modifiers)
    {
        var worldPoint = GetWorldPosition(viewportPoint);
        if (!TryResolveContextTarget(worldPoint, out var hitTarget) || hitTarget == null)
            return;

        var target = hitTarget.Target;
        // У дизайнера форм каждый контейнер — DesignEditorItem.
        var container = (DesignEditorItem)hitTarget.Container;
        if (target == null)
            return;

        var isTargetInSelection = SelectedDesignTargets.Any(selected =>
            ReferenceEquals(selected.Target, target));

        // Щёлкнули внутри выделения — оно принадлежит пользователю и остаётся как есть.
        // Иначе выбор схлопывался до кластера под курсором: две выбранные группы
        // превращались в одну ровно в тот момент, когда над ними вызывают меню,
        // и команда группировки становилась недоступной.
        if (isTargetInSelection)
            return;

        var index = IndexFromContainer(container);
        if (index >= 0)
        {
            Selection.Clear();
            Selection.Select(index);
        }

        // Context invocation must not use additive toggle semantics (e.g. Shift+RMB).
        var normalizedModifiers = modifiers & ~InputGestures.AdditiveSelectionModifiers;
        UpdateSelectionTargetFromPoint(container, viewportPoint, normalizedModifiers);
    }
}
