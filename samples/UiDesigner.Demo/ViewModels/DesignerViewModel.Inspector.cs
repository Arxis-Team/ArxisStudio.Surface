using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using Avalonia.Controls;
using Avalonia.Media;

namespace UiDesigner.Demo.ViewModels;

public sealed partial class DesignerViewModel
{
    /// <summary>What the selected element sets, and nothing it does not.</summary>
    public ObservableCollection<PropertyRow> Properties { get; } = [];

    /// <summary>The same rows under the design's headings.</summary>
    public ObservableCollection<PropertyGroup> PropertyGroups { get; } = [];

    /// <summary>The selected element's type, for the inspector's header.</summary>
    public string SelectedType => Selected is null ? string.Empty : Selected.Name.LocalName;

    /// <summary>
    /// The namespace the selected control's type lives in, which the design shows beside the type.
    /// </summary>
    /// <remarks>
    /// Read off the live object rather than the document, because the document says a prefix and a
    /// local name and the answer to "which Button is this" is the resolved type. A document naming
    /// a type nothing resolved has no namespace to show, and shows none.
    /// </remarks>
    public string SelectedNamespace => LiveSelection()?.GetType().Namespace ?? string.Empty;

    /// <summary>The type and its namespace, the way the design writes them under the name.</summary>
    public string SelectedTypeLine => SelectedNamespace is { Length: > 0 } space
        ? $"{SelectedType} · {space}"
        : SelectedType;

    public string SelectedQualifier => Selected is null
        ? "nothing selected"
        : Selected.Name.Prefix is { Length: > 0 } prefix
            ? $"{Selected.Name.LocalName} · {prefix}"
            : $"{Selected.Name.LocalName} · Avalonia.Controls";

    public Geometry SelectedGlyph => Glyphs.For(Selected?.Name.LocalName);

    /// <summary>
    /// The chips the design puts under the header: what the element declares beyond its properties.
    /// </summary>
    /// <remarks>
    /// Directives and classes, because those are the facts about an element that are not values —
    /// <c>x:Name</c> says it can be found from code, a class says a style may reach it. The design
    /// draws them as chips precisely because they are not editable in the rows below.
    /// </remarks>
    public ObservableCollection<string> SelectedChips { get; } = [];

    /// <summary>
    /// Which of the design's three sections a property belongs to.
    /// </summary>
    /// <remarks>
    /// A lookup rather than reflection over the type. The inspector lists what the document sets, and
    /// what the document sets is a name — so the grouping is a fact about the name, and a property
    /// this table has never heard of goes to Content rather than being hidden.
    /// </remarks>
    /// <summary>
    /// The properties an inspector offers for a control, in the design's groups and order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to show only what the document had written, on the grounds that a Button has
    /// upwards of a hundred properties and listing them tells nobody which matter. The first half
    /// was right and the conclusion was wrong: a designer whose inspector is empty until you type
    /// the property name yourself is not an inspector, it is a text editor with extra steps.
    /// </para>
    /// <para>
    /// So there is a list, and it is short on purpose. Every name here is asked of the control
    /// before it is offered — <c>CornerRadius</c> appears for a Border and not for a TextBlock —
    /// so the list is a candidate set and the control decides. Anything the document set that is
    /// not in it is appended, because an inspector that hides what the file says is worse than one
    /// that shows too much.
    /// </para>
    /// </remarks>
    private static readonly (string Group, string[] Names)[] Catalogue =
    [
        // A Window's own facts come first because they are the first thing a person sets on one.
        // Title resolves only on a Window, so every other control never sees this group.
        ("Window",
        [
            "Title", "CanResize", "Topmost", "WindowStartupLocation",
        ]),

        ("Layout",
        [
            "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight",
            "Margin", "Padding",
            "HorizontalAlignment", "VerticalAlignment",
            "HorizontalContentAlignment", "VerticalContentAlignment",
            "Orientation", "Spacing", "ZIndex",
            "RowDefinitions", "ColumnDefinitions",
        ]),

        ("Appearance",
        [
            "Background", "Foreground", "BorderBrush", "BorderThickness", "CornerRadius",
            "FontSize", "FontWeight", "FontStyle", "FontFamily",
            "TextWrapping", "TextAlignment", "Stretch", "Opacity",
        ]),

        // PlaceholderText and not Watermark: both resolve on a TextBox, but Watermark is the
        // obsolete spelling, and an inspector that offers it writes deprecated API into the
        // user's file — twice, beside the same fact under its current name.
        ("Content & Interaction",
        [
            "Content", "Header", "Text", "PlaceholderText", "Source",
            "Command", "CommandParameter", "HotKey", "ToolTip.Tip",
            "IsEnabled", "IsVisible", "IsChecked", "IsReadOnly", "AcceptsReturn", "MaxLength",
            "IsDefault", "IsCancel",
            "Minimum", "Maximum", "Value", "SelectedIndex",
        ]),
    ];

