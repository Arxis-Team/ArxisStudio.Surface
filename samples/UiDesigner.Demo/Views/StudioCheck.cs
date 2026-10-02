using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.Markup.Xaml;
using ArxisStudio.Surface.UiDesigner;
using UiDesigner.Demo.ViewModels;

namespace UiDesigner.Demo.Views;

/// <summary>
/// Drives the studio through the whole of what it claims to do, and says whether it did it.
/// </summary>
/// <remarks>
/// <para>
/// A designer is a claim — "you can build an application in this" — and the only honest way to
/// check a claim like that is to make one. <c>--verify &lt;folder&gt;</c> writes a small project
/// into the folder, opens its window in the designer, lays controls onto it through the same code
/// path the toolbox uses, edits a property through the inspector's own rows, saves, builds and runs
/// it, then stops what it started and reports.
/// </para>
/// <para>
/// Every step goes through the view model rather than around it. A check that wrote the markup
/// itself would prove that this file can write XAML; going through <c>Drop</c>, the inspector's
/// rows and the commands proves the studio can.
/// </para>
/// <para>
/// Waits are on the clock and bounded, because these steps are MSBuild and a process start. There
/// is nothing to coordinate on from outside — that is the difference between this and the tests
/// under <c>tests/</c>, which are forbidden from sleeping precisely because they have something.
/// </para>
/// </remarks>
internal static class StudioCheck
{
    /// <summary>
    /// Whether a swap that could not reclaim should stay put instead of restarting.
    /// </summary>
    /// <remarks>
    /// Off unless <c>--probe</c> was passed. A restart is the right answer for a person and the
    /// wrong one for a measurement: the root is only nameable while it still holds, and the one
    /// tool that names it — a heap dump, read with <c>gcroot</c> — needs the process to still be
    /// there. That is how <c>AvaloniaEdit.RoutedCommand._inputElement</c> was found, after a
    /// reflective walk of the same graph had said nothing held it.
    /// </remarks>
    internal static bool NameRootsOnFallback { get; set; }

    public static void RunWhenShown(MainWindow window, DesignerViewModel designer, string folder) =>
        window.Opened += (_, _) => _ = RunAsync(window, designer, folder);

    private static async Task RunAsync(Window window, DesignerViewModel designer, string folder)
    {
        var failures = 0;

        try
        {
            failures = await CheckAsync(designer, folder);
        }
        catch (Exception error)
        {
            Say($"the check itself failed: {error.GetType().Name}: {error.Message}");

            failures++;
        }

        Say(failures == 0
            ? "VERDICT ok — a project was created, laid out, built and run"
            : $"VERDICT {failures} step(s) failed");

        window.Close();
    }

    /// <summary>Runs the endurance cycle instead of the straight-line check.</summary>
    public static void StressWhenShown(MainWindow window, DesignerViewModel designer, string folder) =>
        window.Opened += (_, _) => _ = StressRunAsync(window, designer, folder);

    /// <summary>Runs the generation-reclaim measurement instead of either.</summary>
    public static void ReclaimWhenShown(MainWindow window, DesignerViewModel designer, string folder) =>
        window.Opened += (_, _) => _ = ReclaimRunAsync(window, designer, folder);

    private static async Task ReclaimRunAsync(Window window, DesignerViewModel designer, string folder)
    {
        var failures = 0;

        try
        {
            failures = await ReclaimAsync(designer, folder);
        }
        catch (Exception error)
        {
            Say($"the reclaim run itself failed: {error.GetType().Name}: {error.Message}");

            failures++;
        }

        Say(failures == 0
            ? "VERDICT ok — a superseded generation was proven dead and its successor resolved cleanly"
            : $"VERDICT {failures} step(s) failed");

        window.Close();
    }

    /// <summary>
    /// The measurement ADR 0021 asked for: whether a superseded generation, given every cleanup
    /// Avalonia 12.1.1 offers, actually leaves the process — and whether its successor then
    /// resolves cleanly where "Unable to substitute" used to live.
    /// </summary>
    /// <remarks>
    /// This began as the go/no-go gate for the adapter's reclaim API and stayed as its regression
    /// harness: the teardown and the reclaim are the designer's own and the adapter's own, called
    /// here rather than copied, so a measurement that passes is a measurement of what the studio
    /// actually does. It is also the run to repeat against a new Avalonia.
    /// </remarks>
    private static async Task<int> ReclaimAsync(DesignerViewModel designer, string folder)
    {
        var failures = 0;

        // The studio must not restart itself out from under the measurement.
        designer.AutoReloadForCode = false;

        // A path to something openable measures that project instead of a scaffolded one, which
        // is how a swap that failed on somebody's real application gets its root named rather
        // than guessed at.
        bool scaffolded = System.IO.Path.GetExtension(folder).ToUpperInvariant()
            is not (".SLN" or ".SLNX" or ".CSPROJ");

        string project = scaffolded
            ? await ProjectScaffold.CreateAsync(folder, "ReclaimApp", CancellationToken.None)
            : folder;

        // A control with a styled property, placed on the window: registering a property is what
        // roots a type in Avalonia's process-wide registry, so a generation without one would
        // collect trivially and the measurement would prove nothing.
        string? controlFile = scaffolded ? await WriteReclaimControlAsync(project) : null;

        // Loaded without opening anything, so the first cases measure Avalonia and these
        // libraries rather than the designer's windows.
        designer.OpenAtStartup(project, [], active: null);

        if (!await Until(() => designer.IsLoaded && !designer.IsBusy, 240))
        {
            return Fail(ref failures, "the project never loaded");
        }

        designer.RestoreCommand.Execute(null);

        if (!await Until(() => !designer.IsBusy, 300, settle: true))
        {
            return Fail(ref failures, "the restore never finished");
        }

        designer.BuildCommand.Execute(null);

        if (!await Until(() => !designer.IsBusy, 300, settle: true))
        {
            return Fail(ref failures, "the build never finished");
        }

        if (Errors(designer).Any())
        {
            foreach (DiagnosticRow row in Errors(designer).Take(5))
            {
                Say($"  build error: {row.Message}");
            }

            return Fail(ref failures, "the scaffolded project did not build");
        }

        if (Grab(designer, "_workspace") is not ProjectWorkspace workspace
            || workspace.CurrentSnapshot is not { } snapshot
            || snapshot.Projects.Length == 0)
        {
            return Fail(ref failures, "no snapshot to build a generation from");
        }

        ProjectIdentity identity = snapshot.Projects[0].Identity;

        if (!scaffolded)
        {
            // A real project measures only the rung that matters for it: the studio's own state,
            // with whatever forms it has, in the shape a swap will meet.
            Say("— the studio's own generation, on this project —");

            if (!await MeasureStudioAsync(designer, "this project", placed: null))
            {
                Fail(ref failures, "the studio's own state kept the generation alive");
            }

            // And the arrangement the studio is actually in when it swaps: a build has just run,
            // in this process, over the very assemblies about to be given back.
            Say("— and again, with a build in between, which is what the studio really does —");

            designer.BuildCommand.Execute(null);

            if (!await Until(() => !designer.IsBusy, 300, settle: true))
            {
                return Fail(ref failures, "the build never finished");
            }

            if (!await MeasureStudioAsync(designer, "after a build", placed: null))
            {
                Fail(ref failures, "a build in this process kept the generation alive");
            }

            // And the arrangement a person is in, which is not the same as the one a script is in:
            // they clicked something. A selection is the studio holding a live control on purpose,
            // and holding one of a generation is what a swap has to survive.
            Say("— and once more, after a click on the canvas, which is what a person does —");

            if (!await MeasureStudioAsync(designer, "after a click", placed: null, click: true))
            {
                Fail(ref failures, "a click on the canvas kept the generation alive");
            }

            return failures;
        }

        // The ladder. Each rung adds one thing to the case below it, so a failure names its own
        // cause instead of leaving "something in the process" to guess at.
        Say("— case 1: a generation that only loaded an assembly —");

        if (!await MeasureGenerationAsync("case 1", snapshot, identity, document: null))
        {
            return Fail(ref failures, "a generation that created nothing would not leave the process — "
                + "the blocker is below Avalonia's own state, and no cleanup here can fix it");
        }

        Say("— case 2: a generation whose control was created and dropped —");

        if (!await MeasureGenerationAsync("case 2", snapshot, identity, controlFile))
        {
            return Fail(ref failures, "a generation that created one control would not leave the "
                + "process — this is the go/no-go rung: Avalonia roots it and the restart stands");
        }

        Say("— case 3: the generation the studio itself opened a form under —");

        if (!await MeasureStudioAsync(designer, "case 3"))
        {
            Fail(ref failures, "the studio's own state kept the generation alive");
        }

        await Until(() => designer.Forms.Count == 0, 60);

        // Whatever the ladder said, the other half is worth measuring: a successor built from
        // newer code, resolving where ADR 0021 measured "Unable to substitute".
        string source = await System.IO.File.ReadAllTextAsync(controlFile! + ".cs");

        await System.IO.File.WriteAllTextAsync(
            controlFile + ".cs", source.Replace("\"First\"", "\"Second\"", StringComparison.Ordinal));

        designer.BuildCommand.Execute(null);

        if (!await Until(() => !designer.IsBusy, 300, settle: true))
        {
            return Fail(ref failures, "the rebuild never finished");
        }

        if (!Open(designer, "MainWindow.axaml")
            || !await Until(
                () => designer.ActiveForm is { Session: not null, Problem: null } reopened
                    && reopened.Name == "MainWindow.axaml"
                    && OnCanvas(reopened, "ReclaimControl"),
                300))
        {
            return Fail(ref failures, "the window did not come back under the successor: "
                + (designer.ActiveForm?.Problem ?? "no form"));
        }

        FormViewModel reborn = designer.ActiveForm!;

        if (!await Until(() => PlacedControlShows(reborn, "Second"), 60))
        {
            Fail(ref failures, "the successor still shows the old default — stale types survived");
        }
        else
        {
            Say("the successor shows what the disk says, in the same process");
        }

        if (designer.Output.Any(static line =>
            line.Contains("Unable to substitute", StringComparison.OrdinalIgnoreCase)))
        {
            Fail(ref failures, "the compiler met two copies of one type — the process was not really cleared");
        }

        failures += await HandlerBesideAsync(reborn);

        return failures;
    }

    /// <summary>
    /// Changes the window's panel from outside, beside a button whose handler the window's class
    /// declares, and checks the change applied in place.
    /// </summary>
    /// <remarks>
    /// The window's class resolved under the successor, so its handlers are real: a panel holding one
    /// used to be refused as a part — it was built without the instance the handler names — and the
    /// designer rebuilt the whole form under a new session. Now the panel's content is rebuilt, the
    /// handler hooked up to the window the session populated, and the session stays.
    /// </remarks>
    private static async Task<int> HandlerBesideAsync(FormViewModel form)
    {
        var failures = 0;

        if (form.Session is not { } session
            || form.File.Value is not { Length: > 0 } file
            || form.Root is not Window window)
        {
            return Fail(ref failures, "the reopened window has no session, file or window to change beside a handler");
        }

        string written = await System.IO.File.ReadAllTextAsync(file);

        await System.IO.File.WriteAllTextAsync(
            file,
            written.Replace(
                "<Button x:Name=\"PingButton\"",
                "<TextBlock Text=\"Beside\" />\n    <Button x:Name=\"PingButton\"",
                StringComparison.Ordinal));

        if (!await Until(() => Text(form).Contains("Text=\"Beside\"", StringComparison.Ordinal)
                && OnCanvasText(form, "Beside"), 30))
        {
            return Fail(ref failures, "a change beside a handled button never reached the open window");
        }

        if (!ReferenceEquals(form.Session, session))
        {
            Fail(ref failures, "a change beside a handled button cost the form a new session");
        }

        if (Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(form.Card)
                .OfType<Button>()
                .FirstOrDefault(static button => button.Name == "PingButton") is not { } ping)
        {
            return Fail(ref failures, "the handled button is not on the canvas after the change");
        }

        ping.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        if (window.Title != "Pinged")
        {
            Fail(ref failures, "the rebuilt button's handler did not run on the window");
        }
        else if (failures == 0)
        {
            Say("a change beside a handled button applied in place: same session, and the handler runs on the window");
        }

        return failures;
    }

    /// <summary>Whether a text block saying exactly this is drawn on the form.</summary>
    private static bool OnCanvasText(FormViewModel form, string text) =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(form.Card)
            .OfType<TextBlock>()
            .Any(block => block.Text == text);

    /// <summary>
    /// Builds a generation, uses it as the case says, forgets it, and reports whether it died.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every local naming the generation is nulled before the measurement begins, and that is not
    /// tidiness: an async method's locals live in a state-machine object that is alive for as long
    /// as the method is, so a local still naming the context would be measuring this method's own
    /// frame and would report a live generation forever. The first version of this harness did
    /// exactly that.
    /// </para>
    /// </remarks>
    private static async Task<bool> MeasureGenerationAsync(
        string what, SolutionSnapshot snapshot, ProjectIdentity project, string? document)
    {
        (XamlLoadEnvironment? environment, ProjectAssemblyContext? generation) =
            ProjectXamlEnvironment.CreateFor(snapshot, project);

        string? failure = document is null
            ? LoadOnly(generation, project)
            : await CreateAndDropAsync(environment, document);

        if (failure is { Length: > 0 })
        {
            Say($"! {what}: {failure}");

            generation.Dispose();

            return false;
        }

        HashSet<string> names = LoadedNames(generation);

        if (names.Count == 0)
        {
            Say($"! {what}: the generation loaded nothing collectible — nothing to measure");

            generation.Dispose();

            return false;
        }

        // The environment first, and the reason is the same one the swap has to respect: its type
        // resolver was handed this generation's assemblies as a list to search.
        environment = null;

        long reclaiming = Stopwatch.GetTimestamp();
        bool gone = await generation.TryReclaimAsync();

        Say($"timing {what}: reclaim {Stopwatch.GetElapsedTime(reclaiming).TotalMilliseconds:F0} ms");

        generation = null;

        return Answered(what, gone, names);
    }

    /// <summary>Says what the reclaim answered, and — when it said no — what still holds it.</summary>
    private static bool Answered(string what, bool gone, HashSet<string> names)
    {
        Say(gone ? $"  {what}: provably dead" : $"  {what}: still held");

        if (!gone)
        {
            ProbeRoots(what, names);
        }

        return gone;
    }

