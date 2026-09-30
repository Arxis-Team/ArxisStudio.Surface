using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UiDesigner.Demo.ViewModels;

/// <summary>
/// Writes a small Avalonia application to disk, for the checks to work on.
/// </summary>
/// <remarks>
/// <para>
/// The demo opens projects and does not make them, so nothing in its interface reaches this. It is
/// what <c>--verify</c>, <c>--stress</c> and <c>--reclaim</c> start from: a check needs a project
/// it may build, run and rewrite, and that cannot be somebody's own.
/// </para>
/// <para>
/// Written here rather than delegated to <c>dotnet new</c>: the templates that produce an Avalonia
/// application are a workload somebody has to have installed, and a check that fails on a clean
/// machine with "no templates matched" has tested the machine. What it writes is a plain SDK
/// project — the same four packages the Avalonia templates use — so the result builds with nothing
/// but the .NET SDK.
/// </para>
/// <para>
/// The project turns central package management off for itself. One written inside a repository
/// that manages versions centrally would otherwise fail to restore, with an error about the
/// <c>Version</c> attributes this file writes.
/// </para>
/// <para>
/// The window has something in it: a panel and two lines of text. A check lays controls out, and a
/// form with nowhere to put them would be a check of an empty canvas.
/// </para>
/// </remarks>
public static class ProjectScaffold
{
    /// <summary>The Avalonia the generated project references.</summary>
    /// <remarks>
    /// The same version this designer is built against, deliberately. The adapter loads the
    /// project's own assemblies to build its forms, and two Avalonia versions in one process produce
    /// two <c>Button</c> types that are not assignable to one another.
    /// </remarks>
    public const string AvaloniaVersion = "12.1.1";

    private const string TargetFramework = "net10.0";

    /// <summary>Whether a name can be a project — and therefore a namespace and a directory.</summary>
    public static bool IsUsableName(string name) =>
        name is { Length: > 0 }
            && char.IsLetter(name[0])
            && name.All(static c => char.IsLetterOrDigit(c) || c is '_' or '.')
            && !name.EndsWith('.');

    /// <summary>
    /// Writes the project and returns the file that names it.
    /// </summary>
    /// <exception cref="IOException">The directory exists and is not empty.</exception>
    public static async Task<string> CreateAsync(
        string parentDirectory,
        string name,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);

        if (!IsUsableName(name))
        {
            throw new ArgumentException(
                "A project name starts with a letter and holds letters, digits, dots and underscores.",
                nameof(name));
        }

