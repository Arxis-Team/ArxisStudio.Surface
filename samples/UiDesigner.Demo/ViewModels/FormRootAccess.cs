using System;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.Surface.UiDesigner;
using Avalonia.Controls;

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
/// The session asks for the root around every write it makes, on the objects' thread, and this
/// answers by letting the card give everything back for exactly that long. Taking it again is what
/// borrows the tree the write has just built. A root that is a control stands on the card as it is —
/// nothing is borrowed from it, so nothing is given back.
/// </para>
/// </remarks>
internal sealed class FormRootAccess(UiDesignerFormItem card) : IXamlRootAccess
{
    public IDisposable Lend(object root)
    {
        if (root is not TopLevel || !ReferenceEquals(card.Root, root))
        {
            return Nothing.Instance;
        }

        card.Root = null;

        return new Lease(card, root);
    }

    /// <summary>Borrows the root again once the session has written it.</summary>
    private sealed class Lease(UiDesignerFormItem card, object root) : IDisposable
    {
        private bool _returned;

        public void Dispose()
        {
            if (_returned)
            {
                return;
            }

            _returned = true;

            // Unless somebody has put another root on the card meanwhile — the newer truth.
            if (card.Root is null)
            {
                card.Root = root;
            }
        }
    }

    /// <summary>The lease of a root nothing was borrowed from.</summary>
    private sealed class Nothing : IDisposable
    {
        public static Nothing Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
