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
    /// Набор уже содержит встроенные команды — отмену, повтор, изменение размера и
    /// смещение стрелками, снятие выделения, запрос удаления и выбор всего (идентификаторы — константы
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
    /// <see cref="SurfaceDeleteRequestedEventArgs.Handled"/>.
    /// </remarks>
    public event EventHandler<SurfaceDeleteRequestedEventArgs>? DeleteRequested;

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
        // Изменение размера раньше смещения: Alt + стрелка — всё ещё стрелка, и смещение
        // иначе забрало бы её первым.
        commands.Add(new SurfaceKeyCommand(
            SurfaceKeyCommands.Resize,
            static (view, e) => IsArrow(e.Key) && view.IsKeyboardResize(e.KeyModifiers),
            static (view, e) => view.TryResizeSelection(e.Key, e.KeyModifiers)));
        commands.Add(new SurfaceKeyCommand(
            SurfaceKeyCommands.Nudge,
            static (_, e) => IsArrow(e.Key),
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

    private static bool IsArrow(Key key) => key is Key.Left or Key.Right or Key.Up or Key.Down;

    private bool IsKeyboardResize(KeyModifiers modifiers)
    {
        var required = InputGestures.KeyboardResizeModifiers;
        return required != KeyModifiers.None && modifiers.HasFlag(required);
    }

    private double KeyboardStep(KeyModifiers modifiers)
        => MatchesModifiers(modifiers, InputGestures.LargeNudgeModifiers)
           && InputGestures.LargeNudgeModifiers != KeyModifiers.None
            ? InteractionOptions.LargeNudgeStep
            : InteractionOptions.NudgeStep;

    /// <summary>
    /// Меняет размер выделения стрелкой: клавиатурная замена ручкам.
    /// </summary>
    /// <remarks>
    /// Двигается правый или нижний край — тот, что ручка тянула бы в ту же сторону;
    /// левый верхний угол стоит, поэтому положение не меняется. Правила те же, что у
    /// ручки, и по той же причине — один результат, каким бы способом его ни добивались:
    /// <list type="bullet">
    /// <item><description>сторона должна быть разрешена политикой изменения размера;</description></item>
    /// <item><description>содержимое не вырастает за свою форму, а уже вылезшее не ужимается задним числом;</description></item>
    /// <item><description>предел жеста не даёт схлопнуть элемент, но и не раздувает того, что уже мельче;</description></item>
    /// <item><description><c>Min</c>/<c>Max</c> контрола сильнее всего — их накладывает шов записи.</description></item>
    /// </list>
    /// Привязки нет, как и у смещения: клавиатура задаёт шаг точно. Каждый выбранный
    /// target меняется сам по себе, а всё нажатие — одна единица редактирования.
    /// </remarks>
    private bool TryResizeSelection(Key key, KeyModifiers modifiers)
    {
        var targets = SelectedTargets;
        if (targets.Count == 0)
            return false;

        var step = KeyboardStep(modifiers);
        var (dw, dh) = key switch
        {
            Key.Right => (step, 0d),
            Key.Left => (-step, 0d),
            Key.Down => (0d, step),
            Key.Up => (0d, -step),
            _ => (0d, 0d)
        };

        if (dw == 0 && dh == 0)
            return false;

        var direction = dw != 0 ? ResizeDirection.Right : ResizeDirection.Bottom;
        var editorMin = Math.Max(0.0, InteractionOptions.ResizeMinSize);

        BeginEdit(SurfaceEditKind.Resize);

        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i].Target;
            if (!IsResizeAllowed(target, direction))
                continue;

            var size = GetTargetSize(target);
            var width = size.Width + dw;
            var height = size.Height + dh;

            if ((dw > 0 || dh > 0) && TryGetContainmentBounds(target, out var limit))
            {
                var position = GetTargetPosition(target);
                if (dw > 0)
                    width = Math.Min(width, Math.Max(size.Width, limit.Right - position.X));
                if (dh > 0)
                    height = Math.Min(height, Math.Max(size.Height, limit.Bottom - position.Y));
            }

            width = Math.Max(width, Math.Max(Math.Min(editorMin, size.Width), target.MinWidth));
            height = Math.Max(height, Math.Max(Math.Min(editorMin, size.Height), target.MinHeight));

            SetTargetSize(target, new Size(width, height));
        }

        CommitEdit();
        RefreshSelectionOverlay();
        return true;
    }

    private bool TryNudgeSelection(Key key, KeyModifiers modifiers)
    {
        var targets = SelectedTargets;
        if (targets.Count == 0)
            return false;

        var step = KeyboardStep(modifiers);

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
        BeginEdit(SurfaceEditKind.Move);

        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i].Target;
            var filtered = ApplyMovePolicy(target, delta);
            if (filtered.X == 0 && filtered.Y == 0)
                continue;

            SetTargetPosition(target, GetTargetPosition(target) + filtered);
        }

        CommitEdit();
        RefreshSelectionOverlay();
        return true;
    }

    internal bool TryClearSelection()
    {
        // Выбор — индексный слой: выбранное бывает и без контейнеров (ADR 0010).
        if (SelectedTargets.Count == 0 && Selection.Count == 0)
            return false;

        using (WriteSelection())
        {
            Selection.Clear();
            _selectedTargets.Clear();
        }

        RefreshSelectionOverlay();
        return true;
    }

    private bool TrySelectAll()
    {
        if (ItemCount == 0)
            return false;

        // Выбирается всё, а разворачивается только видимое: у выбранного контейнер не обязателен
        // (ADR 0010).
        using (WriteSelection())
        {
            using (Selection.BatchUpdate())
            {
                Selection.Clear();
                Selection.SelectAll();
            }

            // Выбор всего работает на уровне контейнеров: это единица документа.
            _selectedTargets.Clear();
            foreach (var child in GetRealizedContainers())
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
        var targets = SelectedTargets;
        if (targets.Count == 0)
            return false;

        var handler = DeleteRequested;
        if (handler == null)
            return false;

        var args = new SurfaceDeleteRequestedEventArgs(targets);

        // Обработчики обходятся по одному, и первый же выполнивший удаление
        // останавливает обход. Список targets снят до правки, поэтому следующему
        // он описывал бы выделение, которого уже нет. Ровно это было исправлено
        // для ReorderRequested и не было исправлено здесь.
        foreach (var invocation in handler.GetInvocationList())
        {
            ((EventHandler<SurfaceDeleteRequestedEventArgs>)invocation)(this, args);

            if (args.Handled)
                break;
        }

        return args.Handled;
    }
}
