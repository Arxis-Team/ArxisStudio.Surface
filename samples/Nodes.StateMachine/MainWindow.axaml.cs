using System.Collections.Specialized;
using System.ComponentModel;
using ArxisStudio.Surface;
using ArxisStudio.Surface.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Nodes.StateMachine.Machine;
using Nodes.StateMachine.Preview;

namespace Nodes.StateMachine;

/// <summary>
/// Окно примера — хост редактора узлов, машина состояний и приложение, которым она управляет.
/// </summary>
/// <remarks>
/// Редактор графом не владеет (ADR 0001, 0004 библиотеки): соединить, перецепить и удалить он просит, а
/// выполняет окно — правит коллекции <see cref="MachineDocument"/> и кладёт правку в ту же историю, что
/// и перетаскивание узлов. Контекстное меню (<see cref="MachineContextMenu"/>) правит граф теми же
/// методами окна. Запущенная машина (<see cref="MachineRunner"/>) читает граф живым, а окно показывает
/// её ход на узлах — классом контейнера по <see cref="MachineNode.Look"/>.
/// </remarks>
public partial class MainWindow : Window
{
    private readonly SurfaceHistory _history;
    private readonly MachineContext _context = new();
    private MachineDocument _document = null!;
    private MachineRunner _runner = null!;

    public MainWindow()
    {
        InitializeComponent();

        _history = new SurfaceHistory(Editor);
        _history.Changed += (_, _) => UpdateChrome();

        Editor.ConnectValidating += OnConnectValidating;
        Editor.ConnectRequested += OnConnectRequested;
        Editor.ReconnectRequested += OnReconnectRequested;
        Editor.LinkDeleteRequested += OnLinkDeleteRequested;
        Editor.LinkSplitRequested += OnLinkSplitRequested;
        Editor.DeleteRequested += OnDeleteRequested;
        Editor.ContextActionProviders.Add(new MachineContextMenu(this));
        Editor.SurfaceSelectionChanged += (_, _) => UpdateChrome();
        Editor.PropertyChanged += (_, e) =>
        {
            if (e.Property == SurfaceView.ViewportZoomProperty)
                UpdateChrome();
        };

        // Вид запущенной машины живёт на контейнере узла: развёрнутый заново контейнер берёт его у
        // модели, отданный в пул — теряет.
        Editor.ContainerPrepared += (_, e) => ApplyLook(e.Container);
        Editor.ContainerClearing += (_, e) => e.Container.Classes.RemoveAll(["active", "trace"]);

        NetworkBox.IsCheckedChanged += (_, _) => _context.Online = NetworkBox.IsChecked == true;

        Load(MachineDocument.CreateUiExample());
        Opened += (_, _) =>
        {
            UpdateLayout();
            FitAll();
        };
    }

    internal MachineDocument Document => _document;

    /// <summary>
    /// Ставит окну машину и забывает историю прежней: отменённая после смены графа, она правила бы то,
    /// чего на холсте уже нет.
    /// </summary>
    private void Load(MachineDocument document)
    {
        _runner?.Stop();
        if (_document != null)
        {
            _document.Nodes.CollectionChanged -= OnNodesChanged;
            _document.Links.CollectionChanged -= OnLinksChanged;
            foreach (var node in _document.Nodes)
                node.PropertyChanged -= OnNodeChanged;
        }

        _document = document;
        _document.Nodes.CollectionChanged += OnNodesChanged;
        _document.Links.CollectionChanged += OnLinksChanged;
        foreach (var node in _document.Nodes)
            node.PropertyChanged += OnNodeChanged;

        if (_runner != null)
            _runner.PropertyChanged -= OnRunnerChanged;

        var log = _runner?.Log;
        _runner = new MachineRunner(_document, _context);
        _runner.PropertyChanged += OnRunnerChanged;
        if (log != null)
        {
            foreach (var line in log.Reverse())
                _runner.Log.Insert(0, line);
        }

        PreviewFrame.Child = new AppPreview(_runner);
        LogList.ItemsSource = _runner.Log;
        DataContext = _document;
        _history.Clear();
        UpdateChrome();
    }

