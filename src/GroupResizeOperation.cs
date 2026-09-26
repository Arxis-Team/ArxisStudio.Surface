using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using ArxisStudio.Controls;
using ArxisStudio.Surface;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio;

internal sealed class GroupResizeOperation
    : IInteractionOperation
{
    private readonly ResizeDirection _direction;
    private readonly Rect _initialBounds;
    private readonly IReadOnlyList<GroupResizeTarget> _targets;
    private readonly double _minSize;

    // Текущая рамка группы. Дельты не накапливаются: Thumb отдаёт смещение
    // относительно самой ручки, а ручка стоит на применённой рамке, поэтому
    // всё, что съели привязка или границы формы, вернётся в следующей дельте.
    private Rect _currentBounds;

    /// <summary>
    /// Указатель, которым начали жест, и насколько он отстоял от двигающегося края.
    /// </summary>
    private readonly PointerSample? _grabSample;
    private readonly Vector _grabOffset;

    public GroupResizeOperation(ResizeDirection direction, Rect initialBounds, IReadOnlyList<GroupResizeTarget> targets, double minSize, PointerSample? pointer = null)
    {
        _direction = direction;
        _initialBounds = initialBounds;
        _targets = targets;
        _minSize = minSize;
        _currentBounds = initialBounds;
        _grabSample = pointer;

        if (pointer is { } sample)
            _grabOffset = sample.World - DesignEditor.MovingEdge(direction, initialBounds);
    }


    /// <summary>
    /// Любой из участников группы. Годится для снимка соседей: он и так
    /// исключает всё выделение целиком, а не один конкретный target.
    /// </summary>
    public Control? SourceTarget => _targets.Count > 0 ? _targets[0].Target : null;

    public void Update(DesignEditor editor, Vector worldDelta)
    {
        // Дельта считается от указателя по той же причине, что и у одиночного resize:
        // ручка отдаёт смещение относительно себя, и приращённым оно бывает лишь пока
        // между движениями проходит layout.
        if (DesignEditor.TryGetGesturePointer(editor.PointerSample, _grabSample, out var pointer))
            worldDelta = (pointer - _grabOffset) - DesignEditor.MovingEdge(_direction, _currentBounds);

        var nextBounds = CalculateResizedBounds(_currentBounds, _direction, worldDelta, _minSize);

        // Для группы привязывается рамка целиком: привязка каждого target'а
        // по отдельности разрушила бы пропорции внутри группы. Это касается
        // и направляющих — линия ловит рамку выделения, а не отдельный контрол.
        if (editor.CanSnapResizeEdge(editor.LastInputModifiers))
            nextBounds = SnapBounds(editor, nextBounds, _direction, _currentBounds, _minSize);

        // Ограничение по форме — тоже над рамкой целиком, по тем же причинам,
        // что и привязка: индивидуальный кламп разъехал бы пропорции группы.
        if (_targets.Count > 0 && editor.TryGetContainmentBounds(_targets[0].Target, out var limit))
            nextBounds = ContainBounds(nextBounds, _direction, _currentBounds, limit, _minSize);

        // Запоминаем применённое: следующая дельта придёт относительно ручки,
        // которая встанет именно на эту рамку.
        _currentBounds = nextBounds;

        // Линии — по применённой рамке: край мог упереться в границу формы.
        editor.PublishResizeGuides(nextBounds);

        // Масштаб и позиции считаются от ИСХОДНОЙ рамки, иначе округления
        // копились бы от кадра к кадру и группа расползалась.
        var scaleX = _initialBounds.Width > 0 ? nextBounds.Width / _initialBounds.Width : 1.0;
        var scaleY = _initialBounds.Height > 0 ? nextBounds.Height / _initialBounds.Height : 1.0;

        // Масштаб группы ограничивается самым «зажатым» target'ом.
        // Иначе его размер упирается в предел и перестаёт меняться,
        // а позиция продолжает считаться от неограниченного масштаба —
        // target'ы наезжают друг на друга и вылезают за рамку выделения.
        // Порядок как в Avalonia: сначала максимум, потом минимум, поэтому
        // при Max < Min побеждает минимум.
        scaleX = Math.Min(scaleX, GetMaximumScale(_targets, horizontal: true));
        scaleY = Math.Min(scaleY, GetMaximumScale(_targets, horizontal: false));
        scaleX = Math.Max(scaleX, GetMinimumScale(_targets, horizontal: true, _minSize));
        scaleY = Math.Max(scaleY, GetMinimumScale(_targets, horizontal: false, _minSize));

        for (var i = 0; i < _targets.Count; i++)
        {
            var target = _targets[i];
            var initialTargetBounds = target.InitialBounds;
            var newX = nextBounds.X + ((initialTargetBounds.X - _initialBounds.X) * scaleX);
            var newY = nextBounds.Y + ((initialTargetBounds.Y - _initialBounds.Y) * scaleY);
            var newWidth = initialTargetBounds.Width * scaleX;
            var newHeight = initialTargetBounds.Height * scaleY;

            editor.SetDesignSize(target.Target, new Size(newWidth, newHeight));
            editor.SetDesignPosition(target.Target, new Point(newX, newY));
        }
    }

    public void Complete(DesignEditor editor)
    {
    }

    /// <summary>
    /// Возвращает минимальный масштаб, при котором ни один target не опускается
    /// ниже своего минимального размера.
    /// </summary>
    private static double GetMinimumScale(IReadOnlyList<GroupResizeTarget> targets, bool horizontal, double minSize)
    {
        var minScale = 0.0;

        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            var initial = horizontal ? target.InitialBounds.Width : target.InitialBounds.Height;
            if (initial <= 0)
                continue;

            var limit = horizontal
                ? Math.Max(minSize, target.Target.MinWidth)
                : Math.Max(minSize, target.Target.MinHeight);

            minScale = Math.Max(minScale, limit / initial);
        }

        return minScale;
    }

    /// <summary>
    /// Возвращает максимальный масштаб, при котором ни один target не превышает
    /// свой предельный размер.
    /// </summary>
    private static double GetMaximumScale(IReadOnlyList<GroupResizeTarget> targets, bool horizontal)
    {
        var maxScale = double.PositiveInfinity;

        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            var initial = horizontal ? target.InitialBounds.Width : target.InitialBounds.Height;
            if (initial <= 0)
                continue;

            var limit = horizontal ? target.Target.MaxWidth : target.Target.MaxHeight;
            if (double.IsInfinity(limit) || double.IsNaN(limit))
                continue;

            maxScale = Math.Min(maxScale, limit / initial);
        }

        return maxScale;
    }

    /// <summary>
    /// Удерживает те края рамки группы, которые тянет пользователь, внутри формы.
    /// </summary>
    private static Rect ContainBounds(Rect bounds, ResizeDirection direction, Rect initialBounds, Rect limit, double minSize)
    {
        var left = bounds.X;
        var top = bounds.Y;
        var right = bounds.Right;
        var bottom = bounds.Bottom;

        if (direction is ResizeDirection.Right or ResizeDirection.TopRight or ResizeDirection.BottomRight)
            right = Math.Min(right, limit.Right);
        else if (direction is ResizeDirection.Left or ResizeDirection.TopLeft or ResizeDirection.BottomLeft)
            left = Math.Max(left, limit.Left);

        if (direction is ResizeDirection.Bottom or ResizeDirection.BottomLeft or ResizeDirection.BottomRight)
            bottom = Math.Min(bottom, limit.Bottom);
        else if (direction is ResizeDirection.Top or ResizeDirection.TopLeft or ResizeDirection.TopRight)
            top = Math.Max(top, limit.Top);

        var width = Math.Max(minSize, right - left);
        var height = Math.Max(minSize, bottom - top);

        if (direction is ResizeDirection.Left or ResizeDirection.TopLeft or ResizeDirection.BottomLeft)
            left = initialBounds.Right - width;

        if (direction is ResizeDirection.Top or ResizeDirection.TopLeft or ResizeDirection.TopRight)
            top = initialBounds.Bottom - height;

        return new Rect(left, top, width, height);
    }

    /// <summary>
    /// Приводит к направляющим и сетке те края рамки группы, которые тянет пользователь.
    /// </summary>
    private static Rect SnapBounds(DesignEditor editor, Rect bounds, ResizeDirection direction, Rect initialBounds, double minSize)
    {
        var modifiers = editor.LastInputModifiers;
        var left = bounds.X;
        var top = bounds.Y;
        var right = bounds.Right;
        var bottom = bounds.Bottom;

        if (direction is ResizeDirection.Right or ResizeDirection.TopRight or ResizeDirection.BottomRight)
            right = editor.ResolveResizeEdge(right, bounds, xAxis: true, farEdge: true, modifiers);
        else if (direction is ResizeDirection.Left or ResizeDirection.TopLeft or ResizeDirection.BottomLeft)
            left = editor.ResolveResizeEdge(left, bounds, xAxis: true, farEdge: false, modifiers);

        if (direction is ResizeDirection.Bottom or ResizeDirection.BottomLeft or ResizeDirection.BottomRight)
            bottom = editor.ResolveResizeEdge(bottom, bounds, xAxis: false, farEdge: true, modifiers);
        else if (direction is ResizeDirection.Top or ResizeDirection.TopLeft or ResizeDirection.TopRight)
            top = editor.ResolveResizeEdge(top, bounds, xAxis: false, farEdge: false, modifiers);

        var width = Math.Max(minSize, right - left);
        var height = Math.Max(minSize, bottom - top);

        // Неподвижный край остаётся на месте даже после клампа по минимуму.
        if (direction is ResizeDirection.Left or ResizeDirection.TopLeft or ResizeDirection.BottomLeft)
            left = initialBounds.Right - width;

        if (direction is ResizeDirection.Top or ResizeDirection.TopLeft or ResizeDirection.TopRight)
            top = initialBounds.Bottom - height;

        return new Rect(left, top, width, height);
    }

    private static Rect CalculateResizedBounds(Rect initialBounds, ResizeDirection direction, Vector delta, double minSize)
    {
        var newX = initialBounds.X;
        var newY = initialBounds.Y;
        var newWidth = initialBounds.Width;
        var newHeight = initialBounds.Height;

        switch (direction)
        {
            case ResizeDirection.Right:
                newWidth += delta.X;
                break;
            case ResizeDirection.Bottom:
                newHeight += delta.Y;
                break;
            case ResizeDirection.Left:
                newWidth -= delta.X;
                newX += delta.X;
                break;
            case ResizeDirection.Top:
                newHeight -= delta.Y;
                newY += delta.Y;
                break;
            case ResizeDirection.BottomRight:
                newWidth += delta.X;
                newHeight += delta.Y;
                break;
            case ResizeDirection.BottomLeft:
                newWidth -= delta.X;
                newX += delta.X;
                newHeight += delta.Y;
                break;
            case ResizeDirection.TopRight:
                newWidth += delta.X;
                newHeight -= delta.Y;
                newY += delta.Y;
                break;
            case ResizeDirection.TopLeft:
                newWidth -= delta.X;
                newX += delta.X;
                newHeight -= delta.Y;
                newY += delta.Y;
                break;
        }

        var initialRight = initialBounds.Right;
        var initialBottom = initialBounds.Bottom;

        newWidth = Math.Max(minSize, newWidth);
        newHeight = Math.Max(minSize, newHeight);

        if (direction is ResizeDirection.Left or ResizeDirection.TopLeft or ResizeDirection.BottomLeft)
            newX = initialRight - newWidth;

        if (direction is ResizeDirection.Top or ResizeDirection.TopLeft or ResizeDirection.TopRight)
            newY = initialBottom - newHeight;

        return new Rect(newX, newY, newWidth, newHeight);
    }
}

internal readonly struct GroupResizeTarget
{
    public GroupResizeTarget(Control target, Rect initialBounds)
    {
        Target = target;
        InitialBounds = initialBounds;
    }

    public Control Target { get; }
    public Rect InitialBounds { get; }
}
