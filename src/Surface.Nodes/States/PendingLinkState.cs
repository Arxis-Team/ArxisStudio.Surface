using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using ArxisStudio.Surface.States;

namespace ArxisStudio.Surface.Nodes.States;

/// <summary>
/// Протяжка новой связи от порта: свободный конец идёт за указателем, на отпускании — запрос.
/// </summary>
/// <remarks>
/// Порты снимаются один раз, на входе в жест, — как соседи для направляющих: во время протяжки
/// узлы не двигаются, а искать порт под указателем по снимку не стоит обхода дерева на каждом
/// движении. Кандидатом становится ближайший порт, в чью рамку, расширенную на
/// <see cref="NodeEditor.PortCaptureRadius"/> пикселей экрана, попал указатель.
/// <para>
/// Все пути выхода — отпускание, Escape, потеря захвата — идут через <see cref="Exit"/>, и
/// уборка стоит там одна: превью, подсветка кандидата, автопрокрутка, захват.
/// </para>
/// </remarks>
internal sealed class PendingLinkState : EditorState
{
    private readonly NodeEditor _editor;
    private readonly Port _origin;
    private readonly object _originKey;
    private readonly IPointer _pointer;
    private readonly Point _startScreen;
    private readonly List<(Port Port, object Key, Point Anchor, Rect Bounds)> _ports = new();
    private Point _originAnchor;
    private Port? _candidate;
    private bool _candidateAllowed;

    public PendingLinkState(NodeEditor editor, Port origin, object originKey, IPointer pointer, Point startScreen) : base(editor)
    {
        _editor = editor;
        _origin = origin;
        _originKey = originKey;
        _pointer = pointer;
        _startScreen = startScreen;
    }

    /// <summary>
    /// Порт под свободным концом, если он есть.
    /// </summary>
    public Port? Candidate => _candidate;

    public override void Enter(EditorState? from)
    {
        _pointer.Capture(_editor);

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

        if (candidate != null && allowed && candidate.Key is { } candidateKey)
        {
            var (source, target) = Normalise(candidateKey);
            _editor.RequestConnect(source, target);
        }
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
    private bool Validate(Port candidate)
    {
        if (candidate.Direction == _origin.Direction || candidate.Key is not { } key)
            return false;

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
