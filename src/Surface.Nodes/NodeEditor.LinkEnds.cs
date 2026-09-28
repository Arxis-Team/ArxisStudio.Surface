using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Data;

namespace ArxisStudio.Surface.Nodes;

// Концы связей: живой порт, смещение порта с последнего показа узла, оценка по краю узла, который
// ни разу не показывался (ADR 0007). Часть NodeEditor; общее описание типа — в NodeEditor.cs.
public partial class NodeEditor
{
    /// <summary>
    /// Идентификатор свойства привязки узла порта.
    /// </summary>
    public static readonly StyledProperty<BindingBase?> PortNodeBindingProperty =
        AvaloniaProperty.Register<NodeEditor, BindingBase?>(nameof(PortNodeBinding));

    // Где был конец связи внутри узла при последнем показе — по ключу порта, вместе с элементом узла.
    private readonly Dictionary<object, (object Node, Vector Offset)> _lastOffsets = new();

    // Ключи портов по элементу их узла: сдвиг свёрнутого узла пересчитывает связи ровно этих портов.
    private readonly Dictionary<object, HashSet<object>> _portKeysByNode = new();

    private ObjectBindingReader? _portNodeReader;

    /// <summary>
    /// Получает или задает привязку, дающую по данным порта элемент его узла.
    /// </summary>
    /// <remarks>
    /// Нужна связи к узлу, который при виртуализации ещё ни разу не показывался (ADR 0007): порта у
    /// такого узла нет, и конец ставится оценкой — на середину правого края прямоугольника узла у
    /// источника и левого у цели. Применяется с данными порта в качестве контекста:
    /// <c>PortNodeBinding="{Binding Node, DataType=vm:PortViewModel}"</c>. После первого показа конец
    /// точный, а свёрнутый снова узел держит смещение порта, снятое при последнем показе. Без привязки
    /// связь к ни разу не показанному узлу не рисуется, пока он не покажется.
    /// </remarks>
    [AssignBinding]
    public BindingBase? PortNodeBinding
    {
        get => GetValue(PortNodeBindingProperty);
        set => SetValue(PortNodeBindingProperty, value);
    }

    /// <summary>
    /// Отвечает, где на холсте конец связи у порта с этими данными.
    /// </summary>
    /// <remarks>
    /// Живой порт даёт точный конец. Свёрнутый узел — нынешнее положение узла из панели плюс смещение
    /// порта, снятое при его последнем показе. Узел, который ни разу не показывался, — середину своего
    /// края, если <see cref="PortNodeBinding"/> называет его по данным порта.
    /// </remarks>
    /// <param name="key">Ключ порта.</param>
    /// <param name="end">Какой это конец: от него зависит край оценки.</param>
    /// <param name="world">Точка в мировых координатах.</param>
    /// <returns><see langword="false"/>, если конец взять неоткуда.</returns>
    internal bool TryGetLinkEnd(object key, LinkEnd end, out Point world)
    {
        if (Ports.Find(key) is { } port && port.TryGetAnchor(out world))
        {
            RememberOffset(key, port, world);
            return true;
        }

        // Без виртуализации узлы не сворачиваются, и конца, кроме живого, у связи нет.
        if (!IsLinkVirtualizing)
        {
            world = default;
            return false;
        }

        if (_lastOffsets.TryGetValue(key, out var last) && TryGetItemBounds(last.Node, out var bounds))
        {
            world = bounds.TopLeft + last.Offset;
            return true;
        }

        if (ReadPortNode(key) is { } node && TryGetItemBounds(node, out bounds))
        {
            TrackPort(node, key);
            world = new Point(end == LinkEnd.Source ? bounds.Right : bounds.Left, bounds.Center.Y);
            return true;
        }

        world = default;
        return false;
    }

    /// <summary>
    /// Запоминает смещение живого порта в его узле — на время, когда узел свернут.
    /// </summary>
    /// <remarks>
    /// Только у виртуализирующей панели: без неё узлы не сворачиваются, а искать индекс узла обычная
    /// панель умеет лишь проходом по детям — на каждом пересчёте конца.
    /// </remarks>
    private void RememberOffset(object key, Port port, Point world)
    {
        if (!IsLinkVirtualizing
            || port.Node is not { } node
            || IndexFromContainer(node) < 0
            || ItemFromContainer(node) is not { } item)
        {
            return;
        }

        _lastOffsets[key] = (item, world - node.Location);
        TrackPort(item, key);
    }

    private void TrackPort(object node, object key)
    {
        if (!_portKeysByNode.TryGetValue(node, out var keys))
            _portKeysByNode[node] = keys = new HashSet<object>();

        keys.Add(key);
    }

    private object? ReadPortNode(object key)
    {
        if (PortNodeBinding is not { } binding)
            return null;

        if (_portNodeReader == null || !ReferenceEquals(_portNodeReader.Binding, binding))
            _portNodeReader = new ObjectBindingReader(binding);

        return _portNodeReader.Read(key);
    }

    /// <summary>
    /// Пересчитывает связи портов узла, чья геометрия сменилась без контейнера, — а без узла все.
    /// </summary>
    /// <remarks>
    /// Этим же панель сообщает об узле, ушедшем из коллекции, а без узла — о том, что перечитала её
    /// целиком. Узел, которого в ней больше нет, забывается здесь: записи держат элементы узлов и
    /// данные портов, а оценка конца заводит запись на каждый узел, до которого дошла связь, — без
    /// уборки редактор держал бы удалённые узлы, а сменив граф, прежний целиком. Забывается после
    /// сообщения панели, а не по событию коллекции: оно приходит раньше, чем панель уберёт узел, и
    /// его связи остались бы нарисованными к пустому месту.
    /// </remarks>
    private void RefreshLinksOfItem(object? item)
    {
        if (item == null)
        {
            ForgetAbsentNodes();
            RefreshAllLinks();
            return;
        }

        if (!_portKeysByNode.TryGetValue(item, out var keys))
            return;

        var touched = new HashSet<LinkRecord>();
        foreach (var key in keys)
        {
            if (_linksByKey.TryGetValue(key, out var links))
                touched.UnionWith(links);
        }

        foreach (var record in touched)
            RefreshLink(record);

        if (!TryGetItemBounds(item, out _))
            ForgetNode(item);

        ReleaseReaders();
    }

    private void RefreshAllLinks()
    {
        foreach (var record in _recordByItem.Values.ToArray())
            RefreshLink(record);

        ReleaseReaders();
    }

    /// <summary>
    /// Забывает узлы, которых нет в коллекции: панель перечитала её целиком.
    /// </summary>
    private void ForgetAbsentNodes()
    {
        List<object>? absent = null;
        foreach (var node in _portKeysByNode.Keys)
        {
            if (!TryGetItemBounds(node, out _))
                (absent ??= new List<object>()).Add(node);
        }

        if (absent == null)
            return;

        foreach (var node in absent)
            ForgetNode(node);
    }

    /// <summary>
    /// Забывает смещения и слежение портов узла; читатель узла порта отпускает тот, кто звал.
    /// </summary>
    private void ForgetNode(object item)
    {
        if (!_portKeysByNode.Remove(item, out var keys))
            return;

        foreach (var key in keys)
        {
            if (_lastOffsets.TryGetValue(key, out var last) && ReferenceEquals(last.Node, item))
                _lastOffsets.Remove(key);
        }
    }
}
