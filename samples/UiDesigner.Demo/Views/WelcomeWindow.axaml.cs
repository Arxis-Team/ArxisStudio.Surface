using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using UiDesigner.Demo.ViewModels;

namespace UiDesigner.Demo.Views;

/// <summary>
/// The window the demo starts in, and the one dialog it needs to answer for a project.
/// </summary>
/// <remarks>
/// The same division as the designer's own window: the view model decides what happens and this
/// file knows what a file picker is.
/// </remarks>
public sealed partial class WelcomeWindow : Window
{
    public WelcomeWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // On the way down rather than on the way up. A list takes Enter for itself — it is how a
        // row is chosen — and marks it handled, so a handler written on the list in markup is never
        // told about the one key it was written for.
        this.GetControl<ListBox>("RecentList").AddHandler(KeyDownEvent, OnRecentKeyDown, RoutingStrategies.Tunnel);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is WelcomeViewModel welcome)
            {
                welcome.PickProjectFile = PickProjectFileAsync;
            }
        };
    }

    private WelcomeViewModel? Welcome => DataContext as WelcomeViewModel;

    private void OnRecentOpened(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: RecentProject project } && Welcome is { } welcome)
        {
            welcome.Open(project);
        }
    }

    /// <summary>
    /// What a double click and the row's cross do, for a row the keyboard stopped on.
    /// </summary>
    /// <remarks>
    /// The row is the one with the focus, not the one selected: Tab arrives on a row without
    /// selecting it, and the list selects it on the same Enter, after this has run. And it has to
    /// be the row itself — the row holds a button of its own, and Enter on the cross is that
    /// button's press; opening the project it was about to remove would be the opposite of what
    /// was asked for.
    /// </remarks>
    private void OnRecentKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is not ListBoxItem { DataContext: RecentProject project } || Welcome is not { } welcome)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                welcome.Open(project);
                e.Handled = true;

                break;

            case Key.Delete:
                welcome.Forget(project);
                e.Handled = true;

                break;
        }
    }

    private void OnRecentForgotten(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: RecentProject project } && Welcome is { } welcome)
        {
            welcome.Forget(project);
        }
    }

    private async Task<string?> PickProjectFileAsync()
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Open a solution or project",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Solutions and projects")
                    {
                        Patterns = ["*.sln", "*.slnx", "*.csproj", "*.fsproj", "*.vbproj"],
                    },
                ],
            });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }
}
