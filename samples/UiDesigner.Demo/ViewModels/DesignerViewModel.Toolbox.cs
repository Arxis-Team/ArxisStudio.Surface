using System;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ArxisStudio.Markup;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.Markup.Xaml;
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
    /// The prefix the snippet's element is written with, or nothing for a snippet written unprefixed.
    /// </summary>
    /// <remarks>
    /// A fragment's default namespace has to mean the same where it is inserted — Markup refuses one that
    /// does not (<c>AXM1043</c>), because a default namespace cannot be renamed. A prefix can be, and is
    /// declared on the form's root where it is missing. So a control from outside Avalonia's namespace
    /// travels prefixed — with the prefix its library suggests, or <c>local</c>, as a project's views are
    /// written — and the fragment's default namespace stays Avalonia's, the one the place a drop writes on
    /// it is named in. A prefixed snippet is a single element.
    /// </remarks>
    public string? Prefix { get; init; }

    /// <summary>
    /// The project's own control this entry places, as the design host listed it — or nothing for one
    /// of Avalonia's.
    /// </summary>
    /// <remarks>
    /// Names only (<see cref="ProjectControlInfo"/>): the palette is on screen across every swap of the
    /// project's types, and a type held here would hold the generation it came from.
    /// </remarks>
    public ProjectControlInfo? Control { get; init; }

    /// <summary>The heading the project's own controls are listed under, first in the palette.</summary>
    internal const string ProjectGroup = "Project";

    /// <summary>An entry for one of the project's own controls, written in the namespace its listing gives.</summary>
    public static ToolboxEntry For(ProjectControlInfo control) =>
        new(ProjectGroup, control.Name, $"<{control.Name} />")
        {
            XmlNamespace = control.XmlNamespace,
            Prefix = control.SuggestedPrefix ?? "local",
            Control = control,
        };

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
        XamlFragment fragment = Fragment();

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

    /// <summary>
    /// The snippet as a fragment in <see cref="XmlNamespace"/> — under <see cref="Prefix"/> when it has
    /// one — with no place written on it.
    /// </summary>
    public XamlFragment Fragment()
    {
        string open = "<" + Name;

        if (!Xaml.StartsWith(open, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The {Name} snippet does not start with its own element.");
        }

        return XamlFragment.Parse(Prefix is { } prefix
            ? $"<{prefix}:{Name} xmlns=\"{DesignerViewModel.AvaloniaNamespace}\" xmlns:{prefix}=\"{XmlNamespace}\"{Xaml[open.Length..]}"
            : Xaml.Insert(open.Length, $" xmlns=\"{XmlNamespace}\""));
    }

    /// <summary>The design's glyph for this control, and the hue it is drawn in.</summary>
    public Avalonia.Media.Geometry Glyph => Glyphs.For(Name);

    public string Hue => Glyphs.HueOf(Name);

    /// <summary>
    /// What the tooltip shows: the whole snippet when it is a line, its opening tag when a snippet
    /// with children would stretch a tooltip across the screen.
    /// </summary>
    public string Tip => Control is { } control
        ? control.ClassName + (control.IsBuilt ? string.Empty : " — not built yet; dropping it builds the project first")
        : Xaml.Length <= 80 || Xaml.IndexOf('>') is < 0
            ? Xaml
            : Xaml[..(Xaml.IndexOf('>') + 1)] + " …";

    public override string ToString() => Name;
}

/// <summary>One of the palette's headings, and what is under it.</summary>
public sealed record ToolboxGroup(string Name, System.Collections.Generic.IReadOnlyList<ToolboxEntry> Items);

public sealed partial class DesignerViewModel
{
    /// <summary>
    /// The palette: the project's own controls first, then Avalonia's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The project's are what ProjectSystem's design host lists by name for the project of the form in
    /// front (<see cref="ProjectDesignHost.GetPlaceableControlsAsync"/>) — what the live generation built,
    /// and the forms the IDE has written that no build has produced yet, marked as such. They are listed
    /// again whenever that can change: the types swapped, a file came or went, another project's form
    /// came to the front.
    /// </para>
    /// <para>
    /// Avalonia's are snippets rather than types: what gets dropped is a line of a file, and the snippet
    /// already says what a sensible new one says.
    /// </para>
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
    public void Drop(FormViewModel form, ToolboxEntry entry, SurfaceDropPlacement placement)
    {
        if (entry.Control is { } control)
        {
            // What the drop meant, as names, before anything waits: the parent the editor named is a
            // control of a generation a build may be about to replace, and a frame that held it across
            // the wait would hold that generation.
            if (IntentOf(form, entry, placement) is { } intent)
            {
                Place(control, intent);
            }

            return;
        }

        RunDetached(() => DropAsync(form, entry, placement));
    }

