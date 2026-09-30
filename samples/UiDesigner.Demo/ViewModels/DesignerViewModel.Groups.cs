using System;
using System.Collections.Generic;
using System.Linq;
using ArxisStudio.Markup.Xaml;
using Avalonia.Controls;

namespace UiDesigner.Demo.ViewModels;

/// <summary>
/// Groups, and where a group is written down.
/// </summary>
/// <remarks>
/// <para>
/// A group is a mark on the controls rather than a node in the tree — the editor does not write the
/// tree, and a container it invented would change what the canvas shows without changing the file.
/// The mark is a path, outermost group first, and the editor keeps it in an attached property.
/// </para>
/// <para>
/// <b>Where it is written is the whole design decision.</b> The attached property belongs to
/// <c>ArxisStudio.DesignEditor</c>, and a project being edited has never heard of that assembly: an
/// <c>xmlns</c> naming it would compile here, where the studio has it loaded, and fail the user's
/// own build with an unresolvable assembly. So the mark goes in the design-time namespace, beside
/// the <c>d:DesignWidth</c> every template already carries — attributes the XAML compiler skips
/// because <c>mc:Ignorable</c> says to. The user's application builds and runs exactly as before,
/// and the mark survives in the file.
/// </para>
/// <para>
/// The cost of that choice, stated rather than discovered: the runtime loader skips those
/// attributes too, so the live control does not receive the group by being loaded. The designer
/// puts it back after every load — see <see cref="FormViewModel.MarkGroups"/> — which is the same
/// arrangement <c>Layout.IsTracked</c> is under.
/// </para>
/// </remarks>
public sealed partial class DesignerViewModel
{
    /// <summary>The design-time attribute a group is written as.</summary>
    private const string GroupAttribute = "DesignGroup";

    /// <summary>Asks the canvas to group or ungroup, because the editor decides what a group is.</summary>
    /// <remarks>
    /// The view supplies these: grouping is a question about the selection, which lives on the
    /// canvas, and the answer to "can this be grouped" is the editor's own. The commands exist so
    /// that a keyboard shortcut and a menu entry reach the same place.
    /// </remarks>
    public Func<bool>? GroupSelection { get; set; }

    public Func<bool>? UngroupSelection { get; set; }

    public RelayCommand GroupCommand { get; private set; } = null!;

    public RelayCommand UngroupCommand { get; private set; } = null!;

    private void InitialiseGroups()
    {
        GroupCommand = new RelayCommand(() => GroupSelection?.Invoke(), () => GroupSelection is not null);
        UngroupCommand = new RelayCommand(() => UngroupSelection?.Invoke(), () => UngroupSelection is not null);
    }

    /// <summary>Writes the marks a grouping produced, or takes them away.</summary>
    /// <remarks>
    /// <b>All of them in one edit.</b> A group is several controls changing together, and applying
    /// them one at a time does not work at all: the first edit produces a new document and a new
    /// live tree, and the controls the remaining changes name belong to the tree that has just been
    /// replaced — so the second mark lands nowhere and says nothing. One edit is also what makes
    /// grouping one step of history, which is what undo has to take back.
    /// </remarks>
    public void WriteGroups(FormViewModel form, IReadOnlyList<(Control Control, string? Id, string? Was)> marks)
    {
        if (marks.Count == 0)
        {
            return;
        }

        RunDetached(() => ApplyAsync(
            form,
            editor =>
            {
                // Once for the edit, not once per control. The check reads the document as it was
                // when the edit began — an editor does not re-parse between calls — so asking it
                // again for the second control answers "not declared" and writes the declaration a
                // second time. Two `xmlns:d` on one element is a document that will not parse, and
                // the form goes blank on the next load.
                Declare(editor, form.Document?.Root);

                var handled = new HashSet<XamlElement>();
                var retired = new HashSet<string>(StringComparer.Ordinal);

                foreach ((Control control, string? id, string? was) in marks)
                {
                    if (form.Objects?.GetElement(control) is not { } element)
                    {
                        Log($"! {control.GetType().Name} has nothing in the document behind it — "
                            + "group not written");

                        continue;
                    }

                    XamlAttribute? existing = DesignAttribute(element, GroupAttribute);

                    handled.Add(element);

                    if (string.IsNullOrEmpty(id))
                    {
                        if (existing is not null)
                        {
                            editor.RemoveAttribute(element, existing.Name);
                        }

                        if (!string.IsNullOrEmpty(was))
                        {
                            retired.Add(was);
                        }

                        continue;
                    }

                    editor.SetAttribute(
                        element,
                        existing?.Name
                            ?? XamlQualifiedName.Parse(DesignPrefix(element) + ":" + GroupAttribute),
                        id);
                }

                // And every other element the document still marks with a group that has just been
                // taken off, whether or not the editor named it. The editor reports the controls it
                // knows are members; the document is what a file carries, and a group half-removed
                // from it comes back on the next load and takes the clicks inside it with it.
                //
                // Once, after the loop. A sweep per control would try to remove the same attribute
                // twice in one edit, and two edits over one span is refused — which is the honest
                // answer to a caller asking for something incoherent.
                Erase(editor, form.Document?.Root, retired, handled);
            },
            marks[0].Id is null or "" ? "ungroup" : "group"));
    }

