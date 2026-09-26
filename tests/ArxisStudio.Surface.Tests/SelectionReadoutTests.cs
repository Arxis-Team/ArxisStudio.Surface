using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface;
using ArxisStudio.Surface.Editing;

namespace ArxisStudio.Tests;

/// <summary>
/// Диагностический вывод о размещении выбранного контрола.
/// </summary>
/// <remarks>
/// Отвечает на вопрос «почему этот контрол не двигается» без отладчика:
/// приложение может показать причину рядом с выделением.
/// </remarks>
public class SelectionReadoutTests
{
    private static readonly Point CardLocation = new(100, 100);

    private static void Select(EditorHarness harness, Control target)
    {
        var centre = harness.CentreOf(target);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseUp(centre, MouseButton.Left);
        harness.RunLayout();
    }

    [AvaloniaFact]
    public void Stack_Child_Reports_Its_Layout_And_A_Blocked_Move()
    {
        var harness = EditorHarness.CreateStackHosted();
        harness.PlaceContainer(0, CardLocation, new Size(340, 400));

        Select(harness, harness.Find<Button>(0, "Action"));

        Assert.Equal("Stack", harness.Editor.PrimarySelectionPlacement);
        Assert.Equal(MovePolicy.None, harness.Editor.PrimarySelectionMovePolicy);

        // Размер раскладка не сужает: явный Width/Height honours любая панель.
        Assert.Equal(ResizePolicy.All, harness.Editor.PrimarySelectionResizePolicy);
    }

    [AvaloniaFact]
    public void Absolute_Child_Reports_A_Free_Move()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, CardLocation, new Size(300, 200));

        Select(harness, harness.Nested(0));

        Assert.Equal("Absolute", harness.Editor.PrimarySelectionPlacement);
        Assert.Equal(MovePolicy.Both, harness.Editor.PrimarySelectionMovePolicy);
    }

    [AvaloniaFact]
    public void Move_Policy_Is_Effective_Not_Raw()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, CardLocation, new Size(300, 200));
        var nested = harness.Nested(0);
        DesignInteraction.SetMovePolicy(nested, MovePolicy.X);

        Select(harness, nested);

        Assert.Equal(MovePolicy.X, harness.Editor.PrimarySelectionMovePolicy);
    }

    [AvaloniaFact]
    public void Empty_Selection_Reports_Nothing()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, CardLocation, new Size(300, 200));

        Select(harness, harness.Nested(0));
        Assert.NotNull(harness.Editor.PrimarySelectionPlacement);

        harness.Editor.Selection.Clear();
        harness.RunLayout();

        Assert.Null(harness.Editor.PrimarySelectionPlacement);
        Assert.Equal(MovePolicy.None, harness.Editor.PrimarySelectionMovePolicy);
    }
}
