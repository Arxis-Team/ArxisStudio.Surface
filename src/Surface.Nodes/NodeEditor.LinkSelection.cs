using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using Avalonia.VisualTree;
using ArxisStudio.Surface.Nodes.States;
using ArxisStudio.Surface.States;

namespace ArxisStudio.Surface.Nodes;

// Выбор связей: попадание по кривой, выбор, подсветка, удаление запросом.
// Часть NodeEditor; общее описание типа — в NodeEditor.cs.
public partial class NodeEditor
{
    /// <summary>
    /// Идентификатор свойства допуска попадания по связи в пикселях экрана.
    /// </summary>
    public static readonly StyledProperty<double> LinkHitToleranceProperty =
        AvaloniaProperty.Register<NodeEditor, double>(nameof(LinkHitTolerance), 6.0);

    /// <summary>
    /// Идентификатор свойства выбранных связей.
    /// </summary>
    public static readonly DirectProperty<NodeEditor, IReadOnlyList<object>> SelectedLinksProperty =
        AvaloniaProperty.RegisterDirect<NodeEditor, IReadOnlyList<object>>(nameof(SelectedLinks), o => o.SelectedLinks);

    // Выбранные связи в порядке выбора — записями: выбор переживает свёрнутый контрол.
    private readonly List<LinkRecord> _selectedLinks = new();

    private IReadOnlyList<object> _selectedLinkItems = Array.Empty<object>();
    private LinkRecord? _highlightedLink;

    /// <summary>
    /// Возникает, когда просят удалить выбранные связи: удалите их из <see cref="Links"/>.
    /// </summary>
    /// <remarks>
    /// Обход подписчиков останавливается на первом, выставившем
    /// <see cref="LinkDeleteRequestedEventArgs.Handled"/>: список снят до правки, и следующему он
    /// описывал бы связи, которых уже нет.
    /// </remarks>
    public event EventHandler<LinkDeleteRequestedEventArgs>? LinkDeleteRequested;

    /// <summary>
    /// Получает или задает, на сколько пикселей экрана от линии связи щелчок ещё попадает в неё.
    /// </summary>
    /// <remarks>
    /// Отсчитывается от края линии, а не от её оси, и задан в пикселях экрана: на отдалении
    /// тонкая кривая иначе стала бы непопадаемой.
    /// </remarks>
    public double LinkHitTolerance
    {
        get => GetValue(LinkHitToleranceProperty);
        set => SetValue(LinkHitToleranceProperty, value);
    }

    /// <summary>
    /// Получает выбранные связи — элементы <see cref="Links"/> в порядке выбора.
    /// </summary>
    /// <remarks>
    /// Выбор связей и выбор узлов не бывают одновременно: выбор одного снимает другой, и поэтому
    /// Delete всегда значит что-то одно. Снимок публикуется только при настоящей перемене.
    /// </remarks>
    public IReadOnlyList<object> SelectedLinks
    {
        get => _selectedLinkItems;
        private set => SetAndRaise(SelectedLinksProperty, ref _selectedLinkItems, value);
    }

