using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio;

/// <summary>
/// Определяет область, для которой запрошено контекстное действие в <see cref="DesignEditor"/>.
/// </summary>
public enum DesignEditorContextScope
{
    /// <summary>
    /// Контекст вызван над пустым пространством поверхности редактора.
    /// </summary>
    Surface = 0,

    /// <summary>
    /// Контекст вызван над контейнером <see cref="DesignEditorItem"/>.
    /// </summary>
    Container = 1,

    /// <summary>
    /// Контекст вызван над вложенным (nested) target внутри контейнера.
    /// </summary>
    NestedTarget = 2,

    /// <summary>
    /// Контекст вызван над текущей группой выделения.
    /// </summary>
    Selection = 3
}

/// <summary>
/// Определяет источник запроса контекста в <see cref="DesignEditor"/>.
/// </summary>
public enum DesignEditorContextSource
{
    /// <summary>
    /// Запрос поступил от указателя (обычно RMB).
    /// </summary>
    Pointer = 0,

    /// <summary>
    /// Запрос поступил от клавиатуры.
    /// </summary>
    Keyboard = 1,

    /// <summary>
    /// Запрос инициирован программно.
    /// </summary>
    Programmatic = 2
}

/// <summary>
/// Представляет снимок контекста, на основании которого формируется меню действий.
/// </summary>
public sealed class DesignEditorContextRequest
{
    /// <summary>
    /// Получает или задает область, в которой вызван контекст.
    /// </summary>
    public DesignEditorContextScope Scope { get; set; }

    /// <summary>
    /// Получает или задает target под курсором в момент вызова.
    /// </summary>
    public DesignSelectionTarget? Target { get; set; }

    /// <summary>
    /// Получает или задает снимок текущего выделения.
    /// </summary>
    public IReadOnlyList<DesignSelectionTarget> Selection { get; set; } = Array.Empty<DesignSelectionTarget>();

    /// <summary>
    /// Получает или задает точку вызова в мировых координатах редактора.
    /// </summary>
    public Point WorldPoint { get; set; }

    /// <summary>
    /// Получает или задает точку вызова в координатах <see cref="DesignEditor"/>.
    /// </summary>
    public Point ViewportPoint { get; set; }

    /// <summary>
    /// Получает или задает точку вызова в экранных координатах.
    /// </summary>
    public PixelPoint ScreenPoint { get; set; }

    /// <summary>
    /// Получает или задает модификаторы ввода на момент вызова.
    /// </summary>
    public KeyModifiers Modifiers { get; set; }

    /// <summary>
    /// Получает или задает источник запроса контекста.
    /// </summary>
    public DesignEditorContextSource Source { get; set; }
}

/// <summary>
/// Описывает контекстное действие в UI-agnostic виде.
/// </summary>
public sealed class DesignEditorContextAction
{
    /// <summary>
    /// Получает или задает стабильный идентификатор действия.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Получает или задает заголовок действия.
    /// </summary>
    public string Header { get; set; } = string.Empty;

    /// <summary>
    /// Получает или задает представление иконки действия.
    /// </summary>
    public object? Icon { get; set; }

    /// <summary>
    /// Получает или задает логическую группу сортировки.
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// Получает или задает порядок внутри группы.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Получает или задает признак видимости действия.
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Получает или задает признак доступности действия.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Получает или задает признак пункта-разделителя.
    /// </summary>
    public bool IsSeparator { get; set; }

    /// <summary>
    /// Получает или задает команду выполнения действия.
    /// </summary>
    public System.Windows.Input.ICommand? Command { get; set; }

    /// <summary>
    /// Получает или задает параметр команды.
    /// </summary>
    public object? CommandParameter { get; set; }

    /// <summary>
    /// Получает или задает дочерние пункты подменю.
    /// </summary>
    public IReadOnlyList<DesignEditorContextAction> Items { get; set; } = Array.Empty<DesignEditorContextAction>();
}

