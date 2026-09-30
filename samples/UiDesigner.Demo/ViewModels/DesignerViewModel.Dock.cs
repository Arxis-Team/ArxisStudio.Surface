using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using ArxisStudio.ProjectSystem;
using Avalonia.Media;

namespace UiDesigner.Demo.ViewModels;

/// <summary>A folder of the open project, flattened with its depth like the hierarchy.</summary>
public sealed record FolderRow(string Name, string Path, double Indent, bool HasChildren);

/// <summary>A file, as the design's grid draws it: a large glyph over a wrapped name.</summary>
/// <summary>One file in the project pane's grid.</summary>
/// <remarks>
/// Observable rather than a record because one of its facts changes while it is on screen: a file
/// that is open in the editor is drawn as open, and it becomes open and stops being open without the
/// grid being rebuilt around it.
/// </remarks>
public sealed class FileTile(string name, CanonicalPath path, Geometry glyph, string hue) : Observable
{
    public string Name { get; } = name;

    public CanonicalPath Path { get; } = path;

    public Geometry Glyph { get; } = glyph;

    public string Hue { get; } = hue;

    /// <summary>Whether this is markup, and so a form the designer can open and delete.</summary>
    public bool IsMarkup => DesignerViewModel.IsMarkupExtension(Path.Extension);

    /// <summary>Whether this file has a tab, which the grid shows without anybody having to look up.</summary>
    public bool IsOpen
    {
        get;
        internal set => Set(ref field, value);
    }
}

public sealed partial class DesignerViewModel
{
    /// <summary>Every log line, split the way the design splits it.</summary>
    public ObservableCollection<LogRow> Logs { get; } = [];

    /// <summary>The project's folders, for the left half of the Project tab.</summary>
    public ObservableCollection<FolderRow> ProjectFolders { get; } = [];

    /// <summary>What is in the selected folder, for the right half.</summary>
    public ObservableCollection<FileTile> ProjectFiles { get; } = [];