    /// <summary>
    /// The attached properties a control's parent gives it, which belong to the child's inspector.
    /// </summary>
    /// <remarks>
    /// <c>Canvas.Left</c> is a property of the button, not of the canvas, and it exists only while
    /// the button is in one. Offering it by the parent's type is the difference between an inspector
    /// that knows where the control lives and a list of everything anybody could ever attach.
    /// </remarks>
    private static readonly (Type Parent, string[] Names)[] Attached =
    [
        (typeof(Canvas), ["Canvas.Left", "Canvas.Top"]),
        (typeof(Grid), ["Grid.Row", "Grid.Column", "Grid.RowSpan", "Grid.ColumnSpan"]),
        (typeof(DockPanel), ["DockPanel.Dock"]),
    ];

    /// <summary>How a document names a control.</summary>
    private const string NameDirective = "x:Name";

    private static string GroupOf(string name) => name switch
    {
        NameDirective => "Identity",

        "Title" or "CanResize" or "Topmost" or "WindowStartupLocation" => "Window",

        "Width" or "Height" or "MinWidth" or "MinHeight" or "MaxWidth" or "MaxHeight"
            or "Margin" or "Padding" or "HorizontalAlignment" or "VerticalAlignment"
            or "HorizontalContentAlignment" or "VerticalContentAlignment"
            or "Dock" or "Spacing" or "Orientation" or "ZIndex"
            or "RowDefinitions" or "ColumnDefinitions" => "Layout",

        "Background" or "Foreground" or "BorderBrush" or "BorderThickness" or "CornerRadius"
            or "FontSize" or "FontWeight" or "FontFamily" or "FontStyle" or "Opacity"
            or "TextWrapping" or "TextAlignment" or "Stretch"
            or "BoxShadow" or "Classes" => "Appearance",

        _ when name.StartsWith("Canvas.", StringComparison.Ordinal)
            || name.StartsWith("Grid.", StringComparison.Ordinal)
            || name.StartsWith("DockPanel.", StringComparison.Ordinal) => "Layout",

        _ => "Content & Interaction",
    };

    /// <summary>The property to add, typed by hand, because a full member list needs reflection.</summary>
    /// <remarks>
    /// A designer with the project's assemblies loaded could offer every settable property of the
    /// selected type — the assembly context is right there. It is not done here because the useful
    /// version of that is a typed editor per property kind, which is a feature rather than a
    /// demonstration; a name and a value is enough to show that the edit reaches the file.
    /// </remarks>
    public string NewPropertyName
    {
        get;
        set => Set(ref field, value);
    } = string.Empty;

    public string NewPropertyValue
    {
        get;
        set => Set(ref field, value);
    } = string.Empty;

    public RelayCommand AddPropertyCommand { get; private set; } = null!;

    public RelayCommand DeleteSelectedCommand { get; private set; } = null!;

    private void InitialiseInspector()
    {
        AddPropertyCommand = new RelayCommand(
            () => Run(AddPropertyAsync),
            () => Selected is not null && NewPropertyName.Length > 0);

        DeleteSelectedCommand = new RelayCommand(
            () => Run(async () =>
            {
                if (ActiveForm is { } form && Selected is { } element)
                {
                    await DeleteAsync(form, element);
                }
            }),
            () => Selected is not null && ActiveForm is not null);
    }

