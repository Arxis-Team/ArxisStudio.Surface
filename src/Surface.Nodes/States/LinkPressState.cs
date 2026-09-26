using System;
using Avalonia;
using Avalonia.Input;
using ArxisStudio.Surface.States;

namespace ArxisStudio.Surface.Nodes.States;

/// <summary>
/// Нажатие по связи до порога перетаскивания: отпускание оставляет только выбор, протяжка
/// отцепляет ближний конец.
/// </summary>
/// <remarks>
/// Ближний конец — тот, чью половину связи взяли: он читается по параметру кривой в точке
/// нажатия, то есть вдоль связи, а не по прямой до концов. Кривая симметрична относительно своей
/// середины, поэтому параметр 0,5 делит её пополам и по длине.
/// </remarks>
internal sealed class LinkPressState : EditorState
{
    private readonly NodeEditor _editor;
    private readonly Link _link;
    private readonly IPointer _pointer;
    private readonly Point _startScreen;
    private readonly Point _startWorld;
    private bool _handedOver;

    public LinkPressState(NodeEditor editor, Link link, IPointer pointer, Point startScreen) : base(editor)
    {
        _editor = editor;
        _link = link;
        _pointer = pointer;
        _startScreen = startScreen;
        _startWorld = editor.GetWorldPosition(startScreen);
    }

    public override void Enter(EditorState? from) => _pointer.Capture(_editor);

    public override void Exit()
    {
        // Протяжке захват передаётся как есть: снятый здесь, он поднял бы у редактора потерю
        // захвата посреди жеста, который продолжается.
        if (!_handedOver && ReferenceEquals(_pointer.Captured, _editor))
            _pointer.Capture(null);
    }

    public override void OnPointerMoved(PointerEventArgs e)
    {
        var screen = e.GetPosition(_editor);
        var threshold = Math.Max(0.0, _editor.InteractionOptions.DragStartThreshold);
        if (Vector.Distance(_startScreen, screen) <= threshold)
            return;

        var end = _link.Geometry.ParameterAt(_startWorld) < 0.5 ? LinkEnd.Source : LinkEnd.Target;
        var (fixedKey, detachedKey) = end == LinkEnd.Source ? (_link.Target, _link.Source) : (_link.Source, _link.Target);

        // Связь, которую за время нажатия убрали или у которой пропал порт, отцеплять не от чего:
        // её уже не видно, а приложению ушёл бы запрос о том, чего нет.
        if (!_link.IsOnSurface || !_link.IsResolved || fixedKey == null || detachedKey == null
            || _editor.Ports.Find(fixedKey) is not { } fixedPort)
        {
            _editor.PopState();
            return;
        }

        _handedOver = true;
        _editor.PopState();
        _editor.PushState(new PendingLinkState(
            _editor, fixedPort, fixedKey, _pointer, screen, new LinkDetachment(_link, end, detachedKey)));
    }

    public override void OnPointerReleased(PointerReleasedEventArgs e) => _editor.PopState();
}
