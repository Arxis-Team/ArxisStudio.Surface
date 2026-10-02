using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.MSBuild;
using Avalonia.Threading;

namespace UiDesigner.Demo.ViewModels;

/// <summary>
/// A visual form designer over three libraries that know nothing about each other.
/// </summary>
/// <remarks>
/// <para>
/// The division is the whole point, and it is worth stating before any of the code below makes
/// sense:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>ArxisStudio.ProjectSystem</b> says what project is open, which files are in it, what it
/// resolves to, and how to restore, build and run it. It never looks inside a document.
/// </item>
/// <item>
/// <b>ArxisStudio.Markup</b> owns the document: it parses the XAML, it is the only thing that
/// edits the tree, and it builds the live objects a designer shows.
/// </item>
/// <item>
/// <b>ArxisStudio.DesignEditor</b> owns the surface: viewport, grid, selection, handles and
/// gestures. It reads the control tree and never writes to it — structural intent arrives as a
/// request this application answers by editing the document.
/// </item>
/// </list>
/// <para>
/// So every user gesture takes the same route: the editor reports it, this class turns it into a
/// document edit, and the live tree is rebuilt from the document. The document is the truth and the
/// canvas is a view of it — never the other way round, because a canvas that could disagree with
/// the file is a designer that loses work.
/// </para>
/// </remarks>
public sealed partial class DesignerViewModel : Observable, IDisposable
{
    private readonly ProjectWorkspace _workspace = new(new MSBuildProjectProvider());
    private readonly CancellationTokenSource _shutdown = new();

    private bool _disposed;

    public DesignerViewModel()
    {
        // First, because the inspector empties the data section whenever it is rebuilt.
        InitialiseData();

        OpenCommand = new RelayCommand(() => Run(OpenAsync));
        SaveCommand = new RelayCommand(() => Run(SaveAsync), () => CanSave);
        NewFormCommand = new RelayCommand(() => Run(NewFormAsync), () => IsLoaded);

        SaveAllCommand = new RelayCommand(
            () => Run(SaveAllAsync),
            () => Forms.Any(form => form.IsDirty));

        UndoCommand = new RelayCommand(() => StepHistory(back: true), () => ActiveForm is { CanUndo: true });
        RedoCommand = new RelayCommand(() => StepHistory(back: false), () => ActiveForm is { CanRedo: true });

        TakeDiskTextCommand = new RelayCommand(() => Run(TakeDiskTextAsync), () => ActiveForm is { HasPendingDiskText: true });
        KeepMyTextCommand = new RelayCommand(() => Run(KeepMyTextAsync), () => ActiveForm is { HasPendingDiskText: true });

        RestoreCommand = new RelayCommand(() => Run(() => ExecuteAsync(ProjectOperationKind.Restore)), CanOperate);
        BuildCommand = new RelayCommand(() => Run(() => ExecuteAsync(ProjectOperationKind.Build)), CanOperate);

        // The button takes the same route the watcher takes: swap the types in place, and restart
        // only when they will not go. What it does not take is the activation gate — a press is
        // somebody asking now.
        RestartCommand = new RelayCommand(
            () => RunDetached(() => _typeSwap = SwapGenerationAsync()),
            () => NeedsRestart && !EntryPoint.IsEmpty && _typeSwap is not { IsCompleted: false });

        // Every open form says when its document moved — an edit, an undo, the IDE writing the file —
        // and the designer follows from one place, whichever route the change took.
        Forms.CollectionChanged += (_, e) =>
        {
            foreach (FormViewModel gone in e.OldItems?.OfType<FormViewModel>() ?? [])
            {
                gone.DocumentChanged -= OnFormDocumentChanged;
            }

            foreach (FormViewModel added in e.NewItems?.OfType<FormViewModel>() ?? [])
            {
                added.DocumentChanged += OnFormDocumentChanged;
            }
        };

        InitialiseHeader();
        InitialiseGuides();
        InitialiseGroups();
        InitialiseShell();
        InitialiseRun();
        InitialiseToolbox();
        InitialiseClipboard();
        InitialiseStructure();

        _workspace.SnapshotChanged += OnSnapshotChanged;

        // A project whose App.axaml says "Default" is previewed in the platform's variant, so the
        // platform changing its mind must reach the previews — the same way it reaches the
        // running application.
        _platform = Avalonia.Application.Current?.PlatformSettings;

        if (_platform is not null)
        {
            _platform.ColorValuesChanged += OnPlatformColorsChanged;
        }

        Log("Ready. Open a .sln, .slnx or .csproj, then open a form from the Project panel.");
        Log(Environment());
    }

