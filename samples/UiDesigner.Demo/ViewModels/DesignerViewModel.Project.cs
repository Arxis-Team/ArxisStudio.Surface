using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.Markup.Xaml;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;

namespace UiDesigner.Demo.ViewModels;

/// <summary>A form the open project contains, whether or not it is on the canvas.</summary>
public sealed record FormFile(string Name, string Folder, CanonicalPath Path, ProjectIdentity Project)
{
    public override string ToString() => Name;
}

public sealed partial class DesignerViewModel
{
    private ProjectAssemblyContext? _assemblies;
    private XamlLoadEnvironment? _environment;
    private ProjectIdentity _environmentProject;

    /// <summary>Every markup document the open project declares.</summary>
    public ObservableCollection<FormFile> ProjectForms { get; } = [];

    /// <summary>Files an open is already under way for.</summary>
    private readonly HashSet<CanonicalPath> _opening = [];

    /// <summary>What the project pane has selected.</summary>
    /// <remarks>
    /// Selecting is state; opening is an act, and <see cref="OpenFile"/> performs it. Assigning
    /// used to open, and that is how one double-click opened the same form twice: the caller set
    /// this and then opened, not knowing the setter had already started an open of its own, and two
    /// detached opens both read an empty <c>Forms</c> before either added to it. Rebuilding the
    /// project tree assigns this too, and a refresh has no business reopening anything.
    /// </remarks>
    public FormFile? SelectedProjectForm
    {
        get;
        set => Set(ref field, value);
    }

    private bool CanSave => ActiveForm is { IsDirty: true };

    /// <summary>
    /// Lists the project's markup, which is what a designer's project panel is for.
    /// </summary>
    /// <remarks>
    /// From <c>Items</c> rather than from a directory walk, so a document the project does not
    /// include is correctly absent — a file sitting in the folder that nothing compiles is not part
    /// of the application, and offering it would invite editing something that has no effect.
    /// </remarks>
    private void BuildProjectTree(SolutionSnapshot snapshot)
    {
        FormFile? previous = SelectedProjectForm;

        ProjectForms.Clear();
        _applicationVariants.Clear();

        foreach (ProjectSnapshot project in snapshot.Projects)
        {
            foreach (ProjectItem item in project.Items)
            {
                if (item.FullPath.IsEmpty
                    || !IsMarkup(item.FullPath)
                    || !item.FullPath.StartsWith(project.ProjectDirectory))
                {
                    continue;
                }

                (string? root, string? requested) = Sniff(item.FullPath);

                // The application is not a form, but it is the one document that decides what
                // every form is drawn with, so its variant is remembered on the way past.
                if (root is "Application")
                {
                    _applicationVariants[project.Identity] = DeclaredVariant(requested);
                }

                if (root is not null && NotAForm.Contains(root, StringComparer.Ordinal))
                {
                    continue;
                }

                string relative = item.FullPath.Value[project.ProjectDirectory.Value.Length..]
                    .Replace('\\', '/')
                    .TrimStart('/');

                int slash = relative.LastIndexOf('/');

                ProjectForms.Add(new FormFile(
                    item.FullPath.FileName,
                    slash < 0 ? project.Name : $"{project.Name}/{relative[..slash]}",
                    item.FullPath,
                    project.Identity));
            }
        }

        BuildProjectPane(snapshot);
        MarkOpenFiles();
        ApplyApplicationVariants();

        Log($"  {Describe(ProjectForms.Count, "form")} in the project");

        if (previous is not null && ProjectForms.FirstOrDefault(f => f.Path == previous.Path) is { } same)
        {
            SelectedProjectForm = same;
        }
    }

    private static bool IsMarkup(CanonicalPath file) => IsMarkupExtension(file.Extension);

    /// <summary>
    /// Whether an extension is one this designer treats as a document.
    /// </summary>
    /// <remarks>
    /// The one definition, because there were three: a project item's, a file tile's and a watched
    /// path's, and one method used two of them on the same file. Which files are documents is a
    /// single fact about this designer and reads better said once.
    /// </remarks>
    internal static bool IsMarkupExtension(string extension) =>
        extension.Equals(".axaml", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase);

    /// <summary>Avalonia's own roots that declare no control, and so no form.</summary>
    private static readonly string[] NotAForm =
        ["Application", "ResourceDictionary", "Styles", "Style", "ControlTheme"];

