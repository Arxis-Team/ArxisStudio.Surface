using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ArxisStudio;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Surface;
using UiDesigner.Demo.ViewModels;

namespace UiDesigner.Demo.Automation;

/// <summary>
/// Канал управления демо для проверки API вживую.
/// </summary>
/// <remarks>
/// Headless-тесты проверяют поведение, но не отвечают на вопрос «а что видит и получает
/// живое приложение». Проверять же живое приложение кликами по координатам — значит читать
/// результат глазами по скриншоту: так проверяется рендер, но не значения.
/// <para>
/// Канал даёт третий способ: команда с аргументами и ответ с точными числами. Транспорт —
/// два файла в каталоге, без сокетов и без сети вообще. Команды исполняются на UI-потоке,
/// потому что именно там живёт API редактора.
/// </para>
/// <para>
/// Включается только аргументом <c>--automation &lt;каталог&gt;</c>. Без него ни таймера,
/// ни файлов, ни подписок — демо остаётся демо.
/// </para>
/// </remarks>
internal sealed class AutomationChannel
{
    private const string CommandFile = "command.json";
    private const string ResponseFile = "response.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _directory;
    private readonly ArxisStudio.Surface.UiDesigner.UiDesignerView _editor;
    private readonly Window _window;
    private readonly List<Dictionary<string, object?>> _events = new();
    private DispatcherTimer? _timer;

    private AutomationChannel(string directory, ArxisStudio.Surface.UiDesigner.UiDesignerView editor, Window window)
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
    /// Поднимает канал, если демо запущено с <c>--automation</c>.
    /// </summary>
    public static void TryStart(string? directory, ArxisStudio.Surface.UiDesigner.UiDesignerView editor, Window window)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;

