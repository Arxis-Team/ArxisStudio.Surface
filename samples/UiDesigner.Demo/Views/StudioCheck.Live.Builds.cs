using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ArxisStudio.ProjectSystem;
using UiDesigner.Demo.ViewModels;

namespace UiDesigner.Demo.Views;

/// <summary>
/// Part 2 of <c>--live</c>: what the designer makes of the IDE's writes to the project, and how its
/// own builds stay out of the IDE's way.
/// </summary>
/// <remarks>
/// <para>
/// A save is a changed document, not a project to read again; a new file is one re-read; a build
/// the IDE ran leaves nothing in the designer's project; and the designer's own build passes while
/// the application the IDE started holds its output. The first two are ProjectSystem's
/// classification at work (its ADR 0025), the last two its design output (ADR 0026).
/// </para>
/// <para>
/// The IDE's side is played as in part 1: files are written the way Rider writes them, and the
/// ordinary build is <c>dotnet build</c>, into <c>bin/Debug</c> and <c>obj/Debug</c>.
/// </para>
/// </remarks>
internal static partial class StudioCheck
{
    private static async Task<int> BesideTheIdeAsync(DesignerViewModel designer, FormViewModel form, string project)
    {
        var failures = 0;
        string file = form.File.Value;

        // Saved here first: an IDE's save over unsaved edits is a question (part 1), not this.
        int settled = designer.SettledBatches;

        designer.SaveCommand.Execute(null);

        if (!await Until(() => !form.IsDirty, 30) || !await Until(() => designer.SettledBatches > settled, 30))
        {
            return Fail(ref failures, "save: the designer's own save never settled");
        }

        // An IDE's save of the open form: the form follows, and the project is not read again. The save
        // puts back the member part 1 renamed in the binding, so that the project builds again.
        long before = Version(designer);

        settled = designer.SettledBatches;

        await SaveLikeAnIdeAsync(file, Disk(file)
            .Replace("{Binding Titel}", "{Binding Title}", StringComparison.Ordinal)
            .Replace("</Window>", "  <!-- saved beside the IDE -->\n</Window>", StringComparison.Ordinal));

        if (!await Until(() => Text(form).Contains("saved beside the IDE", StringComparison.Ordinal), 60)
            || !await Until(() => designer.SettledBatches > settled, 30))
        {
            Fail(ref failures, "save: the IDE's save never reached the form");
        }
        else if (Version(designer) != before)
        {
            Fail(ref failures, $"save: one save of a form read the project again (v{before} -> v{Version(designer)})");
        }
        else
        {
            Say("save: an IDE's save of a form is one changed document, and the project is not read again");
        }

        // A new file written by the IDE: one re-read, and the project has it.
        string extra = Path.Combine(Path.GetDirectoryName(file)!, "Extra.axaml");

        before = Version(designer);
        settled = designer.SettledBatches;

        await File.WriteAllTextAsync(extra, "<UserControl xmlns=\"https://github.com/avaloniaui\" />\n");

        if (!await Until(() => Declares(designer, extra), 60) || !await Until(() => designer.SettledBatches > settled, 30))
        {
            Fail(ref failures, "new file: a file the IDE wrote never reached the project");
        }
        else if (Version(designer) != before + 1)
        {
            Fail(ref failures, $"new file: one new file read the project {Version(designer) - before} time(s)");
        }
        else
        {
            Say("new file: one file the IDE wrote is one re-read, and the project declares it");
        }

        // The IDE's own build — into bin/Debug and obj/Debug — and nothing of it in the designer's project.
        (int exit, string output) = await RunAsync("dotnet", $"build \"{project}\" -nologo -v q");

        if (exit != 0)
        {
            foreach (string line in output.Split('\n').Where(static line => line.Contains("error", StringComparison.OrdinalIgnoreCase)).Take(5))
            {
                Say("  ordinary build: " + line.Trim());
            }

            return Fail(ref failures, $"build: the ordinary build failed with exit code {exit}");
        }

        CanonicalPath debugOutput = CanonicalPath.Create(Path.Combine(Path.GetDirectoryName(project)!, "bin", "Debug"));
        CanonicalPath debugIntermediate = CanonicalPath.Create(Path.Combine(Path.GetDirectoryName(project)!, "obj", "Debug"));

        if (designer.CurrentSnapshot?.Projects.SelectMany(static p => p.Items)
                .FirstOrDefault(item => item.FullPath.StartsWith(debugOutput) || item.FullPath.StartsWith(debugIntermediate)) is { } taken)
        {
            Fail(ref failures, $"build: the IDE's build output is an item of the project ({taken.FullPath})");
        }

        // The IDE saves the window's code: the designer builds into its own folders, and passes.
        string code = file + ".cs";

        if (await BuildsAfterACodeSaveAsync(designer, code, "after the IDE's build") is { } unbuilt)
        {
            Fail(ref failures, "build: " + unbuilt);
        }
        else
        {
            Say("build: after the IDE's build filled obj/Debug, the designer's build passes");
        }

        // The application the IDE started runs from bin/Debug and holds it; the designer's build does not care.
        string exe = Path.Combine(debugOutput.Value, "net10.0", Path.GetFileNameWithoutExtension(project) + ".exe");

        if (!File.Exists(exe))
        {
            return Fail(ref failures, $"build: the ordinary build left no {Path.GetFileName(exe)} to run");
        }

        using Process app = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! })!;

        try
        {
            if (await BuildsAfterACodeSaveAsync(designer, code, "while the IDE's application runs") is { } blocked)
            {
                Fail(ref failures, "running: " + blocked);
            }
            else if (app.HasExited)
            {
                Fail(ref failures, "running: the IDE's application did not stay up, so nothing was held");
            }
            else
            {
                Say("running: with the IDE's application holding bin/Debug, the designer's build passes");
            }
        }
        finally
        {
            if (!app.HasExited)
            {
                app.Kill(entireProcessTree: true);
                await app.WaitForExitAsync();
            }
        }

        return failures;
    }

    /// <summary>
    /// Saves a code file the way the IDE does and waits for the designer's build of it, answering what
    /// went wrong, or <see langword="null"/> when it built.
    /// </summary>
    private static async Task<string?> BuildsAfterACodeSaveAsync(DesignerViewModel designer, string code, string when)
    {
        int builds = designer.Builds;

        await SaveLikeAnIdeAsync(code, Disk(code) + $"// saved {when}\n");

        // The design host's own report of the build that followed, after its quiet moment.
        if (!await Until(() => designer.Builds > builds, 180) || designer.LastBuild is not { } built)
        {
            return $"no build followed the IDE's save of the code {when}";
        }

        if (built.Diagnostics.FirstOrDefault(static diagnostic =>
                diagnostic.Code is "MSB3027" or "MSB3021"
                || diagnostic.Message.Contains("MSB3027", StringComparison.Ordinal)
                || diagnostic.Message.Contains("MSB3021", StringComparison.Ordinal)) is { } locked)
        {
            return $"the designer's build met a locked file {when}: {locked.Message}";
        }

        return built.Status == ProjectOperationStatus.Succeeded
            ? null
            : $"the designer's build did not pass {when}: {string.Join(" | ", built.Diagnostics.Where(static d => d.Severity == ProjectDiagnosticSeverity.Error).Select(static d => d.Message).Take(3))}";
    }

    /// <summary>The version of what the workspace holds.</summary>
    private static long Version(DesignerViewModel designer) => designer.CurrentSnapshot?.Version.Value ?? 0;

    /// <summary>Whether a project of the open solution declares the file.</summary>
    private static bool Declares(DesignerViewModel designer, string path) =>
        designer.CurrentSnapshot is { } snapshot && snapshot.TryGetItem(CanonicalPath.Create(path), out _, out _);

    /// <summary>Runs a tool to the end, answering its exit code and what it printed.</summary>
    private static async Task<(int Exit, string Output)> RunAsync(string tool, string arguments)
    {
        using Process process = Process.Start(new ProcessStartInfo(tool, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        })!;

        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return (process.ExitCode, await output + await error);
    }
}