    /// <summary>The platform's settings, held so the subscription above can be released.</summary>
    private readonly Avalonia.Platform.IPlatformSettings? _platform;

    private void OnPlatformColorsChanged(object? sender, Avalonia.Platform.PlatformColorValues e) =>
        Dispatcher.UIThread.Post(ApplyApplicationVariants);

    /// <summary>The forms open on the canvas. The editor's items are forms, not controls.</summary>
    /// <remarks>
    /// An infinite surface holding several forms at once is the arrangement this buys, and it is why
    /// the editor is bound to a collection rather than to one root: comparing two dialogs side by
    /// side is a thing designers do, and nothing here had to be added for it.
    /// </remarks>
    public ObservableCollection<FormViewModel> Forms { get; } = [];

    public ObservableCollection<FormViewModel> SelectedForms { get; } = [];

    public ObservableCollection<string> Output { get; } = [];

    public ObservableCollection<DiagnosticRow> Diagnostics { get; } = [];

    public RelayCommand OpenCommand { get; }

    public RelayCommand SaveCommand { get; }

    public RelayCommand NewFormCommand { get; }

    /// <summary>Writes every form that has unsaved edits.</summary>
    public RelayCommand SaveAllCommand { get; }

    /// <summary>Goes back one edit.</summary>
    public RelayCommand UndoCommand { get; }

    /// <summary>And forward again.</summary>
    public RelayCommand RedoCommand { get; }

    /// <summary>Takes the file's text over the active form's unsaved edits, which stay one undo away.</summary>
    public RelayCommand TakeDiskTextCommand { get; }

    /// <summary>Keeps the active form's edits over the file's text.</summary>
    public RelayCommand KeepMyTextCommand { get; }

    public RelayCommand RestoreCommand { get; }

    public RelayCommand BuildCommand { get; }

    /// <summary>Starts the studio again on the same project, with the same forms open.</summary>
    /// <remarks>
    /// The one thing a process can do about types it has already loaded. What it costs is the
    /// window blinking; what it buys is a designer that never shows a form built from types the
    /// project has moved past.
    /// </remarks>
    public RelayCommand RestartCommand { get; }

    /// <summary>Set by the view, because picking a file needs a window to hang a dialog off.</summary>
    public Func<Task<string?>>? PickEntryPoint { get; set; }


    public bool IsLoaded
    {
        get;
        private set => Set(ref field, value);
    }

