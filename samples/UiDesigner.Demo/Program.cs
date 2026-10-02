using Avalonia;

namespace UiDesigner.Demo;

internal static class Program
{
    /// <summary>
    /// The automation channel's directory, when the demo was started with <c>--automation</c>.
    /// </summary>
    /// <remarks>
    /// A restart hands it to the new copy. The live check sets it too, before it makes the designer
    /// restart, so that it can ask the new copy what it took.
    /// </remarks>
    public static string? AutomationDirectory { get; internal set; }

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
