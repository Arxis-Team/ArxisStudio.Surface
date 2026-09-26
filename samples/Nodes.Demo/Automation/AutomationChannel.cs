using System.Text.Json;
using Avalonia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ArxisStudio.Surface.Nodes;

namespace Nodes.Demo.Automation;

/// <summary>
/// Канал управления демо для проверки редактора узлов вживую.
/// </summary>
/// <remarks>
/// Тот же приём, что у демо дизайнера форм: команда — файл <c>command.json</c> в каталоге, ответ —
/// <c>response.json</c>, исполняется на UI-потоке. Отвечает числами — где концы связей, какие
/// порты заняты, что выбрано и какие запросы ушли приложению, — а не картинкой.
/// <para>
/// Включается только аргументом <c>--automation &lt;каталог&gt;</c>. Без него ни таймера, ни
/// файлов, ни подписок.
/// </para>
/// <para>
/// Канал подписывается на запросы редактора раньше окна: обход подписчиков останавливается на
/// первом выполнившем, и подписанный после окна канал не увидел бы ни одного выполненного запроса.
/// </para>
/// </remarks>
internal sealed class AutomationChannel
{
    private const string CommandFile = "command.json";
    private const string ResponseFile = "response.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _directory;
    private readonly NodeEditor _editor;
    private readonly MainWindow _window;
    private readonly List<Dictionary<string, object?>> _events = new();

    private AutomationChannel(string directory, NodeEditor editor, MainWindow window)
    {
        _directory = directory;
        _editor = editor;
        _window = window;
    }

    /// <summary>
    /// Каталог канала из аргументов запуска, либо <see langword="null"/>.
    /// </summary>
    public static string? DirectoryFromArguments(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--automation", StringComparison.Ordinal))
                return args[i + 1];
        }

