using Avalonia;
using Avalonia.Input;
using ArxisStudio.Surface;

namespace ArxisStudio.States;

/// <summary>
/// Состояние панорамирования viewport редактора.
/// </summary>
internal class EditorPanningState : EditorState
{
    private readonly IPointer _pointer;
    private readonly GestureCursorScope _cursor = new GestureCursorScope();
    private Point _startMousePosition;
    private Point _startViewportLocation;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="EditorPanningState"/>.
    /// </summary>
    /// <param name="editor">Редактор, которому принадлежит состояние.</param>
    /// <param name="pointer">Указатель, которым идёт жест.</param>
    public EditorPanningState(DesignEditor editor, IPointer pointer) : base(editor)
    {
        _pointer = pointer;
    }

    /// <inheritdoc />
    public override void Enter(EditorState? from)
    {
        // Захватываем начальные координаты
        // Используем GetPosition(Editor), так как это экранные координаты относительно контрола
        // (до применения трансформаций зума к содержимому)
        _startMousePosition = Editor.GetPositionForInput(Editor);
        _startViewportLocation = Editor.ViewportLocation;

        _cursor.Apply(Editor, Editor.Cursors.ResolvePan());

        // Захват берётся явно. Утверждение «Avalonia захватывает сама, поэтому
        // можно не захватывать» стояло здесь и было причиной дефекта: захват
        // достаётся элементу шаблона, а потерю захвата не видел никто — курсор
        // оставался Hand, и каждое следующее движение панорамировало.
        _pointer.Capture(Editor);
    }

    /// <inheritdoc />
    public override void Exit()
    {
        _cursor.Restore();

        if (ReferenceEquals(_pointer.Captured, Editor))
            _pointer.Capture(null);
    }

    /// <inheritdoc />
    public override void OnPointerMoved(PointerEventArgs e)
    {
        var currentMousePos = e.GetPosition(Editor);
        Vector diffScreen = _startMousePosition - currentMousePos;

        // Обновляем ViewportLocation.
        // Делим на Zoom, так как смещение мыши в экранных пикселях,
        // а ViewportLocation — в координатах холста.
        Editor.ViewportLocation = _startViewportLocation + (diffScreen / Editor.ViewportZoom);
    }

    /// <inheritdoc />
    public override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        // Возвращаемся в Idle при отпускании кнопки
        Editor.PopState();
    }

    /// <inheritdoc />
    public override bool OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        // Разрешаем зум даже во время панорамирования (как в Google Maps)
        return Editor.TryHandleZoom(e);
    }
}
