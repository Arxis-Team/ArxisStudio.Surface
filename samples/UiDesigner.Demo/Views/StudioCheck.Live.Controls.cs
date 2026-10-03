using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.Markup.Xaml;
using ArxisStudio.Surface.UiDesigner;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UiDesigner.Demo.ViewModels;

namespace UiDesigner.Demo.Views;

/// <summary>
/// Part 4 of <c>--live</c>: the project's own controls and data beside the IDE, in the solution L7 opened.
/// </summary>
/// <remarks>
/// <para>
/// L6 — a control the IDE has just written is offered in the palette as not built yet; dropped while the
/// swap is held off, it waits, and lands once the build's types are swapped in; the IDE's edit of its
/// markup shows on the window in place; removed on the canvas, in place. L8 — the project's view model
/// chosen as a view's data type, design data made of it, a text bound to its member and showing the
/// design instance's value; the IDE renames the member, and the binding reads as broken after the swap.
/// L10 — a window with a class, a handler and a data type changes structurally in place: its session
/// stays, the handler runs on it, and the platform holds no window more than before. L11 — a user control
/// the IDE writes with nothing in it takes a control from the palette as its content.
/// </para>
/// <para>
/// As in part 3, nothing of a generation is kept across a swap: forms are found again by file name,
/// sessions are compared through weak references, and what touches the canvas does so in methods that
/// answer with text or a flag.
/// </para>
/// </remarks>
internal static partial class StudioCheck
{
    private static async Task<int> ControlsAndDataBesideTheIdeAsync(MainWindow window, DesignerViewModel designer, string solution)
    {
        var failures = 0;

        foreach (Func<Task<int>> step in new Func<Task<int>>[]
        {
            () => PlaceANewControlAsync(window, designer, solution),
            () => BindToTheProjectsDataAsync(designer, solution),
            () => ChangeAFormWithAClassInPlaceAsync(designer, solution),
            () => DropIntoAnEmptyControlAsync(window, designer, solution),
        })
        {
            failures += await step();

            // Types found held are a restart, and every step after this one would wait for a swap that
            // cannot come: what holds them is named, and the rest is not run.
            if (designer.TypesState == ProjectDesignState.RestartRequired)
            {
                NameTheHolder(designer, "part 4");

                return Fail(ref failures, "part 4: the project's types were found held — the steps after this one are not run");
            }
        }

        return failures;
    }

    /// <summary>Names what holds the project's types after a swap found them held, while it still holds them.</summary>
    private static void NameTheHolder(DesignerViewModel designer, string step)
    {
        if (designer.LastSwap is { Reclaimed: false })
        {
            ProbeRoots(step, ProjectAssemblyNames(designer));
            SayTrackedWindows();
        }
    }