    public FolderRow? SelectedFolder
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                ShowFolder();
            }
        }
    }

    private readonly Dictionary<string, List<FileTile>> _filesByFolder = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Turns a log line into the design's three columns.
    /// </summary>
    /// <remarks>
    /// The level is read off the text rather than passed in, because everything that logs here
    /// already says what it means: a line beginning with an exclamation is a failure, an indented
    /// one is detail about the line above it, and a bare one is a step. Inventing a level parameter
    /// would mean editing forty call sites to say what they already say.
    /// </remarks>
    private void Record(string message)
    {
        string level = message.StartsWith("! ", StringComparison.Ordinal)
            || message.StartsWith("  ! ", StringComparison.Ordinal)
                ? "ERROR"
                : message.StartsWith("  │", StringComparison.Ordinal)
                    ? "RUN"
                    : message.StartsWith("  ", StringComparison.Ordinal)
                        ? string.Empty
                        : "INFO";

        Logs.Add(new LogRow(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture), level, message.Trim()));

        while (Logs.Count > 500)
        {
            Logs.RemoveAt(0);
        }
    }

    /// <summary>
    /// Builds the folder tree and the file index from the snapshot.
    /// </summary>
    /// <remarks>
    /// From the project's items, like everything else in this window: a folder exists because
    /// something the project compiles is in it. A directory full of files nothing includes is not
    /// part of the application and showing it invites editing something with no effect.
    /// </remarks>
    private void BuildProjectPane(SolutionSnapshot snapshot)
    {
        ProjectFolders.Clear();
        ProjectFiles.Clear();
        _filesByFolder.Clear();

        var order = new List<string>();

        foreach (ProjectSnapshot project in snapshot.Projects)
        {
            Add(project.Name, string.Empty);

            foreach (ProjectItem item in project.Items)
            {
                if (item.FullPath.IsEmpty
                    || !item.FullPath.StartsWith(project.ProjectDirectory)
                    || !System.IO.File.Exists(item.FullPath.Value))
                {
                    continue;
                }

                string relative = item.FullPath.Value[project.ProjectDirectory.Value.Length..]
                    .Replace('\\', '/')
                    .TrimStart('/');

                if (relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)
                    || relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int slash = relative.LastIndexOf('/');
                string folder = slash < 0 ? project.Name : $"{project.Name}/{relative[..slash]}";

                Add(project.Name, slash < 0 ? string.Empty : relative[..slash]);

                if (!_filesByFolder.TryGetValue(folder, out List<FileTile>? files))
                {
                    _filesByFolder[folder] = files = [];
                }

                files.Add(new FileTile(
                    item.FullPath.FileName,
                    item.FullPath,
                    Glyphs.ForFile(item.FullPath.Extension),
                    Glyphs.HueOfFile(item.FullPath.Extension)));
            }
        }

        foreach (string path in order)
        {
            int depth = path.Count(c => c == '/');
            int slash = path.LastIndexOf('/');

            ProjectFolders.Add(new FolderRow(
                slash < 0 ? path : path[(slash + 1)..],
                path,
                8 + (depth * 16),
                order.Any(other => other.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase))));
        }

        SelectedFolder = ProjectFolders.FirstOrDefault();

        void Add(string project, string relative)
        {
            // Every folder on the way down, so a file three deep does not appear under a root that
            // has no path to it.
            string[] parts = relative.Length == 0 ? [] : relative.Split('/');
            string path = project;

            if (!order.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                order.Add(path);
            }

            foreach (string part in parts)
            {
                path = path + "/" + part;

                if (!order.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    order.Add(path);
                }
            }
        }
    }

    private void ShowFolder()
    {
        ProjectFiles.Clear();

        if (SelectedFolder is not { } folder
            || !_filesByFolder.TryGetValue(folder.Path, out List<FileTile>? files))
        {
            return;
        }

        foreach (FileTile file in files.OrderBy(static f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            ProjectFiles.Add(file);
        }
    }

    /// <summary>
    /// Opens a file from the grid, when it is one this designer can open.
    /// </summary>
    /// <remarks>
    /// A designer opens markup; the grid shows everything in the folder, because a project pane
    /// that hid what it could not open would be lying about the project. A double click on a
    /// <c>.cs</c> file therefore says why nothing happened, which is the whole difference between a
    /// tool that declined and a tool that is broken.
    /// </remarks>
    /// <summary>Tells the tiles which files have a tab, so the grid can say so.</summary>
    internal void MarkOpenFiles()
    {
        foreach (List<FileTile> tiles in _filesByFolder.Values)
        {
            foreach (FileTile tile in tiles)
            {
                tile.IsOpen = Forms.Any(form => form.File == tile.Path);
            }
        }
    }

    /// <summary>Asked of the view: a yes before something on disk is destroyed.</summary>
    public Func<string, string, Task<bool>>? AskToConfirm { get; set; }

    /// <summary>Deletes a form, the file and its code-behind. Called by the view.</summary>
    public void DeleteFile(FileTile file) => RunDetached(() => DeleteFileAsync(file));

    /// <summary>
    /// Takes a form out of the project.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The code-behind goes with it. A form is two files that only mean anything together — a
    /// <c>MainWindow.axaml</c> with no <c>MainWindow.axaml.cs</c> leaves a partial class nothing
    /// completes, and the project stops compiling for a reason the person who deleted one file will
    /// not connect to it. Both are named in the question, so nobody is surprised by the second.
    /// </para>
    /// <para>
    /// Asked before, because this is the one thing this designer does that cannot be undone: Ctrl+Z
    /// goes back through documents, and a document whose file is gone is not one of them.
    /// </para>
    /// <para>
    /// The workspace is re-read afterwards rather than the lists edited by hand. The project's items
    /// come from an evaluation, so a file that has gone is still one of them until the evaluation
    /// says otherwise — the same reason a newly created form needs a refresh before it can be
    /// opened.
    /// </para>
    /// </remarks>
    private async Task DeleteFileAsync(FileTile file)
    {
        if (!file.IsMarkup)
        {
            Log($"! {file.Name} is not a form — this designer deletes the forms it can open");

            return;
        }

        string codeBehind = file.Path.Value + ".cs";
        bool hasCodeBehind = System.IO.File.Exists(codeBehind);

        string what = hasCodeBehind
            ? $"{file.Name} and {file.Name}.cs will be deleted from disk."
            : $"{file.Name} will be deleted from disk.";

        if (AskToConfirm is null
            || !await AskToConfirm("Delete the form", what + " This cannot be undone."))
        {
            return;
        }

        // Closed first. Deleting the file under an open form would leave a tab editing a document
        // with nowhere to save to, and the canvas showing a form that no longer exists.
        if (Forms.FirstOrDefault(form => form.File == file.Path) is { } open)
        {
            CloseForm(open);
        }

        try
        {
            System.IO.File.Delete(file.Path.Value);

            if (hasCodeBehind)
            {
                System.IO.File.Delete(codeBehind);
            }
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        {
            Log($"! {file.Name} could not be deleted: {error.Message}");

            return;
        }

        Log($"Deleted {file.Name}{(hasCodeBehind ? " and its code-behind" : string.Empty)}.");

        await _workspace.RefreshAsync(_shutdown.Token);
    }

    public void OpenFile(FileTile file)
    {
        if (ProjectForms.FirstOrDefault(form => form.Path == file.Path) is { } form)
        {
            // Opened directly rather than by assigning the selection. The setter acts only on a
            // change, and FormFile is a record compared by value, so double-clicking the file that
            // was already selected did nothing at all — not even re-activate the form it names,
            // which is the whole of what a second double-click is asking for.
            SelectedProjectForm = form;

            RunDetached(() => OpenFormAsync(form));

            return;
        }

        // Markup that is not on the list is markup that declares no form -- App.axaml and its kind.
        // Saying which of the two it is, rather than one message for both, is the difference between
        // "this designer cannot open that" and "that is not a thing to open".
        Log(IsMarkup(file.Path)
            ? $"! {file.Name} declares the application's styles and resources, not a form"
            : $"! {file.Name} is not markup — this designer opens .axaml and .xaml");
    }
}
