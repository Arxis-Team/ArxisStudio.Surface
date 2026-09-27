using Avalonia;
using System;

namespace UiDesigner.Demo;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
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