    /// <summary>
    /// L6: the IDE writes a control; the palette offers it as not built; dropped while the swap is held
    /// off, it lands once the types are swapped in; its markup edited in the IDE shows in place; removed in
    /// place.
    /// </summary>
    private static async Task<int> PlaceANewControlAsync(MainWindow window, DesignerViewModel designer, string solution)
    {
        var failures = 0;
        string badge = Path.Combine(Path.GetDirectoryName(solution)!, "SuiteApp", "Views", "BadgeControl.axaml");
        CanonicalPath badgeFile = CanonicalPath.Create(badge);

        if (!await ShowFormAsync(designer, "MainWindow.axaml"))
        {
            return Fail(ref failures, "L6: the application's window would not come to the front");
        }

        // The IDE writes a control while somebody is in the middle of something: it is built, and its
        // types wait for the swap.
        designer.SwapsHeldFor = "L6: somebody in the middle of something";

        int builds = designer.Builds;

        try
        {
            await File.WriteAllTextAsync(badge, BadgeMarkup("Badge one"));
            await File.WriteAllTextAsync(badge + ".cs", BadgeCode);

            if (!await Until(() => designer.Builds > builds && designer.TypesState == ProjectDesignState.SwapPending, 300))
            {
                return Fail(ref failures, $"L6: the IDE's new control was not built ({designer.TypesState})");
            }

            if (!await Until(() => designer.ProjectEntryFor(badgeFile) is { Control.IsBuilt: false }, 60))
            {
                return Fail(ref failures, "L6: the palette does not offer the new control as not built yet");
            }

            // App.axaml declares a class too, and an application is no control to place.
            if (designer.Toolbox.Any(static entry => entry.Control is { Name: "App" }))
            {
                Fail(ref failures, "L6: the palette offers the application's class as a control");
            }

            if (!await DropOntoAsync(window, designer, "MainWindow.axaml", designer.ProjectEntryFor(badgeFile)!))
            {
                return Fail(ref failures, "L6: there was nowhere on the window to drop the control");
            }

            // Placed only once its class is loaded: nothing lands while the swap is held off.
            await Until(() => FormText(designer, "MainWindow.axaml").Contains("BadgeControl", StringComparison.Ordinal), 2);

            if (FormText(designer, "MainWindow.axaml").Contains("BadgeControl", StringComparison.Ordinal))
            {
                Fail(ref failures, "L6: the control was written into the window before its class was built");
            }
        }
        finally
        {
            designer.SwapsHeldFor = null;
        }

        if (!await Until(() => FormShows(designer, "MainWindow.axaml", "BadgeControl", "Badge one"), 180))
        {
            return Fail(ref failures, "L6: the control was not placed once its types were swapped in");
        }

        // The IDE edits the control's own markup: the window shows it, its session kept.
        WeakReference<XamlLoadSession>? session = SessionOf(designer, "MainWindow.axaml");

        await SaveLikeAnIdeAsync(badge, BadgeMarkup("Badge two"));

        if (!await Until(() => FormShows(designer, "MainWindow.axaml", "BadgeControl", "Badge two"), 60))
        {
            Fail(ref failures, "L6: the IDE's edit of the control's markup does not show on the window");
        }
        else if (!SameSession(designer, "MainWindow.axaml", session))
        {
            Fail(ref failures, "L6: the window was built again for its placed control's markup");
        }

        // Removed on the canvas, in place.
        if (!SelectDrawn(designer, "MainWindow.axaml", "BadgeControl"))
        {
            return Fail(ref failures, "L6: the placed control could not be selected on the canvas");
        }

        designer.DeleteSelectedCommand.Execute(null);

        if (!await Until(() => !FormText(designer, "MainWindow.axaml").Contains("<BadgeControl", StringComparison.Ordinal)
                && !FormText(designer, "MainWindow.axaml").Contains(":BadgeControl", StringComparison.Ordinal), 30))
        {
            Fail(ref failures, "L6: the placed control was not removed");
        }
        else if (!SameSession(designer, "MainWindow.axaml", session))
        {
            Fail(ref failures, "L6: removing the placed control cost the window its session");
        }

        // What follows is written by the IDE, over a window with nothing unsaved.
        await SaveFormAsync(designer, "MainWindow.axaml");

        if (failures == 0)
        {
            Say("L6: a control the IDE wrote is offered not built; dropped, it waits for its build's swap and lands; its markup follows in place, and it is removed in place");
        }

        return failures;
    }

