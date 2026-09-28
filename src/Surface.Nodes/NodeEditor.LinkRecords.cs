using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Data;
using Avalonia.Utilities;

namespace ArxisStudio.Surface.Nodes;

// Записи связей: коллекция хоста, концы из привязок без контролов, контролы только у развёрнутых
// (ADR 0007). Часть NodeEditor; общее описание типа — в NodeEditor.cs.
public partial class NodeEditor
{
    // Запись на каждый элемент коллекции связей, по самому элементу.
    private readonly Dictionary<object, LinkRecord> _recordByItem = new(ReferenceEqualityComparer.Instance);

    // Сколько раз элемент стоит в коллекции: одна запись на все его вхождения.
    private readonly Dictionary<LinkRecord, int> _recordReferences = new();

    // Контролы свёрнутых связей, готовые показать другую.
    private readonly Stack<Link> _linkPool = new();

    private LinkWatcher? _linkWatcher;
    private INotifyCollectionChanged? _watchedLinks;
    private ObjectBindingReader? _sourceReader;
    private ObjectBindingReader? _targetReader;
    private ObjectBindingReader? _strokeReader;
    private LinkPanel? _linkPanel;

    // Толщина линии связи без контрола — последняя, что показал контрол: запас рамки на неё.
    private double _linkThickness = 2;

    /// <summary>
    /// Записи всех связей.
    /// </summary>
    internal IEnumerable<LinkRecord> LinkRecords => _recordByItem.Values;

    /// <summary>
    /// Сколько связей развёрнуто — для тестов виртуализации.
    /// </summary>
    internal int RealizedLinks => _recordByItem.Values.Count(record => record.Control != null);

    /// <summary>
    /// Сколько контролов связей редактор создал за свою жизнь — для стенда: пул их переиспользует.
    /// </summary>
    internal int LinksCreated { get; private set; }

    /// <summary>
    /// Запись элемента коллекции связей, если он в ней есть.
    /// </summary>
    internal LinkRecord? RecordOf(object? item) =>
        item != null && _recordByItem.TryGetValue(item, out var record) ? record : null;

    /// <summary>
    /// Виртуализирует ли редактор связи: вместе с узлами, когда задана привязка положения.
    /// </summary>
    /// <remarks>
    /// Решает привязка, а не уже созданная панель узлов: панель связей стоит в шаблоне раньше и
    /// меряется первой, и без привязки в ответе она развернула бы все связи графа, чтобы свернуть их,
    /// как только появится панель узлов.
    /// </remarks>
    internal bool IsLinkVirtualizing =>
        ItemLocationBinding != null && ItemsPanelRoot is null or VirtualizingSurfacePanel;

    /// <summary>
    /// Пересчитывает концы связи — по живым портам, смещениям с последнего показа или оценке.
    /// </summary>
    internal void RefreshLink(LinkRecord record)
    {
        LinkUpdates++;

        Point source = default, target = default;
        var resolved = record.Source != null && record.Target != null
            && TryGetLinkEnd(record.Source, LinkEnd.Source, out source)
            && TryGetLinkEnd(record.Target, LinkEnd.Target, out target);

        if (resolved)
        {
            record.Geometry = new LinkGeometry(source, target);
            record.WorldBounds = record.Geometry.Bounds.Inflate(ThicknessOf(record));
        }

        record.IsResolved = resolved;

        if (record.Control is { } control)
        {
            control.Sync();
        }
        else
        {
            if (_linkPanel != null && (!IsLinkVirtualizing || (resolved && WantsControl(record, LinkWindows().Realize))))
                _linkPanel.InvalidateMeasure();

            OnSimplifiedLinksChanged();
        }

        OnContentChanged();
    }

    /// <summary>
    /// Возникает, когда сменилось то, что рисует слой упрощённых связей: запись без контрола, состав
    /// развёрнутых или выбор (ADR 0008).
    /// </summary>
    internal event EventHandler? SimplifiedLinksChanged;

    private void OnSimplifiedLinksChanged() => SimplifiedLinksChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Положен ли связи контрол, кроме закрепления: ниже порога упрощённого вида — если на её конце
    /// живой порт, то есть узел развёрнут и его связи рисуются вживую; выше — если она в окне.
    /// </summary>
    private bool WantsControl(LinkRecord record, Rect window) =>
        IsSimplified
            ? (record.Source != null && Ports.Find(record.Source) != null) || (record.Target != null && Ports.Find(record.Target) != null)
            : window.Intersects(record.WorldBounds);

