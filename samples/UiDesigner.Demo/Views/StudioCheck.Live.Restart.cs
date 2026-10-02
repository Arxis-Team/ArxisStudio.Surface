using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.ProjectSystem.Markup.Xaml;
using ArxisStudio.Surface.UiDesigner;
using Avalonia.Controls;
using UiDesigner.Demo.ViewModels;

namespace UiDesigner.Demo.Views;

/// <summary>
/// L9 of <c>--live</c>: the old types will not leave, and the designer restarts by itself — once it is
/// in front and nobody is in the middle of anything — with the session carried across.
/// </summary>
/// <remarks>
/// <para>
/// What holds them is the commonest thing a project's own code does by accident: a control that
/// subscribes to something the process keeps. The host proves the generation is still there, keeps no
/// successor beside it and asks for a restart (ProjectSystem ADR 0028); the canvas holds its frame
/// meanwhile. Behind another window the designer waits; in front and idle it starts its next copy,
/// which opens the forms with the text the old copy had and says so by deleting the handoff.
/// </para>
/// <para>
/// "In front" is answered by the check through the same question the view answers
/// (<see cref="DesignerViewModel.IsStudioActive"/>): a script cannot bring a window to the front on
/// Windows, which lets only the foreground process do that. The new copy is asked through its automation
/// channel what it took, and stopped — it is this process's own child — before this process leaves.
/// </para>
/// </remarks>
internal static partial class StudioCheck
{
    private static async Task<int> RestartWhenHeldAsync(MainWindow window, DesignerViewModel designer, string solution, string folder)
    {
        var failures = 0;
        string views = Path.Combine(Path.GetDirectoryName(solution)!, "SuiteApp", "Views");
        string held = Path.Combine(views, "HeldControl.axaml");
        string main = Path.Combine(views, "MainWindow.axaml");
        int swaps = designer.Swaps;

        // The IDE writes a control that subscribes to the process. Built and swapped in, nothing of it
        // exists yet, and nothing holds anything.
        await File.WriteAllTextAsync(held, HeldMarkup);
        await File.WriteAllTextAsync(held + ".cs", HeldCode);

        if (!await Until(() => designer.Swaps > swaps && designer.TypesState == ProjectDesignState.Live, 300))
        {
            return Fail(ref failures, $"L9: the IDE's new control was never built and swapped in ({designer.TypesState})");
        }

        // The IDE places it on the window: from here a control of the generation is subscribed to the process.
        await SaveLikeAnIdeAsync(main, Disk(main)
            .Replace("<Window ", "<Window xmlns:v=\"using:SuiteApp.Views\" ", StringComparison.Ordinal)
            .Replace("<lib:LibCard />", "<lib:LibCard />\n        <v:HeldControl />", StringComparison.Ordinal));

        if (!await Until(() => FormShows(designer, "MainWindow.axaml", "HeldControl", "Held by the process"), 60))
        {
            return Fail(ref failures, "L9: the window does not draw the control the IDE placed");
        }

        if (await SetTheBenchAsync(designer, "MainWindow.axaml", "LibCard.axaml", "Carried across", zoom: 1.5) is not { } before)
        {
            return Fail(ref failures, "L9: the designer could not be given a person's state to carry");
        }

        UiDesignerView surface = window.GetControl<UiDesignerView>("Surface");
        string channel = Path.Combine(folder, "automation");
        bool inFront = false;
        var taken = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        Program.AutomationDirectory = channel;
        designer.IsStudioActive = () => inFront;
        designer.RestartsByItself = true;
        designer.BeforeLeaving = async successor =>
        {
            taken.TrySetResult(await CheckTheSuccessorAsync(successor, channel, before));

            // This process goes when this returns, and the verdict has to be out by then.
            await VerdictSaid.Task;
        };

        using var complaints = new Complaints(designer);

        // The IDE saves the control's code: the build that follows cannot be swapped in.
        await SaveLikeAnIdeAsync(held + ".cs", Disk(held + ".cs") + "// saved while the control is subscribed\n");

        if (!await Until(() => designer.NeedsRestart, 300))
        {
            return Fail(ref failures, $"L9: the types a subscription holds never asked for a restart ({designer.TypesState})");
        }

        if (designer.LastSwap is { Reclaimed: false } report)
        {
            Timing("L9", report);
        }

        if (designer.Host is not { State: ProjectDesignState.RestartRequired })
        {
            Fail(ref failures, $"L9: the host is {designer.Host?.State} rather than waiting for a restart");
        }

        if (!surface.IsFrozen || designer.Forms.Count > 0)
        {
            Fail(ref failures, $"L9: while the restart waits, the canvas holds no frame or a form was built beside the old types (frozen {surface.IsFrozen}, {designer.Forms.Count} form(s))");
        }

        // Behind another window, idle: it waits.
        await Until(() => Restarting(designer), 3);

        if (Restarting(designer))
        {
            return Fail(ref failures, "L9: the designer restarted while it was behind another window");
        }

        // In front and idle: it restarts by itself, and the new copy has what this one had.
        var cameForward = Stopwatch.StartNew();

        inFront = true;

        if (await Task.WhenAny(taken.Task, Task.Delay(TimeSpan.FromSeconds(150))) != taken.Task)
        {
            return Fail(ref failures, "L9: the designer did not restart by itself once in front, or the new copy did not take the session");
        }

        Say($"timing L9: the new copy took the session {cameForward.ElapsedMilliseconds} ms after the window came to the front");

        failures += await taken.Task;

        // The one complaint this step expects: the old types would not leave.
        foreach (string line in complaints.Lines.Where(static line => !line.Contains("would not leave this process", StringComparison.Ordinal)))
        {
            Fail(ref failures, "L9: the console complained — " + line);
        }

        if (failures == 0)
        {
            Say("L9: types a subscription holds are a restart the designer makes by itself, in front and idle, with tabs, unsaved text, selection and zoom carried across");
        }

        return failures;
    }

