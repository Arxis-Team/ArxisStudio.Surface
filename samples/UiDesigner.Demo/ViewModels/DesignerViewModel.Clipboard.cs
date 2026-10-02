using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using ArxisStudio.Markup;
using ArxisStudio.Markup.Xaml;

namespace UiDesigner.Demo.ViewModels;

/// <summary>
/// Copy, cut, paste and duplicate, which are how a form gets laid out once it has one of anything.
/// </summary>
/// <remarks>
/// <para>
/// A control is copied as the markup that declares it, exactly as the file has it — attributes,
/// children, formatting and all. That is what makes the clipboard useful between this designer and
/// a text editor, and between two forms: what leaves here is what a person would have typed.
/// </para>
/// <para>
/// The clipboard itself belongs to the view. A view model that reached for the top level's
/// clipboard would be a view model that needs a window to be tested.
/// </para>
/// </remarks>
public sealed partial class DesignerViewModel
{
    /// <summary>Puts markup on the system clipboard. Supplied by the view.</summary>
    public Func<string, Task>? PutOnClipboard { get; set; }

    /// <summary>Takes text off it. Supplied by the view.</summary>
    public Func<Task<string?>>? TakeFromClipboard { get; set; }

    public RelayCommand CopyCommand { get; private set; } = null!;

    public RelayCommand CutCommand { get; private set; } = null!;

    public RelayCommand PasteCommand { get; private set; } = null!;

    public RelayCommand DuplicateCommand { get; private set; } = null!;

    private void InitialiseClipboard()
    {
        CopyCommand = new RelayCommand(() => RunDetached(CopyAsync), () => Selected is not null);

        CutCommand = new RelayCommand(
            () => RunDetached(CutAsync),
            () => Selected is not null && !IsRoot(Selected));

        PasteCommand = new RelayCommand(() => RunDetached(PasteAsync), () => ActiveForm is not null);

        DuplicateCommand = new RelayCommand(
            () => RunDetached(DuplicateAsync),
            () => Selected is not null && !IsRoot(Selected));
    }

    /// <summary>Whether this element is the document's root, which has no sibling and no parent.</summary>
    private bool IsRoot(XamlElement element) => ReferenceEquals(element, ActiveForm?.Document?.Root);

    /// <summary>
    /// Puts the selection on the clipboard as markup that stands on its own.
    /// </summary>
    /// <remarks>
    /// The element as the file writes it, with the namespace declarations its names need on its start
    /// tag (Markup's <see cref="XamlFragment"/>): <c>local:Badge</c> copied without them names whatever
    /// the receiving document binds <c>local</c> to, which is how a paste turns into a different
    /// control or into markup that does not load. Pasted into a text editor it is still what a person
    /// would have typed.
    /// </remarks>
    private async Task CopyAsync()
    {
        if (Selected is { } element && PutOnClipboard is { } put)
        {
            await put(XamlFragment.From(element).ToXamlText());

            Log($"Copied {element.Name}.");
        }
    }

    private async Task CutAsync()
    {
        if (ActiveForm is not { } form || Selected is not { } element)
        {
            return;
        }

        await CopyAsync();
        await DeleteAsync(form, element);
    }

    /// <summary>
    /// Puts what is on the clipboard into the form.
    /// </summary>
    /// <remarks>
    /// Where it goes is the same question a drop asks, and it has the same answer: into the
    /// selection when the selection can hold children, and beside it otherwise. Pasting into a
    /// Button would mean replacing its content, which is not what anybody means by paste.
    /// </remarks>
    private async Task PasteAsync() => await PasteCoreAsync();

    private async Task PasteCoreAsync()
    {
        if (ActiveForm is not { Document.Root: { } root } form || TakeFromClipboard is not { } take)
        {
            return;
        }

        if (await take() is not { Length: > 0 } text || text.TrimStart() is not ['<', ..])
        {
            Log("! the clipboard does not hold markup");

            return;
        }

        (XamlElement parent, int index) = Landing(root);

        XamlFragment fragment = XamlFragment.Parse(text, sourceUri: null);
        ImmutableArray<MarkupDiagnostic> found = [];

        // Written in this document's namespaces — declared on the root where they are missing,
        // renamed only where a prefix means something else here — and without the names it carries:
        // two controls called GoButton is a form the loader refuses, and Rider drops the name too.
        await ApplyAsync(
            form,
            editor =>
            {
                editor.InsertFragment(parent, index, fragment, XamlDuplicateNames.Remove);

                found = editor.Diagnostics;
            },
            "paste");

        foreach (MarkupDiagnostic diagnostic in found)
        {
            Log(diagnostic.IsError
                ? $"! {diagnostic.Code}: {diagnostic.Message}"
                : $"  {diagnostic.Code}: {diagnostic.Message}");
        }

        if (!found.Any(static diagnostic => diagnostic.IsError))
        {
            Log($"Pasted into {parent.Name}.");
        }
    }

    private async Task DuplicateAsync()
    {
        if (ActiveForm is not { } form
            || Selected is not { IsPropertyElementSyntax: false } element
            || IsRoot(element))
        {
            return;
        }

        // Straight after the original, written as it is, with the names inside the copy taken out.
        await ApplyAsync(form, editor => editor.DuplicateElement(element), $"duplicate {element.Name}");

        Log($"Duplicated {element.Name}.");
    }

    /// <summary>Where a paste lands: inside the selection when it can hold children, beside it when not.</summary>
    private (XamlElement Parent, int Index) Landing(XamlElement root)
    {
        if (Selected is not { } element)
        {
            return (root, IndexAtEnd(root));
        }

        if (LiveSelection() is { } live && CanHoldChildren(live))
        {
            return (element, IndexAtEnd(element));
        }

        return element.Parent is XamlElement parent
            ? (parent, element.IndexInContent < 0 ? IndexAtEnd(parent) : element.IndexInContent + 1)
            : (root, IndexAtEnd(root));
    }
}
