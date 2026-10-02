using System;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.Surface.UiDesigner;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using UiDesigner.Demo.ViewModels;

namespace UiDesigner.Demo.Views;

/// <summary>
/// Drops, the way the window makes them: the editor resolves the place at a point, shows it, and the
/// designer writes it.
/// </summary>
/// <remarks>
/// <para>
/// The runs do not drive a system drag — a check cannot hold a mouse button down across processes —
/// but nothing is skipped by that: the window's drag-over asks <see cref="UiDesignerView.TryResolveDropPlacement"/>
/// at the pointer and shows the answer, and its drop asks again and hands the answer to
/// <see cref="DesignerViewModel.Drop"/>. These do the same at points chosen inside the controls.
/// </para>
/// <para>
/// One thing a run does that a person cannot: drop again before the canvas has caught up with the last
/// drop. The document takes a drop at once and the live tree follows a moment later, and a place
/// resolved against a panel the last edit has not reached goes in front of the control that edit wrote.
/// So every place is resolved once the session shows the form's text and the panel is laid out.
/// </para>
/// </remarks>
internal static partial class StudioCheck
{
    /// <summary>
    /// Drops a toolbox entry into a control at the end, the way a person drops below its last child.
    /// </summary>
    /// <returns><see langword="false"/> when the editor found no place there.</returns>
    private static async Task<bool> DropIntoAsync(
        Window window, DesignerViewModel designer, FormViewModel form, ToolboxEntry tool, Func<Control?> over)
    {
        if (window.FindControl<UiDesignerView>("Surface") is not { } editor
            || await PlaceAtAsync(editor, form, over, AtEnd) is not { } placement)
        {
            return false;
        }

        editor.ShowDropIndicator(placement);
        designer.Drop(form, tool, placement);
        editor.HideDropIndicator();

        return true;
    }

    /// <summary>
    /// A point that means "after everything" in a panel: inside its last child, at that child's far
    /// corner — past its middle along any flow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a corner of the panel. The reorder rule takes the nearest child by its rectangle, and a
    /// narrow last child stands centred — a TextBox with a width is — so the panel's bottom-left corner
    /// is nearer the wide text above it, and the drop went in front of the TextBox. A person would have
    /// seen the line there and moved; a run aims inside the child it means.
    /// </para>
    /// <para>
    /// The last child the form shows, not the last there is: a form a long run has filled pushes its
    /// panel's last children past its bottom edge, where nothing takes a drop — a person drops after
    /// the last control they can see.
    /// </para>
    /// </remarks>
    private static Point AtEnd(Control target)
    {
        if (target is Panel panel && target.FindAncestorOfType<UiDesignerItem>() is { } card)
        {
            foreach (Control child in panel.Children.Reverse())
            {
                var corner = new Point(child.Bounds.Right - 2, child.Bounds.Bottom - 2);

                if (child.IsVisible
                    && target.TranslatePoint(corner, card) is { } onCard
                    && new Rect(card.Bounds.Size).Contains(onCard))
                {
                    return corner;
                }
            }
        }

        return new Point(Math.Min(10, target.Bounds.Width / 2), Math.Min(10, target.Bounds.Height / 2));
    }

    /// <summary>
    /// The place the editor offers at a point inside a control, as the window asks under the pointer —
    /// once the canvas has caught up with the document.
    /// </summary>
    private static async Task<SurfaceDropPlacement?> PlaceAtAsync(
        UiDesignerView editor, FormViewModel form, Func<Control?> control, Func<Control, Point> inside)
    {
        SurfaceDropPlacement? placement = null;
        string why = string.Empty;

        await Until(Resolved, 10);

        if (placement is null)
        {
            Say($"  no place to drop: {why}");
        }

        return placement;

        bool Resolved()
        {
            if (!CaughtUp(form))
            {
                why = $"the canvas is behind the document ({form.Live?.State})";
                return false;
            }

            if (control() is not { } target)
            {
                why = "there is no control to drop into";
                return false;
            }

            if (!LaidOut(target))
            {
                why = $"the {target.GetType().Name} is not laid out";
                return false;
            }

            if (target.TranslatePoint(inside(target), editor) is not { } point)
            {
                why = $"the point is not on the editor";
                return false;
            }

            if (!editor.TryResolveDropPlacement(point, out placement))
            {
                why = $"the editor offers nothing at {point} over the {target.GetType().Name} ({target.Bounds})";
                return false;
            }

            return true;
        }
    }

