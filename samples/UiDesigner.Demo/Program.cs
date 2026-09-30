using Avalonia;

namespace UiDesigner.Demo;

internal static class Program
{
    /// <summary>
    /// The automation channel's directory, when the demo was started with <c>--automation</c>.
    /// </summary>
    public static string? AutomationDirectory { get; private set; }

    [System.STAThread]
    public static void Main(string[] args)
    {
        AutomationDirectory = Automation.AutomationChannel.DirectoryFromArguments(args);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
