using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Tests;

/// <summary>
/// DPI-aware трансформация viewport.
/// </summary>
/// <remarks>
/// Редактор публикует две трансформации: точную <see cref="DesignEditor.ViewportTransform"/>
/// и <see cref="DesignEditor.DpiScaledViewportTransform"/>, у которой смещение округлено
/// до целого физического пикселя — иначе фон и сетка размываются.
/// <para>
/// Тесты закрепляют подписку на смену DPI: она резолвится через
/// <c>TopLevel.GetTopLevel(this)</c> при подключении к дереву. Ранее использовалось
/// <c>e.RootVisual is TopLevel</c>, что могло молча не сработать.
/// </para>
/// </remarks>
public class DpiScalingTests
{
    private static TranslateTransform DpiTranslate(DesignEditor editor)
    {
        var group = Assert.IsType<TransformGroup>(editor.DpiScaledViewportTransform);
        return Assert.IsType<TranslateTransform>(group.Children[^1]);
    }

    private static TranslateTransform ExactTranslate(DesignEditor editor)
    {
        var group = Assert.IsType<TransformGroup>(editor.ViewportTransform);
        return Assert.IsType<TranslateTransform>(group.Children[^1]);
    }

    [AvaloniaFact]
    public void Dpi_Translate_Matches_Exact_Translate_At_Scaling_One()
    {
        var harness = EditorHarness.Create();
        harness.Editor.ViewportLocation = new Point(100.3, 50.7);
        harness.RunLayout();

        var exact = ExactTranslate(harness.Editor);
        var dpi = DpiTranslate(harness.Editor);

        // При scaling = 1 округление до физического пикселя даёт целые значения.
        Assert.Equal(-100.3, exact.X, 3);
        Assert.Equal(-100.0, dpi.X, 3);
        Assert.Equal(-51.0, dpi.Y, 3);
    }

    [AvaloniaFact]
    public void Changing_Render_Scaling_Updates_Dpi_Translate()
    {
        var harness = EditorHarness.Create();
        harness.Editor.ViewportLocation = new Point(100.3, 0);
        harness.RunLayout();

        Assert.Equal(-100.0, DpiTranslate(harness.Editor).X, 3);

        // Смена DPI должна дойти до редактора через подписку на ScalingChanged.
        harness.Window.SetRenderScaling(2.0);
        harness.RunLayout();

        // Round(-100.3 * 2) / 2 = -201 / 2 = -100.5
        Assert.Equal(-100.5, DpiTranslate(harness.Editor).X, 3);
    }

    [AvaloniaFact]
    public void Exact_Translate_Is_Unaffected_By_Render_Scaling()
    {
        var harness = EditorHarness.Create();
        harness.Editor.ViewportLocation = new Point(100.3, 0);
        harness.RunLayout();

        harness.Window.SetRenderScaling(2.0);
        harness.RunLayout();

        Assert.Equal(-100.3, ExactTranslate(harness.Editor).X, 3);
    }
}
