using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ArxisStudio;
using Avalonia.Controls;
using DesignEditor.Demo.ViewModels;
using ArxisStudio.Surface;
using ArxisStudio.Surface.Editing;

namespace DesignEditor.Demo.Context;

public sealed class DesignEditorDemoContextActionsProvider : IDesignEditorContextActionProvider
{
    private sealed class InteractionPolicySnapshot
    {
        public ResizePolicy ResizePolicy { get; init; }
        public MovePolicy MovePolicy { get; init; }
    }

    private static readonly Dictionary<Control, InteractionPolicySnapshot> LockedTargets = new();

    public ValueTask<IReadOnlyList<DesignEditorContextAction>> GetActionsAsync(
        SurfaceView surface,
        DesignEditorContextRequest request,
        CancellationToken cancellationToken = default)
    {
        // Действия демо — действия дизайнера форм: над другой поверхностью им нечего делать.
        if (surface is not global::ArxisStudio.Surface.UiDesigner.DesignEditor editor)
            return ValueTask.FromResult<IReadOnlyList<DesignEditorContextAction>>(Array.Empty<DesignEditorContextAction>());

        var actions = request.Scope switch
        {
            DesignEditorContextScope.Surface => CreateSurfaceActions(editor),
            DesignEditorContextScope.Container => CreateContainerActions(editor, request),
            DesignEditorContextScope.NestedTarget => CreateNestedActions(editor, request),
            DesignEditorContextScope.Selection => CreateSelectionActions(editor),
            _ => Array.Empty<DesignEditorContextAction>()
        };

        return ValueTask.FromResult(actions);
    }

    private static IReadOnlyList<DesignEditorContextAction> CreateSurfaceActions(global::ArxisStudio.Surface.UiDesigner.DesignEditor editor)
    {
        return new[]
        {
            Action("surface.add-device", "Добавить экран устройства", () => AddElement(editor, new ThermostatScreenViewModel(500, 300))),
            Action("surface.add-dialog", "Добавить диалог", () => AddElement(editor, new AddDeviceDialogViewModel(900, 300))),
            Action("surface.add-automation", "Добавить сценарий", () => AddElement(editor, new AutomationScreenViewModel(900, 660))),
            Separator("surface.sep1"),
            Action("surface.reset-zoom", "Сбросить масштаб", () => editor.ViewportZoom = 1.0),
        };
    }

    private static IReadOnlyList<DesignEditorContextAction> CreateContainerActions(
        global::ArxisStudio.Surface.UiDesigner.DesignEditor editor,
        DesignEditorContextRequest request)
    {
        var hasTarget = request.Target != null;
        return new[]
        {
            Action(
                "container.center",
                "Центрировать элемент",
                () => CenterTarget(editor, request),
                isEnabled: hasTarget),
            Action(
                "container.fit",
                "Вписать элемент",
                () => FitTarget(editor, request),
                isEnabled: hasTarget),
            Separator("container.sep1"),
            Action("container.front", "На передний план", () => editor.BringToFront()),
            Action("container.forward", "Поднять на уровень", () => editor.BringForward()),
            Action("container.backward", "Опустить на уровень", () => editor.SendBackward()),
            Action("container.back", "На задний план", () => editor.SendToBack()),
            Separator("container.sep2"),
            Action(
                "container.delete",
                "Удалить элемент",
                () => DeleteTarget(editor, request),
                isEnabled: hasTarget)
        };
    }

    private static IReadOnlyList<DesignEditorContextAction> CreateNestedActions(
        global::ArxisStudio.Surface.UiDesigner.DesignEditor editor,
        DesignEditorContextRequest request)
    {
        var nestedTarget = request.Target?.Target;
        var hasTarget = nestedTarget != null;
        var isLocked = nestedTarget != null && IsTargetLocked(nestedTarget);

        return new[]
        {
            Action(
                "nested.center",
                "Центрировать родительский элемент",
                () => CenterTarget(editor, request),
                isEnabled: hasTarget),
            Action(
                "nested.fit",
                "Вписать родительский элемент",
                () => FitTarget(editor, request),
                isEnabled: hasTarget),
            Separator("nested.sep0"),
            Action("nested.front", "На передний план", () => editor.BringToFront()),
            Action("nested.back", "На задний план", () => editor.SendToBack()),
            Separator("nested.sep1"),
            Action(
                "nested.toggle-lock",
                isLocked ? "Разблокировать" : "Блокировать",
                () => ToggleNestedLock(request),
                isEnabled: hasTarget),
            Separator("nested.sep-group"),
            Action(
                "nested.ungroup",
                "Разгруппировать",
                () => editor.UngroupSelection(),
                editor.CanUngroupSelection()),
            Separator("nested.sep2"),
            Action(
                "nested.delete-owner",
                "Удалить родительский элемент",
                () => DeleteTarget(editor, request),
                isEnabled: hasTarget)
        };
    }

