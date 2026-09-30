using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ArxisStudio.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace UiDesigner.Demo.ViewModels;

/// <summary>One row of the document's tree, flattened for a list rather than nested.</summary>
/// <remarks>
/// Flat, with the depth carried as an indent, because that is what the design draws and because a
/// nested tree control would own expansion state the document does not have. Every row is an
/// element of the document, so selecting one is selecting markup.
/// </remarks>
public sealed class HierarchyRow(XamlElement element, int depth, bool hasChildren)
{
    public XamlElement Element { get; } = element;

    public double Indent { get; } = 8 + (depth * 14);

    public string Name { get; } =
        element.GetDirective("Name") is { Length: > 0 } named ? named : element.Name.LocalName;

    /// <summary>The type, and what it holds, which is the second line of every row in the design.</summary>
    public string TypeLabel { get; } = Describe(element);

    public Geometry Glyph { get; } = Glyphs.For(element.Name.LocalName);

    public string Hue { get; } = Glyphs.HueOf(element.Name.LocalName);

    public double ChevronOpacity { get; } = hasChildren ? 1 : 0;

    private static string Describe(XamlElement element)
    {
        int children = element.ContentElements.Count();

        return children switch
        {
            0 => element.Name.LocalName,
            1 => element.Name.LocalName + " · 1 child",
            _ => $"{element.Name.LocalName} · {children} children",
        };
    }
}

/// <summary>One step of the breadcrumb, and whether it is the one being edited.</summary>
/// <remarks>
/// The last step is the selection itself and the design gives it the foreground colour and a
/// medium weight; the ones before it are the path to it and stay quiet. Carrying the flag is what
/// lets one template draw both.
/// </remarks>
public sealed record Crumb(string Name, bool IsFirst, bool IsLast)
{
    /// <summary>The design gives the selection itself a medium weight and the path to it none.</summary>
    public Avalonia.Media.FontWeight Weight =>
        IsLast ? Avalonia.Media.FontWeight.Medium : Avalonia.Media.FontWeight.Normal;
}

/// <summary>A line of the console, split the way the design splits it.</summary>
public sealed record LogRow(string Time, string Level, string Message)
{
    /// <summary>The colour the level is drawn in.</summary>
    public string Hue => Level switch
    {
        "ERROR" => "RedText",
        "WARN" => "YelText",
        "BUILD" => "Pur",
        "RUN" => "GrnText",
        _ => "Fg3",
    };
}

/// <summary>What the editor shows in the middle: the surface, the markup, or both.</summary>
public enum DocumentView
{
    Design,
    Xaml,
    Split,
}

public sealed partial class DesignerViewModel
{
    /// <summary>The open document's tree, in document order.</summary>
    public ObservableCollection<HierarchyRow> Hierarchy { get; } = [];

    /// <summary>The path from the root to what is selected, which the design puts above the canvas.</summary>
    public ObservableCollection<Crumb> Breadcrumb { get; } = [];

    /// <summary>
    /// Raised when a row in the tree asks the canvas to show what it names.
    /// </summary>
    /// <remarks>
    /// An event rather than the view watching <see cref="Selected"/>, because that property changes
    /// from both directions and watching it would send the canvas's own selection straight back at
    /// it. This fires only on the way the canvas cannot already know about.
    /// </remarks>
    public event EventHandler<XamlElement>? CanvasSelectionRequested;

    /// <summary>
    /// Raised when what the canvas has selected has stopped existing.
    /// </summary>
    /// <remarks>
    /// The other half of the pair above, and needed for the same reason: the canvas cannot know. A
    /// deleted control leaves the tree, but the editor is still holding it as the selected target and
    /// goes on drawing the frame it had — over nothing, until the next click happens to reselect and
    /// the frame quietly disappears. Clearing is the host's to do, because the host is what knows the
    /// control was removed rather than merely detached for a moment.
    /// </remarks>
    public event EventHandler? CanvasSelectionCleared;

    public HierarchyRow? SelectedHierarchyRow
    {
        get;
        set
        {
            if (Set(ref field, value) && value is not null && !_syncingHierarchy)
            {
                Selected = value.Element;

                CanvasSelectionRequested?.Invoke(this, value.Element);
            }
        }
    }

