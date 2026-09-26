using System;
using Avalonia;
using Avalonia.Input;
using ArxisStudio.Surface;

namespace ArxisStudio.Surface.States;

/// <summary>
/// Состояние прямоугольного выделения элементов редактора.
/// </summary>
internal class EditorSelectingState : EditorState
{
    private readonly IPointer _pointer;
    private readonly GestureCursorScope _cursor = new GestureCursorScope();
    private Point _startLocationWorld;

    /// <summary>
    /// Режим рамки, зафиксированный в момент нажатия.
    /// </summary>
    /// <remarks>
    /// Решается один раз, по точке нажатия и модификатору: жест не должен менять смысл,
    /// если пользователь отпустит или нажмёт клавишу посреди протяжки. Владелец рамки,
    /// в отличие от режима, пересчитывается каждый кадр.
    /// </remarks>
    private bool _useContainerSelection;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="EditorSelectingState"/>.
    /// </summary>
    /// <param name="editor">Редактор, которому принадлежит состояние.</param>
    /// <param name="pointer">Указатель, которым идёт жест.</param>
    public EditorSelectingState(SurfaceView editor, IPointer pointer) : base(editor)
    {
        _pointer = pointer;
    }

    /// <inheritdoc />
    public override void Enter(EditorState? from)
    {
        // Захват принадлежит жесту. Avalonia на нажатии захватывает попавший под
        // указатель элемент сама, но это элемент шаблона, а не редактор: доставка
        // release зависела бы от формы шаблона. А главное — потерю захвата теперь
        // видит редактор и разбирает стек состояний сам, см. OnPointerCaptureLost.
        _pointer.Capture(Editor);

        // Снимок контейнеров на весь жест — по той же причине, что и соседи для
        // направляющих: рамка спрашивает их на каждом кадре, а меняться внутри
        // жеста они не должны, иначе охват обещал бы одно, а применилось другое.
        Editor.BeginContainerSnapshot();

        _cursor.Apply(Editor, Editor.Cursors.ResolveMarquee());

        // 1. Включаем режим отрисовки рамки в Editor
        Editor.IsSelecting = true;

        // 2. Запоминаем точку старта в МИРОВЫХ координатах (с учетом зума и пана)
        var pressPoint = Editor.GetPositionForInput(Editor);
        _startLocationWorld = Editor.GetWorldPosition(pressPoint);

        // Режим решает точка нажатия: снаружи форм рамка набирает формы, изнутри —
        // их содержимое. Модификатор по-прежнему требует форм откуда угодно.
        _useContainerSelection = Editor.ShouldUseContainerMarquee(pressPoint, Editor.LastInputModifiers);

        // 3. Сбрасываем текущее выделение, если не активен additive selection.
        var modifiers = Editor.LastInputModifiers;
        if (!Editor.ShouldUseAdditiveSelection(modifiers))
        {
            Editor.SelectedItem = null; // Сброс одиночного выделения
            Editor.Selection.Clear();   // Сброс множественного
        }

        Editor.SelectedArea = new Rect(_startLocationWorld, new Size(0, 0));
        Editor.UpdateMarqueeScope(Editor.SelectedArea, _useContainerSelection);
    }

    /// <inheritdoc />
    public override void Exit()
    {
        Editor.StopAutoPan();
        Editor.IsSelecting = false;
        Editor.SelectedArea = new Rect(0, 0, 0, 0);
        Editor.ClearMarqueeScope();

        // Симметрично Enter. Отпускать снимок надо и на потере захвата, а не только
        // на обычном отпускании: иначе он пережил бы жест и следующее чтение
        // контейнеров пошло бы по устаревшему списку. Exit проходит на обоих путях.
        Editor.EndContainerSnapshot();

        _cursor.Restore();

        // Если выход вызван самой потерей захвата, это no-op; на обычном отпускании
        // симметрично снимает то, что взял Enter.
        if (ReferenceEquals(_pointer.Captured, Editor))
            _pointer.Capture(null);
    }

    /// <inheritdoc />
    public override void OnPointerMoved(PointerEventArgs e)
    {
        var screenPoint = e.GetPosition(Editor);
        UpdateArea(screenPoint);

        // У края холст едет сам, а рамка растёт за указателем: начало её лежит в
        // мировых координатах и уезжает вместе с холстом, угол пересчитывается по той
        // же точке экрана.
        Editor.TrackAutoPan(screenPoint, UpdateArea);
    }

    private void UpdateArea(Point screenPoint)
    {
        Point currentMousePosWorld = Editor.GetWorldPosition(screenPoint);

        double x = Math.Min(_startLocationWorld.X, currentMousePosWorld.X);
        double y = Math.Min(_startLocationWorld.Y, currentMousePosWorld.Y);
        double w = Math.Abs(_startLocationWorld.X - currentMousePosWorld.X);
        double h = Math.Abs(_startLocationWorld.Y - currentMousePosWorld.Y);

        Editor.SelectedArea = new Rect(x, y, w, h);

        // Область действия следует за прямоугольником, а не за точкой нажатия:
        // по MarqueeScope можно показывать целевой контейнер прямо во время жеста.
        Editor.UpdateMarqueeScope(Editor.SelectedArea, _useContainerSelection);
    }

    /// <inheritdoc />
    public override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        // Применяем выделение. Режим берём зафиксированный, а не текущий.
        Editor.CommitSelection(
            Editor.SelectedArea,
            Editor.ShouldUseAdditiveSelection(e.KeyModifiers),
            _useContainerSelection);

        // Возвращаемся в Idle
        Editor.PopState();
    }
}
