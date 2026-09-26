using Avalonia;

namespace Nodes.Demo;

internal sealed class Program
{
    /// <summary>
    /// Каталог канала управления, если демо запущено с <c>--automation</c>.
    /// </summary>
    public static string? AutomationDirectory { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        AutomationDirectory = Automation.AutomationChannel.DirectoryFromArguments(args);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
