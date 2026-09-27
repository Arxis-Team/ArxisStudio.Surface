using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;

namespace ArxisStudio.Surface;

/// <summary>
/// Определяет область, для которой запрошено контекстное действие в <see cref="SurfaceView"/>.
/// </summary>
public enum SurfaceContextScope
{
    /// <summary>
    /// Контекст вызван над пустым пространством поверхности редактора.
    /// </summary>
    Surface = 0,

    /// <summary>
    /// Контекст вызван над контейнером <see cref="SurfaceItem"/>.
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
/// Определяет источник запроса контекста в <see cref="SurfaceView"/>.
/// </summary>
public enum SurfaceContextSource
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
public sealed class SurfaceContextRequest
{
    /// <summary>
    /// Получает или задает область, в которой вызван контекст.
    /// </summary>
    public SurfaceContextScope Scope { get; set; }

    /// <summary>
    /// Получает или задает target под курсором в момент вызова.
    /// </summary>
    public SurfaceSelectionTarget? Target { get; set; }

    /// <summary>
    /// Получает или задает снимок текущего выделения.
    /// </summary>
    public IReadOnlyList<SurfaceSelectionTarget> Selection { get; set; } = Array.Empty<SurfaceSelectionTarget>();

    /// <summary>
    /// Получает или задает точку вызова в мировых координатах редактора.
    /// </summary>
    public Point WorldPoint { get; set; }

    /// <summary>
    /// Получает или задает точку вызова в координатах <see cref="SurfaceView"/>.
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
    public SurfaceContextSource Source { get; set; }
}

/// <summary>
/// Описывает контекстное действие в UI-agnostic виде.
/// </summary>
public sealed class SurfaceContextAction
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
    public IReadOnlyList<SurfaceContextAction> Items { get; set; } = Array.Empty<SurfaceContextAction>();
}

/// <summary>
/// Определяет контракт провайдера действий контекстного меню <see cref="SurfaceView"/>.
/// </summary>
public interface ISurfaceContextActionProvider
{
    /// <summary>
    /// Возвращает набор действий для указанного контекста.
    /// </summary>
    /// <param name="editor">Экземпляр редактора.</param>
    /// <param name="request">Снимок контекста вызова.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список действий контекстного меню.</returns>
    ValueTask<IReadOnlyList<SurfaceContextAction>> GetActionsAsync(
        SurfaceView editor,
        SurfaceContextRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Определяет контракт presenter-слоя для визуализации контекстных действий.
/// </summary>
public interface ISurfaceContextPresenter
{
    /// <summary>
    /// Отображает контекстные действия для запроса.
    /// </summary>
    /// <param name="editor">Экземпляр редактора.</param>
    /// <param name="request">Снимок контекста вызова.</param>
    /// <param name="actions">Действия для отображения.</param>
    /// <returns><see langword="true"/>, если отображение обработано presenter'ом.</returns>
    bool TryShow(SurfaceView editor, SurfaceContextRequest request, IReadOnlyList<SurfaceContextAction> actions);
}

/// <summary>
/// Presenter по умолчанию, отображающий действия через Avalonia <see cref="ContextMenu"/>.
/// </summary>
public sealed class ContextMenuContextPresenter : ISurfaceContextPresenter
{
    private ContextMenu? _activeContextMenu;

    /// <inheritdoc />
    public bool TryShow(SurfaceView editor, SurfaceContextRequest request, IReadOnlyList<SurfaceContextAction> actions)
    {
        if (actions == null || actions.Count == 0)
            return false;

        _activeContextMenu?.Close();
        var contextMenu = new ContextMenu
        {
            ItemsSource = CreateContextMenuItems(actions),
            // Меню открывается в точке вызова, а не по дефолтному placement редактора.
            // ViewportPoint уже в координатах поверхности, поэтому он же и есть якорь.
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

    private static IReadOnlyList<object> CreateContextMenuItems(IReadOnlyList<SurfaceContextAction> actions)
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
public sealed class SurfaceContextRequestingEventArgs : CancelEventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SurfaceContextRequestingEventArgs"/>.
    /// </summary>
    /// <param name="request">Снимок запроса контекста.</param>
    public SurfaceContextRequestingEventArgs(SurfaceContextRequest request)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
    }

    /// <summary>
    /// Получает снимок запроса контекста.
    /// </summary>
    public SurfaceContextRequest Request { get; }

    /// <summary>
    /// Actions resolved by providers; can be modified by host.
    /// </summary>
    public IReadOnlyList<SurfaceContextAction> Actions { get; set; } = Array.Empty<SurfaceContextAction>();

    /// <summary>
    /// True when host handles context presentation itself.
    /// </summary>
    public bool Handled { get; set; }
}

/// <summary>
/// Event arguments for post-resolution context request.
/// </summary>
public sealed class SurfaceContextRequestedEventArgs : EventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SurfaceContextRequestedEventArgs"/>.
    /// </summary>
    /// <param name="request">Снимок запроса контекста.</param>
    /// <param name="actions">Разрешенный набор действий.</param>
    /// <param name="handled">Признак, что показ контекста обработан.</param>
    public SurfaceContextRequestedEventArgs(
        SurfaceContextRequest request,
        IReadOnlyList<SurfaceContextAction> actions,
        bool handled)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Actions = actions ?? throw new ArgumentNullException(nameof(actions));
        Handled = handled;
    }

    /// <summary>
    /// Получает снимок запроса контекста.
    /// </summary>
    public SurfaceContextRequest Request { get; }

    /// <summary>
    /// Получает финальный набор контекстных действий.
    /// </summary>
    public IReadOnlyList<SurfaceContextAction> Actions { get; }

    /// <summary>
    /// Получает признак, что показ контекста был обработан.
    /// </summary>
    public bool Handled { get; }
}
