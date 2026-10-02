using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.ProjectSystem.Markup.Xaml;
using ArxisStudio.Surface.UiDesigner;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using UiDesigner.Demo.ViewModels;

namespace UiDesigner.Demo.Views;

/// <summary>
/// Part 3 of <c>--live</c>: the IDE saves code, and the designer swaps the project's types in place —
/// with the window behind, after a gesture lets go, beside the application it started, and for a library
/// and the application that references it — or restarts by itself when the old types will not go.
/// </summary>
/// <remarks>
/// <para>
/// The swap is the design host's (ProjectSystem ADR 0028), and what these steps check is the policy the
/// demo gives it: nothing but the designer's own unfinished business holds a swap off — not the window
/// being behind the IDE's (L3), not the application running (L5) — and a gesture that does hold it off
/// gets the swap the moment it lets go (L4). Each says its phases on a <c>timing</c> line.
/// </para>
/// <para>
/// Nothing of a generation is kept across a swap here. A form, a control or a type of the old types in
/// a local of these methods would be a root the check brought with it, and the swap it measures would
/// fall back to a restart; what is compared across one is text, paths and numbers, and whatever has to
/// touch the canvas does it in a method of its own that returns one of those.
/// </para>
/// </remarks>
internal static partial class StudioCheck
{
    /// <summary>Part 3: L3, L4 and L5 in the project parts 1 and 2 used, L7 in a solution of two, and L9 last.</summary>
    private static async Task<int> TypesBesideTheIdeAsync(MainWindow window, DesignerViewModel designer, string folder, string project)
    {
        var failures = 0;
        string? solution;

        using (var complaints = new Complaints(designer))
        {
            failures += await SwapsBesideTheIdeAsync(window, designer, project);

            (int library, solution) = await LibraryBesideTheApplicationAsync(designer, folder);

            failures += library;

            if (complaints.Lines.Count > 0)
            {
                Fail(ref failures, $"part 3: {complaints.Lines.Count} error line(s) in the console — see above");
            }
        }

        // Last, because the process this check runs in ends with it.
        if (solution is not null)
        {
            failures += await RestartWhenHeldAsync(window, designer, solution, folder);
        }

        return failures;
    }

    /// <summary>L3, L4 and L5: swaps of the types the IDE's saves of the view model call for.</summary>
    private static async Task<int> SwapsBesideTheIdeAsync(MainWindow window, DesignerViewModel designer, string project)
    {
        var failures = 0;

        // Parts 1 and 2 held every swap, and what their builds left waiting runs as soon as they stop.
        int swaps = designer.Swaps;
        bool waiting = designer.TypesState == ProjectDesignState.SwapPending;

        designer.SwapsHeldFor = null;

        if (!await Until(() => designer.TypesState == ProjectDesignState.Live && (!waiting || designer.Swaps > swaps), 120))
        {
            if (designer.LastSwap is { Reclaimed: false })
            {
                // Something parts 1 and 2 did keeps the types: named here, while it still holds them.
                ProbeRoots("L3", ProjectAssemblyNames(designer));
                SayTrackedWindows();
            }

            return Fail(ref failures, $"L3: the swap parts 1 and 2 left waiting never ran ({designer.TypesState})");
        }

        string model = Path.Combine(Path.GetDirectoryName(project)!, "ViewModels", "MainWindowViewModel.cs");

        failures += await SwapInTheBackgroundAsync(window, designer, model);
        failures += await SwapAfterTheGestureAsync(window, designer, model);
        failures += await SwapBesideTheRunningApplicationAsync(designer, model);

        return failures;
    }

