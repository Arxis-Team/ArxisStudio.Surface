using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace UiDesigner.Demo.ViewModels;

/// <summary>One project the studio has opened before.</summary>
public sealed record RecentProject(string Name, string Path, DateTimeOffset Opened)
{
    /// <summary>The folder, written the way a person wrote it rather than in full.</summary>
    /// <remarks>
    /// The home directory is a prefix everybody has and nobody reads, so it is replaced by the
    /// character that has meant it since the seventies.
    /// </remarks>
    public string Location
    {
        get
        {
            string? directory = System.IO.Path.GetDirectoryName(Path);

            if (directory is not { Length: > 0 })
            {
                return Path;
            }

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (home is { Length: > 0 } && directory.StartsWith(home, StringComparison.OrdinalIgnoreCase))
            {
                directory = "~" + directory[home.Length..];
            }

            return directory.Replace('\\', '/');
        }
    }

    /// <summary>The two letters the tile shows, taken the way initials are taken.</summary>
    public string Initials
    {
        get
        {
            string[] words = Name.Split(
                [' ', '-', '_', '.'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return words.Length switch
            {
                0 => "··",
                1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
                _ => string.Concat(words[0][..1], words[1][..1]).ToUpperInvariant(),
            };
        }
    }

    /// <summary>Which of the palette's tiles the project is, decided by the name so it does not move.</summary>
    /// <remarks>
    /// By the sum of its characters and not by <c>GetHashCode</c>: a string's hash is seeded per
    /// process, so the same project was yellow in one run and green in the next. The keys are the
    /// tiles made to carry white initials, not the hues glyphs are drawn in — white on those stood
    /// at 2.1:1.
    /// </remarks>
    public string Hue
    {
        get
        {
            var sum = 0;

            foreach (char letter in Name)
            {
                sum = (sum + letter) % 5;
            }

            return sum switch
            {
                0 => "AccFill",
                1 => "MonoGrn",
                2 => "MonoOrg",
                3 => "MonoPur",
                _ => "MonoRed",
            };
        }
    }

    /// <summary>The Avalonia the project references, read from the project file.</summary>
    public string Framework => ProjectScaffold.VersionOf(Path);

    /// <summary>When it was last opened, in the words a person would use for it.</summary>
    public string Ago
    {
        get
        {
            TimeSpan since = DateTimeOffset.Now - Opened;

            return since switch
            {
                { TotalMinutes: < 1 } => "just now",
                { TotalHours: < 1 } => $"{(int)since.TotalMinutes} min ago",
                { TotalDays: < 1 } => Opened.ToLocalTime().ToString("HH:mm", Culture),
                { TotalDays: < 2 } => "yesterday " + Opened.ToLocalTime().ToString("HH:mm", Culture),
                { TotalDays: < 7 } => $"{(int)since.TotalDays} days ago",
                _ => Opened.ToLocalTime().ToString("d MMMM", Culture),
            };
        }
    }

    /// <summary>The culture the dates are written in.</summary>
    /// <remarks>
    /// The invariant one, which writes a month in English like every other word on this screen. It
    /// is also the only one there is: the demo runs in invariant-globalization mode, and asking for
    /// any other by name throws.
    /// </remarks>
    private static readonly System.Globalization.CultureInfo Culture =
        System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>Whether the project is still where it was when it was last opened.</summary>
    public bool Exists => File.Exists(Path);
}

/// <summary>
/// The list of projects the studio offers to reopen.
/// </summary>
/// <remarks>
/// Kept beside the user's other application data rather than beside the executable, because a tool
/// that writes to its own install directory is a tool that fails the first time it is installed
/// somewhere a user cannot write to.
/// </remarks>
public static class RecentProjects
{
    private const int Keep = 12;

    private static readonly JsonSerializerOptions Format = new() { WriteIndented = true };

    private static string File => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ArxisStudio",
        "UiDesigner.Demo",
        "recent.json");

    /// <summary>Reads the list, newest first, dropping anything that has since been moved away.</summary>
    public static IReadOnlyList<RecentProject> Read()
    {
        try
        {
            if (!System.IO.File.Exists(File))
            {
                return [];
            }

            RecentProject[]? read = JsonSerializer.Deserialize<RecentProject[]>(
                System.IO.File.ReadAllText(File));

            return read is null
                ? []
                : [.. read.Where(static project => project.Exists).OrderByDescending(static p => p.Opened)];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            // A list of shortcuts is not worth an error message. An unreadable one is an empty one.
            return [];
        }
    }

    /// <summary>Puts a project at the top of the list, and writes it back.</summary>
    public static IReadOnlyList<RecentProject> Remember(string path, string name)
    {
        var updated = new List<RecentProject>
        {
            new(name, path, DateTimeOffset.Now),
        };

        updated.AddRange(Read().Where(project =>
            !string.Equals(project.Path, path, StringComparison.OrdinalIgnoreCase)));

        if (updated.Count > Keep)
        {
            updated.RemoveRange(Keep, updated.Count - Keep);
        }

        Write(updated);

        return updated;
    }

    /// <summary>Takes one out, for a project somebody no longer wants offered.</summary>
    public static IReadOnlyList<RecentProject> Forget(string path)
    {
        List<RecentProject> kept =
        [
            .. Read().Where(project => !string.Equals(project.Path, path, StringComparison.OrdinalIgnoreCase)),
        ];

        Write(kept);

        return kept;
    }

    private static void Write(IReadOnlyList<RecentProject> projects)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(File)!);

            System.IO.File.WriteAllText(File, JsonSerializer.Serialize(projects, Format));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Same again: the studio works without this file, so failing to write it is not an event.
        }
    }
}