    /// <summary>Places a control of the project from names alone, once it is built.</summary>
    /// <remarks>
    /// A method of its own, not a lambda in <see cref="Drop"/>: the lambdas of one method share its
    /// closure, and the one for an Avalonia control captures the placement — live controls of the
    /// generation the wait is for a swap to replace. Written there, this one held them, and the
    /// generation with them, for as long as the build and the swap took, and the swap found its
    /// types still held.
    /// </remarks>
    private void Place(ProjectControlInfo control, DropIntent intent) => RunDetached(() => PlaceAsync(control, intent));

    private async Task DropAsync(FormViewModel form, ToolboxEntry entry, SurfaceDropPlacement placement)
    {
        if (form.Objects is not { } map || form.Document?.Root is not { } root)
        {
            return;
        }

        // The placement names controls of the live generation, and this writes after a wait: a swap in
        // the meantime would find them held here.
        using IDisposable writing = Defer("a drop being written");

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

        int index = IndexFor(placement, map, parent);

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

    /// <summary>Where among the parent's content a placement puts the new element.</summary>
    private static int IndexFor(SurfaceDropPlacement placement, XamlObjectMap map, XamlElement parent) => placement switch
    {
        { Kind: SurfaceDropKind.Content } => 0,
        { Anchor: { } anchor } when map.GetElement(anchor) is { IndexInContent: >= 0 } next => next.IndexInContent,
        _ => IndexAtEnd(parent),
    };

    /// <summary>What a drop meant, in names a swap of the project's types does not take away.</summary>
    /// <param name="File">The form's file.</param>
    /// <param name="Parent">Where the new element goes, as a path in the form's document.</param>
    /// <param name="Index">Where among the parent's content.</param>
    /// <param name="Fragment">The markup, with its place written on it where the parent reads one.</param>
    private sealed record DropIntent(CanonicalPath File, XamlElementPath Parent, int Index, XamlFragment Fragment);

    /// <summary>What a drop onto a form meant, read off the live controls and kept as names.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private DropIntent? IntentOf(FormViewModel form, ToolboxEntry entry, SurfaceDropPlacement placement)
    {
        if (form.Objects is not { } map || map.GetElement(placement.Parent) is not { } parent)
        {
            Log($"! {entry.Name}: what it was dropped into has no element in the document");

            return null;
        }

        // A control placed inside its own markup would construct itself without end.
        if (entry.Control is { Document.IsEmpty: false } control && control.Document == form.File)
        {
            Log($"{entry.Name} cannot be placed inside its own form");

            return null;
        }

        return new DropIntent(form.File, XamlElementPath.Of(parent), IndexFor(placement, map, parent), entry.FragmentFor(placement));
    }

    /// <summary>
    /// Places one of the project's own controls where a drop meant it — building it first when no build
    /// has produced its class yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A control the IDE has just written has no class until the project builds, and a document holding
    /// an element nothing can build shows nothing. So the design host builds it, and the drop waits for
    /// the generation that has it (<see cref="ProjectDesignHost.EnsureBuiltAsync"/>) — through the gate:
    /// a drop made while a gesture holds the swap off is placed when the gesture lets go.
    /// </para>
    /// <para>
    /// The swap builds every form again, so the drop waits as names and is found again afterwards: the
    /// form by its file, the parent by its path in the document, which the host kept across the swap.
    /// The element is written in the namespace the listing gives — <c>using:</c> the CLR namespace for a
    /// control not built yet — and the editor declares it on the root.
    /// </para>
    /// </remarks>
    private async Task PlaceAsync(ProjectControlInfo control, DropIntent intent)
    {
        if (_host is not { } host)
        {
            return;
        }

        if (!control.IsBuilt)
        {
            Log($"Building {ProjectNameOf(control.Project)} to place {control.Name}…");
        }

        bool placeable;

        try
        {
            placeable = await host.EnsureBuiltAsync(control, _shutdown.Token);
        }
        catch (ObjectDisposedException)
        {
            // The project was closed while it was being built.
            return;
        }

        if (!placeable)
        {
            Log($"! {control.Name} could not be placed: the project's build has no {control.ClassName} — see the build above");

            return;
        }

        if (Forms.FirstOrDefault(open => open.File == intent.File) is not { } form)
        {
            Log($"! {control.Name}: {intent.File.FileName} was closed while it was being built");

            return;
        }

        ImmutableArray<MarkupDiagnostic> found = [];
        string? into = null;

        await ApplyAsync(
            form,
            editor =>
            {
                if (intent.Parent.Resolve(editor.Document) is not { } parent)
                {
                    return;
                }

                editor.InsertFragment(parent, Math.Min(intent.Index, parent.ContentElements.Count()), intent.Fragment);

                found = editor.Diagnostics;
                into = parent.Name.ToString();
            },
            $"add {control.Name}");

        foreach (MarkupDiagnostic diagnostic in found)
        {
            Log(diagnostic.IsError
                ? $"! {diagnostic.Code}: {diagnostic.Message}"
                : $"  {diagnostic.Code}: {diagnostic.Message}");
        }

        if (into is null)
        {
            Log($"! {control.Name}: what it was dropped into is no longer in the document");
        }
        else if (!found.Any(static diagnostic => diagnostic.IsError))
        {
            Log($"  {control.Name} added to {into}");
        }
    }

    /// <summary>A project's name, for the console.</summary>
    private string ProjectNameOf(ProjectIdentity project) =>
        _workspace.CurrentSnapshot is { } snapshot && snapshot.TryGetProject(project, out ProjectSnapshot? found)
            ? found.Name
            : "the project";

    /// <summary>Which listing of the project's controls is the latest asked for, so an older answer is dropped.</summary>
    private int _projectListing;

    /// <summary>The project the palette's Project heading was last listed for.</summary>
    private ProjectIdentity _listedProject;

    /// <summary>The palette entry that places the control a form's file declares, if it is one.</summary>
    internal ToolboxEntry? ProjectEntryFor(CanonicalPath file) =>
        Toolbox.FirstOrDefault(entry => entry.Control is { Document.IsEmpty: false } control && control.Document == file);

    /// <summary>
    /// Lists the project's own controls again under the palette's Project heading — after the types
    /// moved, a file came or went, or another project's form came to the front.
    /// </summary>
    private void RefreshProjectToolbox() => RunDetached(RefreshProjectToolboxAsync);

    /// <summary>Lists them again only when the form in front is of another project than the last listing.</summary>
    private void RefreshProjectToolboxForTheFormInFront()
    {
        if (ToolboxProject() is not { } project || project != _listedProject)
        {
            RefreshProjectToolbox();
        }
    }

    private async Task RefreshProjectToolboxAsync()
    {
        int listing = ++_projectListing;
        ImmutableArray<ProjectControlInfo> controls = [];
        ProjectIdentity listed = default;

        if (_host is { } host && ToolboxProject() is { } project)
        {
            listed = project;

            try
            {
                controls = await host.GetPlaceableControlsAsync(project, _shutdown.Token);
            }
            catch (Exception error) when (error is ObjectDisposedException or InvalidOperationException)
            {
                // Closed, or not started yet: there is nothing to list until it is.
            }
        }

        if (listing != _projectListing)
        {
            return;
        }

        _listedProject = listed;

        foreach (ToolboxEntry old in Toolbox.Where(static entry => entry.Control is not null).ToArray())
        {
            Toolbox.Remove(old);
        }

        for (int at = 0; at < controls.Length; at++)
        {
            Toolbox.Insert(at, ToolboxEntry.For(controls[at]));
        }

        GroupToolbox();
    }

    /// <summary>The project whose controls the palette offers: the one the form in front is in, or the design set's first.</summary>
    private ProjectIdentity? ToolboxProject()
    {
        if (_workspace.CurrentSnapshot is not { } snapshot)
        {
            return null;
        }

        if (ActiveForm is { } form && snapshot.TryGetProjectForFile(form.File, out ProjectSnapshot? owner))
        {
            return owner.Identity;
        }

        return _host?.DesignSet is { IsDefaultOrEmpty: false } set ? set[0] : null;
    }
}