    /// <summary>Takes a group id off every element that still carries it.</summary>
    /// <remarks>
    /// The one place that knows a group is a fact about the document rather than about whichever
    /// controls the editor happened to report.
    /// </remarks>
    private static void Erase(
        XamlDocumentEditor editor,
        XamlElement? root,
        HashSet<string> retired,
        HashSet<XamlElement> handled)
    {
        if (root is null || retired.Count == 0)
        {
            return;
        }

        foreach (XamlElement element in root.DescendantElements().Prepend(root))
        {
            if (handled.Contains(element))
            {
                continue;
            }

            if (DesignAttribute(element, GroupAttribute) is { } mark
                && mark.GetValueText() is { } value
                && retired.Contains(value))
            {
                editor.RemoveAttribute(element, mark.Name);
            }
        }
    }

    /// <summary>The design-namespace attribute of this name, whatever prefix the document gave it.</summary>
    private static XamlAttribute? DesignAttribute(XamlElement element, string name) =>
        element.Attributes.FirstOrDefault(attribute =>
            attribute.Name.LocalName == name
            && element.NamespaceContext.LookupNamespace(attribute.Name.Prefix) == XamlNamespaces.Design);

    /// <summary>The prefix this document binds the design-time namespace to, or the usual one.</summary>
    private static string DesignPrefix(XamlElement element) =>
        element.NamespaceContext.Declarations
            .Where(declaration => declaration.Value == XamlNamespaces.Design)
            .Select(declaration => declaration.Key)
            .FirstOrDefault()
        ?? "d";

    /// <summary>
    /// Makes sure the document can carry a design-time attribute at all.
    /// </summary>
    /// <remarks>
    /// Every template writes both declarations already, so this is usually a no-op — but a document
    /// written by hand may have neither, and an attribute in a namespace the compiler cannot resolve
    /// is a build error in somebody else's project. The prefix has to be listed in
    /// <c>mc:Ignorable</c> as well as declared: that list is what tells the compiler to skip it.
    /// </remarks>
    private static void Declare(XamlDocumentEditor editor, XamlElement? root)
    {
        if (root is null)
        {
            return;
        }

        string prefix = DesignPrefix(root);

        if (root.NamespaceContext.LookupNamespace(prefix) != XamlNamespaces.Design)
        {
            editor.SetAttribute(
                root, XamlQualifiedName.Parse("xmlns:" + prefix), XamlNamespaces.Design);
        }

        string? compatibility = root.NamespaceContext.Declarations
            .Where(declaration => declaration.Value == XamlNamespaces.MarkupCompatibility)
            .Select(declaration => declaration.Key)
            .FirstOrDefault();

        if (compatibility is null)
        {
            compatibility = "mc";

            editor.SetAttribute(
                root,
                XamlQualifiedName.Parse("xmlns:" + compatibility),
                XamlNamespaces.MarkupCompatibility);
        }

        XamlAttribute? ignorable = root.Attributes.FirstOrDefault(attribute =>
            attribute.Name.LocalName == "Ignorable"
            && root.NamespaceContext.LookupNamespace(attribute.Name.Prefix)
                == XamlNamespaces.MarkupCompatibility);

        string listed = ignorable?.GetValueText() ?? string.Empty;

        if (listed.Split(' ').Contains(prefix))
        {
            return;
        }

        editor.SetAttribute(
            root,
            ignorable?.Name ?? XamlQualifiedName.Parse(compatibility + ":Ignorable"),
            listed.Length == 0 ? prefix : listed + " " + prefix);
    }
}
