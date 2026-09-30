using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.ProjectSystem;
using Avalonia.Threading;

namespace UiDesigner.Demo.ViewModels;

public sealed partial class DesignerViewModel
{
    private Process? _running;

    public RelayCommand RunCommand { get; private set; } = null!;

    public RelayCommand StopCommand { get; private set; } = null!;

    /// <summary>Holds the running application still, and lets it go again.</summary>
    public RelayCommand PauseCommand { get; private set; } = null!;

    public bool IsRunning
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                RefreshAllCommands();
            }
        }
    }

    /// <summary>Whether the running application is currently held.</summary>
    public bool IsPaused
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                Raise(nameof(PauseTip));
                RefreshAllCommands();
            }
        }
    }

    /// <summary>What the pause button would do if pressed now.</summary>
    public string PauseTip => IsPaused ? "Resume" : "Pause";

    private void InitialiseRun()
    {
        RunCommand = new RelayCommand(() => Run(RunProjectAsync), () => CanOperate() && !IsRunning);
        StopCommand = new RelayCommand(StopProject, () => IsRunning);
        PauseCommand = new RelayCommand(PauseProject, () => IsRunning);
    }

    /// <summary>
    /// Builds the project and starts what it produced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Which file to start comes from <see cref="ProjectSnapshot.Outputs"/> rather than from a
    /// listing of <c>bin</c>: the assembly artifact is what the project says it produces, and the
    /// runtime configuration is how it says it is startable at all. A project with neither is a
    /// library, and saying so is better than starting something arbitrary.
    /// </para>
    /// <para>
    /// A build that failed stops it. Running the previous build after a failed one is how somebody
    /// spends an afternoon debugging code they did not write.
    /// </para>
    /// </remarks>
    private async Task RunProjectAsync()
    {
        if (Project() is not { } project)
        {
            return;
        }

        Log($"Building {project.Name} before running…");

        if (await ExecuteAsync(ProjectOperationKind.Build) != ProjectOperationStatus.Succeeded)
        {
            Log("  the build failed — not starting anything");

            return;
        }

        if (project.Outputs.FirstOrDefault(o => o.Kind == OutputArtifactKind.Assembly) is not { } assembly)
        {
            Log($"  {project.Name} produces no assembly to start");

            return;
        }

        if (!project.Outputs.Any(o => o.Kind == OutputArtifactKind.RuntimeConfiguration))
        {
            Log($"  {project.Name} is a library — nothing to start");

            return;
        }

        Start(assembly.Path, project.Name);
    }

    private void Start(CanonicalPath assembly, string name)
    {
        if (!File.Exists(assembly.Value))
        {
            Log($"  {assembly.FileName} is not there");

            return;
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet", $"\"{assembly.Value}\"")
            {
                WorkingDirectory = assembly.Directory.Value,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };

        process.OutputDataReceived += (_, e) => Mirror(e.Data);
        process.ErrorDataReceived += (_, e) => Mirror(e.Data);

        process.Exited += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            Log($"  {name} exited with code {process.ExitCode}");

            IsRunning = false;
            IsPaused = false;
            _running = null;

            StopClock();

            process.Dispose();
        });

        Log($"Running {assembly.FileName}…");

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        _running = process;
        IsRunning = true;

        StartClock();
    }

    private void Mirror(string? line)
    {
        if (line is { Length: > 0 })
        {
            Log($"  │ {line}");
        }
    }

    /// <summary>
    /// Holds the running application, or lets it go.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This suspends the process, which is what a tool without a debugger can honestly offer: every
    /// thread stops where it is and continues from there. It is not a breakpoint — nothing is
    /// inspected and no line is shown — and a suspended window stops repainting, so the application
    /// looks frozen, because it is.
    /// </para>
    /// <para>
    /// The two platforms have different words for it and the same meaning. Windows has no supported
    /// managed call, so it goes through <c>ntdll</c>, which is what every process explorer on the
    /// system does. Elsewhere it is the signal pair that has meant this since v7 Unix.
    /// </para>
    /// </remarks>
    private void PauseProject()
    {
        if (_running is not { HasExited: false } process)
        {
            return;
        }

        bool resuming = IsPaused;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                int status = resuming ? NtResumeProcess(process.Handle) : NtSuspendProcess(process.Handle);

                if (status != 0)
                {
                    Log($"  the process refused to {(resuming ? "resume" : "pause")} (0x{status:X8})");

                    return;
                }
            }
            else if (!Signal(process.Id, resuming ? "-CONT" : "-STOP"))
            {
                return;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            Log($"  already gone ({exception.GetType().Name})");

            return;
        }

        IsPaused = !resuming;

        Log(resuming ? "Resumed." : "Paused.");

        if (resuming)
        {
            ResumeClock();
        }
        else
        {
            PauseClock();
        }
    }

    /// <summary>Sends a signal the way a shell would, because .NET has no call for these two.</summary>
    private bool Signal(int pid, string signal)
    {
        using var kill = Process.Start(new ProcessStartInfo("kill", $"{signal} {pid}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        if (kill is null)
        {
            Log("  kill is not available here");

            return false;
        }

        kill.WaitForExit();

        return kill.ExitCode == 0;
    }

    // DllImport rather than LibraryImport: the generated marshalling stub needs the whole project
    // compiled with AllowUnsafeBlocks, and one handle passed by value does not need marshalling at
    // all. Two entry points are not a reason to open a sample's compilation.
#pragma warning disable SYSLIB1054
    [System.Runtime.InteropServices.DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr process);

    [System.Runtime.InteropServices.DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr process);
#pragma warning restore SYSLIB1054

    private void StopProject()
    {
        if (_running is not { HasExited: false } process)
        {
            return;
        }

        Log("Stopping…");

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            Log($"  already gone ({exception.GetType().Name})");
        }
    }

    /// <summary>Restores or builds through the workspace, which routes it to the provider.</summary>
    /// <remarks>
    /// An operation is not a mutation: it changes what is on disk rather than what the workspace
    /// knows, so nothing is published and the version does not advance. A restore is the exception
    /// worth refreshing after, because it rewrites an evaluation input.
    /// </remarks>
    private Task<ProjectOperationStatus> ExecuteAsync(ProjectOperationKind kind) =>
        Project() is { } project
            ? ExecuteAsync(kind, project)
            : Task.FromResult(ProjectOperationStatus.Failed);

    private async Task<ProjectOperationStatus> ExecuteAsync(
        ProjectOperationKind kind, ProjectSnapshot project)
    {

        Log($"{kind} {project.Name}…");

        var progress = new Progress<ProjectOperationProgress>(report => Progress = report.Message);

        ProjectOperationResult result = await _workspace.ExecuteAsync(
            new ProjectOperationRequest
            {
                Kind = kind,
                Workspace = _workspace.Identity,
                EntryPointPath = EntryPoint,
                Configuration = Configuration,
                GlobalProperties = DesignerOutput,

                // The one project rather than the whole solution: a designer builds what it is
                // showing, and waiting for everything else is a wait nobody asked for.
                Projects = [project.Identity],
            },
            progress,
            _shutdown.Token);

        Log($"  {result.Status} — {Describe(result.Diagnostics.Length, "diagnostic")}");

        ShowDiagnostics(result.Diagnostics);
        ExplainLocks(result.Diagnostics);

        if (kind == ProjectOperationKind.Restore && result.Status == ProjectOperationStatus.Succeeded)
        {
            await _workspace.RefreshAsync(_shutdown.Token);
        }

        return result.Status;
    }

    /// <summary>
    /// Says in one sentence what a dozen lines of MSB3026 mean.
    /// </summary>
    /// <remarks>
    /// A build that cannot write its own output is nearly always an application still running from
    /// it, and MSBuild says so ten times over while it retries and then once more when it gives up.
    /// The designer builds into a folder of its own so that the other tool's running application is
    /// not the cause — which leaves its own, and the answer to that is Stop.
    /// </remarks>
    private void ExplainLocks(System.Collections.Immutable.ImmutableArray<ProjectDiagnostic> diagnostics)
    {
        if (!diagnostics.Any(diagnostic =>
                diagnostic.Message.Contains("MSB3027", StringComparison.Ordinal)
                || diagnostic.Message.Contains("MSB3026", StringComparison.Ordinal)
                || diagnostic.Message.Contains("MSB3021", StringComparison.Ordinal)))
        {
            return;
        }

        Log("  the output file is held by something that is running it —"
            + (IsRunning ? " press Stop and build again" : " close the application that is using it"));
    }

    /// <summary>The project everything acts on: whichever one owns the form being designed.</summary>
    private ProjectSnapshot? Project()
    {
        if (_workspace.CurrentSnapshot is not { } snapshot)
        {
            return null;
        }

        if (ActiveForm is { } form && snapshot.TryGetProjectForFile(form.File, out ProjectSnapshot? owner))
        {
            return owner;
        }

        return SelectedRunTarget() ?? snapshot.Projects.FirstOrDefault();
    }
}