        Directory.CreateDirectory(directory);
        var channel = new AutomationChannel(directory, editor, window);
        channel.Start();
    }

    private void Start()
    {
        _editor.SurfaceSelectionChanged += (_, e) => Record("SurfaceSelectionChanged", new Dictionary<string, object?>
        {
            ["added"] = e.Added.Select(Describe).ToList(),
            ["removed"] = e.Removed.Select(Describe).ToList(),
            ["targets"] = e.NewTargets.Select(Describe).ToList(),
            ["isPrimaryChanged"] = e.IsPrimaryChanged,
            ["newPrimary"] = e.NewPrimary == null ? null : Describe(e.NewPrimary)
        });

        _editor.EditCompleted += (_, e) => Record("EditCompleted", new Dictionary<string, object?>
        {
            ["kind"] = e.Kind.ToString(),
            ["changes"] = e.Changes.Select(change => new Dictionary<string, object?>
            {
                ["type"] = change.GetType().Name,
                ["target"] = NameOf(change.Target)
            }).ToList()
        });

        _editor.ReorderRequested += (_, e) => Record("ReorderRequested", new Dictionary<string, object?>
        {
            ["target"] = NameOf(e.Target),
            ["oldIndex"] = e.OldIndex,
            ["newIndex"] = e.NewIndex,
            ["anchor"] = e.Anchor == null ? null : NameOf(e.Anchor)
        });

        _editor.DeleteRequested += (_, e) => Record("DeleteRequested", new Dictionary<string, object?>
        {
            ["targets"] = e.Targets.Select(Describe).ToList()
        });

        // Опрос, а не FileSystemWatcher: команда обязана исполниться на UI-потоке,
        // а таймер диспетчера это гарантирует без ручной переброски.
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => Poll());
        _timer.Start();
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
            response = new Dictionary<string, object?>
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

        string Text(string key, string fallback = "") =>
            command.TryGetProperty(key, out var value) ? value.GetString() ?? fallback : fallback;

        int Number(string key, int fallback = 0) =>
            command.TryGetProperty(key, out var value) && value.TryGetInt32(out var parsed) ? parsed : fallback;

        bool Flag(string key) =>
            command.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;

        switch (name)
        {
            case "state":
                return State();

            case "tree":
                return Tree();

            case "select":
            {
                var control = Resolve(Text("target"));
                var applied = _editor.SelectTarget(control, Flag("additive"));
                return Result(new Dictionary<string, object?> { ["returned"] = applied });
            }

            case "selectContainer":
            {
                var container = ContainerAt(Number("index"));
                var applied = _editor.SelectTarget(container, Flag("additive"));
                return Result(new Dictionary<string, object?> { ["returned"] = applied });
            }

            case "clearSelection":
                _editor.SelectedItems?.Clear();
                return Result();

            case "setSelectedIndex":
                _editor.SelectedIndex = Number("index", -1);
                return Result();

            case "addSelectedItem":
            {
                var item = _editor.Items[Number("index")];
                _editor.SelectedItems?.Add(item);
                return Result();
            }

            case "nudge":
            {
                var key = Enum.Parse<Key>(Text("key", "Right"), ignoreCase: true);
                var modifiers = Text("modifiers").Equals("shift", StringComparison.OrdinalIgnoreCase)
                    ? KeyModifiers.Shift
                    : KeyModifiers.None;

                _editor.Focus();
                for (var i = 0; i < Math.Max(1, Number("times", 1)); i++)
                {
                    _editor.RaiseEvent(new KeyEventArgs
                    {
                        RoutedEvent = InputElement.KeyDownEvent,
                        Key = key,
                        KeyModifiers = modifiers
                    });
                }

                return Result();
            }

            case "zorder":
            {
                var applied = Text("op") switch
                {
                    "front" => _editor.BringToFront(),
                    "back" => _editor.SendToBack(),
                    "forward" => _editor.BringForward(),
                    "backward" => _editor.SendBackward(),
                    _ => throw new ArgumentException("op: front|back|forward|backward")
                };

                return Result(new Dictionary<string, object?> { ["returned"] = applied });
            }

            case "viewport":
                _editor.ViewportZoom = command.TryGetProperty("zoom", out var zoom) ? zoom.GetDouble() : _editor.ViewportZoom;
                return Result();

            case "guides":
            {
                // Набор направляющих читается у самого редактора: так видно то же,
                // что видит хост, а не то, что нарисовано.
                var list = (_editor.Guides ?? Enumerable.Empty<ArxisStudio.Surface.Editing.SurfaceGuide>())
                    .Select(g => new Dictionary<string, object?>
                    {
                        ["orientation"] = g.Orientation.ToString(),
                        ["position"] = g.Position
                    })
                    .ToList();

                return new Dictionary<string, object?> { ["guides"] = list };
            }

            case "group":
            {
                var applied = Text("op") switch
                {
                    "group" => _editor.GroupSelection(),
                    "ungroup" => _editor.UngroupSelection(),
                    _ => throw new ArgumentException("op: group|ungroup")
                };

                return Result(new Dictionary<string, object?> { ["returned"] = applied });
            }

            case "groups":
            {
                // Состав читается у редактора тем же запросом, что доступен хосту:
                // отвечает на вопрос «что получает живой потребитель», а не «что нарисовано».
                var container = ContainerAt(Number("index"));
                var list = _editor.GetGroups(container)
                    .Select(g => new Dictionary<string, object?>
                    {
                        ["id"] = g.Id,
                        ["container"] = NameOf(g.Container),
                        ["members"] = g.Members.Select(NameOf).ToList()
                    })
                    .ToList();

                return new Dictionary<string, object?> { ["groups"] = list };
            }

            case "events":
            {
                var drained = _events.Select(entry => entry).ToList();
                _events.Clear();
                return new Dictionary<string, object?> { ["events"] = drained };
            }

            default:
                throw new ArgumentException($"неизвестная команда: {name ?? "<null>"}");
        }
    }

    /// <summary>
    /// Ответ команды: её собственный результат плюс состояние после исполнения.
    /// </summary>
    private Dictionary<string, object?> Result(Dictionary<string, object?>? own = null)
    {
        var response = own ?? new Dictionary<string, object?>();
        response["state"] = State();
        return response;
    }

    private Dictionary<string, object?> State() => new()
    {
        ["selectedItemsCount"] = _editor.SelectedItems?.Count ?? 0,
        ["selectedIndex"] = _editor.SelectedIndex,
        ["selectionIndexes"] = _editor.Selection.SelectedIndexes.ToList(),
        ["selectedTargetsCount"] = _editor.SelectedTargetsCount,
        ["selectedTargets"] = _editor.SelectedTargets.Select(Describe).ToList(),
        ["primary"] = _editor.PrimarySelectionTarget == null ? null : Describe(_editor.PrimarySelectionTarget),
        ["selectionBounds"] = Rect(_editor.SelectionBounds),
        ["hasSingleSelection"] = _editor.HasSingleSelection,
        ["hasMultipleSelection"] = _editor.HasMultipleSelection,
        ["hasMultipleContainerSelection"] = _editor.HasMultipleContainerSelection,
        ["hasMultipleNestedSelection"] = _editor.HasMultipleNestedSelection,
        ["isSelecting"] = _editor.IsSelecting,
        ["isReordering"] = _editor.IsReordering,
        ["marqueeScope"] = _editor.MarqueeScope == null ? null : NameOf(_editor.MarqueeScope),
        ["viewportZoom"] = _editor.ViewportZoom,
        ["viewportLocation"] = new[] { _editor.ViewportLocation.X, _editor.ViewportLocation.Y },
        ["designer"] = Designer()
    };

    /// <summary>
    /// Что держит дизайнер: вкладки с текстом документа и выбором каждой, активная, масштаб, типы проекта.
    /// </summary>
    /// <remarks>
    /// Этим отвечает копия, поднятая перезапуском: проверка спрашивает, приняла ли она сессию прежней —
    /// вкладки, несохранённый текст, выбор и масштаб, — а не «похоже ли окно на прежнее».
    /// </remarks>
    private Dictionary<string, object?>? Designer()
    {
        if (_window.DataContext is not DesignerViewModel designer)
            return null;

        return new Dictionary<string, object?>
        {
            ["forms"] = designer.Forms.Select(form => new Dictionary<string, object?>
            {
                ["file"] = form.File.Value,
                ["dirty"] = form.IsDirty,
                ["text"] = form.Document?.SourceText.ToString(),
                ["selected"] = form.SelectedPath?.ToString()
            }).ToList(),
            ["active"] = designer.ActiveForm?.File.Value,
            ["selected"] = designer.Selected is { IsPropertyElementSyntax: false } element
                ? ArxisStudio.Markup.Xaml.XamlElementPath.Of(element).ToString()
                : null,
            ["zoom"] = designer.Zoom,
            ["types"] = designer.TypesState.ToString(),
            ["generation"] = designer.Host?.GenerationName
        };
    }

    /// <summary>
    /// Дерево контейнеров и их авторских контролов — чтобы адресовать target по имени.
    /// </summary>
    private Dictionary<string, object?> Tree()
    {
        var containers = new List<Dictionary<string, object?>>();

        for (var i = 0; i < _editor.ItemCount; i++)
        {
            if (_editor.ContainerFromIndex(i) is not UiDesignerItem container)
                continue;

            containers.Add(new Dictionary<string, object?>
            {
                ["index"] = i,
                ["contentMode"] = container.ContentMode.ToString(),
                ["controls"] = container.GetVisualDescendants()
                    .OfType<Control>()
                    .Where(control => !string.IsNullOrEmpty(control.Name))
                    .Select(control => new Dictionary<string, object?>
                    {
                        ["name"] = control.Name,
                        ["type"] = control.GetType().Name
                    })
                    .ToList()
            });
        }

        return new Dictionary<string, object?> { ["containers"] = containers };
    }

    private Control Resolve(string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("нужен target");

        var found = _editor.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(control => string.Equals(control.Name, name, StringComparison.Ordinal));

        return found ?? throw new ArgumentException($"контрол «{name}» не найден");
    }

    private UiDesignerItem ContainerAt(int index) =>
        _editor.ContainerFromIndex(index) as UiDesignerItem
        ?? throw new ArgumentException($"контейнер {index} не реализован");

    private void Record(string name, Dictionary<string, object?> payload)
    {
        payload["event"] = name;
        _events.Add(payload);
    }

    private static Dictionary<string, object?> Describe(SurfaceSelectionTarget target) => new()
    {
        ["name"] = NameOf(target.Target),
        ["type"] = target.Target.GetType().Name,
        ["scope"] = target.Scope.ToString(),
        ["container"] = NameOf(target.Container),
        ["display"] = target.DisplayName,
        ["groupId"] = target.GroupId
    };

    private static string NameOf(Control control) =>
        string.IsNullOrEmpty(control.Name) ? "<" + control.GetType().Name + ">" : control.Name!;

    private static double[] Rect(Avalonia.Rect rect) => new[] { rect.X, rect.Y, rect.Width, rect.Height };

    /// <summary>Окно нужно для жестов, которые адресуются точкой экрана.</summary>
    public Window Window => _window;
}