    /// <summary>
    /// L8: the project's view model chosen as a view's data type, design data made of it, a text bound to
    /// its member showing the design instance's value — and, the member renamed in the IDE, a binding that
    /// reads as broken once the types are swapped in.
    /// </summary>
    private static async Task<int> BindToTheProjectsDataAsync(DesignerViewModel designer, string solution)
    {
        var failures = 0;
        string app = Path.Combine(Path.GetDirectoryName(solution)!, "SuiteApp");
        string model = Path.Combine(app, "ViewModels", "CardViewModel.cs");
        string view = Path.Combine(app, "Views", "CardView.axaml");
        int swaps = designer.Swaps;

        // The IDE writes a view model, and a view that binds to nothing yet.
        await File.WriteAllTextAsync(model, CardModel("Caption"));
        await File.WriteAllTextAsync(view, CardViewMarkup);
        await File.WriteAllTextAsync(view + ".cs", CardViewCode);

        if (!await Until(() => designer.Swaps > swaps && designer.TypesState == ProjectDesignState.Live, 300))
        {
            return Fail(ref failures, $"L8: the IDE's view model and view were not built and swapped in ({designer.TypesState})");
        }

        await Until(() => designer.ProjectForms.Any(static form => form.Name == "CardView.axaml"), 60);

        if (!Open(designer, "CardView.axaml") || !await ShowFormAsync(designer, "CardView.axaml"))
        {
            return Fail(ref failures, "L8: the IDE's view did not open");
        }

        // The project's view model, chosen as the view's data type.
        SelectRootOf(designer, "CardView.axaml");

        if (!await Until(() => designer.DataTypeOptions.Any(static option => option.Name == "CardViewModel"), 60))
        {
            return Fail(ref failures, "L8: the data section does not offer the project's view model as a data type");
        }

        designer.DataTypeOptions.First(static option => option.Name == "CardViewModel").Command.Execute(null);

        if (!await Until(() => FormText(designer, "CardView.axaml").Contains(":CardViewModel\"", StringComparison.Ordinal), 30))
        {
            return Fail(ref failures, "L8: choosing the data type did not write x:DataType");
        }

        // Design data made of it.
        SelectRootOf(designer, "CardView.axaml");

        if (!await Until(() => designer.CanCreateDesignData, 30))
        {
            return Fail(ref failures, $"L8: a view model with nothing to make it from was not offered as design data ({designer.DesignDataText})");
        }

        designer.CreateDesignDataCommand.Execute(null);

        if (!await Until(() => FormText(designer, "CardView.axaml").Contains("Design.DataContext", StringComparison.Ordinal), 30))
        {
            return Fail(ref failures, "L8: the design data was not written");
        }

        // The text bound to the model's member, from the inspector.
        if (!SelectNamed(designer, "CardView.axaml", "CardText")
            || !await Until(() => TextRow(designer)?.BindOptions.Any(static option => option.Name == "Caption") == true, 60))
        {
            return Fail(ref failures, "L8: the text's row does not offer the model's Caption to bind to");
        }

        TextRow(designer)!.BindOptions.First(static option => option.Name == "Caption").Command.Execute(null);

        if (!await Until(() => FormText(designer, "CardView.axaml").Contains("{Binding Caption}", StringComparison.Ordinal)
                && FormShowsText(designer, "CardView.axaml", "Design caption"), 60))
        {
            return Fail(ref failures, "L8: the binding was not written, or the canvas does not show the design instance's value");
        }

        // The IDE renames the member: built, swapped, and the binding reads as broken.
        swaps = designer.Swaps;

        await SaveLikeAnIdeAsync(model, CardModel("Heading"));

        if (!await Until(() => designer.Swaps > swaps && designer.TypesState == ProjectDesignState.Live, 300))
        {
            return Fail(ref failures, $"L8: the renamed member was not built and swapped in ({designer.TypesState})");
        }

        await ShowFormAsync(designer, "CardView.axaml");

        if (!SelectNamed(designer, "CardView.axaml", "CardText")
            || !await Until(() => TextRow(designer) is { Binding: BindingState.Broken }, 60))
        {
            Fail(ref failures, $"L8: a binding to a member renamed in the IDE does not read as broken ({TextRow(designer)?.Binding})");
        }
        else if (failures == 0)
        {
            Say($"L8: the project's view model is a view's data type and design data; Text bound to it shows the design instance; renamed in the IDE, the binding reads as broken — {TextRow(designer)!.BindingMessage}");
        }

        // The view's edits are the person's, not the file's; the window comes back to the front for what follows.
        await ShowFormAsync(designer, "MainWindow.axaml");

        return failures;
    }

