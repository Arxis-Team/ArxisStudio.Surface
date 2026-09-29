using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using ArxisStudio.Surface;
using ArxisStudio.Surface.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nodes.Calculator.Actions;
using Nodes.Calculator.Graph;

namespace Nodes.Calculator;

/// <summary>
/// Строка журнала вывода.
/// </summary>
public sealed class LogLine(string text, IBrush brush)
{
    public string Text { get; } = text;

    public IBrush Brush { get; } = brush;
}

/// <summary>
/// Строка на экране от «Вывести строку»; гаснет по своей длительности.
/// </summary>
public sealed class ScreenLine(string text) : Observable
{
    private double _opacity = 1;

    public string Text { get; } = text;

    public double Opacity
    {
        get => _opacity;
        set => Set(ref _opacity, value);
    }
}

/// <summary>
/// Окно калькулятора — хост редактора узлов.
/// </summary>
/// <remarks>
/// Редактор графом не владеет (ADR 0001, 0004 библиотеки): он просит соединить, перецепить, разрезать и
/// удалить, а окно правит <see cref="CalcDocument"/> по правилам Blueprint и кладёт правку в историю. Так
/// любой шаг — поставленный из меню узел вместе с его проводом и преобразованием, литерал пина, новая
/// переменная — отменяется одним Ctrl + Z.
/// </remarks>
public partial class MainWindow : Window
{
    /// <summary>
    /// Шаг проигрывания прогона: провод выполнения за провод — чтобы порядок было видно, как при отладке
    /// Blueprint с замедлением.
    /// </summary>
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(160);

    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#D6D9DE"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#8A96A8"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#F85149"));

    private readonly SurfaceHistory _history;
    private readonly ActionMenu _menu;
    private readonly DispatcherTimer _player = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = new();
    private readonly Dictionary<Color, LinkPulse> _pulses = new();
    private readonly ObservableCollection<LogLine> _log = new();
    private readonly ObservableCollection<ScreenLine> _screen = new();
    private CalcDocument? _document;
    private Graph.Trace? _trace;
    private int _next;
    private bool _refreshQueued;
    private bool _fitPending;
    private (TextBox Box, string Before)? _editing;

    public MainWindow()
    {
        InitializeComponent();

        _history = new SurfaceHistory(Editor);
        _history.Changed += (_, _) => QueueRefresh();

        _menu = new ActionMenu(this);
        Editor.ContextActionProviders.Add(_menu);
        Editor.ContextPresenter = _menu;
        Palette.Done += (_, _) => PalettePopup.IsOpen = false;

        Editor.ConnectValidating += OnConnectValidating;
        Editor.ConnectRequested += OnConnectRequested;
        Editor.ConnectDropped += OnConnectDropped;
        Editor.ReconnectRequested += OnReconnectRequested;
        Editor.LinkDeleteRequested += OnLinkDeleteRequested;
        Editor.LinkSplitRequested += OnLinkSplitRequested;
        Editor.DeleteRequested += OnDeleteRequested;
        Editor.PropertyChanged += (_, e) =>
        {
            if (e.Property == SurfaceView.ViewportZoomProperty)
                UpdateChrome();
        };

        Opened += (_, _) => FitIfPending();

        LogList.ItemsSource = _log;
        ScreenList.ItemsSource = _screen;
        _player.Tick += (_, _) => PlayTick();

        // Литералы пинов и поля переменных: правка — запись истории, от фокуса до ухода с поля.
        AddHandler(GotFocusEvent, OnFieldFocused, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(LostFocusEvent, OnFieldLostFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(TextBox.TextChangedEvent, (_, _) => QueueRefresh(), RoutingStrategies.Bubble);
        AddHandler(Button.ClickEvent, OnFlagClicked, RoutingStrategies.Bubble, handledEventsToo: true);

        Load(CalcDocument.CreateSample());
    }

    internal CalcDocument Document => _document ?? throw new InvalidOperationException("У окна нет графа.");

    /// <summary>
    /// Ставит окну граф и забывает историю прежнего.
    /// </summary>
    private void Load(CalcDocument document)
    {
        Stop(log: false);
        Editor.ClearLinkPulses();
        DataContext = document;
        _history.Clear();
        QueueRefresh();
        _fitPending = true;
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_document != null)
        {
            _document.Nodes.CollectionChanged -= OnGraphChanged;
            _document.Links.CollectionChanged -= OnGraphChanged;
            _document.Variables.CollectionChanged -= OnGraphChanged;
        }

        _document = DataContext as CalcDocument;
        if (_document != null)
        {
            _document.Nodes.CollectionChanged += OnGraphChanged;
            _document.Links.CollectionChanged += OnGraphChanged;
            _document.Variables.CollectionChanged += OnGraphChanged;
        }
    }

    private void OnGraphChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueRefresh();

    /// <summary>
    /// Живой счёт после любой правки — одним проходом на кадр, сколько бы правок ни пришло.
    /// </summary>
    private void QueueRefresh()
    {
        if (_refreshQueued)
            return;

        _refreshQueued = true;
        Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Background);
    }

