using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.Markup.Xaml;
using Avalonia;
using Avalonia.Threading;

namespace UiDesigner.Demo.ViewModels;

/// <summary>
/// Starts the designer again when only a new process can show the project's types — by itself, when
/// the window is in front and nobody is in the middle of anything, and with everything open carried
/// across.
/// </summary>
/// <remarks>
/// <para>
/// The design host says when a swap could not be proven (<c>GenerationStillHeld</c>) or a package the
/// types loaded moved (<c>PackagesChanged</c>), and keeps no successor beside what stayed. Until the
/// restart the canvas holds the frame it froze. The restart waits for the person: a window in the
/// background is somebody working in the other editor, and a designer that vanished and came back
/// under their hands would be the designer deciding something it has no business deciding.
/// </para>
/// <para>
/// The session crosses as text, the way the studio's own restart carries it
/// (<c>StudioSession</c>, <c>StudioRestarter</c> in ArxisStudio): every open form's text, what its
/// file held, its place, its selection, the active tab and the zoom, written to a file in the data
/// folder. The new copy reads it, opens the forms with it, asks each file whether it moved on in the
/// meantime — a conflict if it did, never an overwrite — and confirms by deleting the file. The old
/// copy leaves only then; a copy that does not confirm, or does not start, leaves the old one where
/// it was, with its forms back, detached, so what was typed can be saved. The undo history does not
/// cross: it stays with the process that had it.
/// </para>
/// <para>
/// Three restarts in a row that each found the types held are a project whose code holds its own
/// types — a control subscribing to something the process keeps — and restarting on every save would
/// never end. After the third the designer stops by itself and offers the button.
/// </para>
/// </remarks>
public sealed partial class DesignerViewModel
{
    /// <summary>How many restarts in a row found the types held, carried from copy to copy.</summary>
    private int _restartsInARow;

    /// <summary>When the person last touched the designer, for "nobody is in the middle of anything".</summary>
    private DateTime _lastInput = DateTime.MinValue;

    private DispatcherTimer? _restartTimer;

    /// <summary>Asked of the view, because whether the studio's window is in front is its to know.</summary>
    public Func<bool>? IsStudioActive { get; set; }

    /// <summary>
    /// Whether the designer restarts by itself when it has to — off for a self-check that drives one
    /// process through a story and would lose the story to a new one.
    /// </summary>
    public bool RestartsByItself { get; set; } = true;

    /// <summary>Whether only a new process can show the project's types as they are now.</summary>
    public bool NeedsRestart
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                RestartCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Why, in the host's words.</summary>
    public string RestartReason
    {
        get;
        private set => Set(ref field, value);
    } = string.Empty;

    /// <summary>Called by the view on every pointer and key, to know when the designer is idle.</summary>
    public void NoteInput() => _lastInput = DateTime.UtcNow;

    /// <summary>Called by the view when the studio's window comes to the front.</summary>
    public void OnStudioActivated() => TryRestartByItself();