    /// <summary>Whether the canvas shows the text the form holds: the session is live on the same document.</summary>
    private static bool CaughtUp(FormViewModel form) =>
        form.Live is { State: XamlLiveDocumentState.Live, Session: { } session } live
        && ReferenceEquals(session.Document, live.Document);

    /// <summary>Whether a panel's children are all laid out; a control that is not a panel is.</summary>
    private static bool LaidOut(Control control) =>
        control is not Panel panel
        || panel.Children.All(static child => !child.IsVisible || child.Bounds.Width > 0 || child.Bounds.Height > 0);

    /// <summary>
    /// Shows a place, drops an entry there, and waits until the document has the control.
    /// </summary>
    private static async Task<bool> DropAtAsync(
        UiDesignerView editor, DesignerViewModel designer, FormViewModel form, string name, SurfaceDropPlacement placement)
    {
        if (designer.Toolbox.FirstOrDefault(entry => entry.Name == name) is not { } tool)
        {
            return false;
        }

        int before = Count(designer, name);

        editor.ShowDropIndicator(placement);

        bool shown = Equals(editor.DropIndicator, placement);

        designer.Drop(form, tool, placement);
        editor.HideDropIndicator();

        return shown && await Until(() => Count(designer, name) > before, 30);
    }

    /// <summary>Undoes until the document says what it said, a step at a time.</summary>
    private static async Task<bool> UndoToAsync(DesignerViewModel designer, FormViewModel form, string text)
    {
        for (int step = 0; step < 8 && Text(form) != text && designer.UndoCommand.CanExecute(null); step++)
        {
            string current = Text(form);

            designer.UndoCommand.Execute(null);

            await Until(() => Text(form) != current, 30);
        }

        return Text(form) == text && await Until(() => CaughtUp(form), 30);
    }

    /// <summary>
    /// Where a live control's slot starts, in the editor's world: its top-left less its margin, which
    /// is what the panel placed and what the indicator showed — a Separator's theme gives it a margin.
    /// </summary>
    private static Point SlotOf(UiDesignerView editor, Control control) =>
        editor.GetWorldPosition(control.TranslatePoint(default, editor) ?? default)
        - new Vector(control.Margin.Left, control.Margin.Top);

    /// <summary>
    /// Waits for the live control the element produced to stand where it should — the canvas follows the
    /// document a moment later — and says where it stands.
    /// </summary>
    private static async Task<(bool Stands, Point At)> StandsAsync(
        UiDesignerView editor, FormViewModel form, Func<XamlElement?> element, Func<Control, Point, bool> where)
    {
        Point at = default;

        bool stands = await Until(
            () => CaughtUp(form)
                && LiveOf<Control>(form, element()) is { Bounds.Height: > 0 } control
                && where(control, at = SlotOf(editor, control)),
            10);

        return (stands, at);
    }

    /// <summary>The panel's first and third controls: what the second one now stands between.</summary>
    private static (Control? Above, Control? Under) Neighbours(FormViewModel form) =>
        (LiveOf<Control>(form, PanelOf(form)?.ContentElements.ElementAtOrDefault(0)),
         LiveOf<Control>(form, PanelOf(form)?.ContentElements.ElementAtOrDefault(2)));

    /// <summary>Where a live control's slot ends, in the editor's world.</summary>
    private static double Bottom(UiDesignerView editor, Control control) =>
        SlotOf(editor, control).Y + control.Margin.Top + control.Bounds.Height + control.Margin.Bottom;

    /// <summary>The form's panel as the document writes it: the window's one child.</summary>
    private static XamlElement? PanelOf(FormViewModel form) =>
        form.Document?.Root?.ContentElements.FirstOrDefault(static element => element.Name.LocalName == "StackPanel");

