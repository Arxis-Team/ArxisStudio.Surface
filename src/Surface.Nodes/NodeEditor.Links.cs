using System.Collections;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Metadata;
using Avalonia.VisualTree;

namespace ArxisStudio.Surface.Nodes;

// Связи: коллекция хоста, концы по данным портов, пересчёт только затронутых.
// Часть NodeEditor; общее описание типа — в NodeEditor.cs.
public partial class NodeEditor
{
    /// <summary>
    /// Идентификатор свойства коллекции связей.
    /// </summary>
    public static readonly StyledProperty<IEnumerable?> LinksProperty =
        AvaloniaProperty.Register<NodeEditor, IEnumerable?>(nameof(Links));

    /// <summary>
    /// Идентификатор свойства привязки источника связи.
    /// </summary>
    public static readonly StyledProperty<BindingBase?> LinkSourceBindingProperty =
        AvaloniaProperty.Register<NodeEditor, BindingBase?>(nameof(LinkSourceBinding));

    /// <summary>
    /// Идентификатор свойства привязки цели связи.
    /// </summary>
    public static readonly StyledProperty<BindingBase?> LinkTargetBindingProperty =
        AvaloniaProperty.Register<NodeEditor, BindingBase?>(nameof(LinkTargetBinding));

    /// <summary>
    /// Идентификатор свойства привязки цвета провода.
    /// </summary>
    public static readonly StyledProperty<BindingBase?> LinkStrokeBindingProperty =
        AvaloniaProperty.Register<NodeEditor, BindingBase?>(nameof(LinkStrokeBinding));

    /// <summary>
    /// Идентификатор свойства привязки толщины провода.
    /// </summary>
    public static readonly StyledProperty<BindingBase?> LinkThicknessBindingProperty =
        AvaloniaProperty.Register<NodeEditor, BindingBase?>(nameof(LinkThicknessBinding));

    // Связи по ключу любого из концов: сдвиг порта пересчитывает ровно их, а не все.
    private readonly Dictionary<object, List<LinkRecord>> _linksByKey = new();

    // Порты по узлу: сдвиг узла пересчитывает связи его портов.
    private readonly Dictionary<Node, List<Port>> _portsByNode = new();

    /// <summary>
    /// Получает или задает коллекцию связей.
    /// </summary>
    /// <remarks>
    /// Коллекцией владеет приложение, как и узлами (ADR 0001): редактор связь не добавит и не
    /// уберёт, а попросит об этом запросом. Элементом может быть готовая <see cref="Link"/>; любой
    /// другой объект получит свою, с концами из <see cref="LinkSourceBinding"/> и
    /// <see cref="LinkTargetBinding"/>.
    /// </remarks>
    public IEnumerable? Links
    {
        get => GetValue(LinksProperty);
        set => SetValue(LinksProperty, value);
    }

    /// <summary>
    /// Получает или задает привязку, дающую связи данные её порта-источника.
    /// </summary>
    /// <remarks>
    /// Применяется к <see cref="Link.Source"/>, когда связь создаётся, с элементом коллекции в
    /// качестве контекста данных: <c>LinkSourceBinding="{Binding From}"</c>.
    /// </remarks>
    [AssignBinding]
    [InheritDataTypeFromItems(nameof(Links))]
    public BindingBase? LinkSourceBinding
    {
        get => GetValue(LinkSourceBindingProperty);
        set => SetValue(LinkSourceBindingProperty, value);
    }

