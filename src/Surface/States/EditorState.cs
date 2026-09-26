using Avalonia.Input;

namespace ArxisStudio.Surface.States;

/// <summary>
/// Базовый класс состояния редактора.
/// </summary>
internal abstract class EditorState
{
    /// <summary>
    /// Получает редактор, которому принадлежит состояние.
    /// </summary>
    protected SurfaceView Editor { get; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="EditorState"/>.
    /// </summary>
    /// <param name="editor">Редактор, которому принадлежит состояние.</param>
    protected EditorState(SurfaceView editor)
    {
        Editor = editor;
    }

    /// <summary>
    /// Вызывается при входе в состояние.
    /// </summary>
    /// <param name="from">Предыдущее состояние или <see langword="null"/>.</param>
    public virtual void Enter(EditorState? from) { }

    /// <summary>
    /// Вызывается при выходе из состояния.
    /// </summary>
    public virtual void Exit() { }

    /// <summary>
    /// Обрабатывает нажатие указателя.
    /// </summary>
    /// <param name="e">Аргументы указателя.</param>
    public virtual void OnPointerPressed(PointerPressedEventArgs e) { }

    /// <summary>
    /// Обрабатывает перемещение указателя.
    /// </summary>
    /// <param name="e">Аргументы указателя.</param>
    public virtual void OnPointerMoved(PointerEventArgs e) { }

    /// <summary>
    /// Обрабатывает отпускание указателя.
    /// </summary>
    /// <param name="e">Аргументы указателя.</param>
    public virtual void OnPointerReleased(PointerReleasedEventArgs e) { }

    /// <summary>
    /// Обрабатывает вращение колесика мыши.
    /// </summary>
    /// <param name="e">Аргументы колесика мыши.</param>
    /// <returns><see langword="true"/>, если состояние потребило колесо.</returns>
    public virtual bool OnPointerWheelChanged(PointerWheelEventArgs e) => false;
}
