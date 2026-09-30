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
    public XamlDocument? Document
    {
        get;
        private set => Set(ref field, value);
    }

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

    /// <summary>
    /// What the document was before each edit, and what it was before each undo.
    /// </summary>
    /// <remarks>
    /// Documents rather than a list of what changed, because a document already is one: every edit
    /// produces a whole new immutable one, so remembering the previous is remembering everything,
    /// and going back is applying it the same way any other change is applied. An editor that
    /// recorded inverse operations would have to be right about each of them; this cannot be wrong
    /// about what the file said, because it is what the file said.
    /// </remarks>
    private readonly System.Collections.Generic.Stack<XamlDocument> _undo = new();

    private readonly System.Collections.Generic.Stack<XamlDocument> _redo = new();

    /// <summary>The document as the file has it, for telling an edited form from a returned one.</summary>
    private XamlDocument? _saved;

    internal bool CanUndo => _undo.Count > 0;

    internal bool CanRedo => _redo.Count > 0;

    /// <summary>Records where an edit started from. A new edit is the end of any redo path.</summary>
    internal void Remember(XamlDocument before)
    {
        _undo.Push(before);
        _redo.Clear();
    }

    /// <summary>The document to go back to, if there is one.</summary>
    internal XamlDocument? StepBack(XamlDocument current)
    {
        if (_undo.Count == 0)
        {
            return null;
        }

        _redo.Push(current);

        return _undo.Pop();
    }

    /// <summary>And the one to come forward to.</summary>
    internal XamlDocument? StepForward(XamlDocument current)
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        _undo.Push(current);

        return _redo.Pop();
    }

    /// <summary>
    /// Everything about a form that outlives the objects built from a generation of types.
    /// </summary>
    /// <remarks>
    /// A swap cannot keep the form object: measurement showed that a form left open — even taken
    /// off the canvas — keeps the generation it was built under in the process, and a successor
    /// created beside it would be a second copy of every type. So the form is remembered, closed,
    /// and built again on the far side, which is what makes a swap something other than a reopen:
    /// the text, the history and the place on the canvas are all here.
    /// </remarks>
    internal sealed record FormMemory(
        CanonicalPath File,
        Point Location,
        double Width,
        double Height,
        XamlDocument? Document,
        XamlDocument[] Undone,
        XamlDocument[] Redone,
        XamlDocument? Saved);

    /// <summary>Remembers what a swap must carry across.</summary>
    internal FormMemory Remember() =>
        new(File, Location, Width, Height, Document, [.. _undo], [.. _redo], _saved);

    /// <summary>Puts a remembered form back, before its session is built again.</summary>
    /// <remarks>
    /// The stacks are pushed back in reverse, because a stack enumerates from its top and this
    /// has to end where the other began — an undo history restored upside down is worse than none.
    /// </remarks>
    internal void Recall(FormMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);

        Location = memory.Location;
        Width = memory.Width;
        Height = memory.Height;
        Document = memory.Document;

        _undo.Clear();
        _redo.Clear();

        for (int index = memory.Undone.Length - 1; index >= 0; index--)
        {
            _undo.Push(memory.Undone[index]);
        }

        for (int index = memory.Redone.Length - 1; index >= 0; index--)
        {
            _redo.Push(memory.Redone[index]);
        }

        _saved = memory.Saved;

        IsDirty = !ReferenceEquals(Document, _saved);
    }

    /// <summary>A form freshly loaded or freshly saved has nothing behind it and nothing pending.</summary>
    internal void MarkSaved()
    {
        _saved = Document;

        IsDirty = false;
    }

    /// <summary>Whether the document differs from what the file holds, which an undo can undo.</summary>
    internal void Restated() => IsDirty = !ReferenceEquals(Document, _saved);

    /// <summary>Whether the document has edits the file does not have yet.</summary>
    public bool IsDirty
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                Raise(nameof(Title));
            }
        }
    }

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

    internal XamlLoadSession? Session { get; private set; }

    /// <summary>The assembly generation this form was loaded under.</summary>
    /// <remarks>
    /// The compilation scope travels with the environment, so nothing here enters it — this exists
    /// for lifetime: a superseded generation must stay alive until the last form loaded under it
    /// closes, and this is how the designer knows which forms those are.
    /// </remarks>
    internal ArxisStudio.ProjectSystem.Markup.Xaml.ProjectAssemblyContext? Assemblies { get; set; }

    /// <summary>
    /// Puts a freshly loaded session in place of whatever was there.
    /// </summary>
    /// <remarks>
    /// The old session is disposed after the new root is published rather than before, so the canvas
    /// never has a moment with nothing in it — and because disposing a session tears down objects the
    /// visual tree may still be walking.
    /// </remarks>
    internal async ValueTask AdoptAsync(XamlLoadSession session)
    {
        XamlLoadSession? previous = Session;

        Session = session;
        Document = session.Document;

        Publish(session);

        Problem = Root is null
            ? $"The document's root is {session.RootObject.GetType().Name}, which is not a Control."
            : null;

        // A form that has just been loaded is the file, and has no history behind it.
        _undo.Clear();
        _redo.Clear();

        MarkSaved();

        if (previous is not null)
        {
            await previous.DisposeAsync();
        }
    }

    /// <summary>
    /// Republishes the root after an update replaced it.
    /// </summary>
    /// <remarks>
    /// An update that reaches far enough rebuilds the root object rather than patching it, and the
    /// canvas is holding the previous one until it is told otherwise.
    /// </remarks>
    internal void AdoptRoot(XamlLoadSession session) => Publish(session);

    /// <summary>
    /// Lets go of the session and everything built from it, keeping the document and the history.
    /// </summary>
    /// <remarks>
    /// The first half of moving a form to a new generation. Everything the old assemblies produced
    /// is released — the surface gives back what it borrowed, the root is dropped, the session is
    /// disposed — so the collectible context that made them can actually collect. The document is
    /// text and owes the assemblies nothing, which is why it and the undo history stay.
    /// </remarks>
    internal async ValueTask RetireSessionAsync()
    {
        if (Session is not { } session)
        {
            return;
        }

        Session = null;

        Card.Root = null;
        Root = null;

        // And the generation itself, or the sweep would find this form still using it and keep it
        // alive — which is the whole thing this retirement exists to end.
        Assemblies = null;

        await session.DisposeAsync();
    }

    /// <summary>
    /// Adopts a session created over the same document under a new generation.
    /// </summary>
    /// <remarks>
    /// The other half. Unlike <see cref="AdoptAsync"/> this is not an opening: the form was already
    /// open, its history is real, and its unsaved edits are in the document the new session was
    /// built from — so nothing is cleared and nothing is marked saved. The elements even survive,
    /// because the session keeps the document instance it was given.
    /// </remarks>
    internal void Migrate(XamlLoadSession session)
    {
        Session = session;
        Document = session.Document;

        Publish(session);

        Problem = Root is null
            ? $"The document's root is {session.RootObject.GetType().Name}, which is not a Control."
            : null;
    }

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

    /// <summary>Replaces the document without rebuilding the live tree.</summary>
    /// <remarks>
    /// Used after an edit the session applied itself: the session already holds the new document and
    /// the live objects to match, so re-reading either would be undoing work that is already correct.
    /// </remarks>
    internal void Adopt(XamlDocument document)
    {
        Document = document;
        Raise(nameof(Objects));
    }

    public async ValueTask DisposeAsync()
    {
        if (Session is { } session)
        {
            Session = null;

            await session.DisposeAsync();
        }

        Card.Root = null;

        Root = null;
    }
}
