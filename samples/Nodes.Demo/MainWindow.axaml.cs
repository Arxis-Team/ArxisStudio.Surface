using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ArxisStudio.Surface;
using ArxisStudio.Surface.Nodes;

namespace Nodes.Demo;

/// <summary>
/// Окно демо — хост редактора узлов.
/// </summary>
/// <remarks>
/// Редактор узлов графом не владеет (ADR 0001, 0004): он просит соединить, перецепить и удалить,
/// а выполняет это окно — правит коллекции <see cref="GraphDocument"/> и кладёт правку в ту же
/// историю, что и перетаскивание узлов. Так любой шаг отменяется одним Ctrl + Z, в каком бы
/// порядке его ни делали.
/// </remarks>
public partial class MainWindow : Window
{
    private readonly SurfaceHistory _history;
    private GraphDocument? _document;

    public MainWindow()
    {
        InitializeComponent();

        // История — первой: окно показывает её состояние с первого же события редактора.
        _history = new SurfaceHistory(Editor);
        _history.Changed += (_, _) => UpdateChrome();

        // Канал управления для проверки вживую; без --automation ничего не поднимается. Раньше
        // подписок окна: обход запросов останавливается на выполнившем, и канал, подписанный
        // после окна, не увидел бы ни одного выполненного запроса.
        Automation.AutomationChannel.TryStart(Program.AutomationDirectory, Editor, this);

        Editor.ContainerPrepared += OnContainerPrepared;
        Editor.ConnectValidating += OnConnectValidating;
        Editor.ConnectRequested += OnConnectRequested;
        Editor.ReconnectRequested += OnReconnectRequested;
        Editor.LinkDeleteRequested += OnLinkDeleteRequested;
        Editor.LinkSplitRequested += OnLinkSplitRequested;
        Editor.DeleteRequested += OnDeleteRequested;
        Editor.SurfaceSelectionChanged += (_, _) => UpdateChrome();
        Editor.PropertyChanged += (_, e) =>
        {
            if (e.Property == SurfaceView.ViewportZoomProperty || e.Property == NodeEditor.SelectedLinksProperty)
                UpdateChrome();
        };
    }

    internal GraphDocument Document => _document ?? throw new InvalidOperationException("У окна нет графа.");