    /// <summary>
    /// Reads a markup document's root tag and its requested theme variant, and nothing further.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The root answers two questions at once. Whether the document is a form: <c>App.axaml</c>
    /// declares the application and a resource dictionary declares no control at all, so a root on
    /// the <see cref="NotAForm"/> list is kept off the canvas, the tab strip and the count. And
    /// what the project's forms are drawn with: the <c>Application</c> root is where a project
    /// declares its theme variant, which is what the previews must follow rather than the
    /// designer's own.
    /// </para>
    /// <para>
    /// An XML reader rather than a parse, because both answers are in the first tag, and a
    /// designer that parsed every markup file in the solution to build a list would pay for it
    /// again on every refresh.
    /// </para>
    /// <para>
    /// A file that cannot be read answers <see langword="null"/>, which offers it as a form: the
    /// open path remains the authority and says, in words, what is wrong with it — better than a
    /// document silently missing from the list.
    /// </para>
    /// </remarks>
    private static (string? Root, string? RequestedThemeVariant) Sniff(CanonicalPath file)
    {
        try
        {
            using System.Xml.XmlReader reader = System.Xml.XmlReader.Create(
                file.Value,
                new System.Xml.XmlReaderSettings
                {
                    DtdProcessing = System.Xml.DtdProcessing.Ignore,
                    IgnoreComments = true,
                    IgnoreProcessingInstructions = true,
                    IgnoreWhitespace = true,
                });

            return reader.MoveToContent() != System.Xml.XmlNodeType.Element
                ? (null, null)
                : (reader.LocalName, reader.GetAttribute("RequestedThemeVariant"));
        }
        catch (Exception error)
            when (error is System.IO.IOException
                or UnauthorizedAccessException
                or System.Xml.XmlException)
        {
            return (null, null);
        }
    }

    /// <summary>
    /// What each project's <c>App.axaml</c> declares its variant to be, or <see langword="null"/>
    /// for one that leaves it to the platform.
    /// </summary>
    /// <remarks>
    /// Declared, not resolved: "Default" and "no App.axaml" both store <see langword="null"/>, and
    /// the platform is asked at apply time — so an operating system that changes its mind changes
    /// the previews, the same way it would change the running application.
    /// </remarks>
    private readonly System.Collections.Generic.Dictionary<ProjectIdentity, ThemeVariant?>
        _applicationVariants = [];

    /// <summary>Maps what an <c>App.axaml</c> writes to what it means, when it means one.</summary>
    private static ThemeVariant? DeclaredVariant(string? requested) => requested switch
    {
        "Dark" => ThemeVariant.Dark,
        "Light" => ThemeVariant.Light,

        // "Default" spelled out, misspelled, or absent: the application follows the platform, and
        // so must the preview. Guessing a misspelling to be dark or light would be inventing a
        // declaration the project has not made.
        _ => null,
    };

    /// <summary>
    /// The theme variant a form's own application would draw it with.
    /// </summary>
    /// <remarks>
    /// The layer the surface cannot know — see <c>UiDesignerFormItem.ApplicationThemeVariant</c>.
    /// The project's <c>App.axaml</c> is the authority when it declares a side; when it declares
    /// "Default", or there is no application document at all, the answer is the platform's, which
    /// is what the running application would resolve it to. The designer's own variant is never
    /// the answer: the studio being dark is a fact about the studio.
    /// </remarks>
    private ThemeVariant VariantFor(CanonicalPath file) =>
        _workspace.CurrentSnapshot is { } snapshot
            && snapshot.TryGetProjectForFile(file, out ProjectSnapshot? owner)
            && _applicationVariants.TryGetValue(owner.Identity, out ThemeVariant? declared)
            && declared is not null
            ? declared
            : PlatformVariant();

    /// <summary>The variant the operating system is set to, which is what "Default" runs as.</summary>
    private static ThemeVariant PlatformVariant() =>
        Avalonia.Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant
            == Avalonia.Platform.PlatformThemeVariant.Dark
            ? ThemeVariant.Dark
            : ThemeVariant.Light;

    /// <summary>Tells a form's surface which application it is being previewed for.</summary>
    internal void ApplyApplicationVariant(FormViewModel form) =>
        form.Card.ApplicationThemeVariant = VariantFor(form.File);

