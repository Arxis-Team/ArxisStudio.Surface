using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using ArxisStudio.Markup;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.Markup.Xaml;
using Avalonia.Controls;
using Avalonia.Threading;

namespace UiDesigner.Demo.ViewModels;

/// <summary>
/// Replaces the project's types without replacing the studio.
/// </summary>
/// <remarks>
/// <para>
/// A code change used to end in a restart, because a superseded generation was never collected
/// and a second one would put two copies of every type in the process — the failure ADR 0021
/// measured. Avalonia 12.1.1 made the first half fixable and measurement found the rest, so the
/// adapter can now hand a generation back to the process and <em>prove</em> it went. That proof
/// is what this is built on: the successor is created only after the predecessor is provably
/// gone, so the "first match wins" resolvers that made the old failure have nothing to choose
/// between.
/// </para>
/// <para>
/// What a swap keeps is the point of doing it: the tabs, their order, the active one, the
/// documents including unsaved edits, the undo history, the place and size on the canvas, and the
/// selection. The documents are the forms' live documents, detached for the swap — a detached one
/// holds nothing of the generation it was shown under — and attached again to the successor. What
/// a swap cannot keep is the form objects themselves — a form left open holds the generation it
/// was built under, measured, so each one is remembered, closed, and built again on the far side.
/// </para>
/// <para>
/// When the proof fails — a user control that started a timer, a subscription nothing released —
/// nothing is created, and the studio restarts exactly as it did before. That path is not a
/// regression; it is yesterday's behaviour, kept for the day the swap cannot be honest.
/// </para>
/// </remarks>
public sealed partial class DesignerViewModel
{
    /// <summary>The swap in flight, so an open cannot land between two generations.</summary>
    private Task? _typeSwap;

    /// <summary>
    /// Swaps the project's types in place, or restarts the studio when they will not leave.
    /// </summary>
    private async Task SwapGenerationAsync()
    {
        if (_assemblies is null || _workspace.CurrentSnapshot is null)
        {
            return;
        }

        Log("The project's code changed — swapping the types in place…");

        // Where everything was, said in a way that survives losing the objects that held it: each
        // form's document is detached — its session gone, its root off the card, a window it built
        // closed — and handed over with the form's place and its selection.
        var memories = new List<FormViewModel.FormMemory>();

        foreach (FormViewModel form in Forms.ToArray())
        {
            memories.Add(await form.RememberAsync());
        }

        CanonicalPath active = ActiveForm?.File ?? default;
        double zoom = Zoom;

        await LetGoOfEverythingAsync();

        // Every generation in the process, not only the current one: the runtime XAML compiler
        // snapshots the assemblies it can see, so one survivor is enough to reintroduce the
        // failure this exists to prevent.
        bool reclaimed = await ReclaimEveryGenerationAsync();

        if (!reclaimed)
        {
            // Under --probe the studio stays exactly here instead of restarting, because the one
            // thing that can name the root is a heap dump of the process while the root still
            // holds. A restart is the honest answer for a person and the wrong one for a
            // measurement: it takes the evidence with it.
            if (Views.StudioCheck.NameRootsOnFallback)
            {
                Log("  ! the old types would not leave — holding here so a dump can say why (--probe)");

                return;
            }

            Log("  ! the old types would not leave this process — restarting instead");

            Restart();

            return;
        }

        Log("  the old types are gone — building the new ones");

        await RestoreAsync(memories, active, zoom);
    }