    // --- Правки графа: их зовут и редактор, и контекстное меню ------------------------------------

    internal void AddNode(MachineNode node)
    {
        _document.Nodes.Add(node);
        _history.Push(ListEdit<MachineNode>.Added(_document.Nodes, _document.Nodes.Count - 1, node));
    }

    internal void ChangeScreen(StateNode state, AppScreen screen)
    {
        var change = new PropertyChange<AppScreen>(value => state.Screen = value, state.Screen, screen);
        change.Reapply();
        _history.Push(change);
    }

    internal void ChangeTrigger(TransitionNode transition, Trigger trigger)
    {
        var change = new PropertyChange<Trigger>(value => transition.Trigger = value, transition.Trigger, trigger);
        change.Reapply();
        _history.Push(change);
    }

    /// <summary>
    /// Делает состояние начальным: провод входа — к нему; входа нет — окно ставит его слева от
    /// состояния.
    /// </summary>
    internal void MakeInitial(StateNode state)
    {
        var changes = new List<ISurfaceChange>();
        if (_document.Nodes.OfType<EntryNode>().FirstOrDefault() is not { } entry)
        {
            entry = new EntryNode(state.Location - new Vector(220, -10));
            _document.Nodes.Add(entry);
            changes.Add(ListEdit<MachineNode>.Added(_document.Nodes, _document.Nodes.Count - 1, entry));
        }

        changes.Add(ListEdit<FlowLink>.Remove(_document.Links, _document.Links.Where(link => ReferenceEquals(link.From.Node, entry))));
        var link = new FlowLink(entry.Outputs[0], state.Inputs[0]);
        _document.Links.Add(link);
        changes.Add(ListEdit<FlowLink>.Added(_document.Links, _document.Links.Count - 1, link));
        _history.Push(new CompositeChange([.. changes]));
    }

    /// <summary>
    /// Удаляет узлы вместе с их связями — одной записью истории.
    /// </summary>
    internal void RemoveNodes(IEnumerable<MachineNode> nodes)
    {
        var doomed = nodes.ToHashSet();
        if (doomed.Count == 0)
            return;

        var links = _document.Links.Where(l => doomed.Contains(l.From.Node) || doomed.Contains(l.To.Node)).ToList();
        var linkEdit = ListEdit<FlowLink>.Remove(_document.Links, links);
        var nodeEdit = ListEdit<MachineNode>.Remove(_document.Nodes, doomed);
        _history.Push(new CompositeChange(linkEdit, nodeEdit));
    }

    /// <summary>
    /// Показывает машину целиком.
    /// </summary>
    internal void FitAll()
    {
        if (Editor.ItemsExtent is { Width: > 0, Height: > 0 } extent)
            Editor.FitToView(extent);
    }

    // --- Запросы редактора ---------------------------------------------------------------------------

    private void OnConnectValidating(object? sender, ConnectValidatingEventArgs e)
    {
        if (e.Source is FlowPort source && e.Target is FlowPort target
            && !_document.CanConnect(source, target, e.Link as FlowLink))
        {
            e.IsAllowed = false;
        }
    }

    private void OnConnectRequested(object? sender, ConnectRequestedEventArgs e)
    {
        if (e.Source is not FlowPort source || e.Target is not FlowPort target)
            return;

        var link = new FlowLink(source, target);
        _document.Links.Add(link);
        _history.Push(ListEdit<FlowLink>.Added(_document.Links, _document.Links.Count - 1, link));
        e.Handled = true;
    }

    private void OnReconnectRequested(object? sender, ReconnectRequestedEventArgs e)
    {
        if (e.Link is not FlowLink before || e.NewPort is not FlowPort port)
            return;

        var index = _document.Links.IndexOf(before);
        if (index < 0)
            return;

        var after = e.End == LinkEnd.Source ? new FlowLink(port, before.To) : new FlowLink(before.From, port);
        _document.Links[index] = after;
        _history.Push(ListEdit<FlowLink>.Replaced(_document.Links, index, before, after));
        e.Handled = true;
    }

