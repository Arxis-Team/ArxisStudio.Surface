using System.Threading.Tasks;
using ArxisStudio.Markup;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.MSBuild;

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
/// one change of the saved file, a move between folders is a rename — and the design host classifies
/// the batch against the snapshot and acts on it (its ADR 0028): a form read again or followed, the
/// project read again, code built. What is left here is the watching, and the two answers a person
/// gives when the file changed under their edits.
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

        if (_workspace.CurrentSnapshot is not { } snapshot || _host is not { } host)
        {
            return;
        }

        _coalescer = FileChangeCoalescer.ForChanges(batch => host.NotifyChanged(batch));
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
