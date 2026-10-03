using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Surface.UiDesigner;
using Avalonia.Controls;

namespace UiDesigner.Demo.ViewModels;

public sealed partial class DesignerViewModel
{
    /// <summary>The form the panels are about.</summary>
    public FormViewModel? ActiveForm
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                ShowOnlyTheActiveForm();

                // The selection this form had when it was last looked at, found again in its
                // document as it reads now — never the element another form had selected.
                Selected = value?.SelectedPath is { } path && value.Document is { } shown
                    ? path.Resolve(shown) ?? path.Parent?.Resolve(shown)
                    : null;

                RebuildHierarchy();
                Raise(nameof(CanvasCaption));
                Raise(nameof(CanvasSize));
                Raise(nameof(CanvasSizeAndZoom));
                Raise(nameof(TargetName));
                RefreshAllCommands();

                if (value is not null)
                {
                    ShowTheActiveDocument();
                }

                RefreshProjectToolboxForTheFormInFront();
            }
        }
    }

    /// <summary>
    /// Tells the design host which document is looked at, and brings it up to date.
    /// </summary>
    /// <remarks>
    /// The canvas shows one form, so a swap of the types attaches its document and leaves the rest
    /// detached; a form shown again catches up then — attached to the types that are live, and rebuilt
    /// if a control it places changed while it was hidden. Work nobody could see is not done.
    /// </remarks>
    private void ShowTheActiveDocument()
    {
        if (_host is not { } host || ActiveForm?.Live is not { } live || !host.Documents.Contains(live))
        {
            return;
        }

        host.SetVisibleDocuments([live]);

        RunDetached(() => host.EnsureLiveAsync(live, _shutdown.Token).AsTask());
    }

    /// <summary>
    /// The forms the canvas is showing, which is the active one and nothing else.
    /// </summary>
    /// <remarks>
    /// A tab is a document you are working on, and a canvas holding every open document at once is a
    /// pile rather than a workspace: opening a second form put it beside the first, both editable,
    /// with the inspector and the hierarchy describing whichever was last touched. One at a time is
    /// what the tabs above the canvas have always promised.
    /// <para>
    /// A collection rather than a single item because the editor is an items control — the card, its
    /// selection and its adorners are the container's, and a container is what an item gets.
    /// </para>
    /// </remarks>
    public ObservableCollection<FormViewModel> Shown { get; } = [];

    private void ShowOnlyTheActiveForm()
    {
        Shown.Clear();

        foreach (FormViewModel open in Forms)
        {
            open.IsActive = ReferenceEquals(open, ActiveForm);
        }

        if (ActiveForm is { } form)
        {
            Shown.Add(form);
        }
    }

    /// <summary>The control the inspector is about, as an element of the document.</summary>
    /// <remarks>
    /// An element rather than a live object, because the document is what gets edited. The live
    /// object is how the user pointed at it and is of no further interest once the pointing is done.
    /// </remarks>
    /// <summary>The control the current selection was made through, when it was made on the canvas.</summary>
    private Control? _selectedControl;

    public XamlElement? Selected
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                // Remembered on the form as a position, which is what survives the next edit.
                if (ActiveForm is { } form)
                {
                    form.SelectedPath = value is { IsPropertyElementSyntax: false }
                        ? XamlElementPath.Of(value)
                        : null;
                }

                Raise(nameof(SelectedName));
                BuildInspector();
                SyncHierarchySelection();
                ShowBreadcrumb();
                RefreshAllCommands();
            }
        }
    }

    public string SelectedName => Selected is null
        ? "nothing selected"
        : Selected.Name.ToString() + (Selected.GetDirective("Name") is { Length: > 0 } name
            ? $"  “{name}”"
            : string.Empty);

    /// <summary>
    /// Follows a click on the canvas back to the element that produced the control.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The object map runs both ways, and this is the direction that makes a designer feel like one:
    /// a person points at a button on screen and the tool knows which line of the file drew it.
    /// Templates and styles produce controls no element made — a button's own border, the text
    /// inside it — so the walk goes up through the parents until it finds one that is mapped, which
    /// is how clicking a button's label selects the button.
    /// </para>
    /// <para>
    /// The walk ends at the document's root rather than at nothing. Not every control on screen has
    /// a mapped ancestor — a part a template produced inside another part need not — and selecting
    /// the form is a better answer than selecting nothing, because the form is a thing worth
    /// selecting: its title and its size are edited there.
    /// </para>
    /// </remarks>
    public void SelectFromCanvas(FormViewModel form, Control? control)
    {
        if (_syncingCanvas)
        {
            return;
        }

        ActiveForm = form;

        // Remembered because an element does not survive an edit and a control does. Elements are
        // immutable syntax nodes: applying a change produces a new document with new ones, and the
        // one the inspector is holding goes on describing the form as it was. The control is the
        // handle that still means something afterwards.
        _selectedControl = control;

        if (form.Objects is not { } map)
        {
            Selected = null;

            return;
        }

        // The nearest control the map knows. A project's own control placed on this form is the
        // form's element, and what its own markup built inside it belongs to no element here — so a
        // click inside it lands on the control, the same way a template's parts land on their owner.
        for (Control? current = control; current is not null; current = current.Parent as Control)
        {
            if (map.GetElement(current) is { } element)
            {
                Selected = element;

                return;
            }
        }

        // The card that stands for a window-rooted form has no element of its own, and should not:
        // it is what the root would be if the root could be shown. Falling back to the document's
        // root is therefore the right answer here rather than a shrug.
        Selected = form.Document?.Root;
    }

    /// <summary>Finds the live control an element produced, for the editor to select.</summary>
    public static Control? ControlFor(FormViewModel form, XamlElement element) =>
        form.Objects?.GetObject(element) as Control;

    /// <summary>
    /// Records an edit in the form's document, which shows it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One route for every change, which is what keeps the canvas incapable of disagreeing with the
    /// file. The edit lands in the text and the form's history first, and the session works out what
    /// that means for the objects already on screen — only what changed is rebuilt, and a change it
    /// cannot follow builds the form again from the text (Markup's <see cref="XamlLiveDocument"/>).
    /// What follows on screen — the tree, the inspector, the selection put back by path — is
    /// <see cref="OnFormDocumentChanged"/>, which every route a change takes ends in: an edit here,
    /// an undo, a file written by the IDE.
    /// </para>
    /// <para>
    /// The edit is kept whatever the objects make of it, because it is real either way: a change the
    /// live tree could not follow is still a change to the file. The form then shows the last text
    /// it could, and says why.
    /// </para>
    /// <para>
    /// The callback runs inside the document's turn. An element found before an earlier edit landed
    /// belongs to the text that edit replaced, and the editor refuses it rather than writing where it
    /// no longer is — nothing is recorded, and the console says so.
    /// </para>
    /// </remarks>
    private async Task ApplyAsync(FormViewModel form, Action<XamlDocumentEditor> edit, string what)
    {
        if (form.Live is not { } live)
        {
            return;
        }

        XamlLiveEditResult result;

        try
        {
            result = await live.EditAsync(edit, what, _shutdown.Token);
        }
        catch (InvalidOperationException refused)
        {
            Log($"  ! {what} was not recorded: {refused.Message}");

            return;
        }

        if (result.TextChanged && result.State != XamlLiveDocumentState.Live)
        {
            Log($"  {what}: the form shows the text before it — {FirstError(result.Diagnostics)}");
        }
    }

    /// <summary>The first error a load or an update reported, for a line in the console.</summary>
    private static string FirstError(IEnumerable<MarkupDiagnostic> diagnostics) =>
        diagnostics.FirstOrDefault(static diagnostic => diagnostic.IsError)?.Message
            ?? "no diagnostic said why";

    /// <summary>
    /// Brings what the designer shows in line with a form whose document moved, whatever moved it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Raised on the UI thread once the change is over and the session has caught up, so the map,
    /// the root and the text describe one document. What placed copies of this control are drawn
    /// from follows the edit by itself — the design host registers the document and rebuilds the
    /// forms that place it — and unsaved is the point: the other tab shows the control as it is here,
    /// not as the file last had it.
    /// </para>
    /// <para>
    /// The panels follow only the active form. A form in a background tab keeps its selection as a
    /// path and finds it again when it is shown — following every form put the inspector into the
    /// document of a tab the IDE had just saved.
    /// </para>
    /// <para>
    /// And the card follows the document, always: an undone resize rolls the document back, the
    /// design sizes are applied to the live window again, and a card left at the size from before
    /// the undo was a form overflowing its own frame the moment somebody edited a Title.
    /// </para>
    /// </remarks>
    private void OnFormDocumentChanged(object? sender, XamlLiveDocumentChangedEventArgs e)
    {
        if (sender is not FormViewModel form)
        {
            return;
        }

        if ((e.Changes & XamlLiveDocumentChanges.State) != 0)
        {
            ReportState(form);
        }

        if (ReferenceEquals(form, ActiveForm)
            && (e.Changes & (XamlLiveDocumentChanges.Text | XamlLiveDocumentChanges.State | XamlLiveDocumentChanges.Uri)) != 0)
        {
            SizeToContent(form);
            RebuildHierarchy();

            if (form.Document is { } shown)
            {
                Reselect(
                    form,
                    form.SelectedPath is { } path ? path.Resolve(shown) ?? path.Parent?.Resolve(shown) : null);
            }
        }

        RefreshAllCommands();
    }

    /// <summary>Says in the console what the form shows of its text, when that changed.</summary>
    /// <remarks>
    /// With every diagnostic the attempt found, not only the first: a form behind its text is waiting
    /// for its author, and the author is in the other editor, reading this console for line numbers.
    /// A form that is shown again takes its rows out of the list.
    /// </remarks>
    private void ReportState(FormViewModel form)
    {
        if (form.Live is not { } live)
        {
            return;
        }

        switch (live.State)
        {
            case XamlLiveDocumentState.Behind:
                Log($"  {form.Name} shows the text before the last change:");
                ShowMarkupDiagnostics(live.Diagnostics, live.Document.SourceText.ToString(), form.File);
                break;

            case XamlLiveDocumentState.Broken:
                Log($"  ! {form.Name} cannot be shown:");
                ShowMarkupDiagnostics(live.Diagnostics, live.Document.SourceText.ToString(), form.File);
                break;

            default:
                // Shown, with whatever warnings the load had — said once, when they change.
                ShowMarkupDiagnostics(live.Diagnostics, live.Document.SourceText.ToString(), form.File);
                break;
        }
    }

    /// <summary>
    /// Writes a control's geometry into the document after the editor has moved or resized it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Which attributes to write is a question about the parent, not about the control: inside a
    /// <c>Canvas</c> a position is <c>Canvas.Left</c> and <c>Canvas.Top</c>, and inside a
    /// <c>StackPanel</c> there is no position to write at all — the panel decides, and writing a
    /// margin to fake one would produce a file whose meaning changes the moment somebody adds a
    /// sibling.
    /// </para>
    /// <para>
    /// So a move in a flow layout is refused rather than approximated. The editor already declines
    /// to start such a gesture when nothing answers its reorder request, and this is the other half
    /// of the same honesty.
    /// </para>
    /// </remarks>
    private async Task WriteGeometryAsync(
        FormViewModel form, XamlElement element, Control control, bool moved, bool resized)
    {
        Control? parent = control.Parent as Control;

        bool positioned = parent is Canvas or null;

        await ApplyAsync(
            form,
            editor =>
            {
                if (moved && positioned && parent is Canvas)
                {
                    Set(editor, element, "Canvas.Left", Canvas.GetLeft(control));
                    Set(editor, element, "Canvas.Top", Canvas.GetTop(control));
                }

                if (resized)
                {
                    WriteSize(editor, element, "Width", control.Bounds.Width);
                    WriteSize(editor, element, "Height", control.Bounds.Height);
                }
            },
            "geometry");

        if (moved && !positioned)
        {
            Log($"  {element.Name} sits in a {parent?.GetType().Name}, which owns its position — "
                + "nothing written");
        }

        static void Set(XamlDocumentEditor editor, XamlElement element, string name, double value)
        {
            if (double.IsFinite(value))
            {
                editor.SetAttribute(
                    element,
                    XamlQualifiedName.Parse(name),
                    Math.Round(value).ToString(CultureInfo.InvariantCulture));
            }
        }

        // A size goes to the attribute that decides what the designer shows. Every template writes
        // its windows with d:DesignWidth and d:DesignHeight and no Width at all — and in design
        // mode those win over Width, applied again on every update. So a resize that wrote Width
        // put the new number in the document and the card while the live window snapped back to
        // the design size the moment the update landed: three panels, two answers. When the
        // element states a design size, the design size is what a resize updates; the plain
        // attribute is written too when the document already had one, so the file never carries
        // two different numbers for one fact.
        static void WriteSize(
            XamlDocumentEditor editor, XamlElement element, string name, double value)
        {
            if (!double.IsFinite(value))
            {
                return;
            }

            string text = Math.Round(value).ToString(CultureInfo.InvariantCulture);

            XamlAttribute? design = DesignSize(element, name);

            if (design is not null)
            {
                editor.SetAttribute(element, design.Name, text);
            }

            if (design is null || element.GetAttribute(name) is not null)
            {
                editor.SetAttribute(element, XamlQualifiedName.Parse(name), text);
            }
        }
    }

    /// <summary>
    /// The design-namespace attribute that stands in for a size the element does not write plainly.
    /// </summary>
    /// <remarks>
    /// One lookup for every door a size can be edited through. The drag-resize learned the hard way
    /// that <c>d:DesignWidth</c> is what the designer actually shows; an inspector row that wrote
    /// plain <c>Width</c> on such a root would be the same bug reached through the other door.
    /// </remarks>
    private static XamlAttribute? DesignSize(XamlElement element, string name)
    {
        if (name is not ("Width" or "Height"))
        {
            return null;
        }

        string designName = "Design" + name;

        return element.Attributes.FirstOrDefault(attribute =>
            attribute.Name.LocalName == designName
            && element.NamespaceContext.LookupNamespace(attribute.Name.Prefix)
                == XamlNamespaces.Design);
    }

    /// <summary>
    /// Writes a finished gesture into the document. Called by the view.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A gesture on the card is a gesture on the form. The card is the container and the stand-in
    /// for a root which cannot be shown, both at once; it has no element of its own, nor should —
    /// it is what the root is when the root is on screen. So it resolves to the document's root, and
    /// resizing the card resizes the form.
    /// </para>
    /// <para>
    /// Without that they resolved to nothing and the write was skipped in silence: the caption read
    /// the new size off the card while the document went on saying the old one, and the two
    /// disagreed with nothing to say so. A control that genuinely has nothing behind it says so now.
    /// </para>
    /// <para>
    /// Only the size, though. Where a card sits on the canvas is the designer's business and not the
    /// document's — a form does not have a position, the thing showing it does.
    /// </para>
    /// </remarks>
    public void WriteGeometry(FormViewModel form, Control control, bool moved, bool resized)
    {
        if (form.Objects?.GetElement(control) is { } element)
        {
            RunDetached(() => WriteGeometryAsync(form, element, control, moved, resized));

            return;
        }

        if (StandsForTheForm(form, control))
        {
            if (resized && form.Document?.Root is { } root)
            {
                RunDetached(() => WriteGeometryAsync(form, root, control, moved: false, resized: true));
            }

            return;
        }

        Log($"! {control.GetType().Name} has nothing in the document behind it — geometry not written");
    }

    /// <summary>Whether this control is the form as it appears, rather than something inside it.</summary>
    private static bool StandsForTheForm(FormViewModel form, Control control) =>
        ReferenceEquals(control, form.Card);

    /// <summary>
    /// Answers the editor's reorder request by moving the element in the document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Subscribing is what makes the gesture exist.</b> The editor reads the control tree and
    /// never writes to it, so dragging a control within a flow layout does nothing at all until
    /// something answers — it does not even draw the insertion point. This is that something, and
    /// what it does is edit the document, which is where the order actually lives.
    /// </para>
    /// <para>
    /// The anchor is used rather than the index. The editor's index counts a panel's children and
    /// the document's counts its content elements, and the two disagree the moment a parent holds a
    /// property element — which a Grid with its row definitions does. The neighbour the control goes
    /// before survives that difference, which is why the request carries one.
    /// </para>
    /// </remarks>
    public void ReorderFromCanvas(FormViewModel form, Control target, Control? anchor)
    {
        if (form.Objects is not { } map
            || map.GetElement(target) is not { } element
            || element.Parent is not XamlElement parent)
        {
            return;
        }

        int index = anchor is not null && map.GetElement(anchor) is { } before
            ? parent.ContentElements.ToList().IndexOf(before)
            : parent.ContentElements.Count();

        if (index < 0)
        {
            return;
        }

        RunDetached(() => ApplyAsync(
            form,
            editor => editor.MoveElement(element, parent, index),
            $"move {element.Name}"));
    }

    /// <summary>
    /// Puts the selection back on an element after the document behind it has been replaced.
    /// </summary>
    /// <remarks>
    /// Both halves, because the canvas holds a control and the inspector holds an element, and after
    /// an update the control the canvas was holding may not exist. The canvas is asked through the
    /// same event the tree uses, which is the one path that knows how to select a form's root.
    /// </remarks>
    private void Reselect(FormViewModel form, XamlElement? element)
    {
        Selected = element;

        if (element is null)
        {
            _selectedControl = null;

            return;
        }

        _selectedControl = ControlFor(form, element);

        // The canvas is asked to follow, and its answer is not listened to. Selecting a control
        // makes the editor report a selection, that report comes back through SelectFromCanvas, and
        // its walk answers with the nearest thing it considers a target — which, for a control the
        // editor has only just been given, is the panel above it. The element resolved here is the
        // better answer and this is where it was resolved, so the echo is ignored. The tree is kept
        // in step the same way, by the same kind of flag.
        using (SyncingCanvas())
        {
            CanvasSelectionRequested?.Invoke(this, element);
        }
    }

    /// <summary>
    /// Marks the canvas's selection as this designer's own doing until disposed.
    /// </summary>
    /// <remarks>
    /// A scope rather than a flag set in place, because the canvas cannot always answer at once: a
    /// control the document has only just produced has no bounds until the layout has run, and the
    /// editor will not report a target it cannot draw a frame around. The view therefore asks
    /// again after a layout pass, and that second ask is the same designer's doing as the first —
    /// so the guard has to be something the view can hold across the wait.
    /// </remarks>
    internal IDisposable SyncingCanvas()
    {
        _syncingCanvas = true;

        return new CanvasSync(this);
    }

    private sealed class CanvasSync(DesignerViewModel designer) : IDisposable
    {
        public void Dispose() => designer._syncingCanvas = false;
    }

    /// <summary>Whether the canvas's selection is currently this designer's own doing.</summary>
    private bool _syncingCanvas;

    /// <summary>Goes back one edit, and forward again.</summary>
    /// <remarks>
    /// The form's own history, kept by its live document: every step a person took here, and every
    /// time the IDE wrote the file under the form, which is a step that can be taken back too. Both go
    /// the way every change goes, so an undone insert takes its control off the canvas, out of the
    /// tree and out of the inspector exactly the way deleting it would.
    /// </remarks>
    private void StepHistory(bool back)
    {
        if (ActiveForm is not { Live: { } live })
        {
            return;
        }

        RunDetached(async () =>
        {
            XamlLiveEditResult result = back
                ? await live.UndoAsync(_shutdown.Token)
                : await live.RedoAsync(_shutdown.Token);

            if (result.TextChanged)
            {
                Log(back ? "Undone." : "Redone.");
            }
        });
    }

    /// <summary>Answers the editor's delete request. Called by the view.</summary>
    public void DeleteFromCanvas(FormViewModel form, Control control)
    {
        if (form.Objects?.GetElement(control) is { } element)
        {
            RunDetached(() => DeleteAsync(form, element));
        }
    }

    /// <summary>Removes the selected element, which is a structural edit and therefore Markup's.</summary>
    /// <remarks>
    /// A control of the project's own is removed like any other: the session pairs it with the
    /// element that places it, whatever its own markup built inside it, so the update takes it out
    /// where it stands.
    /// </remarks>
    private async Task DeleteAsync(FormViewModel form, XamlElement element)
    {
        await ApplyAsync(form, editor => editor.RemoveElement(element), "delete");

        Selected = null;

        // And the canvas too. Clearing the inspector left the editor holding the control that has
        // just been removed, so its frame stayed on screen over the space where the control had
        // been, until a click elsewhere replaced the selection.
        CanvasSelectionCleared?.Invoke(this, EventArgs.Empty);

        Log($"  removed {element.Name}");
    }

    /// <summary>Writes the document back to its file.</summary>
    /// <remarks>
    /// The whole text, from the document, rather than the text changes — the changes are how the
    /// editor got here and the document is what it means. A form saved this way keeps every scrap of
    /// formatting the user wrote, because the document was only ever edited where it was edited.
    /// </remarks>
    private async Task SaveAsync() => await SaveAsync(ActiveForm);

    /// <summary>Writes one form back to its file.</summary>
    private async Task SaveAsync(FormViewModel? which)
    {
        if (which is not { Live: { } live } form)
        {
            return;
        }

        // The text read once, written, and named as what was written: an edit that lands in between
        // is not in the file, and the form goes on saying so.
        SourceText written = live.Document.SourceText;

        await System.IO.File.WriteAllTextAsync(form.File.Value, written.ToString(), _shutdown.Token);
        await live.MarkSavedAsync(written, _shutdown.Token);

        form.PendingDiskText = null;

        Log($"Saved {form.Name}");

        RefreshAllCommands();
    }
}
