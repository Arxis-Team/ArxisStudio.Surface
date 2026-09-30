using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace UiDesigner.Demo.ViewModels;

/// <summary>
/// The few conversions the design needs, all of them looking a brush up by name.
/// </summary>
/// <remarks>
/// By name rather than by literal, because the palette has two variants and a hard-coded colour
/// would be right in one of them. Resolving through the application's resources is what lets the
/// theme switch reach a glyph drawn from a view model.
/// </remarks>
public static class Converters
{
    /// <summary>A hue key from <see cref="Glyphs"/> to the brush it names.</summary>
    public static IValueConverter Hue { get; } = new Lookup<string?>(static hue => hue ?? "Fg2");

    /// <summary>The accent fill when a flag is set, and nothing at all when it is not.</summary>
    /// <remarks>
    /// Transparent rather than the panel colour, so a segmented control sits on whatever is behind
    /// it without each segment having to know what that is. The fill and not the accent itself,
    /// because white text goes on it.
    /// </remarks>
    public static IValueConverter AccentWhenTrue { get; } =
        new Lookup<bool>(static on => on ? "AccFill" : null);

    /// <summary>The foreground a breadcrumb step or a dock tab takes when it is the current one.</summary>
    public static IValueConverter CurrentWhenTrue { get; } =
        new Lookup<bool>(static on => on ? "Fg" : "Fg2");

    /// <summary>The two-pixel underline the design puts under the active tab, and nothing otherwise.</summary>
    public static IValueConverter UnderlineWhenTrue { get; } =
        new Lookup<bool>(static on => on ? "Acc" : null);

    /// <summary>The text colour that goes with it.</summary>
    public static IValueConverter OnAccentWhenTrue { get; } =
        new Lookup<bool>(static on => on ? "OnAcc" : "Fg2");

    /// <summary>Finds a brush in the application's resources for the current variant.</summary>
    private static IBrush? Brush(string? key)
    {
        if (key is null || Application.Current is not { } application)
        {
            return null;
        }

        return application.TryGetResource(key, application.ActualThemeVariant, out object? found)
            && found is IBrush brush
                ? brush
                : null;
    }

    private sealed class Lookup<T>(Func<T, string?> key) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Brush(key(value is T typed ? typed : default!));

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException("These are for display only.");
    }
}
