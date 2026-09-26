using System;
using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Surface.Editing;

/// <summary>
/// Attached API политики редактирования для designer targets.
/// </summary>
public static class DesignInteraction
{
    /// <summary>
    /// Идентификатор attached-свойства политики resize.
    /// </summary>
    public static readonly AttachedProperty<ResizePolicy> ResizePolicyProperty =
        AvaloniaProperty.RegisterAttached<Control, ResizePolicy>(
            "ResizePolicy",
            typeof(DesignInteraction),
            ResizePolicy.All,
            inherits: false);

    /// <summary>
    /// Идентификатор attached-свойства политики перемещения.
    /// </summary>
    public static readonly AttachedProperty<MovePolicy> MovePolicyProperty =
        AvaloniaProperty.RegisterAttached<Control, MovePolicy>(
            "MovePolicy",
            typeof(DesignInteraction),
            MovePolicy.Both,
            inherits: false);

    /// <summary>
    /// Возвращает policy изменения размера для target.
    /// </summary>
    public static ResizePolicy GetResizePolicy(AvaloniaObject target) => target.GetValue(ResizePolicyProperty);

    /// <summary>
    /// Задает policy изменения размера для target.
    /// </summary>
    public static void SetResizePolicy(AvaloniaObject target, ResizePolicy value) => target.SetValue(ResizePolicyProperty, value);

    /// <summary>
    /// Возвращает policy перемещения для target.
    /// </summary>
    public static MovePolicy GetMovePolicy(AvaloniaObject target) => target.GetValue(MovePolicyProperty);

    /// <summary>
    /// Задает policy перемещения для target.
    /// </summary>
    public static void SetMovePolicy(AvaloniaObject target, MovePolicy value) => target.SetValue(MovePolicyProperty, value);
}