    public bool IsBusy
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                RefreshAllCommands();
            }
        }
    }

    public string Status
    {
        get;
        private set => Set(ref field, value);
    } = "No project";

    public string Progress
    {
        get;
        private set => Set(ref field, value);
    } = string.Empty;

    public double Zoom
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                Raise(nameof(ZoomText));
                Raise(nameof(CanvasCaption));
                Raise(nameof(CanvasSize));
                Raise(nameof(CanvasSizeAndZoom));
            }
        }
    } = 1.0;

    /// <summary>The zoom as a person reads it.</summary>
    public string ZoomText => $"{Zoom * 100:F0}%";

    /// <summary>
    /// Whether a drag snaps to the grid, and whether it aligns to its neighbours.
    /// </summary>
    /// <remarks>
    /// Both on, and both switchable, because either one is occasionally the wrong help. Holding Alt
    /// bypasses them for one gesture — which is the setting somebody actually reaches for, and the
    /// reason these two are a pair of checkboxes rather than a settings page.
    /// </remarks>
    public bool SnapToGrid
    {
        get;
        set => Set(ref field, value);
    } = true;

    public bool SnapToGuides
    {
        get;
        set => Set(ref field, value);
    } = true;

    /// <summary>
    /// Opens an entry point, and optionally a form in it, from the command line.
    /// </summary>
    /// <param name="path">The solution or project to open.</param>
    /// <param name="form">A form to open once it has loaded, matched by file name.</param>
    public void OpenAtStartup(string path, string? form = null) =>
        OpenAtStartup(path, form is { Length: > 0 } ? [form] : [], active: null);

    /// <summary>
    /// Opens an entry point and a set of forms in it, from the command line.
    /// </summary>
    /// <remarks>
    /// So the designer can be pointed at a project without a mouse, which is what makes it usable as
    /// a smoke test: the output pane is mirrored to standard output, so a run says whether the
    /// document parsed, how many elements it mapped, and what went wrong when something did. Several
    /// forms is what a restart passes, so the studio comes back with the tabs it had.
    /// </remarks>
    /// <param name="path">The solution or project to open.</param>
    /// <param name="forms">Forms to open once it has loaded, in tab order, matched by file name.</param>
    /// <param name="active">The form to put in front, or <see langword="null"/> for the last opened.</param>
    public void OpenAtStartup(string path, IReadOnlyList<string> forms, string? active) => Run(async () =>
    {
        EntryPoint = CanonicalPath.Create(path);

        await LoadAsync();

        if (forms.Count == 0)
        {
            return;
        }

        // The project panel is filled from the snapshot notification, which is posted rather than
        // raised inline. Yielding to a lower priority lets that arrive first, so there is something
        // to look the name up in.
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);

        foreach (string form in forms)
        {
            if (ProjectForms.FirstOrDefault(candidate =>
                candidate.Name.Equals(form, StringComparison.OrdinalIgnoreCase)) is { } found)
            {
                await OpenFormAsync(found);
            }
            else
            {
                Log($"! no form called {form} in this project");
            }
        }

        if (active is { Length: > 0 }
            && Forms.FirstOrDefault(open =>
                open.Name.Equals(active, StringComparison.OrdinalIgnoreCase)) is { } front)
        {
            ActiveForm = front;
        }
    });

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _shutdown.Cancel();

        if (_platform is not null)
        {
            _platform.ColorValuesChanged -= OnPlatformColorsChanged;
        }

        StopWatching();
        StopProject();
        CloseAllForms();

        _ = DisposeWorkspaceAsync();

        _shutdown.Dispose();
    }

    private CanonicalPath EntryPoint { get; set; }

    /// <summary>
    /// What this designer adds to every evaluation, build and run: an output folder of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This tool is used beside an IDE, and both build the same project. They cannot both own
    /// <c>bin\Debug</c>: the moment one of them has the application running, the other's build stops
    /// with a dozen lines of MSB3026 and then MSB3027 — "the file is locked by .NET Host" — which is
    /// true, unactionable, and looks like the designer is broken.
    /// </para>
    /// <para>
    /// So the designer builds into <c>bin\ArxisStudio</c> and runs what it built there. The property
    /// is relative, so every project in the solution resolves it against itself, and it is passed to
    /// the evaluation as well as to the operations — the run path starts what the evaluation says the
    /// project produces, and the two would disagree if only one of them knew.
    /// </para>
    /// <para>
    /// The intermediate folder is deliberately left shared. That is where the restore writes its
    /// assets file, and a second copy of it would mean restoring the same packages twice for no
    /// benefit — the file both tools read is the same file, and neither writes it while the other is
    /// building.
    /// </para>
    /// </remarks>
    private static readonly ProjectMetadata DesignerOutput = ProjectMetadata.Create(
    [
        new System.Collections.Generic.KeyValuePair<string, string>("BaseOutputPath", "bin/ArxisStudio/"),
    ]);

    private bool CanOperate() => IsLoaded && !IsBusy;

    private static string Environment()
    {
        try
        {
            return $"MSBuild: {MSBuildEnvironment.Register()}";
        }
        catch (InvalidOperationException exception)
        {
            return $"! No MSBuild: {exception.Message} Opening a project will report "
                + $"{MSBuildDiagnosticCodes.MSBuildNotFound}.";
        }
    }

    /// <summary>Runs work the user asked for, and keeps the window honest about it.</summary>
    private async void Run(Func<Task> work)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Log($"! {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
            Progress = string.Empty;
        }
    }

    /// <summary>
    /// Runs work that follows from something rather than from a click, without the busy flag.
    /// </summary>
    private async void RunDetached(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Log($"! {exception.GetType().Name}: {exception.Message}");
        }
    }

    private async Task OpenAsync()
    {
        if (PickEntryPoint is null || await PickEntryPoint() is not { Length: > 0 } picked)
        {
            return;
        }

        EntryPoint = CanonicalPath.Create(picked);

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        Log($"Opening {EntryPoint.FileName}…");

        WorkspaceLoadResult result = await _workspace.LoadAsync(
            new WorkspaceLoadRequest
            {
                Workspace = _workspace.Identity,
                EntryPointPath = EntryPoint,

                // The configuration the header is showing, because it is what the evaluation is of:
                // output paths and conditioned items move with it, and the designer starts what the
                // evaluation says the project produces.
                Configuration = Configuration,

                // And an output folder of its own, so that building here never fights the IDE that
                // has the same project open.
                GlobalProperties = DesignerOutput,

                // Items are how the Project panel finds the forms, so the designer asks for them.
                Options = new WorkspaceLoadOptions { IncludeItems = true },
            },
            _shutdown.Token);

        Log($"  {result.Status} — {result.Diagnostics.Length} diagnostic(s)");

        IsLoaded = result.Snapshot is not null;

        // Watched from here on, because this designer is used beside an IDE and the IDE writes to
        // the same files.
        WatchProject();

        Raise(nameof(StatusLeft));
        Raise(nameof(ProjectName));
        Raise(nameof(EntryPointName));
        Raise(nameof(RunTargetName));

        await ReadBranchAsync(_shutdown.Token).ConfigureAwait(true);

        ShowDiagnostics(result.Diagnostics);
        RefreshAllCommands();
    }

    private void OnSnapshotChanged(object? sender, WorkspaceChangedEventArgs e) =>
        Dispatcher.UIThread.Post(() => Show(e.Snapshot));

    private void Show(SolutionSnapshot snapshot)
    {
        Status = $"{snapshot.Name} — v{snapshot.Version}"
            + (snapshot.HasErrors ? ", with errors" : string.Empty);

        // Watched as it is now: a project that gained or lost a folder, a solution that gained a
        // project, are heard from this snapshot on.
        _watcher?.Watch(snapshot);

        BuildProjectTree(snapshot);
        ShowRunTargets();
        Raise(nameof(StatusLeft));
        RefreshAllCommands();
    }

    private async Task DisposeWorkspaceAsync()
    {
        try
        {
            await _workspace.DisposeAsync();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
        }
    }

    /// <summary>
    /// Keeps what an operation complained about, and says it in the console.
    /// </summary>
    /// <remarks>
    /// The console is where they are read. There was a pane for them once, and with it a build that
    /// failed said "3 diagnostics" here and kept the three sentences somewhere else; now the line
    /// that reports the count is followed by what was counted. An error is written the way every
    /// failure in this log is, so it is coloured as one.
    /// </remarks>
    private void ShowDiagnostics(System.Collections.Generic.IEnumerable<ProjectDiagnostic> diagnostics)
    {
        Diagnostics.Clear();

        foreach (ProjectDiagnostic diagnostic in diagnostics)
        {
            DiagnosticRow row = DiagnosticRow.From(diagnostic);

            Diagnostics.Add(row);

            string where = row.Where.Length > 0 ? $" — {row.Where}" : string.Empty;

            Log(row.Severity == ProjectDiagnosticSeverity.Error
                ? $"  ! {row.Code}: {row.Message}{where}"
                : $"  {row.Severity.ToString().ToLowerInvariant()} {row.Code}: {row.Message}{where}");
        }
    }

    private void RefreshAllCommands()
    {
        OpenCommand.RaiseCanExecuteChanged();
        SaveCommand.RaiseCanExecuteChanged();
        NewFormCommand.RaiseCanExecuteChanged();
        RestoreCommand.RaiseCanExecuteChanged();
        BuildCommand.RaiseCanExecuteChanged();
        RunCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        PauseCommand.RaiseCanExecuteChanged();
        SaveAllCommand.RaiseCanExecuteChanged();
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
        TakeDiskTextCommand.RaiseCanExecuteChanged();
        KeepMyTextCommand.RaiseCanExecuteChanged();
        CopyCommand.RaiseCanExecuteChanged();
        CutCommand.RaiseCanExecuteChanged();
        PasteCommand.RaiseCanExecuteChanged();
        DuplicateCommand.RaiseCanExecuteChanged();
        MoveUpCommand.RaiseCanExecuteChanged();
        MoveDownCommand.RaiseCanExecuteChanged();
        AddPropertyCommand.RaiseCanExecuteChanged();
        DeleteSelectedCommand.RaiseCanExecuteChanged();
    }

    /// <summary>What to do about a form with unsaved edits that is being closed.</summary>
    public enum SaveAnswer
    {
        Save,
        Discard,
        Cancel,
    }

    /// <summary>Asked of the view before unsaved work is thrown away.</summary>
    public Func<string, Task<SaveAnswer>>? AskToSave { get; set; }

    /// <summary>Writes every form that has something to write.</summary>
    private async Task SaveAllAsync()
    {
        foreach (FormViewModel form in Forms.Where(form => form.IsDirty).ToArray())
        {
            await SaveAsync(form);
        }
    }

    /// <summary>Says something in the console. Internal, because the view has things to say too.</summary>
    internal void Log(string message)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Log(message));

            return;
        }

        Output.Add(message);
        Record(message);
        Console.WriteLine(message);

        while (Output.Count > 500)
        {
            Output.RemoveAt(0);
        }
    }

    private static string Describe(int count, string noun) =>
        $"{count} {noun}{(count == 1 ? string.Empty : "s")}";
}

/// <summary>A diagnostic flattened for a list.</summary>
/// <remarks>
/// The severity stays the library's enum rather than its name. As a string it was compared with
/// "Error" in one check and with "ERROR" in another, and the second one — the self-check's "the
/// project did not build" — could not fail on any project.
/// </remarks>
public sealed record DiagnosticRow(ProjectDiagnosticSeverity Severity, string Code, string Message, string Where)
{
    /// <summary>The file the diagnostic is about, so a form's rows can be replaced when it is shown again.</summary>
    public CanonicalPath File { get; init; }

    public static DiagnosticRow From(ProjectDiagnostic diagnostic) => new(
        diagnostic.Severity,
        diagnostic.Code,
        diagnostic.Message,
        diagnostic.FilePath.IsEmpty
            ? string.Empty
            : diagnostic.FilePath.FileName + (diagnostic.Span.IsEmpty ? string.Empty : $":{diagnostic.Span.StartLine}"))
    {
        File = diagnostic.FilePath,
    };
}
