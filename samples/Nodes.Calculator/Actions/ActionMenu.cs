using System.Windows.Input;
using ArxisStudio.Surface;
using Avalonia;
using Nodes.Calculator.Graph;

namespace Nodes.Calculator.Actions;

/// <summary>
/// Меню действий холста — и поставщик действий библиотеки, и их показ.
/// </summary>
/// <remarks>
/// Правая кнопка по пустому холсту и провод, отпущенный из пина в пустоту (<c>ConnectDropped</c>, ADR
/// 0018 библиотеки), открывают палитру «Все действия для этого Blueprint» с поиском, как в Unreal Engine.
/// Из пина палитра по умолчанию «с учётом контекста»: только узлы, у которых есть пин, способный принять
/// этот провод напрямую или через преобразование, и новый узел встаёт подключённым. Правая кнопка по
/// узлу — обычное контекстное меню библиотеки: удалить, разорвать связи, добавить и убрать пин.
/// </remarks>
internal sealed class ActionMenu(MainWindow window) : ISurfaceContextActionProvider, ISurfaceContextPresenter
{
    private readonly ContextMenuContextPresenter _menu = new();
    private readonly Dictionary<SurfaceContextAction, string> _keywords = new();
    private readonly HashSet<SurfaceContextAction> _fitting = new();
    private CalcPort? _origin;
    private CalcPort? _shown;
    private Point _dropPoint;

    /// <summary>
    /// Провод отпущен из пина в пустоту: следующее меню — действия для этого пина в этой точке.
    /// </summary>
    public void BeginDrop(CalcPort origin, Point location)
    {
        _origin = origin;
        _dropPoint = location;
    }

    public ValueTask<IReadOnlyList<SurfaceContextAction>> GetActionsAsync(
        SurfaceView editor, SurfaceContextRequest request, CancellationToken cancellationToken = default)
    {
        _keywords.Clear();
        _fitting.Clear();

        // Бросок живёт одно меню: отменённый запрос не должен оставить его следующей правой кнопке.
        _shown = _origin;
        _origin = null;
        if (_shown != null)
            return ValueTask.FromResult(ForSurface(_dropPoint, _shown));

        var node = request.Target?.Container is { } container ? editor.ItemFromContainer(container) as CalcNode : null;
        return ValueTask.FromResult(node != null ? ForNode(node) : ForSurface(request.WorldPoint, null));
    }

    public bool TryShow(SurfaceView editor, SurfaceContextRequest request, IReadOnlyList<SurfaceContextAction> actions)
    {
        var origin = _shown;
        _shown = null;
        if (origin == null && request.Target != null)
            return _menu.TryShow(editor, request, actions);

        window.ShowPalette(request.ViewportPoint, actions, KeywordsOf, origin != null ? _fitting.Contains : null);
        return true;
    }

    private string KeywordsOf(SurfaceContextAction action) => _keywords.GetValueOrDefault(action, string.Empty);

    /// <summary>
    /// Все действия по разделам каталога, переменные — последним разделом.
    /// </summary>
    private IReadOnlyList<SurfaceContextAction> ForSurface(Point at, CalcPort? origin)
    {
        var roots = new List<SurfaceContextAction>();
        var sections = new Dictionary<string, List<SurfaceContextAction>>();

        List<SurfaceContextAction> Section(string path)
        {
            if (sections.TryGetValue(path, out var items))
                return items;

            items = new List<SurfaceContextAction>();
            sections[path] = items;
            var cut = path.LastIndexOf('|');
            var parent = cut < 0 ? roots : Section(path[..cut]);
            parent.Add(new SurfaceContextAction { Header = path[(cut + 1)..], Items = items });
            return items;
        }

        var hasBegin = window.Document.Nodes.Any(n => n.Definition == NodeCatalog.Startup);
        foreach (var definition in NodeCatalog.All)
        {
            // Событие запуска у Blueprint одно на граф, как Begin Play.
            var enabled = definition != NodeCatalog.Startup || !hasBegin;
            Section(definition.Category).Add(Leaf(definition, null, at, origin, enabled));
        }

        foreach (var variable in window.Document.Variables)
        {
            foreach (var definition in NodeCatalog.ForVariables)
                Section(NodeCatalog.Get.Category).Add(Leaf(definition, variable, at, origin, enabled: true));
        }

        return roots;
    }

    private SurfaceContextAction Leaf(NodeDefinition definition, CalcVariable? variable, Point at, CalcPort? origin, bool enabled)
    {
        var title = variable == null ? definition.Title : (definition == NodeCatalog.Get ? "Получить {0}" : definition.Title).Replace("{0}", variable.Name);
        var action = new SurfaceContextAction
        {
            Header = title,
            Icon = Glyph(definition),
            IsEnabled = enabled,
            Command = new Command(() => window.AddFromMenu(definition, variable, at, origin))
        };

        _keywords[action] = $"{definition.Keywords} {definition.Symbol} {definition.Category.Replace('|', ' ')}";
        if (origin != null && Fits(definition, origin))
            _fitting.Add(action);

        return action;
    }

    /// <summary>
    /// Значок строки — как в палитре Blueprint: знак операции, «ƒ» функции, «◆» события.
    /// </summary>
    private static string Glyph(NodeDefinition definition) => definition.Look switch
    {
        NodeLook.Event => "◆",
        NodeLook.Function or NodeLook.Pure => "ƒ",
        NodeLook.Flow => "⇉",
        NodeLook.Getter => "●",
        _ => definition.Symbol ?? "•"
    };

    /// <summary>
    /// Есть ли у узла пин, который примет провод из <paramref name="origin"/>.
    /// </summary>
    private static bool Fits(NodeDefinition definition, CalcPort origin) => origin.IsInput
        ? definition.Outputs.Any(spec => Accepts(spec.Type, origin.Type))
        : definition.Inputs.Any(spec => Accepts(origin.Type, spec.Type));

    private static bool Accepts(PinType from, PinType to)
    {
        if (from == PinType.Wildcard || to == PinType.Wildcard)
            return true;

        if (from == PinType.Exec || to == PinType.Exec)
            return from == to;

        return from == to || NodeCatalog.ConversionOf(from, to) != null;
    }

    private IReadOnlyList<SurfaceContextAction> ForNode(CalcNode node)
    {
        var actions = new List<SurfaceContextAction>();
        if (node.CanAddPin)
        {
            actions.Add(Item("Добавить пин", () => window.AddPin(node)));
            actions.Add(Item("Убрать последний пин", () => window.RemoveLastPin(node), window.CanRemovePin(node)));
            actions.Add(new SurfaceContextAction { IsSeparator = true });
        }

        actions.Add(Item("Разорвать все связи", () => window.BreakLinks(node), window.Document.Links.Any(l => ReferenceEquals(l.From.Node, node) || ReferenceEquals(l.To.Node, node))));
        actions.Add(Item("Удалить", () => window.RemoveNodes([node])));
        return actions;
    }

    private static SurfaceContextAction Item(string header, Action action, bool enabled = true) =>
        new() { Header = header, Command = new Command(action), IsEnabled = enabled };

    private sealed class Command(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => action();
    }
}
