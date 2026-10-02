using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using Avalonia;
using Avalonia.Controls;
using UiDesigner.Demo.ViewModels;

namespace UiDesigner.Demo.Views;

/// <summary>
/// <c>--live &lt;folder&gt;</c>: the designer beside an IDE that writes the same files.
/// </summary>
/// <remarks>
/// <para>
/// The other runs check what the designer does by itself. This one plays the other editor too: it
/// writes the open form's file the way JetBrains Rider saves — the text to a temporary file, the
/// original renamed out of the way, the temporary renamed over it, the original deleted — and
/// checks that the form follows, that its history and its saved state tell the truth, and that a
/// write over unsaved edits is a question rather than a loss. Part 1 of the plan's <c>--live</c>:
/// an outside edit of a clean form (L1), the same over unsaved edits with both answers (L2), a
/// binding made from the inspector against the form's design data, and undo across an outside edit.
/// Part 2 is <see cref="BesideTheIdeAsync"/>: a save that reads nothing again, a new file read once,
/// and the designer's builds beside the IDE's. Part 3 is <see cref="TypesBesideTheIdeAsync"/>: the
/// IDE saves code, and the designer swaps the project's types in place — or, when they will not go,
/// restarts by itself with the session carried across.
/// </para>
/// <para>
/// Every step goes through the view model, as the other runs do; only the IDE's side writes files.
/// </para>
/// </remarks>
internal static partial class StudioCheck
{
    /// <summary>Runs the live run instead of the others.</summary>
    public static void LiveWhenShown(MainWindow window, DesignerViewModel designer, string folder) =>
        window.Opened += (_, _) => _ = LiveRunAsync(window, designer, folder);

    /// <summary>
    /// Said once the verdict is out — what a restart under way waits on before it lets this process go.
    /// </summary>
    private static readonly TaskCompletionSource VerdictSaid = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task LiveRunAsync(MainWindow window, DesignerViewModel designer, string folder)
    {
        var failures = 0;

        try
        {
            string project = await ProjectScaffold.CreateAsync(folder, "LiveApp", CancellationToken.None);

            failures = await LiveAsync(window, designer, project);

            // Part 3 only once parts 1 and 2 have returned: the forms they held were built under the types
            // part 3 replaces, and a frame of theirs still running would hold those types in the process.
            if (designer.Host is null)
            {
                Fail(ref failures, "part 3: no project is open to swap the types of");
            }
            else
            {
                failures += await TypesBesideTheIdeAsync(window, designer, folder, project);
            }
        }
        catch (Exception error)
        {
            Say($"the live run itself failed: {error.GetType().Name}: {error.Message}");

            failures++;
        }

        Say(failures == 0
            ? "VERDICT ok — the designer followed an IDE writing its forms and its code"
            : $"VERDICT {failures} step(s) failed");

        VerdictSaid.TrySetResult();

        window.Close();
    }