    private void Refresh()
    {
        _refreshQueued = false;
        if (_document == null)
            return;

        _document.Retype();
        Evaluator.Preview(_document);
        UpdateChrome();
        FitIfPending();
    }

    /// <summary>
    /// Вписывает новый граф, когда у окна есть размер, а у узлов — значения живого счёта: пузыри
    /// значений расширяют узлы, и вписанный до счёта граф вылезал бы за край.
    /// </summary>
    private void FitIfPending()
    {
        if (!_fitPending || !IsVisible || _refreshQueued)
            return;

        _fitPending = false;
        UpdateLayout();
        FitAll();
    }

    // --- Соединение ----------------------------------------------------------------------------------

    private void OnConnectValidating(object? sender, ConnectValidatingEventArgs e)
    {
        if (e.Source is not CalcPort source || e.Target is not CalcPort target)
            return;

        // Перецепляемый конец узла преобразования не ставит: провод остаётся одним проводом.
        var connection = Document.Check(source, target, e.Link as CalcLink);
        if (connection == Connection.Refused || (e.Link != null && connection != Connection.Direct))
            e.IsAllowed = false;
    }

    private void OnConnectRequested(object? sender, ConnectRequestedEventArgs e)
    {
        if (e.Source is not CalcPort source || e.Target is not CalcPort target || Connect(source, target, around: null) is not { } change)
            return;

        _history.Push(change);
        e.Handled = true;
    }

    /// <summary>
    /// Провод, отпущенный из пина в пустоту, открывает палитру действий для этого пина (ADR 0018
    /// библиотеки).
    /// </summary>
    private void OnConnectDropped(object? sender, ConnectDroppedEventArgs e)
    {
        if (e.Port is not CalcPort port)
            return;

        _menu.BeginDrop(port, e.Location);
        _ = Editor.RequestContextAsync(SurfaceContextSource.Programmatic, e.ViewportPoint);
    }

    /// <summary>
    /// Соединяет выход и вход по правилам Blueprint и возвращает правку, которая это помнит.
    /// </summary>
    /// <remarks>
    /// Провод, занимавший вход данных или выход выполнения, уходит вместе с повисшими на нём изломами.
    /// Разные типы соединяются через узел преобразования: он встаёт посередине между пинами, а у пина
    /// нового, ещё не разложенного узла — посередине до <paramref name="around"/>.
    /// </remarks>
    private ISurfaceChange? Connect(CalcPort source, CalcPort target, Point? around)
    {
        var connection = Document.Check(source, target);
        if (connection == Connection.Refused)
            return null;

        var changes = new List<ISurfaceChange>();
        if (Document.Occupant(source, target) is { } occupant)
            changes.AddRange(Remove([occupant], []));

        if (connection == Connection.Direct)
        {
            changes.Add(ListEdit<CalcLink>.Append(Document.Links, new CalcLink(source, target)));
        }
        else
        {
            var from = AnchorOf(source) ?? around ?? source.Node.Location;
            var to = AnchorOf(target) ?? around ?? target.Node.Location;
            var middle = new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2);
            var conversion = new CalcNode(NodeCatalog.ConversionOf(source.Type, target.Type)!, middle - new Vector(21, 17));
            changes.Add(ListEdit<CalcNode>.Append(Document.Nodes, conversion));
            changes.Add(ListEdit<CalcLink>.Append(Document.Links, new CalcLink(source, conversion.Inputs[0])));
            changes.Add(ListEdit<CalcLink>.Append(Document.Links, new CalcLink(conversion.Outputs[0], target)));
        }

