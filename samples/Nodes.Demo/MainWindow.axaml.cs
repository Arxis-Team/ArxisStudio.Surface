using System.Collections.Specialized;
using Avalonia;
using Avalonia.Media;
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
    private readonly GraphEvaluation _evaluation = new();
    private readonly Dictionary<Color, LinkPulse> _pulses = new();
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

        Editor.ConnectValidating += OnConnectValidating;
        Editor.ConnectRequested += OnConnectRequested;
        Editor.ReconnectRequested += OnReconnectRequested;
        Editor.LinkDeleteRequested += OnLinkDeleteRequested;
        Editor.LinkSplitRequested += OnLinkSplitRequested;
        Editor.DeleteRequested += OnDeleteRequested;
        Editor.SurfaceSelectionChanged += (_, _) => UpdateChrome();
        _evaluation.LinkFired += OnLinkFired;
        _evaluation.Finished += OnEvaluationFinished;
        Editor.PropertyChanged += (_, e) =>
        {
            if (e.Property == SurfaceView.ViewportZoomProperty || e.Property == NodeEditor.SelectedLinksProperty)
                UpdateChrome();
        };
    }

    internal GraphDocument Document => _document ?? throw new InvalidOperationException("У окна нет графа.");

    internal SurfaceHistory History => _history;

    /// <summary>
    /// Ставит окну новый граф и забывает историю прежнего.
    /// </summary>
    /// <remarks>
    /// История помнит правки коллекций и узлы прежнего документа: отменённая после смены графа, она
    /// правила бы то, чего на холсте уже нет.
    /// </remarks>
    internal void Load(GraphDocument document)
    {
        _evaluation.Stop();
        Editor.ClearLinkPulses();
        DataContext = document;
        _history.Clear();
    }

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

    /// <summary>
    /// Снимает связи, а с ними — узлы перенаправления, оставшиеся без входа или без выхода, одной записью
    /// истории.
    /// </summary>
    private void OnLinkDeleteRequested(object? sender, LinkDeleteRequestedEventArgs e)
    {
        var (links, knots) = Document.Stranded(e.Links.OfType<GraphLink>(), []);
        var linkEdit = ListEdit<GraphLink>.Remove(Document.Links, links);
        if (linkEdit.IsEmpty)
            return;

        var nodeEdit = ListEdit<GraphNode>.Remove(Document.Nodes, knots);
        _history.Push(nodeEdit.IsEmpty ? linkEdit : new CompositeChange(linkEdit, nodeEdit));
        e.Handled = true;
    }

    /// <summary>
    /// Ломает связь узлом перенаправления: вместо одной связи — узел перенаправления и две связи через него, одной записью
    /// истории.
    /// </summary>
    /// <remarks>
    /// <see cref="LinkSplitRequestedEventArgs.Location"/> — где быть центру узла перенаправления, а узел
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
    /// Удаляет выбранные узлы вместе с их связями и повисшими на них узлами перенаправления — одной
    /// записью истории.
    /// </summary>
    /// <remarks>
    /// Положение забирать у контейнера не нужно: привязка уже держит его в модели, и отмена удаления
    /// вернёт узел туда, где он стоял.
    /// </remarks>
    private void OnDeleteRequested(object? sender, SurfaceDeleteRequestedEventArgs e)
    {
        // Весь выбор — элементами: выбранный узел за окном остаётся выбранным без контейнера (ADR 0010
        // библиотеки), и в Targets его нет.
        var nodes = new HashSet<GraphNode>();
        foreach (var item in e.Items)
        {
            if (item is GraphNode model)
                nodes.Add(model);
        }

        if (nodes.Count == 0)
            return;

        var touching = Document.Links.Where(l => nodes.Contains(l.From.Node) || nodes.Contains(l.To.Node));
        var (links, knots) = Document.Stranded(touching, nodes);
        nodes.UnionWith(knots);
        var linkEdit = ListEdit<GraphLink>.Remove(Document.Links, links);
        var nodeEdit = ListEdit<GraphNode>.Remove(Document.Nodes, nodes);

        _history.Push(new CompositeChange(linkEdit, nodeEdit));
        e.Handled = true;
    }

    private void OnGraphChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateChrome();

    private void OnLargeGraphClick(object? sender, RoutedEventArgs e) => Load(GraphDocument.CreateGrid(10_000));

    // --- Волна вычисления и импульсы (ADR 0014 библиотеки) -------------------------------------------

    private void OnEvaluateClick(object? sender, RoutedEventArgs e)
    {
        if (_evaluation.IsRunning)
        {
            RepeatBox.IsChecked = false;
            _evaluation.Stop();
            Editor.ClearLinkPulses();
        }
        else
        {
            _evaluation.Start(Document);
        }

        UpdateChrome();
    }

    /// <summary>
    /// Сработавшая связь — импульс цвета своего типа: свечение — цвет провода, пузыри — он же, осветлённый
    /// к белому: того же цвета, что свечение, они в нём тонут. Вид импульса один на цвет — объект читается
    /// на каждом кадре и не меняется.
    /// </summary>
    private void OnLinkFired(GraphLink link)
    {
        if (link.Color is not { } color)
        {
            Editor.PulseLink(link);
            return;
        }

        if (!_pulses.TryGetValue(color, out var pulse))
        {
            var bubble = Color.FromRgb(Lighten(color.R), Lighten(color.G), Lighten(color.B));
            _pulses[color] = pulse = new LinkPulse { Brush = new SolidColorBrush(bubble), GlowBrush = new SolidColorBrush(color) };
        }

        Editor.PulseLink(link, pulse);

        static byte Lighten(byte channel) => (byte)(channel + ((255 - channel) * 0.6));
    }

    /// <summary>
    /// «Повторять» запускает волну снова, как Event Tick: провода горят, пока граф считается.
    /// </summary>
    private void OnEvaluationFinished()
    {
        if (RepeatBox.IsChecked == true && _document != null)
            _evaluation.Start(_document);

        UpdateChrome();
    }

    private void OnFitClick(object? sender, RoutedEventArgs e) => FitAll();

    /// <summary>
    /// Показывает граф целиком. На большом графе это ниже порога упрощённого вида: узлы рисуются
    /// карточками, и контейнеров нет ни у одного (ADR 0008).
    /// </summary>
    internal void FitAll()
    {
        if (Editor.ItemsExtent is { Width: > 0, Height: > 0 } extent)
            Editor.FitToView(extent);
    }

    private void OnUndoClick(object? sender, RoutedEventArgs e) => _history.Undo();

    private void OnRedoClick(object? sender, RoutedEventArgs e) => _history.Redo();

    private void UpdateChrome()
    {
        UndoButton.IsEnabled = _history.CanUndo;
        RedoButton.IsEnabled = _history.CanRedo;
        EvaluateButton.Content = _evaluation.IsRunning ? "■ Стоп" : "▶ Вычислить";
        ZoomText.Text = $"{Editor.ViewportZoom:P0}";

        if (_document == null)
            return;

        StatusText.Text = $"Узлов {_document.Nodes.Count}, связей {_document.Links.Count} · выбрано узлов "
            + $"{Editor.Selection.Count}, связей {Editor.SelectedLinks.Count}"
            + (_evaluation.IsRunning ? $" · волна {_evaluation.Duration.TotalSeconds:0.0} с" : "")
            + (_evaluation.Skipped > 0 ? $" · в кольцах {_evaluation.Skipped} связей не считаются" : "");
    }
}