    private static async Task<int> LiveAsync(Window window, DesignerViewModel designer, string project)
    {
        var failures = 0;

        // The IDE's writes touch the project, and swapping the types is what part 3 is about: until
        // then every swap waits, and the designer never restarts itself under the run.
        designer.SwapsHeldFor = "the self-check";
        designer.RestartsByItself = false;

        designer.OpenAtStartup(project, "MainWindow.axaml");

        if (!await Until(() => designer.ActiveForm is { Session: not null, Problem: null }, 300))
        {
            return Fail(ref failures, "the window never opened: " + (designer.ActiveForm?.Problem ?? "no form"));
        }

        FormViewModel form = designer.ActiveForm!;
        XamlLiveDocument live = form.Live!;
        string file = form.File.Value;

        // L1. The IDE saves the open form while it has nothing unsaved here: the form follows, the
        //     change is one step of its history, nothing is unsaved, and the selection stays put.
        SelectTextBlock(designer, form, 1);

        XamlElementPath? selected = designer.Selected is { } before ? XamlElementPath.Of(before) : null;

        // Waited for as a batch the design host has dealt with, not only as text: the host takes the
        // file off the user interface thread, and the document's text moves before the canvas does.
        int settled = designer.SettledBatches;

        await SaveLikeAnIdeAsync(file, Text(form).Replace("Opacity=\"0.75\"", "Opacity=\"0.5\"", StringComparison.Ordinal));

        if (!await Until(() => Text(form).Contains("Opacity=\"0.5\"", StringComparison.Ordinal) && designer.SettledBatches > settled, 60))
        {
            Fail(ref failures, "L1: the IDE's save never reached the open form");
        }
        else
        {
            if (form.IsDirty)
            {
                Fail(ref failures, "L1: a form that shows its file calls itself edited");
            }

            if (live.UndoDescription != "Changed outside the designer")
            {
                Fail(ref failures, $"L1: the IDE's save is not a step of the form's history ({live.UndoDescription ?? "none"})");
            }

            if (selected is not null
                && (designer.Selected is not { } after || !XamlElementPath.Of(after).Equals(selected)))
            {
                Fail(ref failures, "L1: the selection moved when the file changed");
            }

            if (TextBlocks(form).ElementAtOrDefault(1) is not { Opacity: 0.5 })
            {
                Fail(ref failures, "L1: the canvas does not show the new opacity");
            }

            Say("L1: an IDE's save of a clean form is shown, is one undo step, and leaves it saved");
        }

        // L2. The same over unsaved edits: nothing is overwritten, the bar asks, and either answer
        //     leaves the text, the saved state and the canvas agreeing.
        if (!await EditTitleAsync(designer, form, "Mine"))
        {
            Fail(ref failures, "L2: the Title edit never reached the document");
        }

        string mine = Text(form);

        settled = designer.SettledBatches;

        await SaveLikeAnIdeAsync(file, Disk(file).Replace("Opacity=\"0.5\"", "Opacity=\"0.25\"", StringComparison.Ordinal));

        if (!await Until(() => form.HasPendingDiskText && designer.SettledBatches > settled, 60))
        {
            Fail(ref failures, "L2: a write over unsaved edits was not put to the person");
        }
        else
        {
            if (Text(form) != mine)
            {
                Fail(ref failures, "L2: the write over unsaved edits changed the form before anybody answered");
            }

            designer.KeepMyTextCommand.Execute(null);

            if (!await Until(() => !form.HasPendingDiskText, 30) || Text(form) != mine || !form.IsDirty)
            {
                Fail(ref failures, "L2: keeping my edits did not keep them, or the form stopped calling itself edited");
            }

            if (live.SavedText.ToString() != Disk(file))
            {
                Fail(ref failures, "L2: kept edits are not compared with what the file now says");
            }

            Say("L2: keep mine — the edits stay, the form reads as changed against the file");
        }

        settled = designer.SettledBatches;

        await SaveLikeAnIdeAsync(file, Disk(file).Replace("Opacity=\"0.25\"", "Opacity=\"0.3\"", StringComparison.Ordinal));

        if (!await Until(() => form.HasPendingDiskText && designer.SettledBatches > settled, 60))
        {
            Fail(ref failures, "L2: the second write over unsaved edits was not put to the person");
        }
        else
        {
            string theirs = Disk(file);

            designer.TakeDiskTextCommand.Execute(null);

            if (!await Until(() => Text(form) == theirs, 30) || form.IsDirty)
            {
                Fail(ref failures, "L2: taking the file did not show the file, or left the form edited");
            }

            if (TextBlocks(form).ElementAtOrDefault(1) is not { Opacity: 0.3 })
            {
                Fail(ref failures, "L2: the canvas does not show the file that was taken");
            }

            // Undo across the outside edit: the person's own text comes back, and it is not the file.
            designer.UndoCommand.Execute(null);

            if (!await Until(() => Text(form) == mine, 30) || !form.IsDirty)
            {
                Fail(ref failures, "L2: undo did not bring back the edits the file was taken over");
            }

            designer.RedoCommand.Execute(null);

            if (!await Until(() => Text(form) == theirs, 30) || form.IsDirty)
            {
                Fail(ref failures, "L2: redo did not take the file again");
            }

            Say("L2: take the file — shown and saved, the edits one undo away, and back again");
        }

        // Binding from the inspector, against the form's design data.
        SelectTextBlock(designer, form, 0);

        if (!await Until(() => TextRow(designer) is { Binding: BindingState.Resolved }, 30))
        {
            Fail(ref failures, "binding: {Binding Title} on the first text block is not read as resolved");
        }

        if (!designer.DataTypeText.Contains("vm:MainWindowViewModel", StringComparison.Ordinal)
            || designer.DesignDataText != "MainWindowViewModel"
            || designer.CanCreateDesignData)
        {
            Fail(ref failures, $"binding: the data section says '{designer.DataTypeText}' / '{designer.DesignDataText}'");
        }

        // A text block with text of its own, dropped from the toolbox, is what gets bound: both of the
        // scaffold's are bound already.
        if (designer.Toolbox.FirstOrDefault(static entry => entry.Name == "TextBlock") is not { } tool
            || Find(designer, "StackPanel") is not { } panel)
        {
            Fail(ref failures, "binding: no TextBlock in the toolbox, or no panel to drop it into");
        }
        else
        {
            await DropIntoAsync(window, designer, form, tool, () => panel);

            await Until(() => TextBlocks(form).Length == 3, 30);
        }

        SelectTextBlock(designer, form, 2);

        if (!await Until(() => TextRow(designer) is { BindOptions.Count: > 0 }, 30)
            || TextRow(designer)?.BindOptions.FirstOrDefault(option => option.Name == "Title") is not { } title)
        {
            Fail(ref failures, "binding: the dropped text block's Text offers no member to bind to");
        }
        else
        {
            title.Command.Execute(null);

            if (!await Until(() => TextBlocks(form).ElementAtOrDefault(2) is { Text: "Ready to go" }, 30)
                || Text(form).Split("{Binding Title}").Length != 3)
            {
                Fail(ref failures, "binding: Text bound to Title does not show the design data's title");
            }
            else
            {
                Say("binding: Text bound to a member from the inspector shows the design instance's value");
            }
        }

        // The other editor renames the member the binding names — a typo, as far as the path goes.
        designer.SaveCommand.Execute(null);

        await Until(() => !form.IsDirty, 30);

        int second = Disk(file).LastIndexOf("{Binding Title}", StringComparison.Ordinal);
        string renamed = Disk(file);

        await SaveLikeAnIdeAsync(file, renamed[..second] + "{Binding Titel}" + renamed[(second + "{Binding Title}".Length)..]);

        if (!await Until(() => Text(form).Contains("{Binding Titel}", StringComparison.Ordinal), 60))
        {
            Fail(ref failures, "binding: the IDE's rename never reached the form");
        }
        else
        {
            SelectTextBlock(designer, form, 2);

            if (!await Until(() => TextRow(designer) is { Binding: BindingState.Broken }, 30))
            {
                Fail(ref failures, "binding: a path naming no member is not shown as broken");
            }
            else
            {
                Say($"binding: a path the data does not have reads as broken — {TextRow(designer)!.BindingMessage}");
            }
        }

        // Design data written by the designer, once the IDE has taken it out.
        string withoutDesign = Disk(file);
        int open = withoutDesign.IndexOf("<Design.DataContext>", StringComparison.Ordinal);
        int close = withoutDesign.IndexOf("</Design.DataContext>", StringComparison.Ordinal);

        if (open < 0 || close < 0)
        {
            Fail(ref failures, "design data: the scaffolded window writes no Design.DataContext");
        }
        else
        {
            int start = withoutDesign.LastIndexOf('\n', open) + 1;
            int end = withoutDesign.IndexOf('\n', close) + 1;

            await SaveLikeAnIdeAsync(file, withoutDesign[..start] + withoutDesign[end..]);

            if (!await Until(() => !Text(form).Contains("Design.DataContext", StringComparison.Ordinal), 60))
            {
                Fail(ref failures, "design data: the IDE's removal never reached the form");
            }

            SelectTextBlock(designer, form, 0);

            if (!await Until(() => designer.CanCreateDesignData, 30))
            {
                Fail(ref failures, $"design data: a creatable data type with no design data offers none ({designer.DesignDataText})");
            }
            else
            {
                designer.CreateDesignDataCommand.Execute(null);

                if (!await Until(() => Text(form).Contains("<vm:MainWindowViewModel />", StringComparison.Ordinal), 30)
                    || !await Until(() => TextBlocks(form).FirstOrDefault() is { Text: "Ready to go" }, 30))
                {
                    Fail(ref failures, "design data: Design.DataContext was not written, or the canvas does not show it");
                }
                else
                {
                    Say("design data: written as a property element, and the canvas shows the instance again");
                }
            }
        }

        // Part 2: the IDE writes the project and builds it, and the designer keeps up without getting
        // in its way.
        failures += await BesideTheIdeAsync(designer, form, project);

        int errors = CountErrors(designer);

        if (errors > 0)
        {
            Fail(ref failures, $"{errors} error line(s) in the console — see above");
        }

        return failures;
    }

