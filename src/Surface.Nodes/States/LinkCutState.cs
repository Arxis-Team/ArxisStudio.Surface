using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using ArxisStudio.Surface.States;

namespace ArxisStudio.Surface.Nodes.States;

/// <summary>
/// Разрез: отрезок от нажатия до указателя; связи, которые он перечёркивает, на отпускании
/// просят удалить (ADR 0005).
/// </summary>
/// <remarks>
/// Начало отрезка запоминается в мировых координатах: автопрокрутка у края двигает холст, и
/// начало обязано остаться там, где его поставили, а конец — под указателем. Перечёркнутые связи
/// показываются по ходу (<c>:cutting</c>). Все пути выхода — отпускание, Escape, потеря захвата —
/// идут через <see cref="Exit"/>, и уборка там одна.
/// </remarks>
internal sealed class LinkCutState : EditorState
{
    private readonly NodeEditor _editor;
    private readonly IPointer _pointer;
    private readonly Point _start;
    private readonly HashSet<Link> _crossing = new();
    private readonly HashSet<Link> _next = new();
    private Point _end;

    public LinkCutState(NodeEditor editor, IPointer pointer, Point startScreen) : base(editor)
    {
        _editor = editor;
        _pointer = pointer;
        _start = editor.GetWorldPosition(startScreen);
        _end = _start;
    }

    /// <summary>
    /// Связи, которые отрезок перечёркивает сейчас.
    /// </summary>
    public IReadOnlyCollection<Link> Crossing => _crossing;

    public override void Enter(EditorState? from)
    {
        _pointer.Capture(_editor);
        _editor.CutPreview?.Show(_start, _end, _editor.ViewportZoom);
    }

    public override void Exit()
    {
        _editor.StopAutoPan();

        foreach (var link in _crossing)
            link.SetCutting(false);

        _crossing.Clear();
        _editor.CutPreview?.Hide();

        if (ReferenceEquals(_pointer.Captured, _editor))
            _pointer.Capture(null);
    }

    public override void OnPointerMoved(PointerEventArgs e)
    {
        var screen = e.GetPosition(_editor);
        Update(screen);
        _editor.TrackAutoPan(screen, Update);
    }

    public override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var items = _editor.ItemsInCollectionOrder(_crossing);
        _editor.PopState();

        if (items.Count > 0)
            _editor.RequestLinkDelete(items);
    }

    private void Update(Point screen)
    {
        _end = _editor.GetWorldPosition(screen);
        _editor.CutPreview?.Show(_start, _end, _editor.ViewportZoom);
        _editor.CollectCrossing(_start, _end, _next);

        foreach (var link in _crossing)
        {
            if (!_next.Contains(link))
                link.SetCutting(false);
        }

        foreach (var link in _next)
        {
            if (!_crossing.Contains(link))
                link.SetCutting(true);
        }

        _crossing.Clear();
        _crossing.UnionWith(_next);
    }
}