    /// <summary>
    /// L10: a window with a class, a handler and a data type changes structurally in place — beside the
    /// handled button, and at its root — keeping its session, running the handler on itself, and leaving
    /// the platform no window more than before.
    /// </summary>
    private static async Task<int> ChangeAFormWithAClassInPlaceAsync(DesignerViewModel designer, string solution)
    {
        var failures = 0;
        string window = Path.Combine(Path.GetDirectoryName(solution)!, "SuiteApp", "Views", "MainWindow.axaml");
        int swaps = designer.Swaps;

        if (!await ShowFormAsync(designer, "MainWindow.axaml"))
        {
            return Fail(ref failures, "L10: the application's window would not come to the front");
        }

        // The IDE gives the window's class a handler, then a button that uses it.
        await SaveLikeAnIdeAsync(window + ".cs", Disk(window + ".cs").Replace(
            "public MainWindow() => InitializeComponent();",
            "public MainWindow() => InitializeComponent();\n\n    private void Ping(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Title = \"Pinged\";",
            StringComparison.Ordinal));

        if (!await Until(() => designer.Swaps > swaps && designer.TypesState == ProjectDesignState.Live, 300))
        {
            return Fail(ref failures, $"L10: the window's handler was not built and swapped in ({designer.TypesState})");
        }

        await ShowFormAsync(designer, "MainWindow.axaml");

        await SaveLikeAnIdeAsync(window, Disk(window).Replace(
            "<lib:LibCard />",
            "<lib:LibCard />\n        <Button x:Name=\"PingButton\" Content=\"Ping\" Click=\"Ping\" />",
            StringComparison.Ordinal));

        if (!await Until(() => FormHasNamed(designer, "MainWindow.axaml", "PingButton"), 60))
        {
            return Fail(ref failures, "L10: the IDE's button never reached the window");
        }

        int before = PlatformWindows();
        WeakReference<XamlLoadSession>? session = SessionOf(designer, "MainWindow.axaml");

        // Beside the handled button: the panel is built again, and the handler hooked up to the window.
        await SaveLikeAnIdeAsync(window, Disk(window).Replace(
            "<Button x:Name=\"PingButton\"",
            "<TextBlock Text=\"Beside the button\" />\n        <Button x:Name=\"PingButton\"",
            StringComparison.Ordinal));

        if (!await Until(() => FormShowsText(designer, "MainWindow.axaml", "Beside the button"), 60))
        {
            Fail(ref failures, "L10: a change beside the handled button never reached the window");
        }

        // At the root: resources the window did not have, moved onto it from a copy of it.
        int settled = designer.SettledBatches;

        await SaveLikeAnIdeAsync(window, Disk(window).Replace(
            "<StackPanel",
            "<Window.Resources>\n    <SolidColorBrush x:Key=\"Accent\" Color=\"#3D7EFF\" />\n  </Window.Resources>\n\n  <StackPanel",
            StringComparison.Ordinal));

        if (!await Until(() => designer.SettledBatches > settled && FormText(designer, "MainWindow.axaml").Contains("x:Key=\"Accent\"", StringComparison.Ordinal), 60))
        {
            Fail(ref failures, "L10: the window's new resources never reached it");
        }

        if (!SameSession(designer, "MainWindow.axaml", session))
        {
            Fail(ref failures, "L10: a structural change of a window with a class cost it its session");
        }

        if (!Ping(designer, "MainWindow.axaml"))
        {
            Fail(ref failures, "L10: the rebuilt button's handler did not run on the window");
        }

        // A copy of the window left open is a window more on the platform's list, for as long as the
        // process runs; a tooltip shown meanwhile is one for a moment.
        int after = PlatformWindows();

        if (before >= 0 && after > before)
        {
            await Until(() => PlatformWindows() <= before, 3);

            after = PlatformWindows();
        }

        if (before >= 0 && after > before)
        {
            Fail(ref failures, $"L10: the platform holds {after - before} window(s) more after the changes — a copy was left open");
        }
        else if (failures == 0)
        {
            Say($"L10: a window with a class, a handler and a data type changed structurally in place — same session, the handler runs on it, {(before < 0 ? "the platform's windows not counted here" : "no window more on the platform's list")}");
        }

        return failures;
    }

    /// <summary>Puts a form in front and waits until it shows.</summary>
    private static async Task<bool> ShowFormAsync(DesignerViewModel designer, string name)
    {
        // A form just asked to open joins the tabs a moment later.
        await Until(() => designer.Forms.Any(form => form.Name == name), 60);

        if (designer.Forms.FirstOrDefault(form => form.Name == name) is not { } form)
        {
            return false;
        }

        designer.ActiveForm = form;

        return await Until(() => designer.ActiveForm is { Session: not null, Problem: null } shown && shown.Name == name, 120);
    }

    /// <summary>Saves a form from the designer, so that what the IDE writes next is no conflict.</summary>
    private static async Task SaveFormAsync(DesignerViewModel designer, string name)
    {
        if (await ShowFormAsync(designer, name) && designer.ActiveForm is { IsDirty: true })
        {
            designer.SaveCommand.Execute(null);

            await Until(() => designer.ActiveForm is { IsDirty: false }, 30);
        }
    }

    /// <summary>Drops a palette entry at the end of a form's panel, the way a person drops it.</summary>
    private static async Task<bool> DropOntoAsync(MainWindow window, DesignerViewModel designer, string name, ToolboxEntry entry) =>
        designer.Forms.FirstOrDefault(form => form.Name == name) is { } form
        && await DropIntoAsync(window, designer, form, entry, () => Find(designer, "StackPanel"));

    /// <summary>What a form's document says now, or nothing when it is not open.</summary>
    private static string FormText(DesignerViewModel designer, string name) =>
        designer.Forms.FirstOrDefault(form => form.Name == name) is { } form ? Text(form) : string.Empty;

    /// <summary>Whether a form's document names an element so.</summary>
    private static bool FormHasNamed(DesignerViewModel designer, string name, string element) =>
        FormText(designer, name).Contains($"x:Name=\"{element}\"", StringComparison.Ordinal);

