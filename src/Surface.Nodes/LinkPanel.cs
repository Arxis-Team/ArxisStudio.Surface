using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Панель связей: ставит каждую развёрнутую связь в прямоугольник её кривой на холсте.
/// </summary>
/// <remarks>
/// Прямоугольник у связи настоящий, а не весь холст: по нему считаются и области перерисовки, и
/// то, что уходит за край. Координаты панели — мировые, как у панели узлов.
/// <para>
/// Какие связи развёрнуты, решает редактор (<see cref="NodeEditor"/>, ADR 0007): у каждой связи
/// есть запись, а контрол — только у видимой при виртуализации и у всех без неё. Панель зовёт его
/// на своей мере, а он ставит и снимает детей.
/// </para>
/// <para>
/// Пересчитанная связь переставляется одна: она просит об этом сама, а её желаемый размер от кривой
/// не зависит, поэтому пересчёт не поднимает перемер панели — то есть всех связей холста. Собственная
/// мера панели нужна при смене состава детей и видимой области.
/// </para>
/// </remarks>
internal sealed class LinkPanel : Panel
{
    // Пересчитанные с прошлой расстановки: только их расстановка и ставит заново.
    private readonly HashSet<Link> _moved = new();
    private bool _arrangeAll = true;

    /// <summary>
    /// Редактор, который решает, какие связи развёрнуты.
    /// </summary>
    internal NodeEditor? Owner { get; set; }

    /// <summary>
    /// Сколько раз панель меряла связи — для стенда стоимости.
    /// </summary>
    internal int MeasuredChildren { get; private set; }

    /// <summary>
    /// Сколько раз панель расставляла связи — для стенда стоимости.
    /// </summary>
    internal int ArrangedChildren { get; private set; }

    /// <summary>
    /// Отмечает связь, сменившую концы: в следующей расстановке она встанет в новый прямоугольник.
    /// </summary>
    internal void OnLinkMoved(Link link)
    {
        if (!_arrangeAll)
            _moved.Add(link);

        InvalidateArrange();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        Owner?.RealizeLinks(this);

        var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
        double right = 0, bottom = 0;

        foreach (var child in Children)
        {
            MeasuredChildren++;
            child.Measure(infinite);

            if (child is Link { IsResolved: true } link)
            {
                right = Math.Max(right, link.WorldBounds.Right);
                bottom = Math.Max(bottom, link.WorldBounds.Bottom);
            }
        }

        // Мера панели — при смене состава: расставить после неё надо всех.
        _arrangeAll = true;
        return new Size(right, bottom);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_arrangeAll)
        {
            foreach (var child in Children)
                ArrangeChild(child);

            _arrangeAll = false;
        }
        else
        {
            foreach (var link in _moved)
            {
                if (ReferenceEquals(link.GetVisualParent(), this))
                    ArrangeChild(link);
            }
        }

        _moved.Clear();
        return finalSize;
    }

    /// <summary>
    /// Снимает связь с панели вместе с её отметкой о сдвиге.
    /// </summary>
    internal void Remove(Link link)
    {
        _moved.Remove(link);
        Children.Remove(link);
    }

    private void ArrangeChild(Control child)
    {
        ArrangedChildren++;
        if (child is Link { IsResolved: true } link)
            child.Arrange(link.WorldBounds);
        else
            child.Arrange(default);
    }
}