        string root = Path.Combine(parentDirectory, name);

        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new IOException($"{root} already exists and is not empty.");
        }

        Directory.CreateDirectory(Path.Combine(root, "Views"));
        Directory.CreateDirectory(Path.Combine(root, "ViewModels"));

        string project = Path.Combine(root, name + ".csproj");

        await WriteAsync(project, Csproj(), cancellationToken).ConfigureAwait(false);
        await WriteAsync(Path.Combine(root, "Program.cs"), Program(name), cancellationToken).ConfigureAwait(false);
        await WriteAsync(Path.Combine(root, "App.axaml"), AppMarkup(name), cancellationToken).ConfigureAwait(false);
        await WriteAsync(Path.Combine(root, "App.axaml.cs"), AppCode(name), cancellationToken).ConfigureAwait(false);

        await WriteAsync(
            Path.Combine(root, "ViewModels", "MainWindowViewModel.cs"),
            ViewModel(name),
            cancellationToken).ConfigureAwait(false);

        await WriteAsync(
            Path.Combine(root, "Views", "MainWindow.axaml"),
            Window(name),
            cancellationToken).ConfigureAwait(false);

        await WriteAsync(
            Path.Combine(root, "Views", "MainWindow.axaml.cs"),
            WindowCode(name),
            cancellationToken).ConfigureAwait(false);

        return project;
    }

    /// <summary>The Avalonia version a project references, for the recent list's chip.</summary>
    public static string VersionOf(string projectFile)
    {
        try
        {
            foreach (string line in File.ReadLines(projectFile))
            {
                int avalonia = line.IndexOf("\"Avalonia\"", StringComparison.Ordinal);

                if (avalonia < 0)
                {
                    continue;
                }

                int version = line.IndexOf("Version=\"", avalonia, StringComparison.Ordinal);

                if (version < 0)
                {
                    continue;
                }

                version += "Version=\"".Length;

                int end = line.IndexOf('"', version);

                if (end > version)
                {
                    return "Avalonia " + line[version..end];
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The chip is decoration; a file that cannot be read simply has none.
        }

        return string.Empty;
    }

    private static async Task WriteAsync(string path, string content, CancellationToken cancellationToken) =>
        await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);

    private static string Csproj() => string.Create(
        CultureInfo.InvariantCulture,
        $"""
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <OutputType>WinExe</OutputType>
            <TargetFramework>{TargetFramework}</TargetFramework>
            <Nullable>enable</Nullable>
            <BuiltInComInteropSupport>true</BuiltInComInteropSupport>
            <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>

            <!-- Its own versions, so that a folder inside a repository that manages them centrally
                 does not turn every reference below into a restore error. -->
            <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="Avalonia" Version="{AvaloniaVersion}" />
            <PackageReference Include="Avalonia.Desktop" Version="{AvaloniaVersion}" />
            <PackageReference Include="Avalonia.Themes.Fluent" Version="{AvaloniaVersion}" />
            <PackageReference Include="Avalonia.Fonts.Inter" Version="{AvaloniaVersion}" />
          </ItemGroup>

        </Project>

        """);

    private static string Program(string name) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""
        using Avalonia;

        namespace {{name}};

        internal static class Program
        {
            [System.STAThread]
            public static void Main(string[] args) => BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);

            public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
        }

        """);

    private static string AppMarkup(string name) => string.Create(
        CultureInfo.InvariantCulture,
        $"""
        <Application xmlns="https://github.com/avaloniaui"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     x:Class="{name}.App"
                     RequestedThemeVariant="Dark">

          <Application.Styles>
            <FluentTheme />
          </Application.Styles>

        </Application>

        """);

    private static string AppCode(string name) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""
        using Avalonia;
        using Avalonia.Controls.ApplicationLifetimes;
        using Avalonia.Markup.Xaml;
        using {{name}}.ViewModels;
        using {{name}}.Views;

        namespace {{name}};

        public partial class App : Application
        {
            public override void Initialize() => AvaloniaXamlLoader.Load(this);

            public override void OnFrameworkInitializationCompleted()
            {
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.MainWindow = new MainWindow
                    {
                        DataContext = new MainWindowViewModel(),
                    };
                }

                base.OnFrameworkInitializationCompleted();
            }
        }

        """);

    private static string WindowCode(string name) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""
        using Avalonia.Controls;

        namespace {{name}}.Views;

        public partial class MainWindow : Window
        {
            public MainWindow() => InitializeComponent();
        }

        """);

    /// <summary>
    /// The view model behind the window, with the two lines its form binds to.
    /// </summary>
    /// <remarks>
    /// Plain properties and no framework: the point here is the markup rather than the plumbing.
    /// </remarks>
    private static string ViewModel(string name) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""
        namespace {{name}}.ViewModels;

        public sealed class MainWindowViewModel
        {
            public string Title { get; } = "Ready to go";

            public string Greeting { get; } = "Open Views/MainWindow.axaml in the designer.";
        }

        """);

    /// <summary>The form itself, which is what the designer opens first.</summary>
    private static string Window(string name) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vm="using:{{name}}.ViewModels"
                x:Class="{{name}}.Views.MainWindow"
                x:DataType="vm:MainWindowViewModel"
                Title="{{name}}"
                Width="900" Height="600">

              <StackPanel Margin="24" Spacing="10" VerticalAlignment="Center">
                <TextBlock Text="{Binding Title}" FontSize="22" FontWeight="SemiBold" />
                <TextBlock Text="{Binding Greeting}" Opacity="0.75" />
              </StackPanel>

        </Window>

        """);
}