    /// <summary>Re-answers the variant question for every open form.</summary>
    private void ApplyApplicationVariants()
    {
        foreach (FormViewModel form in Forms)
        {
            ApplyApplicationVariant(form);
        }
    }

    /// <summary>
    /// Opens a form: read the file, parse it, build the live objects, put it on the canvas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The environment comes from the adapter, which is the one piece that knows both families: it
    /// turns a project snapshot into the assemblies a document may name and loads them into a
    /// collectible context. That is what lets a form use the project's own controls rather than only
    /// Avalonia's — after the project has been built, because a control that has not been compiled
    /// is not a type anything can create.
    /// </para>
    /// <para>
    /// A document that fails to load is reported on the form itself rather than thrown. Half the
    /// documents a designer is pointed at are mid-edit, and a designer that refuses to open one is
    /// less useful than one that shows what is wrong with it.
    /// </para>
    /// </remarks>
    private async Task OpenFormAsync(FormFile form)
    {
        if (Forms.FirstOrDefault(open => open.File == form.Path) is { } already)
        {
            ActiveForm = already;

            return;
        }

        // The "already open" test above is read before the first await and acted on long after it,
        // so it cannot settle a race on its own: two opens of one file both find nothing and both
        // add. One form per file is the invariant, and this is where it is kept.
        if (!_opening.Add(form.Path))
        {
            return;
        }

        try
        {
            await OpenFormCoreAsync(form).ConfigureAwait(true);
        }
        finally
        {
            _opening.Remove(form.Path);
        }
    }

    private async Task OpenFormCoreAsync(FormFile form)
    {
        // A form opened while the types are being replaced would be built under whichever
        // generation happened to be current when it looked, which is the one thing a swap cannot
        // survive: two copies, one process.
        if (_typeSwap is { IsCompleted: false } swapping)
        {
            await swapping;
        }

        if (_workspace.CurrentSnapshot is not { } snapshot)
        {
            return;
        }

        Log($"Opening {form.Name}…");

        string text = await ReadAsync(form.Path);

        // Parsed with the URI the document will have once it is embedded, because a relative URI
        // means nothing without one. A freshly created Avalonia window says
        // Icon="/Assets/avalonia-logo.ico", and without a base the load fails on it -- correctly,
        // and uselessly, since the file is right there in the project.
        XamlDocument document = XamlDocument.Parse(
            text,
            new XamlParseOptions { DocumentUri = AvaresUriFor(snapshot, form) });

        // Built first when it has to be. A document naming an x:Class names a type, and a type in a
        // project nobody has compiled does not exist -- so opening a form in a freshly created
        // application failed with "unable to resolve type", which is true, unactionable, and the
        // first thing anybody sees. Building is what a designer does about it.
        if (NeedsBuilding(snapshot, form.Project, document, form.Path))
        {
            snapshot = await BuildBeforeOpeningAsync(snapshot, form.Project) ?? snapshot;
        }

        var opened = new FormViewModel(form.Path, NextFreeSpot())
        {
        };

        // Told before it is shown, so the first frame is already the right one. What the form's
        // application would draw it with is not something the surface can know by itself.
        ApplyApplicationVariant(opened);

        Forms.Add(opened);
        ActiveForm = opened;

        // From here the form is in Forms, and that is what a second open of the same file finds —
        // so the guard has done its job and must let go. Holding it until the load finishes made a
        // close-and-open-again do nothing at all: the reporting step waits for the dispatcher to go
        // idle, so the open was still "in progress" long after the form was on screen, and the next
        // open of that file was dropped in silence.
        _opening.Remove(form.Path);

        MarkOpenFiles();

        await LoadIntoAsync(opened, snapshot, form.Project, document, text);
    }

    /// <summary>
    /// The <c>avares</c> URI a project file will be embedded under.
    /// </summary>
    /// <remarks>
    /// Composed rather than looked up, because the map answers the other direction: it says which
    /// file a URI names, and a project item does not carry the URI it will be given. The rule is the
    /// one the adapter documents — the producing assembly is the authority, the path is relative to
    /// the project directory — and the segments are escaped, because a file name is not a URI and a
    /// '#' in one would truncate the path.
    /// </remarks>
    private static Uri? AvaresUriFor(SolutionSnapshot snapshot, FormFile form)
    {
        if (!snapshot.TryGetProject(form.Project, out ProjectSnapshot? project)
            || !form.Path.StartsWith(project.ProjectDirectory))
        {
            return null;
        }

        string assembly = project.Properties.TryGetValue("AssemblyName", out string? name) && name.Length > 0
            ? name
            : project.Name;

        string relative = form.Path.Value[project.ProjectDirectory.Value.Length..]
            .Replace('\\', '/')
            .TrimStart('/');

        string escaped = string.Join('/', relative.Split('/').Select(Uri.EscapeDataString));

        return new Uri($"avares://{assembly}/{escaped}");
    }

