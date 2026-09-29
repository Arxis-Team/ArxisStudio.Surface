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
    private ObjectBindingReader? _markerReader;
    private LinkPanel? _linkPanel;

    // Толщина линии связи без контрола — последняя, что показал контрол: запас рамки на неё.
    private double _linkThickness = 2;

    // Окно связей (ADR 0011): записи по ячейкам мира — вход в окно ищется по ячейкам окна; развёрнутые и
    // закреплённые жестом — отдельно, чтобы кадр не проходил все записи.
    private readonly Dictionary<(int X, int Y), List<LinkRecord>> _linkCells = new();
    private readonly HashSet<LinkRecord> _realizedLinks = new();
    private readonly HashSet<LinkRecord> _pinnedLinks = new();
    private readonly List<LinkRecord> _linkScratch = new();
    private double _linkCellSize = 1;
    private bool _linkCellsStale = true;

    // Проход по всем записям — после смены коллекции, панели или вида: готовая связь из коллекции
    // развёрнута всегда, а окно её не ищет.
    private bool _linkFullPass = true;
    private Rect _linkWindowVisible;
    private bool _hasLinkWindow;

    /// <summary>
    /// Записи всех связей.
    /// </summary>
    internal IEnumerable<LinkRecord> LinkRecords => _recordByItem.Values;

    /// <summary>
    /// Сколько связей развёрнуто — для тестов виртуализации.
    /// </summary>
    internal int RealizedLinks => _realizedLinks.Count;

    /// <summary>
    /// Сколько записей редактор проверил, ища вошедших в окно связей, — для тестов и стенда: не растёт с
    /// графом.
    /// </summary>
    internal int LinkWindowChecks { get; private set; }

    /// <summary>
    /// Сколько раз редактор пересматривал окно связей — для тестов и стенда.
    /// </summary>
    internal int LinkWindowPasses { get; private set; }

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

        // Холст сменился, только если концы сдвинулись или связь нашлась либо потерялась: узел,
        // развёрнутый заново на панораме, пересчитывает свои связи в те же точки (ADR 0011).
        var moved = resolved != record.IsResolved
            || (resolved && (!Near(record.Geometry.Source, source) || !Near(record.Geometry.Target, target)));

        // Касательные меняет и разворот узла перенаправления на конце, при тех же концах (ADR 0013).
        var bent = false;
        if (resolved)
        {
            var geometry = new LinkGeometry(
                source,
                target,
                IsReversedEnd(record.Source, LinkEnd.Source),
                IsReversedEnd(record.Target, LinkEnd.Target),
                LinkCurve);
            bent = !moved && (!Near(record.Geometry.SourceControl, geometry.SourceControl)
                || !Near(record.Geometry.TargetControl, geometry.TargetControl));
            record.Geometry = geometry;
            record.WorldBounds = record.Geometry.Bounds.Inflate(ThicknessOf(record));
        }

        record.IsResolved = resolved;
        IndexLink(record);

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

        if (moved || bent)
            OnContentChanged();

        OnLinkFlowChanged();

        // Сдвинулся дальний конец — узел перенаправления на ближнем мог развернуться (ADR 0013).
        if (moved && _knotByKey.Count > 0)
        {
            UpdateKnotAt(record.Source);
            UpdateKnotAt(record.Target);
        }
    }

    /// <summary>
    /// Совпадают ли точки с точностью до шума арифметики: конец по живому порту и по смещению,
    /// снятому при показе, считаются разными дорогами.
    /// </summary>
    private static bool Near(Point a, Point b) => Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6;

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
        var windowed = virtualizing && !IsSimplified;
        var (visible, realize, keep) = windowed ? LinkWindows() : default;
        if (windowed)
        {
            LinkWindowPasses++;
            _linkWindowVisible = visible;
            _hasLinkWindow = true;
        }

        // Без окна — без виртуализации или в упрощённом виде, где связи держит живой порт, — и после
        // смены состава проходятся все записи; такая мера редка: панорама её не зовёт.
        if (!windowed || _linkFullPass)
        {
            _linkFullPass = false;
            foreach (var record in _recordByItem.Values.ToArray())
                RealizeOrRelease(record, panel, virtualizing, realize, keep);

            return;
        }

        // Кадр окна: уходят из развёрнутых, приходят закреплённые жестом и вошедшие — по ячейкам окна.
        _linkScratch.Clear();
        _linkScratch.AddRange(_realizedLinks);
        foreach (var record in _linkScratch)
            RealizeOrRelease(record, panel, virtualizing, realize, keep);

        _linkScratch.Clear();
        _linkScratch.AddRange(_pinnedLinks);
        foreach (var record in _linkScratch)
            RealizeOrRelease(record, panel, virtualizing, realize, keep);

        // Сперва собрать, потом разворачивать: запись, пересчитанная при показе, переложилась бы в
        // другую ячейку посреди обхода.
        EnsureLinkCells();
        _linkScratch.Clear();
        var (c0, r0, c1, r1) = LinkCellsOf(realize);
        for (var y = r0; y <= r1; y++)
        {
            for (var x = c0; x <= c1; x++)
            {
                if (!_linkCells.TryGetValue((x, y), out var cell))
                    continue;

                foreach (var record in cell)
                {
                    LinkWindowChecks++;
                    if (record.Control == null && record.IsResolved && realize.Intersects(record.WorldBounds))
                        _linkScratch.Add(record);
                }
            }
        }

        // Длинная связь лежит в нескольких ячейках — развёрнутая в первой, дальше она уже с контролом.
        foreach (var record in _linkScratch)
        {
            if (record.Control == null)
                Realize(record, panel);
        }
    }

    private void RealizeOrRelease(LinkRecord record, LinkPanel panel, bool virtualizing, Rect realize, Rect keep)
    {
        if (!virtualizing || record.Own != null)
        {
            if (record.Control == null)
                Realize(record, panel);

            return;
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

    /// <summary>
    /// Ушла ли видимая область от места, где окно связей пересматривали, на четверть запаса, или
    /// сменились масштаб или размер, — как у панели узлов (ADR 0011).
    /// </summary>
    private bool LinkWindowMoved()
    {
        if (!_hasLinkWindow)
            return true;

        var (visible, _, _) = LinkWindows();
        if (visible.Size != _linkWindowVisible.Size)
            return true;

        var margin = Math.Max(0, (ItemsPanelRoot as VirtualizingSurfacePanel)?.RealizationMargin ?? 0);
        var step = margin / Math.Max(ViewportZoom, 0.0001) / 4;
        return Math.Abs(visible.X - _linkWindowVisible.X) > step || Math.Abs(visible.Y - _linkWindowVisible.Y) > step;
    }

    /// <summary>
    /// Кладёт запись в ячейки по её нынешней рамке — разрешённую, — или вынимает.
    /// </summary>
    private void IndexLink(LinkRecord record)
    {
        if (_linkCellsStale)
            return;

        var next = record.IsResolved && _recordByItem.ContainsKey(record.Item) ? record.WorldBounds : (Rect?)null;
        if (next == record.IndexedBounds)
            return;

        if (record.IndexedBounds is { } before)
            RemoveFromLinkCells(record, before);

        if (next is { } after)
            AddToLinkCells(record, after);

        record.IndexedBounds = next;
    }

    private void EnsureLinkCells()
    {
        if (!_linkCellsStale)
            return;

        _linkCellsStale = false;
        _linkCells.Clear();

        // Ячейка — как у панели узлов: несколько предполагаемых узлов.
        var estimated = EstimatedItemSize;
        _linkCellSize = Math.Max(256, 4 * Math.Max(estimated.Width, estimated.Height));
        foreach (var record in _recordByItem.Values)
        {
            record.IndexedBounds = null;
            IndexLink(record);
        }
    }

    private void AddToLinkCells(LinkRecord record, Rect bounds)
    {
        var (c0, r0, c1, r1) = LinkCellsOf(bounds);
        for (var y = r0; y <= r1; y++)
        {
            for (var x = c0; x <= c1; x++)
            {
                if (!_linkCells.TryGetValue((x, y), out var cell))
                    _linkCells[(x, y)] = cell = new List<LinkRecord>();

                cell.Add(record);
            }
        }
    }

    private void RemoveFromLinkCells(LinkRecord record, Rect bounds)
    {
        var (c0, r0, c1, r1) = LinkCellsOf(bounds);
        for (var y = r0; y <= r1; y++)
        {
            for (var x = c0; x <= c1; x++)
            {
                if (_linkCells.TryGetValue((x, y), out var cell))
                    cell.Remove(record);
            }
        }
    }

    private (int C0, int R0, int C1, int R1) LinkCellsOf(Rect bounds) =>
        (LinkCell(bounds.Left), LinkCell(bounds.Top), LinkCell(bounds.Right), LinkCell(bounds.Bottom));

    private int LinkCell(double value) => (int)Math.Clamp(Math.Floor(value / _linkCellSize), -1_000_000, 1_000_000);

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
        IndexLink(record);
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
        OnFlowPropertyChanged(change);

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
            OnLinkFlowChanged();
        }
        else if (change.Property == LinkMarkerBindingProperty)
        {
            _markerReader = null;
            foreach (var record in _recordByItem.Values)
            {
                if (record.Own == null)
                    record.Marker = Read(ref _markerReader, LinkMarkerBinding, record.Item) as LinkMarker;
            }

            ReleaseReaders();
            OnLinkFlowChanged();
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
            _linkFullPass = true;
            _linkPanel?.InvalidateMeasure();
        }
        else if (change.Property == EstimatedItemSizeProperty)
        {
            _linkCellsStale = true;
        }
        else if (IsLinkVirtualizing
                 && !IsSimplified
                 && (change.Property == ViewportLocationProperty || change.Property == ViewportZoomProperty || change.Property == BoundsProperty)
                 && LinkWindowMoved())
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
        _linkFullPass = true;
        if (panel != null)
        {
            panel.Owner = this;
            panel.InvalidateMeasure();
        }
    }

    private void SetState(LinkRecord record, bool value, Action<LinkRecord, bool> set)
    {
        set(record, value);
        if (record.IsHighlighted || record.IsCutting || record.IsDetaching)
            _pinnedLinks.Add(record);
        else
            _pinnedLinks.Remove(record);

        if (record.Control is { } control)
            control.Sync();
        else if (value && IsLinkVirtualizing)
            _linkPanel?.InvalidateMeasure();

        // Маркер выбранной связи — цвета выбора.
        OnLinkFlowChanged();
    }

    private double ThicknessOf(LinkRecord record) => record.Control?.StrokeThickness ?? _linkThickness;

    private (Rect Visible, Rect Realize, Rect Keep) LinkWindows()
    {
        var zoom = Math.Max(ViewportZoom, 0.0001);
        var visible = new Rect(ViewportLocation, Bounds.Size / zoom);
        var margin = Math.Max(0, (ItemsPanelRoot as VirtualizingSurfacePanel)?.RealizationMargin ?? 0) / zoom;
        return (visible, visible.Inflate(margin), visible.Inflate(margin * 2));
    }

    private void Realize(LinkRecord record, LinkPanel panel)
    {
        var link = record.Own ?? (_linkPool.Count > 0 ? _linkPool.Pop() : CreateLink());
        record.Control = link;
        _realizedLinks.Add(record);
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
        _realizedLinks.Remove(record);
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
        _linkCellsStale = true;
        _linkFullPass = true;
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

        // Готовую связь из коллекции окно не ищет: она развёрнута всегда.
        if (record.Own != null)
            _linkFullPass = true;

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
        IndexLink(record);
        _pinnedLinks.Remove(record);
        UnregisterLink(record);
        if (record.Own == null && record.Item is INotifyPropertyChanged model && _linkWatcher != null)
            WeakEvents.ThreadSafePropertyChanged.Unsubscribe(model, _linkWatcher);

        if (_linkPanel != null)
            Unrealize(record, _linkPanel);

        OnLinkRemoved(record);
        OnSimplifiedLinksChanged();
        OnLinkFlowChanged();
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
        record.Marker = Read(ref _markerReader, LinkMarkerBinding, record.Item) as LinkMarker;
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
        _markerReader?.Release();
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