        return null;
    }

    /// <summary>
    /// Поднимает канал, если демо запущено с <c>--automation</c>. Звать до подписок окна.
    /// </summary>
    public static void TryStart(string? directory, NodeEditor editor, MainWindow window)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;

        Directory.CreateDirectory(directory);
        new AutomationChannel(directory, editor, window).Start();
    }

    private void Start()
    {
        _editor.ConnectRequested += (_, e) => Record("ConnectRequested", new()
        {
            ["source"] = Name(e.Source),
            ["target"] = Name(e.Target)
        });
        _editor.ReconnectRequested += (_, e) => Record("ReconnectRequested", new()
        {
            ["link"] = Name(e.Link),
            ["end"] = e.End.ToString(),
            ["oldPort"] = Name(e.OldPort),
            ["newPort"] = Name(e.NewPort)
        });
        _editor.LinkDeleteRequested += (_, e) => Record("LinkDeleteRequested", new()
        {
            ["links"] = e.Links.Select(Name).ToList()
        });
        _editor.DeleteRequested += (_, e) => Record("DeleteRequested", new()
        {
            ["nodes"] = e.Targets.Select(t => Name(_editor.ItemFromContainer(t.Container))).ToList()
        });
        _editor.EditCompleted += (_, e) => Record("EditCompleted", new()
        {
            ["kind"] = e.Kind.ToString(),
            ["targets"] = e.Changes.Select(c => Name(_editor.ItemFromContainer(c.Target))).ToList()
        });

        // Опрос, а не FileSystemWatcher: команда обязана исполниться на UI-потоке, и таймер
        // диспетчера это гарантирует без ручной переброски.
        new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => Poll()).Start();
    }

    private void Poll()
    {
        var path = Path.Combine(_directory, CommandFile);
        if (!File.Exists(path))
            return;

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException)
        {
            // Файл ещё пишут — попробуем на следующем тике.
            return;
        }

        File.Delete(path);

        Dictionary<string, object?> response;
        try
        {
            using var document = JsonDocument.Parse(text);
            response = Execute(document.RootElement);
            response["ok"] = true;
        }
        catch (Exception exception)
        {
            response = new()
            {
                ["ok"] = false,
                ["error"] = exception.GetType().Name + ": " + exception.Message
            };
        }

        File.WriteAllText(Path.Combine(_directory, ResponseFile), JsonSerializer.Serialize(response, Json));
    }

    private Dictionary<string, object?> Execute(JsonElement command)
    {
        var name = command.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;

        int Number(string key, int fallback = 0) =>
            command.TryGetProperty(key, out var value) && value.TryGetInt32(out var parsed) ? parsed : fallback;

        double? Real(string key) =>
            command.TryGetProperty(key, out var value) && value.TryGetDouble(out var parsed) ? parsed : null;

        bool Flag(string key) =>
            command.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;

        switch (name)
        {
            case "state":
                return State();

            case "selectLink":
            {
                var links = _window.Document.Links;
                var index = Number("index", -1);
                if (index < 0 || index >= links.Count)
                    throw new ArgumentOutOfRangeException("index", $"связей {links.Count}");

                return new() { ["returned"] = _editor.SelectLink(links[index], Flag("additive")) };
            }

            case "clearLinkSelection":
                _editor.ClearLinkSelection();
                return new();

            case "events":
            {
                var events = _events.ToList();
                if (Flag("clear"))
                    _events.Clear();

                return new() { ["events"] = events };
            }

            case "viewport":
                if (Real("zoom") is { } zoom)
                    _editor.ViewportZoom = zoom;

                if (Real("x") is { } x && Real("y") is { } y)
                    _editor.ViewportLocation = new Point(x, y);

                return new() { ["zoom"] = _editor.ViewportZoom, ["location"] = PointOf(_editor.ViewportLocation) };

            case "undo":
                return new() { ["returned"] = _window.History.Undo() };

            case "redo":
                return new() { ["returned"] = _window.History.Redo() };

            default:
                throw new ArgumentException($"Неизвестная команда: {name}. Есть: state, selectLink, clearLinkSelection, events, viewport, undo, redo.");
        }
    }

    private Dictionary<string, object?> State()
    {
        var document = _window.Document;
        var selectedNodes = _editor.SelectedDesignTargets
            .Select(t => _editor.ItemFromContainer(t.Container))
            .ToHashSet();

        var nodes = document.Nodes.Select(model =>
        {
            var node = _editor.ContainerFromItem(model) as Node;
            return new Dictionary<string, object?>
            {
                ["title"] = model.Title,
                ["location"] = node == null ? null : PointOf(node.Location),
                ["size"] = node == null ? null : new { width = node.Bounds.Width, height = node.Bounds.Height },
                ["selected"] = selectedNodes.Contains(model)
            };
        }).ToList();

        var linkControls = _editor.GetVisualDescendants().OfType<Link>().ToList();
        var links = document.Links.Select((item, index) =>
        {
            var link = linkControls.FirstOrDefault(l => ReferenceEquals(l.DataContext, item));
            return new Dictionary<string, object?>
            {
                ["index"] = index,
                ["link"] = item.ToString(),
                ["visible"] = link?.IsVisible,
                ["selected"] = link?.IsSelected,
                ["sourceAnchor"] = link == null ? null : PointOf(link.SourceAnchor),
                ["targetAnchor"] = link == null ? null : PointOf(link.TargetAnchor)
            };
        }).ToList();

        var ports = _editor.GetVisualDescendants().OfType<Port>()
            .Select(port => new Dictionary<string, object?>
            {
                ["port"] = Name(port.Data),
                ["direction"] = port.Direction.ToString(),
                ["connected"] = port.IsConnected
            })
            .ToList();

        return new()
        {
            ["viewport"] = new { zoom = _editor.ViewportZoom, location = PointOf(_editor.ViewportLocation) },
            ["nodes"] = nodes,
            ["links"] = links,
            ["ports"] = ports,
            ["selectedLinks"] = _editor.SelectedLinks.Select(Name).ToList(),
            ["canUndo"] = _window.History.CanUndo,
            ["canRedo"] = _window.History.CanRedo
        };
    }

    private void Record(string name, Dictionary<string, object?> data)
    {
        data["event"] = name;
        _events.Add(data);
    }

    private static string? Name(object? item) => item?.ToString();

    private static object PointOf(Point point) => new { x = Math.Round(point.X, 2), y = Math.Round(point.Y, 2) };
}