    /// <summary>
    /// Whether this document needs the project compiled before it can produce anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A document with no <c>x:Class</c> names no type of the project's and loads out of Avalonia
    /// alone, so building for it would be a wait bought with nothing. One that names a class needs
    /// that class to exist, which means an assembly — and, less obviously, an assembly built since
    /// the class was last written.
    /// </para>
    /// <para>
    /// That second half is what a freshly created form fell through. The project had been built, so
    /// the assembly was there and the designer went straight to loading — into an assembly compiled
    /// before the form's own code-behind existed, which answered exactly as it should have:
    /// <c>x:Class names 'App.NewWindow', which was not found in any assembly</c>. It is the same for
    /// a form whose code-behind was edited in the other editor since the last build.
    /// </para>
    /// <para>
    /// Compared by time rather than by asking what is in the assembly: the assembly this would have
    /// to inspect is the one the designer is about to load, and loading it to find out whether it is
    /// worth loading is a circle. A file's timestamp answers the same question from outside.
    /// </para>
    /// </remarks>
    private static bool NeedsBuilding(
        SolutionSnapshot snapshot, ProjectIdentity project, XamlDocument document, CanonicalPath file)
    {
        if (document.Root?.GetDirective("Class") is not { Length: > 0 })
        {
            return false;
        }

        if (!snapshot.TryGetProject(project, out ProjectSnapshot? owner))
        {
            return false;
        }

        if (owner.Outputs.FirstOrDefault(o => o.Kind == OutputArtifactKind.Assembly) is not { } assembly
            || !System.IO.File.Exists(assembly.Path.Value))
        {
            return true;
        }

        DateTime built = System.IO.File.GetLastWriteTimeUtc(assembly.Path.Value);

        return Written(file.Value + ".cs") > built || Written(file.Value) > built;

        static DateTime Written(string path) =>
            System.IO.File.Exists(path) ? System.IO.File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
    }

    /// <summary>
    /// Whether the project has an assets file, which is what a build needs and a new project lacks.
    /// </summary>
    /// <remarks>
    /// Asked of MSBuild's own property when the evaluation carried one, and of the default location
    /// otherwise. Both are guesses at the same fact and the cost of guessing wrong is one restore
    /// that was not needed.
    /// </remarks>
    private static bool IsRestored(ProjectSnapshot project)
    {
        string assets = project.Properties.TryGetValue("ProjectAssetsFile", out string? declared)
            && declared is { Length: > 0 }
                ? declared
                : System.IO.Path.Combine(project.ProjectDirectory.Value, "obj", "project.assets.json");

        return System.IO.File.Exists(assets);
    }

    /// <summary>
    /// Builds one project and returns the snapshot to load against.
    /// </summary>
    /// <remarks>
    /// The refresh afterwards is what makes the new assembly reachable: the environment is keyed on
    /// the snapshot version, so loading against the old one would build a context from paths that
    /// were empty when it was taken.
    /// </remarks>
    private async Task<SolutionSnapshot?> BuildBeforeOpeningAsync(
        SolutionSnapshot snapshot, ProjectIdentity project)
    {
        if (!snapshot.TryGetProject(project, out ProjectSnapshot? owner))
        {
            return null;
        }

        Log($"  {owner.Name} has not been built — building it first");

        // A project that has never been restored cannot be built: there is no assets file, and
        // MSBuild says so in a sentence about NETSDK1004 that a person opening a form has no reason
        // to be reading. A project this designer has just created from a template is exactly that
        // project, so the restore comes first and only when the build was going to happen anyway.
        if (!IsRestored(owner) && await ExecuteAsync(ProjectOperationKind.Restore, owner)
            != ProjectOperationStatus.Succeeded)
        {
            Log("  the restore failed — the form is opened anyway, and will say what it could not find");

            return null;
        }

        if (await ExecuteAsync(ProjectOperationKind.Build, owner) != ProjectOperationStatus.Succeeded)
        {
            Log("  the build failed — the form is opened anyway, and will say what it could not find");

            return null;
        }

        await _workspace.RefreshAsync(_shutdown.Token);

        // A generation made before this build now holds yesterday's types — the first open of a
        // form whose code was edited outside lands here. Detected the same way the watcher's
        // rebuild detects it, and answered the same way: a reload, offered or performed.
        if (_assemblies is { } generation && !generation.IsCurrentOnDisk())
        {
            RequestTypeReload($"{owner.Name} was rebuilt after this run loaded its types");
        }

        return _workspace.CurrentSnapshot;
    }