    /// <summary>
    /// L3: the IDE saves the view model, and the host builds it and swaps the types by itself, with nobody
    /// bringing the window forward — the tabs, the unsaved text, its history, the selection and the zoom
    /// where they were, on a canvas that held its frame meanwhile.
    /// </summary>
    private static async Task<int> SwapInTheBackgroundAsync(MainWindow window, DesignerViewModel designer, string model)
    {
        var failures = 0;

        if (designer.Host is not { } host
            || await SetTheBenchAsync(designer, "MainWindow.axaml", "Extra.axaml", "Swapped", zoom: 1.25) is not { } before)
        {
            return Fail(ref failures, "L3: the designer could not be given a person's state to carry");
        }

        UiDesignerView surface = window.GetControl<UiDesignerView>("Surface");
        Func<IDisposable?>? freeze = designer.FreezeCanvas;
        int froze = 0, thawed = 0, activations = 0, atBuild = -1, atSwap = -1;
        bool frameShown = false, frameGone = false;
        ProjectDesignState frozenIn = ProjectDesignState.Live;

        // The view's own freeze, watched: when the canvas froze, whether the frame was up, and whether it
        // came down with the forms back.
        designer.FreezeCanvas = () =>
        {
            IDisposable? frame = freeze?.Invoke();

            froze++;
            frozenIn = host.State;
            frameShown = surface.IsFrozen;

            return new Disposer(() =>
            {
                frame?.Dispose();

                thawed++;
                frameGone = !surface.IsFrozen;
            });
        };

        void OnActivated(object? sender, EventArgs e) => activations++;
        void OnBuilt(object? sender, ProjectDesignBuildCompletedEventArgs e) => atBuild = activations;
        void OnSwapped(object? sender, ProjectDesignSwapCompletedEventArgs e) => atSwap = activations;

        window.Activated += OnActivated;
        host.BuildCompleted += OnBuilt;
        host.SwapCompleted += OnSwapped;

        int swaps = designer.Swaps;
        bool inFront = window.IsActive;

        try
        {
            await SaveLikeAnIdeAsync(model, Disk(model).Replace("\"Ready to go\"", "\"Types swapped\"", StringComparison.Ordinal));

            if (!await Until(() => designer.Swaps > swaps && designer.TypesState == ProjectDesignState.Live, 180))
            {
                return Fail(ref failures, $"L3: the IDE's save of the view model was never built and swapped in ({designer.TypesState})");
            }
        }
        finally
        {
            window.Activated -= OnActivated;
            host.BuildCompleted -= OnBuilt;
            host.SwapCompleted -= OnSwapped;
            designer.FreezeCanvas = freeze;
        }

        if (designer.LastSwap is not { Reclaimed: true } report)
        {
            return Fail(ref failures, $"L3: the old types did not leave — {designer.LastSwap}");
        }

        Timing("L3", report);

        if (atBuild < 0 || atSwap != atBuild)
        {
            Fail(ref failures, $"L3: the window was activated {Math.Max(0, atSwap - atBuild)} time(s) between the build and the swap — the swap did not come by itself");
        }

        if (froze != 1 || thawed != 1 || !frameShown || !frameGone || frozenIn != ProjectDesignState.Swapping)
        {
            Fail(ref failures, $"L3: the canvas did not hold its frame for the swap alone (froze {froze}, thawed {thawed}, frame up {frameShown}, down after {frameGone}, while {frozenIn})");
        }

        failures += await TheBenchSurvivedAsync(designer, before, "L3");

        if (!await Until(() => FirstTextOfActiveForm(designer) == "Types swapped", 60))
        {
            Fail(ref failures, $"L3: the canvas does not show the view model the IDE saved ({FirstTextOfActiveForm(designer) ?? "nothing"})");
        }

        // The step made before the swap is undone after it, under the new types.
        designer.UndoCommand.Execute(null);

        if (!await Until(() => designer.ActiveForm is { IsDirty: false } front && !Text(front).Contains("Title=\"Swapped\"", StringComparison.Ordinal), 30))
        {
            Fail(ref failures, "L3: the edit made before the swap could not be undone after it");
        }

        if (failures == 0)
        {
            Say($"L3: a class the IDE saved is built and swapped in by itself, the window {(inFront ? "in front" : "behind")} — "
                + "tabs, unsaved text, history, selection and zoom kept, the canvas frozen for the swap alone");
        }

        return failures;
    }

