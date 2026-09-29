using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;

namespace Nodes.StateMachine.Machine;

/// <summary>
/// Экран приложения, которым управляет машина: каждое состояние показывает один.
/// </summary>
public enum AppScreen
{
    Login,
    Checking,
    LoginError,
    Home,
    Details,
    Settings,
    Offline
}

/// <summary>
/// Названия экранов — они же названия состояний, пока состояние не переименовали сменой экрана.
/// </summary>
public static class AppScreens
{
    public static readonly AppScreen[] All = Enum.GetValues<AppScreen>();

    public static string Title(AppScreen screen) => screen switch
    {
        AppScreen.Login => "Вход в систему",
        AppScreen.Checking => "Проверка",
        AppScreen.LoginError => "Ошибка входа",
        AppScreen.Home => "Главная",
        AppScreen.Details => "Карточка",
        AppScreen.Settings => "Настройки",
        AppScreen.Offline => "Нет сети",
        _ => screen.ToString()
    };
}

/// <summary>
/// Цвета видов узла — данные графа, как категории узлов в Blueprint: полоса заголовка узла.
/// </summary>
/// <remarks>
/// Белое название на каждом читается с контрастом не ниже 4,5:1 — те же цвета, что у демо узлов.
/// </remarks>
public static class NodeKinds
{
    public static readonly Color Entry = Color.Parse("#4D7A2A");

    public static readonly Color State = Color.Parse("#3B6FB6");

    public static readonly Color Any = Color.Parse("#6A4C93");

    public static readonly Color Transition = Color.Parse("#B2560D");
}

/// <summary>
/// Что умеет увидеть машина, пока идёт: приложение, которым она управляет.
/// </summary>
/// <remarks>
/// Условия переходов читают его на каждом такте, как правила переходов в Unreal; экраны пишут в него
/// то, что ввёл человек.
/// </remarks>
public sealed class MachineContext : Observable
{
    /// <summary>
    /// Пароль, который считается верным: подсказка стоит на экране входа.
    /// </summary>
    public const string ValidPassword = "avalonia";

    private string _login = "admin";
    private string _password = string.Empty;
    private bool _online = true;
    private string _selectedItem = string.Empty;

    public string Login
    {
        get => _login;
        set => Set(ref _login, value);
    }

    public string Password
    {
        get => _password;
        set => Set(ref _password, value);
    }

    /// <summary>
    /// Есть ли сеть — переключатель на панели запуска.
    /// </summary>
    public bool Online
    {
        get => _online;
        set => Set(ref _online, value);
    }

    /// <summary>
    /// Что открыли на главной — показывает карточка.
    /// </summary>
    public string SelectedItem
    {
        get => _selectedItem;
        set => Set(ref _selectedItem, value);
    }
}

/// <summary>
/// Условие перехода: событие интерфейса, наименьшее время в состоянии и проверка приложения.
/// </summary>
/// <param name="Title">Как условие видно на холсте — название узла-перехода.</param>
/// <param name="Event">Событие интерфейса, которого ждёт переход, или <see langword="null"/>.</param>
/// <param name="MinSeconds">Сколько секунд машина должна пробыть в состоянии.</param>
/// <param name="Guard">Проверка приложения, или <see langword="null"/>.</param>
public sealed record Trigger(string Title, string? Event, double MinSeconds, Func<MachineContext, bool>? Guard)
{
    /// <summary>
    /// Выполнено ли условие на этом такте.
    /// </summary>
    public bool IsMet(MachineContext context, IReadOnlyCollection<string> events, double secondsInState) =>
        (Event == null || events.Contains(Event))
        && secondsInState >= MinSeconds
        && (Guard == null || Guard(context));

    public override string ToString() => Title;
}

/// <summary>
/// Условия, из которых собирают переходы, — и в примере, и в контекстном меню.
/// </summary>
public static class Triggers
{
    public static readonly Trigger SignIn = new("нажата «Войти»", "войти", 0, null);

    public static readonly Trigger Retry = new("нажата «Повторить»", "повторить", 0, null);

    public static readonly Trigger Open = new("нажата «Открыть»", "открыть", 0, null);

    public static readonly Trigger Back = new("нажата «Назад»", "назад", 0, null);

    public static readonly Trigger OpenSettings = new("нажата «Настройки»", "настройки", 0, null);

    public static readonly Trigger SignOut = new("нажата «Выйти»", "выйти", 0, null);

    public static readonly Trigger PasswordValid = new("1,5 с · пароль верный", null, 1.5, c => c.Password == MachineContext.ValidPassword);

    public static readonly Trigger PasswordInvalid = new("1,5 с · пароль неверный", null, 1.5, c => c.Password != MachineContext.ValidPassword);

    public static readonly Trigger NetworkLost = new("нет сети", null, 0, c => !c.Online);

    public static readonly Trigger NetworkBack = new("сеть вернулась", null, 0, c => c.Online);

    public static readonly Trigger After3Seconds = new("через 3 с", null, 3, null);

    public static readonly Trigger[] All =
        [SignIn, Retry, Open, Back, OpenSettings, SignOut, PasswordValid, PasswordInvalid, NetworkLost, NetworkBack, After3Seconds];
}

/// <summary>
/// Основа моделей: уведомление о смене свойства.
/// </summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Как узел выглядит, пока машина идёт, — как в отладке State Machine Unreal.
/// </summary>
public enum RunLook
{
    /// <summary>Машина его не касалась.</summary>
    None,

    /// <summary>Активное состояние.</summary>
    Active,

    /// <summary>Недавно покинутое состояние или только что сработавший переход — след гаснет.</summary>
    Trace
}

