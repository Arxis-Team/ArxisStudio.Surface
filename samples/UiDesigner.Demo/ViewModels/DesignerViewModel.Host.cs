using System;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.Markup.Xaml;
using ArxisStudio.ProjectSystem.MSBuild;

namespace UiDesigner.Demo.ViewModels;

/// <summary>
/// The project's types, kept current beside the IDE by ProjectSystem's design host.
/// </summary>
/// <remarks>
/// <para>
/// What this designer used to do itself — a generation per project, the population that makes placed
/// controls follow their documents, a build after a saved class, the swap of the types and the order
/// it has to keep, the restart when they will not go — is the host's now (ProjectSystem ADR 0028).
/// The designer opens and closes documents through it, lets go of its forms when asked
/// (<see cref="FormsParticipant"/>), says what it is in the middle of (<see cref="Defer"/>), and
/// decides what to tell the person about what the host reports.
/// </para>
/// <para>
/// The host's events arrive on the user interface thread, and its work runs off it. A swap is held
/// off only by what the designer itself is doing: a gesture on the canvas, a value half typed, a
/// dialog, a drag. The window being in the background does not hold it off, and nor does the
/// application running from the designer — the types replaced are the designer's, not the app's.
/// </para>
/// </remarks>
public sealed partial class DesignerViewModel
{
    private ProjectDesignHost? _host;
    private IDisposable? _formsParticipant;

    /// <summary>The design host of the open project, or <see langword="null"/> when none is open.</summary>
    internal ProjectDesignHost? Host => _host;

    /// <summary>How many batches of changes the host has dealt with — what a check waits on.</summary>
    internal int SettledBatches { get; private set; }

    /// <summary>What the workspace holds now — what a check reads.</summary>
    internal SolutionSnapshot? CurrentSnapshot => _workspace.CurrentSnapshot;

    /// <summary>What a change the IDE made is called in a form's history.</summary>
    private const string ChangedOutside = "Changed outside the designer";

    /// <summary>
    /// How the host builds and lets go: into the designer's own output folders, and with the designer's
    /// own input state let go of last.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This tool is used beside an IDE, and both build the same project. They cannot both own
    /// <c>bin\Debug</c>: the moment one of them has the application running, the other's build stops
    /// with a dozen lines of MSB3026 and then MSB3027 — "the file is locked by .NET Host" — which is
    /// true, unactionable, and looks like the designer is broken. Nor <c>obj\Debug</c>: Rider saves
    /// before it runs, this designer hears the save and builds too, and two builds writing one
    /// intermediate folder fail one of them.
    /// </para>
    /// <para>
    /// So the designer builds into <c>bin\ArxisStudio</c> and <c>obj\ArxisStudio</c> —
    /// ProjectSystem's <see cref="MSBuildDesignOutput"/>, its ADR 0026. The properties are relative, so
    /// every project in the solution resolves them against itself, and they are passed to the
    /// evaluation as well as to the builds: the generation loads what the evaluation says the project
    /// produces, and the two would disagree if only one of them knew. The output paths move and the
    /// bases do not, so the IDE's <c>bin\Debug</c> stays out of the project's items and the restore is
    /// shared — <c>obj\project.assets.json</c> is the file both tools read.
    /// </para>
    /// </remarks>
    private ProjectDesignHostOptions HostOptions() => new()
    {
        BuildProperties = MSBuildDesignOutput.GlobalProperties,
        Configuration = Configuration,
        ExternalEditDescription = ChangedOutside,
        ReleaseHostState = _ =>
        {
            ClearInputState();

            return ValueTask.CompletedTask;
        },
    };

    /// <summary>
    /// The request the project is loaded with: what the host needs from the evaluation, and whether
    /// bindings compile by default, which the inspector's data section asks.
    /// </summary>
    private WorkspaceLoadRequest LoadRequest()
    {
        WorkspaceLoadRequest request = HostOptions().CreateLoadRequest(_workspace, EntryPoint);

        return request with
        {
            Options = request.Options with
            {
                AdditionalProperties = [.. request.Options.AdditionalProperties, "AvaloniaUseCompiledBindingsByDefault"],
            },
        };
    }

    /// <summary>Starts a host over the loaded workspace: builds what is out of date and loads the types.</summary>
    private async Task StartHostAsync()
    {
        await StopHostAsync();

        var host = new ProjectDesignHost(_workspace, HostOptions());

        host.StateChanged += OnHostStateChanged;
        host.BuildCompleted += OnBuildCompleted;
        host.SwapCompleted += OnSwapCompleted;
        host.RestartRequired += OnRestartRequired;
        host.ExternalConflict += OnExternalConflict;
        host.DocumentMoved += OnDocumentMoved;
        host.DocumentDeleted += OnDocumentDeleted;
        host.ChangesApplied += OnChangesApplied;
        host.PopulationFailed += OnPopulationFailed;
        host.OperationFailed += OnHostFailed;

        _host = host;
        _formsParticipant = host.Register(new FormsParticipant(this));

        if (SwapsHeldFor is { } held)
        {
            _held = host.Gate.Defer(held);
        }

        await host.StartAsync(_shutdown.Token);

        foreach (ProjectDiagnostic diagnostic in host.GenerationDiagnostics)
        {
            Log($"  {diagnostic.Code}: {diagnostic.Message}");
        }

        Log(host.GenerationName is { } generation
            ? $"  the project's types are loaded — generation “{generation}”, {Describe(host.DesignSet.Length, "project")}"
            : "  the project's types could not be loaded in this process — see above");
    }

