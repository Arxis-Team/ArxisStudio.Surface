using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
#if DEBUG
using AvaDevTools;
#endif
using UiDesigner.Demo.ViewModels;
using UiDesigner.Demo.Views;

namespace UiDesigner.Demo;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

#if DEBUG
        // On the application rather than on a window: the tools' tree is then rooted in the
        // application, every window is a node of it, and F12 from any of them opens the same
        // tools. This demo has two windows — the welcome screen and the designer — and a call made
        // on one would have inspected only that one.
        this.AttachAvaDevTools();
#endif
    }

    /// <summary>
    /// Starts the studio, which means the welcome screen unless a project was named on the way in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Opening straight into an empty canvas answered "which project?" by not asking it. The welcome
    /// screen asks, and the designer is built only once there is an answer — which also keeps MSBuild
    /// out of the path to the first window being on screen.
    /// </para>
    /// <para>
    /// A path on the command line skips it, because somebody who typed a path has already answered.
    /// That is also what makes this application scriptable, which is how its own screenshots are
    /// taken.
    /// </para>
    /// <para>
    /// Shutdown follows the last window rather than the main one: the welcome closes when the
    /// designer opens, and with the default mode that would take the application with it.
    /// </para>
    /// </remarks>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;

            string[] args = desktop.Args ?? [];
            int shot = Array.IndexOf(args, "--shot");
            string? shotPath = shot >= 0 && shot + 1 < args.Length ? args[shot + 1] : null;

            // Which of the three the middle shows, so that a screenshot can be of the XAML pane.
            int view = Array.IndexOf(args, "--view");
            string? viewName = view >= 0 && view + 1 < args.Length ? args[view + 1] : null;

            // Which variant the studio opens in, so that a screenshot can be of the light one. The
            // toggle in the toolbar is the only other way there, and a script cannot press it.
            int theme = Array.IndexOf(args, "--theme");

            if (theme >= 0 && theme + 1 < args.Length)
            {
                RequestedThemeVariant = args[theme + 1].Equals("light", StringComparison.OrdinalIgnoreCase)
                    ? ThemeVariant.Light
                    : ThemeVariant.Dark;
            }

            int verify = Array.IndexOf(args, "--verify");
            int stress = Array.IndexOf(args, "--stress");
            int reclaim = Array.IndexOf(args, "--reclaim");
            int live = Array.IndexOf(args, "--live");

            // For the one question a fallback cannot answer by itself: what held the old types.
            StudioCheck.NameRootsOnFallback = Array.IndexOf(args, "--probe") >= 0;

            if (live >= 0 && live + 1 < args.Length)
            {
                var model = new DesignerViewModel();
                var window = new MainWindow { DataContext = model };

                StudioCheck.LiveWhenShown(window, model, args[live + 1]);

                desktop.Exit += (_, _) => model.Dispose();
                desktop.MainWindow = window;
            }
            else if (reclaim >= 0 && reclaim + 1 < args.Length)
            {
                var model = new DesignerViewModel();
                var window = new MainWindow { DataContext = model };

                StudioCheck.ReclaimWhenShown(window, model, args[reclaim + 1]);

                desktop.Exit += (_, _) => model.Dispose();
                desktop.MainWindow = window;
            }
            else if (verify >= 0 && verify + 1 < args.Length)
            {
                var model = new DesignerViewModel();
                var window = new MainWindow { DataContext = model };

                StudioCheck.RunWhenShown(window, model, args[verify + 1]);

                desktop.Exit += (_, _) => model.Dispose();
                desktop.MainWindow = window;
            }
            else if (stress >= 0 && stress + 1 < args.Length)
            {
                var model = new DesignerViewModel();
                var window = new MainWindow { DataContext = model };

                StudioCheck.StressWhenShown(window, model, args[stress + 1]);

                desktop.Exit += (_, _) => model.Dispose();
                desktop.MainWindow = window;
            }
            else if (Positional(args) is [{ Length: > 0 } path, .. var rest])
            {
                int active = Array.IndexOf(args, "--active");
                string? activeName = active >= 0 && active + 1 < args.Length ? args[active + 1] : null;

                desktop.MainWindow = Designer(desktop, path, rest, activeName, shotPath, viewName);
            }
            else
            {
                var welcome = new WelcomeWindow { DataContext = new WelcomeViewModel() };

                if (shotPath is { Length: > 0 })
                {
                    WindowShot.TakeWhenShown(welcome, shotPath);
                }

                ((WelcomeViewModel)welcome.DataContext).ProjectChosen += (_, chosen) =>
                {
                    MainWindow designer = Designer(
                        desktop, chosen, forms: [], active: null, shotPath: null, viewName: null);

                    desktop.MainWindow = designer;

                    designer.Show();
                    welcome.Close();
                };

                desktop.MainWindow = welcome;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Builds the designer window around a project, and disposes it when the studio exits.</summary>
    private static MainWindow Designer(
        IClassicDesktopStyleApplicationLifetime desktop,
        string path,
        IReadOnlyList<string> forms,
        string? active,
        string? shotPath,
        string? viewName)
    {
        var model = new DesignerViewModel();
        var window = new MainWindow { DataContext = model };

        if (viewName is { Length: > 0 })
        {
            model.View = viewName.ToUpperInvariant() switch
            {
                "XAML" => DocumentView.Xaml,
                "SPLIT" => DocumentView.Split,
                _ => DocumentView.Design,
            };
        }

        if (shotPath is { Length: > 0 })
        {
            WindowShot.TakeAfterLoad(window, model, shotPath);
        }

        model.OpenAtStartup(path, forms, active);

        desktop.Exit += (_, _) => model.Dispose();

        return window;
    }

    /// <summary>The arguments that are not a switch and are not a switch's value.</summary>
    /// <remarks>
    /// All but one of this sample's switches take a value: `--shot`, `--verify`, `--stress`,
    /// `--reclaim`, `--live`, `--view`, `--active`, `--theme` and `--automation` do, and `--probe` does not — so the project path can
    /// follow it without being read as the thing it turns on.
    /// </remarks>
    private static string[] Positional(string[] args)
    {
        var positional = new List<string>();

        for (int index = 0; index < args.Length; index++)
        {
            if (args[index].StartsWith("--", StringComparison.Ordinal))
            {
                if (!args[index].Equals("--probe", StringComparison.Ordinal))
                {
                    index++;
                }

                continue;
            }

            positional.Add(args[index]);
        }

        return [.. positional];
    }
}
