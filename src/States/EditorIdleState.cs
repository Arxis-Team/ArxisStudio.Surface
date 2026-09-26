using Avalonia;
using Avalonia.Input;
using Avalonia.VisualTree;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.States;

/// <summary>
/// Состояние ожидания, в котором редактор обрабатывает старт выделения, панорамирования и зума.
/// </summary>
internal class EditorIdleState : EditorState
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="EditorIdleState"/>.
    /// </summary>
    /// <param name="editor">Редактор, которому принадлежит состояние.</param>
    public EditorIdleState(DesignEditor editor) : base(editor) { }

    /// <inheritdoc />
    public override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(Editor).Properties;
        var modifiers = e.KeyModifiers;

        if (Editor.ShouldStartPan(props, modifiers))
        {
            Editor.PushState(new EditorPanningState(Editor, e.Pointer));
            return;
        }

        // Рамку начинаем, когда нажатие не забрал ни один контейнер: снаружи их
        // просто нет, а внутри контейнер сам уступает жест по пустой области.
        // Проверять принадлежность источника контейнеру больше нельзя — она
        // не отличает «внутри контейнера» от «контейнер удержал жест».
        if (!e.Handled && Editor.ShouldStartMarquee(props, modifiers))
        {
            Editor.PushState(new EditorSelectingState(Editor, e.Pointer));
            return;
        }
    }

    /// <inheritdoc />
    public override bool OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        // Зум работает всегда, даже в Idle
        return Editor.TryHandleZoom(e);
    }
}
