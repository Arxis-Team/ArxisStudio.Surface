using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.MSBuild;
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
/// Watching is composed from <c>ArxisStudio.ProjectSystem</c>'s pieces, as its ADRs 0016 and 0025 say:
/// <see cref="ProjectSourceWatcher"/> reports every change with its kind, <see cref="FileChangeCoalescer"/>
/// nets a burst into what it amounts to — an editor's save through a temporary file and two renames is
/// one change of the saved file, a move between folders is a rename — and the snapshot classifies the
/// batch (<see cref="SolutionSnapshot.Classify"/>). What is left here is what the answers mean to a
/// designer: a form to read again or to follow, a project to read again, code to build.
/// </para>
/// <para>
/// None of it filters by name. A build writing <c>bin</c> and <c>obj</c> — this designer's own
/// included — a tool's state in a dot-directory and an editor's temporary files come to nothing in
/// the snapshot's answer, because the snapshot knows where its projects build and what they declare.
/// </para>
/// </remarks>
public sealed partial class DesignerViewModel
{
    private ProjectSourceWatcher? _watcher;
    private FileChangeCoalescer? _coalescer;

    /// <summary>The batch being dealt with; the next one waits for it.</summary>
    private Task _settling = Task.CompletedTask;

    /// <summary>Whether a rebuild for changed code is already running.</summary>
    private bool _buildingForCode;

    /// <summary>Projects whose code changed while a rebuild ran, built when it finishes.</summary>
    private readonly HashSet<ProjectIdentity> _codePending = [];

    /// <summary>
    /// Projects an input of whose restore changed — the project file, an import — restored before
    /// their next build.
    /// </summary>
    private readonly HashSet<ProjectIdentity> _restorePending = [];

    /// <summary>How many batches of changes have been dealt with — what a check waits on.</summary>
    internal int SettledBatches { get; private set; }

    /// <summary>What the workspace holds now — what a check reads.</summary>
    internal SolutionSnapshot? CurrentSnapshot => _workspace.CurrentSnapshot;

    /// <summary>
    /// Starts watching what the open solution is made of.
    /// </summary>
    /// <remarks>
    /// Each project's folder with everything below it, and the files outside them that the snapshot
    /// names — the solution, imports above the projects, files linked in. Every new snapshot is
    /// watched as it is (<see cref="Show"/>), so a project that gained a folder hears it.
    /// </remarks>
    private void WatchProject()
    {
        StopWatching();

        if (_workspace.CurrentSnapshot is not { } snapshot)
        {
            return;
        }

        _coalescer = FileChangeCoalescer.ForChanges(OnChanges);
        _watcher = new ProjectSourceWatcher(_coalescer.Add);
        _watcher.Watch(snapshot);
    }

    private void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;