    private void OnRestartRequired(object? sender, ProjectDesignRestartEventArgs e)
    {
        RestartReason = e.Message;
        NeedsRestart = true;

        Log(e.Reason == ProjectDesignRestartReason.GenerationStillHeld
            ? "The old types would not leave this process — the designer restarts when you are back and idle"
            : "A package the types loaded changed — the designer restarts when you are back and idle");

        if (_restartsInARow >= 3)
        {
            Log("  three restarts in a row found the types held — press Restart when ready");

            return;
        }

        _restartTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => TryRestartByItself());
        _restartTimer.Start();
    }

    /// <summary>Restarts now if the window is in front, nothing is held off and nobody touched anything for a moment.</summary>
    private void TryRestartByItself()
    {
        if (!NeedsRestart
            || !RestartsByItself
            || _restartsInARow >= 3
            || IsBusy
            || _host is not { Gate.IsOpen: true }
            || IsStudioActive?.Invoke() != true
            || DateTime.UtcNow - _lastInput < TimeSpan.FromSeconds(1))
        {
            return;
        }

        _restartTimer?.Stop();

        RunDetached(() => RestartAsync(byItself: true));
    }

    /// <summary>
    /// Starts the designer again on this project with everything open, and leaves once the new copy
    /// has taken it.
    /// </summary>
    private async Task RestartAsync(bool byItself)
    {
        if (System.Environment.ProcessPath is not { Length: > 0 } executable)
        {
            Log("! this build cannot restart itself — close the designer and start it again");

            return;
        }

        string handoff = Path.Combine(DataFolder, $"restart-{System.Environment.ProcessId}.json");

        Directory.CreateDirectory(DataFolder);

        await File.WriteAllTextAsync(handoff, JsonSerializer.Serialize(Capture(byItself), HandoffJson), _shutdown.Token);

        var start = new ProcessStartInfo(executable) { UseShellExecute = false };

        start.ArgumentList.Add("--resume");
        start.ArgumentList.Add(handoff);
        start.ArgumentList.Add("--theme");
        start.ArgumentList.Add(Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light ? "light" : "dark");

        if (Program.AutomationDirectory is { Length: > 0 } automation)
        {
            start.ArgumentList.Add("--automation");
            start.ArgumentList.Add(automation);
        }

        Process? successor;

        try
        {
            successor = Process.Start(start);
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException)
        {
            File.Delete(handoff);

            Log($"! the designer could not start itself again: {error.Message}");
            ReturnFormsAfterAFailedRestart();

            return;
        }

        Log($"Restarting — the new copy (process {successor?.Id}) takes the session…");

        // The new copy says it took the session by deleting the file; until then nothing here moves.
        var waited = Stopwatch.StartNew();

        while (File.Exists(handoff) && waited.Elapsed < TimeSpan.FromSeconds(60) && successor is { HasExited: false })
        {
            await Task.Delay(100, _shutdown.Token);
        }

        if (!File.Exists(handoff))
        {
            Log("  the new copy took the session — leaving");

            if (Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }

            return;
        }

        // Not taken: the copy failed or is stuck. It is this process's own child, stopped by its number.
        File.Delete(handoff);

        if (successor is { HasExited: false })
        {
            successor.Kill(entireProcessTree: true);
        }

        Log("! the new copy did not take the session — the forms are back here, detached; save them and restart by hand");
        ReturnFormsAfterAFailedRestart();
    }

    /// <summary>What crosses a restart: the project, the forms with their text, and where the designer was.</summary>
    private Handoff Capture(bool byItself)
    {
        IEnumerable<HandoffForm> forms = _letGo is { } letGo
            ? letGo.Forms.Select(static memory => HandoffForm.Of(memory.File, memory.Live, memory.Location, memory.Width, memory.Height, memory.SelectedPath))
            : Forms.Select(static form => HandoffForm.Of(form.File, form.Live, form.Location, form.Width, form.Height, form.SelectedPath));

        CanonicalPath active = _letGo?.Active ?? ActiveForm?.File ?? default;

        return new Handoff(
            EntryPoint.Value,
            Configuration,
            [.. forms],
            active.IsEmpty ? null : active.Value,
            _letGo?.Zoom ?? Zoom,
            byItself ? _restartsInARow + 1 : 0);
    }

    /// <summary>
    /// Takes the session the previous copy handed over: the project, the forms with their text, the tab
    /// in front and the zoom — and confirms by deleting the file.
    /// </summary>
    /// <param name="file">The handoff file.</param>
    public void ResumeAtStartup(string file) => Run(async () =>
    {
        Handoff handoff;

        try
        {
            handoff = JsonSerializer.Deserialize<Handoff>(await File.ReadAllTextAsync(file), HandoffJson)
                ?? throw new InvalidDataException("The handoff is empty.");
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            Log($"! the session handed over could not be read: {error.Message}");

            return;
        }

        _restartsInARow = handoff.RestartsInARow;
        EntryPoint = CanonicalPath.Create(handoff.EntryPoint);

        if (handoff.Configuration is { Length: > 0 } configuration)
        {
            Configuration = configuration;
        }

        await LoadAsync();

        if (_host is not { } host)
        {
            return;
        }

        foreach (HandoffForm form in handoff.Forms)
        {
            await ReopenAsync(host, form);
        }

        ActiveForm = Forms.FirstOrDefault(form => form.File.Value.Equals(handoff.Active, StringComparison.OrdinalIgnoreCase))
            ?? Forms.FirstOrDefault();
        Zoom = handoff.Zoom;

        MarkOpenFiles();

        // Taken: the previous copy leaves once this file is gone.
        File.Delete(file);

        Log($"Resumed the session — {Describe(Forms.Count, "form")}, restart {handoff.RestartsInARow} in a row");
    });

    /// <summary>Opens a form with the text the previous copy had, and asks its file whether it moved on.</summary>
    private async Task ReopenAsync(ProjectDesignHost host, HandoffForm handed)
    {
        CanonicalPath file = CanonicalPath.Create(handed.File);

        if (!File.Exists(file.Value))
        {
            Log($"! {file.FileName} is gone — its text from the previous copy is not reopened");

            return;
        }

        var form = new FormViewModel(file, new Point(handed.X, handed.Y))
        {
            Width = handed.Width,
            Height = handed.Height,
        };

        if (XamlElementPath.TryParse(handed.Selected, out XamlElementPath? selected))
        {
            form.SelectedPath = selected;
        }

        ApplyApplicationVariant(form);
        Forms.Add(form);

        XamlLiveDocument live = await host.OpenDocumentAsync(
            file,
            new ProjectDesignDocumentOptions { RootAccess = form.RootAccess, Text = handed.Text, SavedText = handed.SavedText },
            _shutdown.Token);

        form.Take(live);

        // The other editor went on working while the designer restarted: a file that moved on under
        // restored edits is a question for the person, raised as one (OnExternalConflict).
        await host.ReloadAsync(live, _shutdown.Token);
    }

    /// <summary>Where the designer keeps what it hands to its next copy.</summary>
    private static string DataFolder => Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "ArxisStudio", "UiDesigner.Demo");

    private static readonly JsonSerializerOptions HandoffJson = new() { WriteIndented = true };

    /// <summary>The session as it crosses a restart.</summary>
    private sealed record Handoff(
        string EntryPoint,
        string? Configuration,
        IReadOnlyList<HandoffForm> Forms,
        string? Active,
        double Zoom,
        int RestartsInARow);

    /// <summary>One form as it crosses a restart: its text, what its file held, and where it was.</summary>
    private sealed record HandoffForm(
        string File,
        string Text,
        string SavedText,
        double X,
        double Y,
        double Width,
        double Height,
        string? Selected)
    {
        public static HandoffForm Of(
            CanonicalPath file, XamlLiveDocument? live, Point location, double width, double height, XamlElementPath? selected) =>
            new(
                file.Value,
                live?.Document.SourceText.ToString() ?? string.Empty,
                live?.SavedText.ToString() ?? string.Empty,
                location.X,
                location.Y,
                width,
                height,
                selected?.ToString());
    }
}
