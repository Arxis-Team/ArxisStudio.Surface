using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using Avalonia;
using Avalonia.Controls;

namespace UiDesigner.Demo.ViewModels;

/// <summary>
/// A control the toolbox offers, and the markup that creates it.
/// </summary>
/// <remarks>
/// Markup rather than a type, because what gets dropped is a line of a file. A palette holding
/// <see cref="Type"/> would have to render one back into text at the moment of the drop, and would
/// then have to decide what a sensible new Button says — which is exactly what the snippet already
/// says, in the form the file will hold.
/// </remarks>
/// <param name="Group">The palette heading the entry sits under.</param>
/// <param name="Name">The control's type name, as the document will spell it.</param>
/// <param name="Xaml">The markup a drop writes.</param>
/// <param name="Package">
/// The NuGet package the control lives in when that is not Avalonia itself, so a refusal can name
/// what to add rather than only what is missing.
/// </param>
public sealed record ToolboxEntry(string Group, string Name, string Xaml, string? Package = null)
{
    /// <summary>The design's glyph for this control, and the hue it is drawn in.</summary>
    public Avalonia.Media.Geometry Glyph => Glyphs.For(Name);

    public string Hue => Glyphs.HueOf(Name);

    /// <summary>
    /// What the tooltip shows: the whole snippet when it is a line, its opening tag when a snippet
    /// with children would stretch a tooltip across the screen.
    /// </summary>
    public string Tip => Xaml.Length <= 80 || Xaml.IndexOf('>') is < 0
        ? Xaml
        : Xaml[..(Xaml.IndexOf('>') + 1)] + " …";

    public override string ToString() => Name;
}

/// <summary>One of the palette's headings, and what is under it.</summary>
public sealed record ToolboxGroup(string Name, System.Collections.Generic.IReadOnlyList<ToolboxEntry> Items);

public sealed partial class DesignerViewModel
{
    /// <summary>
    /// The palette.
    /// </summary>
    /// <remarks>
    /// Avalonia's own controls, and deliberately only those. A toolbox built by reflecting over the
    /// project's assemblies is a real feature and a different one: it needs the project built, it
    /// needs a rule for which types are controls somebody would place, and it needs a sensible
    /// initial markup per type. This palette is the part that demonstrates the drop reaching the
    /// file, which is the thing worth showing.
    /// </remarks>
    public ObservableCollection<ToolboxEntry> Toolbox { get; } = [];

    /// <summary>The same entries under the design's headings, filtered by the search box.</summary>
    public ObservableCollection<ToolboxGroup> ToolboxGroups { get; } = [];

