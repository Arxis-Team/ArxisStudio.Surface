using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.Markup.Xaml;
using Avalonia.Controls;
using UiDesigner.Demo.ViewModels;

namespace UiDesigner.Demo.Views;

/// <summary>
/// L7 of <c>--live</c>: an application and the library it references, open as one solution.
/// </summary>
/// <remarks>
/// The two are one generation (ProjectSystem ADR 0027): the library's assembly is loaded once, whatever
/// references it, and the application's window draws the library's control from it. A class the IDE
/// saves in the library is built through the application that references it — the top of what changed
/// — and the swap that follows replaces both projects' types at once.
/// </remarks>
internal static partial class StudioCheck
{
    /// <summary>
    /// L7: the solution of two opened with a form of each, the library loaded once, and the IDE's save of
    /// the library's class built through the application and swapped in.
    /// </summary>
    /// <returns>The failures, and the solution for L9 — or <see langword="null"/> when it never opened.</returns>
    private static async Task<(int Failures, string? Solution)> LibraryBesideTheApplicationAsync(DesignerViewModel designer, string folder)
    {
        var failures = 0;
        string solution = await WriteSuiteAsync(Path.Combine(folder, "Suite"));

        await Until(() => !designer.IsBusy, 120);

        designer.OpenAtStartup(solution, ["LibCard.axaml", "MainWindow.axaml"], active: "MainWindow.axaml");

        if (!await Until(
                () => !designer.IsBusy
                    && designer.Host is { GenerationName: not null }
                    && designer.Forms.Count == 2
                    && designer.Forms.Any(static open => open.Name == "LibCard.axaml")
                    && designer.ActiveForm is { Name: "MainWindow.axaml", Session: not null, Problem: null },
                600))
        {
            Fail(ref failures, "L7: the solution never opened with a form of each project: "
                + string.Join("; ", designer.Forms.Select(static open => $"{open.Name} — {open.Problem ?? (open.Session is null ? "no session" : "open")}")));

            return (failures, null);
        }

        if (designer.Host is { DesignSet.Length: not 2 } host)
        {
            Fail(ref failures, $"L7: the generation holds {host.DesignSet.Length} project(s) rather than the application and its library");
        }

        (int contexts, int loaded) = Copies("SuiteLib");

        if (contexts != 1 || loaded != 1)
        {
            Fail(ref failures, $"L7: the library is loaded {loaded} time(s), {contexts} of them in a live load context, rather than once");
        }

        if (!await Until(() => FormShows(designer, "MainWindow.axaml", "LibCard", "Library one"), 60))
        {
            Fail(ref failures, "L7: the application's window does not draw the library's control");
        }

        // The IDE saves the library's class.
        string card = Path.Combine(Path.GetDirectoryName(solution)!, "SuiteLib", "LibCard.axaml.cs");
        int swaps = designer.Swaps;

        await SaveLikeAnIdeAsync(card, Disk(card).Replace("\"Library one\"", "\"Library two\"", StringComparison.Ordinal));

        if (!await Until(() => designer.Swaps > swaps && designer.TypesState == ProjectDesignState.Live, 300))
        {
            Fail(ref failures, $"L7: the IDE's save of the library's class was never built and swapped in ({designer.TypesState})");

            return (failures, solution);
        }

        if (designer.LastSwap is not { Reclaimed: true } report)
        {
            Fail(ref failures, $"L7: the old types did not leave — {designer.LastSwap}");

            return (failures, solution);
        }

        Timing("L7", report);

        string[] built = BuiltProjects(designer);

        if (built is not ["SuiteApp"])
        {
            Fail(ref failures, $"L7: the library's change built {string.Join(", ", built)} rather than the application that references it");
        }

        (contexts, loaded) = Copies("SuiteLib");

        if (contexts != 1 || loaded != 1)
        {
            Fail(ref failures, $"L7: after the swap the library is loaded {loaded} time(s), {contexts} of them in a live load context, rather than once");
        }

        if (!await Until(() => FormShows(designer, "MainWindow.axaml", "LibCard", "Library two"), 60))
        {
            Fail(ref failures, "L7: the application's window does not draw the library's control as the IDE saved it");
        }

        if (failures == 0)
        {
            Say("L7: an application and its library are one generation, the library loaded once; its saved class is built through the application and swapped in");
        }

        return (failures, solution);
    }

