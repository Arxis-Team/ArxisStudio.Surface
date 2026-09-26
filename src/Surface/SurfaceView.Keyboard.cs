using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;

namespace ArxisStudio.Surface;

// Клавиатура: набор команд и их разбор.
// Часть SurfaceView; общее описание типа — в SurfaceView.cs.
public partial class SurfaceView
{
    /// <summary>
    /// Клавиатурные команды поверхности в порядке, в котором они слушают нажатие.
    /// </summary>
    /// <remarks>
    /// Набор уже содержит встроенные команды — отмену, повтор, смещение стрелками,
    /// снятие выделения, запрос удаления и выбор всего (идентификаторы — константы
    /// <see cref="SurfaceKeyCommands"/>). Приложение добавляет свои, заменяет встроенные
    /// по идентификатору или снимает их.
    /// </remarks>
    public SurfaceKeyCommands KeyCommands { get; } = CreateBuiltInKeyCommands();

    /// <summary>
    /// Возникает при запросе удаления выделения с клавиатуры.
    /// </summary>
    /// <remarks>
    /// Редактор не владеет коллекцией элементов и удалять их не может: обработчик
    /// должен выполнить удаление сам и выставить
    /// <see cref="DesignEditorDeleteRequestedEventArgs.Handled"/>.
    /// </remarks>
    public event EventHandler<DesignEditorDeleteRequestedEventArgs>? DeleteRequested;

    /// <summary>
    /// Передаёт нажатие клавиатурным командам.
    /// </summary>
    /// <param name="e">Аргументы клавиатуры.</param>
    /// <remarks>
    /// Уже обработанные нажатия пропускаются: если фокус во вложенном редактируемом
    /// контроле, стрелки и Delete принадлежат ему, а не редактору.
    /// </remarks>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
            return;

        foreach (var command in KeyCommands.Snapshot())
        {
            if (!command.Matches(this, e))
                continue;

            if (command.Execute(this, e))
            {
                e.Handled = true;
                return;
            }
        }
    }

    private static SurfaceKeyCommands CreateBuiltInKeyCommands()
    {
        // История идёт первой: сочетание задаётся целиком и настраивается, поэтому
        // конкретная клавиша заранее не известна и уступать её стрелкам или букве A нельзя.
        var commands = new SurfaceKeyCommands();
        commands.Add(new SurfaceKeyCommand(
            SurfaceKeyCommands.Undo,
            static (view, e) => MatchesAny(view.InputGestures.UndoGestures ?? view.HotkeyConfiguration?.Undo, e),
            static (view, _) => view.RequestUndo()));
        commands.Add(new SurfaceKeyCommand(
            SurfaceKeyCommands.Redo,
            static (view, e) => MatchesAny(view.InputGestures.RedoGestures ?? view.HotkeyConfiguration?.Redo, e),
            static (view, _) => view.RequestRedo()));
        commands.Add(new SurfaceKeyCommand(
            SurfaceKeyCommands.Nudge,
            static (_, e) => e.Key is Key.Left or Key.Right or Key.Up or Key.Down,
            static (view, e) => view.TryNudgeSelection(e.Key, e.KeyModifiers)));
        commands.Add(new SurfaceKeyCommand(
            SurfaceKeyCommands.ClearSelection,
            static (_, e) => e.Key == Key.Escape,
            static (view, _) => view.TryClearSelection()));
        commands.Add(new SurfaceKeyCommand(
            SurfaceKeyCommands.Delete,
            static (_, e) => e.Key is Key.Delete or Key.Back,
            static (view, _) => view.TryRequestDelete()));
        commands.Add(new SurfaceKeyCommand(
            SurfaceKeyCommands.SelectAll,
            static (view, e) => e.Key == Key.A && view.ShouldUseContainerInteraction(e.KeyModifiers),
            static (view, _) => view.TrySelectAll()));
        return commands;
    }

    /// <summary>
    /// Сочетания клавиш, принятые на этой платформе.
    /// </summary>
    /// <remarks>
    /// Отмена — не политика редактора, а соглашение системы: на macOS это Cmd, а не
    /// Ctrl, и повтор там пишется Cmd + Shift + Z. Спрашивать платформу дешевле, чем
    /// заводить свои свойства и потом объяснять, почему они не совпадают с остальным
    /// приложением.
    /// </remarks>
    private PlatformHotkeyConfiguration? HotkeyConfiguration => this.GetPlatformSettings()?.HotkeyConfiguration;

    private static bool MatchesAny(IReadOnlyList<KeyGesture>? gestures, KeyEventArgs e)
    {
        if (gestures == null)
            return false;

        for (var i = 0; i < gestures.Count; i++)
        {
            if (gestures[i].Matches(e))
                return true;
        }

        return false;
    }

    private bool TryNudgeSelection(Key key, KeyModifiers modifiers)
    {
        var targets = SelectedDesignTargets;
        if (targets.Count == 0)
            return false;

        var step = MatchesModifiers(modifiers, InputGestures.LargeNudgeModifiers)
            && InputGestures.LargeNudgeModifiers != KeyModifiers.None
                ? InteractionOptions.LargeNudgeStep
                : InteractionOptions.NudgeStep;

        var delta = key switch
        {
            Key.Left => new Vector(-step, 0),
            Key.Right => new Vector(step, 0),
            Key.Up => new Vector(0, -step),
            Key.Down => new Vector(0, step),
            _ => default
        };

        if (delta.X == 0 && delta.Y == 0)
            return false;

        // Одно нажатие — одна единица редактирования, как и одно перетаскивание.
        BeginEdit(DesignEditKind.Move);

        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i].Target;
            var filtered = ApplyMovePolicy(target, delta);
            if (filtered.X == 0 && filtered.Y == 0)
                continue;

            SetDesignPosition(target, GetDesignPosition(target) + filtered);
        }

        CommitEdit();
        RefreshSelectionOverlay();
        return true;
    }

    private bool TryClearSelection()
    {
        if (SelectedDesignTargets.Count == 0)
            return false;

        Selection.Clear();
        _selectedTargets.Clear();
        RefreshSelectionOverlay();
        return true;
    }

    private bool TrySelectAll()
    {
        if (ItemCount == 0)
            return false;

        using (Selection.BatchUpdate())
        {
            Selection.Clear();
            Selection.SelectAll();
        }

        // Выбор всего работает на уровне контейнеров: это единица документа.
        _selectedTargets.Clear();
        if (Presenter?.Panel != null)
        {
            foreach (var child in Presenter.Panel.Children)
            {
                if (child is SurfaceItem container)
                    AddSelectedTarget(container);
            }
        }

        RefreshSelectionOverlay();
        return true;
    }

    private bool TryRequestDelete()
    {
        var targets = SelectedDesignTargets;
        if (targets.Count == 0)
            return false;

        var handler = DeleteRequested;
        if (handler == null)
            return false;

        var args = new DesignEditorDeleteRequestedEventArgs(targets);

        // Обработчики обходятся по одному, и первый же выполнивший удаление
        // останавливает обход. Список targets снят до правки, поэтому следующему
        // он описывал бы выделение, которого уже нет. Ровно это было исправлено
        // для ReorderRequested и не было исправлено здесь.
        foreach (var invocation in handler.GetInvocationList())
        {
            ((EventHandler<DesignEditorDeleteRequestedEventArgs>)invocation)(this, args);

            if (args.Handled)
                break;
        }

        return args.Handled;
    }
}