    /// <summary>
    /// Получает или задает привязку, дающую проводу цвет из его элемента — например, по типу данных,
    /// как в Blueprint.
    /// </summary>
    /// <remarks>
    /// ADR 0009. Значение — кисть или цвет; иное, как и <see langword="null"/>, оставляет цвет темы.
    /// Применяется с элементом коллекции связей в качестве контекста данных, перечитывается по
    /// <c>INotifyPropertyChanged</c> модели и держится в записи связи, поэтому упрощённый вид рисует
    /// провода теми же цветами. Цвет модели — обычное состояние провода: выбор, наведение и разрез красят
    /// его цветом темы. Готовую <see cref="Link"/> из коллекции хост красит сам.
    /// </remarks>
    [AssignBinding]
    [InheritDataTypeFromItems(nameof(Links))]
    public BindingBase? LinkStrokeBinding
    {
        get => GetValue(LinkStrokeBindingProperty);
        set => SetValue(LinkStrokeBindingProperty, value);
    }

    /// <summary>
    /// Получает или задает привязку, дающую проводу толщину из его элемента, в мировых единицах, —
    /// например, провод выполнения толще провода данных, как в Blueprint (ADR 0016).
    /// </summary>
    /// <remarks>
    /// Значение — число; иное, как и <see langword="null"/>, оставляет толщину темы
    /// (<c>NodeEditor.Link.Thickness</c>). Перечитывается по <c>INotifyPropertyChanged</c> модели, как цвет.
    /// Толщину берут контрол связи, её рамка и попадание по ней; слой упрощённого вида и миникарта рисуют
    /// все провода одной толщиной. Готовой <see cref="Link"/> из коллекции толщину ставит хост.
    /// </remarks>
    [AssignBinding]
    [InheritDataTypeFromItems(nameof(Links))]
    public BindingBase? LinkThicknessBinding
    {
        get => GetValue(LinkThicknessBindingProperty);
        set => SetValue(LinkThicknessBindingProperty, value);
    }

    /// <summary>
    /// Получает или задает привязку, дающую связи данные её порта-цели.
    /// </summary>
    [AssignBinding]
    [InheritDataTypeFromItems(nameof(Links))]
    public BindingBase? LinkTargetBinding
    {
        get => GetValue(LinkTargetBindingProperty);
        set => SetValue(LinkTargetBindingProperty, value);
    }

    /// <summary>
    /// Сколько раз связи пересчитывали свои концы — для стенда стоимости.
    /// </summary>
    internal int LinkUpdates { get; private set; }

    /// <summary>
    /// Ставит запись в смежность под её нынешними ключами.
    /// </summary>
    internal void RegisterLink(LinkRecord record)
    {
        record.RegisteredSource = record.Source;
        record.RegisteredTarget = record.Target;

        AddLink(record.Source, record);
        if (!Equals(record.Source, record.Target))
            AddLink(record.Target, record);

        UpdateConnected(record.Source);
        UpdateConnected(record.Target);
    }

    /// <summary>
    /// Снимает запись со смежности — с тех ключей, под которыми её ставили.
    /// </summary>
    internal void UnregisterLink(LinkRecord record)
    {
        var source = record.RegisteredSource;
        var target = record.RegisteredTarget;
        record.RegisteredSource = null;
        record.RegisteredTarget = null;

        RemoveLink(source, record);
        if (!Equals(source, target))
            RemoveLink(target, record);

        UpdateConnected(source);
        UpdateConnected(target);

        if (_knotByKey.Count > 0)
        {
            SettleKnotAt(source);
            SettleKnotAt(target);
        }
    }

    internal void AttachPort(Port port, Node node)
    {
        if (!_portsByNode.TryGetValue(node, out var list))
            _portsByNode[node] = list = new List<Port>();

        if (!list.Contains(port))
            list.Add(port);
    }

    internal void DetachPort(Port port, Node node)
    {
        if (_portsByNode.TryGetValue(node, out var list) && list.Remove(port) && list.Count == 0)
            _portsByNode.Remove(node);
    }

    /// <summary>
    /// Пересчитывает связи, у которых один из концов — порт с этим ключом.
    /// </summary>
    internal void RefreshLinksAt(object? key)
    {
        if (key == null || !_linksByKey.TryGetValue(key, out var links))
            return;

        // Снимок: пересчёт связи вправе поменять её регистрацию.
        foreach (var record in links.ToArray())
            RefreshLink(record);
    }