    /// <summary>
    /// Writes an application and a library it references — the library's control placed on the
    /// application's window — and a solution of the two.
    /// </summary>
    /// <returns>The solution.</returns>
    private static async Task<string> WriteSuiteAsync(string folder)
    {
        string application = await ProjectScaffold.CreateAsync(folder, "SuiteApp", CancellationToken.None);
        string library = Path.Combine(folder, "SuiteLib");

        Directory.CreateDirectory(library);

        await File.WriteAllTextAsync(
            Path.Combine(library, "SuiteLib.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <TargetFramework>{ProjectScaffold.TargetFramework}</TargetFramework>
                <Nullable>enable</Nullable>
                <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Avalonia" Version="{ProjectScaffold.AvaloniaVersion}" />
              </ItemGroup>

            </Project>

            """);

        // A control with a styled property, as the reclaim run's is: registering one is what roots a type
        // in Avalonia's registry, so the swap has something real to let go of.
        await File.WriteAllTextAsync(
            Path.Combine(library, "LibCard.axaml"),
            """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:lib="using:SuiteLib"
                         x:Class="SuiteLib.LibCard"
                         x:DataType="lib:LibCard">
              <Border Padding="12">
                <TextBlock Text="{Binding Caption}" />
              </Border>
            </UserControl>

            """);

        await File.WriteAllTextAsync(
            Path.Combine(library, "LibCard.axaml.cs"),
            """
            using Avalonia;
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml;

            namespace SuiteLib;

            public partial class LibCard : UserControl
            {
                public static readonly StyledProperty<string> CaptionProperty =
                    AvaloniaProperty.Register<LibCard, string>(nameof(Caption), defaultValue: "Library one");

                public string Caption
                {
                    get => GetValue(CaptionProperty);
                    set => SetValue(CaptionProperty, value);
                }

                public LibCard()
                {
                    AvaloniaXamlLoader.Load(this);
                    DataContext = this;
                }
            }

            """);

        string project = await File.ReadAllTextAsync(application);

        await File.WriteAllTextAsync(
            application,
            project.Replace(
                "</Project>",
                "  <ItemGroup>\n    <ProjectReference Include=\"../SuiteLib/SuiteLib.csproj\" />\n  </ItemGroup>\n\n</Project>",
                StringComparison.Ordinal));

        string window = Path.Combine(Path.GetDirectoryName(application)!, "Views", "MainWindow.axaml");
        string markup = await File.ReadAllTextAsync(window);

        await File.WriteAllTextAsync(
            window,
            markup
                .Replace("<Window ", "<Window xmlns:lib=\"using:SuiteLib\" ", StringComparison.Ordinal)
                .Replace("</StackPanel>", "  <lib:LibCard />\n      </StackPanel>", StringComparison.Ordinal));

        string solution = Path.Combine(folder, "Suite.slnx");

        await File.WriteAllTextAsync(
            solution,
            """
            <Solution>
              <Project Path="SuiteApp/SuiteApp.csproj" />
              <Project Path="SuiteLib/SuiteLib.csproj" />
            </Solution>

            """);

        return solution;
    }

    /// <summary>
    /// How many copies of an assembly the process holds: in the load contexts that are alive, and in the
    /// process at all — where a context that is unloading still counts until it has gone.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (int Contexts, int Process) Copies(string name) => (
        AssemblyLoadContext.All.SelectMany(static context => context.Assemblies).Count(assembly => assembly.GetName().Name == name),
        AppDomain.CurrentDomain.GetAssemblies().Count(assembly => assembly.GetName().Name == name));

    /// <summary>Whether a control of this type on an open form draws a text block saying exactly this.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool FormShows(DesignerViewModel designer, string form, string control, string text) =>
        designer.Forms.FirstOrDefault(open => open.Name == form) is { } shown
        && Drawn(shown, control) is { } placed
        && Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(placed)
            .OfType<TextBlock>()
            .Any(block => block.Text == text);

    /// <summary>The names of the projects the last design build built.</summary>
    private static string[] BuiltProjects(DesignerViewModel designer) =>
        designer.LastBuild is not { } build || designer.CurrentSnapshot is not { } snapshot
            ? []
            : [.. build.Projects.Select(identity => snapshot.TryGetProject(identity, out ProjectSnapshot? project) ? project.Name : "?")];
}