    /// <summary>The live control the document's element produced.</summary>
    private static T? LiveOf<T>(FormViewModel form, XamlElement? element) where T : Control =>
        element is null ? null : DesignerViewModel.ControlFor(form, element) as T;

    /// <summary>The second child of the first page of the TabControl appended to the form's panel.</summary>
    private static XamlElement? WrittenOnPage(FormViewModel form) =>
        PanelOf(form)?.ContentElements.LastOrDefault()?.ContentElements.FirstOrDefault()
            ?.ContentElements.FirstOrDefault()?.ContentElements.ElementAtOrDefault(1);

    /// <summary>
    /// 3a. Drops land where the indicator stood: between two neighbours of a StackPanel, onto a
    /// TabControl's page, into a Grid's cell and at a Canvas's point.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The palette used to decide the place itself, and decided it badly: every control went to the end
    /// of a StackPanel and into the first cell of a Grid, and a position for a Canvas was written in
    /// front of the snippet's last <c>/&gt;</c> — the last child's, for a TabControl. Each case here asks
    /// the document where the control went and the canvas where it stands, against the indicator that
    /// was shown.
    /// </para>
    /// <para>
    /// Every case is undone again before the next, back to the text the case started from: the form
    /// stays small enough for what is appended to stay on it, and the steps after this one see the
    /// form step 3 left.
    /// </para>
    /// </remarks>
    private static async Task<int> DropsAsync(Window window, DesignerViewModel designer, FormViewModel form)
    {
        var failures = 0;

        if (window.FindControl<UiDesignerView>("Surface") is not { } editor)
        {
            return Fail(ref failures, "drops: the window has no editor");
        }

        // Between the panel's first two controls: the reorder rule, a line at the second one's top.
        string start = Text(form);

        Control? Second() => LiveOf<StackPanel>(form, PanelOf(form))?.Children.ElementAtOrDefault(1);

        if (await PlaceAtAsync(editor, form, Second, static _ => new Point(4, 2)) is not
                { Kind: SurfaceDropKind.Insert, Index: 1 } between
            || !ReferenceEquals(between.Anchor, Second()))
        {
            Fail(ref failures, "drops: above the panel's second control the editor offers no place in front of it");
        }
        else if (!await DropAtAsync(editor, designer, form, "Separator", between))
        {
            Fail(ref failures, "drops: the Separator dropped between two controls never reached the document");
        }
        else if (PanelOf(form)?.ContentElements.ElementAtOrDefault(1) is not { Name.LocalName: "Separator" })
        {
            Fail(ref failures, "drops: the Separator is not the panel's second child in the document");
        }
        // Between the same two neighbours the line was drawn between — not at the line's height: the
        // form's panel is centred, and what goes into it moves it up by half of what it adds.
        else if (await StandsAsync(
                editor,
                form,
                () => PanelOf(form)?.ContentElements.ElementAtOrDefault(1),
                (separator, at) => Neighbours(form) is ({ } above, { } under)
                    && at.Y >= Bottom(editor, above) - 1
                    && Bottom(editor, separator) <= SlotOf(editor, under).Y + 1) is { Stands: false } separator)
        {
            Fail(ref failures, $"drops: the Separator stands at {separator.At.Y:0.#}, not between the two controls the line was drawn between");
        }
        else
        {
            Say("drops: between two neighbours, the document and the canvas put it where the line stood");
        }

        if (!await UndoToAsync(designer, form, start))
        {
            return Fail(ref failures, "drops: undoing the Separator did not give the form back");
        }

        failures += await IntoTabPageAsync(window, editor, designer, form);
        failures += await IntoGridCellAsync(window, editor, designer, form);
        failures += await AtCanvasPointAsync(window, editor, designer, form);

        return failures;
    }

