using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Metadata;
using ArxisStudio.Surface.Editing;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Редактор узлов: поверхность, на которой лежат узлы графа и связи между их портами.
/// </summary>
/// <remarks>
/// Слой рядом с дизайнером интерфейса, а не над ним (ADR 0004). Холст, выделение, рамка, перетаскивание
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
        // Привязка к сетке и выравнивание по соседям — та же служба, что у дизайнера интерфейса.
        // Соседей-узлы она собирает сама; своих, кроме них, у редактора узлов нет.
        AddService(new SnapService(this, static () => Array.Empty<Rect>()));

        // Миникарта инструментов рисует контейнеры сама, а связи ей отдаёт этот слой.
        AddService(new LinkMinimapLayer(this));

        Ports.Changed += OnPortsChanged;

        // Узел без контейнера о своём сдвиге говорит только панели, а она — сюда.
        ItemGeometryChanged += RefreshLinksOfItem;

        // Свои команды — впереди встроенных: они отвечают, только когда им есть что делать.
        // Escape сперва бросает протяжку, потом снимает выбор связей и лишь затем — узлов.
        KeyCommands.Insert(0, CancelLinkCommand());
        KeyCommands.Insert(1, ClearLinkSelectionCommand());
        KeyCommands.Insert(2, DeleteLinksCommand());

        // Выбор узлов и выбор связей взаимоисключающие: Delete значит что-то одно.
        SurfaceSelectionChanged += OnNodeSelectionChanged;
    }

    /// <summary>
    /// Идентификатор свойства привязки заголовка узла.
    /// </summary>
    public static readonly StyledProperty<BindingBase?> ItemHeaderBindingProperty =
        AvaloniaProperty.Register<NodeEditor, BindingBase?>(nameof(ItemHeaderBinding));

    /// <summary>
    /// Получает или задает привязку, дающую узлу заголовок из его элемента.
    /// </summary>
    /// <remarks>
    /// ADR 0009. Применяется к <see cref="Node.Header"/> созданного контейнера с элементом коллекции в
    /// качестве контекста данных — тем же приёмом, что <see cref="SurfaceView.ItemLocationBinding"/>:
    /// <c>ItemHeaderBinding="{Binding Title}"</c>. Снимается, когда контейнер уходит в пул; смена
    /// привязки переставляет заголовок и развёрнутым узлам. Готовый <see cref="Node"/> из коллекции не
    /// трогается.
    /// </remarks>
    [AssignBinding]
    [InheritDataTypeFromItems(nameof(ItemsSource))]
    public BindingBase? ItemHeaderBinding
    {
        get => GetValue(ItemHeaderBindingProperty);
        set => SetValue(ItemHeaderBindingProperty, value);
    }

    /// <summary>
    /// Живые порты на поверхности по их ключу.
    /// </summary>
    internal PortRegistry Ports { get; } = new();

    /// <inheritdoc />
    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);

        if (container is Node node && !ReferenceEquals(container, item))
            BindHeader(node);
    }

    /// <inheritdoc />
    protected override void ClearContainerForItemOverride(Control container)
    {
        base.ClearContainerForItemOverride(container);

        if (container is Node { HeaderBinding: { } binding } node)
        {
            binding.Dispose();
            node.HeaderBinding = null;
        }
    }

    private void BindHeader(Node node)
    {
        node.HeaderBinding?.Dispose();
        node.HeaderBinding = ItemHeaderBinding is { } header ? node.Bind(Node.HeaderProperty, header) : null;
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