        return changes.Count == 1 ? changes[0] : new CompositeChange([.. changes]);
    }

    private void OnReconnectRequested(object? sender, ReconnectRequestedEventArgs e)
    {
        if (e.Link is not CalcLink before || e.NewPort is not CalcPort port)
            return;

        var (source, target) = e.End == LinkEnd.Source ? (port, before.To) : (before.From, port);
        var changes = new List<ISurfaceChange>();
        if (Document.Occupant(source, target) is { } occupant && !ReferenceEquals(occupant, before))
            changes.AddRange(Remove([occupant], []));

        var index = Document.Links.IndexOf(before);
        if (index < 0)
            return;

        var after = new CalcLink(source, target);
        Document.Links[index] = after;
        changes.Add(ListEdit<CalcLink>.Replaced(Document.Links, index, before, after));
        _history.Push(new CompositeChange([.. changes]));
        e.Handled = true;
    }

    private void OnLinkDeleteRequested(object? sender, LinkDeleteRequestedEventArgs e)
    {
        var changes = Remove(e.Links.OfType<CalcLink>(), []);
        if (changes.Count == 0)
            return;

        _history.Push(new CompositeChange([.. changes]));
        e.Handled = true;
    }

    /// <summary>
    /// Двойной щелчок по проводу ставит излом — узел перенаправления, — одной записью истории.
    /// </summary>
    private void OnLinkSplitRequested(object? sender, LinkSplitRequestedEventArgs e)
    {
        if (e.Link is not CalcLink link)
            return;

        var index = Document.Links.IndexOf(link);
        if (index < 0)
            return;

        var half = Editor.TryFindResource("NodeEditor.Reroute.Size", ActualThemeVariant, out var size) && size is double d ? d / 2 : 0;
        var knot = new RerouteNode(e.Location - new Vector(half, half));

        var removed = ListEdit<CalcLink>.Remove(Document.Links, [link]);
        var added = ListEdit<CalcNode>.Append(Document.Nodes, knot);
        var into = new CalcLink(link.From, knot.Inputs[0]);
        var outOf = new CalcLink(knot.Outputs[0], link.To);
        Document.Links.Insert(index, into);
        Document.Links.Insert(index + 1, outOf);
        var rewired = new ListEdit<CalcLink>(Document.Links, [], [(index, into), (index + 1, outOf)]);

        _history.Push(new CompositeChange(removed, added, rewired));
        e.Handled = true;
    }

    private void OnDeleteRequested(object? sender, SurfaceDeleteRequestedEventArgs e)
    {
        var nodes = e.Items.OfType<CalcNode>().ToList();
        if (nodes.Count == 0)
            return;

        RemoveNodes(nodes);
        e.Handled = true;
    }

    /// <summary>
    /// Снимает провода, а с ними — изломы, оставшиеся без входа или без выхода, и узлы
    /// <paramref name="nodes"/>. Правки уже сделаны; возвращаются, чтобы лечь в историю.
    /// </summary>
    private List<ISurfaceChange> Remove(IEnumerable<CalcLink> links, IReadOnlyCollection<CalcNode> nodes)
    {
        var (gone, knots) = Document.Stranded(links, nodes);
        var changes = new List<ISurfaceChange>();
        var linkEdit = ListEdit<CalcLink>.Remove(Document.Links, gone);
        if (!linkEdit.IsEmpty)
            changes.Add(linkEdit);

        var nodeEdit = ListEdit<CalcNode>.Remove(Document.Nodes, nodes.Concat(knots));
        if (!nodeEdit.IsEmpty)
            changes.Add(nodeEdit);

        return changes;
    }

    private IEnumerable<CalcLink> LinksOf(CalcNode node) =>
        Document.Links.Where(l => ReferenceEquals(l.From.Node, node) || ReferenceEquals(l.To.Node, node)).ToList();

    // --- Действия меню -------------------------------------------------------------------------------

    /// <summary>
    /// Ставит узел из палитры; открытая проводом из пина — подключает его первым пином, способным
    /// принять провод, как Blueprint. Узел, провод и преобразование — одна запись истории.
    /// </summary>
    internal void AddFromMenu(NodeDefinition definition, CalcVariable? variable, Point at, CalcPort? origin)
    {
        // От входа узел встаёт слева от точки: его выход должен смотреть на пин, из которого тянули.
        var location = origin is { IsInput: true } ? at - new Vector(EstimatedWidth(definition), 16) : origin != null ? at - new Vector(0, 16) : at;
        var node = new CalcNode(definition, location, variable);
        var changes = new List<ISurfaceChange> { ListEdit<CalcNode>.Append(Document.Nodes, node) };

        if (origin != null)
        {
            var pair = origin.IsInput
                ? node.Outputs.Where(p => Document.Check(p, origin) != Connection.Refused).Select(p => (Source: p, Target: origin)).FirstOrDefault()
                : node.Inputs.Where(p => Document.Check(origin, p) != Connection.Refused).Select(p => (Source: origin, Target: p)).FirstOrDefault();

            if (pair.Source != null && Connect(pair.Source, pair.Target, at) is { } connect)
                changes.Add(connect);
        }

        _history.Push(changes.Count == 1 ? changes[0] : new CompositeChange([.. changes]));
    }

    private static double EstimatedWidth(NodeDefinition definition) => definition.Look switch
    {
        NodeLook.Conversion => 44,
        NodeLook.Getter => 80,
        NodeLook.Compact => 110,
        _ => 180
    };

    internal void AddPin(CalcNode node)
    {
        var list = node.Definition.ExtraPinIsOutput ? node.Outputs : node.Inputs;
        _history.Push(ListEdit<CalcPort>.Append(list, node.CreateExtraPin()));
    }

    internal bool CanRemovePin(CalcNode node) => node.Definition.ExtraPinIsOutput
        ? node.Outputs.Count > node.Definition.Outputs.Length
        : node.Inputs.Count > node.Definition.Inputs.Length;

    internal void RemoveLastPin(CalcNode node)
    {
        if (!CanRemovePin(node))
            return;

        var list = node.Definition.ExtraPinIsOutput ? node.Outputs : node.Inputs;
        var port = list[^1];
        var changes = Remove(Document.Links.Where(l => ReferenceEquals(l.From, port) || ReferenceEquals(l.To, port)).ToList(), []);
        changes.Add(ListEdit<CalcPort>.Remove(list, [port]));
        _history.Push(new CompositeChange([.. changes]));
    }

    internal void BreakLinks(CalcNode node)
    {
        var changes = Remove(LinksOf(node), []);
        if (changes.Count > 0)
            _history.Push(new CompositeChange([.. changes]));
    }

    internal void RemoveNodes(IReadOnlyCollection<CalcNode> nodes)
    {
        var changes = Remove(Document.Links.Where(l => nodes.Contains(l.From.Node) || nodes.Contains(l.To.Node)).ToList(), nodes);
        if (changes.Count > 0)
            _history.Push(new CompositeChange([.. changes]));
    }

    internal void ShowPalette(
        Point viewportPoint,
        IReadOnlyList<SurfaceContextAction> actions,
        Func<SurfaceContextAction, string> keywords,
        Func<SurfaceContextAction, bool>? fits)
    {
        PalettePopup.PlacementRect = new Rect(viewportPoint, new Size(1, 1));
        Palette.Show(actions, keywords, fits);
        PalettePopup.IsOpen = true;
        Dispatcher.UIThread.Post(Palette.FocusSearch, DispatcherPriority.Loaded);
    }

    private void OnAddPinClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is CalcNode node)
            AddPin(node);
    }

    private void OnAddVariableClick(object? sender, RoutedEventArgs e)
    {
        var number = 1;
        while (Document.Variables.Any(v => v.Name == $"Переменная{number}"))
            number++;

        _history.Push(ListEdit<CalcVariable>.Append(Document.Variables, new CalcVariable($"Переменная{number}", "0")));
    }

    /// <summary>
    /// Центр штырька в мировых координатах — или ничего, если узел ещё не разложен.
    /// </summary>
    private Point? AnchorOf(CalcPort port)
    {
        if (Editor.ContainerFromItem(port.Node) is not Control container)
            return null;

        var view = container.GetVisualDescendants().OfType<Port>().FirstOrDefault(p => ReferenceEquals(p.Data, port));
        if (view == null || view.Bounds.Width <= 0)
            return null;

        var local = new Point(port.IsInput ? 9 : view.Bounds.Width - 9, view.Bounds.Height / 2);
        return view.TranslatePoint(local, Editor) is { } screen ? Editor.GetWorldPosition(screen) : null;
    }

    // --- Литералы и поля -----------------------------------------------------------------------------

    private void OnFieldFocused(object? sender, FocusChangedEventArgs e)
    {
        if (e.Source is TextBox { DataContext: CalcPort or CalcVariable } box && !ReferenceEquals(_editing?.Box, box))
            _editing = (box, box.Text ?? string.Empty);
    }

    private void OnFieldLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_editing is not { } editing || !ReferenceEquals(e.Source, editing.Box))
            return;

        _editing = null;
        Commit(editing.Box, editing.Before, editing.Box.Text ?? string.Empty);
    }

    /// <summary>
    /// Правка поля — одна запись истории. Пустое имя переменной не принимается: узлы остались бы без
    /// названия.
    /// </summary>
    private void Commit(TextBox box, string before, string after)
    {
        if (before == after)
            return;

        switch (box.DataContext)
        {
            case CalcPort port:
                _history.Push(new PropertyChange<string>(value => port.Text = value, before, after));
                break;

            case CalcVariable variable when Equals(box.Tag, "name"):
                if (string.IsNullOrWhiteSpace(after))
                {
                    variable.Name = before;
                    return;
                }

                _history.Push(new PropertyChange<string>(value => variable.Name = value, before, after));
                break;

            case CalcVariable variable:
                _history.Push(new PropertyChange<string>(value => variable.Value = value, before, after));
                break;
        }
    }

    /// <summary>
    /// Флажок логического литерала: щелчок уже переключил его — в историю ложится переключение.
    /// </summary>
    private void OnFlagClicked(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not CheckBox { DataContext: CalcPort port })
            return;

        var after = port.Flag;
        _history.Push(new PropertyChange<bool>(value => port.Flag = value, !after, after));
    }

    /// <summary>
    /// Enter принимает поле, Esc возвращает прежнее значение; F5 — «Играть» откуда угодно.
    /// </summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            TogglePlay();
            e.Handled = true;
            return;
        }

        if (_editing is not { } editing || !ReferenceEquals(e.Source, editing.Box))
            return;

        if (e.Key == Key.Escape)
        {
            editing.Box.Text = editing.Before;
            e.Handled = true;
            Editor.Focus();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Editor.Focus();
        }
    }

    // --- Играть --------------------------------------------------------------------------------------

    private void OnPlayClick(object? sender, RoutedEventArgs e) => TogglePlay();

    /// <summary>
    /// Прогоняет граф и проигрывает прогон: провода вспыхивают импульсами по шагам, «Вывести строку»
    /// пишет на экран и в журнал.
    /// </summary>
    private void TogglePlay()
    {
        if (_player.IsEnabled)
        {
            Stop(log: true);
            return;
        }

        Refresh();
        Editor.ClearLinkPulses();
        _trace = Evaluator.Run(Document);
        _next = 0;
        Log("LogPlayLevel: Запуск", MutedBrush);
        _clock.Restart();
        _player.Start();
        PlayTick();
        UpdateChrome();
    }

    private void PlayTick()
    {
        if (_trace == null)
            return;

        var moment = (int)(_clock.Elapsed / Step);
        while (_next < _trace.Events.Count && _trace.Events[_next].Moment <= moment)
            Apply(_trace.Events[_next++]);

        if (_next < _trace.Events.Count)
            return;

        if (_trace is { Error: { } error, Failed: { } failed })
        {
            Log($"Ошибка: {error}", ErrorBrush);
            failed.Warning = "Бесконечный цикл";
        }

        Stop(log: true);
    }

    private void Apply(TraceEvent step)
    {
        if (step.Link is { } link)
            Pulse(link);

        if (step.Print is not { } print)
            return;

        if (print.ToScreen)
            ShowOnScreen(print.Text, print.Duration);

        if (print.ToLog)
            Log($"LogBlueprintUserMessages: [Калькулятор] {print.Text}", TextBrush);
    }

    private void Stop(bool log)
    {
        if (!_player.IsEnabled)
            return;

        _player.Stop();
        _trace = null;
        if (log)
            Log("LogPlayLevel: Остановлено", MutedBrush);

        UpdateChrome();
    }

    /// <summary>
    /// Сработавший провод — импульс цвета своего типа; выполнение — цвета роли из темы редактора.
    /// </summary>
    private void Pulse(CalcLink link)
    {
        if ((link.Color ?? RoleColor(link.Role)) is not { } color)
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

    private Color? RoleColor(PinRole role) =>
        role == PinRole.Execution && Editor.TryFindResource("NodeEditor.Link.Execution.Stroke", ActualThemeVariant, out var value)
            && value is ISolidColorBrush brush
            ? brush.Color
            : null;

    private void ShowOnScreen(string text, double seconds)
    {
        var line = new ScreenLine(text);
        _screen.Insert(0, line);
        while (_screen.Count > 12)
            _screen.RemoveAt(_screen.Count - 1);

        DispatcherTimer.RunOnce(() =>
        {
            line.Opacity = 0;
            DispatcherTimer.RunOnce(() => _screen.Remove(line), TimeSpan.FromMilliseconds(400));
        }, TimeSpan.FromSeconds(Math.Clamp(seconds, 0.1, 60)));
    }

    private void Log(string text, IBrush brush)
    {
        _log.Add(new LogLine(text, brush));
        Dispatcher.UIThread.Post(LogScroll.ScrollToEnd, DispatcherPriority.Background);
    }

    private void OnClearLogClick(object? sender, RoutedEventArgs e) => _log.Clear();

    // --- Прочее --------------------------------------------------------------------------------------

    private void OnSampleClick(object? sender, RoutedEventArgs e) => Load(CalcDocument.CreateSample());

    private void OnFitClick(object? sender, RoutedEventArgs e) => FitAll();

    /// <summary>
    /// Вписывает граф и довписывает, пока охват не устоится.
    /// </summary>
    /// <remarks>
    /// Узел вне окна живёт без контейнера (ADR 0007 библиотеки), и охват считает его по оценке
    /// размера. Вписанный граф показывает такие узлы, они измеряются, и охват меняется — следующий
    /// проход вписывает уже по нему. Трёх проходов хватает: после второго показано всё.
    /// </remarks>
    internal void FitAll() => Fit(passes: 3);

    private void Fit(int passes)
    {
        if (Editor.ItemsExtent is not { Width: > 0, Height: > 0 } extent)
            return;

        Editor.FitToView(extent.Inflate(24));
        if (passes > 1)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (Editor.ItemsExtent != extent)
                    Fit(passes - 1);
            }, DispatcherPriority.Background);
        }
    }

    private void OnUndoClick(object? sender, RoutedEventArgs e) => _history.Undo();

    private void OnRedoClick(object? sender, RoutedEventArgs e) => _history.Redo();

    private void UpdateChrome()
    {
        UndoButton.IsEnabled = _history.CanUndo;
        RedoButton.IsEnabled = _history.CanRedo;
        PlayButton.Content = _player.IsEnabled ? "■ Стоп" : "▶ Играть";
        ZoomText.Text = $"{Editor.ViewportZoom:P0}";
    }
}
