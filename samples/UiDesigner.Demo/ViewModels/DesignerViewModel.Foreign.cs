using System;
using System.Collections.Generic;
using System.Linq;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;

namespace UiDesigner.Demo.ViewModels;

/// <summary>
/// The pairing for controls the object map deliberately refuses: a project's own control, placed
/// on another form.
/// </summary>
/// <remarks>
/// <para>
/// The map pairs objects by where Avalonia says they were built, and an embedded
/// <c>&lt;views:MyControl /&gt;</c> answers with the wrong document: its own
/// <c>InitializeComponent</c> loads <c>MyControl.axaml</c> over the instance, and from then on the
/// instance says it came from there. The map is right to refuse that answer — mapping it would
/// claim another document's objects as this one's — but the refusal left the control selectable as
/// nothing at all: a click walked past it to the window, the tree said Window, and delete deleted
/// nothing.
/// </para>
/// <para>
/// What the map cannot deduce, the structure still states. The embedded control is an authored
/// child of a control the map does know — its panel's children, its decorator's child, its
/// window's content — and the document says the same thing in the same order. Matching the two by
/// position, with the type name as the witness, recovers exactly the pairs the source stamp lost,
/// and only those: the control's own internals have no elements in this document to pair with, so
/// the walk inside it still finds nothing, which is what makes clicking its interior select the
/// control itself.
/// </para>
/// </remarks>
public sealed partial class DesignerViewModel
{
    /// <summary>The element behind a control: the map's answer, or the structure's.</summary>
    /// <remarks>
    /// The map's answer is taken only when it round-trips. Asked about a control built by another
    /// document — anything inside an embedded <c>MyControl</c> — it answers with this document's
    /// root, which is true about provenance and useless as identity: a click on a button inside the
    /// embedded control selected the window. The pair that leads back to the same object is the one
    /// the document actually declared; everything else falls through to the structure below.
    /// </remarks>
    internal static XamlElement? ElementBehind(XamlObjectMap map, Control control)
    {
        if (map.GetElement(control) is { } mapped
            && ReferenceEquals(map.GetObject(mapped), control)
            && Names(control, mapped))
        {
            return mapped;
        }

        if (control.Parent is not Control parent
            || ElementBehind(map, parent) is not { } parentElement)
        {
            return null;
        }

        int index = IndexAmong(AuthoredChildren(parent), control);

        if (index < 0)
        {
            return null;
        }

        XamlElement? candidate = parentElement.ContentElements.ElementAtOrDefault(index);

        // The type name is the witness that position told the truth: a candidate that is not even
        // the control's type is a mismatch, and no answer is better than a wrong element.
        return candidate is not null && Names(control, candidate) ? candidate : null;
    }

    /// <summary>The control behind an element, when the map has no answer for it.</summary>
    internal static Control? ObjectBehind(XamlObjectMap map, XamlElement element)
    {
        if (map.GetObject(element) is Control mapped)
        {
            return mapped;
        }

        if (element.Parent is not XamlElement parentElement
            || ObjectBehind(map, parentElement) is not { } parent)
        {
            return null;
        }

        int index = 0;

        foreach (XamlElement sibling in parentElement.ContentElements)
        {
            if (ReferenceEquals(sibling, element))
            {
                break;
            }

            index++;
        }

        Control? candidate = AuthoredChildren(parent).ElementAtOrDefault(index);

        return candidate is not null && Names(candidate, element) ? candidate : null;
    }

    /// <summary>
    /// Whether an element could have produced this control — its type, or one it derives from.
    /// </summary>
    /// <remarks>
    /// The round trip alone is not enough of a witness. Asked about a panel inside an embedded
    /// control the map answers with this document's root and answers back with the panel, so the
    /// pair agrees with itself and means nothing; a Window element that claims a StackPanel is the
    /// shape of that. Derivation is what makes it hold for the case it must: an <c>x:Class</c> root
    /// is written <c>&lt;Window&gt;</c> and built as <c>MainWindow</c>.
    /// </remarks>
    private static bool Names(Control control, XamlElement element)
    {
        for (Type? type = control.GetType(); type is not null; type = type.BaseType)
        {
            if (type.Name == element.Name.LocalName)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The children a document would list for this control, in the document's order.
    /// </summary>
    /// <remarks>
    /// The same relations the markup states: a panel's children, a decorator's child, content when
    /// content is a control. Items controls are left out on purpose — their items are elements the
    /// map already pairs by source, and their containers are generated, not authored.
    /// </remarks>
    private static IEnumerable<Control> AuthoredChildren(Control control)
    {
        switch (control)
        {
            case Panel panel:
                foreach (Control child in panel.Children)
                {
                    yield return child;
                }

                break;

            case Decorator { Child: { } decorated }:
                yield return decorated;
                break;

            case ContentControl { Content: Control content }:
                yield return content;
                break;

            case ContentPresenter { Content: Control presented }:
                yield return presented;
                break;

            default:
                break;
        }
    }

    private static int IndexAmong(IEnumerable<Control> children, Control control)
    {
        var index = 0;

        foreach (Control child in children)
        {
            if (ReferenceEquals(child, control))
            {
                return index;
            }

            index++;
        }

        return -1;
    }
}
