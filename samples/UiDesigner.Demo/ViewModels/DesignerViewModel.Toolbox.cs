using System;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Markup;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.Surface.UiDesigner;
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
    /// <summary>
    /// The XML namespace the snippet's names are written in.
    /// </summary>
    /// <remarks>
    /// Said rather than assumed. A snippet without one means whatever the receiving document's default
    /// namespace means — Avalonia's in every template, but not in a document that binds Avalonia to a
    /// prefix — and both the check that the type resolves and the insert ask in this namespace, so a
    /// document that would read the snippet as something else refuses it with a reason instead of
    /// taking a different control.
    /// </remarks>
    public string XmlNamespace { get; init; } = DesignerViewModel.AvaloniaNamespace;

    /// <summary>
    /// The snippet as a fragment in <see cref="XmlNamespace"/>, with the place written on its root where
    /// the parent reads one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The place is set on the fragment's root element through Markup's editor, before the fragment is
    /// inserted. The snippet used to be edited as text — the attributes went in front of its last
    /// <c>/&gt;</c> — and a snippet with children, a TabControl's pages or an Expander's content, closes
    /// its last child that way, so the position landed on a nested TextBlock.
    /// </para>
    /// <para>
    /// A Canvas reads <c>Canvas.Left</c> and <c>Canvas.Top</c>, a Grid reads <c>Grid.Row</c> and
    /// <c>Grid.Column</c> — written only when not zero, as a person would leave them out. Every other
    /// parent decides the place by its own layout, and nothing is written: an attribute the layout
    /// ignores is a file that lies about the layout.
    /// </para>
    /// </remarks>
    public XamlFragment FragmentFor(SurfaceDropPlacement placement)
    {
        string open = "<" + Name;

        if (!Xaml.StartsWith(open, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The {Name} snippet does not start with its own element.");
        }

        XamlFragment fragment = XamlFragment.Parse(Xaml.Insert(open.Length, $" xmlns=\"{XmlNamespace}\""));

        if (fragment.Root is not { } root)
        {
            return fragment;
        }

        XamlDocumentEditor editor = fragment.Document.Edit();

        switch (placement)
        {
            case { Kind: SurfaceDropKind.Position, Parent: Canvas }:
                Set("Canvas.Left", placement.Position.X);
                Set("Canvas.Top", placement.Position.Y);
                break;

            case { Kind: SurfaceDropKind.Cell }:
                if (placement.Row > 0)
                {
                    Set("Grid.Row", placement.Row);
                }

                if (placement.Column > 0)
                {
                    Set("Grid.Column", placement.Column);
                }

                break;
        }

        return editor.HasChanges && editor.Apply().Root is { } placed ? XamlFragment.From(placed) : fragment;

        void Set(string attached, double value) =>
            editor.SetAttribute(
                root,
                editor.Qualify(root, DesignerViewModel.AvaloniaNamespace, attached),
                Math.Round(value).ToString(CultureInfo.InvariantCulture));
    }

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
        // Pages that hold a panel, so a control dropped onto a page goes into it: a page holding only
        // its text could take nothing without losing the text.
        Add("Layout", "TabControl", """<TabControl Width="220" Height="150"><TabItem Header="One"><StackPanel><TextBlock Text="First page" /></StackPanel></TabItem><TabItem Header="Two"><StackPanel><TextBlock Text="Second page" /></StackPanel></TabItem></TabControl>""");
        Add("Layout", "Expander", """<Expander Header="Expander" IsExpanded="True"><StackPanel><TextBlock Text="Content" /></StackPanel></Expander>""");

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
    /// Drops a control onto a form, where the editor said it lands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The place is the editor's answer (<see cref="UiDesignerView.TryResolveDropPlacement"/>), the one its
    /// indicator showed while the drag was over the form: the parent under the pointer that can take a
    /// child, and where in it — between two neighbours of a StackPanel by the rule the canvas reorders
    /// by, in the cell of a Grid, at the point of a Canvas, as the content of an empty Border or tab page.
    /// This only writes it. Deciding it here used to drop every control at the end of a StackPanel and
    /// into the first cell of a Grid, whatever the pointer said.
    /// </para>
    /// <para>
    /// The placement names live controls and the document holds elements: the form's object map turns
    /// one into the other, and the neighbour the control goes in front of is asked for its element rather
    /// than trusting an index measured against the live panel.
    /// </para>
    /// </remarks>
    public void Drop(FormViewModel form, ToolboxEntry entry, SurfaceDropPlacement placement) =>
        RunDetached(() => DropAsync(form, entry, placement));

    private async Task DropAsync(FormViewModel form, ToolboxEntry entry, SurfaceDropPlacement placement)
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
        if (!await ResolvesAsync(form, root, entry).ConfigureAwait(true))
        {
            Log(entry.Package is { Length: > 0 } package
                ? $"{entry.Name} needs the {package} package — add it to the project, then drop it again."
                : $"{entry.Name} does not resolve in this project — a package or reference is missing.");

            return;
        }

        if (map.GetElement(placement.Parent) is not { } parent)
        {
            Log($"! {entry.Name}: what it was dropped into has no element in the document");

            return;
        }

        int index = placement switch
        {
            { Kind: SurfaceDropKind.Content } => 0,
            { Anchor: { } anchor } when map.GetElement(anchor) is { IndexInContent: >= 0 } next => next.IndexInContent,
            _ => IndexAtEnd(parent),
        };

        XamlFragment fragment = entry.FragmentFor(placement);
        ImmutableArray<MarkupDiagnostic> found = [];

        await ApplyAsync(
            form,
            editor =>
            {
                editor.InsertFragment(parent, index, fragment);

                found = editor.Diagnostics;
            },
            $"add {entry.Name}");

        foreach (MarkupDiagnostic diagnostic in found)
        {
            Log(diagnostic.IsError
                ? $"! {diagnostic.Code}: {diagnostic.Message}"
                : $"  {diagnostic.Code}: {diagnostic.Message}");
        }

        if (!found.Any(static diagnostic => diagnostic.IsError))
        {
            Log($"  {entry.Name} added to {parent.Name}");
        }
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
    /// <remarks>
    /// Asked in the entry's own namespace, not in whatever the document's default namespace happens to
    /// be: the insert writes the snippet in that namespace too.
    /// </remarks>
    private static async Task<bool> ResolvesAsync(FormViewModel form, XamlElement root, ToolboxEntry entry)
    {
        if (form.Session is not { } session)
        {
            return true;
        }

        // A resolver that cannot answer is not a refusal: the drop proceeds and the ordinary
        // diagnostics have their say.
        try
        {
            XamlTypeResolution resolution = await session.Environment.TypeResolver.ResolveAsync(
                new XamlTypeName(entry.XmlNamespace, entry.Name),
                root.NamespaceContext,
                System.Threading.CancellationToken.None).ConfigureAwait(true);

            return resolution.Success;
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
        {
            return true;
        }
    }

    private static int IndexAtEnd(XamlElement parent) => parent.ContentElements.Count();
}