    /// <summary>Parses text into a document and builds its objects into a form.</summary>
    private async Task LoadIntoAsync(
        FormViewModel form,
        SolutionSnapshot snapshot,
        ProjectIdentity project,
        XamlDocument document,
        string text)
    {
        XamlLoadEnvironment environment = EnvironmentFor(snapshot, project);

        // The registry fills from the disk when the generation is created, and the first load
        // waits for it: a form that places a project control must find the control's document
        // already registered, or its first preview would show the compiled shape for one open.
        if (_liveRegistration is { } registering)
        {
            await registering;
        }

        // And this document goes in as the live one — the same call every later edit makes — so
        // its control, when placed on another form, is drawn from what this tab shows.
        await SetLiveDocumentAsync(document);

        // Design mode, which is the whole difference between a designer and a runtime. A form is
        // usually a page of bindings with nothing bound at rest -- the Avalonia template's window is
        // one TextBlock reading {Binding Greeting} -- so loading it the way the application would
        // shows a blank rectangle and nothing to click. Design mode applies what the document says
        // is for looking at: Design.DataContext, d:DesignWidth, the lot.
        var options = new XamlLoadOptions { Mode = XamlLoadMode.Design, RootAccess = form.RootAccess };

        // The file as read is the document, its history starts here, and opening is not a step of
        // it. The session it builds puts its root on the card through the form.
        var live = XamlLiveDocument.Open(document.Uri ?? new Uri(form.File.Value), document.SourceText);

        form.Take(live);

        XamlLiveEditResult attached = await live.AttachAsync(environment, options, _shutdown.Token);

        // Which generation the form now belongs to. The compilation scope travels with the
        // environment — the session brackets every compile itself — but the generation's lifetime
        // is still this designer's to manage: it must outlive the last form loaded under it.
        form.Assemblies = _assemblies;

        ShowMarkupDiagnostics(attached.Diagnostics, text, form.File);

        if (live.Session is not { } session)
        {
            string why = FirstError(attached.Diagnostics);

            form.Fail(why);

            // Said out loud, not only shown. "did not load" with the reason in another panel is the
            // shape of unhelpfulness this whole family is written against.
            Log($"  ! {form.Name} did not load: {why}");

            // A missing type is nearly always a project that has not been built, so the answer is
            // in what the context was given. Naming the project's own output is the useful half.
            if (_assemblies is { } context)
            {
                Log("    context held: " + string.Join(
                    ", ",
                    context.Assemblies.Select(static a => a.Path.FileName).Take(8)));
            }

            OfferRestartIfTypesAreStale(attached.Diagnostics);

            return;
        }

        SizeToContent(form);
        RebuildHierarchy();
        Raise(nameof(CanvasCaption));

        Log($"  {form.Name} loaded, {Describe(session.Objects.MappedElements.Count, "element")} mapped");

        await ReportShownAsync(form);
    }

    /// <summary>
    /// Says whether the form reached the canvas, once the canvas has had a chance to lay it out.
    /// </summary>
    /// <remarks>
    /// Loading and appearing are different things, and the gap between them is where the worst bug
    /// in this designer lived: a window-rooted form loaded perfectly and then threw during layout,
    /// off the stack of everything that could have reported it, leaving a canvas that was simply
    /// empty. A size measured after the fact is the one statement that cannot be made by a form
    /// nobody can see.
    /// </remarks>
    private async Task ReportShownAsync(FormViewModel form)
    {
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);

