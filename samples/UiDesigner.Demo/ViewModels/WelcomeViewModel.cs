using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

namespace UiDesigner.Demo.ViewModels;

/// <summary>
/// The screen the demo opens on: what you have been working on, and the way in to anything else.
/// </summary>
/// <remarks>
/// <para>
/// A designer that opens straight into an empty canvas has asked its first question — which project?
/// — by not asking it. This asks: the projects you had open, and Open for the one that is not there.
/// </para>
/// <para>
/// That is all it offers, on purpose. It made projects from templates and cloned repositories once,
/// and neither is what this demo is for: it shows a designer working on a project, and a project is
/// something a person already has.
/// </para>
/// <para>
/// It owns no workspace and loads nothing. Choosing a project raises <see cref="ProjectChosen"/> and
/// the shell takes it from there, which keeps the one expensive thing this application does — MSBuild
/// evaluation — out of a window whose job is to be up instantly.
/// </para>
/// </remarks>
public sealed class WelcomeViewModel : Observable
{
    public WelcomeViewModel()
    {
        OpenCommand = new RelayCommand(() => Detached(OpenAsync));

        ShowRecent(RecentProjects.Read());
    }

    /// <summary>The projects offered for reopening, filtered by whatever is in the search box.</summary>
    public ObservableCollection<RecentProject> Recent { get; } = [];

    public RelayCommand OpenCommand { get; }

    /// <summary>Raised with the project file to open, once one has been chosen.</summary>
    public event EventHandler<string>? ProjectChosen;

    /// <summary>Asked of the view: a project file to open.</summary>
    public Func<Task<string?>>? PickProjectFile { get; set; }

    /// <summary>What the studio is, under its name.</summary>
    public string Edition { get; } = $"2026.2 · Avalonia {ProjectScaffold.AvaloniaVersion}";

    public string Search
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                ShowRecent(RecentProjects.Read());
            }
        }
    } = string.Empty;

    /// <summary>Whether there is anything to reopen, which decides what the middle of the page is.</summary>
    public bool HasRecent => Recent.Count > 0;

    /// <summary>What went wrong with the last thing that was asked for, when something did.</summary>
    public string Problem
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                Raise(nameof(HasProblem));
            }
        }
    } = string.Empty;

    public bool HasProblem => Problem.Length > 0;

    /// <summary>Opens a project from the recent list. Called by the view.</summary>
    public void Open(RecentProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Exists)
        {
            Problem = $"{project.Name} is no longer at that path";

            ShowRecent(RecentProjects.Forget(project.Path));

            return;
        }

        Choose(project.Path);
    }

    /// <summary>Takes a project out of the list without touching what is on disk. Called by the view.</summary>
    public void Forget(RecentProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        ShowRecent(RecentProjects.Forget(project.Path));
    }

    private async Task OpenAsync()
    {
        if (PickProjectFile is null || await PickProjectFile() is not { Length: > 0 } picked)
        {
            return;
        }

        Choose(picked);
    }

    private void Choose(string path)
    {
        ShowRecent(RecentProjects.Remember(path, Path.GetFileNameWithoutExtension(path)));

        ProjectChosen?.Invoke(this, path);
    }

    private void ShowRecent(IReadOnlyList<RecentProject> projects)
    {
        Recent.Clear();

        foreach (RecentProject project in projects)
        {
            if (Search is { Length: > 0 } filter
                && !project.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                && !project.Path.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Recent.Add(project);
        }

        Raise(nameof(HasRecent));
    }

    /// <summary>
    /// Runs work that nothing awaits, which is the one command on this page.
    /// </summary>
    /// <remarks>
    /// A dialog is awaited inside, so the command cannot be synchronous; and nothing can await the
    /// command, so the failure has to be caught here or it is lost. The page reports it in the one
    /// place a person is already looking.
    /// </remarks>
    private async void Detached(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception error)
        {
            Problem = $"{error.GetType().Name}: {error.Message}";
        }
    }
}