    /// <summary>
    /// What the palette's search box holds.
    /// </summary>
    /// <remarks>
    /// A filter over the names rather than a search of anything: the palette is twenty entries, and
    /// the box exists because a person scanning for "Toggle" should not have to read all twenty.
    /// </remarks>
    public string ToolboxFilter
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                GroupToolbox();
            }
        }
    } = string.Empty;

    /// <summary>Which of the design's two variants the toggle is offering to switch to.</summary>
    public string ThemeName => IsDark ? "Dark" : "Light";

    private void GroupToolbox()
    {
        ToolboxGroups.Clear();

        string filter = ToolboxFilter.Trim();

        foreach (IGrouping<string, ToolboxEntry> group in Toolbox
            .Where(entry => filter.Length == 0
                || entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .GroupBy(static entry => entry.Group))
        {
            ToolboxGroups.Add(new ToolboxGroup(group.Key.ToUpperInvariant(), [.. group]));
        }
    }

    private void InitialiseToolbox()
    {
        InitialiseInspector();

        // The design's groups, in its order. A palette is read by position as much as by name once
        // somebody has used it twice, so the order is part of it. Controls that hold items drop
        // with a few — an empty ComboBox is an invisible drop and teaches nothing about the file
        // shape, while three ComboBoxItems are visible, selectable, and show what to edit next.
        Add("Layout", "Grid", """<Grid Width="200" Height="140" />""");
        Add("Layout", "StackPanel", """<StackPanel Width="180" Height="120" />""");
        Add("Layout", "DockPanel", """<DockPanel Width="200" Height="140" />""");
        Add("Layout", "WrapPanel", """<WrapPanel Width="200" Height="140" />""");

        // A faint background because a bare Canvas hit-tests as nothing at all: the next control
        // dropped over it would land beside it, and the panel that exists for free placement
        // would be the one panel nothing could be placed into.
        Add("Layout", "Canvas", """<Canvas Width="200" Height="140" Background="#11FFFFFF" />""");
        Add("Layout", "Border", """<Border Width="160" Height="90" Background="#22FFFFFF" />""");
        Add("Layout", "ScrollViewer", """<ScrollViewer Width="200" Height="140" />""");
        Add("Layout", "TabControl", """<TabControl Width="220" Height="150"><TabItem Header="One"><TextBlock Text="First page" /></TabItem><TabItem Header="Two"><TextBlock Text="Second page" /></TabItem></TabControl>""");
        Add("Layout", "Expander", """<Expander Header="Expander" IsExpanded="True"><TextBlock Text="Content" /></Expander>""");

        Add("Input", "Button", """<Button Content="Button" />""");
        Add("Input", "TextBox", """<TextBox Width="160" />""");
        Add("Input", "CheckBox", """<CheckBox Content="Check" />""");
        Add("Input", "RadioButton", """<RadioButton Content="Option" />""");
        Add("Input", "ComboBox", """<ComboBox Width="160" SelectedIndex="0"><ComboBoxItem Content="One" /><ComboBoxItem Content="Two" /><ComboBoxItem Content="Three" /></ComboBox>""");
        Add("Input", "Slider", """<Slider Width="160" Maximum="100" Value="40" />""");
        Add("Input", "ToggleSwitch", """<ToggleSwitch />""");
        Add("Input", "NumericUpDown", """<NumericUpDown Width="140" Value="0" />""");
        Add("Input", "DatePicker", """<DatePicker />""");
        Add("Input", "Menu", """<Menu><MenuItem Header="File"><MenuItem Header="New" /><MenuItem Header="Open" /></MenuItem><MenuItem Header="Edit" /></Menu>""");

        Add("Display", "TextBlock", """<TextBlock Text="Text" />""");
        Add("Display", "Image", """<Image Width="120" Height="90" />""");
        Add("Display", "ListBox", """<ListBox Width="180"><ListBoxItem Content="One" /><ListBoxItem Content="Two" /><ListBoxItem Content="Three" /></ListBox>""");
        Add("Display", "TreeView", """<TreeView Width="180"><TreeViewItem Header="Root" IsExpanded="True"><TreeViewItem Header="Leaf" /><TreeViewItem Header="Leaf" /></TreeViewItem></TreeView>""");
        Add(
            "Display",
            "DataGrid",
            """<DataGrid Width="220" Height="140" />""",
            "Avalonia.Controls.DataGrid");
        Add("Display", "ProgressBar", """<ProgressBar Width="160" Value="40" />""");
        Add("Display", "Separator", """<Separator Width="160" />""");
        Add("Display", "Path", """<Path Data="M3 12.5C5 6 11 10 13 3.5" Stroke="#9DA0A8" StrokeThickness="1.4" />""");

        GroupToolbox();

        void Add(string group, string name, string xaml, string? package = null) =>
            Toolbox.Add(new ToolboxEntry(group, name, xaml, package));
    }

    /// <summary>
    /// Drops a control onto a form at a point on that form.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The parent is decided by what is under the pointer rather than by what is selected, which is
    /// what makes dropping into a panel work the way a person expects: hover over the Canvas inside
    /// the form and the control lands in the Canvas, hover over the form itself and it lands there.
    /// </para>
    /// <para>
    /// A position is written only when the parent is one that honours it. Dropping into a
    /// <see cref="StackPanel"/> puts the control at the end and says nothing about coordinates,
    /// because the panel is what decides them — writing <c>Canvas.Left</c> there would produce an
    /// attribute that does nothing and a file that lies about the layout.
    /// </para>
    /// </remarks>
    public void Drop(FormViewModel form, ToolboxEntry entry, Control? over, Point at) =>
        RunDetached(() => DropAsync(form, entry, over, at));

    private async Task DropAsync(FormViewModel form, ToolboxEntry entry, Control? over, Point at)
    {
        if (form.Objects is not { } map || form.Document?.Root is not { } root)
        {
            return;
        }

        // Refused before the file is touched, not diagnosed after. A DataGrid lives in its own
        // package, and a project without it would take the drop, resolve nothing, and show an
        // empty canvas — a drop that looks like it did nothing. The session can answer "would
        // this resolve" up front, so the refusal names the package while the document stays
        // exactly as it was. Adding the package is the project's business, not this demo's.
        if (!await ResolvesAsync(form, root, entry.Name).ConfigureAwait(true))
        {
            Log(entry.Package is { Length: > 0 } package
                ? $"{entry.Name} needs the {package} package — add it to the project, then drop it again."
                : $"{entry.Name} does not resolve in this project — a package or reference is missing.");

            return;
        }

        (XamlElement parent, Control? parentControl) = ParentFor(map, root, over);

        string xaml = entry.Xaml;

        // Only a Canvas positions its children by Canvas.Left and Canvas.Top, so only a Canvas gets
        // them written. The root used to count too, and a form rooted at a UserControl or a
        // StackPanel therefore collected attached properties that mean nothing where they landed —
        // noise in the user's file that the layout ignores and a reader has to puzzle over.
        if (parentControl is Canvas)
        {
            // Into the parent's own coordinates. The point arrives in the card's, and the two
            // coincide only while the parent happens to sit at the form's origin — so a canvas
            // nested anywhere else would take the control and put it at the wrong place, by exactly
            // the parent's offset.
            Point local = parentControl is not null
                && form.Card.TranslatePoint(at, parentControl) is { } inParent
                ? inParent
                : at;

            xaml = WithPosition(entry, local);
        }

        await ApplyAsync(
            form,
            editor => editor.InsertElement(parent, IndexAtEnd(parent), xaml),
            $"add {entry.Name}");

        Log($"  {entry.Name} added to {parent.Name}");
    }

    /// <summary>
    /// The element that should receive the drop, walking up from whatever was under the pointer.
    /// </summary>
    /// <remarks>
    /// Only a control that can hold children is a candidate: dropping onto a Button means dropping
    /// beside it, not inside it, because a Button's content is one thing and replacing it silently
    /// would delete what was there.
    /// </remarks>
    private static (XamlElement Parent, Control? Control) ParentFor(
        XamlObjectMap map, XamlElement root, Control? over)
    {
        for (Control? current = over; current is not null; current = current.Parent as Control)
        {
            if (current is not Panel and not ContentControl and not Decorator)
            {
                continue;
            }

            if (map.GetElement(current) is { } element && CanHoldChildren(current))
            {
                return (element, current);
            }
        }

        return (root, null);
    }

    private static bool CanHoldChildren(Control control) => control switch
    {
        Panel => true,
        Decorator decorator => decorator.Child is null,
        ContentControl content => content.Content is null,
        _ => false,
    };

    /// <summary>
    /// Whether the entry's type would resolve in this form's project, asked of the session's own
    /// resolver — the same one the load will use, so the answer cannot disagree with the outcome.
    /// </summary>
    private static async Task<bool> ResolvesAsync(FormViewModel form, XamlElement root, string name)
    {
        if (form.Session is not { } session)
        {
            return true;
        }

        // The snippet's root is unprefixed, so it means whatever the document's default namespace
        // means — resolve it exactly there. A resolver that cannot answer is not a refusal: the
        // drop proceeds and the ordinary diagnostics have their say.
        try
        {
            XamlTypeResolution resolution = await session.Environment.TypeResolver.ResolveAsync(
                new XamlTypeName(
                    root.NamespaceContext.LookupNamespace(string.Empty) ?? string.Empty, name),
                root.NamespaceContext,
                System.Threading.CancellationToken.None).ConfigureAwait(true);

            return resolution.Success;
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
        {
            return true;
        }
    }

    /// <summary>Adds a position to a snippet, for a parent that honours one.</summary>
    private static string WithPosition(ToolboxEntry entry, Point at)
    {
        string position = string.Create(
            CultureInfo.InvariantCulture,
            $" Canvas.Left=\"{Math.Round(at.X)}\" Canvas.Top=\"{Math.Round(at.Y)}\"");

        int close = entry.Xaml.LastIndexOf("/>", StringComparison.Ordinal);

        return close < 0
            ? entry.Xaml
            : entry.Xaml[..close] + position.TrimEnd() + " " + entry.Xaml[close..];
    }

    private static int IndexAtEnd(XamlElement parent) => parent.ContentElements.Count();
}