    /// <summary>
    /// Writes a file the way JetBrains Rider's safe write does: a temporary file, the original renamed
    /// out of the way, the temporary renamed over it, the original deleted.
    /// </summary>
    private static async Task SaveLikeAnIdeAsync(string file, string text)
    {
        string temporary = file + "___jb_tmp___";
        string old = file + "___jb_old___";

        await File.WriteAllTextAsync(temporary, text);

        File.Move(file, old, overwrite: true);
        File.Move(temporary, file, overwrite: true);
        File.Delete(old);
    }

    /// <summary>What the file on disk says now.</summary>
    private static string Disk(string file) => File.ReadAllText(file);

    /// <summary>The form's text blocks as the canvas shows them, in the order the document writes them.</summary>
    private static TextBlock[] TextBlocks(FormViewModel form) =>
        form.Document?.Root is not { } root
            ? []
            : [.. root.DescendantElements()
                .Where(static element => element.Name.LocalName == "TextBlock")
                .Select(element => DesignerViewModel.ControlFor(form, element))
                .OfType<TextBlock>()];

    /// <summary>Selects a text block on the canvas, the way a click does.</summary>
    private static void SelectTextBlock(DesignerViewModel designer, FormViewModel form, int index)
    {
        if (TextBlocks(form).ElementAtOrDefault(index) is { } block)
        {
            designer.SelectFromCanvas(form, block);
        }
    }

    /// <summary>The inspector's row for the selection's Text.</summary>
    private static PropertyRow? TextRow(DesignerViewModel designer) =>
        designer.Properties.FirstOrDefault(static row => row.Name == "Text");

    /// <summary>Sets the window's title through the inspector's row, as a person types it.</summary>
    private static async Task<bool> EditTitleAsync(DesignerViewModel designer, FormViewModel form, string title)
    {
        if (form.Root is not { } root)
        {
            return false;
        }

        designer.SelectFromCanvas(form, root);

        if (designer.Properties.FirstOrDefault(static row => row.Name == "Title") is not { } row)
        {
            return false;
        }

        row.Value = title;

        return await Until(() => Text(form).Contains($"Title=\"{title}\"", StringComparison.Ordinal), 30);
    }
}