    /// <summary>Whether a form's canvas draws a text block saying exactly this.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool FormShowsText(DesignerViewModel designer, string name, string text) =>
        designer.Forms.FirstOrDefault(form => form.Name == name) is { } form && OnCanvasText(form, text);

    /// <summary>A weak reference to a form's session, to tell later whether it is still the same one.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<XamlLoadSession>? SessionOf(DesignerViewModel designer, string name) =>
        designer.Forms.FirstOrDefault(form => form.Name == name)?.Session is { } session ? new WeakReference<XamlLoadSession>(session) : null;

    /// <summary>Whether a form's session is the one remembered.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool SameSession(DesignerViewModel designer, string name, WeakReference<XamlLoadSession>? remembered) =>
        remembered is not null
        && remembered.TryGetTarget(out XamlLoadSession? session)
        && ReferenceEquals(designer.Forms.FirstOrDefault(form => form.Name == name)?.Session, session);

    /// <summary>Selects the first control of a type a form's canvas draws, as a click does.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool SelectDrawn(DesignerViewModel designer, string name, string typeName)
    {
        if (designer.Forms.FirstOrDefault(form => form.Name == name) is not { } form || Drawn(form, typeName) is not { } control)
        {
            return false;
        }

        designer.SelectFromCanvas(form, control);

        return true;
    }

    /// <summary>Selects a form's root, as a click on its card does.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SelectRootOf(DesignerViewModel designer, string name)
    {
        if (designer.Forms.FirstOrDefault(form => form.Name == name) is { Root: Control root } form)
        {
            designer.SelectFromCanvas(form, root);
        }
    }

    /// <summary>Selects the control a form's element of that <c>x:Name</c> produced.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool SelectNamed(DesignerViewModel designer, string name, string element)
    {
        if (designer.Forms.FirstOrDefault(form => form.Name == name) is not { } form || LiveByName(designer, element) is not { } control)
        {
            return false;
        }

        designer.SelectFromCanvas(form, control);

        return true;
    }

    /// <summary>Clicks the window's handled button and says whether its handler ran on the window.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Ping(DesignerViewModel designer, string name)
    {
        if (designer.Forms.FirstOrDefault(form => form.Name == name) is not { Root: Window root } form
            || Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(form.Card)
                .OfType<Button>()
                .FirstOrDefault(static button => button.Name == "PingButton") is not { } ping)
        {
            return false;
        }

        ping.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        return root.Title == "Pinged";
    }

    /// <summary>
    /// How many top-level windows the platform holds for this process and shows nowhere — what a copy of
    /// a window nobody closed adds to; -1 where nothing counts them.
    /// </summary>
    private static int PlatformWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return -1;
        }

        int count = 0;
        uint self = (uint)System.Environment.ProcessId;

        EnumWindows(
            (handle, _) =>
            {
                if (GetWindowThreadProcessId(handle, out uint owner) != 0 && owner == self && !IsWindowVisible(handle))
                {
                    count++;
                }

                return true;
            },
            IntPtr.Zero);

        return count;
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr state);

    // DllImport rather than LibraryImport, as DesignerViewModel.Run.cs says why: three entry points of a
    // check are not a reason to open the sample's compilation to unsafe code.
