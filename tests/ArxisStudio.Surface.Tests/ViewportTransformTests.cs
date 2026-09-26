using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface.Editing;

namespace ArxisStudio.Tests;

/// <summary>
/// Трансформации viewport и то, как их видят оверлеи.
/// </summary>
/// <remarks>
/// Оверлеи лежат внутри слоя с <c>ViewportTransform</c> и держат обратный масштаб,
/// иначе ручки выделения росли бы вместе с содержимым. Здесь закреплено, что
/// обратный масштаб следует за зумом, а точное и округлённое до пикселя смещения
/// остаются разными.
/// <para>
/// Тесты написаны до того, как механизм поменяли, и пережили замену: они проверяют
/// действующую матрицу, а не то, каким типом трансформации она получена.
/// </para>
/// </remarks>
public class ViewportTransformTests
{
    private static EditorHarness CreateSelected()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, new Point(100, 100), new Size(200, 150));
        harness.Editor.SelectDesignTarget(harness.Nested(0));
        harness.RunLayout();
        return harness;
    }

    private static SelectionAdorner PrimaryAdorner(EditorHarness harness) =>
        harness.Editor.GetVisualDescendants()
            .OfType<SelectionAdorner>()
            .First(adorner => adorner.Name == "PART_SelectionAdorner");

    [AvaloniaFact]
    public void The_Overlay_Inverse_Follows_The_Zoom()
    {
        var harness = CreateSelected();
        var adorner = PrimaryAdorner(harness);

        harness.Editor.ViewportZoom = 2.0;
        harness.RunLayout();

        // Обратный масштаб держит ручки одинакового размера на любом зуме.
        // Если он перестанет обновляться, адорнер вырастет вместе с содержимым,
        // и заметить это можно будет только глазами.
        // Проверяется действующая матрица, а не конкретный тип трансформации:
        // тип — это реализация, а обещание библиотеки в том, что оверлей
        // не масштабируется вместе с содержимым.
        var matrix = adorner.RenderTransform!.Value;
        Assert.Equal(0.5, matrix.M11, 3);
        Assert.Equal(0.5, matrix.M22, 3);
    }

    [AvaloniaFact]
    public void Panning_Does_Not_Disturb_The_Overlay_Scale()
    {
        var harness = CreateSelected();
        var adorner = PrimaryAdorner(harness);

        harness.Editor.ViewportZoom = 2.0;
        harness.RunLayout();
        harness.Editor.ViewportLocation = new Point(37, 53);
        harness.RunLayout();

        Assert.Equal(0.5, adorner.RenderTransform!.Value.M11, 3);
    }

    [AvaloniaFact]
    public void The_Scaled_And_Dpi_Transforms_Stay_Separate()
    {
        var harness = CreateSelected();

        harness.Editor.ViewportZoom = 1.0;
        harness.Editor.ViewportLocation = new Point(10.4, 20.6);
        harness.RunLayout();

        // Сетка и фон получают округлённое до пикселя смещение, содержимое — точное.
        // Один без другого менять нельзя, поэтому оба закреплены.
        var content = Assert.IsType<TransformGroup>(harness.Editor.ViewportTransform);
        var dpi = Assert.IsType<TransformGroup>(harness.Editor.DpiScaledViewportTransform);

        Assert.Equal(-10.4, content.Value.M31, 3);
        Assert.Equal(Math.Round(-10.4), dpi.Value.M31, 3);
    }
}
