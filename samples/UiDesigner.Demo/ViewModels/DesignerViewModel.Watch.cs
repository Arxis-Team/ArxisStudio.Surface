using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.ProjectSystem;
using Avalonia.Styling;
using Avalonia.Threading;

namespace UiDesigner.Demo.ViewModels;

/// <summary>
/// Follows the project on disk, because this designer is not the only thing editing it.
/// </summary>
/// <remarks>
/// <para>
/// A form designer is used beside an IDE, not instead of one: the layout is done here and the code
/// is written there, and the same <c>.axaml</c> is open in both. A designer that only knew about its
/// own edits showed a form that had stopped being the file some time ago, and wrote over the other
/// tool's work the next time anybody pressed save.
/// </para>
/// <para>
/// Watching is composed by the host, which is what <c>ArxisStudio.ProjectSystem</c>'s ADR 0016 says
/// and what this is: a watcher, a debounce, and four answers — reload the form, re-register the
/// document a closed control lives in, re-read the project, or rebuild for changed code. The
/// workspace is told nothing it could not be told by a person pressing refresh, and the build is
/// the same build a person could run.
/// </para>
/// <para>
/// Editors do not write files the way this would expect. Rider writes a temporary file and renames
/// it over the original, so what arrives is a rename or a creation rather than a change, and a save
/// can produce several events for one edit. All of them are taken, coalesced over a quarter of a
/// second, and acted on once.
/// </para>
/// </remarks>
public sealed partial class DesignerViewModel
{
    private FileSystemWatcher? _watcher;

    /// <summary>Files that have changed and have not been dealt with yet.</summary>
    private readonly HashSet<string> _touched = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Code files that have changed, which is what makes a rebuild worth running.</summary>
    private readonly HashSet<string> _codeTouched = new(StringComparer.OrdinalIgnoreCase);

    private DispatcherTimer? _settle;

    /// <summary>Whether the project needs re-reading rather than a form reloading.</summary>
    private bool _projectTouched;

    /// <summary>Whether a rebuild for changed code is already running.</summary>
    private bool _buildingForCode;