    /// <summary>Stops the host: its documents are closed and its generation reclaimed.</summary>
    private async Task StopHostAsync()
    {
        _formsParticipant?.Dispose();
        _formsParticipant = null;

        _held?.Dispose();
        _held = null;

        if (_host is not { } host)
        {
            return;
        }

        _host = null;

        host.StateChanged -= OnHostStateChanged;
        host.BuildCompleted -= OnBuildCompleted;
        host.SwapCompleted -= OnSwapCompleted;
        host.RestartRequired -= OnRestartRequired;
        host.ExternalConflict -= OnExternalConflict;
        host.DocumentMoved -= OnDocumentMoved;
        host.DocumentDeleted -= OnDocumentDeleted;
        host.ChangesApplied -= OnChangesApplied;
        host.PopulationFailed -= OnPopulationFailed;
        host.OperationFailed -= OnHostFailed;

        await host.DisposeAsync();
    }

    /// <summary>The deferral <see cref="SwapsHeldFor"/> keeps on the current host.</summary>
    private IDisposable? _held;

    /// <summary>
    /// Holds every swap off for as long as it is set, on every host the designer starts — for a
    /// self-check that keeps hold of forms across its steps and would lose them to a swap.
    /// </summary>
    internal string? SwapsHeldFor
    {
        get;
        set
        {
            field = value;

            _held?.Dispose();
            _held = value is not null ? _host?.Gate.Defer(value) : null;
        }
    }

    /// <summary>
    /// Holds a swap of the project's types off while something is in progress that a swap would cut.
    /// </summary>
    /// <param name="what">What is in progress, for the console.</param>
    /// <returns>The deferral, or nothing to dispose when no project is open.</returns>
    internal IDisposable Defer(string what) => _host?.Gate.Defer(what) ?? Nothing.Instance;

    /// <summary>The form showing a document, if any does.</summary>
    private FormViewModel? FormOf(XamlLiveDocument document) =>
        Forms.FirstOrDefault(form => ReferenceEquals(form.Live, document));

    private void OnHostStateChanged(object? sender, ProjectDesignStateChangedEventArgs e)
    {
        TypesState = e.State;

        RefreshAllCommands();
    }

