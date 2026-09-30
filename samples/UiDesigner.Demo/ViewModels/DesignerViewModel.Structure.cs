using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;

namespace UiDesigner.Demo.ViewModels;

/// <summary>
/// The structure edits: move a control among its siblings, wrap it, unwrap it.
/// </summary>
/// <remarks>
/// <para>
/// Order and nesting are the half of layout a pointer is bad at. Dragging within a flow layout
/// works, but "one step up" said with a drag is a gamble on a few pixels, and there is no gesture
/// at all that means "put a Border around this". These are those operations, said exactly.
/// </para>
/// <para>
/// Each one is a single document edit, so each is one step of the history. Markup's editor is what
/// makes that possible: <c>MoveElement</c> is a removal and an insertion in one batch, and
/// <c>ReplaceElement</c> is one change over the element's own span — so a wrap cannot half-happen,
/// and an undo takes the whole thing back.
/// </para>
/// </remarks>
public sealed partial class DesignerViewModel
{
    /// <summary>What a control can be wrapped in, which is the containers the toolbox offers.</summary>
    public static IReadOnlyList<string> WrapContainers { get; } =
        ["StackPanel", "Grid", "Border", "ScrollViewer"];

    /// <summary>Moves the selection one place earlier among its siblings.</summary>
    public RelayCommand MoveUpCommand { get; private set; } = null!;

    /// <summary>And one place later.</summary>
    public RelayCommand MoveDownCommand { get; private set; } = null!;

    private void InitialiseStructure()
    {
        MoveUpCommand = new RelayCommand(
            () => RunDetached(() => MoveSelectedAsync(-1)),
            () => CanMove(-1));

        MoveDownCommand = new RelayCommand(
            () => RunDetached(() => MoveSelectedAsync(+1)),
            () => CanMove(+1));
    }

    private bool CanMove(int direction)
    {
        if (Selected is not { } element || IsRoot(element) || element.Parent is not XamlElement parent)
        {
            return false;
        }

        int index = element.IndexInContent;

        return direction < 0
            ? index > 0
            : index >= 0 && index < parent.ContentElements.Count() - 1;
    }

    /// <summary>
    /// Moves the selected element one place among its siblings.
    /// </summary>
    /// <remarks>
    /// The target index is counted in the document as it stands — with the element still in it,
    /// because that is the list <c>InsertElement</c> reads. One earlier is the earlier sibling's
    /// own position; one later is <em>two</em> past the current one, which is "after the next
    /// sibling" in a list that still contains the element being moved.
    /// </remarks>
    private async Task MoveSelectedAsync(int direction)
    {
        if (ActiveForm is not { } form
            || Selected is not { } element
            || IsRoot(element)
            || element.Parent is not XamlElement parent)
        {
            return;
        }

        int index = element.IndexInContent;

        if (index < 0 || !CanMove(direction))
        {
            return;
        }

        // The parent survives the edit as a position even though it does not survive as an object,
        // which is what lets the moved element be found again for the selection.
        XamlElementPath parentPath = XamlElementPath.Of(parent);
        int landed = direction < 0 ? index - 1 : index + 1;

        await ApplyAsync(
            form,
            editor => editor.MoveElement(element, parent, direction < 0 ? index - 1 : index + 2),
            direction < 0 ? "move up" : "move down");

        // The selection follows the element to where it landed. The apply path's own restore is by
        // position, and the position it remembered is the one the element just left — resolving it
        // would select the sibling that took the old place.
        if (form.Document is { } document
            && parentPath.Resolve(document) is { } landedParent
            && landedParent.ContentElements.ElementAtOrDefault(landed) is { } moved)
        {
            Reselect(form, moved);
        }
    }

    /// <summary>Puts a container around the selected element. Called by the view.</summary>
    public void Wrap(string container)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);

        RunDetached(async () =>
        {
            if (ActiveForm is not { } form
                || Selected is not { } element
                || IsRoot(element)
                || MarkupOf(element) is not { Length: > 0 } markup)
            {
                return;
            }

            // One replacement over the element's own span: its position among its siblings and the
            // whitespace around it stay put, and the wrap is one step of the history.
            await ApplyAsync(
                form,
                editor => editor.ReplaceElement(element, $"<{container}>{markup}</{container}>"),
                $"wrap in {container}");

            Log($"Wrapped in {container}.");
        });
    }

    /// <summary>
    /// Removes the selected container, lifting its children into its place. Called by the view.
    /// </summary>
    /// <remarks>
    /// The one structural edit with a validity question: a parent that holds a single child — a
    /// Border, a Window — cannot take three lifted grandchildren. That case is refused in words
    /// rather than written into the file for the loader to refuse later. Whatever the container
    /// carried beyond its content — its own attributes, its row definitions — goes with it, because
    /// removing the container is what was asked.
    /// </remarks>
    public void Unwrap()
    {
        RunDetached(async () =>
        {
            if (ActiveForm is not { } form || Selected is not { } element || IsRoot(element))
            {
                return;
            }

            List<XamlElement> children = [.. element.ContentElements];

            if (children.Count == 0)
            {
                await DeleteAsync(form, element);

                return;
            }

            if (children.Count > 1 && LiveSelection()?.Parent is not Avalonia.Controls.Panel)
            {
                Log($"! {element.Name} holds {children.Count} children and its parent holds one — unwrap would not load");

                return;
            }

            string lifted = string.Join(
                System.Environment.NewLine,
                children.Select(MarkupOf).Where(static text => text is { Length: > 0 }));

            await ApplyAsync(form, editor => editor.ReplaceElement(element, lifted), "unwrap");

            Log($"Unwrapped {element.Name}.");
        });
    }
}