        // Dropped, not delivered: what was pending was about a project this designer is leaving.
        _coalescer?.Dispose();
        _coalescer = null;
    }

    /// <summary>
    /// Takes a batch from the coalescer's thread to the UI thread, behind the one before it.
    /// </summary>
    /// <remarks>
    /// One at a time and in order: a batch is a document reloaded, a project re-read, a build — and
    /// the second of two overlapping ones would act on a snapshot the first is replacing.
    /// </remarks>
    private void OnChanges(ImmutableArray<FileChange> batch) =>
        Dispatcher.UIThread.Post(() => _settling = SettleAfterAsync(_settling, batch));

    private async Task SettleAfterAsync(Task previous, ImmutableArray<FileChange> batch)
    {
        await previous;

        try
        {
            await SettleAsync(batch);
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
            SettledBatches++;
        }
    }

    /// <summary>
    /// Deals with one batch of changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Forms first, because they are what a person is looking at: a form whose file moved follows it,
    /// one whose file went away closes, and one whose file was saved elsewhere is read again — a
    /// document nobody has open still feeds the placed copies of its control, so it is re-registered.
    /// </para>
    /// <para>
    /// Then the project, only when the snapshot says so: an evaluation input changed, a file appeared
    /// or went away where a project's globs reach, or changes were lost. A saved form or class is none
    /// of these, and costs no evaluation. Then code, which a build settles — and only a build: nothing
    /// here guesses what a save meant to the compiler.
    /// </para>
    /// </remarks>
    private async Task SettleAsync(ImmutableArray<FileChange> batch)
    {
        if (_workspace.CurrentSnapshot is not { } snapshot)
        {
            return;
        }

        WorkspaceChangeSet changes = snapshot.Classify(batch);

        if (changes.IsEmpty)
        {
            return;
        }

        foreach (FileRename rename in changes.Renames)
        {
            await FollowIfOpenAsync(snapshot, rename);
        }

        foreach (FileChange change in batch)
        {
            if (change.Kind == FileChangeKind.Deleted)
            {
                CloseIfOpen(change.Path);
            }
        }

        foreach (ProjectItemChange edited in changes.ItemsEdited)
        {
            string path = edited.Item.FullPath.Value;

            if (!IsMarkupPath(path))
            {
                continue;
            }

            if (!await ReloadIfOpenAsync(path))
            {
                await RegisterUnopenedAsync(path);
            }

            RefreshVariantIfApplication(path);
        }

        if (changes.RequiresRescan)
        {
            // Changes were lost, so every open form is read again; one whose file is unchanged says so.
            foreach (FormViewModel form in Forms.ToArray())
            {
                await ReloadIfOpenAsync(form.File.Value);
            }
        }

        NoteRestoreInputs(snapshot, changes);

        ImmutableArray<ProjectIdentity> code = CodeChanged(snapshot, changes, batch);

        if (IsLoaded && (changes.RequiresRescan || !changes.Invalidation.IsEmpty || !changes.MembershipChanged.IsEmpty))
        {
            Log($"The project changed on disk ({changes}) — re-reading it.");

            await _workspace.RefreshAsync(_shutdown.Token);
        }

        if (code.Length > 0 && IsLoaded)
        {
            await RebuildForCodeAsync(code);
        }
    }

    private static bool IsMarkupPath(string path) => IsMarkupExtension(Path.GetExtension(path));

    private static bool IsCodePath(string path) =>
        Path.GetExtension(path).ToUpperInvariant() is ".CS";

    /// <summary>
    /// The projects whose code changed: a class saved, or one appearing, going away or moving where a
    /// project's globs reach.
    /// </summary>
    /// <remarks>
    /// The snapshot has already said which projects such a change belongs to — a class written into
    /// <c>obj</c> by a build is nobody's — so a file counts only for a project the snapshot named, or
    /// when the snapshot declares it.
    /// </remarks>
    private static ImmutableArray<ProjectIdentity> CodeChanged(
        SolutionSnapshot snapshot,
        WorkspaceChangeSet changes,
        ImmutableArray<FileChange> batch)
    {
        var projects = new HashSet<ProjectIdentity>();

        foreach (ProjectItemChange edited in changes.ItemsEdited)
        {
            if (IsCodePath(edited.Item.FullPath.Value))
            {
                projects.Add(edited.Project);
            }
        }

        foreach (FileChange change in batch)
        {
            if (change.Kind is FileChangeKind.Changed or FileChangeKind.Overflow)
            {
                continue;
            }

            foreach (CanonicalPath path in (CanonicalPath[])[change.OldPath, change.Path])
            {
                if (!path.IsEmpty
                    && IsCodePath(path.Value)
                    && snapshot.TryGetProjectForFile(path, out ProjectSnapshot? owner)
                    && (changes.MembershipChanged.Contains(owner.Identity)
                        || changes.Invalidation.Projects.Contains(owner.Identity)
                        || snapshot.TryGetItem(path, out _, out _)))
                {
                    projects.Add(owner.Identity);
                }
            }
        }

        return [.. snapshot.Projects.Select(static project => project.Identity).Where(projects.Contains)];
    }

    /// <summary>
    /// Notes the projects a restore has to run for before their next build.
    /// </summary>
    /// <remarks>
    /// An evaluation input that changed and is not one restore writes itself — the project file, an
    /// import somebody edits — may have changed what there is to restore. Restore's own output
    /// changing is a restore having run, the IDE's or this designer's, and restoring again for it
    /// would never stop (<c>ProjectSnapshot.RestoreOutputs</c>, ProjectSystem ADR 0026).
    /// </remarks>
    private void NoteRestoreInputs(SolutionSnapshot snapshot, WorkspaceChangeSet changes)
    {
        foreach (ProjectSnapshot project in snapshot.Projects)
        {
            if (changes.Invalidation.Causes.Any(cause =>
                    project.EvaluationInputs.Contains(cause) && !project.RestoreOutputs.Contains(cause)))
            {
                _restorePending.Add(project.Identity);
            }
        }
    }

    /// <summary>Whether a project has to be restored before it is built.</summary>
    /// <remarks>
    /// Never restored — none of what a restore writes is on disk — or something its restore reads
    /// changed since. A project whose provider names no restore output has no restore to run.
    /// </remarks>
    private bool NeedsRestore(ProjectSnapshot project) =>
        _restorePending.Remove(project.Identity)
        || (!project.RestoreOutputs.IsEmpty && !project.RestoreOutputs.Any(static output => File.Exists(output.Value)));

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
    /// The build is the designer's own — into <c>bin/ArxisStudio</c> and <c>obj/ArxisStudio</c>, so it
    /// never fights the other editor — and it is narrowed to the projects whose code changed, which is
    /// what keeps a save's cost proportionate to the save. A project is restored first when it has to
    /// be (<see cref="NeedsRestore"/>). Its diagnostics land in the console the way any build's do; a
    /// failed build changes nothing and says so.
    /// </para>
    /// <para>
    /// One at a time: code that changes while a build runs is built when it finishes. A successful
    /// build is not yet news. <c>IsCurrentOnDisk</c> is what says whether it moved the types this run
    /// holds — an untouched output means the change was cosmetic to the compiler — and only then is
    /// the reload requested, which happens at once when the studio is in front and clean, and on its
    /// next activation otherwise.
    /// </para>
    /// </remarks>
    private async Task RebuildForCodeAsync(IReadOnlyCollection<ProjectIdentity> projects)
    {
        if (_buildingForCode)
        {
            _codePending.UnionWith(projects);

            return;
        }

        _buildingForCode = true;

        try
        {
            IReadOnlyCollection<ProjectIdentity> next = projects;

            while (next.Count > 0)
            {
                await BuildChangedCodeAsync(next);

                next = [.. _codePending];
                _codePending.Clear();
            }
        }
        finally
        {
            _buildingForCode = false;
        }
    }

    private async Task BuildChangedCodeAsync(IReadOnlyCollection<ProjectIdentity> projects)
    {
        if (_workspace.CurrentSnapshot is not { } snapshot)
        {
            return;
        }

        ProjectSnapshot[] owners =
        [
            .. snapshot.Projects.Where(project => projects.Contains(project.Identity)),
        ];

        if (owners.Length == 0)
        {
            return;
        }

        Log($"Code changed on disk — building {string.Join(", ", owners.Select(static o => o.Name))}…");

        var built = true;

        foreach (ProjectSnapshot owner in owners)
        {
            if (NeedsRestore(owner)
                && await ExecuteAsync(ProjectOperationKind.Restore, owner) != ProjectOperationStatus.Succeeded)
            {
                built = false;

                continue;
            }

            built &= await ExecuteAsync(ProjectOperationKind.Build, owner) == ProjectOperationStatus.Succeeded;
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

    /// <summary>
    /// Follows a form whose file was renamed or moved, keeping its history and its unsaved edits.
    /// </summary>
    /// <remarks>
    /// A rename in the other editor used to close the form, as if the file had been deleted — which is
    /// what a rename looked like to a watcher that reported paths. The snapshot says where the file
    /// went, a folder renamed around it included, and the document moves there: its session is built
    /// again from the new place, because what it includes is found relative to where it lives.
    /// </remarks>
    private async Task FollowIfOpenAsync(SolutionSnapshot snapshot, FileRename rename)
    {
        if (Forms.FirstOrDefault(open => open.File == rename.OldPath) is not { } form)
        {
            return;
        }

        Uri uri = snapshot.TryGetProjectForFile(rename.NewPath, out ProjectSnapshot? owner)
            && AvaresUriFor(snapshot, new FormFile(rename.NewPath.FileName, string.Empty, rename.NewPath, owner.Identity)) is { } avares
                ? avares
                : new Uri(rename.NewPath.Value);

        await form.MoveToAsync(rename.NewPath, uri, _shutdown.Token);

        MarkOpenFiles();

        Log($"{rename.OldPath.FileName} was moved outside — the form follows it to {rename.NewPath.FileName}");
    }

    /// <summary>
    /// Closes the forms whose file has gone — the file, or a folder it was in.
    /// </summary>
    /// <remarks>
    /// A tab editing a document with nowhere to save to is worse than no tab: the next save would put
    /// the file back, which is not what deleting it meant. Unsaved edits go with it — there is nothing
    /// left to reconcile them against — and the console says so, because a tab that closes itself
    /// without a word looks like a crash. A file moved rather than deleted is followed instead.
    /// </remarks>
    private void CloseIfOpen(CanonicalPath path)
    {
        foreach (FormViewModel form in Forms.Where(open => open.File.StartsWith(path)).ToArray())
        {
            Log($"{form.Name} was deleted outside — closing it"
                + (form.IsDirty ? ", with unsaved edits" : string.Empty));

            CloseForm(form);
        }
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
