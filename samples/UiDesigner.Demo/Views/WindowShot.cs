using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using UiDesigner.Demo.ViewModels;

namespace UiDesigner.Demo.Views;

/// <summary>
/// Renders the window to a file and closes.
/// </summary>
/// <remarks>
/// <para>
/// A designer is a thing you look at, so "does it work" and "does it look right" are different
/// questions and only one of them has ever been answerable from a log. This makes the second one
/// answerable too: <c>--shot out.png</c> opens the project, waits until the window has settled, and
/// writes exactly what is on screen.
/// </para>
/// <para>
/// Settled is waited for rather than slept through. Yielding at <see cref="DispatcherPriority.Background"/>
/// lets layout and render run between each check, so the loop ends when the work has finished
/// instead of when a guess about how long it takes has expired.
/// </para>
/// </remarks>
internal static class WindowShot
{
    public static void TakeAfterLoad(Window window, DesignerViewModel designer, string path) =>
        window.Opened += (_, _) => _ = TakeAsync(window, designer, path);

    /// <summary>The same for a window with nothing to load, which is the one the studio opens on.</summary>
    public static void TakeWhenShown(Window window, string path) =>
        window.Opened += (_, _) => _ = TakeShownAsync(window, path);

    private static async Task TakeShownAsync(Window window, string path)
    {
        for (int drawing = 0; drawing < 40; drawing++)
        {
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
        }

        var size = new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height);

        if (size.Width <= 0 || size.Height <= 0)
        {
            Console.WriteLine("! the window has no size to capture");

            window.Close();

            return;
        }

        using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));

        bitmap.Render(window);
        bitmap.Save(path, new PngBitmapEncoderOptions());

        Console.WriteLine($"shot {size.Width}×{size.Height} → {path}");

        window.Close();
    }

    private static async Task TakeAsync(Window window, DesignerViewModel designer, string path)
    {
        // Bounded, because a project that never finishes loading must not leave a process that never
        // finishes either. The number is generous and the loop usually ends long before it.
        for (int settling = 0; settling < 400; settling++)
        {
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);

            if (!designer.IsBusy && designer.Hierarchy.Count > 0 && window.Bounds.Width > 0)
            {
                break;
            }
        }

        // A few more, so the last layout pass the load provoked has actually been drawn.
        for (int drawing = 0; drawing < 12; drawing++)
        {
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
        }

        var size = new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height);

        if (size.Width <= 0 || size.Height <= 0)
        {
            Console.WriteLine("! the window has no size to capture");

            window.Close();

            return;
        }

        using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));

        bitmap.Render(window);
        bitmap.Save(path, new PngBitmapEncoderOptions());

        Console.WriteLine($"shot {size.Width}×{size.Height} → {path}");

        window.Close();
    }
}