    /// <summary>
    /// L4: a gesture on the canvas holds a swap off, and the swap runs the moment it lets go.
    /// </summary>
    private static async Task<int> SwapAfterTheGestureAsync(MainWindow window, DesignerViewModel designer, string model)
    {
        var failures = 0;

        if (designer.Host is not { } host)
        {
            return Fail(ref failures, "L4: no design host");
        }

        UiDesignerView surface = window.GetControl<UiDesignerView>("Surface");
        int builds = designer.Builds;
        int swaps = designer.Swaps;
        var letGo = new Stopwatch();
        long swappedAfter = -1;

        void OnSwapped(object? sender, ProjectDesignSwapCompletedEventArgs e) => swappedAfter = letGo.IsRunning ? letGo.ElapsedMilliseconds : -1;

        host.SwapCompleted += OnSwapped;

        try
        {
            using (new HeldGesture(surface))
            {
                if (!surface.IsInteracting || host.Gate.IsOpen)
                {
                    return Fail(ref failures, "L4: a press of the pan button on the canvas held nothing off");
                }

                await SaveLikeAnIdeAsync(model, Disk(model).Replace("\"Types swapped\"", "\"After the gesture\"", StringComparison.Ordinal));

                if (!await Until(() => designer.Builds > builds, 180))
                {
                    return Fail(ref failures, "L4: no build followed the IDE's save of the view model");
                }

                // Past the build by more than a swap takes to start: one that was going to has.
                await Until(() => designer.Swaps > swaps, 2);

                if (designer.Swaps > swaps)
                {
                    Fail(ref failures, "L4: the types were swapped under the gesture");
                }
                else if (designer.TypesState != ProjectDesignState.SwapPending)
                {
                    Fail(ref failures, $"L4: with the gesture held the new types are {designer.TypesState} rather than waiting");
                }

                letGo.Start();
            }

            if (!await Until(() => designer.Swaps > swaps && designer.TypesState == ProjectDesignState.Live, 30))
            {
                return Fail(ref failures, $"L4: the swap did not follow the gesture letting go ({designer.TypesState})");
            }
        }
        finally
        {
            host.SwapCompleted -= OnSwapped;
        }

        Say($"timing L4: the swap was over {swappedAfter} ms after the gesture let go");

        if (designer.LastSwap is not { Reclaimed: true } report)
        {
            return Fail(ref failures, $"L4: the old types did not leave — {designer.LastSwap}");
        }

        Timing("L4", report);

        if (swappedAfter is < 0 or > 5000)
        {
            Fail(ref failures, "L4: the swap was not over within five seconds of the gesture letting go");
        }
        else if (failures == 0)
        {
            Say("L4: a gesture on the canvas holds the swap off, and it runs the moment the gesture lets go");
        }

        return failures;
    }

    /// <summary>
    /// L5: the application the designer started runs on while the IDE's save of a class is built and
    /// swapped in — it runs from a copy, so the design build writes over nothing it holds.
    /// </summary>
    private static async Task<int> SwapBesideTheRunningApplicationAsync(DesignerViewModel designer, string model)
    {
        var failures = 0;

        await Until(() => designer.RunCommand.CanExecute(null), 60);

        designer.RunCommand.Execute(null);

        if (!await Until(() => designer.RunningProcess is not null && designer.RunningFrom is not null, 180, settle: true))
        {
            return Fail(ref failures, "L5: the application never started from the designer");
        }

        int application = designer.RunningProcess!.Value;
        string copy = designer.RunningFrom!;
        int swaps = designer.Swaps;

        try
        {
            if (await BuildsAfterACodeSaveAsync(designer, model, "while the designer's application runs") is { } blocked)
            {
                Fail(ref failures, "L5: " + blocked);
            }
            else if (!await Until(() => designer.Swaps > swaps && designer.TypesState == ProjectDesignState.Live, 60))
            {
                Fail(ref failures, $"L5: the build beside the application was not swapped in ({designer.TypesState})");
            }
            else if (designer.RunningProcess != application)
            {
                Fail(ref failures, "L5: the application did not live through the designer's build and swap");
            }
            else
            {
                if (designer.LastSwap is { } report)
                {
                    Timing("L5", report);
                }

                Say("L5: the application the designer started runs on through a build and a swap of its types");
            }
        }
        finally
        {
            designer.StopCommand.Execute(null);
        }

        if (!await Until(() => !designer.IsRunning && !Directory.Exists(copy), 30))
        {
            Fail(ref failures, $"L5: the copy the application ran from outlived it ({copy})");
        }

        return failures;
    }

