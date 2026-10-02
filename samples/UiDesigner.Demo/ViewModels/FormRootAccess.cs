using System;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.Surface.UiDesigner;

namespace UiDesigner.Demo.ViewModels;

/// <summary>
/// Gives a window-rooted form's content back to its session for each write, and lets the card take
/// it again afterwards.
/// </summary>
/// <remarks>
/// <para>
/// A window cannot be the content of anything, so the card shows one by borrowing its content,
/// resources and styles. While they are borrowed the window the session is built around is empty: an
/// update wrote the rebuilt content into a window nobody looks at, and the map, rebuilt by walking the
/// window, came back holding the window and its template and nothing else — so every drop after the
/// first had no container to land in.
/// </para>
/// <para>
/// The session asks for the root around every write it makes, on the objects' thread, and the card
/// answers with <see cref="UiDesignerFormItem.SuspendRoot"/>: it gives back exactly what it borrowed
/// and takes whatever the window holds when the lease ends, so a write that replaced the content
/// reaches the canvas by itself. The root, the size, the title and the theme stay as they are — the
/// card is not let go and taken again, which used to blink the form on every keystroke typed in the
/// inspector. A root that is a control stands on the card as it is and has nothing to give back.
/// </para>
/// <para>
/// One per document, not per card. The design host keeps it with the document for every session it
/// builds, and the card a form is shown on changes when the project's types are swapped: the forms
/// are let go of and built again, and the new form's card is pointed at here
/// (<see cref="Card"/>). Between the two it points at nothing, so it holds no card the swap is
/// letting go of.
/// </para>
/// </remarks>
internal sealed class FormRootAccess : IXamlRootAccess
{
    /// <summary>Gets or sets the card the document is shown on now, or <see langword="null"/> between two.</summary>
    public UiDesignerFormItem? Card { get; set; }

    public IDisposable Lend(object root) =>
        Card is { } card && ReferenceEquals(card.Root, root) ? card.SuspendRoot() : Nothing.Instance;

    /// <summary>The lease of a root no card holds.</summary>
    private sealed class Nothing : IDisposable
    {
        public static Nothing Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
