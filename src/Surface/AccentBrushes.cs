using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ArxisStudio.Surface;

/// <summary>
/// Кисть полосы заголовка из значения привязки хоста: кисть — как есть, цвет — кистью, одной на цвет.
/// </summary>
/// <remarks>
/// Один кеш на поверхности и её слои (ADR 0009): ячейка свёрнутого элемента, контейнер и запись связи
/// получают для одного цвета один и тот же объект, поэтому упрощённый вид группирует полосы и провода
/// по кисти, а не по цвету. Живёт на UI-потоке, как всё, что его зовёт.
/// </remarks>
internal static class AccentBrushes
{
    private static readonly Dictionary<Color, IBrush> s_byColor = new();

    /// <summary>
    /// Кисть для значения привязки; иное, как и <see langword="null"/>, полосы не даёт.
    /// </summary>
    public static IBrush? From(object? value)
    {
        switch (value)
        {
            case IBrush brush:
                return brush;
            case Color color:
                if (!s_byColor.TryGetValue(color, out var cached))
                    s_byColor[color] = cached = new ImmutableSolidColorBrush(color);

                return cached;
            default:
                return null;
        }
    }
}