    /// <summary>
    /// Releases everything the studio built from the generation, in the order that works.
    /// </summary>
    /// <remarks>
    /// Every step here was measured rather than guessed. A window-rooted form leaves a real
    /// <c>Window</c> in the windowing platform's own list, and retiring the session does not take
    /// it out. A form that stays open — even with the canvas cleared — holds its generation
    /// through the container that shows it. And the population registry holds delegates on the
    /// generation's own types, so it goes before the types do.
    /// </remarks>
    private async Task LetGoOfEverythingAsync()
    {
        Selected = null;
        _selectedControl = null;

        CanvasSelectionCleared?.Invoke(this, EventArgs.Empty);

        // Nothing may still point at a form that is going. The active one is the easiest to
        // forget and the most expensive to leave: a form the panels are still holding holds the
        // surface it was shown on, and a surface holds the window it borrowed from — which is a
        // path to every type in the generation. Measured: with this line missing, the swap that a
        // harness proved possible on the same project fell back to a restart every time.
        ActiveForm = null;

        foreach (FormViewModel form in Forms.ToArray())
        {
            await CloseFormForSwapAsync(form);
        }

        Shown.Clear();
        SelectedForms.Clear();
        Hierarchy.Clear();
        Properties.Clear();
        PropertyGroups.Clear();

        _population?.Dispose();
        _population = null;
        _liveRegistration = null;

        // One turn of the dispatcher, so the layout and render work queued for the tree that has
        // just gone lets go of it before anything asks whether it did.
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);

        // Last, and that ordering was measured too: closing a form moves focus and routes commands
        // through the editor, which is what puts these back on a live tree a moment after they
        // were cleared. Clearing them first and closing afterwards left the swap failing exactly
        // as it had.
        ClearInputState();

        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Lets go of what the input system, and the libraries listening to it, are pointing at.
    /// </summary>
    /// <remarks>
    /// Two things a click leaves behind, neither of them visible to a view model. The focus
    /// manager belongs to the studio's window and goes on holding whatever was clicked until
    /// something else takes focus. And <see cref="ForgetTheEditorsLastInputElement"/> is the one
    /// that actually mattered — measured, and named by a heap dump rather than by reading.
    /// </remarks>
    private void ClearInputState()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        foreach (Window window in desktop.Windows)
        {
            if (window.FocusManager is not { } focus)
            {
                continue;
            }

            // Said out loud when it was one of the project's own, because that is the difference
            // between a swap and a restart and nothing else in the log would mention it.
            if (focus.GetFocusedElement() is { } focused
                && AssemblyLoadContext.GetLoadContext(focused.GetType().Assembly) is { IsCollectible: true })
            {
                Log($"  the focus was on {focused.GetType().Name}, which the generation owns");
            }

            focus.Focus(null);
        }

