using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Слой связей редактора: по <see cref="Link"/> на элемент <see cref="NodeEditor.Links"/>.
/// </summary>
/// <remarks>
/// Своя тема у него обязательна: <see cref="ItemsControl"/> без шаблона не рисует ничего, а
/// библиотека не полагается на базовую тему приложения.
/// </remarks>
internal sealed class LinksPresenter : ItemsControl
{
    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(LinksPresenter);

    /// <inheritdoc />
    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
        => NeedsContainer<Link>(item, out recycleKey);

    /// <inheritdoc />
    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
        => new Link();

    /// <summary>
    /// Задаёт концы связи привязками редактора — тем же приёмом, что <c>DisplayMemberBinding</c>.
    /// </summary>
    /// <remarks>
    /// Готовую <see cref="Link"/> из коллекции не трогает: её концы задал тот, кто её создал.
    /// Не заданная привязка тоже ничего не ставит — концы тогда вправе задать стиль.
    /// </remarks>
    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);

        if (container is not Link link)
            return;

        // Выбор и запросы говорят с приложением его элементами, а не нашими контролами.
        link.Item = item;
        if (ReferenceEquals(link, item))
            return;

        if (this.FindAncestorOfType<NodeEditor>() is not { } editor)
            return;

        if (editor.LinkSourceBinding is { } source)
            link.Bind(Link.SourceProperty, source);

        if (editor.LinkTargetBinding is { } target)
            link.Bind(Link.TargetProperty, target);
    }

    /// <inheritdoc />
    protected override void ClearContainerForItemOverride(Control container)
    {
        base.ClearContainerForItemOverride(container);
        if (container is Link link)
            link.Item = null;
    }
}

/// <summary>
/// Панель связей: ставит каждую связь в прямоугольник её кривой на холсте.
/// </summary>
/// <remarks>
/// Прямоугольник у связи настоящий, а не весь холст: по нему считаются и области перерисовки, и
/// то, что уходит за край. Координаты панели — мировые, как у панели узлов.
/// </remarks>
internal sealed class LinkPanel : Panel
{
    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
        double right = 0, bottom = 0;

        foreach (var child in Children)
        {
            child.Measure(infinite);

            if (child is Link { IsResolved: true } link)
            {
                right = Math.Max(right, link.WorldBounds.Right);
                bottom = Math.Max(bottom, link.WorldBounds.Bottom);
            }
        }

        return new Size(right, bottom);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            if (child is Link { IsResolved: true } link)
                child.Arrange(link.WorldBounds);
            else
                child.Arrange(default);
        }

        return finalSize;
    }
}