    /// <summary>Where the project's types are: live, being built, waiting to be swapped, being swapped.</summary>
    public ProjectDesignState TypesState
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                Raise(nameof(TypesStateText));
            }
        }
    } = ProjectDesignState.Starting;

    /// <summary>The state of the types as the status bar says it.</summary>
    public string TypesStateText => TypesState switch
    {
        ProjectDesignState.Building => "Building…",
        ProjectDesignState.SwapPending => "New types waiting" + (_host?.Gate.Reasons is { Length: > 0 } reasons ? $" ({reasons[0]})" : string.Empty),
        ProjectDesignState.Swapping => "Swapping types…",
        ProjectDesignState.RestartRequired => "Restart required",
        _ => string.Empty,
    };

    /// <summary>The last build the host reported — what a check reads.</summary>
    internal ProjectDesignBuildResult? LastBuild { get; private set; }

    /// <summary>How many builds the host has reported — what a check waits on.</summary>
    internal int Builds { get; private set; }

    private void OnBuildCompleted(object? sender, ProjectDesignBuildCompletedEventArgs e)
    {
        ProjectDesignBuildResult result = e.Result;

        LastBuild = result;
        Builds++;

        Log(result.Status == ProjectOperationStatus.Succeeded
            ? $"Built {Describe(result.Projects.Length, "project")} ({result.Reason})"
                + (result.Restored ? " after a restore" : string.Empty)
                + (result.TypesChanged ? " — the types changed" : " — the types this run holds are current")
                + $", {result.Duration.TotalMilliseconds:F0} ms"
            : $"! the build failed ({result.Reason}) — the previews keep the types they have");

        // What the build said replaces what the last one said, whichever way it went: a build that
        // succeeded clears the errors of the one that did not.
        ShowDiagnostics(result.Diagnostics);
    }

    private void OnSwapCompleted(object? sender, ProjectDesignSwapCompletedEventArgs e)
    {
        ProjectDesignSwapReport report = e.Report;

        Log(report.Reclaimed
            ? $"Types swapped in place ({report.Reason}) — {report}"
            : $"! the old types would not leave this process ({report.Reason}) — {report}");

        if (report.Reclaimed)
        {
            // A swap that worked ends a run of restarts that found the types held.
            _restartsInARow = 0;
        }

        LastSwap = report;
        Swaps++;
    }

    /// <summary>The last swap of the types, as the host reported it — what a check reads.</summary>
    internal ProjectDesignSwapReport? LastSwap { get; private set; }

    /// <summary>How many swaps the host has reported — what a check waits on.</summary>
    internal int Swaps { get; private set; }

    private void OnExternalConflict(object? sender, ProjectDesignConflictEventArgs e)
    {
        if (FormOf(e.Document) is not { } form)
        {
            return;
        }

        // Held for the person rather than applied: the edits here are not in the file. The bar above
        // the canvas offers both ways out. A question, not a failure: said without the "!".
        form.PendingDiskText = e.DiskText;

        Log($"{form.Name} changed on disk under unsaved edits here — keep yours or take the file");
        RefreshAllCommands();
    }

    private void OnDocumentMoved(object? sender, ProjectDesignDocumentEventArgs e)
    {
        if (FormOf(e.Document) is not { } form)
        {
            return;
        }

        form.MovedTo(e.File);

        MarkOpenFiles();

        Log($"{e.Previous.FileName} was moved outside — the form follows it to {e.File.FileName}");
    }

    /// <summary>
    /// Closes a form whose file has gone — the file, or a folder it was in.
    /// </summary>
    /// <remarks>
    /// A tab editing a document with nowhere to save to is worse than no tab: the next save would put
    /// the file back, which is not what deleting it meant. Unsaved edits go with it — there is nothing
    /// left to reconcile them against — and the console says so, because a tab that closes itself
    /// without a word looks like a crash. A file moved rather than deleted is followed instead.
    /// </remarks>
    private void OnDocumentDeleted(object? sender, ProjectDesignDocumentEventArgs e)
    {
        if (FormOf(e.Document) is not { } form)
        {
            return;
        }

        Log($"{form.Name} was deleted outside — closing it" + (form.IsDirty ? ", with unsaved edits" : string.Empty));

        CloseForm(form);
    }

    private void OnChangesApplied(object? sender, ProjectDesignChangesEventArgs e)
    {
        foreach (ProjectItemChange edited in e.Changes.ItemsEdited)
        {
            RefreshVariantIfApplication(edited.Item.FullPath);
        }

        // A question asked about a file that the other editor then put back has nothing left to be
        // about: the form reads as its file again.
        foreach (FormViewModel form in Forms)
        {
            if (form.PendingDiskText is not null && form.Live is { IsDirty: false })
            {
                form.PendingDiskText = null;
            }
        }

        SettledBatches++;
        RefreshAllCommands();
    }

    private void OnPopulationFailed(object? sender, XamlLivePopulationFailedEventArgs e) => Log(
        $"! {e.ControlType.Name} could not be drawn from its live markup — the compiled shape is shown: "
        + string.Join(
            "; ",
            e.Diagnostics.Where(static d => d.Severity != MarkupDiagnosticSeverity.Info)
                .Select(static d => d.Message)
                .DefaultIfEmpty("no diagnostic said why")));

    private void OnHostFailed(object? sender, ProjectDesignFailureEventArgs e) =>
        Log($"! {e.Operation}: {e.Exception.GetType().Name}: {e.Exception.Message}");

    /// <summary>
    /// Follows a save to <c>App.axaml</c>, because the application's variant is every preview's.
    /// </summary>
    /// <remarks>
    /// The other editor is where a project's theme gets flipped, and a designer that only read the
    /// declaration on load showed yesterday's variant until the project was reopened. The sniff is the
    /// same first-tag read the project tree does, so a save to any other document costs one root
    /// element.
    /// </remarks>
    private void RefreshVariantIfApplication(CanonicalPath file)
    {
        if (!IsMarkup(file)
            || Sniff(file) is not ("Application", var requested)
            || _workspace.CurrentSnapshot is not { } snapshot
            || !snapshot.TryGetProjectForFile(file, out ProjectSnapshot? owner))
        {
            return;
        }

        Avalonia.Styling.ThemeVariant? declared = DeclaredVariant(requested);

        if (_applicationVariants.TryGetValue(owner.Identity, out Avalonia.Styling.ThemeVariant? known) && known == declared)
        {
            return;
        }

        _applicationVariants[owner.Identity] = declared;

        Log($"  the application asks for {declared?.ToString() ?? "the platform's variant"} — the previews follow");

        ApplyApplicationVariants();
    }

    /// <summary>A deferral that holds nothing off, for when there is no project.</summary>
    private sealed class Nothing : IDisposable
    {
        public static Nothing Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
