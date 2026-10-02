using System;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Surface;
using ArxisStudio.Surface.UiDesigner;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiDesigner.Demo.ViewModels;

namespace UiDesigner.Demo.Views;

/// <summary>
/// Where the editor's gestures become document edits.
/// </summary>
/// <remarks>
/// This is the only file that knows both a pointer and a file. The view model above it edits
/// documents and knows nothing about drag thresholds; the editor below it reports gestures and knows
/// nothing about XAML. Keeping the join in one place is what stops either of them growing a little
/// knowledge of the other.
/// </remarks>
public sealed partial class MainWindow : Window
{
    private ToolboxEntry? _dragging;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        Activated += (_, _) => (DataContext as DesignerViewModel)?.OnStudioActivated();

        // Every pointer and key, heard on the way down and whether or not something took it: what
        // "idle" means for a restart the designer makes by itself.
        AddHandler(PointerMovedEvent, (_, _) => Designer?.NoteInput(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, (_, _) => Designer?.NoteInput(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, (_, _) => Designer?.NoteInput(), RoutingStrategies.Tunnel, handledEventsToo: true);

        UiDesignerView surface = this.GetControl<UiDesignerView>("Surface");
        AvaloniaEdit.TextEditor markup = this.GetControl<AvaloniaEdit.TextEditor>("XamlView");

        // The editor's own XML rules, which is what "colour a tag differently from its attributes"
        // means without this file inventing a second definition of XAML.
        markup.SyntaxHighlighting =
            AvaloniaEdit.Highlighting.HighlightingManager.Instance.GetDefinition("XML");

        // Painted once the window is up, not here: a window that is not attached yet finds none of
        // the application's resources, so every colour asked for in a constructor comes back unset.
        Opened += (_, _) => PaintMarkup(markup);

        // And again when the palette changes, because the colours come out of it.
        ActualThemeVariantChanged += (_, _) => PaintMarkup(markup);
        ItemsControl toolbox = this.GetControl<ItemsControl>("ToolboxList");

        // Enter on a file tile, heard on the way down: the list takes Enter for itself and marks
        // it handled, so a handler written on the list in markup never hears it.
        this.GetControl<ListBox>("FileTiles").AddHandler(KeyDownEvent, OnProjectFileKeyDown, RoutingStrategies.Tunnel);

        // The channel that answers "what does a live host get from the editor's public API" in
        // numbers rather than in a screenshot. Nothing starts without --automation.
        Automation.AutomationChannel.TryStart(Program.AutomationDirectory, surface, this);

        surface.SurfaceSelectionChanged += OnSurfaceSelectionChanged;
        surface.EditCompleted += OnEditCompleted;
        surface.DeleteRequested += OnDeleteRequested;
        surface.ContextMenuRequesting += OnContextMenuRequesting;

        // A right press selects the row under the pointer before its menu opens, or the menu would
        // act on whatever happened to be selected from before.
        this.GetControl<ListBox>("HierarchyTree").AddHandler(
            PointerPressedEvent, OnTreePressed, RoutingStrategies.Tunnel);
        surface.ReorderRequested += OnReorderRequested;

        // The set of guides is the designer's, so the editor asks rather than changes it. Until
        // this answers, a line dragged off a ruler is only a preview — the same division the
        // control tree is under, and the reason dragging one anywhere does nothing without it.
        surface.GuideChangeRequested += (_, e) =>
            e.Handled = Designer?.ApplyGuideChange(e.Kind, e.Guide, e.Original) == true;

        // History belongs to whoever keeps it, and here that is the document: the editor knows a
        // gesture finished, not what a step means in XAML. So it asks, and answering is what makes
        // Ctrl+Z work while the pointer is over the canvas — the window's own key bindings only see
        // what the focused control did not take. Unanswered, the request bubbles and they still do.
        surface.UndoRequested += (_, e) => e.Handled = Run(Designer?.UndoCommand);
        surface.RedoRequested += (_, e) => e.Handled = Run(Designer?.RedoCommand);

        static bool Run(RelayCommand? command)
        {
            if (command is null || !command.CanExecute(null))
            {
                return false;
            }

            command.Execute(null);

            return true;
        }

        // Drag out of the toolbox, drop onto the surface. Avalonia's own drag-and-drop rather than a
        // hand-rolled pointer dance, so the cursor, the escape key and the drop feedback are the
        // platform's and behave the way every other application does.
        toolbox.AddHandler(PointerPressedEvent, OnToolboxPressed, RoutingStrategies.Tunnel);

        DragDrop.SetAllowDrop(surface, true);

        surface.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        surface.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        surface.AddHandler(DragDrop.DropEvent, OnDrop);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is DesignerViewModel designer)
            {
                // The pane follows the document rather than binding to it: TextEditor keeps its text
                // in a document of its own, and assigning only what changed keeps the caret and the
                // scroll position where the reader left them.
                ShowMarkup(markup, designer.DocumentText);

                designer.PropertyChanged += (_, changed) =>
                {
                    if (changed.PropertyName == nameof(DesignerViewModel.DocumentText))
                    {
                        ShowMarkup(markup, designer.DocumentText);
                    }
                };

                designer.PickEntryPoint = PickEntryPointAsync;
                designer.AskForNewForm = AskForNewFormAsync;
                designer.AskToConfirm = AskToConfirmAsync;
                designer.AskToSave = AskToSaveAsync;

                // Whether the window is in front decides one thing: when the designer restarts by
                // itself, after the project's types would not leave. Swapping them waits for nothing
                // but what the designer is in the middle of.
                designer.IsStudioActive = () => IsActive;

                // The canvas holds its last frame while the types are replaced, so the person sees the
                // forms rather than an empty surface.
                designer.FreezeCanvas = surface.Freeze;

                // A gesture holds the pointer and a swap would take the forms off the canvas under it:
                // held off for as long as the gesture lasts.
                IDisposable? gesture = null;

                surface.PropertyChanged += (_, changed) =>
                {
                    if (changed.Property != ArxisStudio.Surface.SurfaceView.IsInteractingProperty)
                    {
                        return;
                    }

                    if (surface.IsInteracting)
                    {
                        gesture ??= designer.Defer("a gesture on the canvas");
                    }
                    else
                    {
                        gesture?.Dispose();
                        gesture = null;
                    }
                };

                // And a value half typed in the inspector, for as long as the inspector has the keyboard.
                IDisposable? typing = null;
                Border inspector = this.GetControl<Border>("InspectorPanel");

                inspector.PropertyChanged += (_, changed) =>
                {
                    if (changed.Property != IsKeyboardFocusWithinProperty)
                    {
                        return;
                    }

                    if (inspector.IsKeyboardFocusWithin)
                    {
                        typing ??= designer.Defer("a value being typed in the inspector");
                    }
                    else
                    {
                        typing?.Dispose();
                        typing = null;
                    }
                };

                designer.ZoomToFitRequested += (_, _) => OnZoomToFit(this, new RoutedEventArgs());

                // The centre's columns follow the view. Re-asserted on every change rather than
                // bound, because the splitter writes local widths when it is dragged, and a local
                // value outlives any binding — switching Design → XAML → Split would otherwise
                // keep whatever widths the last drag happened to leave.
                designer.PropertyChanged += (_, changed) =>
                {
                    if (changed.PropertyName == nameof(DesignerViewModel.View))
                    {
                        ApplyViewColumns(designer.View);
                    }
                };

                ApplyViewColumns(designer.View);
                designer.SearchRequested += (_, _) =>
                    this.GetControl<TextBox>("ToolboxSearch").Focus();

                designer.CanvasSelectionRequested += (_, element) => ShowOnCanvas(surface, element);

                // Grouping is asked of the canvas, because the selection lives there and what a
                // group may contain is the editor's rule, not this designer's.
                designer.GroupSelection = () => Group(surface, designer);
                designer.UngroupSelection = surface.UngroupSelection;

                // The documented way to clear the canvas: dropping the item selection takes the
                // targets with it, which is what the frame is drawn from.
                designer.CanvasSelectionCleared += (_, _) => surface.SelectedItems?.Clear();

                // The clipboard belongs to the top level, so the window is what reaches for it.
                // Avalonia 12 puts data transfers on it rather than strings: an item carries the
                // text and the clipboard owns it once it has been handed over.
                designer.PutOnClipboard = async markup =>
                {
                    if (Clipboard is { } clipboard)
                    {
                        var transfer = new DataTransfer();

                        transfer.Add(DataTransferItem.Create(DataFormat.Text, markup));

                        await clipboard.SetDataAsync(transfer);
                    }
                };

                designer.TakeFromClipboard = async () =>
                {
                    if (Clipboard is not { } clipboard)
                    {
                        return null;
                    }

                    // Returned rather than lent: what comes off the clipboard is the caller's to
                    // dispose, which is the opposite of what goes on to it.
                    using IAsyncDataTransfer? transfer = await clipboard.TryGetDataAsync();

                    return transfer is null ? null : await transfer.TryGetTextAsync();
                };
            }
        };
    }

    private DesignerViewModel? Designer => DataContext as DesignerViewModel;

    /// <summary>
    /// Deals the centre's columns for a view: all canvas, all editor, or a split.
    /// </summary>
    /// <remarks>
    /// The editor used to keep a fixed 520 pixels at the window's right edge whatever the view,
    /// which in XAML-only left the canvas's empty star column holding the whole middle of the
    /// window — a void where the editor should have been.
    /// </remarks>
    private void ApplyViewColumns(DocumentView view)
    {
        ColumnDefinitions columns = this.GetControl<Grid>("CentreColumns").ColumnDefinitions;

        (columns[0].Width, columns[2].Width) = view switch
        {
            DocumentView.Xaml => (new GridLength(0), new GridLength(1, GridUnitType.Star)),
            DocumentView.Split => (new GridLength(1, GridUnitType.Star), new GridLength(520)),
            _ => (new GridLength(1, GridUnitType.Star), new GridLength(0)),
        };
    }

    /// <summary>
    /// Repaints the XAML pane's highlighting in the design's own colours.
    /// </summary>
    /// <remarks>
    /// The editor's XML rules are written for a white page: on this background an attribute value
    /// came out dark red on near-black and a tag name dark blue, which is a pane nobody can read.
    /// The rules are kept — what a tag is and where a value starts is exactly what they know — and
    /// only the colours are replaced, taken from the same tokens as the rest of the window, so the
    /// pane follows the light and dark palettes with everything else.
    /// </remarks>
    private void PaintMarkup(AvaloniaEdit.TextEditor editor)
    {
        if (editor.SyntaxHighlighting is not { } rules)
        {
            return;
        }

        Paint("XmlTag", "CodeTag");
        Paint("AttributeName", "CodeAttr");
        Paint("AttributeValue", "CodeStr");
        Paint("Comment", "CodeCmt");
        Paint("XmlDeclaration", "CodeCmt");
        Paint("DocType", "CodeCmt");
        Paint("CData", "YelText");
        Paint("Entity", "YelText");
        Paint("BrokenEntity", "RedText");

        // Links too, which are not a highlighting rule at all: the view draws every address it
        // finds in a colour of its own, a pure blue chosen for a white page. Every document opens
        // with two of them — the namespace declarations — and on the dark palette they stood at
        // 1.9:1 against the pane. The palette has a role for an accent written as text.
        if (this.TryFindResource("Lnk", ActualThemeVariant, out object? link) && link is IBrush address)
        {
            editor.TextArea.TextView.LinkTextForegroundBrush = address;
        }

        // Re-assigned so the view re-reads the colours it had already cached.
        editor.SyntaxHighlighting = rules;
        editor.TextArea.TextView.Redraw();

        void Paint(string token, string key)
        {
            // Asked with the variant, because the palette lives in theme dictionaries: a lookup
            // that does not say which variant it wants finds nothing at all in one.
            if (rules.GetNamedColor(token) is not { } colour
                || !this.TryFindResource(key, ActualThemeVariant, out object? resource)
                || resource is not ISolidColorBrush brush)
            {
                return;
            }

            colour.Foreground = new AvaloniaEdit.Highlighting.SimpleHighlightingBrush(brush.Color);
        }
    }

    /// <summary>Puts the document in the XAML pane, and only when it is not already there.</summary>
    private static void ShowMarkup(AvaloniaEdit.TextEditor editor, string markup)
    {
        if (!string.Equals(editor.Text, markup, StringComparison.Ordinal))
        {
            editor.Text = markup;
        }
    }


    /// <summary>
    /// Follows the editor's selection into the document.
    /// </summary>
    /// <remarks>
    /// The editor reports a target as a container and a control inside it. The container is the
    /// form, the control is what the user pointed at, and the object map turns the second into the
    /// element that produced it.
    /// </remarks>
    private void OnSurfaceSelectionChanged(object? sender, SurfaceSelectionChangedEventArgs e)
    {
        if (Designer is not { } designer || e.NewPrimary is not { } primary)
        {
            return;
        }

        if (primary.Container.DataContext is FormViewModel form)
        {
            designer.SelectFromCanvas(form, primary.Target);
        }
    }

    /// <summary>
    /// Follows the tree's selection onto the canvas, which is the direction the editor cannot infer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The map answers the question the other way round from the selection handler above: there a
    /// control became an element, here an element becomes the control it produced.
    /// </para>
    /// <para>
    /// A window-rooted form needs the second attempt. Its root is a control the canvas cannot host —
    /// that is the whole reason the card stands in for it — so it is nowhere under the editor and
    /// cannot be selected as itself. The card is what stands in for it on screen, so the card is what
    /// gets selected, which is the same answer the canvas gives in the other direction when a click
    /// lands on the card. Without it, picking the root row of a window form did nothing and said
    /// nothing.
    /// </para>
    /// <para>
    /// A row with nothing live behind it at all leaves the canvas as it was, and says so — because a
    /// selection that silently does not happen is indistinguishable from a designer that has stopped
    /// responding. Leaving it is a choice, not a limit: clearing the canvas is
    /// <c>surface.SelectedItems.Clear()</c>, which drops the targets with it. Keeping the
    /// last selection is the friendlier answer to "that row names nothing you can point at".
    /// </para>
    /// </remarks>
    private void ShowOnCanvas(UiDesignerView surface, XamlElement element)
    {
        if (Designer is not { ActiveForm: { } form } designer)
        {
            return;
        }

        if (TrySelectOnCanvas(surface, form, element))
        {
            return;
        }

        // Not yet, rather than not at all. A control the document has only just produced has not
        // been through a layout pass, and until it has it measures nothing — the editor refuses a
        // target it cannot draw a frame around, which is what keeps a zero-sized control from being
        // reported as whatever sits behind it. Every selection that follows an edit arrives in
        // exactly that window, so the ask is repeated once the layout has run.
        //
        // Below normal priority, because that is what puts it after the layout the rebuild queued.
        // The guard that tells this designer's own selection from the user's is held across the
        // wait: without it the editor's answer comes back as though somebody had clicked, and the
        // walk that answers a click resolves to the panel above the control rather than to it.
        IDisposable sync = designer.SyncingCanvas();

        // The form is named rather than captured. A queued operation holds everything its closure
        // mentions, and a form holds the session, the live objects and through them every type in
        // the project's generation — which the studio has to be able to give back when the project
        // is rebuilt. A path holds nothing, and re-reading the active form is also the check that
        // it is still the same one.
        CanonicalPath file = form.File;

        Dispatcher.UIThread.Post(
            () =>
            {
                try
                {
                    if (Designer is not { ActiveForm: { } current } model || current.File != file)
                    {
                        return;
                    }

                    if (!TrySelectOnCanvas(surface, current, element))
                    {
                        model.Log($"! {element.Name} is not on the canvas to select");
                    }
                }
                finally
                {
                    sync.Dispose();
                }
            },
            DispatcherPriority.Background);
    }

    /// <summary>Selects whatever stands for an element on the canvas, if anything does.</summary>
    /// <remarks>
    /// Two answers, in order. The control the element produced is the direct one. A window's root
    /// produced no control of its own — the card is what stands for it — so the container is
    /// selected instead, which is the same answer the canvas gives in the other direction when a
    /// click lands on the card.
    /// </remarks>
    private static bool TrySelectOnCanvas(UiDesignerView surface, FormViewModel form, XamlElement element)
    {
        if (DesignerViewModel.ControlFor(form, element) is { } control
            && surface.SelectTarget(control))
        {
            return true;
        }

        return ReferenceEquals(element, form.Document?.Root)
            && surface.ContainerFromItem(form) is Control card
            && surface.SelectTarget(card);
    }

    /// <summary>
    /// Writes a finished move or resize into the document.
    /// </summary>
    /// <remarks>
    /// On completion rather than on every delta: a drag produces hundreds of intermediate positions
    /// and only the one the user let go at is a fact about the form. Editing the document per frame
    /// would also rebuild the live tree per frame, which is a designer that fights the mouse.
    /// </remarks>
    private void OnEditCompleted(object? sender, SurfaceEditCompletedEventArgs e)
    {
        if (Designer is not { } designer)
        {
            return;
        }

        // A group is not geometry: nothing moved, and what changed is a mark saying which controls
        // belong together. It is also several controls at once, and they go down in one edit — the
        // first write replaces the document and the live tree with it, so a second write naming a
        // control of the tree that has just gone lands nowhere.
        if (e.Kind == SurfaceEditKind.Group)
        {
            foreach (IGrouping<FormViewModel, GroupChange> byForm in e.Changes
                .OfType<GroupChange>()
                .GroupBy(change => FormOf(change.Target)!)
                .Where(group => group.Key is not null))
            {
                designer.WriteGroups(
                    byForm.Key,
                    [.. byForm.Select(change => (change.Target, change.NewId, change.OldId))]);
            }

            return;
        }

        foreach (TargetChange change in e.Changes)
        {
            if (FormOf(change.Target) is { } form)
            {
                designer.WriteGeometry(
                    form,
                    change.Target,
                    moved: e.Kind == SurfaceEditKind.Move,
                    resized: e.Kind == SurfaceEditKind.Resize);
            }
        }
    }

    /// <summary>
    /// Writes the colour a picker was left on.
    /// </summary>
    /// <remarks>
    /// On close rather than on change, because a change arrives for every colour the pointer crosses
    /// in the spectrum and each one rebuilds the inspector's rows — which would take away the picker
    /// mid-gesture. The row has been showing the colour all along; this is where it becomes an edit.
    /// </remarks>
    private void OnColourPicked(object? sender, System.EventArgs e)
    {
        if (sender is not Flyout { Target.DataContext: PropertyRow row } || Designer is not { } designer)
        {
            return;
        }

        // Only if this row is still the one the inspector is showing. A flyout also closes because
        // the rows were rebuilt under it — any edit does that, and the row then holds an element the
        // document has replaced, whose spans point into text that is no longer there. Writing
        // through it would address the wrong document; skipping is the only correct answer left.
        if (designer.Properties.Contains(row))
        {
            row.CommitColour();
        }
    }

    /// <summary>
    /// Fills the canvas's context menu with what this designer can do to a control.
    /// </summary>
    /// <remarks>
    /// The editor asks and the host answers, which is the right way round: the editor knows a right
    /// click happened over a target and nothing about clipboards or documents. Availability is asked
    /// of the commands themselves, so the menu cannot offer a paste when there is nothing to paste
    /// into.
    /// </remarks>
    private void OnContextMenuRequesting(object? sender, SurfaceContextRequestingEventArgs e)
    {
        if (Designer is not { } designer || sender is not UiDesignerView surface)
        {
            return;
        }

        e.Actions =
        [
            Action("copy", "Copy", "Ctrl+C", designer.CopyCommand, 0),
            Action("cut", "Cut", "Ctrl+X", designer.CutCommand, 1),
            Action("paste", "Paste", "Ctrl+V", designer.PasteCommand, 2),
            Action("duplicate", "Duplicate", "Ctrl+D", designer.DuplicateCommand, 3),
            new SurfaceContextAction { Id = "-", IsSeparator = true, Group = "edit", Order = 4 },
            Action("undo", "Undo", "Ctrl+Z", designer.UndoCommand, 5),
            Action("redo", "Redo", "Ctrl+Y", designer.RedoCommand, 6),
            new SurfaceContextAction { Id = "--", IsSeparator = true, Group = "edit", Order = 7 },
            Action("up", "Move up", "Alt+Up", designer.MoveUpCommand, 8),
            Action("down", "Move down", "Alt+Down", designer.MoveDownCommand, 9),
            new SurfaceContextAction
            {
                Id = "wrap",
                Header = "Wrap in",
                Group = "edit",
                Order = 10,
                Items =
                [
                    .. DesignerViewModel.WrapContainers.Select(container =>
                        new SurfaceContextAction
                        {
                            Id = "wrap:" + container,
                            Header = container,
                            Command = new RelayCommand(() => designer.Wrap(container)),
                        }),
                ],
            },
            new SurfaceContextAction
            {
                Id = "unwrap",
                Header = "Unwrap",
                Group = "edit",
                Order = 11,
                Command = new RelayCommand(designer.Unwrap),
            },
            new SurfaceContextAction { Id = "---", IsSeparator = true, Group = "edit", Order = 12 },

            // Grouping is the editor's, and what it produces is a mark on the controls rather than
            // a change to the tree — so it needs no answer from here beyond writing the mark down
            // when EditCompleted reports it. Availability is the editor's answer too: two clusters
            // in one form to group, a group under the pointer to ungroup.
            new SurfaceContextAction
            {
                Id = "group",
                Header = "Group   Ctrl+G",
                Group = "arrange",
                Order = 12,
                Command = new RelayCommand(() => surface.GroupSelection()),
                IsEnabled = surface.CanGroupSelection(),
            },
            new SurfaceContextAction
            {
                Id = "ungroup",
                Header = "Ungroup   Ctrl+Shift+G",
                Group = "arrange",
                Order = 13,
                Command = new RelayCommand(() => surface.UngroupSelection()),
                IsEnabled = surface.CanUngroupSelection(),
            },

            // Distribution is the editor's own arithmetic, not the document's: it moves the
            // selected controls and reports the moves through EditCompleted like any gesture, so
            // they reach the document by the path every other move takes. Offered only when the
            // editor says it can — three controls in one form, in a layout that lets them move.
            new SurfaceContextAction
            {
                Id = "distribute.h",
                Header = "Distribute horizontally",
                Group = "arrange",
                Order = 14,
                Command = new RelayCommand(() => surface.DistributeHorizontally()),
                IsEnabled = surface.SelectedTargets.Count > 2,
            },
            new SurfaceContextAction
            {
                Id = "distribute.v",
                Header = "Distribute vertically",
                Group = "arrange",
                Order = 15,
                Command = new RelayCommand(() => surface.DistributeVertically()),
                IsEnabled = surface.SelectedTargets.Count > 2,
            },
            new SurfaceContextAction { Id = "----", IsSeparator = true, Group = "arrange", Order = 16 },
            Action("delete", "Delete", "Del", designer.DeleteSelectedCommand, 17),
        ];

        static SurfaceContextAction Action(
            string id, string header, string gesture, UiDesigner.Demo.ViewModels.RelayCommand command, int order) =>
            new()
            {
                Id = id,
                Header = $"{header}   {gesture}",
                Group = "edit",
                Order = order,
                Command = command,
                IsEnabled = command.CanExecute(null),
            };
    }

    /// <summary>
    /// Groups, unless the selection holds a control and something inside it.
    /// </summary>
    /// <remarks>
    /// A group whose members include both a panel and its own children is not a group anybody can
    /// work with: a click inside the panel expands to the cluster, the cluster contains the panel,
    /// and the panel is what gets selected — so nothing in it can be reached again. It is easy to
    /// arrive at by accident, because a marquee over a form takes the panel along with what it
    /// holds, and every control here is selectable in its own right.
    /// <para>
    /// Refused here rather than untangled: which of the two the person meant is not knowable, and
    /// silently dropping one of them would group something other than what is on screen.
    /// </para>
    /// </remarks>
    private static bool Group(UiDesignerView surface, DesignerViewModel designer)
    {
        Control[] selected = [.. surface.SelectedTargets.Select(target => target.Target)];

        foreach (Control control in selected)
        {
            for (Control? above = control.Parent as Control; above is not null;
                above = above.Parent as Control)
            {
                if (System.Array.IndexOf(selected, above) < 0)
                {
                    continue;
                }

                designer.Log($"! {above.GetType().Name} is selected along with what is inside it — "
                    + "select either the container or its contents, not both");

                return false;
            }
        }

        return surface.GroupSelection();
    }

    private void OnDeleteRequested(object? sender, SurfaceDeleteRequestedEventArgs e)
    {
        if (Designer is not { } designer)
        {
            return;
        }

        foreach (SurfaceSelectionTarget target in e.Targets)
        {
            if (target.Container.DataContext is FormViewModel form)
            {
                designer.DeleteFromCanvas(form, target.Target);
            }
        }

        // Answering it is what makes it happen: until a subscriber marks the request handled the
        // editor does nothing, because removing a control from the tree is not the editor's to do.
        e.Handled = true;
    }

    /// <summary>
    /// Answers the editor's reorder request, which is what makes the gesture happen at all.
    /// </summary>
    /// <remarks>
    /// The editor refuses to start a drag in a flow layout when nothing is listening — no insertion
    /// point is drawn and no order changes — so this handler is not a refinement of the gesture, it
    /// is the gesture.
    /// </remarks>
    private void OnReorderRequested(object? sender, UiDesignerReorderRequestedEventArgs e)
    {
        if (Designer is not { } designer || FormOf(e.Target) is not { } form)
        {
            return;
        }

        designer.ReorderFromCanvas(form, e.Target, e.Anchor);

        e.Handled = true;
    }

    /// <summary>The form a control on the surface belongs to.</summary>
    private static FormViewModel? FormOf(Control control)
    {
        for (Control? current = control; current is not null; current = current.Parent as Control)
        {
            if (current is UiDesignerItem item && item.DataContext is FormViewModel form)
            {
                return form;
            }
        }

        return null;
    }

    private async void OnToolboxPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ItemsControl toolbox
            || !e.GetCurrentPoint(toolbox).Properties.IsLeftButtonPressed
            || (e.Source as Control)?.DataContext is not ToolboxEntry entry)
        {
            return;
        }

        _dragging = entry;

        var data = new DataTransfer();

        data.Add(DataTransferItem.CreateText(entry.Name));

        // A swap of the project's types would take the forms off the canvas under the drop.
        using IDisposable? deferral = Designer?.Defer("a drag from the toolbox");

        try
        {
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
        }
        finally
        {
            _dragging = null;
        }
    }

    /// <summary>
    /// Shows where the entry being dragged would land, as the editor resolves it.
    /// </summary>
    /// <remarks>
    /// The editor answers where — the parent under the pointer that can take a child, and the place in
    /// it — and draws it; this window answers whether, which for the palette is only "is it ours". The
    /// place shown is the place the drop writes: the drop asks the same question at the same point.
    /// </remarks>
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        UiDesignerView editor = this.GetControl<UiDesignerView>("Surface");

        if (_dragging is not null
            && Designer is not null
            && editor.TryResolveDropPlacement(e.GetPosition(editor), out SurfaceDropPlacement? placement))
        {
            editor.ShowDropIndicator(placement);
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            editor.HideDropIndicator();
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e) =>
        this.GetControl<UiDesignerView>("Surface").HideDropIndicator();

    /// <summary>
    /// Drops a toolbox entry where the indicator stood.
    /// </summary>
    /// <remarks>
    /// Resolved by the editor geometrically rather than from <c>e.Source</c>: a loaded form does not
    /// take input, so nothing inside it is hit-testable and the event source is the card whatever the
    /// pointer is over. The editor measures rectangles, as it does to decide what a click selected.
    /// </remarks>
    private void OnDrop(object? sender, DragEventArgs e)
    {
        UiDesignerView editor = this.GetControl<UiDesignerView>("Surface");
        editor.HideDropIndicator();

        if (_dragging is not { } entry
            || Designer is not { } designer
            || !editor.TryResolveDropPlacement(e.GetPosition(editor), out SurfaceDropPlacement? placement)
            || editor.ItemFromContainer(placement.Container) is not FormViewModel form)
        {
            return;
        }

        designer.Drop(form, entry, placement);

        e.Handled = true;
    }

    private async Task<string?> PickEntryPointAsync()
    {
        System.Collections.Generic.IReadOnlyList<IStorageFile> files =
            await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open a solution or project",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Solutions and projects")
                    {
                        Patterns = ["*.sln", "*.slnx", "*.csproj", "*.fsproj", "*.vbproj"],
                    },
                ],
            });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    /// <summary>
    /// Asks what to create and what to call it.
    /// </summary>
    /// <remarks>
    /// Two questions in one dialog because they are one decision: a window and a user control are
    /// different things and are named differently, so the suggestion follows the choice until
    /// somebody types over it. The dialog before this one asked only for a name and made a user
    /// control whatever the answer was — which is how a control came to be called NewForm.
    /// </remarks>
    private async Task<NewFormRequest?> AskForNewFormAsync(NewFormRequest suggested)
    {
        var window = new RadioButton { Content = "Window", GroupName = "kind", IsChecked = true };
        var control = new RadioButton { Content = "UserControl", GroupName = "kind" };

        var name = new TextBox
        {
            Text = suggested.Name,
            Width = 300,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        };

        Avalonia.Automation.AutomationProperties.SetName(name, "Name");

        Button create = Dialogs.Primary("Create");
        Button cancel = Dialogs.Quiet("Cancel");

        create.IsDefault = true;
        cancel.IsCancel = true;

        Window dialog = Dialogs.Create(
            "New form",
            400,
            Dialogs.Label("What to create"),
            new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 16,
                Children = { window, control },
            },
            Dialogs.Label("Name"),
            name,
            Dialogs.Answers(create, cancel));

        // The suggestion follows the choice, and stops following it the moment somebody types.
        var typed = false;

        name.TextChanged += (_, _) => typed = name.Text is not ("NewWindow" or "NewUserControl");

        window.IsCheckedChanged += (_, _) =>
        {
            if (!typed && window.IsChecked == true)
            {
                name.Text = "NewWindow";
            }
        };

        control.IsCheckedChanged += (_, _) =>
        {
            if (!typed && control.IsChecked == true)
            {
                name.Text = "NewUserControl";
            }
        };

        NewFormRequest? answer = null;

        create.Click += (_, _) =>
        {
            answer = new NewFormRequest(
                name.Text ?? string.Empty,
                control.IsChecked == true ? NewFormKind.UserControl : NewFormKind.Window);

            dialog.Close();
        };

        cancel.Click += (_, _) => dialog.Close();

        using (Designer?.Defer("a dialog"))
        {
            await dialog.ShowDialog(this);
        }

        return answer;
    }

    /// <summary>Choosing a tab makes that form the one being edited.</summary>
    /// <remarks>
    /// A click and not a press, because the tab is a button: that is what lets Enter and Space
    /// choose it, which a press handler on a border never heard.
    /// </remarks>
    private void OnTabChosen(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FormViewModel form } && Designer is { } designer)
        {
            designer.ActiveForm = form;
        }
    }

    /// <summary>And the cross on it closes that form, whether or not it is the active one.</summary>
    private void OnTabClosed(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FormViewModel form } && Designer is { } designer)
        {
            designer.CloseForm(form, ask: true);
        }
    }

    /// <summary>Selects the row under a right press, so its menu acts on it.</summary>
    private void OnTreePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox tree
            || !e.GetCurrentPoint(tree).Properties.IsRightButtonPressed)
        {
            return;
        }

        for (Visual? current = e.Source as Visual; current is not null; current = current.GetVisualParent())
        {
            if (current is ListBoxItem { DataContext: HierarchyRow row } && Designer is { } designer)
            {
                designer.SelectedHierarchyRow = row;

                return;
            }
        }
    }

    /// <summary>Wraps the selection in the container the menu item names.</summary>
    private void OnWrap(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string container })
        {
            Designer?.Wrap(container);
        }
    }

    private void OnUnwrap(object? sender, RoutedEventArgs e) => Designer?.Unwrap();

    /// <summary>Opens the tree's filter and puts the caret in it.</summary>
    private void OnFindInTree(object? sender, RoutedEventArgs e)
    {
        if (Designer is not { } designer)
        {
            return;
        }

        designer.IsHierarchySearchOpen = !designer.IsHierarchySearchOpen;

        if (designer.IsHierarchySearchOpen)
        {
            this.GetControl<TextBox>("TreeSearch").Focus();
        }
    }

    /// <summary>
    /// Fits the form to the window.
    /// </summary>
    /// <remarks>
    /// The zoom that makes the card fit with a margin around it, and then the viewport moved onto
    /// it. Worked out here rather than in the view model because it is a question about a viewport:
    /// the editor knows how big it is on screen and the form knows how big it is in its own
    /// coordinates, and nothing else needs to know either.
    /// </remarks>
    private void OnZoomToFit(object? sender, RoutedEventArgs e)
    {
        if (Designer is not { ActiveForm: { } form } designer)
        {
            return;
        }

        UiDesignerView surface = this.GetControl<UiDesignerView>("Surface");

        double width = form.Width;
        double height = form.Height;

        if (width <= 0 || height <= 0 || surface.Bounds.Width <= 0 || surface.Bounds.Height <= 0)
        {
            return;
        }

        // A tenth of the smaller side as breathing room, and never magnified past life size: a form
        // smaller than the window is easier to work on at 100% than blown up to fill it.
        double fit = Math.Min(
            (surface.Bounds.Width * 0.9) / width,
            (surface.Bounds.Height * 0.9) / height);

        designer.Zoom = Math.Clamp(fit, 0.1, 1);

        surface.CenterOnSelection();
    }

    /// <summary>
    /// Asks what to do with a form that is about to be closed with unsaved edits.
    /// </summary>
    /// <remarks>
    /// Three answers, because two of them are wrong on their own: a dialog without Cancel makes the
    /// cross on a tab dangerous, and one without Discard makes it impossible to throw away an
    /// experiment. Cancel is the default, as it is for every question that can lose work.
    /// </remarks>
    private async Task<DesignerViewModel.SaveAnswer> AskToSaveAsync(string name)
    {
        Button save = Dialogs.Primary("Save");
        Button discard = Dialogs.Quiet("Don't save");
        Button cancel = Dialogs.Quiet("Cancel");

        cancel.IsCancel = true;
        cancel.IsDefault = true;

        Window dialog = Dialogs.Create(
            "Unsaved changes",
            420,
            Dialogs.Message($"{name} has edits that are not in the file."),
            Dialogs.Answers(save, discard, cancel));

        DesignerViewModel.SaveAnswer answer = DesignerViewModel.SaveAnswer.Cancel;

        save.Click += (_, _) =>
        {
            answer = DesignerViewModel.SaveAnswer.Save;
            dialog.Close();
        };

        discard.Click += (_, _) =>
        {
            answer = DesignerViewModel.SaveAnswer.Discard;
            dialog.Close();
        };

        cancel.Click += (_, _) => dialog.Close();

        using (Designer?.Defer("a dialog"))
        {
            await dialog.ShowDialog(this);
        }

        return answer;
    }

    /// <summary>Enter writes the value, by leaving the field the way a click elsewhere would.</summary>
    /// <remarks>
    /// The binding commits on lost focus, so this does not need to know about bindings: moving the
    /// focus is the same event, and it leaves the caret somewhere a person expects after Enter.
    /// </remarks>
    private void OnPropertyKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter && sender is Control field)
        {
            field.Focus(NavigationMethod.Unspecified);

            this.GetControl<UiDesignerView>("Surface").Focus();

            e.Handled = true;
        }
    }

    private void OnProjectFileMenuOpen(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FileTile file })
        {
            Designer?.OpenFile(file);
        }
    }

    private void OnProjectFileDeleted(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FileTile file })
        {
            Designer?.DeleteFile(file);
        }
    }

    /// <summary>
    /// Asks a yes-or-no question before something is destroyed.
    /// </summary>
    /// <remarks>
    /// Cancel is the default button, because the answer to a question nobody read should be the one
    /// that changes nothing.
    /// </remarks>
    private async Task<bool> AskToConfirmAsync(string title, string question)
    {
        Button yes = Dialogs.Primary("Delete");
        Button no = Dialogs.Quiet("Cancel");

        no.IsCancel = true;
        no.IsDefault = true;

        Window dialog = Dialogs.Create(title, 420, Dialogs.Message(question), Dialogs.Answers(yes, no));

        var answer = false;

        yes.Click += (_, _) =>
        {
            answer = true;
            dialog.Close();
        };

        no.Click += (_, _) => dialog.Close();

        using (Designer?.Defer("a dialog"))
        {
            await dialog.ShowDialog(this);
        }

        return answer;
    }

    /// <summary>Opens the file a tile in the project grid stands for.</summary>
    private void OnProjectFileOpened(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: FileTile file })
        {
            Designer?.OpenFile(file);
        }
    }

    /// <summary>The same, for a tile the keyboard stopped on: Enter is what a double click is.</summary>
    /// <remarks>
    /// <para>
    /// The grid is a list for exactly this. While it was a panel of borders it had nothing for the
    /// keyboard to stand on and nothing to press Enter on, so a file could be opened by pointer or
    /// not at all.
    /// </para>
    /// <para>
    /// The tile is the one with the focus, not the one selected. Tab arrives on a tile without
    /// selecting it, and the list selects it on this same Enter — after this handler has run.
    /// </para>
    /// </remarks>
    private void OnProjectFileKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter && e.Source is ListBoxItem { DataContext: FileTile file })
        {
            Designer?.OpenFile(file);

            e.Handled = true;
        }
    }

    // -- The window's own chrome ------------------------------------------------------------------
    //
    // `BorderOnly` keeps the frame and drops the caption, so the toolbar is the title bar and these
    // four handlers are what a caption would otherwise have given for free. Dragging is asked of the
    // platform rather than simulated with pointer arithmetic, which is what keeps snapping, the
    // aero-shake gesture and multi-monitor behaviour working the way every other window does.

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // A control in the toolbar handles its own press; the empty space between them moves the
        // window. Testing the source for the Border itself would never match — its Grid covers it —
        // so the walk up asks the real question: was anything clickable underneath the pointer?
        if (IsInteractive(e.Source as Visual))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximised();

            return;
        }

        BeginMoveDrag(e);
    }

    private void OnMinimise(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximise(object? sender, RoutedEventArgs e) => ToggleMaximised();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    /// <summary>Whether the press landed on something in the toolbar that answers presses itself.</summary>
    private static bool IsInteractive(Visual? source)
    {
        for (Visual? visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Button or TextBox or ComboBox or MenuItem)
            {
                return true;
            }

            if (visual is Border { Name: "TitleBar" })
            {
                return false;
            }
        }

        return false;
    }

    private void ToggleMaximised() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