    /// <summary>
    /// Starts watching the folder the project lives in.
    /// </summary>
    /// <remarks>
    /// The entry point's own directory, and everything under it. That is the project for a
    /// <c>.csproj</c> and the solution folder for a <c>.sln</c>, which is the same thing one level
    /// up — and either way it is where the files this designer opens are.
    /// </remarks>
    private void WatchProject()
    {
        StopWatching();

        if (EntryPoint.IsEmpty || !Directory.Exists(EntryPoint.Directory.Value))
        {
            return;
        }

        _settle ??= CreateSettleTimer();

        try
        {
            _watcher = new FileSystemWatcher(EntryPoint.Directory.Value)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };

            _watcher.Changed += OnFileTouched;
            _watcher.Created += OnFileTouched;
            _watcher.Deleted += OnFileTouched;
            _watcher.Renamed += OnFileTouched;

            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log($"! the project cannot be watched: {error.Message}");
        }
    }

    private DispatcherTimer CreateSettleTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };

        timer.Tick += (_, _) =>
        {
            timer.Stop();

            RunDetached(SettleAsync);
        };

        return timer;
    }

    private void StopWatching()
    {
        if (_watcher is { } watcher)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();

            _watcher = null;
        }

        _settle?.Stop();
    }

    /// <summary>
    /// Notes what changed and waits for the writing to stop.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three different facts arrive on this one event. A <em>markup</em> file whose contents
    /// changed moves what a form shows — an open one by reload, a closed one through the placed
    /// copies of its control. A <em>code</em> file whose contents changed moves what the types
    /// mean, which only a build can settle. And a file that <em>appeared, went away or was
    /// renamed</em> changes what the project consists of, whatever its extension is — an SDK
    /// project takes its items from globs, so a new class is a new item and the panel that lists
    /// them is stale until the evaluation is done again.
    /// </para>
    /// <para>
    /// The noise is filtered out first. Builds write under <c>bin</c> and <c>obj</c> — this
    /// designer's own rebuilds included — tools keep their state in dot-directories, and editors
    /// save through temporary files whose names they do not intend anybody to see — re-evaluating
    /// a project for any of those would be a second of nothing, repeatedly.
    /// </para>
    /// </remarks>
    private void OnFileTouched(object sender, FileSystemEventArgs e)
    {
        if (IsNoise(e.FullPath))
        {
            return;
        }

        // Appearing, going away or being renamed changes what the project consists of. So does a
        // project file being written to — a reference added by hand in the other editor is a change
        // to the project and to nothing else, and it arrives as a plain save.
        bool structural = e.ChangeType is not WatcherChangeTypes.Changed || IsProjectFile(e.FullPath);

        string[] paths = e is RenamedEventArgs renamed
            ? [renamed.FullPath, renamed.OldFullPath]
            : [e.FullPath];

        Dispatcher.UIThread.Post(() =>
        {
            var noted = false;

            foreach (string path in paths)
            {
                if (IsMarkupPath(path))
                {
                    _touched.Add(path);

                    noted = true;
                }

                // A saved .cs is invisible to the evaluation and everything to the types: the
                // other editor is where code is written, and a designer that ignored it showed
                // controls built from code the project has moved past.
                if (IsCodePath(path))
                {
                    _codeTouched.Add(path);

                    noted = true;
                }
            }

            // Appearing and disappearing is the project's business; a save is not.
            if (structural)
            {
                _projectTouched = true;

                noted = true;
            }

            if (!noted)
            {
                return;
            }

            _settle?.Stop();
            _settle?.Start();
        });
    }

    /// <summary>What gets written under a project that nobody is editing.</summary>
    private static bool IsNoise(string path)
    {
        foreach (string part in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part is "bin" or "obj" || (part.Length > 1 && part[0] == '.'))
            {
                return true;
            }
        }

        string name = Path.GetFileName(path);

        // JetBrains saves through these two, and every editor saves through something.
        return name.Contains("___jb_", StringComparison.Ordinal)
            || name.EndsWith('~')
            || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProjectFile(string path) =>
        Path.GetExtension(path).ToUpperInvariant() is ".CSPROJ" or ".SLN" or ".SLNX" or ".PROPS" or ".TARGETS";

    private static bool IsMarkupPath(string path) => IsMarkupExtension(Path.GetExtension(path));

    private static bool IsCodePath(string path) =>
        Path.GetExtension(path).ToUpperInvariant() is ".CS";

    /// <summary>
    /// Deals with everything that changed while the writing was going on.
    /// </summary>
    /// <remarks>
    /// A project file means the evaluation is stale — items, references, the lot — so the workspace
    /// is re-read. A document means whatever shows it is showing something else: a form open on it
    /// is reloaded, and a document nobody has open still feeds the placed copies of its control, so
    /// it is re-registered from the disk and the forms placing it are told. Code means the types
    /// are suspect, which a build settles — and only a build: nothing here guesses what a save
    /// meant to the compiler.
    /// </remarks>
    private async Task SettleAsync()
    {
        bool project = _projectTouched;
        string[] documents = [.. _touched];
        string[] code = [.. _codeTouched];

        _projectTouched = false;
        _touched.Clear();
        _codeTouched.Clear();

        foreach (string path in documents)
        {
            if (File.Exists(path))
            {
                if (!await ReloadIfOpenAsync(path))
                {
                    await RegisterUnopenedAsync(path);
                }

                RefreshVariantIfApplication(path);
            }
            else
            {
                CloseIfOpen(path);
            }
        }

        if (project && IsLoaded)
        {
            Log("The project changed on disk — re-reading it.");

            await _workspace.RefreshAsync(_shutdown.Token);
        }

        if (code.Length > 0 && IsLoaded)
        {
            await RebuildForCodeAsync(code);
        }
    }

    /// <summary>
    /// Follows a save to <c>App.axaml</c>, because the application's variant is every preview's.
    /// </summary>
    /// <remarks>
    /// The other editor is where a project's theme gets flipped, and a designer that only read the
    /// declaration on load showed yesterday's variant until the project was reopened. The sniff is
    /// the same first-tag read the project tree does, so a save to any other document costs one
    /// root element.
    /// </remarks>
    private void RefreshVariantIfApplication(string path)
    {
        if (!CanonicalPath.TryCreate(path, out CanonicalPath file)
            || Sniff(file) is not ("Application", var requested)
            || _workspace.CurrentSnapshot is not { } snapshot
            || !snapshot.TryGetProjectForFile(file, out ProjectSnapshot? owner))
        {
            return;
        }

        ThemeVariant? declared = DeclaredVariant(requested);

        if (_applicationVariants.TryGetValue(owner.Identity, out ThemeVariant? known) && known == declared)
        {
            return;
        }

        _applicationVariants[owner.Identity] = declared;

        Log($"  the application asks for {declared?.ToString() ?? "the platform's variant"} — the previews follow");

        ApplyApplicationVariants();
    }

    /// <summary>
    /// Re-registers a changed document nobody has open, so placed copies of its control follow it.
    /// </summary>
    /// <remarks>
    /// The scenario is the other editor saving <c>MyControl.axaml</c> while only the form placing
    /// it is open here: no tab to reload, and before this, nothing changed on screen until a
    /// restart. Now the document is read again, registered as the control's live markup, and the
    /// forms that place it are marked — the visible one rebuilds at once.
    /// </remarks>
    private async Task RegisterUnopenedAsync(string path)
    {
        if (_population is null
            || !CanonicalPath.TryCreate(path, out CanonicalPath file)
            || _workspace.CurrentSnapshot is not { } snapshot
            || !snapshot.TryGetProjectForFile(file, out ProjectSnapshot? owner))
        {
            return;
        }

        string text;

        try
        {
            text = await ReadAsync(file);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Mid-write; the watcher will bring the next event along.
            return;
        }

        XamlDocument document = XamlDocument.Parse(
            text,
            new XamlParseOptions
            {
                DocumentUri = AvaresUriFor(
                    snapshot, new FormFile(file.FileName, string.Empty, file, owner.Identity)),
            });

        if (document.Root?.GetDirective("Class") is not { Length: > 0 })
        {
            return;
        }

        await SetLiveDocumentAsync(document);
        MarkDependentsStale(document, except: null);
    }

    /// <summary>
    /// Builds the projects whose code changed, and asks for a reload when the build moved the types.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The build is the designer's own — into <c>bin/ArxisStudio</c>, so it never fights the other
    /// editor — and it is narrowed to the projects that own the changed files, which is what keeps
    /// a save's cost proportionate to the save. Its diagnostics land in the Problems pane the way
    /// any build's do; a failed build changes nothing and says so.
    /// </para>
    /// <para>
    /// A successful build is not yet news. <c>IsCurrentOnDisk</c> is what says whether it moved
    /// the types this run holds — an untouched output means the change was cosmetic to the
    /// compiler — and only then is the reload requested, which happens at once when the studio is
    /// in front and clean, and on its next activation otherwise.
    /// </para>
    /// </remarks>
    private async Task RebuildForCodeAsync(IReadOnlyList<string> code)
    {
        if (_buildingForCode)
        {
            // One at a time; what arrived during this build is kept and retried when the timer
            // fires again.
            foreach (string path in code)
            {
                _codeTouched.Add(path);
            }

            _settle?.Start();

            return;
        }

        if (_workspace.CurrentSnapshot is not { } snapshot)
        {
            return;
        }

        var owners = new List<ProjectSnapshot>();

        foreach (string path in code)
        {
            if (CanonicalPath.TryCreate(path, out CanonicalPath file)
                && snapshot.TryGetProjectForFile(file, out ProjectSnapshot? owner)
                && owners.All(known => known.Identity != owner.Identity))
            {
                owners.Add(owner);
            }
        }

        if (owners.Count == 0)
        {
            return;
        }

        _buildingForCode = true;

        try
        {
            Log($"Code changed on disk — building {string.Join(", ", owners.Select(static o => o.Name))}…");

            var built = true;

            foreach (ProjectSnapshot owner in owners)
            {
                built &= await ExecuteAsync(ProjectOperationKind.Build, owner)
                    == ProjectOperationStatus.Succeeded;
            }

            if (!built)
            {
                Log("  ! the code did not build — the previews keep the types they have");

                return;
            }

            if (_assemblies is { } generation && !generation.IsCurrentOnDisk())
            {
                RequestTypeReload("the project's code changed on disk");
            }
            else
            {
                Log("  the build changed nothing this run holds");
            }
        }
        finally
        {
            _buildingForCode = false;
        }
    }

    /// <summary>
    /// Closes a form whose file has gone.
    /// </summary>
    /// <remarks>
    /// Deleted in the other editor, or renamed, which arrives as the same thing. A tab editing a
    /// document with nowhere to save to is worse than no tab: the next save would put the file back,
    /// which is not what deleting it meant. Unsaved edits go with it — there is nothing left to
    /// reconcile them against — and the console says so, because a tab that closes itself without a
    /// word looks like a crash.
    /// </remarks>
    private void CloseIfOpen(string path)
    {
        if (!CanonicalPath.TryCreate(path, out CanonicalPath file)
            || Forms.FirstOrDefault(open => open.File == file) is not { } form)
        {
            return;
        }

        Log($"{form.Name} was deleted outside — closing it"
            + (form.IsDirty ? ", with unsaved edits" : string.Empty));

        CloseForm(form);
    }

    /// <summary>
    /// Brings an open form back in line with its file, answering whether the file was open at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Applied as a document update rather than by reopening the form, so the session, the live tree
    /// and the canvas survive: the selection is put back by path, the tabs do not move, and the
    /// change joins the undo history like any other — which means an edit made in another editor can
    /// be taken back here as well.
    /// </para>
    /// <para>
    /// A form with unsaved edits is not overwritten. Whoever is typing here has work that is not in
    /// the file, and throwing it away because another program wrote to the same path is the one
    /// thing a designer must not do. It says so instead, and the next save is the person's decision.
    /// The answer is still <see langword="true"/> — the file is open, and the unsaved document is
    /// deliberately what its placed copies keep following.
    /// </para>
    /// </remarks>
    private async Task<bool> ReloadIfOpenAsync(string path)
    {
        if (!CanonicalPath.TryCreate(path, out CanonicalPath file)
            || Forms.FirstOrDefault(open => open.File == file) is not { } form)
        {
            return false;
        }

        string text;

        try
        {
            text = await ReadAsync(file);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A file being written by another process is a file to look at again in a moment, not an
            // error: the watcher will bring the next event along with it.
            return true;
        }

        if (form.Live is not { } live)
        {
            return true;
        }

        XamlExternalTextResult result = await live.AcceptExternalTextAsync(
            SourceText.From(text), ChangedOutside, XamlExternalTextPolicy.ApplyIfClean, _shutdown.Token);

        switch (result.Outcome)
        {
            case XamlExternalTextOutcome.AlreadyCurrent or XamlExternalTextOutcome.AlreadySaved:
                // Our own save, reported now or late — or the file put back as it was. A question
                // asked about an earlier write has nothing left to be about.
                form.PendingDiskText = null;

                break;

            case XamlExternalTextOutcome.Taken:
                form.PendingDiskText = null;

                Log($"{form.Name} was changed outside — shown"
                    + (result.Edit is { State: not XamlLiveDocumentState.Live } edit
                        ? $", though the form shows the text before it ({FirstError(edit.Diagnostics)})"
                        : string.Empty));

                break;

            case XamlExternalTextOutcome.Conflict:
                // Held for the person rather than applied: the edits here are not in the file. The bar
                // above the canvas offers both ways out.
                form.PendingDiskText = text;

                // A question for the person, not a failure: said without the "!" errors are marked with.
                Log($"{form.Name} changed on disk under unsaved edits here — keep yours or take the file");

                break;
        }

        return true;
    }

    /// <summary>What a change the IDE made is called in a form's history.</summary>
    private const string ChangedOutside = "Changed outside the designer";

    /// <summary>
    /// Takes the file's text over the form's unsaved edits, which stay one undo away.
    /// </summary>
    private async Task TakeDiskTextAsync()
    {
        if (ActiveForm is not { Live: { } live, PendingDiskText: { } disk } form)
        {
            return;
        }

        XamlExternalTextResult result = await live.AcceptExternalTextAsync(
            SourceText.From(disk), ChangedOutside, XamlExternalTextPolicy.TakeTheirs, _shutdown.Token);

        form.PendingDiskText = null;

        Log($"{form.Name}: the file's text is shown — undo brings your edits back"
            + (result.Outcome == XamlExternalTextOutcome.AlreadyCurrent ? " (it was already the same)" : string.Empty));
    }

    /// <summary>
    /// Keeps the form's edits over the file's text; the form reads as changed, and saving writes it.
    /// </summary>
    private async Task KeepMyTextAsync()
    {
        if (ActiveForm is not { Live: { } live, PendingDiskText: { } disk } form)
        {
            return;
        }

        await live.AcceptExternalTextAsync(
            SourceText.From(disk), ChangedOutside, XamlExternalTextPolicy.KeepMine, _shutdown.Token);

        form.PendingDiskText = null;

        Log($"{form.Name}: your edits are kept — saving writes them over the file");
    }
}
