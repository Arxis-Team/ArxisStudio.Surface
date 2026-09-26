using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ArxisStudio.Controls;
using ArxisStudio.States;
using Xunit;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Приведение размера к ограничениям контрола на шве записи.
/// </summary>
/// <remarks>
/// Смысл в том, чтобы запрошенный и фактический размеры не расходились:
/// раскладка применит <c>Min</c>/<c>Max</c> в любом случае, и если редактор
/// запишет мимо них, дальше он будет считать от величины, которой нет на экране.
/// </remarks>
public class SizeCoercionTests
{
    private static readonly Point CardLocation = new(100, 100);
    private static readonly Size CardSize = new(340, 400);

    [AvaloniaFact]
    public void Max_Clamps_The_Recorded_Size()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, CardLocation, new Size(300, 300));
        var nested = harness.Nested(0);
        nested.MaxWidth = 80;

        harness.Editor.SetDesignSize(nested, new Size(500, 40));

        Assert.Equal(80, nested.Width, 1);
    }

    [AvaloniaFact]
    public void Min_Wins_Over_A_Smaller_Max()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, CardLocation, new Size(300, 300));
        var nested = harness.Nested(0);
        nested.MinWidth = 120;
        nested.MaxWidth = 40;

        harness.Editor.SetDesignSize(nested, new Size(500, 40));

        // Тот же порядок, что в самой Avalonia: сначала максимум, потом минимум.
        Assert.Equal(120, nested.Width, 1);
    }

    /// <summary>
    /// Минимум редактора — предел жеста, а не свойство шва записи.
    /// </summary>
    /// <remarks>
    /// Раньше <c>ResizeMinSize</c> участвовал прямо в <c>CoerceDesignSize</c>, то есть
    /// применялся к <b>любой</b> записи размера. Отсюда шёл дефект: вход в жест сам
    /// записывает текущий размер, чтобы было от чего считать, — и контрол мельче порога
    /// раздувался от простого нажатия на ручку, без единой дельты. Тем же путём
    /// раздувала бы и отмена: <c>ApplyGeometry</c> идёт через тот же шов.
    /// </remarks>
    [AvaloniaFact]
    public void The_Editor_Minimum_Does_Not_Reach_The_Write_Seam()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, CardLocation, new Size(300, 300));
        var nested = harness.Nested(0);

        harness.Editor.SetDesignSize(nested, new Size(1, 1));

        Assert.Equal(1, nested.Width, 1);
        Assert.Equal(1, nested.Height, 1);
    }

    /// <summary>
    /// Нажатие на ручку не меняет размер.
    /// </summary>
    /// <remarks>
    /// Жалоба выглядела так: <c>TextBlock</c> 50×16 внутри <c>Border</c>, клик по ручке
    /// без всякой протяжки — и в читалке 49×20. Вход в жест записывает текущий размер,
    /// чтобы было от чего считать дельту, а шов записи накладывал на него минимум
    /// редактора и раздувал контрол, который и так был мельче.
    /// <para>
    /// Стенд повторяет соотношение: минимум редактора выставлен выше высоты цели.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void Pressing_A_Handle_Leaves_The_Size_Alone()
    {
        var harness = EditorHarness.Create();
        var container = harness.PlaceContainer(0, CardLocation, new Size(300, 300));
        var nested = harness.Nested(0);

        // Цель ниже минимума по высоте: 40 против 60.
        harness.Editor.InteractionOptions.ResizeMinSize = 60;
        var before = harness.Editor.GetDesignSize(nested);

        container.PushState(new ItemResizingState(container, nested, ResizeDirection.Bottom));
        harness.RunLayout();

        Assert.Equal(before.Width, harness.Editor.GetDesignSize(nested).Width, 1);
        Assert.Equal(before.Height, harness.Editor.GetDesignSize(nested).Height, 1);
    }

    /// <summary>
    /// Минимум редактора не раздувает контрол, который и так меньше.
    /// </summary>
    /// <remarks>
    /// Предел жеста существует, чтобы контрол нельзя было схлопнуть в точку и потерять.
    /// Про контрол, который автор сделал мельче, он ничего не знает и увеличивать его
    /// не должен — иначе первая же протяжка за нижний край подбрасывала бы высоту вверх.
    /// </remarks>
    [AvaloniaFact]
    public void The_Editor_Minimum_Never_Enlarges_A_Smaller_Target()
    {
        var harness = EditorHarness.Create();
        var container = harness.PlaceContainer(0, CardLocation, new Size(300, 300));
        var nested = harness.Nested(0);

        harness.Editor.InteractionOptions.ResizeMinSize = 60;
        var before = harness.Editor.GetDesignSize(nested);

        var state = new ItemResizingState(container, nested, ResizeDirection.Bottom);
        container.PushState(state);
        state.OnResizeDelta(new ResizeDeltaEventArgs(
            new Vector(0, -500), ResizeDirection.Bottom, DesignEditorItem.ResizeDeltaEvent));

        harness.RunLayout();

        Assert.Equal(before.Height, harness.Editor.GetDesignSize(nested).Height, 1);
    }

    /// <summary>
    /// Жест не даёт ужать контрол ниже минимума редактора.
    /// </summary>
    [AvaloniaFact]
    public void The_Gesture_Refuses_To_Shrink_Below_The_Editor_Minimum()
    {
        var harness = EditorHarness.Create();
        var container = harness.PlaceContainer(0, CardLocation, new Size(300, 300));
        var nested = harness.Nested(0);
        var minimum = harness.Editor.InteractionOptions.ResizeMinSize;

        var state = new ItemResizingState(container, nested, ResizeDirection.Right);
        container.PushState(state);
        state.OnResizeDelta(new ResizeDeltaEventArgs(
            new Vector(-500, 0), ResizeDirection.Right, DesignEditorItem.ResizeDeltaEvent));

        harness.RunLayout();

        Assert.Equal(minimum, harness.Editor.GetDesignSize(nested).Width, 1);
    }

    [AvaloniaFact]
    public void Clamped_Resize_Keeps_The_Opposite_Edge_Fixed()
    {
        var harness = EditorHarness.CreateStackHosted();
        var container = harness.PlaceContainer(0, CardLocation, CardSize);
        var action = harness.Find<Button>(0, "Action");
        action.MaxWidth = 120;
        harness.RunLayout();

        var centre = harness.CentreOf(action);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseUp(centre, MouseButton.Left);
        harness.RunLayout();

        var rightBefore = harness.Editor.SelectionBounds.Right;

        var state = new ItemResizingState(container, action, ResizeDirection.Left);
        container.PushState(state);
        state.OnResizeDelta(new ResizeDeltaEventArgs(
            new Vector(-300, 0), ResizeDirection.Left, DesignEditorItem.ResizeDeltaEvent));
        harness.RunLayout();

        // Ширина упёрлась в предел, но правый край обязан остаться на месте:
        // он считается из итогового размера, а не из запрошенного.
        Assert.Equal(120, action.Width, 1);
        Assert.Equal(rightBefore, harness.Editor.SelectionBounds.Right, 1);
    }

    [AvaloniaFact]
    public void Group_Scale_Is_Limited_By_The_Most_Capped_Target()
    {
        var editor = new DesignEditor();
        var capped = new Border { Width = 100, Height = 20, MaxWidth = 150 };
        var free = new Border { Width = 100, Height = 20 };

        var operation = new GroupResizeOperation(
            ResizeDirection.Right,
            new Rect(0, 0, 200, 20),
            new[]
            {
                new GroupResizeTarget(capped, new Rect(0, 0, 100, 20)),
                new GroupResizeTarget(free, new Rect(100, 0, 100, 20))
            },
            minSize: 10);

        // Запрошенный масштаб 2.0 поднял бы capped до 200 при пределе 150.
        operation.Update(editor, new Vector(200, 0));

        Assert.Equal(150, editor.GetDesignSize(capped).Width, 6);

        // Главное: сосед не уезжает от рамки — позиция считается от того же
        // зажатого масштаба, что и размер.
        Assert.Equal(150, editor.GetDesignPosition(free).X, 6);
    }
}
