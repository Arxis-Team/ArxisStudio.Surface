using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Surface.Editing;

namespace ArxisStudio.Tests;

/// <summary>
/// Тема библиотеки: ресурсы, варианты и то, что настраивается снаружи.
/// </summary>
/// <remarks>
/// Загрузка темы покрыта неявно — <c>TestApp</c> подключает её, и весь набор идёт
/// против настоящей темы. Не покрыто было другое: светлый вариант (стенд просит
/// только тёмный), разрешение ключей по имени и то, что документированная
/// настройка размера ручек действительно работает.
/// </remarks>
public class ThemeTests
{
    /// <summary>Ключи, которые README предъявляет как настраиваемые снаружи.</summary>
    private static readonly string[] DocumentedKeys =
    {
        "Surface.BackgroundBrush",
        "Surface.MarqueeStrokeBrush",
        "Surface.MarqueeFillBrush",
        "UiDesigner.ReorderIndicatorBrush",
        "Surface.SnapGuideBrush",
        "Surface.SnapGuide.Thickness",
        "Surface.SnapGuide.CentreBrush",
        "Surface.SnapGuide.DashStyle",
        "Surface.UserGuideBrush",
        "Surface.UserGuide.DashStyle",
        "Surface.Ruler.BackgroundBrush",
        "Surface.Ruler.TickBrush",
        "Surface.Ruler.LabelBrush",
        "Surface.Ruler.Thickness",
        "Surface.Ruler.CursorHorizontal",
        "Surface.Ruler.CursorVertical",
        "Surface.SelectionAdorner.CursorTopLeft",
        "Surface.SelectionAdorner.CursorTop",
        "Surface.SelectionAdorner.CursorTopRight",
        "Surface.SelectionAdorner.CursorLeft",
        "Surface.SelectionAdorner.CursorRight",
        "Surface.SelectionAdorner.CursorBottomLeft",
        "Surface.SelectionAdorner.CursorBottom",
        "Surface.SelectionAdorner.CursorBottomRight",
        "Surface.Grid.CellSize",
        "Surface.Grid.MajorInterval",
        "Surface.Grid.LineThickness",
        "Surface.Grid.MinCellSize",
        "Surface.Grid.LineBrush",
        "Surface.Grid.MajorLineBrush",
        "Surface.Selection.StrokeThickness",
        "Surface.Simplified.ItemFill",
        "Surface.Simplified.ItemStroke",
        "Surface.Simplified.AccentHeight",
        "Surface.SelectionAdorner.HandleSize",
        "UiDesignerItem.BorderThickness",
        "UiDesignerItem.CornerRadius",
        "UiDesignerItem.OutlineOpacity"
    };

    private static Window Show(Control content, ThemeVariant? variant = null)
    {
        var window = new Window { Width = 400, Height = 300, Content = content };
        if (variant != null)
            window.RequestedThemeVariant = variant;

        window.Show();
        var manager = window.GetLayoutManager();
        manager?.ExecuteInitialLayoutPass();
        manager?.ExecuteLayoutPass();
        return window;
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Documented_Resource_Keys_Resolve_In_Both_Variants(string variantName)
    {
        var variant = variantName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var editor = new UiDesignerView();
        var window = Show(editor, variant);

        foreach (var key in DocumentedKeys)
        {
            Assert.True(
                window.TryFindResource(key, variant, out var value) && value != null,
                $"ресурс {key} не разрешается в варианте {variantName}");
        }
    }

    [AvaloniaFact]
    public void Light_And_Dark_Give_Different_Brushes()
    {
        var editor = new UiDesignerView();
        var window = Show(editor);

        window.TryFindResource("Surface.BackgroundBrush", ThemeVariant.Light, out var light);
        window.TryFindResource("Surface.BackgroundBrush", ThemeVariant.Dark, out var dark);

        // ThemeDictionaries существуют ровно ради этого: варианты различаются
        // без дублирования ControlTheme.
        Assert.NotNull(light);
        Assert.NotNull(dark);
        Assert.NotEqual(((SolidColorBrush)light!).Color, ((SolidColorBrush)dark!).Color);
    }

    [AvaloniaTheory]
    [InlineData(8.0)]
    [InlineData(12.0)]
    [InlineData(20.0)]
    public void Handles_Stay_Centred_On_The_Edge_At_Any_Size(double handleSize)
    {
        var adorner = new SelectionAdorner
        {
            Width = 120,
            Height = 90,
            ShowHandles = true,
            HandleSize = handleSize
        };

        Show(adorner);

        var thumb = adorner.GetVisualDescendants().OfType<Thumb>().FirstOrDefault();
        Assert.NotNull(thumb);
        Assert.Equal(handleSize, thumb!.Width);

        // Ручка сидит на краю выделения, значит наружу торчит ровно половина.
        // Отступ был литералом -4 под размер 8: переопределение задокументированного
        // Surface.SelectionAdorner.HandleSize молча сдвигало все ручки.
        var expected = -handleSize / 2;
        Assert.Equal(expected, thumb.Margin.Left);
        Assert.Equal(expected, thumb.Margin.Top);
        Assert.Equal(expected, thumb.Margin.Right);
        Assert.Equal(expected, thumb.Margin.Bottom);
    }
}