    /// <summary>Design, XAML or both, which is the segmented control in the breadcrumb bar.</summary>
    public DocumentView View
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                Raise(nameof(IsDesignVisible));
                Raise(nameof(IsXamlVisible));
                Raise(nameof(IsDesignView));
                Raise(nameof(IsXamlView));
                Raise(nameof(IsSplitView));
            }
        }
    } = DocumentView.Design;

    public bool IsDesignVisible => View is DocumentView.Design or DocumentView.Split;

    public bool IsXamlVisible => View is DocumentView.Xaml or DocumentView.Split;

    public bool IsDesignView => View == DocumentView.Design;

    public bool IsXamlView => View == DocumentView.Xaml;

    public bool IsSplitView => View == DocumentView.Split;

    public RelayCommand ShowDesignCommand { get; private set; } = null!;

    public RelayCommand ShowXamlCommand { get; private set; } = null!;

    public RelayCommand ShowSplitCommand { get; private set; } = null!;

    public RelayCommand ZoomInCommand { get; private set; } = null!;

    public RelayCommand ZoomOutCommand { get; private set; } = null!;

    public RelayCommand ZoomResetCommand { get; private set; } = null!;

    /// <summary>Asks the canvas to fit the form to the window.</summary>
    /// <remarks>
    /// A command here and the arithmetic in the view, because fitting is a question about a viewport
    /// — how big the canvas is on screen — and that is the one thing a view model does not know.
    /// </remarks>
    public RelayCommand ZoomToFitCommand { get; private set; } = null!;

    /// <summary>Raised when the canvas should fit the form to the window.</summary>
    public event EventHandler? ZoomToFitRequested;

    public RelayCommand ToggleThemeCommand { get; private set; } = null!;

    /// <summary>Which pane of the bottom dock is showing.</summary>
    /// <remarks>
    /// An index with two booleans off it rather than a TabControl, because the design's tabs are
    /// text with a two-pixel underline and nothing else — no border, no header background — and
    /// restyling a TabControl into that is more code than drawing it.
    /// </remarks>
    public int DockTab
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                Raise(nameof(IsDockProject));
                Raise(nameof(IsDockConsole));
            }
        }
    }

    public bool IsDockProject => DockTab == 0;

    public bool IsDockConsole => DockTab == 1;

    public RelayCommand ShowProjectCommand { get; private set; } = null!;

    public RelayCommand ShowConsoleCommand { get; private set; } = null!;

    /// <summary>The document's text, for the XAML view.</summary>
    public string DocumentText
    {
        get;
        private set => Set(ref field, value);
    } = string.Empty;

    public bool IsDark
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                Raise(nameof(IsLight));
                Raise(nameof(ThemeName));
                Raise(nameof(ThemeMenuHeader));
            }
        }
    } = Avalonia.Application.Current?.RequestedThemeVariant != ThemeVariant.Light;

    public bool IsLight => !IsDark;

    /// <summary>The project's name, for the toolbar's first control.</summary>
    public string ProjectName => IsLoaded && EntryPoint.FileName is { Length: > 0 } name
        ? System.IO.Path.GetFileNameWithoutExtension(name)
        : "No project";

    /// <summary>What was opened, which the design shows beside the project like a branch.</summary>
    public string EntryPointName => EntryPoint.IsEmpty ? "—" : EntryPoint.FileName;

    /// <summary>
    /// The configuration a build would use.
    /// </summary>
    /// <remarks>
    /// Read off the project rather than assumed, because the evaluated snapshot knows: a solution
    /// opened without a configuration evaluates under Debug and says so, and a designer that printed
    /// "Debug" regardless would be wrong for anybody who changed it.
    /// </remarks>
    /// <summary>What Run would start, which the design shows in the run bezel.</summary>
    public string TargetName => ActiveForm is { } form ? form.Name : "—";

    /// <summary>What the status bar says on the left.</summary>
    public string StatusLeft => IsLoaded
        ? $"{Status} · {Describe(ProjectForms.Count, "form")}"
        : "No project";

    /// <summary>The label the design floats above the form on the canvas.</summary>
    public string CanvasCaption => ActiveForm is { } form ? "⌖ " + form.Name : string.Empty;

    /// <summary>The size chip in the breadcrumb bar, and the one over the canvas.</summary>
    public string CanvasSize => ActiveForm is { } form
        ? $"{form.Width:F0} × {form.Height:F0}"
        : "—";

    public string CanvasSizeAndZoom => ActiveForm is null
        ? string.Empty
        : $"{CanvasSize} · {Zoom * 100:F0}%";

    private bool _syncingHierarchy;

    private void InitialiseShell()
    {
        ShowDesignCommand = new RelayCommand(() => View = DocumentView.Design);
        ShowXamlCommand = new RelayCommand(() => View = DocumentView.Xaml);
        ShowSplitCommand = new RelayCommand(() => View = DocumentView.Split);

        ZoomInCommand = new RelayCommand(() => Zoom = Math.Min(4, Zoom * 1.1));
        ZoomOutCommand = new RelayCommand(() => Zoom = Math.Max(0.1, Zoom / 1.1));
        ZoomResetCommand = new RelayCommand(() => Zoom = 1);
        ZoomToFitCommand = new RelayCommand(() => ZoomToFitRequested?.Invoke(this, EventArgs.Empty));

        ToggleThemeCommand = new RelayCommand(SwitchTheme);

        ShowProjectCommand = new RelayCommand(() => DockTab = 0);
        ShowConsoleCommand = new RelayCommand(() => DockTab = 1);
    }

    /// <summary>
    /// Switches the whole application between the design's two variants.
    /// </summary>
    /// <remarks>
    /// On the application rather than on the window, because the palette is a resource dictionary
    /// keyed by variant and every panel reads it — including the canvas grid, which belongs to a
    /// library and would otherwise stay dark on a light desk. The previews do not follow: each
    /// form's surface carries the variant of the application it belongs to, and the studio
    /// changing its clothes is a fact about the studio.
    /// </remarks>
    private void SwitchTheme()
    {
        IsDark = !IsDark;

        ThemeVariant variant = IsDark ? ThemeVariant.Dark : ThemeVariant.Light;

        if (Avalonia.Application.Current is { } application)
        {
            application.RequestedThemeVariant = variant;
        }
    }

    /// <summary>
    /// Rebuilds the tree and the document text from whatever form is active.
    /// </summary>
    /// <remarks>
    /// From the document, not from the live objects. The tree a designer navigates is the file's
    /// structure — a template's parts are not in it, and neither is anything a control invented for
    /// itself — which is also why every row can be selected and edited.
    /// </remarks>
    /// <summary>
    /// What the tree is filtered by, which is the magnifier beside it.
    /// </summary>
    /// <remarks>
    /// A form of thirty controls is a tree nobody scrolls twice, and the name of the one being
    /// looked for is usually known. Rows are dropped rather than folded away: the indent still says
    /// how deep each one is, so what is left reads as a tree with the middle taken out rather than a
    /// flat list.
    /// </remarks>
    public string HierarchyFilter
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                Raise(nameof(IsHierarchyFiltered));
                RebuildHierarchy();
            }
        }
    } = string.Empty;

    /// <summary>Whether the tree is showing part of the form rather than all of it.</summary>
    public bool IsHierarchyFiltered => HierarchyFilter.Length > 0;

    /// <summary>Whether the filter box is on screen at all.</summary>
    /// <remarks>
    /// Hidden until it is asked for, and closing it clears what was typed — a filter left in place
    /// behind a hidden box is a tree that is missing rows for no reason anybody can see.
    /// </remarks>
    public bool IsHierarchySearchOpen
    {
        get;
        set
        {
            if (Set(ref field, value) && !value)
            {
                HierarchyFilter = string.Empty;
            }
        }
    }

    private bool Matches(HierarchyRow row) =>
        HierarchyFilter is not { Length: > 0 } filter
            || row.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || row.TypeLabel.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private void RebuildHierarchy()
    {
        Hierarchy.Clear();

        DocumentText = ActiveForm?.Document?.SourceText.ToString() ?? string.Empty;

        if (ActiveForm?.Document?.Root is not { } root)
        {
            ShowBreadcrumb();

            return;
        }

        Walk(root, 0);

        SyncHierarchySelection();
        ShowBreadcrumb();

        void Walk(XamlElement element, int depth)
        {
            List<XamlElement> children = [.. element.ContentElements];
            var row = new HierarchyRow(element, depth, children.Count > 0);

            if (Matches(row))
            {
                Hierarchy.Add(row);
            }

            foreach (XamlElement child in children)
            {
                Walk(child, depth + 1);
            }
        }
    }

    /// <summary>Moves the tree's highlight to whatever is selected, without answering back.</summary>
    private void SyncHierarchySelection()
    {
        _syncingHierarchy = true;

        try
        {
            SelectedHierarchyRow = Selected is null
                ? null
                : Hierarchy.FirstOrDefault(row => ReferenceEquals(row.Element, Selected));
        }
        finally
        {
            _syncingHierarchy = false;
        }
    }

    private void ShowBreadcrumb()
    {
        Breadcrumb.Clear();

        if (Selected is null)
        {
            return;
        }

        var trail = new Stack<string>();

        for (XamlElement? current = Selected; current is not null; current = current.Parent as XamlElement)
        {
            trail.Push(current.GetDirective("Name") is { Length: > 0 } named
                ? named
                : current.Name.LocalName);
        }

        string[] steps = [.. trail];

        for (int index = 0; index < steps.Length; index++)
        {
            Breadcrumb.Add(new Crumb(steps[index], index == 0, index == steps.Length - 1));
        }
    }
}
