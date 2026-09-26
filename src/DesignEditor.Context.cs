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
using ArxisStudio.Controls;
using ArxisStudio.Surface.Editing;
using ArxisStudio.Surface.UiDesigner.Placement;
using ArxisStudio.States;

namespace ArxisStudio;

// Контекстные действия.
// Часть DesignEditor; общее описание типа — в DesignEditor.cs.
public partial class DesignEditor
{
    /// <summary>
    /// Запрашивает контекстное меню программно.
    /// </summary>
    /// <param name="source">Источник запроса.</param>
    /// <param name="viewportPoint">Точка в координатах DesignEditor.</param>
    /// <param name="modifiers">Модификаторы ввода.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public Task RequestContextAsync(
        DesignEditorContextSource source,
        Point viewportPoint,
        KeyModifiers modifiers = KeyModifiers.None,
        CancellationToken cancellationToken = default)
    {
        var request = BuildContextRequest(source, viewportPoint, modifiers);
        return HandleContextRequestAsync(request, cancellationToken);
    }

    /// <summary>
    /// Запрашивает контекстное меню программно в позиции последнего ввода.
    /// </summary>
    public Task RequestContextAsync(CancellationToken cancellationToken = default)
    {
        var request = BuildContextRequest(DesignEditorContextSource.Programmatic, _lastMousePosition, LastInputModifiers);
        return HandleContextRequestAsync(request, cancellationToken);
    }

    private async Task HandleContextRequestAsync(DesignEditorContextRequest request, CancellationToken cancellationToken)
    {
        var resolvedActions = await ResolveContextActionsAsync(request, cancellationToken);
        var requestingArgs = new DesignEditorContextRequestingEventArgs(request)
        {
            Actions = resolvedActions
        };

        ContextMenuRequesting?.Invoke(this, requestingArgs);
        if (requestingArgs.Cancel)
            return;

        var actions = requestingArgs.Actions ?? Array.Empty<DesignEditorContextAction>();
        var handled = requestingArgs.Handled;
        if (!handled && actions.Count > 0)
            handled = ContextPresenter.TryShow(this, request, actions);

        ContextMenuResolved?.Invoke(this, new DesignEditorContextRequestedEventArgs(request, actions, handled));
    }

    private async Task<IReadOnlyList<DesignEditorContextAction>> ResolveContextActionsAsync(
        DesignEditorContextRequest request,
        CancellationToken cancellationToken)
    {
        if (ContextActionProviders.Count == 0)
            return Array.Empty<DesignEditorContextAction>();

        var result = new List<DesignEditorContextAction>();
        foreach (var provider in ContextActionProviders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var actions = await provider.GetActionsAsync(this, request, cancellationToken);
            if (actions == null || actions.Count == 0)
                continue;

            foreach (var action in actions)
            {
                if (action.IsVisible)
                    result.Add(action);
            }
        }

        return result
            .OrderBy(static a => a.Group, StringComparer.Ordinal)
            .ThenBy(static a => a.Order)
            .ToArray();
    }

    private DesignEditorContextRequest BuildContextRequest(
        DesignEditorContextSource source,
        Point viewportPoint,
        KeyModifiers modifiers)
    {
        var worldPoint = GetWorldPosition(viewportPoint);
        var hasHitTarget = TryResolveContextTarget(worldPoint, out var hitTarget);
        var selection = SelectedDesignTargets;
        var scope = DesignEditorContextScope.Surface;
        var topLevel = TopLevel.GetTopLevel(this);

        if (selection.Count > 1 &&
            hitTarget != null &&
            selection.Any(selected => ReferenceEquals(selected.Target, hitTarget.Target)))
        {
            scope = DesignEditorContextScope.Selection;
        }
        else if (hasHitTarget && hitTarget != null)
        {
            scope = hitTarget.Scope == DesignSelectionScope.Container
                ? DesignEditorContextScope.Container
                : DesignEditorContextScope.NestedTarget;
        }

        return new DesignEditorContextRequest
        {
            Scope = scope,
            Target = hitTarget,
            Selection = selection,
            WorldPoint = worldPoint,
            ViewportPoint = viewportPoint,
            ScreenPoint = topLevel?.PointToScreen(viewportPoint) ?? default,
            Modifiers = modifiers,
            Source = source
        };
    }

    private bool TryResolveContextTarget(Point worldPoint, out DesignSelectionTarget? target)
    {
        target = null;
        var container = FindContainerAtWorldPoint(worldPoint);
        if (container == null)
            return false;

        Control? bestMatch = null;
        Rect bestBounds = default;
        var bestDepth = -1;

        foreach (var control in EnumerateSelectionCandidates(container))
        {
            if (!IsSelectableTarget(control, container))
                continue;

            if (!TryGetDesignBounds(control, out var bounds) || !bounds.Contains(worldPoint))
                continue;

            var depth = GetVisualDepth(control, container);
            if (bestMatch == null ||
                depth > bestDepth ||
                (depth == bestDepth && bounds.Width * bounds.Height < bestBounds.Width * bestBounds.Height))
            {
                bestMatch = control;
                bestBounds = bounds;
                bestDepth = depth;
            }
        }

        // Container в контракте target — это владеющий item верхнего уровня,
        // согласованно со snapshot'ом выделения; глубина передаётся через Depth.
        var resolvedTarget = (Control?)bestMatch ?? container;
        var ownerItem = ResolveOwningItem(container) ?? container;
        target = new DesignSelectionTarget(ownerItem, resolvedTarget, _groupStore);
        return true;
    }

    /// <summary>
    /// Запускает запрос контекста, не дожидаясь его завершения.
    /// </summary>
    /// <remarks>
    /// Указатель ждать не может: обработчик нажатия обязан вернуться сразу. Отсюда
    /// два следствия, которых раньше не было.
    /// <para>
    /// Новый запрос отменяет предыдущий. Провайдер объявлен асинхронным, значит он
    /// вправе ходить в свою модель; два правых клика подряд доводили до конца оба
    /// запроса, и меню разрешалось дважды — второй показ поверх первого.
    /// </para>
    /// <para>
    /// Упавший провайдер больше не исчезает молча. Публичного события об ошибке
    /// здесь нет намеренно — контракт провайдера асинхронный, и ловить свои
    /// исключения хост умеет сам, — но в лог сообщение уходит, иначе отладка
    /// сводится к «меню не открылось, и никаких следов».
    /// </para>
    /// </remarks>
    private void RequestContextSafe(DesignEditorContextSource source, Point viewportPoint, KeyModifiers modifiers)
    {
        var previous = _contextRequest;
        var current = new CancellationTokenSource();
        _contextRequest = current;

        if (previous != null)
        {
            previous.Cancel();
            previous.Dispose();
        }

        _ = RequestContextAsync(source, viewportPoint, modifiers, current.Token).ContinueWith(
            task =>
            {
                if (ReferenceEquals(_contextRequest, current))
                    _contextRequest = null;

                current.Dispose();

                if (task.Exception is { } exception && !task.IsCanceled)
                {
                    Logger.TryGet(LogEventLevel.Error, LogArea.Control)?.Log(
                        this, "Провайдер контекстных действий завершился ошибкой: {Error}", exception.GetBaseException());
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    // Отмена запроса контекста, который ещё идёт. Null означает, что запроса нет.
    private CancellationTokenSource? _contextRequest;

    private void RetargetSelectionForContext(Point viewportPoint, KeyModifiers modifiers)
    {
        var worldPoint = GetWorldPosition(viewportPoint);
        if (!TryResolveContextTarget(worldPoint, out var hitTarget) || hitTarget == null)
            return;

        var target = hitTarget.Target;
        var container = hitTarget.Container;
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