    /// <summary>Whether the designer has started its next copy.</summary>
    private static bool Restarting(DesignerViewModel designer) =>
        designer.Output.Any(static line => line.StartsWith("Restarting", StringComparison.Ordinal));

    /// <summary>
    /// Asks the copy a restart started what it took — until it has put the selection back on its canvas,
    /// which it does once the form in front is live — and stops it.
    /// </summary>
    private static async Task<int> CheckTheSuccessorAsync(Process successor, string channel, Workbench before)
    {
        var failures = 0;
        string? selected = before.Selected?.ToString();

        try
        {
            JsonElement? state = null;
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);

            while (DateTime.UtcNow < deadline)
            {
                if (await AskAsync(channel, "{\"name\":\"state\"}") is { } answer && answer.TryGetProperty("designer", out JsonElement held)
                    && held.ValueKind == JsonValueKind.Object)
                {
                    state = held;

                    if (held.GetProperty("selected").GetString() == selected && held.GetProperty("types").GetString() == "Live")
                    {
                        break;
                    }
                }

                await Task.Delay(500);
            }

            if (state is not { } taken)
            {
                return Fail(ref failures, $"L9: the new copy (process {successor.Id}) never said what it holds");
            }

            JsonElement[] forms = [.. taken.GetProperty("forms").EnumerateArray()];
            string[] tabs = [.. forms.Select(static form => form.GetProperty("file").GetString() ?? string.Empty)];

            if (!tabs.SequenceEqual(before.Tabs, StringComparer.OrdinalIgnoreCase))
            {
                Fail(ref failures, $"L9: the new copy has other tabs ({string.Join(", ", tabs.Select(static tab => Path.GetFileName(tab)))})");
            }

            if (!string.Equals(taken.GetProperty("active").GetString(), before.Active, StringComparison.OrdinalIgnoreCase))
            {
                Fail(ref failures, $"L9: another form is in front in the new copy ({taken.GetProperty("active").GetString()})");
            }

            if (forms.FirstOrDefault(form => string.Equals(form.GetProperty("file").GetString(), before.Active, StringComparison.OrdinalIgnoreCase))
                is not { ValueKind: JsonValueKind.Object } front)
            {
                return Fail(ref failures, "L9: the form that was in front is not open in the new copy");
            }

            if (front.GetProperty("text").GetString() != before.Text || !front.GetProperty("dirty").GetBoolean())
            {
                Fail(ref failures, "L9: the text that was not saved did not cross the restart");
            }

            if (front.GetProperty("selected").GetString() != selected)
            {
                Fail(ref failures, $"L9: the form's selection did not cross ({front.GetProperty("selected").GetString() ?? "none"})");
            }

            if (taken.GetProperty("selected").GetString() != selected)
            {
                Fail(ref failures, $"L9: the new copy did not put the selection back on its canvas ({taken.GetProperty("selected").GetString() ?? "none"})");
            }

            if (Math.Abs(taken.GetProperty("zoom").GetDouble() - before.Zoom) > 0.0001)
            {
                Fail(ref failures, $"L9: the zoom did not cross ({taken.GetProperty("zoom").GetDouble():F2})");
            }

            if (taken.GetProperty("types").GetString() != "Live")
            {
                Fail(ref failures, $"L9: the new copy's types are {taken.GetProperty("types").GetString()} rather than live");
            }

            if (failures == 0)
            {
                Say($"L9: the new copy (process {successor.Id}) has the tabs, the unsaved text, the selection and the zoom, on live types — {taken.GetProperty("generation").GetString()}");
            }
        }
        finally
        {
            // The new copy is this process's own child: stopped by its number, and waited for.
            if (!successor.HasExited)
            {
                successor.Kill(entireProcessTree: true);

                using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                try
                {
                    await successor.WaitForExitAsync(patience.Token);
                }
                catch (OperationCanceledException)
                {
                    Fail(ref failures, $"L9: the new copy (process {successor.Id}) did not stop");
                }
            }
        }

        return failures;
    }

    /// <summary>Sends a command through an automation channel and reads the answer, or nothing within the time.</summary>
    private static async Task<JsonElement?> AskAsync(string channel, string command, int seconds = 10)
    {
        string answer = Path.Combine(channel, "response.json");

        Directory.CreateDirectory(channel);
        File.Delete(answer);

        await File.WriteAllTextAsync(Path.Combine(channel, "command.json"), command);

        DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);

        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(answer))
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(answer));

                    return document.RootElement.Clone();
                }
                catch (Exception error) when (error is IOException or JsonException)
                {
                    // Still being written.
                }
            }

            await Task.Delay(100);
        }

        return null;
    }

    /// <summary>A control that subscribes to the process, as a project's own code does by accident.</summary>
    private const string HeldMarkup =
        """
        <UserControl xmlns="https://github.com/avaloniaui"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     x:Class="SuiteApp.Views.HeldControl">
          <TextBlock Text="Held by the process" />
        </UserControl>

        """;

    private const string HeldCode =
        """
        using System;
        using Avalonia.Controls;
        using Avalonia.Markup.Xaml;

        namespace SuiteApp.Views;

        public partial class HeldControl : UserControl
        {
            public HeldControl()
            {
                AvaloniaXamlLoader.Load(this);

                // The process keeps its subscribers for as long as it runs, and with this control its type
                // and every type built beside it.
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            }

            private void OnProcessExit(object? sender, EventArgs e) => IsVisible = false;
        }

        """;
}