    /// <summary>
    /// Ставит на панель контролы связей, которым они положены, и снимает остальные.
    /// </summary>
    /// <remarks>
    /// Без виртуализации развёрнуты все. С ней — видимые с запасом, как узлы, и сворачиваются за
    /// двойным запасом; под курсором, под разрезом и отцепляемая — всегда, их вид — часть жеста. Под
    /// удержанием жеста не сворачивается ничего, а готовая связь из коллекции развёрнута всегда.
    /// </remarks>
    internal void RealizeLinks(LinkPanel panel)
    {
        var virtualizing = IsLinkVirtualizing;
        var (realize, keep) = virtualizing && !IsSimplified ? LinkWindows() : default;

        foreach (var record in _recordByItem.Values.ToArray())
        {
            if (!virtualizing || record.Own != null)
            {
                if (record.Control == null)
                    Realize(record, panel);

                continue;
            }

            var pinned = record.IsHighlighted || record.IsCutting || record.IsDetaching;
            if (record.Control == null)
            {
                if (pinned || (record.IsResolved && WantsControl(record, realize)))
                    Realize(record, panel);
            }
            else if (!pinned && !IsRealizationHeld && !(record.IsResolved && WantsControl(record, keep)))
            {
                Unrealize(record, panel);
            }
        }
    }

    /// <summary>
    /// Готовая связь из коллекции сменила концы: переставить её в смежности и пересчитать.
    /// </summary>
    internal void OnOwnLinkEndsChanged(LinkRecord record)
    {
        ReadEnds(record);
        Rekey(record);
    }

    /// <summary>
    /// Контрол связи сменил толщину: у рамки его записи — новый запас, и она же — толщина записей без
    /// контрола.
    /// </summary>
    internal void OnLinkThicknessChanged(Link link)
    {
        _linkThickness = link.StrokeThickness;
        if (link.Record is not { IsResolved: true } record)
            return;

        record.WorldBounds = record.Geometry.Bounds.Inflate(link.StrokeThickness);
        link.Sync();
    }

    /// <summary>
    /// Показывает, что связь перечёркнута разрезом, — с контролом или без.
    /// </summary>
    internal void SetLinkCutting(LinkRecord record, bool value) => SetState(record, value, static (r, v) => r.IsCutting = v);

    /// <summary>
    /// Показывает, что конец связи отцеплён и тянется, — с контролом или без.
    /// </summary>
    internal void SetLinkDetaching(LinkRecord record, bool value) => SetState(record, value, static (r, v) => r.IsDetaching = v);

