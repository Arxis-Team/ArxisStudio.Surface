using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Набор группы контейнеров кликами.
/// </summary>
/// <remarks>
/// Фиксирует сегодняшнее поведение: <c>Ctrl</c> переводит клик на уровень
/// контейнера, но добавление вторым <c>Ctrl+Shift</c>-кликом на этот уровень
/// не попадает — выбирается вложенный контрол. Рабочий путь набрать группу
/// контейнеров сейчас один: <c>Ctrl+A</c> либо рамка с <c>Ctrl</c>.
/// </remarks>
public class ContainerSelectionGestureTests
{
    private static readonly Size ContainerSize = new(200, 150);

    private static EditorHarness Create()
    {
        var harness = EditorHarness.Create(nodeCount: 2);
        harness.PlaceContainer(0, new Point(100, 100), ContainerSize);
        harness.PlaceContainer(1, new Point(400, 100), ContainerSize);
        return harness;
    }

    private static void Click(EditorHarness harness, int index, RawInputModifiers modifiers)
    {
        var centre = harness.CentreOf(harness.Container(index));
        harness.Window.MouseDown(centre, MouseButton.Left, modifiers);
        harness.Window.MouseUp(centre, MouseButton.Left, modifiers);
        harness.RunLayout();
    }

    [AvaloniaFact]
    public void Control_Click_Selects_The_Container_Itself()
    {
        var harness = Create();

        Click(harness, 0, RawInputModifiers.Control);

        Assert.Equal(SurfaceSelectionScope.Container, harness.Editor.PrimarySelectionTarget!.Scope);
    }

    [AvaloniaFact]
    public void Additive_Control_Click_Adds_The_Container()
    {
        var harness = Create();

        Click(harness, 0, RawInputModifiers.Control);
        Click(harness, 1, RawInputModifiers.Control | RawInputModifiers.Shift);

        Assert.Equal(2, harness.Editor.SelectedTargetsCount);
        Assert.True(harness.Editor.HasMultipleContainerSelection);
        Assert.All(harness.Editor.SelectedTargets,
            t => Assert.Equal(SurfaceSelectionScope.Container, t.Scope));
    }

    [AvaloniaFact]
    public void Repeated_Additive_Click_Removes_The_Container()
    {
        var harness = Create();

        Click(harness, 0, RawInputModifiers.Control);
        Click(harness, 1, RawInputModifiers.Control | RawInputModifiers.Shift);
        Click(harness, 1, RawInputModifiers.Control | RawInputModifiers.Shift);

        // Повторный additive-клик снимает контейнер, но не опустошает выделение —
        // то же правило, что и для вложенных target'ов.
        Assert.Equal(1, harness.Editor.SelectedTargetsCount);
        Assert.Same(harness.Container(0), harness.Editor.PrimarySelectionTarget!.Target);
    }

    [AvaloniaFact]
    public void Additive_Click_Keeps_The_Item_Selection_In_Sync()
    {
        var harness = Create();

        Click(harness, 0, RawInputModifiers.Control);
        Click(harness, 1, RawInputModifiers.Control | RawInputModifiers.Shift);

        // Индексная модель Avalonia обязана согласоваться со слоем target'ов:
        // иначе контейнер без своего target'а подменяется вложенным по умолчанию.
        Assert.Equal(2, harness.Editor.Selection.Count);
    }

    [AvaloniaFact]
    public void Select_All_Gives_A_Container_Group()
    {
        var harness = Create();

        harness.Editor.Focus();
        harness.Window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        harness.RunLayout();

        Assert.True(harness.Editor.HasMultipleContainerSelection);
    }
}