    /// <summary>
    /// The simple names of the assemblies a generation has loaded into its own context.
    /// </summary>
    /// <remarks>
    /// Names rather than the assemblies themselves, because a list of the latter is exactly the
    /// kind of reference that makes a measurement report its own instrument.
    /// </remarks>
    private static HashSet<string> LoadedNames(ProjectAssemblyContext generation)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Grab(generation, "_context") is not AssemblyLoadContext context
            || Grab(generation, "_resolved") is not System.Collections.IDictionary resolved)
        {
            return names;
        }

        foreach (object? value in resolved.Values)
        {
            if (value is Assembly assembly
                && AssemblyLoadContext.GetLoadContext(assembly) == context
                && assembly.GetName().Name is { Length: > 0 } name)
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>The studio's own generation, retired the way a swap would retire it.</summary>
    /// <param name="designer">The studio to measure.</param>
    /// <param name="what">What to call this measurement in the log.</param>
    /// <param name="placed">
    /// The control the scaffolded form places, whose registration is what makes the measurement
    /// worth taking — or <see langword="null"/> for a real project, where whatever the forms draw
    /// is what there is.
    /// </param>
    /// <param name="click">
    /// Whether to select a control on the canvas first, the way a person does before wondering why
    /// their edit needs a restart.
    /// </param>
    private static async Task<bool> MeasureStudioAsync(
        DesignerViewModel designer, string what, string? placed = "ReclaimControl", bool click = false)
    {
        // Two forms, because that is how the studio is actually used — a form and the control it
        // places, open side by side — and because a swap that only ever met one would be a swap
        // measured under the easier arrangement.
        string[] wanted = placed is null
            ? [.. designer.ProjectForms.Take(2).Select(static form => form.Name)]
            : ["MainWindow.axaml", placed + ".axaml"];

        foreach (string name in wanted)
        {
            if (Open(designer, name))
            {
                await Until(
                    () => designer.Forms.Any(open =>
                        open.Name == name && open is { Session: not null, Problem: null }),
                    300);
            }
        }

        if (designer.Forms.Count == 0)
        {
            Say($"! {what}: nothing opened: " + (designer.ActiveForm?.Problem ?? "no form"));

            return false;
        }

        if (placed is not null && !PlacedTypeIsRegistered(designer.Forms[0]))
        {
            Say($"! {what}: the control's type never reached the property registry");

            return false;
        }

        Say($"  {what}: {designer.Forms.Count} form(s) open");

        if (click)
        {
            // After the canvas has laid out, because there is nothing to click before it has.
            await Until(() => Clickable(designer), 60);

            Say(ClickOnCanvas(designer)
                ? $"  {what}: selected a control the project's own code drew"
                : $"  {what}: nothing of the project's own was drawn to click");
        }

        // The designer's own teardown, and then the adapter's own reclaim: a harness that used a
        // second copy of either would be measuring something the studio does not do.
        long tearingDown = Stopwatch.GetTimestamp();

        await RunSwapTeardownAsync(designer);

        Say($"timing {what}: teardown {Stopwatch.GetElapsedTime(tearingDown).TotalMilliseconds:F0} ms");

        return await ReclaimStudioGenerationAsync(designer, what);
    }

    /// <summary>
    /// Selects a control the project's own code drew, the way a click on the canvas does.
    /// </summary>
    /// <remarks>
    /// Answers <see langword="bool"/> rather than handing back what it found, and does not inline:
    /// a <see cref="Control"/> of the dying generation in a local of the measuring frame would be
    /// a root the measurement brought with it.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ClickOnCanvas(DesignerViewModel designer)
    {
        foreach (FormViewModel form in designer.Forms)
        {
            if (Deepest(form) is not { } target)
            {
                continue;
            }

            designer.SelectFromCanvas(form, target);

            // And the half a view model never sees. A pointer press focuses what it landed on, and
            // the focus manager is a thing that lives as long as the window rather than as long as
            // the generation whose control it is now pointing at.
            target.Focus();

            return true;
        }

        return false;
    }

    /// <summary>Whether any open form has drawn something a click could land inside.</summary>
    private static bool Clickable(DesignerViewModel designer) =>
        designer.Forms.Any(static form => Deepest(form) is not null);

    /// <summary>
    /// The last control inside something the project's own code drew.
    /// </summary>
    /// <remarks>
    /// The deepest one, because that is what a pointer actually lands on — a text box inside a
    /// user control, not the user control — and the walk up to whatever the document mapped is
    /// the studio's own business, which is exactly the part being measured.
    /// </remarks>
    private static Control? Deepest(FormViewModel form)
    {
        Control? target = null;

        foreach (Control control in Avalonia.VisualTree.VisualExtensions
            .GetVisualDescendants(form.Card).OfType<Control>())
        {
            if (InsideTheProjectsOwn(control))
            {
                target = control;
            }
        }

        return target;
    }

    /// <summary>Whether a control is, or sits inside, something the project being edited declares.</summary>
    private static bool InsideTheProjectsOwn(Control control)
    {
        for (Control? current = control; current is not null; current = current.Parent as Control)
        {
            if (IsTheProjectsOwn(current.GetType()))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a type came from the generation rather than from the studio.
    /// </summary>
    /// <remarks>
    /// Asked of the load context, not of the name. A project called <c>AvaloniaApplication1</c> —
    /// which is what the template calls one — answers yes to every name test that tries to tell
    /// the studio's assemblies from the project's, and a measurement that skipped the project
    /// would report the arrangement it failed to set up as the arrangement being safe.
    /// </remarks>
    private static bool IsTheProjectsOwn(Type type) =>
        AssemblyLoadContext.GetLoadContext(type.Assembly) is { IsCollectible: true };

    /// <summary>Runs the swap's own release step, whatever it is called this week.</summary>
    private static async Task RunSwapTeardownAsync(DesignerViewModel designer)
    {
        if (designer.GetType().GetMethod(
            "LetGoOfEverythingAsync", BindingFlags.Instance | BindingFlags.NonPublic) is { } release)
        {
            await (Task)release.Invoke(designer, null)!;
        }
    }

    /// <summary>
    /// Asks the adapter to reclaim the studio's generation, in the order the swap asks in.
    /// </summary>
    private static async Task<bool> ReclaimStudioGenerationAsync(DesignerViewModel designer, string what)
    {
        if (Grab(designer, "_assemblies") is not ProjectAssemblyContext generation)
        {
            Say($"! {what}: no generation to reclaim");

            return false;
        }

        HashSet<string> names = LoadedNames(generation);

        // Fields first, environment included — see the swap, and ADR 0023.
        Put(designer, "_assemblies", null);
        Put(designer, "_environment", null);
        Put(designer, "_environmentProject", default(ProjectIdentity));

        long reclaiming = Stopwatch.GetTimestamp();
        bool gone = await generation.TryReclaimAsync();

        Say($"timing {what}: reclaim {Stopwatch.GetElapsedTime(reclaiming).TotalMilliseconds:F0} ms");

        generation = null!;

        return Answered(what, gone, names);
    }

    /// <summary>Whether the placed control's type is in the registry, holding no type afterwards.</summary>
    /// <remarks>
    /// A method of its own because a <see cref="Type"/> of the dying generation held in a local
    /// would be measured as a root: an async method's locals outlive their last use, and this one
    /// would be alive for the whole measurement.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool PlacedTypeIsRegistered(FormViewModel form) =>
        Drawn(form, "ReclaimControl")?.GetType() is { } placed && RegistryHolds(placed);

    /// <summary>Loads the project's own output and creates nothing from it.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string? LoadOnly(ProjectAssemblyContext generation, ProjectIdentity project)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(project.ProjectFilePath.FileName);

        return generation.Resolve(new AssemblyName(name)) is null
            ? $"the generation could not load {name}"
            : null;
    }

    /// <summary>
    /// Creates the document's objects, touches them, and lets go of every one of them.
    /// </summary>
    /// <remarks>
    /// A method of its own so that the session, the root object and the load result are locals of
    /// a frame that has ended by the time anything is measured.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<string?> CreateAndDropAsync(XamlLoadEnvironment environment, string document)
    {
        string text = await System.IO.File.ReadAllTextAsync(document);

        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            XamlDocument.Parse(text, new XamlParseOptions { DocumentUri = new Uri(document) }),
            environment,
            new XamlLoadOptions { Mode = XamlLoadMode.Design });

        if (session is null)
        {
            return string.Join(
                "; ",
                result.Diagnostics.Where(static d => d.IsError).Select(static d => d.Message)
                    .DefaultIfEmpty("no diagnostic said why"));
        }

        // Registering a styled property is what roots a type in Avalonia's registry, and creating
        // one instance is what runs the static constructor that does it.
        string? note = RegistryHolds(session.RootObject.GetType())
            ? null
            : "the created control's type never reached the property registry";

        await session.DisposeAsync();

        return note;
    }

    /// <summary>
    /// Names what still mentions a dying generation, by sweeping the process's static state.
    /// </summary>
    /// <remarks>
    /// Only run when a measurement failed, and only to answer the one question worth answering
    /// then: which static field is holding the generation. Reading a static field runs its type's
    /// initialiser, which is why this is a diagnostic rather than something the product does.
    /// </remarks>
    private static void ProbeRoots(string what, HashSet<string> suspects)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var reported = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(object Value, string Path)>();
        var fields = new Dictionary<Type, FieldInfo[]>();

        // What the walk spent itself on, so that a walk that gives up says where the graph is big.
        var spent = new Dictionary<Type, int>();

        // Enough to cross a studio with forms open, and not enough to spend minutes doing it. A
        // walk that gives up says so; a walk that runs for five minutes on the UI thread looks
        // like a hang, and a diagnostic nobody waits for answers nothing.
        var budget = 2_000_000;

        // The application is seeded by name as well as swept for, because the interesting paths run
        // through the studio's own visual tree — application, lifetime, window, editor, item, view
        // model — and a path that says so reads better than the static it also hangs from.
        Seed(Application.Current, "Application.Current");

        foreach (Assembly probe in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (reported.Count >= 20)
            {
                break;
            }

            // The generation's own statics are not roots of it: they live in the context that is
            // being unloaded and go when it goes. Walking them anyway is how a project that the
            // template called AvaloniaApplication1 gets its own compiler-generated singletons
            // reported as the thing holding it — a name test cannot tell the two apart, and the
            // load context can.
            if (AssemblyLoadContext.GetLoadContext(probe) is { IsCollectible: true })
            {
                continue;
            }

            // Every assembly but the engines', the runtime's own included: a timer, a thread-pool
            // queue or a task continuation keeps whatever its delegate closed over, and those live
            // in the runtime's statics rather than in anything of Avalonia's or the studio's.
            if (probe.GetName().Name is not { } || IsEngine(probe))
            {
                continue;
            }

            foreach (Type type in SafeTypes(probe))
            {
                if (type.IsGenericTypeDefinition || reported.Count >= 20)
                {
                    continue;
                }

                FieldInfo[] statics;

                try
                {
                    statics = type.GetFields(
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (FieldInfo field in statics)
                {
                    try
                    {
                        Seed(field.GetValue(null), $"{type.Name}.{field.Name}");
                    }
                    catch (Exception)
                    {
                        // A static nobody can read is a static nobody is rooting anything with.
                    }
                }
            }
        }

        Drain();

        if (reported.Count == 0)
        {
            Say(budget <= 0
                ? $"  {what}: the walk ran out of budget before it could say — inconclusive"
                : $"  {what}: walked {visited.Count} object(s) and none of them holds it — "
                    + "the root is a stack slot, a thread-static, or native");

            if (budget <= 0)
            {
                Say($"  {what}: the budget went on " + string.Join(", ", spent
                    .OrderByDescending(static pair => pair.Value)
                    .Take(8)
                    .Select(static pair => $"{pair.Key.Name} ×{pair.Value}")));
            }
        }

        // Breadth first, and with no depth limit. Depth first with a limit answers a different
        // question than the one being asked: it says "not within fourteen hops of a seed", which
        // reads exactly like "nothing holds it" and is not the same statement. Breadth also names
        // the shortest path to a holder, which is the one worth printing.
        void Drain()
        {
            while (queue.Count > 0 && budget-- > 0 && reported.Count < 20)
            {
                (object value, string path) = queue.Dequeue();

                spent[value.GetType()] = spent.GetValueOrDefault(value.GetType()) + 1;

                switch (value)
                {
                    case Type type when Suspect(type):
                        Report($"{path} → typeof({type.Name})");
                        continue;

                    case AvaloniaProperty property when Suspect(property.OwnerType):
                        Report($"{path} → {property.OwnerType.Name}.{property.Name}");
                        continue;

                    case Type or AvaloniaProperty or string:
                        continue;
                }

                if (Suspect(value.GetType()))
                {
                    Report($"{path} → an instance of {value.GetType().Name}");

                    continue;
                }

                Type valueType = value.GetType();

                // An array of numbers holds no references, and enumerating one boxes every element
                // into an object of its own: a font's or a bitmap's buffer would spend the budget a
                // byte at a time.
                if (valueType.IsPrimitive
                    || (valueType.IsArray && valueType.GetElementType() is { IsPrimitive: true })
                    || IsEngine(valueType.Assembly))
                {
                    continue;
                }

                // A conditional weak table keeps a value alive exactly as long as its key. An entry
                // keyed by something of the generation is no root of it, and is skipped; one keyed by
                // something that outlives the generation holds the value as firmly as a field would —
                // and a walk that skipped every table could not see that.
                if (value.GetType() is { IsGenericType: true } table
                    && table.GetGenericTypeDefinition() == typeof(ConditionalWeakTable<,>))
                {
                    try
                    {
                        foreach (object? entry in (System.Collections.IEnumerable)value)
                        {
                            if (entry is null
                                || entry.GetType().GetProperty("Key")?.GetValue(entry) is not { } key
                                || Suspect(key as Type ?? key.GetType()))
                            {
                                continue;
                            }

                            Seed(entry.GetType().GetProperty("Value")?.GetValue(entry), $"{path}{{weak key {key.GetType().Name}}}");
                        }
                    }
                    catch (Exception)
                    {
                    }

                    continue;
                }

                switch (value)
                {
                    // A subscription is a reference like any other, and the commonest one a person
                    // makes by accident: what the delegate closed over is reached through here or
                    // not at all.
                    case Delegate handler:
                        foreach (Delegate one in handler.GetInvocationList())
                        {
                            Seed(one.Target, $"{path}→{one.Method.DeclaringType?.Name}.{one.Method.Name}()");
                        }

                        continue;

                    case System.Collections.IDictionary map:
                        try
                        {
                            foreach (System.Collections.DictionaryEntry entry in map)
                            {
                                Seed(entry.Key, path + "{key}");
                                Seed(entry.Value, path + "{value}");
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            // Mutated while being read; a diagnostic does not get to insist.
                        }

                        continue;

                    case System.Collections.IEnumerable sequence:
                        try
                        {
                            foreach (object? item in sequence)
                            {
                                Seed(item, path + "[]");
                            }
                        }
                        catch (Exception)
                        {
                        }

                        continue;

                    default:
                        foreach (FieldInfo field in FieldsOf(value.GetType()))
                        {
                            try
                            {
                                Seed(field.GetValue(value), $"{path}.{field.Name}");
                            }
                            catch (Exception)
                            {
                            }
                        }

                        continue;
                }
            }
        }

        // Claimed on the way in rather than on the way out. A queue that admits an object once per
        // reference to it holds one entry — and one freshly built path string — per edge in the
        // graph rather than per object, which is the difference between a walk and an out-of-memory.
        // Strings and the types reported by name are let through, because they are examined and
        // never traversed.
        // Types repeat and their field lists do not change, so asking reflection once per type
        // rather than once per object is most of the difference between a walk that answers and a
        // walk somebody gives up on.
        FieldInfo[] FieldsOf(Type type)
        {
            if (fields.TryGetValue(type, out FieldInfo[]? known))
            {
                return known;
            }

            try
            {
                known = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch (Exception)
            {
                known = [];
            }

            fields[type] = known;

            return known;
        }

        // Once per object, types and properties included: a type is reported by the first — the
        // shortest — path that reaches it, and admitting it again per reference spent a budget of
        // two million on a few hundred types. A number, an enum or a pointer holds no reference,
        // and reading a pointer field boxes a new one every time, so none of them is admitted.
        void Seed(object? value, string path)
        {
            if (value is null or Pointer || value.GetType() is { IsPrimitive: true } or { IsEnum: true })
            {
                return;
            }

            if (visited.Add(value))
            {
                queue.Enqueue((value, path));
            }
        }

        // By name, because holding the assemblies themselves would make the probe part of what it
        // is looking for.
        bool Suspect(Type type) =>
            type.Assembly.GetName().Name is { } name && suspects.Contains(name);

        // MSBuild's and NuGet's object graphs are most of the process once a project is open —
        // evaluated projects, item caches, the SDK's resolved imports — and none of it can hold a
        // control. Walking them spent the whole budget before the walk reached anything that can.
        static bool IsEngine(Assembly assembly) =>
            assembly.GetName().Name is { } name
            && (name.StartsWith("Microsoft.Build", StringComparison.Ordinal)
                || name.StartsWith("NuGet.", StringComparison.Ordinal));

        void Report(string path)
        {
            if (reported.Add(path))
            {
                Say($"  {what}: held by {path}");
            }
        }
    }

    /// <summary>Writes a control with a styled property beside the window, and places it there.</summary>
    private static async Task<string> WriteReclaimControlAsync(string project)
    {
        string views = System.IO.Path.GetDirectoryName(
            System.IO.Directory.GetFiles(
                System.IO.Path.GetDirectoryName(project)!,
                "MainWindow.axaml",
                System.IO.SearchOption.AllDirectories).First())!;

        string windowFile = System.IO.Path.Combine(views, "MainWindow.axaml");
        string text = await System.IO.File.ReadAllTextAsync(windowFile);

        int classAt = text.IndexOf("x:Class=\"", StringComparison.Ordinal);
        int closing = text.LastIndexOf("</StackPanel>", StringComparison.Ordinal);
        int root = text.IndexOf("<Window", StringComparison.Ordinal);

        if (classAt < 0 || closing < 0 || root < 0)
        {
            throw new InvalidOperationException("the template window lost the shape this run writes into");
        }

        classAt += "x:Class=\"".Length;

        string full = text[classAt..text.IndexOf('"', classAt)];
        string space = full[..full.LastIndexOf('.')];

        string file = System.IO.Path.Combine(views, "ReclaimControl.axaml");

        await System.IO.File.WriteAllTextAsync(
            file,
            $$"""
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:v="using:{{space}}"
                         x:Class="{{space}}.ReclaimControl"
                         x:DataType="v:ReclaimControl">
              <Border Background="#333B4A" Padding="16">
                <TextBlock Text="{Binding HelloText}" />
              </Border>
            </UserControl>
            """);

        await System.IO.File.WriteAllTextAsync(
            file + ".cs",
            $$"""
            using Avalonia;
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml;

            namespace {{space}};

            public partial class ReclaimControl : UserControl
            {
                public static readonly StyledProperty<string> HelloTextProperty =
                    AvaloniaProperty.Register<ReclaimControl, string>(nameof(HelloText), defaultValue: "First");

                public string HelloText
                {
                    get => GetValue(HelloTextProperty);
                    set => SetValue(HelloTextProperty, value);
                }

                public ReclaimControl()
                {
                    AvaloniaXamlLoader.Load(this);
                    DataContext = this;
                }
            }
            """);

        // And a button the window's class handles, which the last step rebuilds beside: a handler the
        // class declares is what a part rebuilt on its own could not carry.
        text = text.Insert(closing, "    <v:ReclaimControl />\n    <Button x:Name=\"PingButton\" Content=\"Ping\" Click=\"Ping\" />\n")
            .Insert(root + "<Window".Length, $" xmlns:v=\"using:{space}\"");

        await System.IO.File.WriteAllTextAsync(windowFile, text);

        string codeBehind = windowFile + ".cs";
        string code = await System.IO.File.ReadAllTextAsync(codeBehind);

        await System.IO.File.WriteAllTextAsync(
            codeBehind,
            code.Replace(
                "public MainWindow() => InitializeComponent();",
                "public MainWindow() => InitializeComponent();\n\n"
                    + "    private void Ping(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Title = \"Pinged\";",
                StringComparison.Ordinal));

        return file;
    }

    /// <summary>Whether the placed control is drawing a text block saying exactly this.</summary>
    private static bool PlacedControlShows(FormViewModel form, string text) =>
        Drawn(form, "ReclaimControl") is { } placed
            && Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(placed)
                .OfType<TextBlock>()
                .Any(block => block.Text == text);

    private static object? Grab(object target, string field) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target);

    private static void Put(object target, string field, object? value) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException incomplete)
        {
            return incomplete.Types.OfType<Type>();
        }
    }

    /// <summary>Whether Avalonia's property registry holds an entry for this type.</summary>
    private static bool RegistryHolds(Type type)
    {
        object? instance = Type.GetType("Avalonia.AvaloniaPropertyRegistry, Avalonia.Base")
            ?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null);

        return instance?.GetType()
                .GetField("_registered", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(instance) is System.Collections.IDictionary registered
            && registered.Contains(type);
    }

    private static async Task StressRunAsync(Window window, DesignerViewModel designer, string folder)
    {
        var failures = 0;

        try
        {
            failures = await StressAsync(window, designer, folder);
        }
        catch (Exception error)
        {
            Say($"the stress run itself failed: {error.GetType().Name}: {error.Message}");

            failures++;
        }

        Say(failures == 0
            ? "VERDICT ok — the designer survived every cycle"
            : $"VERDICT {failures} step(s) failed");

        window.Close();
    }

    /// <summary>
    /// The endurance cycle: edit, be edited from outside, delete bound blocks, repeat.
    /// </summary>
    /// <remarks>
    /// The designer's real life compressed: a window-rooted form with data bindings, edited here and
    /// in the other editor by turns, with whole bound subtrees deleted between rounds. What it
    /// asserts at every step is the invariant the designer lives by — the document, the live tree
    /// and the panels never disagree, and nothing on the way prints an error.
    /// </remarks>
    private static async Task<int> StressAsync(Window window, DesignerViewModel designer, string folder)
    {
        // Held under a second name because a local further down is also called `window` — that one
        // is a form, this one is the studio, and the grouping step at the end needs the studio's.
        Window studio = window;

        var failures = 0;

        // A studio that replaced itself mid-story would take the story with it. The reload's
        // detection still runs; what is asserted is the banner, not the act.
        designer.AutoReloadForCode = false;

        // The blank template's window: a Window root, a StackPanel, and two TextBlocks bound to
        // Title and Greeting — data bindings from the first minute, which is the point.
        string project = await ProjectScaffold.CreateAsync(folder, "StressApp", CancellationToken.None);

        // A control of the project's own, written before the studio opens it: the types a run holds
        // are the ones the project had when it was opened, so a control placed on a form has to be
        // part of that build. Creating one mid-run is the case the studio answers with a restart,
        // and this step checks that answer separately, below.
        string controlFile = await WriteControlAsync(project);

        designer.OpenAtStartup(project, "MainWindow.axaml");

        if (!await Until(() => designer.ActiveForm is { Session: not null, Problem: null }, 300))
        {
            return Fail(ref failures, "the window never opened: "
                + (designer.ActiveForm?.Problem ?? "no form"));
        }

        FormViewModel form = designer.ActiveForm!;
        string file = form.File.Value;
        int errorsSeen = CountErrors(designer);

        for (var cycle = 1; cycle <= 3; cycle++)
        {
            Say($"— cycle {cycle} —");

            // 1. Several designer edits in a row: three controls dropped, then the window's own
            //    Title through the inspector — an edit on the root itself.
            foreach (string control in new[] { "Button", "TextBox", "CheckBox" })
            {
                if (designer.Toolbox.FirstOrDefault(entry => entry.Name == control) is not { } tool)
                {
                    Fail(ref failures, $"no {control} in the toolbox");

                    continue;
                }

                int before = Count(designer, control);

                if (!await Until(() => Find(designer, "StackPanel") is not null, 30))
                {
                    Fail(ref failures, $"cycle {cycle}: no panel to drop {control} into");

                    continue;
                }

                designer.Drop(form, tool, over: Find(designer, "StackPanel"), at: new Point(40, 40));

                if (!await Until(() => Count(designer, control) > before, 30))
                {
                    Fail(ref failures, $"cycle {cycle}: {control} never reached the document");
                }
            }

            if (form.Root is { } root)
            {
                designer.SelectFromCanvas(form, root);

                if (designer.Properties.FirstOrDefault(row => row.Name == "Title") is { } title)
                {
                    title.Value = $"Cycle {cycle}";

                    if (!await Until(
                        () => Text(form).Contains($"Title=\"Cycle {cycle}\"", StringComparison.Ordinal),
                        30))
                    {
                        Fail(ref failures, $"cycle {cycle}: the Title edit never reached the document");
                    }
                }
                else
                {
                    Fail(ref failures, $"cycle {cycle}: the inspector offers no Title for the window");
                }
            }

            designer.SaveCommand.Execute(null);

            await Until(() => !form.IsDirty, 60);

            // 2. The other editor's turn: a whole bound block appended inside the panel, written to
            //    disk the way a save is written.
            string block =
                $"    <StackPanel x:Name=\"OutsideBlock{cycle}\" Spacing=\"4\">\n"
                + "      <TextBlock Text=\"{Binding Greeting}\" />\n"
                + "      <TextBox Text=\"{Binding Title}\" />\n"
                + "      <Button Content=\"{Binding Title}\" />\n"
                + "    </StackPanel>\n";

            string text = await System.IO.File.ReadAllTextAsync(file);
            int closing = text.LastIndexOf("</StackPanel>", StringComparison.Ordinal);

            if (closing < 0)
            {
                Fail(ref failures, $"cycle {cycle}: the form has no panel to write into from outside");

                continue;
            }

            await System.IO.File.WriteAllTextAsync(file, text[..closing] + block + text[closing..]);

            if (!await Until(
                () => Text(form).Contains($"OutsideBlock{cycle}", StringComparison.Ordinal), 60))
            {
                Fail(ref failures, $"cycle {cycle}: the outside edit never arrived");

                continue;
            }

            if (form.IsDirty)
            {
                Fail(ref failures, $"cycle {cycle}: a reloaded form calls itself edited");
            }

            if (form.Problem is { } problem)
            {
                Fail(ref failures, $"cycle {cycle}: the reload failed: {problem}");
            }

            // The block is not just text in a file — it is live on the canvas, bindings and all.
            if (!await Until(() => LiveByName(designer, $"OutsideBlock{cycle}") is not null, 30))
            {
                Fail(ref failures, $"cycle {cycle}: the outside block never became live objects");
            }

            // 3. The bound block goes, as a block: one delete for the panel takes its three bound
            //    children with it.
            if (LiveByName(designer, $"OutsideBlock{cycle}") is { } live)
            {
                designer.SelectFromCanvas(form, live);

                designer.DeleteSelectedCommand.Execute(null);

                if (!await Until(
                    () => !Text(form).Contains($"OutsideBlock{cycle}", StringComparison.Ordinal), 30))
                {
                    Fail(ref failures, $"cycle {cycle}: the bound block would not delete");
                }
            }

            // 4. And back, and gone again — history over a document another editor wrote.
            designer.UndoCommand.Execute(null);

            if (!await Until(
                () => Text(form).Contains($"OutsideBlock{cycle}", StringComparison.Ordinal), 30))
            {
                Fail(ref failures, $"cycle {cycle}: undo did not bring the block back");
            }

            designer.RedoCommand.Execute(null);

            if (!await Until(
                () => !Text(form).Contains($"OutsideBlock{cycle}", StringComparison.Ordinal), 30))
            {
                Fail(ref failures, $"cycle {cycle}: redo did not take the block away again");
            }

            designer.SaveCommand.Execute(null);

            await Until(() => !form.IsDirty, 60);

            // 5. Nothing along the way said "!", and the form still loads clean.
            int errorsNow = CountErrors(designer);

            if (errorsNow > errorsSeen)
            {
                Fail(ref failures,
                    $"cycle {cycle}: {errorsNow - errorsSeen} error line(s) — see the console");

                errorsSeen = errorsNow;
            }

            Say($"cycle {cycle} done: {Summary(designer)}");
        }

        // What the cycles produced is still an application: build it and start it.
        designer.BuildCommand.Execute(null);

        if (!await Until(() => !designer.IsBusy, 300, settle: true))
        {
            Fail(ref failures, "the final build never finished");
        }

        if (Errors(designer).Any())
        {
            Fail(ref failures, "the final build failed");
        }

        designer.RunCommand.Execute(null);

        if (!await Until(() => designer.IsRunning, 300))
        {
            Fail(ref failures, "the application the cycles produced never started");
        }
        else
        {
            await Task.Delay(2000);

            if (!designer.IsRunning)
            {
                Fail(ref failures, "the application exited on its own");
            }

            designer.StopCommand.Execute(null);

            await Until(() => !designer.IsRunning, 30);
        }

        // 12. The palette and the inspector at the depth building an application needs: a Grid
        //     whose rows are written through the row editor, an items control that drops with
        //     visible items, and a control whose package the project does not have — refused in
        //     words, with the file untouched.
        if (designer.Toolbox.FirstOrDefault(entry => entry.Name == "Grid") is { } gridTool)
        {
            designer.Drop(form, gridTool, over: Find(designer, "StackPanel"), at: new Point(20, 20));

            if (!await Until(() => Find(designer, "Grid") is not null, 30))
            {
                Fail(ref failures, "the Grid never reached the document");
            }
            else
            {
                designer.SelectFromCanvas(form, Find(designer, "Grid")!);

                if (designer.Properties.FirstOrDefault(row => row.Name == "RowDefinitions") is { } rows)
                {
                    rows.Value = "Auto,*";

                    if (!await Until(
                        () => Text(form).Contains("RowDefinitions=\"Auto,*\"", StringComparison.Ordinal),
                        30))
                    {
                        Fail(ref failures, "the RowDefinitions edit never reached the document");
                    }
                    else
                    {
                        Say("a Grid's rows are written through the inspector");
                    }
                }
                else
                {
                    Fail(ref failures, "the inspector offers no RowDefinitions for a Grid");
                }
            }
        }

        if (designer.Toolbox.FirstOrDefault(entry => entry.Name == "ComboBox") is { } comboTool)
        {
            designer.Drop(form, comboTool, over: Find(designer, "StackPanel"), at: new Point(20, 20));

            if (!await Until(
                () => Text(form).Contains("<ComboBoxItem", StringComparison.Ordinal)
                    && Find(designer, "ComboBox") is not null,
                30))
            {
                Fail(ref failures, "the ComboBox did not drop with its starter items");
            }
            else
            {
                Say("an items control drops with visible items");
            }
        }

        if (designer.Toolbox.FirstOrDefault(entry => entry.Name == "DataGrid") is { } gridlessTool)
        {
            string beforeRefusal = Text(form);

            designer.Drop(form, gridlessTool, over: Find(designer, "StackPanel"), at: new Point(20, 20));

            if (!await Until(
                () => designer.Output.Any(line =>
                    line.Contains("Avalonia.Controls.DataGrid", StringComparison.Ordinal)),
                30))
            {
                Fail(ref failures, "a DataGrid without its package was not refused in words");
            }
            else if (Text(form) != beforeRefusal)
            {
                Fail(ref failures, "the refused DataGrid still reached the document");
            }
            else
            {
                Say("a control whose package is missing is refused, and the file is untouched");
            }
        }

        // 13. A resize of a form that states only design sizes — the way every template writes its
        //     windows. The card, the document and the live window must end up saying one number.
        string designSized = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(project!)!, "DesignSized.axaml");

        await System.IO.File.WriteAllTextAsync(
            designSized,
            """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
                    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                    mc:Ignorable="d" d:DesignWidth="300" d:DesignHeight="200">
              <Canvas />
            </Window>
            """);

        if (!await Until(
            () => System.Linq.Enumerable.Any(designer.ProjectFiles, tile => tile.Name == "DesignSized.axaml"),
            180))
        {
            Fail(ref failures, "the design-sized form never appeared in the project");
        }
        else
        {
            designer.OpenFile(System.Linq.Enumerable.First(
                designer.ProjectFiles, tile => tile.Name == "DesignSized.axaml"));

            if (!await Until(
                () => designer.ActiveForm is { Problem: null, Root: not null } sized
                    && sized.Name == "DesignSized.axaml",
                120))
            {
                Fail(ref failures, "the design-sized form would not open");
            }
            else
            {
                FormViewModel sized = designer.ActiveForm!;
                UiDesignerView? editor = window.FindControl<UiDesignerView>("Surface");

                sized.Width = 520;
                sized.Height = 300;

                Control? card = null;

                if (!await Until(
                    () => (card = editor?.ContainerFromItem(sized) as Control) is { Bounds.Width: > 519 },
                    30))
                {
                    Fail(ref failures, "the card never took the new size");
                }
                else
                {
                    designer.WriteGeometry(sized, card!, moved: false, resized: true);

                    if (!await Until(
                        () => sized.Root is { Width: > 519 and < 521, Height: > 299 and < 301 }, 30))
                    {
                        Fail(
                            ref failures,
                            "after the resize was written, the live window says "
                                + $"{sized.Root?.Width}×{sized.Root?.Height} while the card says 520×300");
                    }
                    else
                    {
                        Say("a design-sized window resizes: card, document and live window agree");

                        // And an undone resize takes the card back with it. Undo rolls the document
                        // to the old size and the design values are applied to the live window
                        // again — the card used to stay where the drag left it, and the form
                        // overflowed its own frame the moment somebody edited anything else.
                        designer.UndoCommand.Execute(null);

                        if (!await Until(
                            () => sized.Root is { Width: > 299 and < 301 }
                                && sized.Width is > 299 and < 301
                                && sized.Height is > 199 and < 201,
                            30))
                        {
                            Fail(
                                ref failures,
                                "after undoing the resize the card says "
                                    + $"{sized.Width}×{sized.Height} while the window says "
                                    + $"{sized.Root?.Width}×{sized.Root?.Height}");
                        }
                        else
                        {
                            Say("an undone resize takes the card back with the document");

                            // The inspector is the other door to the same size. On a root that
                            // states only design sizes, the Width row must show the size the
                            // designer shows — not sit empty beside a canvas visibly 300 wide —
                            // and writing it must reach the design attribute, or the edit would
                            // be overruled by the next update the way the drag once was.
                            designer.SelectFromCanvas(sized, sized.Root!);

                            PropertyRow? width = designer.Properties.FirstOrDefault(
                                row => row.Name == "Width");

                            if (width is null || width.Value != "300")
                            {
                                Fail(ref failures, "the Width row of a design-sized root shows "
                                    + $"'{width?.Value}' while the designer shows 300");
                            }
                            else
                            {
                                width.Value = "480";

                                if (!await Until(
                                    () => sized.Root is { Width: > 479 and < 481 }
                                        && Text(sized).Contains(
                                            "d:DesignWidth=\"480\"", StringComparison.Ordinal),
                                    30))
                                {
                                    Fail(ref failures, "a Width typed into the inspector did not "
                                        + "reach the design size: the window says "
                                        + $"{sized.Root?.Width} and the document says "
                                        + (Text(sized).Contains("d:DesignWidth=\"480\"", StringComparison.Ordinal)
                                            ? "480" : "something else"));
                                }
                                else
                                {
                                    Say("the inspector edits the size the designer shows");
                                }
                            }
                        }
                    }
                }

                designer.CloseForm(sized);
            }
        }

        failures += await EmbeddedControlAsync(designer, form, controlFile);
        failures += await GroupingAsync(studio, designer);

        return failures;
    }

    /// <summary>
    /// 14. A project's own control, placed on another form — the designer's hardest ordinary case.
    /// </summary>
    /// <remarks>
    /// Everything about an embedded control crosses a boundary: its instance says it came from its
    /// own document, so the object map refuses it; its rendering comes from the compiled assembly,
    /// so a saved edit is invisible until a rebuild; and a rebuild makes a second copy of the
    /// assembly, which is where "unable to substitute MyControl with MyControl" lived. This step
    /// walks the whole story: place it, select it, inspect it, edit and save its source, see the
    /// placement update, survive a reopen after an outside code edit, and delete it.
    /// </remarks>
    private static async Task<int> EmbeddedControlAsync(
        DesignerViewModel designer, FormViewModel form, string controlFile)
    {
        var failures = 0;

        if (form.Document?.Root?.GetDirective("Class") is not { Length: > 0 } mainClass)
        {
            return Fail(ref failures, "the stress window declares no class to derive a namespace from");
        }

        string space = mainClass[..mainClass.LastIndexOf('.')];

        // Saved and closed before anything is written outside: an open dirty form rightly refuses
        // an outside overwrite, and the placement is written into the file while it is closed.
        designer.SaveCommand.Execute(null);

        if (!await Until(() => !form.IsDirty, 60))
        {
            return Fail(ref failures, "the window would not save before the embedded step");
        }

        designer.CloseForm(form);

        // Placed the way the other editor would place it: written into the file while the form is
        // closed. The open below is what builds the project, which is what makes the type exist.
        string text = await System.IO.File.ReadAllTextAsync(form.File.Value);

        // From a clean document. The file on disk still carries whatever the round trip above saved
        // before it ungrouped — the ungroup was never saved — and inserting a second mark into an
        // element that already has one produces a document with two of the same attribute, which is
        // ambiguous rather than stale. That is a fixture that tests itself, and it did.
        text = System.Text.RegularExpressions.Regex.Replace(text, " ?d:DesignGroup=\"[^\"]*\"", "");
        int closing = text.LastIndexOf("</StackPanel>", StringComparison.Ordinal);
        int root = text.IndexOf("<Window", StringComparison.Ordinal);

        if (closing < 0 || root < 0)
        {
            return Fail(ref failures, "the stress window lost the shape this step writes into");
        }

        text = text.Insert(closing, "    <v:MyControl x:Name=\"Embedded\" />\n")
            .Insert(root + "<Window".Length, $" xmlns:v=\"using:{space}\"");

        await System.IO.File.WriteAllTextAsync(form.File.Value, text);

        if (!Open(designer, form.Name))
        {
            return Fail(ref failures, "the window left the project when it was closed");
        }

        if (!await Until(
            () => designer.ActiveForm is { Problem: null, Root: not null } reopened
                && reopened.Name == form.Name
                && LiveByName(designer, "Embedded") is not null,
            300))
        {
            return Fail(ref failures, "the window did not come back with the embedded control live: "
                + (designer.ActiveForm?.Problem ?? "no problem reported"));
        }

        FormViewModel window = designer.ActiveForm!;

        // Selecting the embedded control must select it, not the window around it.
        designer.SelectFromCanvas(window, LiveByName(designer, "Embedded"));

        if (designer.Selected?.Name.LocalName != "MyControl")
        {
            Fail(ref failures, "clicking the embedded control selected "
                + (designer.Selected?.Name.ToString() ?? "nothing"));
        }
        else if (!System.Linq.Enumerable.Any(designer.Properties, row => row.Name == "Width"))
        {
            Fail(ref failures, "the embedded control's inspector offers no Width");
        }
        else
        {
            Say("a placed project control is selectable and inspectable");
        }

        // Edit the control's own form and save: the placement must take the new shape without
        // anybody pressing Build.
        if (!await Until(() => Open(designer, "MyControl.axaml"), 120))
        {
            return Fail(ref failures, "MyControl.axaml never appeared in the project");
        }

        if (!await Until(
            () => designer.ActiveForm is { Problem: null, Root: not null } opened
                && opened.Name == "MyControl.axaml",
            300))
        {
            return Fail(ref failures, "the control's own form would not open: "
                + (designer.ActiveForm?.Problem ?? "no problem reported"));
        }

        FormViewModel control = designer.ActiveForm!;

        designer.SelectFromCanvas(control, Find(designer, "TextBlock"));

        if (System.Linq.Enumerable.FirstOrDefault(
            designer.Properties, row => row.Name == "Text") is not { } textRow)
        {
            Fail(ref failures, "the control's TextBlock offers no Text row");
        }
        else
        {
            textRow.Value = "Second";

            if (!await Until(
                () => Text(control).Contains("Second", StringComparison.Ordinal), 30))
            {
                Fail(ref failures, "the Text edit never reached the control's document");
            }
        }

        designer.SaveCommand.Execute(null);

        // The placed copy follows the live document now — ADR 0022. Bring the window forward and
        // the embedded control must already be showing the edit: no build, no reload, no banner.
        if (!Open(designer, form.Name))
        {
            return Fail(ref failures, "the window left the project after the control edit");
        }

        if (!await Until(
            () => designer.ActiveForm is { } shown
                && shown.Name == form.Name
                && Drawn(shown, "MyControl") is { } placed
                && Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(placed)
                    .OfType<TextBlock>()
                    .Any(text => text.Text == "Second"),
            60))
        {
            Fail(ref failures, "the placed copy did not follow the control's document");
        }
        else
        {
            Say("a placed control follows its document — no rebuild, no restart");
        }

        // The exact reported break: edit the code-behind outside, close the form, open it again.
        // A second generation used to be made here, and the two copies of one assembly met as
        // "Unable to substitute MyControl with MyControl".
        designer.CloseForm(designer.Forms.First(f => f.Name == "MyControl.axaml"));

        await System.IO.File.AppendAllTextAsync(controlFile + ".cs", "\n// touched outside\n");

        if (!Open(designer, "MyControl.axaml"))
        {
            return Fail(ref failures, "MyControl.axaml left the project before the reopen");
        }

        if (!await Until(
            () => designer.ActiveForm is { Problem: null, Root: not null } back
                && back.Name == "MyControl.axaml",
            300))
        {
            Fail(ref failures, "reopening after an outside code edit failed: "
                + (designer.ActiveForm?.Problem ?? "no problem reported"));
        }
        else
        {
            Say("a reopen after an outside code edit still loads");
        }

        // And the placement deletes like anything else.
        if (!Open(designer, form.Name))
        {
            return Fail(ref failures, "the window left the project before the delete");
        }

        if (!await Until(
            () => designer.ActiveForm is { } active && active.Name == form.Name
                && LiveByName(designer, "Embedded") is not null,
            60))
        {
            return Fail(ref failures, "the window would not come forward for the delete");
        }

        FormViewModel target = designer.ActiveForm!;

        designer.SelectFromCanvas(target, LiveByName(designer, "Embedded"));
        designer.DeleteSelectedCommand.Execute(null);

        if (!await Until(
            () => !Text(target).Contains("<v:MyControl", StringComparison.Ordinal), 30))
        {
            Fail(ref failures, "the embedded control would not delete");
        }
        else if (!await Until(() => !OnCanvas(target, "MyControl"), 30, settle: true))
        {
            // The document is not the canvas. A session that cannot see an object cannot remove it,
            // and reported the edit applied while the control went on being drawn — deleted from
            // the tree, deleted from the markup, still on screen.
            Fail(ref failures, "the deleted control is gone from the document and still on the canvas");
        }
        else
        {
            Say("a placed control deletes from the document and the canvas together");
        }

        // The same delete on a window whose whole content is that control. Removing it leaves the
        // window with nothing at all, which is a different update from removing one child of a
        // panel — and the one the reporter's project is written as.
        if (!Open(designer, "Hosted.axaml")
            || !await Until(
                () => designer.ActiveForm is { Problem: null, Root: not null } h
                    && h.Name == "Hosted.axaml",
                300))
        {
            return Fail(ref failures, "Hosted.axaml would not open: "
                + (designer.ActiveForm?.Problem ?? "?"));
        }

        FormViewModel hosted = designer.ActiveForm!;

        if (!await Until(() => OnCanvas(hosted, "MyControl"), 60))
        {
            return Fail(ref failures, "the hosted control never appeared on the canvas");
        }

        designer.SelectFromCanvas(hosted, Drawn(hosted, "MyControl"));

        if (designer.Selected?.Name.LocalName != "MyControl")
        {
            Fail(ref failures, "the window's only content selected as "
                + (designer.Selected?.Name.ToString() ?? "nothing"));
        }

        designer.DeleteSelectedCommand.Execute(null);

        if (!await Until(
            () => !Text(hosted).Contains("<v:MyControl", StringComparison.Ordinal), 30))
        {
            Fail(ref failures, "the window's only content would not delete from the document");
        }
        else if (!await Until(() => !OnCanvas(hosted, "MyControl"), 30, settle: true))
        {
            Fail(ref failures, "a window emptied of its only content still draws it");
        }
        else
        {
            Say("deleting a window's only content clears the canvas with the document");
        }

        return failures;
    }

    /// <summary>
    /// Groups two controls, writes the mark, and proves it comes back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole round trip, because every leg of it is somewhere the mark could be lost. The
    /// editor forms the group and reports it; the designer writes it into the design-time namespace,
    /// which is the one place a project that has never heard of the editor can carry it without its
    /// own build failing; the loader then skips that attribute for the same reason the compiler
    /// does, so the designer has to put it back on the live control after the reload. A test of any
    /// one leg would pass while the mark went missing on another.
    /// </para>
    /// <para>
    /// Grouping is asked of the surface rather than of the view model: the selection lives there,
    /// and additive selection has no other public door.
    /// </para>
    /// </remarks>
    private static async Task<int> GroupingAsync(Window window, DesignerViewModel designer)
    {
        var failures = 0;

        if (!Open(designer, "MainWindow.axaml")
            || !await Until(
                () => designer.ActiveForm is { Problem: null, Root: not null } opened
                    && opened.Name == "MainWindow.axaml",
                300))
        {
            return Fail(ref failures, "the window would not open for the grouping check");
        }

        FormViewModel form = designer.ActiveForm!;

        if (window.FindControl<UiDesignerView>("Surface") is not { } surface)
        {
            return Fail(ref failures, "there is no surface to group on");
        }

        Control? first = Drawn(form, "TextBlock");
        Control? second = Drawn(form, "Button");

        if (first is null || second is null)
        {
            return Fail(ref failures, "the window has nothing to group");
        }

        if (!surface.SelectTarget(first) || !surface.SelectTarget(second, additive: true))
        {
            return Fail(ref failures, "the two controls would not select together");
        }

        if (!surface.CanGroupSelection())
        {
            return Fail(ref failures, "the editor refuses to group two controls of one form");
        }

        surface.GroupSelection();

        if (!await Until(() => Text(form).Contains("DesignGroup=", StringComparison.Ordinal), 60))
        {
            return Fail(ref failures, "grouping did not reach the document");
        }

        designer.SaveCommand.Execute(null);

        if (!await Until(() => !form.IsDirty, 120))
        {
            return Fail(ref failures, "the grouped form would not save");
        }

        string onDisk = await System.IO.File.ReadAllTextAsync(form.File.Value);

        if (!onDisk.Contains("DesignGroup=", StringComparison.Ordinal))
        {
            Fail(ref failures, "the group was not saved");
        }

        // The mark has to be ignorable, or the project it is written into stops building for its
        // own author — which is the entire reason it goes in this namespace rather than the
        // editor's own.
        if (!onDisk.Contains("mc:Ignorable=\"d\"", StringComparison.Ordinal))
        {
            Fail(ref failures, "the group was written without the ignorable declaration that makes it safe");
        }

        designer.CloseForm(form);

        if (!Open(designer, "MainWindow.axaml")
            || !await Until(
                () => designer.ActiveForm is { Problem: null, Root: not null } again
                    && again.Name == "MainWindow.axaml"
                    && Drawn(again, "Button") is not null,
                300))
        {
            return Fail(ref failures, "the window would not reopen after the group was saved");
        }

        FormViewModel reopened = designer.ActiveForm!;

        if (Drawn(reopened, "Button") is not { } restored
            || string.IsNullOrEmpty(ArxisStudio.Surface.UiDesigner.SurfaceGroup.GetId(restored)))
        {
            Fail(ref failures, "the saved group did not come back onto the live control");
        }
        else
        {
            Say($"a group survives a save and a reload as {ArxisStudio.Surface.UiDesigner.SurfaceGroup.GetId(restored)}");
        }

        // And the half a save-and-reopen makes reachable: ungrouping a group that came out of the
        // file rather than out of this session. Reported from the studio — after it, the controls
        // inside the panel could not be clicked at all.
        Control? member = Drawn(reopened, "Button");
        Control? sibling = Drawn(reopened, "TextBox");

        if (member is null || sibling is null)
        {
            return Fail(ref failures, "the reopened window lost the controls the ungroup needs");
        }

        // Both members, because a group is a cluster only when all of it is selected — a click
        // expands to that by itself, and the public API does not.
        surface.SelectTarget(Drawn(reopened, "TextBlock")!);
        surface.SelectTarget(member, additive: true);

        Say("  after reload: "
            + string.Join(
                ", ",
                GroupProbeNames
                    .Select(name => name + "=" + (Drawn(reopened, name) is { } drawn
                        ? "\"" + (ArxisStudio.Surface.UiDesigner.SurfaceGroup.GetId(drawn) ?? "-") + "\""
                        : "?"))));

        if (!surface.CanUngroupSelection())
        {
            Fail(ref failures, "a group that came from the file cannot be ungrouped");
        }
        else
        {
            surface.UngroupSelection();

            if (!await Until(
                () => !Text(reopened).Contains("DesignGroup=", StringComparison.Ordinal), 60))
            {
                Fail(ref failures, "ungrouping did not reach the document");
            }
        }

        FormViewModel after = designer.ActiveForm!;

        Say("  after ungroup: "
            + string.Join(
                ", ",
                GroupProbeNames
                    .Select(name => name + "=" + (Drawn(after, name) is { } drawn
                        ? "\"" + (ArxisStudio.Surface.UiDesigner.SurfaceGroup.GetId(drawn) ?? "-") + "\""
                        : "?"))));

        // The report, stated as an assertion, and asked the way a click asks: through the canvas,
        // which is the path that expands a member to its whole group. Selecting a control and
        // landing on the panel around it is exactly what was reported.
        if (Drawn(after, "TextBox") is not { } clickable)
        {
            Fail(ref failures, "the panel's controls are gone after the ungroup");
        }
        else
        {
            designer.SelectFromCanvas(after, clickable);

            if (designer.Selected?.Name.LocalName != "TextBox")
            {
                Fail(ref failures, "after ungrouping a reloaded group, clicking a control inside the "
                    + "panel selects " + (designer.Selected?.Name.LocalName ?? "nothing"));
            }
            else
            {
                Say("a reloaded group ungroups, and the panel's controls stay clickable");
            }
        }

        failures += await StaleGroupAsync(designer, surface, after);

        return failures;
    }

    /// <summary>
    /// A file whose group holds a panel and what is inside it opens usable, and ungroups clean.
    /// </summary>
    /// <remarks>
    /// Reported from the studio, and easy to arrive at: a marquee over a form takes the panel along
    /// with its contents, every control here is selectable in its own right, and grouping the lot
    /// marks all of them. From then on nothing inside the panel can be clicked — the click expands
    /// to the cluster, the cluster holds the panel, and the panel is what gets selected. The
    /// designer now refuses to make such a group; this is the other half, for the files that
    /// already have one.
    /// </remarks>
    private static async Task<int> StaleGroupAsync(
        DesignerViewModel designer, UiDesignerView surface, FormViewModel form)
    {
        var failures = 0;

        string text = await System.IO.File.ReadAllTextAsync(form.File.Value);

        // From a clean document, for the same reason the embedded-control step starts from one: the
        // ungroup above happened in the editor and was never saved, so the file still carries the
        // group before it. Marking the first Button of that file puts a second DesignGroup on an
        // element that already has one, and a document with two of the same attribute does not
        // compile at all — the build failed, the form opened degraded, and the step reported stale
        // marks that were really its own fixture.
        text = System.Text.RegularExpressions.Regex.Replace(text, " ?d:DesignGroup=\"[^\"]*\"", "");

        // The panel and two of the controls inside it, which is the shape that was reported. One
        // element each, by construction: this form has collected controls from earlier cycles, and
        // marking every `<Button` would put a member in the group that nothing then selects — two
        // chosen out of three is not a group, and the step would fail for a reason of its own
        // making. (It did.)
        text = MarkOnce(text, "StackPanel");
        text = MarkOnce(text, "Button");
        text = MarkOnce(text, "TextBox");

        designer.CloseForm(form);

        await System.IO.File.WriteAllTextAsync(form.File.Value, text);

        if (!Open(designer, "MainWindow.axaml")
            || !await Until(
                () => designer.ActiveForm is { Problem: null, Root: not null } o
                    && o.Name == "MainWindow.axaml"
                    && Drawn(o, "Button") is not null,
                300))
        {
            return Fail(ref failures, "the window with the stale group would not open");
        }

        FormViewModel stale = designer.ActiveForm!;

        // The panel's mark is not restored, which is what gives the file its contents back.
        if (Drawn(stale, "StackPanel") is { } panel
            && !string.IsNullOrEmpty(ArxisStudio.Surface.UiDesigner.SurfaceGroup.GetId(panel)))
        {
            Fail(ref failures, "a panel grouped with its own contents was restored as a member, "
                + "which is what makes everything inside it unclickable");
        }

        Say("  stale marks: "
            + string.Join(
                ", ",
                StaleProbeNames
                    .Select(name => name + "=" + (Drawn(stale, name) is { } drawn
                        ? "\"" + (ArxisStudio.Surface.UiDesigner.SurfaceGroup.GetId(drawn) ?? "-") + "\""
                        : "?"))));

        // Ungrouping takes the whole group out of the document, including the mark on the panel that
        // the editor never knew about. The members are named one by one, because the public
        // selection API deliberately does not expand a member to its cluster — that is what a
        // pointer press does, and there is no pointer here.
        //
        // Named by their mark rather than by their type: this form has collected controls across
        // cycles, and the first Button in the tree is not the Button the mark went on. Asked by
        // type, the step selected an unmarked control, the canvas said so, and the ungroup then
        // passed on the document sweep alone — a step proving something other than what it reads.
        Control[] members = [.. Live(stale)
            .Where(control => ArxisStudio.Surface.UiDesigner.SurfaceGroup.GetId(control) == "group-9")];

        if (members.Length < 2)
        {
            return Fail(ref failures, $"the stale group came back with {members.Length} member(s) "
                + "in the tree, and a group needs two");
        }

        for (var i = 0; i < members.Length; i++)
        {
            surface.SelectTarget(members[i], additive: i > 0);
        }

        // The selection is the premise of everything below: if the canvas refused a member, the
        // ungroup would still clear the document by the sweep, and the step would pass without
        // having asked the question.
        if (surface.SelectedTargets.Count != members.Length)
        {
            return Fail(ref failures, $"the canvas took {surface.SelectedTargets.Count} of "
                + $"{members.Length} member(s) into the selection");
        }

        if (!surface.CanUngroupSelection())
        {
            return Fail(
                ref failures,
                "the stale group cannot be ungrouped; selected "
                    + surface.SelectedTargets.Count + " target(s): "
                    + string.Join(
                        " + ",
                        surface.SelectedTargets.Select(target =>
                            target.Target.GetType().Name + "/"
                            + (ArxisStudio.Surface.UiDesigner.SurfaceGroup.GetId(target.Target) ?? "-"))));
        }

        surface.UngroupSelection();

        if (!await Until(
            () => !Text(designer.ActiveForm!).Contains("DesignGroup=", StringComparison.Ordinal), 60))
        {
            Fail(ref failures, "ungrouping left marks in the document: "
                + Text(designer.ActiveForm!).Split("DesignGroup=").Length + " occurrence(s)");
        }
        else
        {
            Say("a stale group opens usable and ungroups out of the document completely");
        }

        // And with the group gone, a control inside the panel is what a click lands on.
        if (Drawn(designer.ActiveForm!, "CheckBox") is { } inside)
        {
            designer.SelectFromCanvas(designer.ActiveForm!, inside);

            if (designer.Selected?.Name.LocalName != "CheckBox")
            {
                Fail(ref failures, "after the stale group went, clicking a control inside the panel "
                    + "selects " + (designer.Selected?.Name.LocalName ?? "nothing"));
            }
        }

        return failures;
    }

    /// <summary>The controls the grouping step reports the marks of, named once.</summary>
    private static readonly string[] GroupProbeNames = ["TextBlock", "Button", "TextBox"];

    /// <summary>Puts the stale group's mark on the first element of a name, and no other.</summary>
    private static string MarkOnce(string text, string tag)
    {
        int at = text.IndexOf("<" + tag + " ", StringComparison.Ordinal);

        return at < 0
            ? text
            : text.Insert(at + tag.Length + 2, "d:DesignGroup=\"group-9\" ");
    }

    /// <summary>The controls the stale-group step reports the marks of.</summary>
    private static readonly string[] StaleProbeNames = ["StackPanel", "Button", "TextBox", "CheckBox"];

    /// <summary>Writes a control of the project's own beside its window, before anything opens it.</summary>
    private static async Task<string> WriteControlAsync(string project)
    {
        string views = System.IO.Path.GetDirectoryName(
            System.IO.Directory.GetFiles(
                System.IO.Path.GetDirectoryName(project)!, "MainWindow.axaml", System.IO.SearchOption.AllDirectories)
                .First())!;

        string file = System.IO.Path.Combine(views, "MyControl.axaml");

        await System.IO.File.WriteAllTextAsync(
            file,
            """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="StressApp.Views.MyControl">
              <Border Background="#333B4A" Padding="16">
                <TextBlock Text="First" />
              </Border>
            </UserControl>
            """);

        await System.IO.File.WriteAllTextAsync(
            file + ".cs",
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml;

            namespace StressApp.Views;

            public partial class MyControl : UserControl
            {
                public MyControl() => AvaloniaXamlLoader.Load(this);
            }
            """);

        // And a window whose entire content is that control, which is how the reporter's project
        // is written and a shape the panel case does not cover: removing it leaves the window with
        // no content at all.
        await System.IO.File.WriteAllTextAsync(
            System.IO.Path.Combine(views, "Hosted.axaml"),
            """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:v="using:StressApp.Views"
                    x:Class="StressApp.Views.Hosted"
                    Width="400" Height="300">
              <v:MyControl />
            </Window>
            """);

        await System.IO.File.WriteAllTextAsync(
            System.IO.Path.Combine(views, "Hosted.axaml.cs"),
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml;

            namespace StressApp.Views;

            public partial class Hosted : Window
            {
                public Hosted() => AvaloniaXamlLoader.Load(this);
            }
            """);

        return file;
    }

    /// <summary>
    /// A control the canvas is actually drawing for this form, by type name.
    /// </summary>
    /// <remarks>
    /// Asked of the surface rather than of the root, because a window-rooted form is shown by
    /// taking the window's content out of it: the window has nothing under it, and a check that
    /// walked the root would call every control absent — including one still plainly on screen.
    /// </remarks>
    private static Control? Drawn(FormViewModel form, string typeName) =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(form.Card)
            .OfType<Control>()
            .FirstOrDefault(control => control.GetType().Name == typeName);

    private static bool OnCanvas(FormViewModel form, string typeName) => Drawn(form, typeName) is not null;

    /// <summary>Every control the canvas has drawn for this form.</summary>
    private static IEnumerable<Control> Live(FormViewModel form) =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(form.Card).OfType<Control>();

    /// <summary>Opens a project form by file name, the way a double-click in the pane would.</summary>
    private static bool Open(DesignerViewModel designer, string name)
    {
        if (designer.ProjectForms.FirstOrDefault(form => form.Name == name) is not { } form)
        {
            return false;
        }

        designer.OpenFile(new FileTile(
            form.Name, form.Path, Glyphs.ForFile(form.Path.Extension), Glyphs.HueOfFile(form.Path.Extension)));

        return true;
    }

    /// <summary>The live control behind the element carrying this <c>x:Name</c>.</summary>
    private static Control? LiveByName(DesignerViewModel designer, string name)
    {
        if (designer.ActiveForm is not { Objects: not null, Document.Root: { } root } form)
        {
            return null;
        }

        foreach (ArxisStudio.Markup.Xaml.XamlElement element in Elements(root))
        {
            if (element.GetDirective("Name") == name)
            {
                return DesignerViewModel.ControlFor(form, element);
            }
        }

        return null;

        static System.Collections.Generic.IEnumerable<ArxisStudio.Markup.Xaml.XamlElement> Elements(
            ArxisStudio.Markup.Xaml.XamlElement element)
        {
            yield return element;

            foreach (ArxisStudio.Markup.Xaml.XamlElement child in element.ContentElements)
            {
                foreach (ArxisStudio.Markup.Xaml.XamlElement grand in Elements(child))
                {
                    yield return grand;
                }
            }
        }
    }

    /// <summary>How many lines of the console are complaints.</summary>
    private static int CountErrors(DesignerViewModel designer) =>
        designer.Output.Count(line => line.TrimStart().StartsWith('!'));

    /// <summary>The form's root window, held weakly so that asking does not keep it.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Window>? RootWindowOf(FormViewModel form) =>
        form.Root is Window window ? new WeakReference<Window>(window) : null;

    /// <summary>Whether the window is still the form's root.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsRootOf(FormViewModel form, WeakReference<Window> window) =>
        window.TryGetTarget(out Window? target) && ReferenceEquals(form.Root, target);

    /// <summary>Whether the window still has the platform window that keeps it alive.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsOpen(WeakReference<Window> window) =>
        window.TryGetTarget(out Window? target) && target.PlatformImpl is not null;

    /// <summary>What the last restore or build reported as errors.</summary>
    /// <remarks>
    /// One predicate for every step that asks, typed: the two steps that each spelled the severity
    /// out disagreed on its case, and the one that got it wrong passed on a project that does not
    /// compile.
    /// </remarks>
    private static IEnumerable<DiagnosticRow> Errors(DesignerViewModel designer) =>
        designer.Diagnostics.Where(static row => row.Severity == ProjectDiagnosticSeverity.Error);

    private static async Task<int> CheckAsync(DesignerViewModel designer, string folder)
    {
        var failures = 0;

        // Same reason as the stress run: the story needs the process it started in.
        designer.AutoReloadForCode = false;

        string name = "StudioCheckApp";

        // 1. A project to work on, and the way in to it. The demo opens projects and does not make
        //    them, so the check writes its own — and then goes in the way a person does: through
        //    the welcome screen's Open, which remembers the project and names it to whoever opens
        //    it. Only the file picker is stood in for.
        string made = await ProjectScaffold.CreateAsync(folder, name, CancellationToken.None);

        var welcome = new WelcomeViewModel
        {
            PickProjectFile = () => Task.FromResult<string?>(made),
        };

        string? project = null;

        welcome.ProjectChosen += (_, chosen) => project = chosen;

        welcome.OpenCommand.Execute(null);

        if (!await Until(() => project is not null, 120))
        {
            return Fail(ref failures, "the welcome screen never opened the project: " + welcome.Problem);
        }

        Say($"created {project}");

        if (!welcome.Recent.Any(entry => entry.Path == project))
        {
            Fail(ref failures, "the new project is not in the recent list");
        }

        // 2. Opened, which is an MSBuild evaluation of something that has never been restored.
        designer.OpenAtStartup(project!, "MainWindow.axaml");

        if (!await Until(() => designer.IsLoaded && !designer.IsBusy, 240))
        {
            return Fail(ref failures, "the project never finished loading");
        }

        if (!await Until(() => designer.ActiveForm is { Session: not null }, 240))
        {
            return Fail(ref failures, "MainWindow.axaml never opened on the canvas");
        }

        FormViewModel form = designer.ActiveForm!;

        Say($"opened {form.Name}, root {form.Root?.GetType().Name ?? "none"}");

        // 2a. The canvas is the design's, in both variants. The grid belongs to the editor's library
        //     and reads the library's keys; the design answers those keys with its own colours, and
        //     which of the two answers is found is decided by the order the dictionaries are merged
        //     in. Nothing reports the wrong one: the library's dark is three shades from the design's.
        int unmatched = failures;

        foreach (ThemeVariant variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            Color? canvas = BrushColor("Canvas", variant);
            Color? grid = BrushColor("Surface.Grid.BackgroundBrush", variant);

            if (canvas is null || grid != canvas)
            {
                Fail(ref failures, $"the {variant} canvas grid is {grid}, and the design's canvas is {canvas}");
            }
        }

        if (failures == unmatched)
        {
            Say("the canvas grid is the design's colour in both variants");
        }

        // 2b. And the palette holds the thresholds it was copied under. The numbers in Theme.axaml
        //     are the studio theme's, written out a second time, and a second copy has nothing else
        //     to keep it honest: a colour nudged there would go on compiling and go on being drawn.
        int lowContrast = failures;

        foreach (ThemeVariant variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            foreach ((string ink, string surface, double least) in PalettePairs)
            {
                double? ratio = Contrast(ink, surface, variant);

                if (ratio is null || ratio < least)
                {
                    Fail(ref failures, string.Create(
                        CultureInfo.InvariantCulture,
                        $"{variant}: {ink} on {surface} is {ratio:0.00}:1, and {least}:1 is asked"));
                }
            }
        }

        if (failures == lowContrast)
        {
            Say($"the palette holds its contrast thresholds: {PalettePairs.Length} pairs in each variant");
        }

        // 3. Laid out: three controls from the toolbox, dropped into the form's panel.
        //
        // Into the panel rather than onto the root: a Window holds one child, which is what the
        // pointer path works out from what is under the cursor. The live control is found again
        // after every drop, because an edit rebuilds the objects the document produced.
        foreach ((string control, double y) in new[] { ("TextBlock", 40d), ("TextBox", 90d), ("Button", 140d) })
        {
            if (designer.Toolbox.FirstOrDefault(entry => entry.Name == control) is not { } tool)
            {
                Fail(ref failures, $"the toolbox has no {control}");

                continue;
            }

            int before = Count(designer, control);

            // The container is waited for rather than assumed. An edit republishes the live tree,
            // and for a moment between the document changing and the objects catching up there is
            // no panel to point at — a drop aimed at that moment lands on the root, which holds one
            // child and rejects it. A pointer never moves that fast; this does.
            if (!await Until(() => Find(designer, "StackPanel") is not null, 30))
            {
                Fail(ref failures, "the form's panel never came back after the last edit");

                continue;
            }

            designer.Drop(form, tool, over: Find(designer, "StackPanel"), at: new Point(60, y));

            if (!await Until(() => Count(designer, control) > before, 30))
            {
                Fail(ref failures, $"{control} never reached the document");
            }
        }

        Say($"document now: {Summary(designer)}");

        // 4. Edited: the button's own text, written through the inspector's row rather than around it.
        if (Find(designer, "Button") is { } button)
        {
            designer.SelectFromCanvas(form, button);

            if (designer.Properties.FirstOrDefault(row => row.Name == "Content") is { } content)
            {
                content.Value = "Поехали";

                if (!await Until(() => Text(form).Contains("Поехали", StringComparison.Ordinal), 30))
                {
                    Say("  the button's row now reads: " + content.Value);

                    Fail(ref failures, "the inspector's edit never reached the document");
                }
            }
            else
            {
                Fail(ref failures, "the inspector offered no Content row for a Button");
            }
        }
        else
        {
            Fail(ref failures, "no Button to edit");
        }

        // 4a. Duplicated and pasted, which is how a form with one of something gets three.
        if (Find(designer, "Button") is { } original)
        {
            designer.SelectFromCanvas(form, original);

            int buttons = Count(designer, "Button");

            designer.DuplicateCommand.Execute(null);

            if (!await Until(() => Count(designer, "Button") == buttons + 1, 30))
            {
                Fail(ref failures, "duplicate did not add a second Button");
            }

            Say($"  after duplicate the selection is {designer.Selected?.Name.LocalName ?? "none"}");

            designer.CopyCommand.Execute(null);

            await Task.Delay(300);

            designer.PasteCommand.Execute(null);

            if (!await Until(() => Count(designer, "Button") == buttons + 2, 30))
            {
                Fail(ref failures, "paste did not add a third Button");
            }

            // And back, so that what is built is the form that was laid out.
            designer.UndoCommand.Execute(null);
            await Until(() => Count(designer, "Button") == buttons + 1, 30);

            designer.UndoCommand.Execute(null);

            if (!await Until(() => Count(designer, "Button") == buttons, 30))
            {
                Fail(ref failures, "undo did not take the copies back");
            }

            Say($"duplicated and pasted, then undone: {Summary(designer)}");
        }

        // 4b. Undone and redone, which is the same path an edit takes and had better be.
        designer.UndoCommand.Execute(null);

        if (!await Until(() => !Text(form).Contains("Поехали", StringComparison.Ordinal), 30))
        {
            Fail(ref failures, "undo did not take the edit back");
        }

        designer.RedoCommand.Execute(null);

        if (!await Until(() => Text(form).Contains("Поехали", StringComparison.Ordinal), 30))
        {
            Fail(ref failures, "redo did not put the edit back");
        }

        // And far enough back that the form is the file again, which is what a clean form means.
        int steps = 0;

        while (designer.UndoCommand.CanExecute(null) && steps++ < 20)
        {
            designer.UndoCommand.Execute(null);

            await Until(() => !designer.IsBusy, 10);
            await Task.Delay(150);
        }

        if (form.IsDirty)
        {
            Fail(ref failures, "a form undone to where it started still says it has unsaved edits");
        }

        Say($"undone {steps} step(s) back to the file; document: {Summary(designer)}");

        for (int forward = 0; forward < steps && designer.RedoCommand.CanExecute(null); forward++)
        {
            designer.RedoCommand.Execute(null);

            await Until(() => !designer.IsBusy, 10);
            await Task.Delay(150);
        }

        if (!Text(form).Contains("Поехали", StringComparison.Ordinal))
        {
            Fail(ref failures, "redoing every step did not get back to the laid-out form");
        }

        // 4b2. Moved among its siblings, wrapped, and unwrapped — the structure edits, each of
        //      which must be exactly one step of the history.
        if (Find(designer, "Button") is { } ordered)
        {
            designer.SelectFromCanvas(form, ordered);

            int buttonAt = Text(form).IndexOf("<Button", StringComparison.Ordinal);
            int boxAt = Text(form).IndexOf("<TextBox", StringComparison.Ordinal);

            if (buttonAt < boxAt)
            {
                Fail(ref failures, "the drops did not land in the expected order");
            }

            designer.MoveUpCommand.Execute(null);

            if (!await Until(
                () => Text(form).IndexOf("<Button", StringComparison.Ordinal)
                    < Text(form).IndexOf("<TextBox", StringComparison.Ordinal),
                30))
            {
                Fail(ref failures, "move up did not reorder the siblings");
            }

            designer.MoveDownCommand.Execute(null);

            if (!await Until(
                () => Text(form).IndexOf("<Button", StringComparison.Ordinal)
                    > Text(form).IndexOf("<TextBox", StringComparison.Ordinal),
                30))
            {
                Fail(ref failures, "move down did not put the sibling back");
            }

            designer.Wrap("Border");

            // Asked of the document's structure, not its spelling: the wrap lays the Button out a
            // step deeper on a line of its own, the way the file is written.
            if (!await Until(() => ButtonInBorder(form), 30))
            {
                Fail(ref failures, "wrap did not put a Border around the Button");
            }
            else if (designer.Selected?.Name.LocalName != "Border")
            {
                Fail(ref failures, "the selection did not follow the wrap to the Border");
            }
            else
            {
                designer.Unwrap();

                if (!await Until(() => !ButtonInBorder(form), 30))
                {
                    Fail(ref failures, "unwrap did not lift the Button back out");
                }
                else
                {
                    designer.UndoCommand.Execute(null);

                    if (!await Until(() => ButtonInBorder(form), 30))
                    {
                        Fail(ref failures, "undoing the unwrap is not one step");
                    }

                    designer.RedoCommand.Execute(null);
                    await Until(() => !ButtonInBorder(form), 30);
                }
            }

            Say("moved, wrapped, unwrapped — one history step each");
        }

        // 4b3. The inspector's rows filter down and come back.
        if (Find(designer, "Button") is { } filtered)
        {
            designer.SelectFromCanvas(form, filtered);

            int fullRows = designer.PropertyGroups.Sum(group => group.Rows.Count);

            designer.InspectorFilter = "Width";

            int narrowed = designer.PropertyGroups.Sum(group => group.Rows.Count);

            designer.InspectorFilter = string.Empty;

            if (narrowed == 0 || narrowed >= fullRows)
            {
                Fail(ref failures, $"filtering the inspector for Width left {narrowed} of {fullRows} rows");
            }
            else if (designer.PropertyGroups.Sum(group => group.Rows.Count) != fullRows)
            {
                Fail(ref failures, "clearing the inspector's filter did not bring the rows back");
            }
            else
            {
                Say($"the inspector filters to {narrowed} row(s) out of {fullRows} and back");
            }
        }

        // 4b4. A size, and the three ways back from one. A control with a width of its own cannot
        //      stretch, so the inspector has to be able to take the width out: by emptying the
        //      field, by the reset beside it, and by choosing Stretch — which is one decision and
        //      so one step of the history, not two.
        if (Find(designer, "Button") is not null)
        {
            failures += await SizesAsync(designer, form);
        }

        // 4c. Named, because a control nothing can find by name is a control code-behind cannot use.
        if (Find(designer, "Button") is { } toName)
        {
            designer.SelectFromCanvas(form, toName);

            if (designer.Properties.FirstOrDefault(row => row.Name == "x:Name") is not { } named)
            {
                Fail(ref failures, "the inspector offers no name for a Button");
            }
            else
            {
                named.Value = "GoButton";

                if (!await Until(() => Text(form).Contains("x:Name=\"GoButton\"", StringComparison.Ordinal), 30))
                {
                    Fail(ref failures, "naming the button never reached the document");
                }
                else
                {
                    Say($"named the button, and the inspector calls the row \"{named.Heading}\"");
                }
            }
        }

        // 5. Saved, so that the build has something to compile.
        designer.SaveCommand.Execute(null);

        if (!await Until(() => form.IsDirty == false, 60))
        {
            Fail(ref failures, "the form never saved");
        }

        // 5a. Changed from outside, the way the IDE beside this one changes it.
        if (form.File.Value is { Length: > 0 } onDisk)
        {
            string outside = (await System.IO.File.ReadAllTextAsync(onDisk))
                .Replace("Поехали", "Извне", StringComparison.Ordinal);

            await System.IO.File.WriteAllTextAsync(onDisk, outside);

            if (!await Until(() => Text(form).Contains("Извне", StringComparison.Ordinal), 30))
            {
                Fail(ref failures, "an edit made outside never reached the open form");
            }
            else if (form.IsDirty)
            {
                Fail(ref failures, "a form reloaded from disk still calls itself edited");
            }
            else
            {
                Say("an edit made outside the designer arrived in the open form");
            }
        }

        // 5b. Changed from outside where a session cannot follow in place — a directive on the root
        //     is a new session — so the form is rebuilt from the document. A window-rooted form's root
        //     is a real Window, which the windowing platform keeps until it is closed, and with it
        //     every type of the generation: on Win32 a window that is never shown and never closed
        //     survives every collection, and a closed one goes.
        if (form.File.Value is { Length: > 0 } rooted)
        {
            WeakReference<Window>? replaced = RootWindowOf(form);
            string asWritten = await System.IO.File.ReadAllTextAsync(rooted);

            await System.IO.File.WriteAllTextAsync(
                rooted, asWritten.Replace("<Window ", "<Window x:Name=\"Shell\" ", StringComparison.Ordinal));

            if (replaced is null)
            {
                Fail(ref failures, "the form's root is not a window, so a rebuild had nothing to close");
            }
            else if (!await Until(() => !IsRootOf(form, replaced), 30))
            {
                Fail(ref failures, "a form whose root changed outside was never rebuilt");
            }
            else if (IsOpen(replaced))
            {
                Fail(ref failures, "a form rebuilt for a changed root left the window it replaced open");
            }
            else
            {
                Say("a form rebuilt for a changed root closed the window it replaced");
            }

            await System.IO.File.WriteAllTextAsync(rooted, asWritten);
            await Until(() => !Text(form).Contains("x:Name=\"Shell\"", StringComparison.Ordinal), 30);
        }

        // 6. Restored and built, which is the claim that what was laid out is a real application.
        designer.RestoreCommand.Execute(null);

        if (!await Until(() => !designer.IsBusy, 300, settle: true))
        {
            Fail(ref failures, "restore never finished");
        }

        designer.BuildCommand.Execute(null);

        if (!await Until(() => !designer.IsBusy, 300, settle: true))
        {
            Fail(ref failures, "build never finished");
        }

        if (Errors(designer).Any())
        {
            foreach (DiagnosticRow row in Errors(designer).Take(5))
            {
                Say($"  build error: {row.Message}");
            }

            Fail(ref failures, "the project did not build");
        }

        // And a build that cannot succeed is reported as one. The check above passes on every project
        // whose errors it cannot see, so it is asked about a project that certainly has one: a source
        // file that does not compile, written beside the window and taken away again.
        string broken = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(made)!, "Broken.cs");

        await System.IO.File.WriteAllTextAsync(
            broken, "namespace StudioCheckApp;\n\ninternal static class Broken\n{\n    internal static int Value => ;\n}\n");

        await Until(() => !designer.IsBusy, 60, settle: true);
        designer.BuildCommand.Execute(null);

        if (!await Until(() => !designer.IsBusy, 300, settle: true))
        {
            Fail(ref failures, "the broken build never finished");
        }
        else if (!Errors(designer).Any())
        {
            Fail(ref failures, "a build that cannot succeed was not reported as failed");
        }
        else
        {
            Say("a build that cannot succeed is reported as failed");
        }

        System.IO.File.Delete(broken);

        await Until(() => !designer.IsBusy, 60, settle: true);
        designer.BuildCommand.Execute(null);

        if (!await Until(() => !designer.IsBusy, 300, settle: true) || Errors(designer).Any())
        {
            Fail(ref failures, "the project did not build again once the broken file was gone");
        }

        // 7. Run: the application it just laid out, started for real and then stopped.
        designer.RunCommand.Execute(null);

        if (!await Until(() => designer.IsRunning, 300))
        {
            Fail(ref failures, "the application never started");
        }
        else
        {
            await Task.Delay(2500);

            if (!designer.IsRunning)
            {
                Fail(ref failures, "the application exited on its own");
            }
            else
            {
                Say("the application is running");
            }

            designer.StopCommand.Execute(null);

            await Until(() => !designer.IsRunning, 30);
        }

        // 8. A form added and taken away again, which is the other half of a project panel.
        designer.AskForNewForm = _ => Task.FromResult<NewFormRequest?>(
            new NewFormRequest("SecondForm", NewFormKind.Window));

        designer.NewFormCommand.Execute(null);

        if (!await Until(() => designer.ProjectForms.Any(entry => entry.Name == "SecondForm.axaml"), 180))
        {
            Fail(ref failures, "the new form never appeared in the project");
        }
        else
        {
            string added = designer.ProjectForms.First(entry => entry.Name == "SecondForm.axaml").Path.Value;

            // What was made: a window, with a class the other editor can write code in.
            string madeMarkup = await System.IO.File.ReadAllTextAsync(added);

            if (!madeMarkup.Contains("<Window", StringComparison.Ordinal)
                || !madeMarkup.Contains("x:Class=", StringComparison.Ordinal))
            {
                Fail(ref failures, "the new window is not a window with a class");
            }
            else if (!System.IO.File.Exists(added + ".cs"))
            {
                Fail(ref failures, "the new window has no code-behind");
            }
            else
            {
                Say("the new form is a window with a class and a code-behind");
            }

            // And what happens when it is opened, which is the honest half of one generation per
            // run: the class was compiled after this run loaded its types, so the studio cannot
            // show it — and must say so and offer the reload rather than fail quietly.
            if (!await Until(
                () => designer.Forms.Any(open => open.Name == "SecondForm.axaml"), 240))
            {
                Fail(ref failures, "the new window never opened at all");
            }
            else if (!await Until(() => designer.NeedsRestart, 240))
            {
                Fail(ref failures, "a form whose class is newer than this run said nothing about it");
            }
            else if (!designer.RestartCommand.CanExecute(null))
            {
                Fail(ref failures, "the studio said a reload was needed and would not perform one");
            }
            else
            {
                Say("a form newer than this run's types offers the reload that would show it");
            }

            designer.AskToConfirm = (_, _) => Task.FromResult(true);

            if (System.Linq.Enumerable.FirstOrDefault(
                designer.ProjectFiles, tile => tile.Name == "SecondForm.axaml") is not { } offered)
            {
                Fail(ref failures, "the new form is not a tile in the project pane");
            }
            else
            {
                designer.DeleteFile(offered);

                if (!await Until(() => !System.IO.File.Exists(added), 60))
                {
                    Fail(ref failures, "the form was not deleted from disk");
                }
                else if (!await Until(
                    () => designer.ProjectForms.All(entry => entry.Name != "SecondForm.axaml"), 180))
                {
                    Fail(ref failures, "the deleted form is still listed in the project");
                }
                else
                {
                    Say("a second form was created and deleted again");
                }
            }
        }

        // 8a. The tree's filter, save-all, and a close that asks about unsaved work.
        int allRows = designer.Hierarchy.Count;

        designer.IsHierarchySearchOpen = true;
        designer.HierarchyFilter = "Button";

        if (designer.Hierarchy.Count >= allRows || designer.Hierarchy.Count == 0)
        {
            Fail(ref failures, $"filtering the tree for Button left {designer.Hierarchy.Count} of {allRows} rows");
        }

        designer.IsHierarchySearchOpen = false;

        if (designer.Hierarchy.Count != allRows)
        {
            Fail(ref failures, "closing the tree's filter did not bring the rows back");
        }
        else
        {
            Say($"the tree filters to a few rows out of {allRows} and back");
        }

        // An edit, so that there is something to save and something to ask about.
        if (Find(designer, "Button") is { } dirtied)
        {
            designer.SelectFromCanvas(form, dirtied);

            if (designer.Properties.FirstOrDefault(row => row.Name == "Content") is { } content)
            {
                content.Value = "Ещё раз";

                await Until(() => form.IsDirty, 30);
            }
        }

        if (!form.IsDirty)
        {
            Fail(ref failures, "nothing to save after an edit");
        }
        else
        {
            designer.SaveAllCommand.Execute(null);

            if (!await Until(() => designer.Forms.All(open => !open.IsDirty), 60))
            {
                Fail(ref failures, "save all left a form unsaved");
            }
            else
            {
                Say("save all wrote every form that had edits");
            }
        }

        // Cancel keeps the tab; discard takes it away. Both are asked for.
        if (Find(designer, "Button") is { } again)
        {
            designer.SelectFromCanvas(form, again);

            if (designer.Properties.FirstOrDefault(row => row.Name == "Content") is { } content)
            {
                content.Value = "И ещё";

                await Until(() => form.IsDirty, 30);
            }
        }

        designer.AskToSave = _ => Task.FromResult(DesignerViewModel.SaveAnswer.Cancel);
        designer.CloseForm(form, ask: true);

        await Task.Delay(400);

        if (!designer.Forms.Contains(form))
        {
            Fail(ref failures, "cancelling the close still closed the form");
        }
        else
        {
            Say("a close that was cancelled kept the form");
        }

        designer.AskToSave = _ => Task.FromResult(DesignerViewModel.SaveAnswer.Discard);
        designer.CloseForm(form, ask: true);

        if (!await Until(() => !designer.Forms.Contains(form), 30))
        {
            Fail(ref failures, "discarding the edits did not close the form");
        }
        else
        {
            Say("a close that discarded the edits closed the form");
        }

        // 9. And the project file, whose change means the evaluation is stale.
        string wasSaying = designer.Status;

        System.IO.File.SetLastWriteTimeUtc(project!, DateTime.UtcNow);

        if (!await Until(() => designer.Status != wasSaying, 180))
        {
            Fail(ref failures, "a change to the project file did not re-read it");
        }
        else
        {
            Say("a change to the project file was picked up");
        }

        // 10. A form added by the other editor: a file appearing is a change to what the project is.
        string outsideForm = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(project!)!, "MadeOutside.axaml");

        await System.IO.File.WriteAllTextAsync(
            outsideForm,
            """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         Width="300" Height="200">
              <TextBlock Text="made outside" />
            </UserControl>
            """);

        if (!await Until(() => designer.ProjectForms.Any(entry => entry.Name == "MadeOutside.axaml"), 180))
        {
            Fail(ref failures, "a form added outside never appeared in the project");
        }
        else
        {
            Say("a form added outside appeared in the project");

            // 11. And taken away again while it is open here, which has to take the tab with it.
            if (System.Linq.Enumerable.FirstOrDefault(
                designer.ProjectFiles, tile => tile.Name == "MadeOutside.axaml") is { } tile)
            {
                designer.OpenFile(tile);

                if (!await Until(() => designer.Forms.Any(open => open.Name == "MadeOutside.axaml"), 120))
                {
                    Fail(ref failures, "the form added outside would not open");
                }
                else
                {
                    System.IO.File.Delete(outsideForm);

                    if (!await Until(() => designer.Forms.All(open => open.Name != "MadeOutside.axaml"), 120))
                    {
                        Fail(ref failures, "a form deleted outside is still open in the designer");
                    }
                    else
                    {
                        Say("a form deleted outside closed its tab");
                    }
                }
            }
            else
            {
                Fail(ref failures, "the form added outside is not a tile in the project pane");
            }
        }

        // 12. Resources, the way the other editor writes them: onto a control, onto the form's
        //     root, and then the form opened afresh with them still at its root.
        failures += await ResourcesAsync(designer);

        return failures;
    }

    /// <summary>
    /// Resources written into an open form from outside, and a form opened with them at its root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both failures were seen on the live demo while its README pictures were taken, and both
    /// were the loader's. A <c>Resources</c> block added to a control from outside was reported as
    /// reloaded and changed nothing on screen: adding a property element reads as a change to the
    /// element's content, and rebuilding the content moved the content member across and threw
    /// the rebuilt copy away with the resources in it. And a form with resources at its root would
    /// not open at all — "An item with the same key has already been added" — because its class's
    /// constructor loaded the markup once and the session loaded it again over the same object.
    /// </para>
    /// <para>
    /// Asked last, because the reopen builds the project for a form newer than its assembly, and
    /// a build that moves the types lights the reload banner the new-form step waits for.
    /// </para>
    /// </remarks>
    private static async Task<int> ResourcesAsync(DesignerViewModel designer)
    {
        const string Name = "MainWindow.axaml";
        const string ControlKey = "StudioCheckControlProbe";
        const string RootKey = "StudioCheckRootProbe";

        var failures = 0;

        if (!Open(designer, Name)
            || !await Until(
                () => designer.ActiveForm is { } shown && shown.Name == Name
                    && (shown.Session is not null || shown.Problem is not null),
                300)
            || designer.ActiveForm is not { Session: not null, Document: not null } form)
        {
            return Fail(ref failures, "MainWindow.axaml would not open for the resources: "
                + (designer.ActiveForm?.Problem ?? "no problem reported"));
        }

        string file = form.File.Value;

        // 12a. Onto a control inside the form. One change and nothing else: an attribute that
        //      reads the resource would rebuild the control by itself and prove nothing.
        if (Find(designer, "TextBox") is not { } box || form.Objects?.GetElement(box) is not { } element)
        {
            return Fail(ref failures, "the form has no TextBox to write resources onto");
        }

        await System.IO.File.WriteAllTextAsync(file, WithResources(Text(form), element, ControlKey));

        if (!await Until(
            () => Text(form).Contains(ControlKey, StringComparison.Ordinal)
                && Find(designer, "TextBox") is { } live
                && live.Resources.ContainsKey(ControlKey),
            30))
        {
            Fail(ref failures, "resources written outside onto a TextBox never reached the one on the canvas");
        }
        else if (form.IsDirty)
        {
            Fail(ref failures, "a form reloaded with resources still calls itself edited");
        }
        else
        {
            Say("resources written outside onto a control reached it on the canvas");
        }

        // 12b. Onto the root, where the card keeps them for everything inside the form.
        if (form.Document?.Root is not { } root)
        {
            return Fail(ref failures, "the form lost its document");
        }

        await System.IO.File.WriteAllTextAsync(file, WithResources(Text(form), root, RootKey));

        if (!await Until(
            () => Text(form).Contains(RootKey, StringComparison.Ordinal)
                && Find(designer, "TextBox") is { } inside
                && inside.TryFindResource(RootKey, out _),
            30))
        {
            Fail(ref failures, "resources written outside onto the form's root never reached the canvas");
        }
        else
        {
            Say("resources written outside onto the form's root reached what is inside it");
        }

        // 12c. And opened afresh with them there: a new session, whose root is built by the
        //      class's own constructor, which loads the same markup.
        designer.CloseForm(form);

        await Until(() => !designer.Forms.Contains(form), 30);

        if (!Open(designer, Name)
            || !await Until(
                () => designer.ActiveForm is { } back && back.Name == Name && !ReferenceEquals(back, form)
                    && (back.Session is not null || back.Problem is not null),
                300))
        {
            return Fail(ref failures, "MainWindow.axaml would not open again");
        }

        if (designer.ActiveForm is not { Session: not null } reopened)
        {
            Fail(ref failures, "a form with resources at its root did not open: "
                + (designer.ActiveForm?.Problem ?? "no problem reported"));
        }
        else if (!await Until(
            () => Find(designer, "TextBox") is { } again
                && again.TryFindResource(RootKey, out _)
                && again.Resources.ContainsKey(ControlKey),
            30))
        {
            Fail(ref failures, $"{reopened.Name} opened without the resources its file has");
        }
        else
        {
            Say("a form with resources at its root opens, and they are there");
        }

        return failures;
    }

    /// <summary>
    /// Writes a resources block into an element as its first child, as somebody typing would.
    /// </summary>
    private static string WithResources(string text, ArxisStudio.Markup.Xaml.XamlElement element, string key)
    {
        string owner = element.Name.ToString();
        string block = $"<{owner}.Resources><SolidColorBrush x:Key=\"{key}\" Color=\"#2F6FED\" /></{owner}.Resources>";
        ArxisStudio.Markup.TextSpan tag = element.StartTagSpan;

        if (!element.IsEmpty)
        {
            return text.Insert(tag.End, block);
        }

        // An empty element gets an end tag: <TextBox Width="160" /> becomes <TextBox Width="160">…</TextBox>.
        string written = text[tag.Start..tag.End];
        string opened = written[..written.LastIndexOf("/>", StringComparison.Ordinal)].TrimEnd() + ">";

        return text[..tag.Start] + opened + block + $"</{owner}>" + text[tag.End..];
    }

    /// <summary>
    /// Sets a width on the form's button and takes it back out every way the inspector offers.
    /// </summary>
    /// <remarks>
    /// The rows are asked for again after every write, because every write rebuilds them: the
    /// inspector shows what the document now says, and the row that was written to is not the row
    /// that is on screen afterwards. It leaves the document as it found it.
    /// </remarks>
    private static async Task<int> SizesAsync(DesignerViewModel designer, FormViewModel form)
    {
        var failures = 0;

        bool Wide() => Text(form).Contains("Width=\"120\"", StringComparison.Ordinal)
            && Text(form).Contains("<Button", StringComparison.Ordinal);

        async Task<PropertyRow?> RowAsync(string name)
        {
            await Until(() => !designer.IsBusy, 10, settle: true);

            if (Find(designer, "Button") is { } button)
            {
                designer.SelectFromCanvas(form, button);
            }

            return designer.Properties.FirstOrDefault(row => row.Name == name);
        }

        async Task<bool> WidenAsync()
        {
            if (await RowAsync("Width") is not { } width)
            {
                return false;
            }

            width.Value = "120";

            return await Until(Wide, 30);
        }

        string before = Text(form);

        // Emptied. No text is not a number, and the row used to say so instead of clearing.
        if (!await WidenAsync())
        {
            return Fail(ref failures, "the inspector did not write a Width on the Button");
        }

        if (await RowAsync("Width") is not { IsSize: true } typed)
        {
            return Fail(ref failures, "the Width row does not offer the way back to NaN");
        }

        typed.Value = string.Empty;

        if (!await Until(() => !Wide(), 30))
        {
            Fail(ref failures, "emptying the Width field did not take the width out of the document");
        }

        // Reset, by the button beside the field.
        if (!await WidenAsync() || await RowAsync("Width") is not { } reset)
        {
            return Fail(ref failures, "the Width could not be written a second time");
        }

        if (!reset.ResetCommand.CanExecute(null))
        {
            Fail(ref failures, "the reset beside a written Width is switched off");
        }

        reset.ResetCommand.Execute(null);

        if (!await Until(() => !Wide(), 30))
        {
            Fail(ref failures, "the reset did not take the width out of the document");
        }
        else if (await RowAsync("Width") is not { Unset: "NaN" } cleared || cleared.ResetCommand.CanExecute(null))
        {
            Fail(ref failures, "a Width that was reset does not read NaN with its reset switched off");
        }

        // Stretched, which lets the width go in the same step.
        if (!await WidenAsync() || await RowAsync("HorizontalAlignment") is not { } alignment)
        {
            return Fail(ref failures, "the Button offers no HorizontalAlignment to stretch");
        }

        alignment.Value = "Stretch";

        bool Stretched() => Text(form).Contains("HorizontalAlignment=\"Stretch\"", StringComparison.Ordinal);

        if (!await Until(() => Stretched() && !Wide(), 30))
        {
            Fail(ref failures, "choosing Stretch did not take the width out with it");
        }

        await Until(() => !designer.IsBusy, 10, settle: true);

        designer.UndoCommand.Execute(null);

        if (!await Until(() => !Stretched() && Wide(), 30))
        {
            Fail(ref failures, "undoing Stretch is not one step: the width did not come back with it");
        }

        await Until(() => !designer.IsBusy, 10, settle: true);

        designer.UndoCommand.Execute(null);

        if (!await Until(() => string.Equals(Text(form), before, StringComparison.Ordinal), 30))
        {
            Fail(ref failures, "the size steps did not leave the document as they found it");
        }

        if (failures == 0)
        {
            Say("a width comes out by emptying the field, by its reset and by Stretch — one step");
        }

        return failures;
    }

    /// <summary>The document as it stands, which is the text a save would write.</summary>
    private static string Text(FormViewModel form) =>
        form.Document?.SourceText.ToString() ?? string.Empty;

    /// <summary>Whether the document has a Border whose first child is a Button.</summary>
    private static bool ButtonInBorder(FormViewModel form) =>
        form.Document?.Root is { } root
        && root.DescendantElements().Any(static element =>
            element.Name.LocalName == "Border"
            && element.ContentElements.FirstOrDefault()?.Name.LocalName == "Button");

    /// <summary>How many elements of this name the document has, which is what a drop changes.</summary>
    private static int Count(DesignerViewModel designer, string element) =>
        designer.Hierarchy.Count(row => row.TypeLabel.StartsWith(element, StringComparison.Ordinal));

    /// <summary>The live control an element of this name produced, for the inspector to be pointed at.</summary>
    private static Control? Find(DesignerViewModel designer, string element)
    {
        if (designer.ActiveForm?.Objects is not { } map)
        {
            return null;
        }

        foreach (object produced in map.Objects)
        {
            if (produced is Control control
                && control.GetType().Name == element
                && map.GetElement(control) is not null)
            {
                return control;
            }
        }

        return null;
    }

    private static string Summary(DesignerViewModel designer) =>
        string.Join(", ", designer.Hierarchy.Select(row => row.Name));

    /// <summary>The colour a brush key has in a variant, asked of the application the way a control asks.</summary>
    private static Color? BrushColor(string key, ThemeVariant variant) =>
        Application.Current is { } application
        && application.TryGetResource(key, variant, out object? found)
        && found is ISolidColorBrush brush
            ? brush.Color
            : null;

    /// <summary>
    /// The pairs this window draws, and the least contrast each may have.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The thresholds are the ones ArxisStudio's design system sets and its theme is tested against:
    /// text by WCAG 1.4.3 and graphics by 1.4.11. Eleven to one for what is read on a surface and
    /// seven on a plate, four and a half for the two quieter levels where something is said in
    /// them and three where it is only distinguishable, four and a half for any state or accent
    /// written as a word or carrying white text, and three for a glyph, a border and the focus ring.
    /// </para>
    /// <para>
    /// A surface written <c>a/b</c> is <c>a</c> laid over <c>b</c>. The plates under the pointer
    /// and under the press are translucent, so that one plate works on every surface, and what the
    /// text is read against is the two together.
    /// </para>
    /// </remarks>
    private static readonly (string Ink, string Surface, double Least)[] PalettePairs =
    [
        ("Fg", "Bg1", 11), ("Fg", "Bg2", 11),
        ("Fg", "Raised", 7), ("Fg", "Sel", 7), ("Fg", "Bg3/Bg1", 7), ("Fg", "Bg3/Bg2", 7), ("Fg", "Bg4/Bg2", 7),

        ("Fg2", "Bg1", 4.5), ("Fg2", "Bg2", 4.5), ("Fg2", "Canvas", 4.5), ("Fg2", "Raised", 4.5),
        ("Fg2", "Sel", 4.5), ("Fg2", "Bg3/Bg1", 4.5), ("Fg2", "Bg3/Bg2", 4.5),

        ("Fg3", "Bg1", 4.5), ("Fg3", "Bg2", 4.5), ("Fg3", "Canvas", 4.5),
        ("Fg3", "Raised", 3), ("Fg3", "Sel", 3), ("Fg3", "Bg3/Bg1", 3), ("Fg3", "Bg3/Bg2", 3),

        ("OnAcc", "AccFill", 4.5), ("OnAcc", "AccFillH", 4.5), ("OnAcc", "AccFillP", 4.5),
        ("OnAcc", "MonoGrn", 4.5), ("OnAcc", "MonoOrg", 4.5), ("OnAcc", "MonoPur", 4.5), ("OnAcc", "MonoRed", 4.5),
        ("OnAcc", "Acc", 3),

        ("Lnk", "Bg1", 4.5), ("Lnk", "Bg2", 4.5), ("Lnk", "Canvas", 4.5),
        ("RedText", "Bg1", 4.5), ("RedText", "Bg2", 4.5), ("RedText", "Canvas", 4.5),
        ("YelText", "Bg1", 4.5), ("YelText", "Bg2", 4.5),
        ("GrnText", "Bg1", 4.5), ("GrnText", "Bg2", 4.5),
        ("Pur", "Bg2", 4.5),

        ("CodeTxt", "Bg1", 4.5), ("CodeTag", "Bg1", 4.5), ("CodeAttr", "Bg1", 4.5),
        ("CodeStr", "Bg1", 4.5), ("CodeCmt", "Bg1", 4.5),

        ("Acc", "Bg1", 3), ("Acc", "Bg2", 3), ("Acc", "Raised", 3), ("Acc", "Sel", 3),
        ("Grn", "Bg1", 3), ("Grn", "Bg2", 3), ("Org", "Bg1", 3), ("Org", "Bg2", 3),
        ("Pur", "Bg1", 3), ("Red", "Bg1", 3), ("Red", "Bg2", 3), ("Yel", "Bg1", 3), ("Yel", "Bg2", 3),
        ("Brd2", "Bg1", 3), ("Brd2", "Bg2", 3), ("Focus", "Bg1", 3), ("Focus", "Bg2", 3),
    ];

    /// <summary>The contrast of a brush against a surface in a variant, or nothing when one is missing.</summary>
    private static double? Contrast(string ink, string surface, ThemeVariant variant)
    {
        string[] layers = surface.Split('/');

        if (BrushColor(ink, variant) is not { } drawn || BrushColor(layers[^1], variant) is not { } ground)
        {
            return null;
        }

        for (int layer = layers.Length - 2; layer >= 0; layer--)
        {
            if (BrushColor(layers[layer], variant) is not { } plate)
            {
                return null;
            }

            ground = Over(plate, ground);
        }

        double one = Luminance(drawn);
        double other = Luminance(ground);

        return (Math.Max(one, other) + 0.05) / (Math.Min(one, other) + 0.05);
    }

    /// <summary>A translucent colour laid over an opaque one.</summary>
    private static Color Over(Color top, Color bottom)
    {
        double alpha = top.A / 255d;

        return Color.FromRgb(Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));

        byte Mix(byte over, byte under) => (byte)Math.Round((over * alpha) + (under * (1 - alpha)));
    }

    /// <summary>Relative luminance, as WCAG defines it.</summary>
    private static double Luminance(Color colour)
    {
        return (0.2126 * Channel(colour.R)) + (0.7152 * Channel(colour.G)) + (0.0722 * Channel(colour.B));

        static double Channel(byte value)
        {
            double share = value / 255d;

            return share <= 0.03928 ? share / 12.92 : Math.Pow((share + 0.055) / 1.055, 2.4);
        }
    }

    private static int Fail(ref int failures, string what)
    {
        Say("! " + what);

        return ++failures;
    }

    private static void Say(string line) =>
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"CHECK {line}"));

    /// <summary>
    /// Waits for something to become true, yielding to the dispatcher between looks.
    /// </summary>
    /// <param name="settle">
    /// Whether to require the condition twice over two ticks. A command that runs detached has not
    /// necessarily started by the time this first looks, and "not busy" is true before it as well as
    /// after it.
    /// </param>
    private static async Task<bool> Until(Func<bool> condition, int seconds, bool settle = false)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);

        if (settle)
        {
            await Task.Delay(400);
        }

        while (DateTime.UtcNow < deadline)
        {
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);

            if (condition())
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }
}
