using System;
using Avalonia;
using Avalonia.Controls;
using ArxisStudio.Surface.Editing;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Редактор узлов: поверхность, на которой лежат узлы графа и связи между их портами.
/// </summary>
/// <remarks>
/// Слой рядом с дизайнером форм, а не над ним (ADR 0004). Холст, выделение, рамка, перетаскивание
/// узлов с автопрокруткой, клавиатура и контракт изменений — ядра, без правок; из инструментов
/// взята привязка к сетке и выравнивание по соседям. Узлы — унаследованный
/// <see cref="ItemsControl.ItemsSource"/>, каждый в своём контейнере <see cref="Node"/>.
/// </remarks>
public class NodeEditor : SurfaceView
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="NodeEditor"/>.
    /// </summary>
    public NodeEditor()
    {
        // Привязка к сетке и выравнивание по соседям — та же служба, что у дизайнера форм.
        // Соседей-узлы она собирает сама; своих, кроме них, у редактора узлов нет.
        AddService(new SnapService(this, static () => Array.Empty<Rect>()));
    }

    /// <summary>
    /// Определяет, нужен ли элементу коллекции контейнер <see cref="Node"/>.
    /// </summary>
    /// <param name="item">Элемент источника данных.</param>
    /// <param name="index">Индекс элемента.</param>
    /// <param name="recycleKey">Ключ повторного использования контейнера.</param>
    /// <returns><see langword="true"/>, если элемент сам узлом не является.</returns>
    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
        => NeedsContainer<Node>(item, out recycleKey);

    /// <summary>
    /// Создаёт контейнер узла.
    /// </summary>
    /// <param name="item">Элемент источника данных.</param>
    /// <param name="index">Индекс элемента.</param>
    /// <param name="recycleKey">Ключ повторного использования контейнера.</param>
    /// <returns>Новый <see cref="Node"/>.</returns>
    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
        => new Node();
}
