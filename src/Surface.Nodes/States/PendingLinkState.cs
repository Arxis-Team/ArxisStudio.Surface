using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using ArxisStudio.Surface.States;

namespace ArxisStudio.Surface.Nodes.States;

/// <summary>
/// Протяжка связи: свободный конец идёт за указателем, на отпускании — запрос.
/// </summary>
/// <remarks>
/// Жест один на два случая. Новая связь тянется от нажатого порта; отцепленный конец
/// существующей — от порта, на котором остался другой её конец (<see cref="LinkDetachment"/>).
/// Кандидаты, притяжение, проверка приложением и уборка у них общие, различается только итог.
/// <para>
/// Порты снимаются один раз, на входе в жест, — как соседи для направляющих: во время протяжки
/// узлы не двигаются, а искать порт под указателем по снимку не стоит обхода дерева на каждом
/// движении. Кандидатом становится ближайший порт, в чью рамку, расширенную на
/// <see cref="NodeEditor.PortCaptureRadius"/> пикселей экрана, попал указатель.
/// </para>
/// <para>
/// Все пути выхода — отпускание, Escape, потеря захвата — идут через <see cref="Exit"/>, и
/// уборка стоит там одна: превью, подсветка кандидата, отцепляемая связь, автопрокрутка, захват.
/// </para>
/// </remarks>
internal sealed class PendingLinkState : EditorState
{
    private readonly NodeEditor _editor;
    private readonly Port _origin;
    private readonly object _originKey;
    private readonly IPointer _pointer;
    private readonly Point _startScreen;
    private readonly LinkDetachment? _detachment;
    private readonly List<(Port Port, object Key, Point Anchor, Rect Bounds)> _ports = new();
    private Point _originAnchor;
    private Port? _candidate;
    private bool _candidateAllowed;

    /// <param name="editor">Редактор.</param>
    /// <param name="origin">Порт, от которого тянется связь: нажатый или тот, где остался другой конец.</param>
    /// <param name="originKey">Ключ этого порта.</param>
    /// <param name="pointer">Указатель жеста.</param>
    /// <param name="startScreen">Где сейчас свободный конец, в координатах редактора.</param>
    /// <param name="detachment">Отцепляемый конец существующей связи; без него тянется новая.</param>
    public PendingLinkState(
        NodeEditor editor, Port origin, object originKey, IPointer pointer, Point startScreen,
        LinkDetachment? detachment = null) : base(editor)
    {
        _editor = editor;
        _origin = origin;
        _originKey = originKey;
        _pointer = pointer;
        _startScreen = startScreen;
        _detachment = detachment;
    }

    /// <summary>
    /// Порт под свободным концом, если он есть.
    /// </summary>
    public Port? Candidate => _candidate;

    public override void Enter(EditorState? from)
    {
        _pointer.Capture(_editor);
        _detachment?.Link.SetDetaching(true);

        _origin.TryGetAnchor(out _originAnchor);
        foreach (var (key, port) in _editor.Ports.Snapshot())
        {
            if (port.TryGetAnchor(out var anchor) && port.TryGetWorldBounds(out var bounds))
                _ports.Add((port, key, anchor, bounds));
        }

        UpdateLooseEnd(_startScreen);
    }

    public override void Exit()
    {
        _editor.StopAutoPan();
        SetCandidate(null, allowed: false);
        _editor.PendingPreview?.Hide();
        _detachment?.Link.SetDetaching(false);

        if (ReferenceEquals(_pointer.Captured, _editor))
            _pointer.Capture(null);
    }

    public override void OnPointerMoved(PointerEventArgs e)
    {
        var screen = e.GetPosition(_editor);
        UpdateLooseEnd(screen);
        _editor.TrackAutoPan(screen, UpdateLooseEnd);
    }

    public override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var candidate = _candidate;
        var allowed = _candidateAllowed;

        _editor.PopState();

        var accepted = candidate != null && allowed ? candidate.Key : null;
        if (_detachment is { } detachment)
        {
            Release(detachment, candidate, accepted);
            return;
        }

        if (accepted != null)
        {
            var (source, target) = Normalise(accepted);
            _editor.RequestConnect(source, target);
        }
    }

    /// <summary>
    /// Итог отцепления: на другой порт — перецепить, в пустоту — удалить, на свой — ничего.
    /// </summary>
    /// <remarks>
    /// Порт, отказавший связи, пустотой не считается: человек целился в него, и удаление вместо
    /// отказа было бы наказанием за промах.
    /// </remarks>
    private void Release(LinkDetachment detachment, Port? candidate, object? accepted)
    {
        var item = detachment.Link.ItemOrSelf;

        if (candidate == null)
            _editor.RequestLinkDelete(new[] { item });
        else if (accepted != null && !Equals(accepted, detachment.Port))
            _editor.RequestReconnect(item, detachment.End, detachment.Port, accepted);
    }

    private void UpdateLooseEnd(Point screen)
    {
        var world = _editor.GetWorldPosition(screen);
        var candidate = FindCandidate(world, out var candidateAnchor);

        if (!ReferenceEquals(candidate, _candidate))
            SetCandidate(candidate, candidate != null && Validate(candidate));

        // Принятый кандидат притягивает свободный конец к своему штырьку: человек видит, куда
        // встанет связь, ещё до отпускания.
        var loose = _candidate != null && _candidateAllowed ? candidateAnchor : world;
        var (source, target) = _origin.Direction == PortDirection.Output
            ? (_originAnchor, loose)
            : (loose, _originAnchor);

        _editor.PendingPreview?.Show(source, target);
    }

    private Port? FindCandidate(Point world, out Point anchor)
    {
        anchor = default;
        var radius = _editor.PortCaptureRadius / Math.Max(_editor.ViewportZoom, 0.0001);
        Port? best = null;
        var bestDistance = double.MaxValue;

        foreach (var (port, _, portAnchor, bounds) in _ports)
        {
            if (ReferenceEquals(port, _origin) || !bounds.Inflate(radius).Contains(world))
                continue;

            var dx = portAnchor.X - world.X;
            var dy = portAnchor.Y - world.Y;
            var distance = (dx * dx) + (dy * dy);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = port;
                anchor = portAnchor;
            }
        }

        return best;
    }

    /// <summary>
    /// Встроенное правило — только направления; остальное спрашивается у приложения.
    /// </summary>
    /// <remarks>
    /// Свой прежний порт отцеплённый конец принимает без вопроса: вернуть конец на место — не
    /// правка, и правило приложения вроде «во вход — одна связь» не должно ему отказывать.
    /// </remarks>
    private bool Validate(Port candidate)
    {
        if (candidate.Direction == _origin.Direction || candidate.Key is not { } key)
            return false;

        if (_detachment != null && Equals(key, _detachment.Port))
            return true;

        var (source, target) = Normalise(key);
        return _editor.ValidateConnect(source, target);
    }

    /// <summary>
    /// Источник связи — всегда выход, цель — вход, в какую сторону ни тянули.
    /// </summary>
    private (object Source, object Target) Normalise(object candidateKey) =>
        _origin.Direction == PortDirection.Output ? (_originKey, candidateKey) : (candidateKey, _originKey);

    private void SetCandidate(Port? candidate, bool allowed)
    {
        if (_candidate != null)
            _candidate.SetAcceptance(null);

        _candidate = candidate;
        _candidateAllowed = allowed;

        candidate?.SetAcceptance(allowed);
    }
}
