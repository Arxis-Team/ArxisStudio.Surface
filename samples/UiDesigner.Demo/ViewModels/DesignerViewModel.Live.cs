using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.Markup.Xaml;

namespace UiDesigner.Demo.ViewModels;

/// <summary>
/// Keeps placed controls showing their documents as they are now, not as they were built.
/// </summary>
/// <remarks>
/// <para>
/// A form that places <c>&lt;views:MyControl /&gt;</c> gets the control the way its assembly has
/// it, because the placed instance loads the markup that was compiled into it. The adapter's
/// <c>ProjectXamlPopulation</c> is the seam that changes that: every markup document of the
/// project is registered with it — from disk when the project opens, and replaced with the live
/// document whenever a form is opened, edited or reloaded — so every instance created from then
/// on carries the markup as it is right now, unsaved edits included. See the adapter's ADR 0022.
/// </para>
/// <para>
/// Population changes constructions, never instances already on screen. So the second half lives
/// here: when a control's document changes, every open form that places it is marked stale, the
/// active one is rebuilt at once, and the rest are rebuilt when they next become active — which
/// costs nothing while they are hidden and makes switching to them show the truth.
/// </para>
/// </remarks>
public sealed partial class DesignerViewModel
{
    private ProjectXamlPopulation? _population;

    /// <summary>The bulk registration of the project's documents, awaited before the first load.</summary>
    private Task? _liveRegistration;

    /// <summary>
    /// Creates the registry beside a freshly created generation and starts filling it.
    /// </summary>
    /// <remarks>
    /// Called from <see cref="EnvironmentFor"/>, which is the one place a generation is born. The
    /// disk pass is started rather than awaited — opening a form awaits it, so the first preview
    /// never races the registry — and it ends by re-registering the documents of any forms that
    /// are already open, whose unsaved state outranks the disk.
    /// </remarks>
    private void CreatePopulation(SolutionSnapshot snapshot)
    {
        if (_assemblies is not { } assemblies || _environment is not { } environment)
        {
            return;
        }

        _population = ProjectXamlPopulation.Create(assemblies, environment);

        _population.PopulationFailed += (_, failed) => Log(
            $"! {failed.ControlType.Name} could not be drawn from its live markup — "
            + "the compiled shape is shown: "
            + string.Join(
                "; ",
                failed.Diagnostics.Where(static d => d.Severity != MarkupDiagnosticSeverity.Info)
                    .Select(static d => d.Message)
                    .DefaultIfEmpty("no diagnostic said why")));

        _liveRegistration = RegisterProjectDocumentsAsync(snapshot);
    }

