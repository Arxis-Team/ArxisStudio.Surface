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
    /// Opens a form: the design host reads the file, builds the live objects in the project's types,
    /// and the form puts them on the canvas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The host gives the document a history of its own, its <c>avares</c> URI and the environment of
    /// its project — the types the project's build sees — and attaches it to the generation that is
    /// live; a form opened while the types are being replaced waits for the successor. A project that
    /// had not been built was built when the host started, so a form naming an <c>x:Class</c> finds it.
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
        if (_host is not { } host)
        {
            return;
        }

        Log($"Opening {form.Name}…");

        var opened = new FormViewModel(form.Path, NextFreeSpot());

        // Told before it is shown, so the first frame is already the right one. What the form's
        // application would draw it with is not something the surface can know by itself.
        ApplyApplicationVariant(opened);

        Forms.Add(opened);
        ActiveForm = opened;

        // From here the form is in Forms, and that is what a second open of the same file finds — so
        // the guard has done its job and must let go.
        _opening.Remove(form.Path);

        MarkOpenFiles();

        XamlLiveDocument live;

        try
        {
            live = await host.OpenDocumentAsync(
                form.Path, new ProjectDesignDocumentOptions { RootAccess = opened.RootAccess }, _shutdown.Token);
        }
        catch (System.IO.IOException error)
        {
            opened.Fail(error.Message);

            Log($"  ! {form.Name} could not be read: {error.Message}");

            return;
        }

        opened.Take(live);

        // The document the host opened is the one the tabs show: told now, so it is attached when the
        // types are swapped and nothing else is.
        ShowTheActiveDocument();

        // What the load found was said as the form's state was: see ReportState.
        if (live.Session is not { } session)
        {
            string why = live.State == XamlLiveDocumentState.Detached
                ? "the project's types are not loaded in this process — restart to show the form"
                : FirstError(live.Diagnostics);

            opened.Fail(why);

            // Said out loud, not only shown. "did not load" with the reason in another panel is the
            // shape of unhelpfulness this whole family is written against.
            Log($"  ! {form.Name} did not load: {why}");

            return;
        }

        SizeToContent(opened);
        RebuildHierarchy();
        Raise(nameof(CanvasCaption));

        Log($"  {form.Name} loaded, {Describe(session.Objects.MappedElements.Count, "element")} mapped");

        await ReportShownAsync(opened);
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

        // The card first, then the document: disposing the session tears down objects the canvas may
        // still be walking. The host closes a window the session built.
        XamlLiveDocument? live = form.Live;

        form.Close();

        if (live is not null && _host is { } host)
        {
            RunDetached(() => host.CloseDocumentAsync(live, _shutdown.Token).AsTask());
        }
    }

    /// <summary>Takes every form off the canvas; their documents are the host's, and go with it.</summary>
    private void CloseAllForms()
    {
        ActiveForm = null;

        foreach (FormViewModel form in Forms.ToArray())
        {
            Forms.Remove(form);

            form.Close();
        }
    }

    /// <summary>
    /// Closes the open project: its forms, its host and its watching — answering what the forms said,
    /// so a reload of the same project can open them again with their text.
    /// </summary>
    private async Task<IReadOnlyList<HandoffForm>> CloseProjectAsync()
    {
        if (_host is null && Forms.Count == 0)
        {
            return [];
        }

        bool same = _workspace.CurrentSnapshot is { } open && open.Request.EntryPointPath == EntryPoint;

        IReadOnlyList<HandoffForm> forms = same
            ? [.. Forms.Select(static form => HandoffForm.Of(form.File, form.Live, form.Location, form.Width, form.Height, form.SelectedPath))]
            : [];

        StopWatching();
        CloseAllForms();

        await StopHostAsync();

        NeedsRestart = false;
        RestartReason = string.Empty;

        return forms;
    }

    /// <summary>
    /// Turns Markup's diagnostics into the project model's, so one list shows both — and says them in
    /// the console, which is where this designer's diagnostics are read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A form's rows are replaced rather than added to: what a form says now is what is true now, and
    /// a list that kept every load's findings grew with every edit made in the other editor.
    /// </para>
    /// <para>
    /// They used to go into the list and nowhere else, and the list has no pane: a form opened with a
    /// type nothing resolves said "did not load" and kept the reason where nobody could read it.
    /// </para>
    /// </remarks>
    private void ShowMarkupDiagnostics(
        IEnumerable<MarkupDiagnostic> diagnostics, string documentText, CanonicalPath file)
    {
        foreach (DiagnosticRow stale in Diagnostics.Where(row => row.File == file).ToArray())
        {
            Diagnostics.Remove(stale);
        }

        foreach (ProjectDiagnostic translated in
            ProjectMarkupDiagnostics.ToProject(diagnostics, documentText, file))
        {
            DiagnosticRow row = DiagnosticRow.From(translated);

            Diagnostics.Add(row);

            string where = row.Where.Length > 0 ? $" — {row.Where}" : string.Empty;

            Log(row.Severity == ProjectDiagnosticSeverity.Error
                ? $"  ! {row.Code}: {row.Message}{where}"
                : $"  {row.Code}: {row.Message}{where}");
        }
    }
}