    internal SurfaceHistory History => _history;

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_document != null)
        {
            _document.Nodes.CollectionChanged -= OnGraphChanged;
            _document.Links.CollectionChanged -= OnGraphChanged;
        }

        _document = DataContext as GraphDocument;
        if (_document != null)
        {
            _document.Nodes.CollectionChanged += OnGraphChanged;
            _document.Links.CollectionChanged += OnGraphChanged;
        }

        UpdateChrome();
    }

    /// <summary>
    /// Ставит узел туда, где он стоит в модели, когда появляется его контейнер.
    /// </summary>
    /// <remarks>
    /// Не привязкой: перетаскивание пишет положение контейнеру локальным значением, и привязка
    /// стилем проиграла бы ему. Пока контейнер жив, положением распоряжается редактор, а модель
    /// забирает его обратно, когда узел уходит (<see cref="OnDeleteRequested"/>).
    /// </remarks>
    private void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is Node node && Editor.ItemFromContainer(node) is GraphNode model)
            node.Location = model.Location;
    }

    private void OnConnectValidating(object? sender, ConnectValidatingEventArgs e)
    {
        if (e.Source is GraphPort source && e.Target is GraphPort target
            && !Document.CanConnect(source, target, e.Link as GraphLink))
        {
            e.IsAllowed = false;
        }
    }

    private void OnConnectRequested(object? sender, ConnectRequestedEventArgs e)
    {
        if (e.Source is not GraphPort source || e.Target is not GraphPort target)
            return;

        var link = new GraphLink(source, target);
        Document.Links.Add(link);
        _history.Push(ListEdit<GraphLink>.Added(Document.Links, Document.Links.Count - 1, link));
        e.Handled = true;
    }

    private void OnReconnectRequested(object? sender, ReconnectRequestedEventArgs e)
    {
        if (e.Link is not GraphLink before || e.NewPort is not GraphPort port)
            return;

        var index = Document.Links.IndexOf(before);
        if (index < 0)
            return;

        var after = e.End == LinkEnd.Source ? before with { From = port } : before with { To = port };
        Document.Links[index] = after;
        _history.Push(ListEdit<GraphLink>.Replaced(Document.Links, index, before, after));
        e.Handled = true;
    }

    private void OnLinkDeleteRequested(object? sender, LinkDeleteRequestedEventArgs e)
    {
        var edit = ListEdit<GraphLink>.Remove(Document.Links, e.Links.OfType<GraphLink>());
        if (edit.IsEmpty)
            return;

        _history.Push(edit);
        e.Handled = true;
    }

    /// <summary>
    /// Ломает связь перевалкой: вместо одной связи — перевалка и две связи через неё, одной записью
    /// истории.
    /// </summary>
    /// <remarks>
    /// <see cref="LinkSplitRequestedEventArgs.Location"/> — где быть центру перевалки, а узел
    /// ставится левым верхним углом, поэтому из точки вычитается половина её размера. Новые связи
    /// встают на место прежней: порядок коллекции — порядок, в каком хост их видит.
    /// </remarks>
    private void OnLinkSplitRequested(object? sender, LinkSplitRequestedEventArgs e)
    {
        if (e.Link is not GraphLink link)
            return;

        var index = Document.Links.IndexOf(link);
        if (index < 0)
            return;

        var half = Editor.TryFindResource("NodeEditor.Reroute.Size", ActualThemeVariant, out var size) && size is double d ? d / 2 : 0;
        var knot = Document.CreateReroute(e.Location - new Vector(half, half));

        var removed = ListEdit<GraphLink>.Remove(Document.Links, [link]);
        Document.Nodes.Add(knot);
        var added = ListEdit<GraphNode>.Added(Document.Nodes, Document.Nodes.Count - 1, knot);

        var into = new GraphLink(link.From, knot.Inputs[0]);
        var outOf = new GraphLink(knot.Outputs[0], link.To);
        Document.Links.Insert(index, into);
        Document.Links.Insert(index + 1, outOf);
        var rewired = new ListEdit<GraphLink>(Document.Links, [], [(index, into), (index + 1, outOf)]);

        _history.Push(new CompositeChange(removed, added, rewired));
        e.Handled = true;
    }

    /// <summary>
    /// Удаляет выбранные узлы вместе с их связями — одной записью истории.
    /// </summary>
    private void OnDeleteRequested(object? sender, SurfaceDeleteRequestedEventArgs e)
    {
        var nodes = new List<GraphNode>();
        foreach (var target in e.Targets)
        {
            if (target.Container is not Node node || Editor.ItemFromContainer(node) is not GraphNode model || nodes.Contains(model))
                continue;

            // Положение забирается у контейнера: отмена удаления вернёт узел туда, где он стоял.
            model.Location = node.Location;
            nodes.Add(model);
        }

        if (nodes.Count == 0)
            return;

        var links = Document.Links.Where(l => nodes.Contains(l.From.Node) || nodes.Contains(l.To.Node)).ToList();
        var linkEdit = ListEdit<GraphLink>.Remove(Document.Links, links);
        var nodeEdit = ListEdit<GraphNode>.Remove(Document.Nodes, nodes);

        _history.Push(new CompositeChange(linkEdit, nodeEdit));
        e.Handled = true;
    }

    private void OnGraphChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateChrome();

    private void OnUndoClick(object? sender, RoutedEventArgs e) => _history.Undo();

    private void OnRedoClick(object? sender, RoutedEventArgs e) => _history.Redo();

    private void UpdateChrome()
    {
        UndoButton.IsEnabled = _history.CanUndo;
        RedoButton.IsEnabled = _history.CanRedo;
        ZoomText.Text = $"{Editor.ViewportZoom:P0}";

        if (_document == null)
            return;

        StatusText.Text = $"Узлов {_document.Nodes.Count}, связей {_document.Links.Count} · выбрано узлов "
            + $"{Editor.SelectedTargets.Count}, связей {Editor.SelectedLinks.Count}";
    }
}
