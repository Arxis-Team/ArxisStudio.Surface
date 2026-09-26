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

    // Все живые связи поверхности: по ним ищется попадание.
    private readonly HashSet<Link> _links = new();

    // Выбранные связи в порядке выбора.
    private readonly List<Link> _selectedLinks = new();

    private IReadOnlyList<object> _selectedLinkItems = Array.Empty<object>();
    private Link? _highlightedLink;

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
        foreach (var link in _links)
        {
            if (Equals(link.ItemOrSelf, item))
            {
                SelectLinkCore(link, additive);
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

        foreach (var link in _selectedLinks)
            link.IsSelected = false;

        _selectedLinks.Clear();
        PublishLinkSelection();
    }

    internal void SelectLinkCore(Link link, bool additive)
    {
        if (additive)
        {
            if (_selectedLinks.Remove(link))
            {
                link.IsSelected = false;
            }
            else
            {
                _selectedLinks.Add(link);
                link.IsSelected = true;
            }
        }
        else
        {
            foreach (var other in _selectedLinks)
            {
                if (!ReferenceEquals(other, link))
                    other.IsSelected = false;
            }

            _selectedLinks.Clear();
            _selectedLinks.Add(link);
            link.IsSelected = true;
        }

        if (_selectedLinks.Count > 0)
            TryClearSelection();

        PublishLinkSelection();
    }

    /// <summary>
    /// Связь под точкой холста: ближайшая, до чьей линии не дальше допуска.
    /// </summary>
    internal Link? HitTestLink(Point world)
    {
        var tolerance = LinkHitTolerance / Math.Max(ViewportZoom, 0.0001);
        Link? best = null;
        var bestDistance = double.MaxValue;

        foreach (var link in _links)
        {
            // Отсев по рамке, посчитанной при пересчёте концов: она уже включает всю толщину
            // линии, и проход по связям не читает ни одного свойства Avalonia. Толщина нужна
            // только тем немногим, что прошли отсев.
            if (!link.IsResolved || !link.WorldBounds.Inflate(tolerance).Contains(world))
                continue;

            LinkDistanceChecks++;
            var reach = tolerance + (link.StrokeThickness / 2);
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

    internal void OnLinkAttached(Link link) => _links.Add(link);

    internal void OnLinkDetached(Link link)
    {
        _links.Remove(link);

        if (ReferenceEquals(_highlightedLink, link))
            SetHighlightedLink(null);

        if (_selectedLinks.Remove(link))
        {
            link.IsSelected = false;
            PublishLinkSelection();
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.Handled && TryPressLink(e))
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
    private Link? LinkUnderPointer(PointerEventArgs e)
    {
        if (CurrentState is not EditorIdleState || IsOverNode(e.Source))
            return null;

        return HitTestLink(GetWorldPosition(e.GetPosition(this)));
    }

    private static bool IsOverNode(object? source) =>
        source is Visual visual && visual.FindAncestorOfType<Node>(includeSelf: true) != null;

    private void SetHighlightedLink(Link? link)
    {
        if (ReferenceEquals(_highlightedLink, link))
            return;

        _highlightedLink?.SetHighlighted(false);
        _highlightedLink = link;
        link?.SetHighlighted(true);
    }

    private void OnNodeSelectionChanged(object? sender, DesignSelectionChangedEventArgs e)
    {
        if (SelectedDesignTargets.Count > 0)
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