    /// <summary>A person's state in the designer, as text and numbers: nothing of it holds the types.</summary>
    private sealed record Workbench(string[] Tabs, string Active, string Text, string? Step, XamlElementPath? Selected, double Zoom);

    /// <summary>
    /// Gives the designer a person's state to carry across a swap or a restart: a second tab, the main
    /// form in front with an edit not saved, a text block selected and a zoom of their own.
    /// </summary>
    /// <returns>What it set, or <see langword="null"/> when it could not.</returns>
    private static async Task<Workbench?> SetTheBenchAsync(DesignerViewModel designer, string main, string second, string title, double zoom)
    {
        if (!designer.Forms.Any(open => open.Name == second) && Open(designer, second))
        {
            await Until(() => designer.Forms.Any(open => open.Name == second && open.Session is not null), 60);
        }

        if (designer.Forms.FirstOrDefault(open => open.Name == main) is not { } form)
        {
            return null;
        }

        designer.ActiveForm = form;

        if (!await Until(() => form is { Session: not null, Problem: null } && TextBlocks(form).Length > 0, 60)
            || !await EditTitleAsync(designer, form, title))
        {
            return null;
        }

        SelectTextBlock(designer, form, 0);

        designer.Zoom = zoom;

        return new Workbench(
            [.. designer.Forms.Select(static open => open.File.Value)],
            form.File.Value,
            Text(form),
            form.Live?.UndoDescription,
            form.SelectedPath,
            designer.Zoom);
    }