    /// <summary>Onto the first page of a TabControl, below the text it holds.</summary>
    private static async Task<int> IntoTabPageAsync(
        Window window, UiDesignerView editor, DesignerViewModel designer, FormViewModel form)
    {
        var failures = 0;
        string start = Text(form);

        if (!await AppendAsync(window, designer, form, "TabControl"))
        {
            return Fail(ref failures, "drops: the TabControl never reached the form");
        }

        StackPanel? Page() =>
            LiveOf<StackPanel>(form, PanelOf(form)?.ContentElements.LastOrDefault()?.ContentElements.FirstOrDefault()
                ?.ContentElements.FirstOrDefault());

        if (await PlaceAtAsync(
                editor,
                form,
                Page,
                static page => new Point(10, (page as Panel)?.Children.FirstOrDefault()?.Bounds.Height + 10 ?? 10)) is not
                { Kind: SurfaceDropKind.Insert, Index: 1, Anchor: null } below
            || !ReferenceEquals(below.Parent, Page()))
        {
            Fail(ref failures, "drops: below the text on the first tab page the editor offers no place on the page");
        }
        else if (!await DropAtAsync(editor, designer, form, "ProgressBar", below))
        {
            Fail(ref failures, "drops: the ProgressBar dropped on the tab page never reached the document");
        }
        else if (WrittenOnPage(form) is not { Name.LocalName: "ProgressBar" })
        {
            Fail(ref failures, "drops: the ProgressBar is not the tab page's second child in the document");
        }
        else if (await StandsAsync(
                editor,
                form,
                () => WrittenOnPage(form),
                (_, at) => Math.Abs(at.Y - below.Indicator.Top) <= 1) is { Stands: false } bar)
        {
            Fail(ref failures, $"drops: the ProgressBar stands at {bar.At.Y:0.#}, the line under the page's text was at {below.Indicator.Top:0.#}");
        }
        else
        {
            Say("drops: onto a tab page, below its text, where the line stood");
        }

        if (!await UndoToAsync(designer, form, start))
        {
            Fail(ref failures, "drops: undoing the tab page drop did not give the form back");
        }

        return failures;
    }

    /// <summary>Into the far cell of a two-by-two Grid.</summary>
    private static async Task<int> IntoGridCellAsync(
        Window window, UiDesignerView editor, DesignerViewModel designer, FormViewModel form)
    {
        var failures = 0;
        string start = Text(form);

        Grid? Table() => LiveOf<Grid>(form, PanelOf(form)?.ContentElements.LastOrDefault());

        if (!await AppendAsync(window, designer, form, "Grid") || Table() is not { } grid)
        {
            return Fail(ref failures, "drops: the Grid never reached the form");
        }

        designer.SelectFromCanvas(form, grid);

        foreach (string tracks in new[] { "ColumnDefinitions", "RowDefinitions" })
        {
            if (designer.Properties.FirstOrDefault(row => row.Name == tracks) is not { } row)
            {
                Fail(ref failures, $"drops: the inspector offers no {tracks} for the Grid");

                continue;
            }

            row.Value = "*,*";

            await Until(() => Text(form).Contains($"{tracks}=\"*,*\"", StringComparison.Ordinal), 30);
        }

        if (await PlaceAtAsync(
                editor,
                form,
                () => Table() is { ColumnDefinitions.Count: 2, RowDefinitions.Count: 2 } table ? table : null,
                static table => new Point(table.Bounds.Width * 0.75, table.Bounds.Height * 0.75)) is not
                { Kind: SurfaceDropKind.Cell, Row: 1, Column: 1 } cell)
        {
            Fail(ref failures, "drops: over the Grid's far quarter the editor offers no cell (1, 1)");
        }
        else if (!await DropAtAsync(editor, designer, form, "CheckBox", cell))
        {
            Fail(ref failures, "drops: the CheckBox dropped into a cell never reached the document");
        }
        else if (PanelOf(form)?.ContentElements.LastOrDefault()?.ContentElements.LastOrDefault() is not
                { Name.LocalName: "CheckBox" } written
            || written.GetAttribute("Grid.Row")?.GetValueText() != "1"
            || written.GetAttribute("Grid.Column")?.GetValueText() != "1")
        {
            Fail(ref failures, "drops: the CheckBox in the document does not say Grid.Row=\"1\" Grid.Column=\"1\"");
        }
        else if (await StandsAsync(
                editor,
                form,
                () => PanelOf(form)?.ContentElements.LastOrDefault()?.ContentElements.LastOrDefault(),
                (box, at) => Grid.GetRow(box) == 1 && Grid.GetColumn(box) == 1 && cell.Indicator.Contains(at)) is
                { Stands: false } box)
        {
            Fail(ref failures, $"drops: the CheckBox stands at {box.At}, the cell shown was {cell.Indicator}");
        }
        else
        {
            Say("drops: into a Grid's cell, written as Grid.Row and Grid.Column, where the cell was shown");
        }

        if (!await UndoToAsync(designer, form, start))
        {
            Fail(ref failures, "drops: undoing the cell drop did not give the form back");
        }

        return failures;
    }

