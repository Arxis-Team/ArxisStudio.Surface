using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ArxisStudio.Controls;
using Xunit;
using DesignLayout = ArxisStudio.Surface.UiDesigner.Layout;
using ArxisStudio.Surface.Editing;

namespace ArxisStudio.Tests;

/// <summary>
/// Согласование design-координат с фактической раскладкой и единицы слоя адорнеров.
/// </summary>
/// <remarks>
/// Оба утверждения выглядели дефектами в аудите и после разбора оказались верным
/// поведением. Тесты остались, чтобы это не пришлось выяснять заново.
/// </remarks>
public class ReconciliationCharacterizationTests
{
    private static readonly Point CardLocation = new(100, 100);
    private static readonly Size CardSize = new(340, 400);

    private static readonly Point ContainerLocation = new(100, 100);
    private static readonly Size ContainerSize = new(300, 200);

    [AvaloniaFact]
    public void Design_Position_Is_Overwritten_When_The_Parent_Owns_It()
    {
        var harness = EditorHarness.CreateStackHosted();
        harness.PlaceContainer(0, CardLocation, CardSize);
        var action = harness.Find<Button>(0, "Action");

        // Даём DesignY устояться: пересчёт всегда идёт через dispatcher,
        // поэтому сразу после layout там ещё значение по умолчанию.
        Dispatcher.UIThread.RunJobs();
        harness.RunLayout();
        var arranged = DesignLayout.GetDesignY(action);
        Assert.True(arranged > 0, "координата не успела посчитаться");

        DesignLayout.SetDesignY(action, arranged + 120);
        harness.RunLayout();
        Dispatcher.UIThread.RunJobs();
        harness.RunLayout();

        // Так и должно быть: раз позицией распоряжается панель, design-координата
        // обязана показывать её настоящее положение, а не то, что кто-то попросил.
        // Редактор сюда больше не пишет — стратегия размещения отсекает попытку
        // на шве, поэтому «драки» за координату не возникает.
        Assert.Equal(arranged, DesignLayout.GetDesignY(action), 1);
    }

    [AvaloniaFact]
    public void Adorner_Layer_Extent_Scales_With_Zoom_By_Design()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);

        var nested = harness.Nested(0);
        var sibling = harness.Named(0, "Sibling");

        Select(harness, nested);
        Select(harness, sibling, additive: true);
        Assert.Equal(2, harness.Editor.SelectedDesignTargetsCount);

        var layer = harness.Editor.GetVisualDescendants().OfType<SelectionAdornerLayer>().Single();

        harness.Editor.ViewportZoom = 1.0;
        harness.RunLayout();
        var atOne = layer.DesiredSize;

        harness.Editor.ViewportZoom = 2.0;
        harness.RunLayout();
        var atTwo = layer.DesiredSize;

        // Выглядит как смешение единиц: позиции мировые, размеры умножены на зум.
        // Это и есть конвенция слоя. Каждому ребёнку вешается обратный масштаб
        // 1/zoom, чтобы ручки не росли вместе с приближением, а родительский Canvas
        // масштабирует всё целиком: один зум гасится, второй даёт итог. Убрать
        // умножение — ручки поедут. Экстент ни на что не влияет: слой не обрезает.
        Assert.NotEqual(atOne.Width, atTwo.Width, 1);
    }

    private static void Select(EditorHarness harness, Control target, bool additive = false)
    {
        var modifiers = additive ? RawInputModifiers.Shift : RawInputModifiers.None;
        var centre = harness.CentreOf(target);
        harness.Window.MouseDown(centre, MouseButton.Left, modifiers);
        harness.Window.MouseUp(centre, MouseButton.Left, modifiers);
        harness.RunLayout();
    }
}