    /// <summary>
    /// Registers every markup document the snapshot names, read from disk.
    /// </summary>
    /// <remarks>
    /// All of them, not only the designable ones: a document with no <c>x:Class</c> answers with
    /// nothing and costs a parse, and deciding which files declare a control is exactly the
    /// knowledge this should not duplicate. Files that cannot be read right now are skipped — the
    /// watcher brings their next write along, and the compiled markup covers the meantime.
    /// </remarks>
    private async Task RegisterProjectDocumentsAsync(SolutionSnapshot snapshot)
    {
        if (_population is not { } population)
        {
            return;
        }

        var registered = 0;

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

                if (await RegisterFromDiskAsync(population, snapshot, item.FullPath, project.Identity))
                {
                    registered++;
                }
            }
        }

        // The disk pass last of all defers to what is open: a form mid-edit is ahead of its file,
        // and the file must not shadow it.
        foreach (FormViewModel form in Forms.ToArray())
        {
            if (form.Document is { } document)
            {
                await SetLiveDocumentAsync(document);
            }
        }

        if (registered > 0)
        {
            Log($"  {Describe(registered, "control document")} following the disk");
        }
    }

    /// <summary>Reads one file and registers it, answering whether it declared a control.</summary>
    private async Task<bool> RegisterFromDiskAsync(
        ProjectXamlPopulation population,
        SolutionSnapshot snapshot,
        CanonicalPath file,
        ProjectIdentity project)
    {
        string text;

        try
        {
            text = await ReadAsync(file);
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        {
            return false;
        }

        XamlDocument document = XamlDocument.Parse(
            text,
            new XamlParseOptions
            {
                DocumentUri = AvaresUriFor(snapshot, new FormFile(file.FileName, string.Empty, file, project)),
            });

        try
        {
            return await population.SetDocumentAsync(document, _shutdown.Token) is not null;
        }
        catch (ObjectDisposedException)
        {
            // The generation was retired mid-pass — the project is being closed or replaced, and
            // whatever replaces it starts a pass of its own.
            return false;
        }
    }

    /// <summary>
    /// Makes a document what instances of its control are drawn from, from now on.
    /// </summary>
    /// <remarks>
    /// Called with the live document on every route it changes: opening a form, applying an edit,
    /// undo and redo, and a reload from disk. A document that is not a control's — no
    /// <c>x:Class</c>, or a class this run's types do not have — is answered with
    /// <see langword="null"/> inside and costs nothing.
    /// </remarks>
    private async Task SetLiveDocumentAsync(XamlDocument document)
    {
        if (_population is not { } population)
        {
            return;
        }

        try
        {
            await population.SetDocumentAsync(document, _shutdown.Token);
        }
        catch (ObjectDisposedException)
        {
            // The generation was retired between the check and the call, which is a project
            // being closed — nothing to keep current any more.
        }
    }

    /// <summary>
    /// Marks every open form that places the changed control, and refreshes the visible one.
    /// </summary>
    /// <remarks>
    /// The document names the control by its class's simple name, and the scan is over what the
    /// other documents actually say — the same walk the restart nudge used to make, kept because
    /// it is honest about the one thing it must be: a form that does not place the control is not
    /// rebuilt for it.
    /// </remarks>
    private void MarkDependentsStale(XamlDocument changed, FormViewModel? except)
    {
        if (changed.Root?.GetDirective("Class") is not { Length: > 0 } full)
        {
            return;
        }

        string className = full[(full.LastIndexOf('.') + 1)..];

        foreach (FormViewModel form in Forms)
        {
            if (ReferenceEquals(form, except)
                || form.Document?.Root is not { } root
                || !SelfAndDescendants(root).Any(element => element.Name.LocalName == className))
            {
                continue;
            }

            form.IsStale = true;

            if (ReferenceEquals(form, ActiveForm))
            {
                RunDetached(() => RefreshStaleAsync(form));
            }
        }
    }

    /// <summary>
    /// Rebuilds a stale form from its own document, so its placed controls are constructed again.
    /// </summary>
    /// <remarks>
    /// The document has not changed — the control's has — so this is a rebuild rather than an
    /// edit: the history is untouched, the dirty flag keeps whatever it said, and the selection is
    /// put back by path when the form is the one in front.
    /// </remarks>
    private async Task RefreshStaleAsync(FormViewModel form)
    {
        if (!form.IsStale || form.Document is not { } document)
        {
            return;
        }

        form.IsStale = false;

        XamlElementPath? selection =
            ReferenceEquals(form, ActiveForm) && Selected is { IsPropertyElementSyntax: false } element
                ? XamlElementPath.Of(element)
                : null;

        await RebuildFromAsync(form, document, "refresh");

        if (selection is not null && form.Document is { } updated)
        {
            Reselect(form, selection.Resolve(updated) ?? selection.Parent?.Resolve(updated));
        }
    }

    private static IEnumerable<XamlElement> SelfAndDescendants(XamlElement element)
    {
        yield return element;

        foreach (XamlElement child in element.Elements)
        {
            foreach (XamlElement nested in SelfAndDescendants(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>Whether a successful build has left this run's types behind the disk.</summary>
    private bool _typesBehind;

    /// <summary>Asked of the view, because whether the studio's window is in front is its to know.</summary>
    public Func<bool>? IsStudioActive { get; set; }

    /// <summary>
    /// Whether stale types restart the studio by themselves, which is the desk arrangement and not
    /// the scripted one.
    /// </summary>
    /// <remarks>
    /// The self-checks drive one process through a story and read its answers; a studio that
    /// replaced itself mid-story would take the story with it. They turn this off and assert the
    /// banner instead, which is the same fact reported without the act.
    /// </remarks>
    public bool AutoReloadForCode { get; set; } = true;

    /// <summary>
    /// Called by the view when the studio's window comes to the front.
    /// </summary>
    /// <remarks>
    /// The other editor is where code is written, so the moment of coming back here is the moment
    /// stale types stop being tolerable — the same rule Unity applies. The reload happens now if
    /// it can; the banner stays if it cannot.
    /// </remarks>
    public void OnStudioActivated() => TryReloadForTypes(activated: true);

    /// <summary>
    /// Notes that the types on disk have moved past this run's, and reloads when that is safe.
    /// </summary>
    private void RequestTypeReload(string because)
    {
        _typesBehind = true;

        AskForRestart(because);
        TryReloadForTypes(activated: false);
    }

    /// <summary>
    /// Brings the new types in, in place when they can be and by restarting when they cannot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two refusals, and one that used to be here and is not any more. A busy studio or a running
    /// application is mid-something a swap would cut, and a window in the background does not
    /// bring itself forward — the types arrive when the person comes back, which is
    /// <see cref="OnStudioActivated"/>.
    /// </para>
    /// <para>
    /// Unsaved edits no longer stop it. A restart would have discarded them, so it refused; a swap
    /// rebuilds every form from the document it is holding, so the edits come across untouched
    /// along with the history behind them. If the swap cannot prove the old types left, it falls
    /// back to the restart — and the restart refuses over unsaved work exactly as it always did.
    /// </para>
    /// </remarks>
    private void TryReloadForTypes(bool activated)
    {
        if (!_typesBehind || !NeedsRestart || !AutoReloadForCode)
        {
            return;
        }

        if (IsBusy || IsRunning || _typeSwap is { IsCompleted: false })
        {
            return;
        }

        if (!activated && IsStudioActive?.Invoke() != true)
        {
            return;
        }

        RunDetached(() => _typeSwap = SwapGenerationAsync());
    }
}
