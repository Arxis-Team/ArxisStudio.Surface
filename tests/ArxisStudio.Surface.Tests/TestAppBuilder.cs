using Avalonia;
using Avalonia.Headless;
using ArxisStudio.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace ArxisStudio.Tests;

/// <summary>
/// Точка входа headless-платформы Avalonia для тестов.
/// </summary>
public static class TestAppBuilder
{
    /// <summary>
    /// Собирает headless-приложение для прогона тестов.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
