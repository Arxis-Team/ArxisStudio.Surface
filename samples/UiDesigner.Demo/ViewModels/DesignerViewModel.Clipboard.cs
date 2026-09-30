using System;
using System.Threading.Tasks;
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

    /// <summary>The markup an element is written as, which is what the clipboard carries.</summary>
    private string? MarkupOf(XamlElement element) =>
        ActiveForm?.Document?.SourceText.ToString() is { } text
            && element.Span.End <= text.Length
                ? text[element.Span.Start..element.Span.End]
                : null;

    private async Task CopyAsync()
    {
        if (Selected is { } element && MarkupOf(element) is { Length: > 0 } markup && PutOnClipboard is { } put)
        {
            await put(markup);

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

    /// <summary>
    /// The markup of a copy, which is the markup of the original without the names in it.
    /// </summary>
    /// <remarks>
    /// A name identifies one control in one form: two controls called <c>GoButton</c> is not a form
    /// with two buttons, it is a form the loader refuses — and the refusal arrives as an update the
    /// live tree could not follow, long after the paste looked like it had worked. Rider drops the
    /// name for the same reason. Every element in the fragment is stripped, not just its root,
    /// because a copied panel carries its children's names with it.
    /// <para>
    /// Parsed and edited rather than pattern-matched out of the text: <c>x:Name</c> can be written
    /// with any prefix bound to the XAML namespace, and the editor already knows how to take an
    /// attribute out without disturbing anything around it.
    /// </para>
    /// </remarks>
    private static string WithoutNames(string markup)
    {
        try
        {
            XamlDocument fragment = XamlDocument.Parse(markup);

            if (fragment.Root is not { } root)
            {
                return markup;
            }

            XamlDocumentEditor editor = fragment.Edit();

            Strip(root);

            return editor.HasChanges ? editor.Apply().SourceText.ToString() : markup;

            void Strip(XamlElement element)
            {
                if (element.GetDirectiveAttribute("Name") is { } named)
                {
                    editor.RemoveAttribute(element, named.Name);
                }

                foreach (XamlElement child in element.ContentElements)
                {
                    Strip(child);
                }
            }
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            // A fragment this cannot parse is a fragment somebody put on the clipboard by hand. It
            // goes in as it stands and the loader says what is wrong with it.
            return markup;
        }
    }

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

        await ApplyAsync(
            form,
            editor => editor.InsertElement(parent, index, WithoutNames(text.Trim())),
            "paste");

        Log($"Pasted into {parent.Name}.");
    }

    private async Task DuplicateAsync()
    {
        if (ActiveForm is not { Document.Root: { } root } form
            || Selected is not { } element
            || IsRoot(element))
        {
            return;
        }

        if (MarkupOf(element) is not { Length: > 0 } markup)
        {
            return;
        }

        XamlElement parent = element.Parent as XamlElement ?? root;
        int index = element.IndexInContent;

        await ApplyAsync(
            form,
            editor => editor.InsertElement(
                parent, index < 0 ? IndexAtEnd(parent) : index + 1, WithoutNames(markup)),
            $"duplicate {element.Name}");

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
