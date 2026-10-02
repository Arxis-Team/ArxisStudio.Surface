using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ArxisStudio.Surface;
using ArxisStudio.Surface.States;
using ArxisStudio.Surface.UiDesigner.Placement;

namespace ArxisStudio.Surface.UiDesigner.States;

/// <summary>
/// Состояние перестановки контрола среди соседей по родительской панели.
/// </summary>
/// <remarks>
/// Работает там, где позицией распоряжается раскладка: в такой панели
/// перетаскивание не может задать координату, но может изменить порядок.
/// <para>
/// Перестановка применяется на отпускании, а не покадрово: если двигать контрол
/// прямо во время жеста, панель переливается под курсором и элемент прыгает.
/// Пока идёт протяжка, показывается только индикатор точки вставки.
/// </para>
/// </remarks>
internal sealed class ItemReorderingState : SurfaceItemState
{
    private readonly Point _initialPointerPosition;
    private readonly GestureCursorScope _cursor = new GestureCursorScope();
    private Control? _target;
    private Panel? _panel;
    private int _initialIndex = -1;
    private int _insertBefore = -1;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ItemReorderingState"/>.
    /// </summary>
    /// <param name="container">Контейнер, внутри которого идёт перестановка.</param>
    /// <param name="initialPointerPosition">Начальная позиция указателя в координатах редактора.</param>
    public ItemReorderingState(UiDesignerItem container, Point initialPointerPosition)
        : base(container)
    {
        _initialPointerPosition = initialPointerPosition;
    }

    /// <inheritdoc />
    public override void Enter(SurfaceItemState from)
    {
        var editor = Container.FindAncestorOfType<UiDesignerView>();
        if (editor == null)
            return;

        _target = editor.ResolveInteractionTarget(Container);
        _panel = _target.GetVisualParent() as Panel;
        _initialIndex = _panel?.Children.IndexOf(_target) ?? -1;
        _insertBefore = _initialIndex;

        UpdateIndicator(editor, _initialPointerPosition);

        // Курсор ставится контейнеру: захват взял он, ещё в ItemIdleState.
        _cursor.Apply(Container, editor.Cursors.ResolveReorder());
    }

    /// <inheritdoc />
    public override void OnPointerMoved(PointerEventArgs e)
    {
        var editor = Container.FindAncestorOfType<UiDesignerView>();
        if (editor == null)
            return;

        UpdateIndicator(editor, e.GetPosition(editor));
        e.Handled = true;
    }

    /// <inheritdoc />
    public override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var editor = Container.FindAncestorOfType<UiDesignerView>();

        var handled = false;

        if (editor != null && _target != null && _initialIndex >= 0)
        {
            // Точка вставки уходит как есть: перевод в индекс переноса делает
            // редактор, там же, где читает текущую позицию. Обе половины запроса
            // обязаны быть сняты за одно чтение.
            handled = editor.CommitReorder(_target, _insertBefore);
        }
        else
        {
            editor?.CancelReorder();
        }

        Container.PopState();
        e.Pointer.Capture(null);

        // Отказ обработчика оставляет нажатие необработанным — как и у Delete.
        e.Handled = handled;
    }

    /// <inheritdoc />
    public override void Exit()
    {
        Container.FindAncestorOfType<UiDesignerView>()?.CancelReorder();
        _cursor.Restore();
    }

    /// <summary>
    /// Ставит точку вставки и её линию по положению указателя.
    /// </summary>
    /// <remarks>
    /// Правило общее с броском (<see cref="FlowInsertion"/>): элемент, переставленный мышью, обязан
    /// вставать туда же, куда встал бы такой же, принесённый тягой.
    /// </remarks>
    private void UpdateIndicator(UiDesignerView editor, Point pointerInEditor)
    {
        if (_panel == null || _panel.Children.Count == 0)
            return;

        var world = editor.GetWorldPosition(pointerInEditor);
        if (!FlowInsertion.TryResolve(editor, _panel, world, out var insertBefore, out var indicator))
            return;

        _insertBefore = insertBefore;

        if (indicator is { } line)
            editor.UpdateReorderIndicator(line);
    }
}