    private void OnLinkDeleteRequested(object? sender, LinkDeleteRequestedEventArgs e)
    {
        var edit = ListEdit<FlowLink>.Remove(_document.Links, e.Links.OfType<FlowLink>());
        if (edit.IsEmpty)
            return;

        _history.Push(edit);
        e.Handled = true;
    }

    /// <summary>
    /// Ломает провод узлом перенаправления — как у демо узлов: вместо одной связи узел перенаправления
    /// и две связи через него.
    /// </summary>
    private void OnLinkSplitRequested(object? sender, LinkSplitRequestedEventArgs e)
    {
        if (e.Link is not FlowLink link)
            return;

        var index = _document.Links.IndexOf(link);
        if (index < 0)
            return;

        var half = Editor.TryFindResource("NodeEditor.Reroute.Size", ActualThemeVariant, out var size) && size is double d ? d / 2 : 0;
        var knot = new RerouteNode(e.Location - new Vector(half, half));

        var removed = ListEdit<FlowLink>.Remove(_document.Links, [link]);
        _document.Nodes.Add(knot);
        var added = ListEdit<MachineNode>.Added(_document.Nodes, _document.Nodes.Count - 1, knot);

        var into = new FlowLink(link.From, knot.Inputs[0]);
        var outOf = new FlowLink(knot.Outputs[0], link.To);
        _document.Links.Insert(index, into);
        _document.Links.Insert(index + 1, outOf);
        var rewired = new ListEdit<FlowLink>(_document.Links, [], [(index, into), (index + 1, outOf)]);

        _history.Push(new CompositeChange(removed, added, rewired));
        e.Handled = true;
    }

    private void OnDeleteRequested(object? sender, SurfaceDeleteRequestedEventArgs e)
    {
        var nodes = e.Items.OfType<MachineNode>().ToList();
        if (nodes.Count == 0)
            return;

        RemoveNodes(nodes);
        e.Handled = true;
    }

    // --- Вид запущенной машины ------------------------------------------------------------------------

    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var node in e.OldItems?.OfType<MachineNode>() ?? [])
            node.PropertyChanged -= OnNodeChanged;

        foreach (var node in e.NewItems?.OfType<MachineNode>() ?? [])
            node.PropertyChanged += OnNodeChanged;

        UpdateChrome();
    }

    private void OnLinksChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateChrome();

    private void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MachineNode.Look) && sender is MachineNode node && Editor.ContainerFromItem(node) is { } container)
            ApplyLook(container);
    }

    private void ApplyLook(Control container)
    {
        var look = (container.DataContext as MachineNode)?.Look ?? RunLook.None;
        container.Classes.Set("active", look == RunLook.Active);
        container.Classes.Set("trace", look == RunLook.Trace);
    }

    private void OnRunnerChanged(object? sender, PropertyChangedEventArgs e) => UpdateChrome();

    // --- Кнопки --------------------------------------------------------------------------------------

    private void OnRunClick(object? sender, RoutedEventArgs e)
    {
        if (_runner.IsRunning)
            _runner.Stop();
        else
            _runner.Start();
    }

    private void OnExampleClick(object? sender, RoutedEventArgs e)
    {
        Load(MachineDocument.CreateUiExample());
        UpdateLayout();
        FitAll();
    }

    private void OnFitClick(object? sender, RoutedEventArgs e) => FitAll();

    private void OnUndoClick(object? sender, RoutedEventArgs e) => _history.Undo();

    private void OnRedoClick(object? sender, RoutedEventArgs e) => _history.Redo();

    private void UpdateChrome()
    {
        UndoButton.IsEnabled = _history.CanUndo;
        RedoButton.IsEnabled = _history.CanRedo;
        ZoomText.Text = $"{Editor.ViewportZoom:P0}";

        if (_runner == null)
            return;

        RunButton.Content = _runner.IsRunning ? "■ Остановить" : "▶ Запустить";
        var running = _runner.Current is { } current ? $" · активно: {current.Title}" : string.Empty;
        StatusText.Text = $"Узлов {_document.Nodes.Count}, связей {_document.Links.Count} · выбрано {Editor.Selection.Count}{running}";
    }
}
