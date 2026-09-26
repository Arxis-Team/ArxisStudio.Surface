using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using ArxisStudio.Surface;

namespace ArxisStudio.Surface.Editing;

/// <summary>
/// Представляет модель геометрии adorner'а для selection overlay.
/// </summary>
internal class SelectionAdornerInfo
{
    /// <summary>
    /// Получает или задает контейнер, которому принадлежит visual target.
    /// </summary>
    public SurfaceItem? Container { get; set; }

    /// <summary>
    /// Получает или задает visual target, для которого строится adorner.
    /// </summary>
    public Control? Target { get; set; }

    /// <summary>
    /// Получает или задает состав кластера, если адорнер описывает группу.
    /// </summary>
    /// <remarks>
    /// У адорнера одиночного контрола пусто. По этому полю жест понимает, что тянут
    /// рамку группы: <see cref="Target"/> у неё — первый участник, чтобы слой
    /// переиспользовал адорнер по прежнему ключу, а масштабировать надо весь состав.
    /// </remarks>
    public IReadOnlyList<Control>? Members { get; set; }

    /// <summary>
    /// Получает или задает рамку адорнера в мировых координатах редактора.
    /// </summary>
    public Rect Bounds { get; set; }

    /// <summary>
    /// Получает или задает роль адорнера.
    /// </summary>
    public SelectionAdornerRole Role { get; set; } = SelectionAdornerRole.Secondary;

    /// <summary>
    /// Получает или задает признак отображения resize handles.
    /// </summary>
    public bool ShowHandles { get; set; }

    /// <summary>
    /// Получает или задает признак интерактивности adorner.
    /// </summary>
    public bool IsInteractive { get; set; }

    /// <summary>
    /// Получает или задает policy изменения размера target.
    /// </summary>
    public ResizePolicy ResizePolicy { get; set; } = ResizePolicy.All;

    /// <summary>
    /// Получает или задает policy перемещения target.
    /// </summary>
    public MovePolicy MovePolicy { get; set; } = MovePolicy.Both;
}