/// <summary>
/// Узел машины. <see cref="Location"/> держит модель: редактор привязан к нему в обе стороны.
/// </summary>
public abstract class MachineNode : Observable
{
    private Point _location;
    private string? _title;
    private RunLook _look;
    private string? _runText;

    protected MachineNode(string? title, Point location, bool hasInput, bool hasOutput, string inputName = "вход", string outputName = "выход")
    {
        _title = title;
        _location = location;
        Inputs = hasInput ? [new FlowPort(this, inputName, isInput: true)] : [];
        Outputs = hasOutput ? [new FlowPort(this, outputName, isInput: false)] : [];
    }

    public string? Title
    {
        get => _title;
        protected set => Set(ref _title, value);
    }

    public Point Location
    {
        get => _location;
        set => Set(ref _location, value);
    }

    public IReadOnlyList<FlowPort> Inputs { get; }

    public IReadOnlyList<FlowPort> Outputs { get; }

    public virtual Color? Accent => null;

    /// <summary>
    /// Вид узла на запущенной машине: окно ставит по нему класс контейнеру.
    /// </summary>
    public RunLook Look
    {
        get => _look;
        set => Set(ref _look, value);
    }

    /// <summary>
    /// Строка запущенной машины в теле узла: время в состоянии, «сработал» у перехода.
    /// </summary>
    public string? RunText
    {
        get => _runText;
        set
        {
            if (Set(ref _runText, value))
                Raise(nameof(HasRunText));
        }
    }

    public bool HasRunText => !string.IsNullOrEmpty(_runText);

    public override string ToString() => Title ?? GetType().Name;
}

/// <summary>
/// Вход машины: одна связь — в начальное состояние.
/// </summary>
public sealed class EntryNode(Point location) : MachineNode("Вход", location, hasInput: false, hasOutput: true)
{
    public override Color? Accent => NodeKinds.Entry;
}

/// <summary>
/// Любое состояние: его переходы проверяются, где бы машина ни стояла.
/// </summary>
public sealed class AnyStateNode(Point location) : MachineNode("Любое состояние", location, hasInput: false, hasOutput: true)
{
    public override Color? Accent => NodeKinds.Any;
}

/// <summary>
/// Состояние: показывает свой экран приложения.
/// </summary>
public sealed class StateNode : MachineNode
{
    private AppScreen _screen;

    public StateNode(AppScreen screen, Point location)
        : base(AppScreens.Title(screen), location, hasInput: true, hasOutput: true)
    {
        _screen = screen;
    }

    public AppScreen Screen
    {
        get => _screen;
        set
        {
            if (!Set(ref _screen, value))
                return;

            Title = AppScreens.Title(value);
            Raise(nameof(ScreenText));
        }
    }

    public string ScreenText => $"экран: {AppScreens.Title(_screen)}";

    public override Color? Accent => NodeKinds.State;
}

/// <summary>
/// Переход: название — условие, тело — штырьки входа и выхода.
/// </summary>
public sealed class TransitionNode : MachineNode
{
    private Trigger _trigger;

    public TransitionNode(Trigger trigger, Point location)
        : base(trigger.Title, location, hasInput: true, hasOutput: true, inputName: string.Empty, outputName: string.Empty)
    {
        _trigger = trigger;
    }

    public Trigger Trigger
    {
        get => _trigger;
        set
        {
            if (Set(ref _trigger, value))
                Title = value.Title;
        }
    }

    public override Color? Accent => NodeKinds.Transition;
}

/// <summary>
/// Узел перенаправления: излом провода — узел, как у демо узлов. Шаблон у него — <c>Reroute</c> библиотеки.
/// </summary>
public sealed class RerouteNode(Point location) : MachineNode(null, location, hasInput: true, hasOutput: true, "вход", "выход");

/// <summary>
/// Порт узла: им связь называет свой конец, и сравнивается он по ссылке.
/// </summary>
public sealed class FlowPort(MachineNode node, string name, bool isInput)
{
    public MachineNode Node { get; } = node;

    public string Name { get; } = name;

    public bool IsInput { get; } = isInput;

    public override string ToString() => $"{Node}.{Name}";
}

/// <summary>
/// Связь потока управления: из выхода <see cref="From"/> во вход <see cref="To"/>.
/// </summary>
/// <remarks>
/// Цвет провода — модели: редактор берёт его привязкой <c>LinkStrokeBinding</c> и перечитывает по
/// <see cref="Observable.PropertyChanged"/>. Сработавший переход зажигает свои провода, и след гаснет
/// за секунду — <see cref="Heat"/>, ступенями: кисть у библиотеки одна на цвет.
/// </remarks>
public sealed class FlowLink(FlowPort from, FlowPort to) : Observable
{
    /// <summary>
    /// Провод потока — светлый, как провода исполнения в Blueprint.
    /// </summary>
    public static readonly Color Idle = Color.Parse("#C9CED6");

    /// <summary>
    /// Горящий провод и рамка активного состояния — янтарь отладки Unreal.
    /// </summary>
    public static readonly Color Hot = Color.Parse("#FFB300");

    private int _heat;

    public FlowPort From { get; } = from;

    public FlowPort To { get; } = to;

    /// <summary>
    /// Накал: 0 — провод остыл, 10 — только что сработал.
    /// </summary>
    public int Heat
    {
        get => _heat;
        set
        {
            if (Set(ref _heat, Math.Clamp(value, 0, 10)))
                Raise(nameof(Color));
        }
    }

    public Color Color
    {
        get
        {
            var t = _heat / 10.0;
            return Color.FromRgb(
                (byte)(Idle.R + ((Hot.R - Idle.R) * t)),
                (byte)(Idle.G + ((Hot.G - Idle.G) * t)),
                (byte)(Idle.B + ((Hot.B - Idle.B) * t)));
        }
    }

    public override string ToString() => $"{From} → {To}";
}
