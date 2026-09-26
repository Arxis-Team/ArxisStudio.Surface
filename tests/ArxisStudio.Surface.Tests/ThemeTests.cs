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
        "DesignEditor.BackgroundBrush",
        "DesignEditor.MarqueeStrokeBrush",
        "DesignEditor.MarqueeFillBrush",
        "DesignEditor.ReorderIndicatorBrush",
        "DesignEditor.SnapGuideBrush",
        "DesignEditor.SnapGuide.Thickness",
        "DesignEditor.SnapGuide.CentreBrush",
        "DesignEditor.SnapGuide.DashStyle",
        "DesignEditor.UserGuideBrush",
        "DesignEditor.UserGuide.DashStyle",
        "DesignEditor.Ruler.BackgroundBrush",
        "DesignEditor.Ruler.TickBrush",
        "DesignEditor.Ruler.LabelBrush",
        "DesignEditor.Ruler.Thickness",
        "DesignEditor.Ruler.CursorHorizontal",
        "DesignEditor.Ruler.CursorVertical",
        "DesignEditor.SelectionAdorner.CursorTopLeft",
        "DesignEditor.SelectionAdorner.CursorTop",
        "DesignEditor.SelectionAdorner.CursorTopRight",
        "DesignEditor.SelectionAdorner.CursorLeft",
        "DesignEditor.SelectionAdorner.CursorRight",
        "DesignEditor.SelectionAdorner.CursorBottomLeft",
        "DesignEditor.SelectionAdorner.CursorBottom",
        "DesignEditor.SelectionAdorner.CursorBottomRight",
        "DesignEditor.Grid.CellSize",
        "DesignEditor.Grid.MajorInterval",
        "DesignEditor.Grid.LineThickness",
        "DesignEditor.Grid.MinCellSize",
        "DesignEditor.Grid.LineBrush",
        "DesignEditor.Grid.MajorLineBrush",
        "DesignEditor.Selection.StrokeThickness",
        "DesignEditor.SelectionAdorner.HandleSize",
        "DesignEditorItem.BorderThickness",
        "DesignEditorItem.CornerRadius",
        "DesignEditorItem.OutlineOpacity"
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
        var editor = new DesignEditor();
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
        var editor = new DesignEditor();
        var window = Show(editor);

        window.TryFindResource("DesignEditor.BackgroundBrush", ThemeVariant.Light, out var light);
        window.TryFindResource("DesignEditor.BackgroundBrush", ThemeVariant.Dark, out var dark);

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
        // DesignEditor.SelectionAdorner.HandleSize молча сдвигало все ручки.
        var expected = -handleSize / 2;
        Assert.Equal(expected, thumb.Margin.Left);
        Assert.Equal(expected, thumb.Margin.Top);
        Assert.Equal(expected, thumb.Margin.Right);
        Assert.Equal(expected, thumb.Margin.Bottom);
    }
}