    /// <summary>Whether the designer still has what the bench recorded, saying what it does not.</summary>
    private static async Task<int> TheBenchSurvivedAsync(DesignerViewModel designer, Workbench before, string step)
    {
        var failures = 0;
        string[] tabs = [.. designer.Forms.Select(static open => open.File.Value)];

        if (!tabs.SequenceEqual(before.Tabs, StringComparer.OrdinalIgnoreCase))
        {
            Fail(ref failures, $"{step}: the tabs are not the ones that were open ({string.Join(", ", tabs.Select(static tab => Path.GetFileName(tab)))})");
        }

        if (designer.ActiveForm is not { } front || !front.File.Value.Equals(before.Active, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(ref failures, $"{step}: another form is in front ({designer.ActiveForm?.Name ?? "none"})");
        }

        if (Text(front) != before.Text || !front.IsDirty)
        {
            Fail(ref failures, $"{step}: the text that was not saved did not come through");
        }

        if (front.Live?.UndoDescription != before.Step)
        {
            Fail(ref failures, $"{step}: the history is not the one it had ({front.Live?.UndoDescription ?? "none"} rather than {before.Step ?? "none"})");
        }

        if (before.Selected is { } selected
            && !await Until(() => designer.Selected is { } now && XamlElementPath.Of(now).Equals(selected), 30))
        {
            Fail(ref failures, $"{step}: the selection did not come back ({designer.SelectedName})");
        }

        if (Math.Abs(designer.Zoom - before.Zoom) > 0.0001)
        {
            Fail(ref failures, $"{step}: the zoom moved to {designer.Zoom:F2}");
        }

        return failures;
    }

    /// <summary>Lists the windows the developer tools keep, with whether each is still open.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SayTrackedWindows()
    {
        if (Type.GetType("AvaDevTools.DevToolsExtensions, AvaDevTools", throwOnError: false)
                ?.GetField("s_trackedWindows", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(null) is not System.Collections.IEnumerable tracked)
        {
            Say("  the developer tools keep no list of windows here");

            return;
        }

        foreach (object? entry in tracked)
        {
            Say(entry is Window window
                ? $"  tracked: {window.GetType().FullName} “{window.Title}” — {(window.PlatformImpl is null ? "closed" : "open")}, "
                    + $"{(window.IsVisible ? "shown" : "never shown")}, data {window.DataContext?.GetType().Name ?? "none"}"
                : $"  tracked: {entry?.GetType().FullName ?? "null"}");
        }
    }

    /// <summary>What the active form's first text block says on the canvas.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string? FirstTextOfActiveForm(DesignerViewModel designer) =>
        designer.ActiveForm is { } form ? TextBlocks(form).FirstOrDefault()?.Text : null;

    /// <summary>
    /// A press of the canvas's pan button, held until disposed: a gesture as the canvas itself sees one,
    /// with the pointer captured and <see cref="ArxisStudio.Surface.SurfaceView.IsInteracting"/> up.
    /// </summary>
    /// <remarks>
    /// Raised on the canvas rather than sent through the platform: a script has no mouse, and the canvas
    /// starts and ends a pan on exactly these two events, whoever raised them. Panning moves nothing of
    /// the document, and with no move between the two it moves no view either.
    /// </remarks>
    private sealed class HeldGesture : IDisposable
    {
        private readonly UiDesignerView _surface;
        private readonly TopLevel _root;
        private readonly Point _at;
        private readonly Pointer _pointer = new(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        private bool _released;

        public HeldGesture(UiDesignerView surface)
        {
            _surface = surface;
            _root = TopLevel.GetTopLevel(surface) ?? throw new InvalidOperationException("The canvas is not in a window.");
            _at = surface.TranslatePoint(new Point(surface.Bounds.Width / 2, surface.Bounds.Height / 2), _root) ?? default;

            surface.RaiseEvent(new PointerPressedEventArgs(
                surface,
                _pointer,
                _root,
                _at,
                Timestamp(),
                new PointerPointProperties(RawInputModifiers.MiddleMouseButton, PointerUpdateKind.MiddleButtonPressed),
                KeyModifiers.None,
                clickCount: 1));
        }

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;

            _surface.RaiseEvent(new PointerReleasedEventArgs(
                _surface,
                _pointer,
                _root,
                _at,
                Timestamp(),
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.MiddleButtonReleased),
                KeyModifiers.None,
                MouseButton.Middle));
        }

        private static ulong Timestamp() => (ulong)System.Environment.TickCount64;
    }

    /// <summary>Runs an action when disposed.</summary>
    private sealed class Disposer(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    /// <summary>
    /// The complaints the console says from now on, until disposed — heard as they are said, because the
    /// console keeps only its last lines.
    /// </summary>
    private sealed class Complaints : IDisposable
    {
        private readonly DesignerViewModel _designer;

        public Complaints(DesignerViewModel designer)
        {
            _designer = designer;
            _designer.Output.CollectionChanged += OnSaid;
        }

        /// <summary>Gets what was said with the "!" a failure carries.</summary>
        public List<string> Lines { get; } = [];

        public void Dispose() => _designer.Output.CollectionChanged -= OnSaid;

        private void OnSaid(object? sender, NotifyCollectionChangedEventArgs e)
        {
            foreach (string line in e.NewItems?.OfType<string>() ?? [])
            {
                if (line.TrimStart().StartsWith('!'))
                {
                    Lines.Add(line.Trim());
                }
            }
        }
    }
}