    /// <summary>
    /// Выбирает связь по элементу коллекции.
    /// </summary>
    /// <param name="item">Элемент <see cref="Links"/>.</param>
    /// <param name="additive">
    /// <see langword="true"/> — переключить связь в текущем выборе, как щелчок с
    /// модификатором добавления; иначе выбор заменяется ею.
    /// </param>
    /// <returns><see langword="false"/>, если такой связи на поверхности нет.</returns>
    public bool SelectLink(object item, bool additive = false)
    {
        foreach (var record in _recordByItem.Values)
        {
            if (Equals(record.ItemOrSelf, item))
            {
                SelectLinkCore(record, additive);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Снимает выбор со всех связей.
    /// </summary>
    public void ClearLinkSelection()
    {
        if (_selectedLinks.Count == 0)
            return;

        foreach (var record in _selectedLinks)
            SetLinkSelected(record, false);

        _selectedLinks.Clear();
        PublishLinkSelection();
    }

    internal void SelectLinkCore(LinkRecord link, bool additive)
    {
        if (additive)
        {
            if (_selectedLinks.Remove(link))
            {
                SetLinkSelected(link, false);
            }
            else
            {
                _selectedLinks.Add(link);
                SetLinkSelected(link, true);
            }
        }
        else
        {
            foreach (var other in _selectedLinks)
            {
                if (!ReferenceEquals(other, link))
                    SetLinkSelected(other, false);
            }

            _selectedLinks.Clear();
            _selectedLinks.Add(link);
            SetLinkSelected(link, true);
        }

        if (_selectedLinks.Count > 0)
            TryClearSelection();

        PublishLinkSelection();
    }

    /// <summary>
    /// Связь под точкой холста: ближайшая, до чьей линии не дальше допуска.
    /// </summary>
    internal LinkRecord? HitTestLink(Point world)
    {
        var tolerance = LinkHitTolerance / Math.Max(ViewportZoom, 0.0001);
        LinkRecord? best = null;
        var bestDistance = double.MaxValue;

        foreach (var link in _recordByItem.Values)
        {
            // Отсев по рамке, посчитанной при пересчёте концов: она уже включает всю толщину
            // линии, и проход по связям не читает ни одного свойства Avalonia. Толщина нужна
            // только тем немногим, что прошли отсев.
            if (!link.IsResolved || !link.WorldBounds.Inflate(tolerance).Contains(world))
                continue;

            LinkDistanceChecks++;
            var reach = tolerance + (ThicknessOf(link) / 2);
            var distance = link.Geometry.DistanceTo(world);
            if (distance <= reach && distance < bestDistance)
            {
                best = link;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// Сколько раз попадание мерило точное расстояние до кривой — для стенда стоимости.
    /// </summary>
    internal int LinkDistanceChecks { get; private set; }

    /// <summary>
    /// Убирает ушедшую из коллекции связь из подсветки и выбора.
    /// </summary>
    internal void OnLinkRemoved(LinkRecord link)
    {
        if (ReferenceEquals(_highlightedLink, link))
            SetHighlightedLink(null);

        if (_selectedLinks.Remove(link))
        {
            link.IsSelected = false;
            PublishLinkSelection();
        }
    }

    private void SetLinkSelected(LinkRecord link, bool value)
    {
        link.IsSelected = value;
        if (link.Control is { } control)
            control.Sync();
        else
            OnSimplifiedLinksChanged();
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.Handled && (TryStartCut(e) || TryPressLink(e)))
            return;

        base.OnPointerPressed(e);
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        SetHighlightedLink(LinkUnderPointer(e));
    }

    /// <inheritdoc />
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHighlightedLink(null);
    }

    /// <summary>
    /// Нажатие по связи выбирает её; мимо — уступает жесту ядра.
    /// </summary>
    private bool TryPressLink(PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed || ShouldStartPan(point.Properties, e.KeyModifiers))
            return false;

        var additive = ShouldUseAdditiveSelection(e.KeyModifiers);
        var link = LinkUnderPointer(e);
        if (link == null)
        {
            // Мимо связи начинается жест ядра — рамка; выбор связей уходит, как ушёл бы выбор узлов.
            if (!additive)
                ClearLinkSelection();
            return false;
        }

        RecordPointerInput(point.Position, e.KeyModifiers);
        if (!IsKeyboardFocusWithin)
            Focus();

        SelectLinkCore(link, additive);

        // Второе нажатие двойного щелчка просит излом (ADR 0005) и ничего не отцепляет. Выбор при
        // этом — как у щелчка: не разрежет хост — связь останется выбранной.
        if (e.ClickCount >= 2 && !additive)
        {
            RequestLinkSplit(link, GetWorldPosition(point.Position));
            e.Handled = true;
            return true;
        }

        // Протяжка тела связи отцепляет её ближний конец. Нажатие с модификатором добавления —
        // жест выбора, и отцеплять им нечего.
        if (!additive)
            PushState(new LinkPressState(this, link, e.Pointer, point.Position));

        e.Handled = true;
        return true;
    }

    /// <summary>
    /// Связь под указателем — одна на подсветку и на нажатие: подсвеченное обязано быть тем,
    /// что выберет щелчок.
    /// </summary>
    /// <remarks>
    /// Пока идёт другой жест, связи не отвечают. Над узлом тоже: связи лежат под узлами, и
    /// указатель над узлом принадлежит ему, даже когда узел событие не обработал — движение
    /// он не обрабатывает никогда.
    /// </remarks>
    private LinkRecord? LinkUnderPointer(PointerEventArgs e)
    {
        if (CurrentState is not EditorIdleState || IsOverNode(e.Source))
            return null;

        // Карточка упрощённого вида лежит поверх связи, как узел: связь под ней не берётся.
        var world = GetWorldPosition(e.GetPosition(this));
        return IsOverSimplifiedItem(world) ? null : HitTestLink(world);
    }

    private static bool IsOverNode(object? source) =>
        source is Visual visual && visual.FindAncestorOfType<Node>(includeSelf: true) != null;

    private void SetHighlightedLink(LinkRecord? link)
    {
        if (ReferenceEquals(_highlightedLink, link))
            return;

        if (_highlightedLink is { } previous)
            SetLinkHighlighted(previous, false);

        _highlightedLink = link;
        if (link != null)
            SetLinkHighlighted(link, true);
    }

    private void OnNodeSelectionChanged(object? sender, SurfaceSelectionChangedEventArgs e)
    {
        // Выбор узлов — индексный слой: выбранные узлы бывают и без контейнеров (ADR 0010).
        if (Selection.Count > 0)
            ClearLinkSelection();
    }

    private void PublishLinkSelection()
    {
        var current = _selectedLinkItems;
        if (current.Count == _selectedLinks.Count)
        {
            var same = true;
            for (var i = 0; i < current.Count && same; i++)
                same = Equals(current[i], _selectedLinks[i].ItemOrSelf);

            if (same)
                return;
        }

        var items = new object[_selectedLinks.Count];
        for (var i = 0; i < items.Length; i++)
            items[i] = _selectedLinks[i].ItemOrSelf;

        SelectedLinks = items;
    }

    internal bool RequestLinkDelete(IReadOnlyList<object> items)
    {
        var handler = LinkDeleteRequested;
        if (handler == null || items.Count == 0)
            return false;

        var args = new LinkDeleteRequestedEventArgs(items);
        foreach (var invocation in handler.GetInvocationList())
        {
            ((EventHandler<LinkDeleteRequestedEventArgs>)invocation)(this, args);
            if (args.Handled)
                break;
        }

        return args.Handled;
    }

    private static SurfaceKeyCommand ClearLinkSelectionCommand() => new(
        NodeEditorKeyCommands.ClearLinkSelection,
        static (view, e) => e.Key == Key.Escape && view is NodeEditor { _selectedLinks.Count: > 0 },
        static (view, _) =>
        {
            ((NodeEditor)view).ClearLinkSelection();
            return true;
        });

    private static SurfaceKeyCommand DeleteLinksCommand() => new(
        NodeEditorKeyCommands.DeleteLinks,
        static (view, e) => e.Key is Key.Delete or Key.Back && view is NodeEditor { _selectedLinks.Count: > 0 },
        static (view, _) => ((NodeEditor)view).RequestLinkDelete(((NodeEditor)view)._selectedLinkItems));
}
