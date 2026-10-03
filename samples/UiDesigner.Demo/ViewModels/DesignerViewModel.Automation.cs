using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.ProjectSystem.Markup.Xaml;
using Avalonia.Threading;

namespace UiDesigner.Demo.ViewModels;

/// <summary>
/// What the automation channel asks of the designer, by names: forms by file name, elements by
/// <c>x:Name</c>, palette entries by the control they place.
/// </summary>
/// <remarks>
/// <para>
/// A channel addressing the designer by pointer positions would be checking where things are drawn;
/// one addressing it by names checks what it does. Each method goes the way the person's action goes —
/// the form opened as a double click opens it, a drop placed through the drop's own path, an edit
/// through the form's document — and answers what went wrong, or nothing.
/// </para>
/// <para>
/// Every one of them waits for what it started, so an answer is about the designer after the act.
/// </para>
/// </remarks>
public sealed partial class DesignerViewModel
{
    /// <summary>Opens a form of the project by its file name, and waits until it shows.</summary>
    /// <returns>What went wrong, or <see langword="null"/>.</returns>
    internal async Task<string?> OpenByNameAsync(string name)
    {
        if (ProjectForms.FirstOrDefault(form => form.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is not { } found)
        {
            return $"no form called {name} in this project";
        }

        await OpenFormAsync(found);

        return Forms.FirstOrDefault(open => open.File == found.Path) is { Session: not null }
            ? null
            : $"{name} did not open: " + (ActiveForm?.Problem ?? "no session");
    }

    /// <summary>
    /// Puts a palette entry into the form in front: into the element of that <c>x:Name</c> — the root when
    /// none is named — at an index among its content, the end when none is given.
    /// </summary>
    /// <remarks>
    /// The drop's own path without the pointer: a project control not built yet is built first and placed
    /// after the swap, an Avalonia control is written at once. Nothing is placed by position — a Canvas
    /// child goes in at its origin — because a position is the pointer's to say.
    /// </remarks>
    /// <returns>What went wrong, or <see langword="null"/>.</returns>
    internal async Task<string?> PutAsync(string entryName, string? parentName, int? index)
    {
        if (ActiveForm is not { Document.Root: { } root } form)
        {
            return "no form is in front";
        }

        if (Toolbox.FirstOrDefault(entry => entry.Name == entryName) is not { } entry)
        {
            return $"the palette has no {entryName}";
        }

        if (Named(root, parentName) is not { } parent)
        {
            return $"no element called {parentName} in {form.Name}";
        }

        var intent = new DropIntent(
            form.File,
            XamlElementPath.Of(parent),
            Math.Clamp(index ?? IndexAtEnd(parent), 0, IndexAtEnd(parent)),
            entry.Fragment());

        if (entry.Control is { } control)
        {
            if (control.Document == form.File)
            {
                return $"{entryName} cannot be placed inside its own form";
            }

            await PlaceAsync(control, intent);

            return null;
        }

        await ApplyAsync(
            form,
            editor =>
            {
                if (intent.Parent.Resolve(editor.Document) is { } holder)
                {
                    editor.InsertFragment(holder, intent.Index, intent.Fragment);
                }
            },
            $"add {entryName}");

        return null;
    }

    /// <summary>
    /// Writes a property of the element of that <c>x:Name</c> in the form in front — the root when none
    /// is named — as the inspector's row writes it; <c>x:Name</c> itself renames it.
    /// </summary>
    /// <returns>What went wrong, or <see langword="null"/>.</returns>
    internal async Task<string?> SetByNameAsync(string? target, string property, string value)
    {
        if (ActiveForm is not { Document.Root: { } root } form)
        {
            return "no form is in front";
        }

        if (Named(root, target) is not { } element)
        {
            return $"no element called {target} in {form.Name}";
        }

        if (property is "x:Name" or "Name")
        {
            if (NameError(element, value) is { } error)
            {
                return error;
            }

            await RenameAsync(element, value);
        }
        else
        {
            await SetPropertyAsync(element, property, value);
        }

        return null;
    }

    /// <summary>Asks the form in front's file again, as the host does when the file changes.</summary>
    /// <returns>What it came to.</returns>
    internal async Task<string> ReloadActiveAsync()
    {
        if (_host is not { } host || ActiveForm?.Live is not { } live)
        {
            return "no form is in front";
        }

        return (await host.ReloadAsync(live, _shutdown.Token)).Outcome.ToString();
    }

    /// <summary>Builds the design set as the host builds it, and says how it went.</summary>
    internal async Task<string> BuildByRequestAsync()
    {
        if (_host is not { } host)
        {
            return "no project is open";
        }

        ProjectDesignBuildResult result = await host.BuildAsync("the automation channel asked", cancellationToken: _shutdown.Token);

        return result.ToString();
    }

    /// <summary>Swaps the project's types now, whatever holds the swap off, and says how it went.</summary>
    internal async Task<string> SwapByRequestAsync()
    {
        if (_host is not { } host)
        {
            return "no project is open";
        }

        return (await host.SwapAsync("the automation channel asked", _shutdown.Token)).ToString();
    }

    /// <summary>What a restart would hand the next copy now, without restarting.</summary>
    internal string HandoffText() => JsonSerializer.Serialize(Capture(byItself: false), HandoffJson);

    /// <summary>The element of an <c>x:Name</c> under a root, or the root when no name is given.</summary>
    private static XamlElement? Named(XamlElement root, string? name) =>
        string.IsNullOrEmpty(name)
            ? root
            : root.DescendantElements().Prepend(root).FirstOrDefault(element => element.GetDirective("Name") == name);
}