#pragma warning disable SYSLIB1054
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr state);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint process);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);
#pragma warning restore SYSLIB1054

    /// <summary>
    /// L11: a user control the IDE writes with nothing in it takes a control from the palette as its
    /// content, and undo gives the file's text back.
    /// </summary>
    /// <remarks>
    /// The designer leaves a form's root unmarked — the card stands for it — and the editor looked for a
    /// place only among marked controls, asking the card for its root only when the root was a window: an
    /// empty user control took no drop at all, with nothing in the console to say why.
    /// </remarks>
    private static async Task<int> DropIntoAnEmptyControlAsync(MainWindow window, DesignerViewModel designer, string solution)
    {
        string? inFront = designer.ActiveForm?.Name;
        var failures = 0;

        try
        {
            failures += await DropIntoTheEmptyControlAsync(window, designer, solution);
        }
        finally
        {
            // The steps after this one expect the form they left in front, whatever this one came to.
            if (inFront is not null && !await ShowFormAsync(designer, inFront))
            {
                Fail(ref failures, $"L11: {inFront} would not come back to the front");
            }
        }

        return failures;
    }

    /// <summary>What <see cref="DropIntoAnEmptyControlAsync"/> checks, with the form in front left to it.</summary>
    private static async Task<int> DropIntoTheEmptyControlAsync(MainWindow window, DesignerViewModel designer, string solution)
    {
        var failures = 0;
        string empty = Path.Combine(Path.GetDirectoryName(solution)!, "SuiteApp", "Views", "EmptyView.axaml");
        int swaps = designer.Swaps;

        await File.WriteAllTextAsync(empty + ".cs", EmptyViewCode);
        await File.WriteAllTextAsync(empty, EmptyViewMarkup);

        // Its class is built and swapped in before it is opened, as a form a person opens after the build.
        if (!await Until(() => designer.Swaps > swaps && designer.TypesState == ProjectDesignState.Live, 300)
            || !await Until(() => designer.ProjectForms.Any(static form => form.Name == "EmptyView.axaml"), 60)
            || await designer.OpenByNameAsync("EmptyView.axaml") is not null
            || designer.Forms.FirstOrDefault(static form => form.Name == "EmptyView.axaml") is not { } form)
        {
            return Fail(ref failures, $"L11: the IDE's empty user control did not open ({designer.TypesState})");
        }

        string start = Text(form);

        if (window.FindControl<UiDesignerView>("Surface") is not { } editor
            || await PlaceAtAsync(
                editor,
                form,
                () => LiveOf<Control>(form, form.Document?.Root),
                static root => new Point(root.Bounds.Width / 2, root.Bounds.Height / 2)) is not
                { Kind: SurfaceDropKind.Content } placement)
        {
            return Fail(ref failures, "L11: over an empty user control the editor offers no place");
        }

        if (!await DropAtAsync(editor, designer, form, "Button", placement))
        {
            Fail(ref failures, "L11: the Button dropped onto the empty user control never reached the document");
        }
        else if (form.Document?.Root?.ContentElements.SingleOrDefault() is not { Name.LocalName: "Button" } written)
        {
            Fail(ref failures, "L11: the Button is not the user control's content in the document");
        }
        else if (!await Until(() => CaughtUp(form) && LiveOf<Button>(form, written) is { Bounds.Height: > 0 }, 30))
        {
            Fail(ref failures, "L11: the canvas does not show the Button in the user control");
        }
        else
        {
            Say("L11: a user control the IDE wrote with nothing in it takes a control from the palette as its content");
        }

        if (!await UndoToAsync(designer, form, start))
        {
            Fail(ref failures, "L11: undoing the drop did not give the user control's text back");
        }

        return failures;
    }

    /// <summary>A user control as an IDE leaves one emptied: a class, a size, nothing inside.</summary>
    private const string EmptyViewMarkup =
        """
        <UserControl xmlns="https://github.com/avaloniaui"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     x:Class="SuiteApp.Views.EmptyView"
                     Width="480" Height="300">


        </UserControl>

        """;

    private const string EmptyViewCode =
        """
        using Avalonia.Controls;
        using Avalonia.Markup.Xaml;

        namespace SuiteApp.Views;

        public partial class EmptyView : UserControl
        {
            public EmptyView() => AvaloniaXamlLoader.Load(this);
        }

        """;

    private static string BadgeMarkup(string text) =>
        $$"""
        <UserControl xmlns="https://github.com/avaloniaui"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     x:Class="SuiteApp.Views.BadgeControl">
          <Border Padding="6">
            <TextBlock Text="{{text}}" />
          </Border>
        </UserControl>

        """;

    private const string BadgeCode =
        """
        using Avalonia.Controls;
        using Avalonia.Markup.Xaml;

        namespace SuiteApp.Views;

        public partial class BadgeControl : UserControl
        {
            public BadgeControl() => AvaloniaXamlLoader.Load(this);
        }

        """;

    private static string CardModel(string member) =>
        $$"""
        namespace SuiteApp.ViewModels;

        public sealed class CardViewModel
        {
            public string {{member}} { get; } = "Design caption";
        }

        """;

    private const string CardViewMarkup =
        """
        <UserControl xmlns="https://github.com/avaloniaui"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     x:Class="SuiteApp.Views.CardView">
          <StackPanel Margin="12">
            <TextBlock x:Name="CardText" Text="unbound" />
          </StackPanel>
        </UserControl>

        """;

    private const string CardViewCode =
        """
        using Avalonia.Controls;
        using Avalonia.Markup.Xaml;

        namespace SuiteApp.Views;

        public partial class CardView : UserControl
        {
            public CardView() => AvaloniaXamlLoader.Load(this);
        }

        """;
}
