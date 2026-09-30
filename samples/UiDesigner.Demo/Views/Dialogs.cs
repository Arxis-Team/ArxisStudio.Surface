using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace UiDesigner.Demo.Views;

/// <summary>
/// The parts a question is built from, in this tool's own clothes.
/// </summary>
/// <remarks>
/// <para>
/// Both windows ask their questions with dialogs made in code — each is a handful of controls, and
/// a window per question would be a file per question to keep in step with the design. A window
/// made in code starts with the theme's look and not the designer's: the theme's background, its
/// fields, and buttons in the machine's own accent colour. So the dialogs looked like another
/// program's.
/// </para>
/// <para>
/// What brings the tool's rules in is the same thing that brings them to every panel of the two
/// main windows: the <c>chrome</c> class on the root, and a class on each button saying which kind
/// it is. The brushes are asked for by key, so a dialog follows the palette when it changes.
/// </para>
/// </remarks>
internal static class Dialogs
{
    /// <summary>A dialog holding these rows, top to bottom.</summary>
    public static Window Create(string title, double width, params Control[] rows)
    {
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 10 };

        panel.Classes.Add("chrome");
        panel.Children.AddRange(rows);

        var dialog = new Window
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = panel,
        };

        dialog.Bind(TemplatedControl.BackgroundProperty, dialog.GetResourceObservable("Bg2"));

        return dialog;
    }

    /// <summary>The button that does what the dialog is about.</summary>
    public static Button Primary(string text)
    {
        var button = new Button { Content = text };

        button.Classes.Add("primary");

        return button;
    }

    /// <summary>A button beside it: another answer, or none.</summary>
    public static Button Quiet(string text)
    {
        var button = new Button { Content = text, Height = 28 };

        button.Classes.Add("quiet");

        return button;
    }

    /// <summary>The row of answers a dialog ends with, against its right edge.</summary>
    public static StackPanel Answers(params Button[] buttons)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        row.Children.AddRange(buttons);

        return row;
    }

    /// <summary>A label over a field, in the quieter of the text colours.</summary>
    public static TextBlock Label(string text)
    {
        var label = new TextBlock { Text = text, FontSize = 12 };

        label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("Fg2"));

        return label;
    }

    /// <summary>What a dialog says, wrapped to its width.</summary>
    public static TextBlock Message(string text) =>
        new() { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
}