    /// <summary>
    /// Показывает, что связь под указателем, — с контролом или без.
    /// </summary>
    internal void SetLinkHighlighted(LinkRecord record, bool value) => SetState(record, value, static (r, v) => r.IsHighlighted = v);

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LinksProperty)
        {
            WatchLinks(change.GetNewValue<IEnumerable?>());
        }
        else if (change.Property == LinkSourceBindingProperty || change.Property == LinkTargetBindingProperty)
        {
            _sourceReader = null;
            _targetReader = null;
            foreach (var record in _recordByItem.Values.ToArray())
            {
                ReadEnds(record);
                Rekey(record);
            }
        }
        else if (change.Property == LinkStrokeBindingProperty)
        {
            // Цвет — только в записи: концы и смежность не меняются, пересчёт кривой не нужен.
            _strokeReader = null;
            foreach (var record in _recordByItem.Values)
            {
                if (record.Own != null)
                    continue;

                record.Stroke = AccentBrushes.From(Read(ref _strokeReader, LinkStrokeBinding, record.Item));
                record.Control?.Sync();
            }

            ReleaseReaders();
            OnSimplifiedLinksChanged();
        }
        else if (change.Property == ItemHeaderBindingProperty)
        {
            foreach (var container in GetRealizedContainers())
            {
                if (container is Node node && !ReferenceEquals(ItemFromContainer(node), node))
                    BindHeader(node);
            }
        }
        else if (change.Property == PortNodeBindingProperty)
        {
            _portNodeReader = null;
            ForgetPortNodes();
            RefreshAllLinks();
        }
        else if (change.Property == ItemLocationBindingProperty || change.Property == IsSimplifiedProperty)
        {
            // Виртуализация связей идёт вместе с узлами: включилась, выключилась или сменила окно на
            // живые порты упрощённого вида — пересобрать.
            _linkPanel?.InvalidateMeasure();
        }
        else if (IsLinkVirtualizing
                 && !IsSimplified
                 && (change.Property == ViewportLocationProperty || change.Property == ViewportZoomProperty || change.Property == BoundsProperty))
        {
            // В упрощённом виде окна нет: контролы связей от панорамы не зависят.
            _linkPanel?.InvalidateMeasure();
        }
    }

    /// <summary>
    /// Берёт панель связей из шаблона: развёрнутые на прежней уходят вместе с ней.
    /// </summary>
    private void AttachLinkPanel(LinkPanel? panel)
    {
        if (ReferenceEquals(_linkPanel, panel))
            return;

        if (_linkPanel is { } previous)
        {
            foreach (var record in _recordByItem.Values.ToArray())
                Unrealize(record, previous);

            previous.Owner = null;
        }

        _linkPanel = panel;
        if (panel != null)
        {
            panel.Owner = this;
            panel.InvalidateMeasure();
        }
    }

    private void SetState(LinkRecord record, bool value, Action<LinkRecord, bool> set)
    {
        set(record, value);
        if (record.Control is { } control)
            control.Sync();
        else if (value && IsLinkVirtualizing)
            _linkPanel?.InvalidateMeasure();
    }

    private double ThicknessOf(LinkRecord record) => record.Control?.StrokeThickness ?? _linkThickness;

    private (Rect Realize, Rect Keep) LinkWindows()
    {
        var zoom = Math.Max(ViewportZoom, 0.0001);
        var visible = new Rect(ViewportLocation, Bounds.Size / zoom);
        var margin = Math.Max(0, (ItemsPanelRoot as VirtualizingSurfacePanel)?.RealizationMargin ?? 0) / zoom;
        return (visible.Inflate(margin), visible.Inflate(margin * 2));
    }

    private void Realize(LinkRecord record, LinkPanel panel)
    {
        var link = record.Own ?? (_linkPool.Count > 0 ? _linkPool.Pop() : CreateLink());
        record.Control = link;
        link.Show(this, record);
        panel.Children.Add(link);
        OnSimplifiedLinksChanged();
    }

    private Link CreateLink()
    {
        LinksCreated++;
        return new Link();
    }

    private void Unrealize(LinkRecord record, LinkPanel panel)
    {
        if (record.Control is not { } link)
            return;

        record.Control = null;
        panel.Remove(link);
        link.Hide();
        if (record.Own == null)
            _linkPool.Push(link);

        OnSimplifiedLinksChanged();
    }

    private void WatchLinks(IEnumerable? links)
    {
        _linkWatcher ??= new LinkWatcher(this);

        if (_watchedLinks != null)
            WeakEvents.CollectionChanged.Unsubscribe(_watchedLinks, _linkWatcher);

        _watchedLinks = links as INotifyCollectionChanged;
        if (_watchedLinks != null)
            WeakEvents.CollectionChanged.Subscribe(_watchedLinks, _linkWatcher);

        ResetRecords();
    }

    private void ResetRecords()
    {
        foreach (var record in _recordByItem.Values.ToArray())
        {
            _recordReferences[record] = 1;
            RemoveRecord(record);
        }

        if (Links is { } links)
        {
            foreach (var item in links)
            {
                if (item != null)
                    InsertRecord(item);
            }
        }

        ReleaseReaders();
    }

    private void OnLinksCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                foreach (var item in e.NewItems!)
                {
                    if (item != null)
                        InsertRecord(item);
                }

                break;
            case NotifyCollectionChangedAction.Remove:
                foreach (var item in e.OldItems!)
                {
                    if (RecordOf(item) is { } record)
                        RemoveRecord(record);
                }

                break;
            case NotifyCollectionChangedAction.Replace:
                foreach (var item in e.OldItems!)
                {
                    if (RecordOf(item) is { } record)
                        RemoveRecord(record);
                }

                foreach (var item in e.NewItems!)
                {
                    if (item != null)
                        InsertRecord(item);
                }

                break;
            case NotifyCollectionChangedAction.Move:
                // Порядок связей в коллекции ничего на холсте не меняет.
                break;
            default:
                ResetRecords();
                break;
        }

        ReleaseReaders();
    }

    private void InsertRecord(object item)
    {
        if (_recordByItem.TryGetValue(item, out var existing))
        {
            _recordReferences[existing]++;
            return;
        }

        var record = new LinkRecord(item);
        _recordByItem[item] = record;
        _recordReferences[record] = 1;

        ReadEnds(record);
        RegisterLink(record);
        if (record.Own == null && item is INotifyPropertyChanged model)
            WeakEvents.ThreadSafePropertyChanged.Subscribe(model, _linkWatcher ??= new LinkWatcher(this));

        RefreshLink(record);
        _linkPanel?.InvalidateMeasure();
    }

    private void RemoveRecord(LinkRecord record)
    {
        var references = _recordReferences[record] - 1;
        if (references > 0)
        {
            _recordReferences[record] = references;
            return;
        }

        _recordReferences.Remove(record);
        _recordByItem.Remove(record.Item);
        UnregisterLink(record);
        if (record.Own == null && record.Item is INotifyPropertyChanged model && _linkWatcher != null)
            WeakEvents.ThreadSafePropertyChanged.Unsubscribe(model, _linkWatcher);

        if (_linkPanel != null)
            Unrealize(record, _linkPanel);

        OnLinkRemoved(record);
        OnSimplifiedLinksChanged();
        OnContentChanged();
    }

    private void OnLinkModelChanged(object? model)
    {
        if (RecordOf(model) is not { } record)
            return;

        ReadEnds(record);
        ReleaseReaders();
        Rekey(record);
    }

    /// <summary>
    /// Читает ключи концов: у готовой связи — с неё самой, у остальных — привязками редактора.
    /// </summary>
    private void ReadEnds(LinkRecord record)
    {
        if (record.Own is { } own)
        {
            record.Source = own.Source;
            record.Target = own.Target;
            return;
        }

        record.Source = Read(ref _sourceReader, LinkSourceBinding, record.Item);
        record.Target = Read(ref _targetReader, LinkTargetBinding, record.Item);
        record.Stroke = AccentBrushes.From(Read(ref _strokeReader, LinkStrokeBinding, record.Item));
    }

    private static object? Read(ref ObjectBindingReader? reader, BindingBase? binding, object item)
    {
        if (binding == null)
            return null;

        if (reader == null || !ReferenceEquals(reader.Binding, binding))
            reader = new ObjectBindingReader(binding);

        return reader.Read(item);
    }

    /// <summary>
    /// Отпускает последние прочитанные объекты — в конце каждой пачки чтений.
    /// </summary>
    /// <remarks>
    /// Читатели живут дольше графа, и последний прочитанный порт мог принадлежать узлу, которого уже
    /// нет: держать его редактору нельзя.
    /// </remarks>
    private void ReleaseReaders()
    {
        _sourceReader?.Release();
        _targetReader?.Release();
        _strokeReader?.Release();
        _portNodeReader?.Release();
    }

    /// <summary>
    /// Переставляет запись в смежности под нынешними ключами и пересчитывает её.
    /// </summary>
    private void Rekey(LinkRecord record)
    {
        if (!Equals(record.Source, record.RegisteredSource) || !Equals(record.Target, record.RegisteredTarget))
        {
            UnregisterLink(record);
            RegisterLink(record);
        }

        RefreshLink(record);
    }

    /// <summary>
    /// Слушает коллекцию связей и модели связей слабо: они принадлежат хосту и живут дольше.
    /// </summary>
    private sealed class LinkWatcher(NodeEditor editor)
        : IWeakEventSubscriber<NotifyCollectionChangedEventArgs>, IWeakEventSubscriber<PropertyChangedEventArgs>
    {
        public void OnEvent(object? sender, WeakEvent ev, NotifyCollectionChangedEventArgs e) =>
            editor.OnLinksCollectionChanged(e);

        public void OnEvent(object? sender, WeakEvent ev, PropertyChangedEventArgs e) =>
            editor.OnLinkModelChanged(sender);
    }
}
