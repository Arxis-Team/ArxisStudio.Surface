using System;
using System.Globalization;
using System.Threading.Tasks;
using ArxisStudio.ProjectSystem;

namespace UiDesigner.Demo.ViewModels;

/// <summary>What kind of document the New button makes.</summary>
/// <remarks>
/// The two roots an application is built out of: a window is something a user is shown, a user
/// control is something a window is made of. Everything else Avalonia can root a document at —
/// styles, resource dictionaries — is not a form and is not offered here for the same reason the
/// project panel does not open one.
/// </remarks>
public enum NewFormKind
{
    Window,
    UserControl,
}

/// <summary>The answer to "what shall we call it, and what is it".</summary>
public sealed record NewFormRequest(string Name, NewFormKind Kind);

public sealed partial class DesignerViewModel
{
    /// <summary>Asked of the view: the name and kind of a new form.</summary>
    public Func<NewFormRequest, Task<NewFormRequest?>>? AskForNewForm { get; set; }

    /// <summary>
    /// Creates a form in the project and opens it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A window or a user control, because those are two different things and the button used to
    /// make only the second — while calling it "NewForm", which is the name of neither. What is
    /// chosen decides the root element, the suggested name and the size the document starts at.
    /// </para>
    /// <para>
    /// Both come with an <c>x:Class</c> and a code-behind file. A form without one cannot be shown
    /// by the application that owns it — <c>new SettingsWindow()</c> needs a type — so a designer
    /// that made classless documents made things that could be laid out and never used. The class is
    /// what the other editor then writes code in, which is the whole point of the two tools sitting
    /// side by side.
    /// </para>
    /// <para>
    /// The namespace is the project's, and the folder is the project's own directory. A form in a
    /// sub-folder would need the namespace to follow the folder to match what the compiler expects
    /// of the code-behind, and the panel this is started from does not say which folder it means.
    /// </para>
    /// <para>
    /// A canvas inside, because a canvas is the one panel where dropping a control at a point means
    /// what it looks like it means. A form starting as a <c>Grid</c> would take every drop into the
    /// same cell and stack the controls on top of one another, which reads as a broken designer
    /// rather than a correct one.
    /// </para>
    /// </remarks>
    private async Task NewFormAsync()
    {
        if (Project() is not { } project)
        {
            return;
        }

        if (AskForNewForm is null
            || await AskForNewForm(new NewFormRequest("NewWindow", NewFormKind.Window)) is not { } asked
            || asked.Name.Trim() is not { Length: > 0 } chosen)
        {
            return;
        }

        string name = chosen.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)
            ? chosen[..^".axaml".Length]
            : chosen;

        if (!ProjectScaffold.IsUsableName(name))
        {
            Log($"! {name} is not a name a class can have");

            return;
        }

        CanonicalPath file = project.ProjectDirectory.Combine(name + ".axaml");

        if (System.IO.File.Exists(file.Value))
        {
            Log($"! {name}.axaml already exists");

            return;
        }

        string space = project.Properties.TryGetValue("RootNamespace", out string? declared)
            && declared is { Length: > 0 }
                ? declared
                : project.Name;

        await System.IO.File.WriteAllTextAsync(
            file.Value, Markup(asked.Kind, space, name), _shutdown.Token);

        await System.IO.File.WriteAllTextAsync(
            file.Value + ".cs", CodeBehind(asked.Kind, space, name), _shutdown.Token);

        Log($"Created {name}.axaml and {name}.axaml.cs");

        // The project has to be read again before the new files are items of it: the SDK's globs are
        // evaluated, and a file that appeared after the evaluation is not in the result. This is the
        // same reason a restore is followed by a refresh.
        await _workspace.RefreshAsync(_shutdown.Token);

        await OpenFormAsync(new FormFile(name + ".axaml", project.Name, file, project.Identity));
    }

    private static string Markup(NewFormKind kind, string space, string name) => kind switch
    {
        NewFormKind.Window => string.Create(
            CultureInfo.InvariantCulture,
            $"""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="{space}.{name}"
                    Title="{name}"
                    Width="800" Height="500">

              <Canvas>
              </Canvas>

            </Window>

            """),

        _ => string.Create(
            CultureInfo.InvariantCulture,
            $"""
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="{space}.{name}"
                         Width="480" Height="320">

              <Canvas>
              </Canvas>

            </UserControl>

            """),
    };

    private static string CodeBehind(NewFormKind kind, string space, string name) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""
        using Avalonia.Controls;
        using Avalonia.Markup.Xaml;

        namespace {{space}};

        public partial class {{name}} : {{(kind == NewFormKind.Window ? "Window" : "UserControl")}}
        {
            public {{name}}() => InitializeComponent();

            private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
        }

        """);
}
