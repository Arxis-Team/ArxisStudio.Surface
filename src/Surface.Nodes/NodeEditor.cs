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
/// <see cref="ItemsControl.ItemsSource"/>, каждый в своём контейнере <see cref="Node"/>; связи —
/// <see cref="Links"/>.
/// </remarks>
public partial class NodeEditor : SurfaceView
{
    static NodeEditor()
    {
        // Сдвиг узла двигает концы связей его портов; сдвиг порта внутри узла ловит сам порт — по
        // своим границам. Границ самого узла здесь не слушают намеренно: сдвиг узла меняет и их, и
        // каждый кадр перетаскивания пересчитывал бы каждую его связь дважды.
        SurfaceItem.LocationProperty.Changed.AddClassHandler<Node>((node, _) => OnNodeGeometryChanged(node));
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="NodeEditor"/>.
    /// </summary>
    public NodeEditor()
    {
        // Привязка к сетке и выравнивание по соседям — та же служба, что у дизайнера форм.
        // Соседей-узлы она собирает сама; своих, кроме них, у редактора узлов нет.
        AddService(new SnapService(this, static () => Array.Empty<Rect>()));

        Ports.Changed += OnPortsChanged;

        // Свои команды — впереди встроенных: они отвечают, только когда им есть что делать.
        // Escape сперва бросает протяжку, потом снимает выбор связей и лишь затем — узлов.
        KeyCommands.Insert(0, CancelLinkCommand());
        KeyCommands.Insert(1, ClearLinkSelectionCommand());
        KeyCommands.Insert(2, DeleteLinksCommand());

        // Выбор узлов и выбор связей взаимоисключающие: Delete значит что-то одно.
        DesignSelectionChanged += OnNodeSelectionChanged;
    }

    /// <summary>
    /// Живые порты на поверхности по их ключу.
    /// </summary>
    internal PortRegistry Ports { get; } = new();

    /// <summary>
    /// Отвечает, где на холсте конец связи у порта с этими данными.
    /// </summary>
    /// <param name="data">Ключ порта.</param>
    /// <param name="world">Точка в мировых координатах.</param>
    /// <returns><see langword="false"/>, если такого порта на поверхности нет или он ещё не разложен.</returns>
    internal bool TryGetPortAnchor(object data, out Point world)
    {
        world = default;
        return Ports.Find(data) is { } port && port.TryGetAnchor(out world);
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