    private static IReadOnlyList<DesignEditorContextAction> CreateSelectionActions(global::ArxisStudio.Surface.UiDesigner.DesignEditor editor)
    {
        return new[]
        {
            Action("selection.center", "Центрировать выделение", editor.CenterOnSelection),
            Action("selection.fit", "Вписать выделение", editor.FitSelectionToView),
            Separator("selection.sep1"),

            // Группировка спрашивается у редактора, а не выводится из числа выбранных:
            // правила у неё свои — одна форма, только вложенные контролы, — и повторять
            // их в хосте значило бы разойтись с ними при первой же правке.
            Action(
                "selection.group",
                "Группировать",
                () => editor.GroupSelection(),
                editor.CanGroupSelection()),
            Action(
                "selection.ungroup",
                "Разгруппировать",
                () => editor.UngroupSelection(),
                editor.CanUngroupSelection()),
            Separator("selection.sep-group"),

            // Распределять есть что от трёх элементов: два уже задают отрезок.
            Action(
                "selection.distributeH",
                "Распределить по горизонтали",
                () => editor.DistributeHorizontally(),
                editor.SelectedDesignTargetsCount >= 3),
            Action(
                "selection.distributeV",
                "Распределить по вертикали",
                () => editor.DistributeVertically(),
                editor.SelectedDesignTargetsCount >= 3),
            Separator("selection.sep2"),
            Action("selection.clear", "Снять выделение", () => editor.Selection.Clear())
        };
    }

    private static void AddElement(global::ArxisStudio.Surface.UiDesigner.DesignEditor editor, DesignItemViewModel element)
    {
        if (editor.DataContext is not MainWindowViewModel viewModel)
            return;

        viewModel.Elements.Add(element);
    }

    private static void DeleteTarget(global::ArxisStudio.Surface.UiDesigner.DesignEditor editor, DesignEditorContextRequest request)
    {
        if (editor.DataContext is not MainWindowViewModel viewModel || request.Target == null)
            return;

        if (request.Target.Container.DataContext is DesignItemViewModel element)
            viewModel.Elements.Remove(element);
    }

    private static void CenterTarget(global::ArxisStudio.Surface.UiDesigner.DesignEditor editor, DesignEditorContextRequest request)
    {
        if (request.Target?.Container is { } container)
            editor.CenterOnItem(container);
    }

    private static void FitTarget(global::ArxisStudio.Surface.UiDesigner.DesignEditor editor, DesignEditorContextRequest request)
    {
        if (request.Target?.Container is { } container)
            editor.FitToView(container);
    }

    private static void ToggleNestedLock(DesignEditorContextRequest request)
    {
        if (request.Target?.Target is not Control target)
            return;

        if (IsTargetLocked(target))
        {
            UnlockTarget(target);
            return;
        }

        LockTarget(target);
    }

    private static bool IsTargetLocked(Control target)
    {
        return DesignInteraction.GetResizePolicy(target) == ResizePolicy.None &&
               DesignInteraction.GetMovePolicy(target) == MovePolicy.None;
    }

    private static void LockTarget(Control target)
    {
        if (!LockedTargets.ContainsKey(target))
        {
            LockedTargets[target] = new InteractionPolicySnapshot
            {
                ResizePolicy = DesignInteraction.GetResizePolicy(target),
                MovePolicy = DesignInteraction.GetMovePolicy(target)
            };
        }

        DesignInteraction.SetResizePolicy(target, ResizePolicy.None);
        DesignInteraction.SetMovePolicy(target, MovePolicy.None);
    }

    private static void UnlockTarget(Control target)
    {
        if (LockedTargets.TryGetValue(target, out var snapshot))
        {
            DesignInteraction.SetResizePolicy(target, snapshot.ResizePolicy);
            DesignInteraction.SetMovePolicy(target, snapshot.MovePolicy);
            LockedTargets.Remove(target);
            return;
        }

        DesignInteraction.SetResizePolicy(target, ResizePolicy.All);
        DesignInteraction.SetMovePolicy(target, MovePolicy.Both);
    }

    private static DesignEditorContextAction Action(
        string id,
        string header,
        Action execute,
        bool isEnabled = true)
    {
        return new DesignEditorContextAction
        {
            Id = id,
            Header = header,
            IsEnabled = isEnabled,
            Command = new DelegateCommand(execute, () => isEnabled)
        };
    }

    private static DesignEditorContextAction Separator(string id)
    {
        return new DesignEditorContextAction
        {
            Id = id,
            IsSeparator = true
        };
    }

    private sealed class DelegateCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public DelegateCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute();

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }
    }
}
