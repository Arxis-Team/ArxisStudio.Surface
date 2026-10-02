using System;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Surface.UiDesigner;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Reactive;

namespace UiDesigner.Demo.ViewModels;

/// <summary>
/// One form open on the canvas: the file, the document, and the live objects built from it.
/// </summary>
/// <remarks>
/// <para>
/// The three are kept together because they are three views of one thing and they must never drift.
/// <see cref="Document"/> is the truth — every edit goes through it — <see cref="Root"/> is what the
/// canvas shows, and <see cref="Objects"/> is the map between them, which is what turns a click on a
/// button into the element in the file that produced it.
/// </para>
/// <para>
/// Keeping them in step is Markup's <see cref="XamlLiveDocument"/>, not this class's: the document's
/// history, whether it differs from the file, the session over it and what to do when the session
/// cannot follow a change all live there (Markup ADR 0025). This class holds one, shows the root its
/// session built, and says when anything moved. It kept an undo stack of whole documents of its own
/// until then, beside a workspace whose history nothing stepped through, and decided whether a form
/// was changed by comparing documents by reference — so undoing back to the file left it changed.
/// </para>
/// <para>
/// <see cref="Location"/>, <see cref="Width"/> and <see cref="Height"/> are the form's place on the
/// designer's infinite surface and have nothing to do with the form's own layout. They belong here
/// because the editor binds its container to them, and they are deliberately not written to the
/// document: where a designer parked a window is not a fact about the window.
/// </para>
/// </remarks>
public sealed class FormViewModel : Observable, IAsyncDisposable
{
    public FormViewModel(CanonicalPath file, Point location)
    {
        File = file;
        Name = file.FileName;
        Location = location;
        RootAccess = new FormRootAccess(Card);

        FollowTheCard();
    }

    /// <summary>The file this form was read from and will be written back to.</summary>
    public CanonicalPath File { get; }

    public string Name
    {
        get;
        private set => Set(ref field, value);
    }