    /// <summary>
    /// Пересчитывает связи тех портов узла, что сдвинулись внутри него, — после раскладки узла.
    /// </summary>
    /// <remarks>
    /// Зовётся из раскладки узла, когда его поддерево уже разложено: только тогда положение порта
    /// внутри узла окончательное. Каждая связь пересчитывается один раз, даже если сдвинулись
    /// оба её конца.
    /// </remarks>
    internal void OnNodeArranged(Node node)
    {
        if (!_portsByNode.TryGetValue(node, out var ports))
            return;

        HashSet<LinkRecord>? moved = null;
        foreach (var port in ports)
            CollectMoved(port, node, ref moved);

        if (moved == null)
            return;

        foreach (var record in moved)
            RefreshLink(record);
    }

    /// <summary>
    /// Сверяет порт, получивший новые границы, — на случай, когда он разложился сам, без узла.
    /// </summary>
    /// <remarks>
    /// Внутри раскладки узла ответ здесь ранний — предки порта ещё без границ, — и его не считают
    /// вовсе: сверит <see cref="OnNodeArranged"/> в конце той же раскладки. Иначе связь узла,
    /// развёрнутого на панораме, пересчитывалась бы дважды — в неверную точку и обратно, — и холст
    /// дважды объявлял бы себя сменившимся (ADR 0011).
    /// </remarks>
    internal void OnPortMoved(Port port, Node node)
    {
        if (node.IsArranging)
            return;

        HashSet<LinkRecord>? moved = null;
        CollectMoved(port, node, ref moved);

        if (moved == null)
            return;

        foreach (var record in moved)
            RefreshLink(record);
    }

    private void CollectMoved(Port port, Node node, ref HashSet<LinkRecord>? moved)
    {
        if (!port.TryGetOffsetInNode(node, out var offset) || port.LastOffset == offset)
            return;

        port.LastOffset = offset;
        if (port.Key is { } key && _linksByKey.TryGetValue(key, out var links))
            (moved ??= new HashSet<LinkRecord>()).UnionWith(links);
    }

    /// <summary>
    /// Пересчитывает связи портов узла — каждую один раз, даже если оба её конца на нём.
    /// </summary>
    private void RefreshNodeLinks(Node node)
    {
        if (!_portsByNode.TryGetValue(node, out var ports))
            return;

        var touched = new HashSet<LinkRecord>();
        foreach (var port in ports)
        {
            if (port.Key is { } key && _linksByKey.TryGetValue(key, out var links))
                touched.UnionWith(links);
        }

        foreach (var record in touched)
            RefreshLink(record);
    }

    private void OnPortsChanged(object key)
    {
        UpdateConnected(key);
        RefreshLinksAt(key);
        SettleKnotAt(key);

        // Ниже порога упрощённого вида контрол связи держит живой порт на её конце: пришёл порт —
        // связь разворачивается, ушёл — сворачивается.
        if (IsSimplified)
            _linkPanel?.InvalidateMeasure();
    }

    private void UpdateConnected(object? key)
    {
        if (key != null && Ports.Find(key) is { } port)
            port.IsConnected = _linksByKey.TryGetValue(key, out var links) && links.Count > 0;
    }

    private void AddLink(object? key, LinkRecord record)
    {
        if (key == null)
            return;

        if (!_linksByKey.TryGetValue(key, out var list))
            _linksByKey[key] = list = new List<LinkRecord>();

        if (!list.Contains(record))
            list.Add(record);
    }

    private void RemoveLink(object? key, LinkRecord record)
    {
        if (key != null && _linksByKey.TryGetValue(key, out var list) && list.Remove(record) && list.Count == 0)
            _linksByKey.Remove(key);
    }

    private static void OnNodeGeometryChanged(Node node) =>
        node.FindAncestorOfType<NodeEditor>()?.RefreshNodeLinks(node);
}