        ForgetTheEditorsLastInputElement();
    }

    /// <summary>
    /// Clears the static AvaloniaEdit leaves pointing at whatever last routed a command.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured, not guessed: a heap dump of a swap that fell back named exactly one root, and it
    /// was <c>AvaloniaEdit.RoutedCommand._inputElement</c> holding this studio's editor item —
    /// whose composition subtree contains the drawn form, and through it every type in the
    /// generation. A person who clicked a form before editing its code got a restart; a script
    /// that never clicked got a swap, which is why this took a dump to find rather than a reading.
    /// </para>
    /// <para>
    /// Reflective and shape-checked, in the posture ADR 0020 sets for reaching into somebody
    /// else's statics: a field that is no longer there, or no longer holds what it held, leaves
    /// this doing nothing and the swap falling back to the restart it did before.
    /// </para>
    /// </remarks>
    private static void ForgetTheEditorsLastInputElement()
    {
        try
        {
            if (Type.GetType("AvaloniaEdit.RoutedCommand, AvaloniaEdit", throwOnError: false) is not { } command
                || command.GetField("_inputElement", BindingFlags.Static | BindingFlags.NonPublic)
                    is not { } element
                || !element.FieldType.IsAssignableFrom(typeof(Avalonia.Input.IInputElement)))
            {
                return;
            }

            element.SetValue(null, null);
        }
        catch (Exception)
        {
            // A static that will not be written is a swap that falls back, which is a worse
            // answer than this and not a worse one than crashing here.
        }
    }

    /// <summary>Reclaims the current generation and every retired one, and says whether all went.</summary>
    /// <remarks>
    /// The fields are let go of <em>before</em> anything is asked to leave, and that ordering is
    /// the difference between a swap and a restart. The environment is the reason: its type
    /// resolver was handed the generation's own assemblies as a list to search, so a designer
    /// still holding the environment is a designer holding every assembly in the generation — and
    /// the reclaim, measured, answers "still held" for a generation nothing else wants.
    /// </remarks>
    private async Task<bool> ReclaimEveryGenerationAsync()
    {
        (ProjectAssemblyContext Context, ProjectXamlPopulation? Population)[] retired = [.. _retired];
        ProjectAssemblyContext? current = _assemblies;

        _retired.Clear();
        _assemblies = null;
        _environment = null;
        _environmentProject = default;

        var reclaimed = true;

        foreach ((ProjectAssemblyContext context, ProjectXamlPopulation? population) in retired)
        {
            population?.Dispose();

            bool gone = await context.TryReclaimAsync(_shutdown.Token);

            Log($"  retired generation “{context.Name}”: {(gone ? "gone" : "still held")}");

            reclaimed &= gone;
        }

        if (current is not null)
        {
            bool gone = await current.TryReclaimAsync(_shutdown.Token);

            Log($"  generation “{current.Name}”: {(gone ? "gone" : "still held")}");

            reclaimed &= gone;
        }

        return reclaimed;
    }

    /// <summary>
    /// Builds every form again under the successor, in the order and state it was in.
    /// </summary>
    private async Task RestoreAsync(
        IReadOnlyList<FormViewModel.FormMemory> memories,
        CanonicalPath active,
        double zoom)
    {
        if (_workspace.CurrentSnapshot is not { } snapshot)
        {
            foreach (FormViewModel.FormMemory memory in memories)
            {
                if (memory.Live is { } orphan)
                {
                    await orphan.DisposeAsync();
                }
            }

            return;
        }

        var rebuilt = 0;

        foreach (FormViewModel.FormMemory memory in memories)
        {
            if (memory.Live is not { } live
                || !snapshot.TryGetProjectForFile(memory.File, out ProjectSnapshot? owner))
            {
                if (memory.Live is { } orphan)
                {
                    await orphan.DisposeAsync();
                }

                continue;
            }

            var form = new FormViewModel(memory.File, memory.Location);

            form.Recall(memory);
            ApplyApplicationVariant(form);

            Forms.Add(form);

            XamlLoadEnvironment environment = EnvironmentFor(snapshot, owner.Identity);

            if (_liveRegistration is { } registering)
            {
                await registering;
            }

            await SetLiveDocumentAsync(live.Document);

            // The form was already open: its history is real and its unsaved edits are in the text
            // this session is built from. Attaching shows that text under the successor's types.
            XamlLiveEditResult attached = await live.AttachAsync(
                environment,
                new XamlLoadOptions { Mode = XamlLoadMode.Design, RootAccess = form.RootAccess },
                _shutdown.Token);

            form.Assemblies = _assemblies;

            if (live.Session is null)
            {
                string why = FirstError(attached.Diagnostics);

                form.Fail(why);

                Log($"  ! {form.Name} did not come back: {why}");

                continue;
            }

            SizeToContent(form);

            rebuilt++;
        }

        // Each form puts its own selection back, by path, as it becomes the active one.
        ActiveForm = Forms.FirstOrDefault(form => form.File == active) ?? Forms.FirstOrDefault();
        Zoom = zoom;

        MarkOpenFiles();
        RebuildHierarchy();

        ClearRestartAsk();

        Log($"  types swapped in place — {Describe(rebuilt, "form")} rebuilt");

        RefreshAllCommands();
    }

    /// <summary>
    /// Closes a form without the tidying a user's close performs.
    /// </summary>
    /// <remarks>
    /// The ordinary close picks the next tab, logs, and sweeps retired generations — all of which
    /// are wrong in the middle of a swap, where every form is going and coming back, and equally
    /// wrong when the project is closing and every form is going for good. Both use this.
    /// </remarks>
    private async Task CloseFormForSwapAsync(FormViewModel form)
    {
        Forms.Remove(form);

        // Awaited rather than waited on: a form's document finishes what it is doing on this
        // thread, and blocking the thread on it would be waiting for itself.
        await form.DisposeAsync();
    }
}
