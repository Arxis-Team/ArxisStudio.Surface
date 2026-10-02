using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.Markup.Xaml;
using Avalonia.Controls;

namespace UiDesigner.Demo.ViewModels;

/// <summary>
/// What the designer itself holds of the project's types, let go of when ProjectSystem's design host
/// replaces them and taken up again when it has.
/// </summary>
/// <remarks>
/// <para>
/// The order of a swap is the host's (ProjectSystem ADR 0028): participants first, then the documents
/// detached and the windows their sessions built closed, then the host's own population and
/// environments, then <see cref="ClearInputState"/> last, then the proof that the old types are gone,
/// and only then the successor. What only the designer can do is let go of its own parts, and that is
/// all that is left here.
/// </para>
/// <para>
/// The forms are let go of rather than kept. Measurement showed that a form left open — even taken off
/// the canvas — keeps the generation it was built under, so each is remembered — its place, its
/// selection, a question about its file it was waiting on, and the document, which the host keeps
/// with its history and unsaved edits — closed, and built again on the far side. The canvas holds its
/// last frame meanwhile, so the person sees the forms rather than an empty surface.
/// </para>
/// <para>
/// When the old types will not go, nothing is taken up: the frame stays and the designer restarts
/// (<see cref="OnRestartRequired"/>). The memory is kept for the one case the restart does not happen —
/// then the forms come back detached, with their text, so nothing typed is lost.
/// </para>
/// </remarks>
public sealed partial class DesignerViewModel
{
    /// <summary>Freezes the canvas on its last frame, until the result is disposed; set by the view.</summary>
    public Func<IDisposable?>? FreezeCanvas { get; set; }

    /// <summary>The forms let go of for a swap, and where the designer was.</summary>
    private sealed record SwapMemory(
        IReadOnlyList<FormViewModel.FormMemory> Forms,
        CanonicalPath Active,
        double Zoom,
        IDisposable? Frozen);

    /// <summary>What the last swap let go of, until it is taken up.</summary>
    private SwapMemory? _letGo;

    /// <summary>
    /// Lets go of every form, the selection and the panels about it, with the canvas frozen on what it
    /// showed.
    /// </summary>
    private void LetGoOfForms()
    {
        IDisposable? frozen = FreezeCanvas?.Invoke();

        CanonicalPath active = ActiveForm?.File ?? default;
        double zoom = Zoom;

        CanvasSelectionCleared?.Invoke(this, EventArgs.Empty);

        // Nothing may still point at a form that is going. The active one is the easiest to forget and
        // the most expensive to leave: a form the panels are still holding holds the surface it was
        // shown on, and a surface holds the window it borrowed from. Cleared before the forms are
        // remembered, so each keeps the selection it had.
        ActiveForm = null;
        _selectedControl = null;

        var memories = new List<FormViewModel.FormMemory>();

        foreach (FormViewModel form in Forms.ToArray())
        {
            // Unsubscribed here: clearing the tabs says Reset, which names no form to unsubscribe.
            form.DocumentChanged -= OnFormDocumentChanged;
            memories.Add(form.Remember());
        }

        Forms.Clear();
        Shown.Clear();
        SelectedForms.Clear();
        Hierarchy.Clear();
        Properties.Clear();
        PropertyGroups.Clear();

        _letGo = new SwapMemory(memories, active, zoom, frozen);
    }

    /// <summary>Builds every form again over its document, in the order and state it was in, and lets the canvas go.</summary>
    private void TakeUpForms()
    {
        if (_letGo is not { } memory)
        {
            return;
        }

        _letGo = null;

        foreach (FormViewModel.FormMemory remembered in memory.Forms)
        {
            var form = new FormViewModel(remembered.File, remembered.Location, remembered.RootAccess);

            form.Recall(remembered);
            ApplyApplicationVariant(form);

            // Followed from here as every form is: adding it to the tabs subscribes the designer.
            Forms.Add(form);
        }

        // Each form puts its own selection back, by path, as it becomes the active one.
        ActiveForm = Forms.FirstOrDefault(form => form.File == memory.Active) ?? Forms.FirstOrDefault();
        Zoom = memory.Zoom;

        if (ActiveForm is { } shown)
        {
            SizeToContent(shown);
        }

        MarkOpenFiles();
        RebuildHierarchy();
        RefreshAllCommands();

        memory.Frozen?.Dispose();
    }

    /// <summary>
    /// Puts the forms back, detached, when the designer could not restart after the types would not go —
    /// so that what was typed in them can still be saved.
    /// </summary>
    private void ReturnFormsAfterAFailedRestart()
    {
        if (_letGo is not null)
        {
            TakeUpForms();
        }
    }

    /// <summary>
    /// Lets go of what the input system, and the libraries listening to it, are pointing at.
    /// </summary>
    /// <remarks>
    /// Two things a click leaves behind, neither of them visible to a view model. The focus manager
    /// belongs to the studio's window and goes on holding whatever was clicked until something else
    /// takes focus. And <see cref="ForgetTheEditorsLastInputElement"/> is the one that actually
    /// mattered — measured, and named by a heap dump rather than by reading. The design host calls this
    /// last, after everything else let go: closing forms moves focus and routes commands, which is
    /// what puts these back on a live tree a moment after they were cleared.
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
    /// Measured, not guessed: a heap dump of a swap that fell back named exactly one root, and it was
    /// <c>AvaloniaEdit.RoutedCommand._inputElement</c> holding this studio's editor item — whose
    /// composition subtree contains the drawn form, and through it every type in the generation. A
    /// person who clicked a form before editing its code got a restart; a script that never clicked
    /// got a swap, which is why this took a dump to find rather than a reading.
    /// </para>
    /// <para>
    /// Reflective and shape-checked, in the posture ProjectSystem's ADR 0020 sets for reaching into
    /// somebody else's statics: a field that is no longer there, or no longer holds what it held,
    /// leaves this doing nothing and the swap falling back to the restart it did before.
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
            // A static that will not be written is a swap that falls back, which is a worse answer
            // than this and not a worse one than crashing here.
        }
    }

    /// <summary>The designer's forms, as the design host sees them: a part that lets go and takes up.</summary>
    private sealed class FormsParticipant(DesignerViewModel designer) : IProjectDesignParticipant
    {
        public ValueTask ReleaseAsync(CancellationToken cancellationToken)
        {
            designer.LetGoOfForms();

            return ValueTask.CompletedTask;
        }

        public ValueTask RestoreAsync(CancellationToken cancellationToken)
        {
            designer.TakeUpForms();

            return ValueTask.CompletedTask;
        }
    }
}
