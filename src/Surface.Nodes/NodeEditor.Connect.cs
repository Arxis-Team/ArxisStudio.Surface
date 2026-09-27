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
    /// Возникает, когда конец существующей связи отцепили и отпустили на другой порт того же
    /// направления: перецепите его.
    /// </summary>
    /// <remarks>
    /// Отцепляют протяжкой тела связи, и уходит ближний к нажатию конец. Брошенный в пустоту
    /// конец просит удалить связь (<see cref="LinkDeleteRequested"/>), возвращённый на свой порт
    /// не просит ничего. Порт-кандидат проверяется тем же <see cref="ConnectValidating"/>, что и
    /// новая связь, — с той парой портов, которая получится.
    /// </remarks>
    public event EventHandler<ReconnectRequestedEventArgs>? ReconnectRequested;

    /// <summary>
    /// Превью протягиваемой связи из шаблона.
    /// </summary>
    internal PendingLinkPreview? PendingPreview { get; private set; }

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        PendingPreview = e.NameScope.Find<PendingLinkPreview>("PART_PendingLink");
        CutPreview = e.NameScope.Find<LinkCutPreview>("PART_CutLine");
        AttachLinkPanel(e.NameScope.Find<LinkPanel>("PART_LinkPanel"));
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

    internal bool ValidateConnect(object source, object target, object? link = null)
    {
        var args = new ConnectValidatingEventArgs(source, target, link);
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

    internal bool RequestReconnect(object link, LinkEnd end, object oldPort, object newPort)
    {
        var handler = ReconnectRequested;
        if (handler == null)
            return false;

        var args = new ReconnectRequestedEventArgs(link, end, oldPort, newPort);
        foreach (var invocation in handler.GetInvocationList())
        {
            ((EventHandler<ReconnectRequestedEventArgs>)invocation)(this, args);
            if (args.Handled)
                break;
        }

        return args.Handled;
    }

    // Отменяет протяжку, нажатие по связи до порога и разрез: иначе Escape снял бы выбор, а
    // следующее движение всё равно отцепило бы конец или разрезало связи.
    private static SurfaceKeyCommand CancelLinkCommand() => new(
        NodeEditorKeyCommands.CancelLink,
        static (view, e) => e.Key == Key.Escape && view.CurrentState is PendingLinkState or LinkPressState or LinkCutState,
        static (view, _) =>
        {
            view.PopState();
            return true;
        });
}
