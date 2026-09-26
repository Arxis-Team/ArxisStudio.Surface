using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using ArxisStudio.Surface.Nodes.States;
using ArxisStudio.Surface.States;

namespace ArxisStudio.Surface.Nodes;

// Соединение: протяжка связи от порта, проверка приложением, запрос.
// Часть NodeEditor; общее описание типа — в NodeEditor.cs.
public partial class NodeEditor
{
    /// <summary>
    /// Идентификатор свойства радиуса захвата порта в пикселях экрана.
    /// </summary>
    public static readonly StyledProperty<double> PortCaptureRadiusProperty =
        AvaloniaProperty.Register<NodeEditor, double>(nameof(PortCaptureRadius), 12.0);

    /// <summary>
    /// Получает или задает, на сколько пикселей экрана вокруг порта протягиваемая связь уже
    /// считается пришедшей на него.
    /// </summary>
    /// <remarks>
    /// В пикселях экрана, а не холста: на отдалении порт мельче, и цель в мировых единицах была бы
    /// меньше, чем её видно.
    /// </remarks>
    public double PortCaptureRadius
    {
        get => GetValue(PortCaptureRadiusProperty);
        set => SetValue(PortCaptureRadiusProperty, value);
    }

    /// <summary>
    /// Возникает, когда протягиваемая связь пришла на новый порт: можно ли соединить.
    /// </summary>
    public event EventHandler<ConnectValidatingEventArgs>? ConnectValidating;

    /// <summary>
    /// Возникает, когда связь отпущена на порт, принявший её: соедините порты.
    /// </summary>
    /// <remarks>
    /// Редактор связь не создаёт (ADR 0001, 0004): добавить её в <see cref="Links"/> — дело
    /// приложения, как и положить правку в свою историю отмены.
    /// </remarks>
    public event EventHandler<ConnectRequestedEventArgs>? ConnectRequested;

    /// <summary>
    /// Превью протягиваемой связи из шаблона.
    /// </summary>
    internal PendingLinkPreview? PendingPreview { get; private set; }

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        PendingPreview = e.NameScope.Find<PendingLinkPreview>("PART_PendingLink");
    }

    /// <summary>
    /// Начинает протяжку связи от порта.
    /// </summary>
    /// <returns><see langword="true"/>, если жест начат и нажатие ему принадлежит.</returns>
    internal bool BeginPendingLink(Port origin, PointerPressedEventArgs e)
    {
        if (origin.Key is not { } key || CurrentState is not EditorIdleState)
            return false;

        var position = e.GetPosition(this);
        RecordPointerInput(position, e.KeyModifiers);

        // Фокус поверхность обычно берёт на своём нажатии, но это нажатие досталось порту.
        if (!IsKeyboardFocusWithin)
            Focus();

        PushState(new PendingLinkState(this, origin, key, e.Pointer, position));
        return true;
    }

    internal bool ValidateConnect(object source, object target)
    {
        var args = new ConnectValidatingEventArgs(source, target);
        ConnectValidating?.Invoke(this, args);
        return args.IsAllowed;
    }

    internal bool RequestConnect(object source, object target)
    {
        var handler = ConnectRequested;
        if (handler == null)
            return false;

        var args = new ConnectRequestedEventArgs(source, target);
        foreach (var invocation in handler.GetInvocationList())
        {
            ((EventHandler<ConnectRequestedEventArgs>)invocation)(this, args);
            if (args.Handled)
                break;
        }

        return args.Handled;
    }

    private static SurfaceKeyCommand CancelLinkCommand() => new(
        NodeEditorKeyCommands.CancelLink,
        static (view, e) => e.Key == Key.Escape && view.CurrentState is PendingLinkState,
        static (view, _) =>
        {
            view.PopState();
            return true;
        });
}