    /// <summary>
    /// What the inspector's rows are filtered by.
    /// </summary>
    /// <remarks>
    /// The inspector grew long the day it started offering the standard properties, and a person
    /// looking for Margin should type M rather than scroll. The filter is over the visible heading
    /// and the written name both, so "x:Name" is found under "name" as well as under its directive.
    /// </remarks>
    public string InspectorFilter
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                BuildInspector();
            }
        }
    } = string.Empty;

    private bool MatchesInspectorFilter(PropertyRow row) =>
        InspectorFilter is not { Length: > 0 } filter
            || row.Heading.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || row.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private void BuildInspector()
    {
        Properties.Clear();
        PropertyGroups.Clear();
        ClearData();

        Raise(nameof(SelectedType));
        Raise(nameof(SelectedQualifier));
        Raise(nameof(SelectedTypeLine));
        Raise(nameof(SelectedGlyph));

        SelectedChips.Clear();

        if (Selected is not { } element)
        {
            return;
        }

        foreach (XamlAttribute directive in element.Directives)
        {
            SelectedChips.Add(directive.Name.ToString());
        }

        if (element.Attributes.FirstOrDefault(
            a => a.Name.IsUnprefixed("Classes")) is { } classes)
        {
            foreach (string name in classes.GetValueText().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                SelectedChips.Add("." + name);
            }
        }

        Control? live = LiveSelection();
        XamlLoadSession? session = ActiveForm?.Session;

        var written = new Dictionary<string, XamlAttribute>(StringComparer.Ordinal);

        foreach (XamlAttribute attribute in element.Attributes)
        {
            // Namespace declarations are not properties of the control, they are how the file names
            // its vocabularies, and offering them for editing invites breaking every type in scope.
            if (attribute.Name.Prefix == "xmlns" || attribute.Name.LocalName == "xmlns")
            {
                continue;
            }

            written[attribute.Name.ToString()] = attribute;
        }

        var offered = new HashSet<string>(StringComparer.Ordinal);

        // The name comes first, and it is offered whether or not the document has one. Every control
        // can be named and a named control is what code-behind and bindings reach for, so an
        // inspector that only showed it once somebody had typed it into the file by hand was hiding
        // the one property that has to be set before the file is useful.
        //
        // Written as the directive rather than as the Name property: x:Name is what a document says,
        // and Avalonia lets a control's Name be set once, which an update to a control that already
        // has one would fall foul of.
        offered.Add(NameDirective);

        PropertyRow named = RowFor(
            element, written.GetValueOrDefault(NameDirective), NameDirective, live, session);

        named.Label = "Name";

        Properties.Add(named);

        foreach ((_, string[] names) in Catalogue)
        {
            foreach (string name in names)
            {
                // A property the document already sets the long way — <Grid.RowDefinitions> as an
                // element, a Button's content as a child — must not also be offered as an
                // attribute: the loader would meet the same property set twice and refuse the
                // whole file, which is a file this inspector broke.
                if (SetAsElement(element, name))
                {
                    continue;
                }

                if (Has(live, session, name) && offered.Add(name))
                {
                    Properties.Add(RowFor(element, written.GetValueOrDefault(name), name, live, session));
                }
            }
        }

        foreach ((Type parent, string[] names) in Attached)
        {
            if (live?.Parent is null || !parent.IsInstanceOfType(live.Parent))
            {
                continue;
            }

            foreach (string name in names)
            {
                if (Has(live, session, name) && offered.Add(name))
                {
                    Properties.Add(RowFor(element, written.GetValueOrDefault(name), name, live, session));
                }
            }
        }

        // Whatever else the file says. An inspector that hides it is worse than one showing extra.
        foreach ((string name, XamlAttribute attribute) in written)
        {
            if (offered.Add(name))
            {
                Properties.Add(RowFor(element, attribute, name, live, session));
            }
        }

        // Grouped in the design's order, and a section with nothing in it is not drawn: an inspector
        // showing three empty headings tells somebody the control has no properties, which is the
        // opposite of what it means.
        foreach (string heading in new[] { "Identity", "Window", "Layout", "Appearance", "Content & Interaction" })
        {
            PropertyRow[] rows =
                [.. Properties.Where(row => GroupOf(row.Name) == heading && MatchesInspectorFilter(row))];

            if (rows.Length > 0)
            {
                PropertyGroups.Add(new PropertyGroup(heading, rows));
            }
        }

        // What the bindings read is asked of the session, which answers asynchronously; the rows
        // just built are what the answer fills.
        if (ActiveForm is { } form)
        {
            RunDetached(() => FillDataAsync(form, element));
        }
    }

    /// <summary>
    /// Whether the document already sets this property somewhere an attribute is not.
    /// </summary>
    /// <remarks>
    /// Property-element syntax, and the content property's implicit form: a Grid whose rows are
    /// <c>&lt;Grid.RowDefinitions&gt;</c>, a Button whose Content is its child. Only the names that
    /// have an implicit form are checked against children, because for everything else children are
    /// simply content.
    /// </remarks>
    private static bool SetAsElement(XamlElement element, string name)
    {
        if (element.MemberElements.Any(child =>
            child.Name.LocalName.EndsWith("." + name, StringComparison.Ordinal)))
        {
            return true;
        }

        return name is "Content" or "Header" && element.ContentElements.Any();
    }

    /// <summary>Whether this control has such a property at all.</summary>
    /// <remarks>
    /// Asked of the member resolver, so the candidate list stays a list of candidates: a Border is
    /// offered <c>CornerRadius</c> and a TextBlock is not, and neither of them is offered a name
    /// this designer happened to write down but Avalonia does not have.
    /// </remarks>
    private static bool Has(Control? live, XamlLoadSession? session, string name)
    {
        if (live is null || session is null)
        {
            return false;
        }

        try
        {
            return session.GetMember(live, name) is { IsResolved: true, CanWrite: true, IsReadOnly: false };
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>The live control the selection stands for, when there is one.</summary>
    private Control? LiveSelection() =>
        Selected is { } element ? ActiveForm?.Objects?.GetObject(element) as Control : null;

    /// <summary>
    /// Builds a row, asking the member what kind of value it holds.
    /// </summary>
    /// <remarks>
    /// The document alone cannot answer this — every attribute is text there — so the descriptor is
    /// what turns <c>IsEnabled="True"</c> into a checkbox and <c>Dock="Right"</c> into the four
    /// values <c>Dock</c> allows. A member nothing resolved still gets a row: a document may name a
    /// property of a type the project has not compiled yet, and refusing to show it would hide what
    /// the file says.
    /// </remarks>
    private PropertyRow RowFor(
        XamlElement element,
        XamlAttribute? attribute,
        string name,
        Control? live,
        XamlLoadSession? session)
    {
        // A root that sizes itself with d:DesignWidth writes no plain Width at all, and a row
        // reading only the plain attribute sat empty beside a canvas visibly 300 wide. The design
        // attribute is the size the designer shows, so it is the value the row shows — and
        // SetPropertyAsync routes the write back to it, so the row edits what it displays.
        if (attribute is null && IsRoot(element))
        {
            attribute = DesignSize(element, name);
        }

        string text = attribute?.GetValueText() ?? string.Empty;
        // The name is offered whether or not the file writes it, and unwritten it has no attribute to
        // say it is a directive — but it is one either way, and nothing binds to a directive.
        bool isDirective = attribute?.IsDirective ?? name == NameDirective;

        // An expression is not a value, and a row that let one be typed over would replace a
        // binding with whatever the text happened to look like.
        if (attribute?.GetValue() is XamlMarkupExtensionValue)
        {
            return Row(PropertyEditor.Expression, [], isReadOnly: true, static _ => null);
        }

        XamlMemberDescriptor? member = null;

        if (live is not null && session is not null && !isDirective)
        {
            try
            {
                member = session.GetMember(live, name);
            }
            catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
            {
                member = null;
            }
        }

        if (member is not { IsResolved: true })
        {
            return Row(PropertyEditor.Text, [], isDirective, static _ => null);
        }

        Type value = Nullable.GetUnderlyingType(member.ValueType) ?? member.ValueType;

        (PropertyEditor editor, IReadOnlyList<string> choices) = value switch
        {
            _ when value == typeof(bool) => (PropertyEditor.Flag, (IReadOnlyList<string>)[]),
            _ when value.IsEnum => Choices(value),
            _ when typeof(IBrush).IsAssignableFrom(value) || value == typeof(Color) =>
                (PropertyEditor.Colour, (IReadOnlyList<string>)[]),
            _ when value == typeof(double) || value == typeof(float)
                || value == typeof(int) || value == typeof(long) => (PropertyEditor.Number, (IReadOnlyList<string>)[]),
            _ => (PropertyEditor.Text, (IReadOnlyList<string>)[]),
        };

        XamlMemberDescriptor resolved = member;

        // Nothing typed is not a value to convert. Emptying a field is how a person says "do not
        // set this", and the write below takes the attribute out — but it was never reached for a
        // number: no text is not a Double, so the row reported an error and kept the old width.
        return Row(
            editor,
            choices,
            member.IsReadOnly || !member.CanWrite,
            typed => typed.Length > 0 && resolved.ConvertFromText(typed) is { Succeeded: false } failed
                ? failed.Error ?? $"not a {value.Name}"
                : null);

        static (PropertyEditor, IReadOnlyList<string>) Choices(Type value)
        {
            string[] names = Enum.GetNames(value);

            // Few enough to lay out side by side is the design's rule for a segmented control, and
            // four is where a row of them stops fitting the inspector's width.
            return (names.Length <= 4 ? PropertyEditor.Segmented : PropertyEditor.Choice, names);
        }

        PropertyRow Row(
            PropertyEditor editor,
            IReadOnlyList<string> choices,
            bool isReadOnly,
            Func<string, string?> validate)
        {
            var row = new PropertyRow(
                name,
                text,
                isDirective,
                editor,
                choices,
                isReadOnly,
                validate,
                (_, written) => RunDetached(() => SetPropertyAsync(element, name, written)));

            // A size, and what it reads as when the document does not state one. Not on the
            // root: a form's width is the form's size, and the card falls back to a default
            // rather than to the layout when it is taken away.
            if (name is "Width" or "Height" && !IsRoot(element))
            {
                row.IsSize = true;

                if (text.Length == 0 && live is not null && double.IsNaN(name == "Width" ? live.Width : live.Height))
                {
                    row.Unset = "NaN";
                }
            }

            row.Prime();

            return row;
        }
    }

    /// <summary>
    /// The size an alignment lets go of when it is set to Stretch, if the element states one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stretch and a size of its own are two answers to one question, and the size wins: a control
    /// 200 wide set to Stretch stays 200 wide and sits in the middle of the space it was offered.
    /// Whoever chooses Stretch is asking for the first answer, so the second goes — in the same
    /// edit, because it was one decision and should be one step to undo.
    /// </para>
    /// <para>
    /// Each alignment has its own side: horizontal lets go of the width and leaves the height
    /// alone. Not on the root, whose size is the form's and has nothing to stretch into.
    /// </para>
    /// </remarks>
    private string? SizeReleasedBy(XamlElement element, string name, string value)
    {
        if (IsRoot(element) || !string.Equals(value, "Stretch", StringComparison.Ordinal))
        {
            return null;
        }

        string? size = name switch
        {
            "HorizontalAlignment" => "Width",
            "VerticalAlignment" => "Height",
            _ => null,
        };

        return size is not null && element.GetAttribute(size) is not null ? size : null;
    }

    /// <summary>
    /// Writes a property, or takes it out when it is cleared.
    /// </summary>
    /// <remarks>
    /// Emptying a field is how a user says "I do not want this set", and writing <c>Width=""</c>
    /// would say something else — a value the loader has to reject. Now that the inspector offers
    /// properties the document has not written, clearing one has to be able to put it back to
    /// unwritten.
    /// </remarks>
    private async System.Threading.Tasks.Task SetPropertyAsync(XamlElement element, string name, string value)
    {
        if (ActiveForm is not { } form)
        {
            return;
        }

        XamlQualifiedName qualified = XamlQualifiedName.Parse(name);

        // The same routing the drag-resize uses, reached through the row instead: on a root that
        // states a design size, the design attribute is what the designer shows, so it is what the
        // edit writes — and the plain one too when the file already had it, never a second number.
        XamlAttribute? design = IsRoot(element) ? DesignSize(element, name) : null;
        string? released = SizeReleasedBy(element, name, value);

        await ApplyAsync(
            form,
            editor =>
            {
                if (value.Length == 0)
                {
                    if (design is not null)
                    {
                        editor.RemoveAttribute(element, design.Name);
                    }

                    editor.RemoveAttribute(element, qualified);
                }
                else
                {
                    if (design is not null)
                    {
                        editor.SetAttribute(element, design.Name, value);
                    }

                    if (design is null || element.GetAttribute(name) is not null)
                    {
                        editor.SetAttribute(element, qualified, value);
                    }

                    if (released is not null)
                    {
                        editor.RemoveAttribute(element, XamlQualifiedName.Parse(released));
                    }
                }
            },
            value.Length == 0
                ? $"clear {name}"
                : released is null ? $"set {name}" : $"set {name}, clear {released}");
    }

    private async System.Threading.Tasks.Task AddPropertyAsync()
    {
        if (ActiveForm is not { } form || Selected is not { } element)
        {
            return;
        }

        string name = NewPropertyName.Trim();
        string value = NewPropertyValue;

        await ApplyAsync(
            form,
            editor => editor.SetAttribute(element, XamlQualifiedName.Parse(name), value),
            $"add {name}");

        NewPropertyName = string.Empty;
        NewPropertyValue = string.Empty;

    }

}