    /// <summary>Where the form sits on the designer's surface.</summary>
    public Point Location
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                Raise(nameof(ChromeLeft));
                Raise(nameof(ChromeTop));
            }
        }
    }

    public double Width
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                Raise(nameof(SizeText));
                PreviewTheWidth();
            }
        }
    } = 480;

    public double Height
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                Raise(nameof(SizeText));
                PreviewTheHeight();
            }
        }
    } = 360;

    /// <summary>
    /// Makes the form the size the card is, as the card is dragged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The card follows the pointer, and the form inside it used to follow the document — which does
    /// not change until the drag ends. So a resize showed the form at its old size inside a card at
    /// its new one: growing left a form too small for its card, and shrinking left one hanging out
    /// past every edge, until the button came up and it snapped into place. The size in the caption
    /// was the card's and the size in the inspector was the document's, and for the length of the
    /// gesture they disagreed.
    /// </para>
    /// <para>
    /// This is the one thing the designer writes to the live tree, and it writes nothing else and
    /// nowhere else: a preview lasts as long as the gesture, and the document edit on completion is
    /// what makes it true. The document remains the only thing an edit touches — a preview that was
    /// never committed is corrected by the next load, which reads the file.
    /// </para>
    /// <para>
    /// One side at a time, and that is not tidiness. The card takes its size from the root — it is
    /// the form, so the root's declared size is its size — and hands each side on to this view
    /// model as it arrives. Writing both sides back on either change wrote the side that had not
    /// arrived yet: a window declared 900 × 600 had its width reach here first, and the answer
    /// wrote the default height over the 600 the document said. Each side now answers only for
    /// itself, so a value that came from the root goes back to the root unchanged.
    /// </para>
    /// <para>
    /// A window needs none of this — its content is laid out by the card and follows it — but a
    /// root shown as it stands carries its own declared size, and that one does not follow the card
    /// until it is told.
    /// </para>
    /// </remarks>
    private void PreviewTheWidth()
    {
        if (Root is { } root && double.IsFinite(Width) && Width > 0)
        {
            root.Width = Width;
        }
    }

    /// <summary>The other side. See <see cref="PreviewTheWidth"/>.</summary>
    private void PreviewTheHeight()
    {
        if (Root is { } root && double.IsFinite(Height) && Height > 0)
        {
            root.Height = Height;
        }
    }

    /// <summary>The document, which is the only thing any edit touches.</summary>
    public XamlDocument? Document => Live?.Document;

    /// <summary>
    /// The document with its history and the session over it, from the moment the file is read.
    /// </summary>
    /// <remarks>
    /// Outlives the form object across a swap of the project's types: detached, it holds nothing of
    /// the generation it was shown under, so it is handed to the form built on the far side (see
    /// <see cref="FormMemory"/>), and the history and the unsaved edits go with it.
    /// </remarks>
    internal XamlLiveDocument? Live { get; private set; }

    /// <summary>Raised on the UI thread once the live document's text, saved state or session moved.</summary>
    internal event EventHandler<XamlLiveDocumentChangedEventArgs>? DocumentChanged;

    /// <summary>
    /// Where the selection is on this form, said in a way that survives an edit.
    /// </summary>
    /// <remarks>
    /// Per form, because a selection belongs to the document it was made in. Held by the designer
    /// as a single element, it was put back after every change of every form — and an external save
    /// of a tab in the background moved the inspector into that tab's document.
    /// </remarks>
    internal XamlElementPath? SelectedPath { get; set; }

    /// <summary>
    /// What the file says now, when it changed on disk while this form had edits the file does not.
    /// </summary>
    /// <remarks>
    /// Held rather than applied: whoever is typing here has work that is not in the file. The form
    /// says so above the canvas, and the person decides — take the file, which keeps their edits one
    /// undo away, or keep theirs, which leaves the form changed against the file.
    /// </remarks>
    public string? PendingDiskText
    {
        get;
        internal set
        {
            if (Set(ref field, value))
            {
                Raise(nameof(HasPendingDiskText));
            }
        }
    }

    /// <summary>Whether the file changed under unsaved edits and the person has not said what to keep.</summary>
    public bool HasPendingDiskText => PendingDiskText is not null;

    /// <summary>The live control tree the document produced.</summary>
    /// <remarks>
    /// What the document means, not what the canvas shows — see <see cref="Card"/> for the
    /// difference and why there is one.
    /// </remarks>
    public Control? Root
    {
        get;
        private set => Set(ref field, value);
    }

    /// <summary>
    /// The card the canvas shows the form in — and the container the editor lays out, which is the
    /// same object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A window cannot be a child of anything: Avalonia gives one a <c>TopLevelHost</c> parent the
    /// moment it is constructed, so putting it in a <c>ContentControl</c> throws during layout, off
    /// the stack of whatever asked for the form. Something has to stand in for it.
    /// </para>
    /// <para>
    /// That something used to be a control nested inside the editor's container —
    /// <c>XamlDesignSurface</c>, from Markup — and the nesting cost this sample three things to keep
    /// in step: the card against the stand-in inside it, the card's size against the root's, and a
    /// window's title bar drawn as a separate layer over the canvas. It is the editor's own
    /// container now, <c>UiDesignerFormItem</c> (ADR 0020 of ArxisStudio.Surface): the card <em>is</em>
    /// the stand-in, its size <em>is</em> the form's, and the title bar is part of it.
    /// <c>FormsDesignerView</c> hands this object to the editor as the container for this form.
    /// </para>
    /// <para>
    /// The form owns it from construction rather than waiting for the editor to make one, because
    /// loading and appearing are different moments: a form is published, measured and asked whether
    /// it has anything to show before the canvas has laid anything out.
    /// </para>
    /// </remarks>
    public UiDesignerFormItem Card { get; } = new();

    /// <summary>What the form's session asks for the root through, around every write it makes.</summary>
    /// <remarks>Goes into the options of every session the form gets: opening, rebuilding, swapping.</remarks>
    internal IXamlRootAccess RootAccess { get; }

    /// <summary>Follows what the card reports about the form, which it keeps current.</summary>
    /// <remarks>
    /// Observed rather than copied on publication. The title bar appears and disappears with the
    /// root — a window wears one, a user control does not, and <c>WindowDecorations</c> is a property
    /// an inspector edits — and the caption this designer draws sits above whichever it is.
    /// </remarks>
    private void FollowTheCard()
    {
        var moved = new AnonymousObserver<object?>(_ => Raise(nameof(ChromeTop)));

        Card.GetObservable(UiDesignerFormItem.RootProperty).Subscribe(moved);
        Card.GetObservable(UiDesignerFormItem.DecorationsProperty)
            .Subscribe(new AnonymousObserver<WindowDecorations>(_ => Raise(nameof(ChromeTop))));
    }

    /// <summary>Where the designer's chrome starts, which is the card's left edge.</summary>
    public double ChromeLeft => Location.X;

    /// <summary>
    /// Where the designer's caption starts vertically, which is far enough above the card for it.
    /// </summary>
    /// <remarks>
    /// The caption hangs above the form rather than over it, and above the window's title bar when
    /// there is one. The title bar is the card's own and stands outside its bounds, so this is the
    /// card's top edge less the caption and less the bar. The bar's height is asked of the theme
    /// rather than repeated here: it is a resource somebody may change, and a number copied from it
    /// would go on being the old one.
    /// </remarks>
    public double ChromeTop => Location.Y - CaptionHeight - (WearsTitleBar ? TitleBarHeight : 0);

    /// <summary>The caption's row and the gap under it.</summary>
    private const double CaptionHeight = 22;

    /// <summary>Whether the card is drawing a window's title bar above itself.</summary>
    private bool WearsTitleBar =>
        Card.Root is Window && Card.Decorations == WindowDecorations.Full;

    private static double TitleBarHeight =>
        Application.Current is { } application
            && application.TryFindResource("UiDesigner.Form.TitleBar.Height", out object? height)
            && height is double value
            ? value
            : 0;

    /// <summary>The map between the two, in both directions.</summary>
    public XamlObjectMap? Objects => Session?.Objects;

    internal bool CanUndo => Live?.CanUndo == true;

    internal bool CanRedo => Live?.CanRedo == true;

    /// <summary>
    /// Everything about a form that outlives the objects built from a generation of types.
    /// </summary>
    /// <remarks>
    /// A swap cannot keep the form object: measurement showed that a form left open — even taken
    /// off the canvas — keeps the generation it was built under in the process, and a successor
    /// created beside it would be a second copy of every type. So the form is remembered, closed,
    /// and built again on the far side, which is what makes a swap something other than a reopen:
    /// the place on the canvas is here, and the text, the history and the unsaved edits are the
    /// live document's, detached so that it holds nothing of the generation it leaves.
    /// </remarks>
    internal sealed record FormMemory(
        CanonicalPath File,
        Point Location,
        double Width,
        double Height,
        XamlLiveDocument? Live,
        XamlElementPath? SelectedPath);

    /// <summary>
    /// Detaches the live document and hands it over with the form's place, for the form built on
    /// the far side of a swap.
    /// </summary>
    /// <remarks>
    /// The session goes — the canvas is told, and the card gives the root back — and the document,
    /// its history and what is saved stay in the memory. This form keeps nothing of either.
    /// </remarks>
    internal async ValueTask<FormMemory> RememberAsync()
    {
        XamlLiveDocument? live = Live;

        if (live is not null)
        {
            await live.DetachAsync();

            Let(live);
        }

        Assemblies = null;

        return new FormMemory(File, Location, Width, Height, live, SelectedPath);
    }

    /// <summary>Puts a remembered form back, before its document is attached again.</summary>
    internal void Recall(FormMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);

        Location = memory.Location;
        Width = memory.Width;
        Height = memory.Height;
        SelectedPath = memory.SelectedPath;

        if (memory.Live is { } live)
        {
            Take(live);
        }
    }

    /// <summary>Starts showing a live document: its session's root on the card, its state in the tab.</summary>
    internal void Take(XamlLiveDocument live)
    {
        ArgumentNullException.ThrowIfNull(live);

        if (Live is not null)
        {
            throw new InvalidOperationException($"{Name} already shows a document.");
        }

        Live = live;

        live.SessionReplaced += OnSessionReplaced;
        live.Changed += OnDocumentChanged;

        if (live.Session is { } session)
        {
            Publish(session);
        }

        RaiseDocumentState();
    }

    /// <summary>Stops listening to a live document that is being handed on or closed.</summary>
    private void Let(XamlLiveDocument live)
    {
        live.SessionReplaced -= OnSessionReplaced;
        live.Changed -= OnDocumentChanged;

        Live = null;
    }

    /// <summary>
    /// Puts the root of the session the live document now has on the card, closing a window the
    /// previous one built.
    /// </summary>
    /// <remarks>
    /// Raised while the previous session is still open, on the UI thread. A window-rooted form's root
    /// is a real <c>Window</c>, never shown, and the windowing platform holds it until it is closed —
    /// on Win32 one never closed survives every collection, and with it every type of its generation.
    /// Nothing else will close it: the session's objects are the host's.
    /// </remarks>
    private void OnSessionReplaced(object? sender, XamlSessionReplacedEventArgs e)
    {
        if (e.Previous?.RootObject is { } previous && !ReferenceEquals(previous, e.Current?.RootObject))
        {
            if (ReferenceEquals(Card.Root, previous))
            {
                Card.Root = null;
            }

            Root = null;

            if (previous is Window window)
            {
                CloseQuietly(window);
            }
        }

        if (e.Current is { } session)
        {
            Publish(session);
        }

        RaiseDocumentState();
    }

    /// <summary>Republishes after a change and says what moved.</summary>
    /// <remarks>
    /// After every change of the text, not only when the session was replaced: an update that keeps
    /// the session still builds new objects — an insert rebuilds its parent's children — and in
    /// <c>ContentMode="Annotated"</c> a control nobody marked is a control the editor will not offer.
    /// </remarks>
    private void OnDocumentChanged(object? sender, XamlLiveDocumentChangedEventArgs e)
    {
        if ((e.Changes & (XamlLiveDocumentChanges.Text | XamlLiveDocumentChanges.State)) != 0
            && Session is { } session)
        {
            Publish(session);
        }

        RaiseDocumentState();

        DocumentChanged?.Invoke(this, e);
    }

    /// <summary>Says that what is derived from the live document may have moved.</summary>
    private void RaiseDocumentState()
    {
        Problem = ProblemOf(Live);

        Raise(nameof(Document));
        Raise(nameof(Objects));
        Raise(nameof(IsDirty));
        Raise(nameof(Title));
        Raise(nameof(IsBehind));
        Raise(nameof(BehindReason));
    }

    /// <summary>What the canvas says instead of the form, when there is no form to show.</summary>
    /// <remarks>
    /// <c>Behind</c> is not a problem to show over the canvas: the form there is the last text that
    /// could be shown, and the person is usually halfway through typing the next one in the other
    /// editor. The bar above the canvas says it; the canvas keeps showing what it can.
    /// </remarks>
    private string? ProblemOf(XamlLiveDocument? live)
    {
        if (live is not { State: XamlLiveDocumentState.Broken })
        {
            return live?.Session is { RootObject: not Control } session
                ? $"The document's root is {session.RootObject.GetType().Name}, which is not a Control."
                : null;
        }

        return string.Join(
            "; ",
            live.Diagnostics
                .Where(static diagnostic => diagnostic.IsError)
                .Select(static diagnostic => diagnostic.Message)
                .DefaultIfEmpty("The document could not be shown, and nothing said why."));
    }

    /// <summary>Whether the document has edits the file does not have yet.</summary>
    public bool IsDirty => Live?.IsDirty == true;

    /// <summary>
    /// Whether the canvas shows an earlier text than the document's — the IDE saved the file halfway
    /// through a sentence, or named a type nothing has built yet.
    /// </summary>
    public bool IsBehind => Live?.State == XamlLiveDocumentState.Behind;

    /// <summary>What the bar above the canvas says while the form is behind its text.</summary>
    public string? BehindReason => Live is { State: XamlLiveDocumentState.Behind } live
        ? "Showing the last text that could be shown — "
            + (live.Diagnostics.FirstOrDefault(static diagnostic => diagnostic.IsError)?.Message
                ?? "the current one could not be, and nothing said why.")
        : null;

    /// <summary>Whether this is the form the canvas is showing.</summary>
    /// <remarks>
    /// Held here rather than worked out in the tab strip, because a tab has to answer it about itself
    /// and a template comparing its own item to a property of the window's data context needs a
    /// converter and a second binding to do what a boolean does.
    /// </remarks>
    public bool IsActive
    {
        get;
        internal set => Set(ref field, value);
    }

    /// <summary>
    /// Whether a control this form places has changed since the form was last built.
    /// </summary>
    /// <remarks>
    /// Population reaches instances made from now on, never ones already on screen — so a form
    /// showing yesterday's control is marked rather than rebuilt on the spot, and the rebuild
    /// happens when the form is next looked at. The form's own document is not stale; what it
    /// placed is.
    /// </remarks>
    internal bool IsStale { get; set; }

    /// <summary>What the tab and the canvas label show.</summary>
    public string Title => IsDirty ? Name + " •" : Name;

    /// <summary>The target mark the design floats above the form's card.</summary>
    public string Caption => "⌖ " + Name;

    /// <summary>And its size beside it, in the design's dim colour.</summary>
    public string SizeText => $"{Width:F0} × {Height:F0}";

    /// <summary>Why the form could not be shown, when it could not.</summary>
    public string? Problem
    {
        get;
        private set => Set(ref field, value);
    }

    internal XamlLoadSession? Session => Live?.Session;

    /// <summary>The assembly generation this form was loaded under.</summary>
    /// <remarks>
    /// The compilation scope travels with the environment, so nothing here enters it — this exists
    /// for lifetime: a superseded generation must stay alive until the last form loaded under it
    /// closes, and this is how the designer knows which forms those are.
    /// </remarks>
    internal ArxisStudio.ProjectSystem.Markup.Xaml.ProjectAssemblyContext? Assemblies { get; set; }

    /// <summary>
    /// Works out what to show from what the document produced.
    /// </summary>
    /// <remarks>
    /// Run again after every update, because an update that reaches far enough rebuilds the root —
    /// and because a rebuilt window arrives with its content back inside it.
    /// </remarks>
    private void Publish(XamlLoadSession session)
    {
        Root = session.RootObject as Control;

        // The root object, not the session: the card knows nothing about documents. Set again after
        // an update that rebuilt the root, and the card stays the same card — same place, same
        // selection.
        Card.Root = session.RootObject;

        MarkEditable(session);
        MarkGroups(session);

        Raise(nameof(Objects));
    }

    /// <summary>
    /// Tells the editor which controls are the document's own, and therefore editable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked of the object map rather than worked out from the tree, because the map is the one
    /// thing that knows. A walk would have to guess about the parts the card adds to host the form, about
    /// anything a template generated, and about whatever the designer adds next — and it would guess
    /// silently.
    /// </para>
    /// <para>
    /// But the map holds more than the document's own objects, and marking all of them offered a
    /// button's own label as something to select and resize — a rectangle inside the button, for
    /// which the inspector and the tree both answered "Button", because that is the nearest thing
    /// with an element behind it. After an edit, when the button's template has been realised, the
    /// map picks the label up too.
    /// </para>
    /// <para>
    /// The test is the round trip rather than the origin, and it is the stricter of the two. Asking
    /// for the element behind a control <em>and</em> checking that the element leads back to the
    /// same control admits only what the document declared: a label paired to nothing fails the
    /// first half, and anything paired to its owner's element fails the second. <c>GetOrigin</c>
    /// would do for the label — since the map stopped claiming the document's origin for objects it
    /// found no declaration for — but it answers a question about provenance, and this one is about
    /// editability, which is what an element is.
    /// </para>
    /// <para>
    /// The root is the one document object deliberately left unmarked, because the card already
    /// stands for it. Marking it too gave the form two frames that both said <c>UserControl</c> and
    /// both edited the same element, and the inner one was the worse of the two: it is a control
    /// inside a container, so its handles could not take the form past the card it sits in — they
    /// re-measured it against the card instead. The card is the frame whose size <em>is</em> the
    /// form's, and resizing it writes the root's <c>Width</c> and <c>Height</c>.
    /// </para>
    /// <para>
    /// Everything else keeps working through the map rather than through this flag: a drop still
    /// finds the root as a container, the inspector still edits the root's own properties, and
    /// selecting the root row still lands on the card — <c>SelectTarget</c> refuses an unmarked
    /// control and <c>ShowOnCanvas</c> falls through to the card, which is the answer it already gave
    /// for a window-rooted form.
    /// </para>
    /// </remarks>
    internal static void MarkEditable(XamlLoadSession session)
    {
        XamlObjectMap map = session.Objects;

        foreach (object produced in map.Objects)
        {
            if (ReferenceEquals(produced, session.RootObject))
            {
                continue;
            }

            if (produced is Control control
                && map.GetElement(control) is { } element
                && ReferenceEquals(map.GetObject(element), control))
            {
                Layout.SetIsTracked(control, true);
            }
        }
    }

    /// <summary>
    /// Puts the groups the document records back onto the live controls.
    /// </summary>
    /// <remarks>
    /// The mark lives in the design-time namespace so that a project that has never heard of the
    /// editor still builds — see <see cref="DesignerViewModel.WriteGroup"/> — and the runtime loader
    /// skips those attributes for exactly the same reason the compiler does. So nothing arrives on
    /// the control by being loaded, and this is where it arrives instead: after every load, from the
    /// document, which is the only place the group was ever written down.
    /// </remarks>
    internal static void MarkGroups(XamlLoadSession session)
    {
        XamlObjectMap map = session.Objects;

        var marked = new System.Collections.Generic.Dictionary<Control, string>();

        foreach (XamlElement element in map.MappedElements)
        {
            if (map.GetObject(element) is not Control control)
            {
                continue;
            }

            string? id = element.Attributes
                .Where(attribute =>
                    attribute.Name.LocalName == "DesignGroup"
                    && element.NamespaceContext.LookupNamespace(attribute.Name.Prefix)
                        == XamlNamespaces.Design)
                .Select(attribute => attribute.GetValueText())
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(id))
            {
                marked[control] = id;
            }
        }

        foreach ((Control control, string id) in marked)
        {
            // A container marked with the same group as something inside it is not restored. Such a
            // file can be written — a marquee takes a panel along with what it holds, and every
            // control here is selectable in its own right — and the result is a form nothing inside
            // the panel can be clicked in: the click expands to the cluster, the cluster holds the
            // panel, and the panel is what gets selected. Leaving the ancestor out gives the file
            // back its contents, and the next ungroup takes the stale mark out of the document.
            if (Holds(control, marked, id))
            {
                continue;
            }

            ArxisStudio.Surface.UiDesigner.SurfaceGroup.SetId(control, id);
        }

        static bool Holds(
            Control control, System.Collections.Generic.Dictionary<Control, string> marked, string id)
        {
            foreach ((Control other, string mark) in marked)
            {
                if (ReferenceEquals(other, control) || mark != id)
                {
                    continue;
                }

                for (Control? above = other.Parent as Control; above is not null;
                    above = above.Parent as Control)
                {
                    if (ReferenceEquals(above, control))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    internal void Fail(string problem)
    {
        Problem = problem;
        Root = null;
    }

    /// <summary>Closes a window a session built, which the platform would otherwise keep.</summary>
    private static void CloseQuietly(Window window)
    {
        try
        {
            window.Close();
        }
        catch (Exception error) when (error is InvalidOperationException or NullReferenceException)
        {
            // A window that will not close is a window the platform is still holding, and the proof
            // a swap asks for is what turns that into a restart rather than a guess.
        }
    }

    /// <summary>Closes the form for good, the document with it.</summary>
    /// <remarks>
    /// A form handed on by <see cref="RememberAsync"/> holds no document by now, and disposes nothing
    /// but its card. The root a disposed session built is the host's to take down, so a window is
    /// closed here as it is on any other replacement.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        object? root = Card.Root ?? Session?.RootObject;

        Card.Root = null;
        Root = null;

        if (Live is { } live)
        {
            Let(live);

            await live.DisposeAsync();
        }

        if (root is Window window)
        {
            CloseQuietly(window);
        }
    }
}
