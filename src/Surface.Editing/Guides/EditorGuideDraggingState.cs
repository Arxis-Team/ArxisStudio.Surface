using Avalonia;
using Avalonia.Input;
using ArxisStudio.Surface.States;

namespace ArxisStudio.Surface.Editing;

/// <summary>
/// Состояние перемещения пользовательской направляющей.
/// </summary>
/// <remarks>
/// Устроено как перестановка среди соседей и по той же причине: набором направляющих
/// владеет хост, поэтому во время протяжки редактор ничего не меняет, а показывает
/// превью, и один раз просит на отпускании.
/// </remarks>
internal class EditorGuideDraggingState : EditorState
{
    private readonly UserGuideService _guides;
    private readonly IPointer _pointer;
    private readonly GestureCursorScope _cursor = new GestureCursorScope();
    private readonly DesignGuide _original;
    private DesignGuide _current;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="EditorGuideDraggingState"/>.
    /// </summary>
    /// <param name="editor">Редактор, которому принадлежит состояние.</param>
    /// <param name="guides">Служба направляющих, которая ведёт набор хоста.</param>
    /// <param name="pointer">Указатель, которым идёт жест.</param>
    /// <param name="guide">Перемещаемая направляющая.</param>
    public EditorGuideDraggingState(SurfaceView editor, UserGuideService guides, IPointer pointer, DesignGuide guide) : base(editor)
    {
        _guides = guides;
        _pointer = pointer;
        _original = guide;
        _current = guide;
    }

    /// <inheritdoc />
    public override void Enter(EditorState? from)
    {
        _pointer.Capture(Editor);

        _cursor.Apply(Editor, Editor.Cursors.ResolveGuide(_original.Orientation == DesignGuideOrientation.Vertical));

        _guides.SetPreview(_current);
    }

    /// <inheritdoc />
    public override void Exit()
    {
        _guides.SetPreview(null);
        _cursor.Restore();

        if (ReferenceEquals(_pointer.Captured, Editor))
            _pointer.Capture(null);
    }

    /// <inheritdoc />
    public override void OnPointerMoved(PointerEventArgs e)
    {
        var position = _guides.ResolvePosition(e.GetPosition(Editor), _original.Orientation, e.KeyModifiers);
        _current = new DesignGuide(_original.Orientation, position);
        _guides.SetPreview(_current);
    }

    /// <inheritdoc />
    public override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var point = e.GetPosition(Editor);

        // Уведённая за пределы редактора направляющая убирается. Это тот же жест,
        // которым её вытянули, только в обратную сторону, и другого способа
        // избавиться от линии указателем не нужно.
        if (!new Rect(Editor.Bounds.Size).Contains(point))
            _guides.RequestChange(DesignGuideChangeKind.Remove, _original, _original);
        else if (_current != _original)
            _guides.RequestChange(DesignGuideChangeKind.Move, _current, _original);

        Editor.PopState();
    }
}