/// <summary>
/// Определяет контракт провайдера действий контекстного меню <see cref="DesignEditor"/>.
/// </summary>
public interface IDesignEditorContextActionProvider
{
    /// <summary>
    /// Возвращает набор действий для указанного контекста.
    /// </summary>
    /// <param name="editor">Экземпляр редактора.</param>
    /// <param name="request">Снимок контекста вызова.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список действий контекстного меню.</returns>
    ValueTask<IReadOnlyList<DesignEditorContextAction>> GetActionsAsync(
        DesignEditor editor,
        DesignEditorContextRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Определяет контракт presenter-слоя для визуализации контекстных действий.
/// </summary>
public interface IDesignEditorContextPresenter
{
    /// <summary>
    /// Отображает контекстные действия для запроса.
    /// </summary>
    /// <param name="editor">Экземпляр редактора.</param>
    /// <param name="request">Снимок контекста вызова.</param>
    /// <param name="actions">Действия для отображения.</param>
    /// <returns><see langword="true"/>, если отображение обработано presenter'ом.</returns>
    bool TryShow(DesignEditor editor, DesignEditorContextRequest request, IReadOnlyList<DesignEditorContextAction> actions);
}

/// <summary>
/// Presenter по умолчанию, отображающий действия через Avalonia <see cref="ContextMenu"/>.
/// </summary>
public sealed class ContextMenuContextPresenter : IDesignEditorContextPresenter
{
    private ContextMenu? _activeContextMenu;

    /// <inheritdoc />
    public bool TryShow(DesignEditor editor, DesignEditorContextRequest request, IReadOnlyList<DesignEditorContextAction> actions)
    {
        if (actions == null || actions.Count == 0)
            return false;

        _activeContextMenu?.Close();
        var contextMenu = new ContextMenu
        {
            ItemsSource = CreateContextMenuItems(actions),
            // Меню открывается в точке вызова, а не по дефолтному placement редактора.
            // ViewportPoint уже в координатах DesignEditor, поэтому он же и есть якорь.
            PlacementTarget = editor,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = PopupAnchor.TopLeft,
            PlacementGravity = PopupGravity.BottomRight,
            PlacementRect = new Rect(request.ViewportPoint, new Size(1, 1))
        };

        _activeContextMenu = contextMenu;
        contextMenu.Open(editor);
        return true;
    }

    private static IReadOnlyList<object> CreateContextMenuItems(IReadOnlyList<DesignEditorContextAction> actions)
    {
        var result = new List<object>(actions.Count);
        foreach (var action in actions)
        {
            if (!action.IsVisible)
                continue;

            if (action.IsSeparator)
            {
                result.Add(new Separator());
                continue;
            }

            var item = new MenuItem
            {
                Header = action.Header,
                IsEnabled = action.IsEnabled,
                Command = action.Command,
                CommandParameter = action.CommandParameter,
                Icon = action.Icon
            };

            if (action.Items.Count > 0)
                item.ItemsSource = CreateContextMenuItems(action.Items);

            result.Add(item);
        }

        return result;
    }
}

/// <summary>
/// Event arguments for pre-show context request.
/// </summary>
public sealed class DesignEditorContextRequestingEventArgs : CancelEventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignEditorContextRequestingEventArgs"/>.
    /// </summary>
    /// <param name="request">Снимок запроса контекста.</param>
    public DesignEditorContextRequestingEventArgs(DesignEditorContextRequest request)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
    }

    /// <summary>
    /// Получает снимок запроса контекста.
    /// </summary>
    public DesignEditorContextRequest Request { get; }

    /// <summary>
    /// Actions resolved by providers; can be modified by host.
    /// </summary>
    public IReadOnlyList<DesignEditorContextAction> Actions { get; set; } = Array.Empty<DesignEditorContextAction>();

    /// <summary>
    /// True when host handles context presentation itself.
    /// </summary>
    public bool Handled { get; set; }
}

/// <summary>
/// Event arguments for post-resolution context request.
/// </summary>
public sealed class DesignEditorContextRequestedEventArgs : EventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignEditorContextRequestedEventArgs"/>.
    /// </summary>
    /// <param name="request">Снимок запроса контекста.</param>
    /// <param name="actions">Разрешенный набор действий.</param>
    /// <param name="handled">Признак, что показ контекста обработан.</param>
    public DesignEditorContextRequestedEventArgs(
        DesignEditorContextRequest request,
        IReadOnlyList<DesignEditorContextAction> actions,
        bool handled)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Actions = actions ?? throw new ArgumentNullException(nameof(actions));
        Handled = handled;
    }

    /// <summary>
    /// Получает снимок запроса контекста.
    /// </summary>
    public DesignEditorContextRequest Request { get; }

    /// <summary>
    /// Получает финальный набор контекстных действий.
    /// </summary>
    public IReadOnlyList<DesignEditorContextAction> Actions { get; }

    /// <summary>
    /// Получает признак, что показ контекста был обработан.
    /// </summary>
    public bool Handled { get; }
}