    /// <summary>At a point of a Canvas, written as its position.</summary>
    private static async Task<int> AtCanvasPointAsync(
        Window window, UiDesignerView editor, DesignerViewModel designer, FormViewModel form)
    {
        var failures = 0;
        string start = Text(form);

        if (!await AppendAsync(window, designer, form, "Canvas"))
        {
            return Fail(ref failures, "drops: the Canvas never reached the form");
        }

        if (await PlaceAtAsync(
                editor,
                form,
                () => LiveOf<Canvas>(form, PanelOf(form)?.ContentElements.LastOrDefault()),
                static _ => new Point(30, 20)) is not { Kind: SurfaceDropKind.Position } point
            || Math.Abs(point.Position.X - 30) > 0.5
            || Math.Abs(point.Position.Y - 20) > 0.5)
        {
            Fail(ref failures, "drops: over the Canvas the editor offers no position at the point");
        }
        else if (!await DropAtAsync(editor, designer, form, "RadioButton", point))
        {
            Fail(ref failures, "drops: the RadioButton dropped onto a Canvas never reached the document");
        }
        else if (PanelOf(form)?.ContentElements.LastOrDefault()?.ContentElements.LastOrDefault() is not
                { Name.LocalName: "RadioButton" } written
            || written.GetAttribute("Canvas.Left")?.GetValueText() != "30"
            || written.GetAttribute("Canvas.Top")?.GetValueText() != "20")
        {
            Fail(ref failures, "drops: the RadioButton in the document does not say Canvas.Left=\"30\" Canvas.Top=\"20\"");
        }
        else if (await StandsAsync(
                editor,
                form,
                () => PanelOf(form)?.ContentElements.LastOrDefault()?.ContentElements.LastOrDefault(),
                (radio, at) => Math.Abs(Canvas.GetLeft(radio) - 30) <= 0.5
                    && Math.Abs(at.X - point.Indicator.X - 30) <= 1
                    && Math.Abs(at.Y - point.Indicator.Y - 20) <= 1) is { Stands: false } radio)
        {
            Fail(ref failures, $"drops: the RadioButton stands at {radio.At}, the Canvas was at {point.Indicator.TopLeft} and the point at (30, 20) in it");
        }
        else
        {
            Say("drops: at a Canvas's point, written on the control and not on anything inside it");
        }

        if (!await UndoToAsync(designer, form, start))
        {
            Fail(ref failures, "drops: undoing the Canvas drop did not give the form back");
        }

        return failures;
    }

    /// <summary>
    /// Appends a toolbox entry to the form's panel and waits until the document has it last and the
    /// canvas shows it.
    /// </summary>
    private static async Task<bool> AppendAsync(Window window, DesignerViewModel designer, FormViewModel form, string name)
    {
        if (designer.Toolbox.FirstOrDefault(entry => entry.Name == name) is not { } tool
            || !await DropIntoAsync(window, designer, form, tool, () => LiveOf<StackPanel>(form, PanelOf(form))))
        {
            return false;
        }

        return await Until(
            () => PanelOf(form)?.ContentElements.LastOrDefault() is { } last
                && last.Name.LocalName == name
                && CaughtUp(form)
                && LiveOf<Control>(form, last) is { Bounds.Height: > 0 },
            30);
    }
}