        // Asked of the card, not of the reference. The card is always there — it is a control the
        // form owns from construction — so testing it for null tested nothing, and the one
        // diagnostic this designer has for "the document loaded and there is nothing to show"
        // silently stopped being reachable. HasContent is the question that still has an answer.
        if (!form.Card.HasContent)
        {
            Log($"  ! {form.Name} produced nothing the canvas can host");

            return;
        }

        Avalonia.Controls.Control surface = form.Card;

        // Reported, not judged. One yield is enough for the container to exist and not always
        // enough for it to have measured, so a zero here means "not yet or not at all" and this is
        // in no position to say which. The number is still the most useful thing available about a
        // form somebody says they cannot see.
        Log($"  surface {surface.GetType().Name} measured "
            + $"{surface.Bounds.Width:F0}×{surface.Bounds.Height:F0}"
);
    }

    /// <summary>
    /// The project's assemblies, loaded once and kept for as long as the project is open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One generation per project per run, and this is the hardest-won line in the designer. A
    /// second generation is a second copy of the same assembly in the same process, and the two
    /// cannot both be there: Avalonia's runtime XAML compiler snapshots the loaded assemblies and
    /// answers a simple name with the first match, so it went on resolving <c>x:Class</c> to the
    /// first copy while the designer built root objects from the newest. The load then failed with
    /// "Unable to substitute MainWindow with MainWindow" — the same name on both sides, because
    /// they had never been the same type.
    /// </para>
    /// <para>
    /// Creating a second one beside the first is what must never happen, and for a long time that
    /// meant a run could not replace its types at all: a superseded generation was unloaded but
    /// never collected, because creating one control registers its type in Avalonia's process-wide
    /// property registry and that registration outlived the context.
    /// </para>
    /// <para>
    /// It can be replaced now, but only through its own death.
    /// <see cref="SwapGenerationAsync"/> lets go of everything built from this generation, asks
    /// the adapter to reclaim it, and creates a successor <em>after</em> that is proven — see
    /// ADR 0023. When the proof fails the studio starts again on the same project, which is the
    /// same honesty by a slower route. Markup, meanwhile, is read from the file every time and
    /// never waits for any of this.
    /// </para>
    /// </remarks>
    private XamlLoadEnvironment EnvironmentFor(SolutionSnapshot snapshot, ProjectIdentity project)
    {
        if (_environment is not null && _assemblies is not null && _environmentProject == project)
        {
            return _environment;
        }

        // Another project's generation is a different assembly name, so it cannot be resolved in
        // this one's place. It stays until the forms loaded under it close — and its registry goes
        // with it, because the registry holds that generation's types.
        if (_assemblies is { } previous)
        {
            _retired.Add((previous, _population));

            _population = null;
        }

        (_environment, _assemblies) = ProjectXamlEnvironment.CreateFor(snapshot, project);
        _environmentProject = project;

        CreatePopulation(snapshot);
        SweepRetired();

        Log($"  load context “{_assemblies.Name}” — {_assemblies.Assemblies.Length} assemblies");

        return _environment;
    }

    /// <summary>
    /// Whether the studio is holding types older than the project on disk, and knows it.
    /// </summary>
    /// <remarks>
    /// Set when a load failed for a reason new types would fix, and when a build has moved the
    /// types on disk past the ones this run holds — the same fact said twice. It lights the
    /// command that brings them in, which swaps in place and restarts only as a fallback; the
    /// name is the older of the two behaviours and the banner it drives is the newer.
    /// </remarks>
    public bool NeedsRestart
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                RestartCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>What the studio says it is offering to restart for.</summary>
    public string RestartReason
    {
        get;
        private set => Set(ref field, value);
    } = string.Empty;

    /// <summary>Says so, once, when a failure is one only a restart can clear.</summary>
    /// <remarks>
    /// Decided by which diagnostic said it rather than by wording alone. A class the assembly does
    /// not have yet is <c>AXM3020</c>, a warning since Markup stopped failing the load over it, and
    /// its answer is a build — a restart would only start the same project again. What a restart
    /// does clear is this run holding a different copy of a type than the one on disk, and Avalonia
    /// reports that without a code of its own: "Unable to substitute" from its loader, "Could not
    /// load type" from the runtime. So those words are read only in the diagnostics that carry
    /// Avalonia's text — a load that failed, an object that could not be created.
    /// </remarks>
    private void OfferRestartIfTypesAreStale(IEnumerable<MarkupDiagnostic> diagnostics)
    {
        bool stale = diagnostics.Any(static diagnostic =>
            diagnostic.Code is XamlLoaderDiagnosticCodes.RuntimeLoadFailure
                or XamlLoaderDiagnosticCodes.ObjectCreationFailure
                or XamlLoaderDiagnosticCodes.RootInstanceCreationFailure
            && (diagnostic.Message.Contains("Unable to substitute", StringComparison.OrdinalIgnoreCase)
                || diagnostic.Message.Contains("Could not load type", StringComparison.OrdinalIgnoreCase)));

        if (!stale)
        {
            return;
        }

        AskForRestart("this run holds the types the project had when it was opened");
    }

    /// <summary>
    /// Starts the studio again on this project, with every open form reopened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unsaved work stops it, and says so: a restart that discarded edits to save the user a press
    /// would be the studio deciding something it has no business deciding. Save first, then this.
    /// </para>
    /// <para>
    /// The new process is started before this one closes, so the two overlap for a moment rather
    /// than leaving the screen empty. It is given the same entry point, every open form's name in
    /// tab order, and which one is in front — which is what the command line takes, so a restart
    /// puts the studio back the way it was rather than back to one tab.
    /// </para>
    /// </remarks>
    private void Restart()
    {
        if (Forms.FirstOrDefault(static form => form.IsDirty) is { } unsaved)
        {
            Log($"! {unsaved.Name} has unsaved edits — save them, then Reload");

            return;
        }

        if (System.Environment.ProcessPath is not { Length: > 0 } executable)
        {
            Log("! this build cannot restart itself — close the studio and start it again");

            return;
        }

        var start = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false };

        start.ArgumentList.Add(EntryPoint.Value);

        foreach (FormViewModel open in Forms)
        {
            start.ArgumentList.Add(open.Name);
        }

        if (ActiveForm is { } active)
        {
            start.ArgumentList.Add("--active");
            start.ArgumentList.Add(active.Name);
        }

        try
        {
            System.Diagnostics.Process.Start(start);
        }
        catch (Exception error)
            when (error is System.ComponentModel.Win32Exception or System.IO.IOException)
        {
            Log($"! the studio could not start itself again: {error.Message}");

            return;
        }

        Log("Restarting…");

        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    /// <summary>Takes the offer back, because the types that were behind have arrived.</summary>
    private void ClearRestartAsk()
    {
        RestartReason = string.Empty;
        NeedsRestart = false;
        _typesBehind = false;
    }

    /// <summary>
    /// Says the types are behind, in words, and lights the command that brings them in.
    /// </summary>
    /// <remarks>
    /// The command swaps the generation in place and restarts only if the old one will not leave,
    /// so what is offered here is "the new types", not "a new process" — the studio decides which
    /// of the two it takes when the button is pressed.
    /// </remarks>
    private void AskForRestart(string because)
    {
        RestartReason = because;

        if (NeedsRestart)
        {
            return;
        }

        NeedsRestart = true;

        Log($"! {because} — press Reload to bring the new types in");
    }

    private static async Task<string> ReadAsync(CanonicalPath file) =>
        await System.IO.File.ReadAllTextAsync(file.Value);

    /// <summary>
    /// Makes the canvas item match the form's own size, when the document states one.
    /// </summary>
    /// <remarks>
    /// A window says how big it is and a user control usually does not. Guessing for the second is
    /// better than showing a zero-sized item: the designer's own default is a size somebody can
    /// drag, and the document keeps whatever it said.
    /// </remarks>
    private static void SizeToContent(FormViewModel form)
    {
        if (form.Root is not { } root)
        {
            return;
        }

        // Both read before either is written. Assigning the card's width triggers the drag
        // preview, which writes the card's — still stale — height back into the root; reading the
        // root's height after that reads the designer's own echo, and the reconciliation this
        // method exists for converges on the wrong number.
        double width = root.Width;
        double height = root.Height;

        if (double.IsFinite(width) && width > 0)
        {
            form.Width = width;
        }

        if (double.IsFinite(height) && height > 0)
        {
            form.Height = height;
        }
    }

    /// <summary>Where a form opens, which is the same place for every one of them.</summary>
    /// <remarks>
    /// Forms used to be staggered down the canvas, because the canvas held all of them at once and
    /// two at one spot would have covered each other. It holds the active one now, so the stagger
    /// only moved the second form somewhere else for no reason a user could see.
    /// </remarks>
    private static Point NextFreeSpot() => new(80, 80);

    /// <summary>
    /// Closes one form: the tab, the card, and the session behind them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The next form to be active is the one that took its place — the tab to its right, or the last
    /// one when it was the last. That is what every tab strip does, and it is better than falling
    /// back to nothing and making the user pick again.
    /// </para>
    /// <para>
    /// The session is disposed after the form has left both collections, because disposing tears
    /// down objects the canvas may still be walking. Unsaved edits go with it, deliberately: this
    /// designer saves on Ctrl+S and a close that silently wrote to a file would be worse.
    /// </para>
    /// </remarks>
    public void CloseForm(FormViewModel form) => CloseForm(form, ask: false);

    /// <summary>
    /// Closes a form, asking about unsaved edits when there is somebody to ask.
    /// </summary>
    /// <remarks>
    /// Asked on the gesture and not asked otherwise. Pressing the cross on a tab with edits in it is
    /// the one time a person can still change their mind; a form whose file has been deleted
    /// underneath it has nothing left to save to, and a project being closed has already been
    /// answered for.
    /// </remarks>
    public void CloseForm(FormViewModel form, bool ask)
    {
        ArgumentNullException.ThrowIfNull(form);

        if (ask && form.IsDirty && AskToSave is not null)
        {
            RunDetached(async () =>
            {
                switch (await AskToSave(form.Name))
                {
                    case SaveAnswer.Cancel:
                        return;

                    case SaveAnswer.Save:
                        await SaveAsync(form);
                        break;

                    default:
                        break;
                }

                CloseForm(form, ask: false);
            });

            return;
        }

        CloseFormCore(form);
    }

    private void CloseFormCore(FormViewModel form)
    {
        int index = Forms.IndexOf(form);

        if (index < 0)
        {
            return;
        }

        Forms.RemoveAt(index);

        if (ReferenceEquals(ActiveForm, form))
        {
            ActiveForm = Forms.Count > 0 ? Forms[Math.Min(index, Forms.Count - 1)] : null;
        }

        MarkOpenFiles();

        Log($"Closed {form.Name}.");

        RunDetached(async () =>
        {
            await form.DisposeAsync();

            // The form may have been the last user of a retired generation.
            SweepRetired();
        });
    }

    /// <summary>Generations that have been superseded but may still be in use by an open form.</summary>
    private readonly List<(ProjectAssemblyContext Context, ProjectXamlPopulation? Population)> _retired = [];

    /// <summary>Disposes every retired generation no open form is loaded under.</summary>
    /// <remarks>
    /// The registry first, always: it holds delegates on the generation's types, and a context
    /// asked to unload while a registry still roots it would never collect.
    /// </remarks>
    private void SweepRetired()
    {
        for (int index = _retired.Count - 1; index >= 0; index--)
        {
            (ProjectAssemblyContext context, ProjectXamlPopulation? population) = _retired[index];

            if (Forms.All(form => !ReferenceEquals(form.Assemblies, context)))
            {
                population?.Dispose();
                context.Dispose();

                _retired.RemoveAt(index);
            }
        }
    }

    private void CloseAllForms()
    {
        foreach (FormViewModel form in Forms.ToArray())
        {
            Forms.Remove(form);

            // Shutting down, and nothing waits for a form's document to finish: blocking this thread
            // on it would be waiting for work that finishes on this thread.
            _ = form.DisposeAsync().AsTask();
        }

        foreach ((ProjectAssemblyContext context, ProjectXamlPopulation? population) in _retired)
        {
            population?.Dispose();
            context.Dispose();
        }

        _retired.Clear();

        _population?.Dispose();
        _population = null;

        _assemblies?.Dispose();
        _assemblies = null;
        _environment = null;
    }

    /// <summary>Turns Markup's diagnostics into the project model's, so one list shows both.</summary>
    private void ShowMarkupDiagnostics(
        IEnumerable<MarkupDiagnostic> diagnostics, string documentText, CanonicalPath file)
    {
        foreach (ProjectDiagnostic translated in
            ProjectMarkupDiagnostics.ToProject(diagnostics, documentText, file))
        {
            Diagnostics.Add(DiagnosticRow.From(translated));
        }
    }
}
