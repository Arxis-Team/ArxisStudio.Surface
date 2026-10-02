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
/// </remarks>
internal sealed class FormRootAccess(UiDesignerFormItem card) : IXamlRootAccess
{
    public IDisposable Lend(object root) =>
        ReferenceEquals(card.Root, root) ? card.SuspendRoot() : Nothing.Instance;

    /// <summary>The lease of a root the card does not hold any more.</summary>
    private sealed class Nothing : IDisposable
    {
        public static Nothing Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
