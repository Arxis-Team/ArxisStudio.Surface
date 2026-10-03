using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.VisualTree;
using ArxisStudio.Surface.UiDesigner.Placement;
using Xunit;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Surface.Editing;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Разрешение стратегии размещения и композиция политик.
/// </summary>
public class PlacementStrategyTests
{
    private static Control ChildOf(Control parent)
    {
        var child = new Border { Name = "Child" };

        if (parent is Panel panel)
            panel.Children.Add(child);
        else
            ((Border)parent).Child = child;

        return child;
    }

    // Семантика сверяется по имени: SurfaceMoveSemantics — internal,
    // а сигнатура публичного теста не может быть менее доступной.
    [AvaloniaTheory]
    [InlineData(typeof(AbsolutePanel), "Absolute", "Reposition")]
    [InlineData(typeof(Canvas), "Canvas", "Reposition")]
    [InlineData(typeof(StackPanel), "Stack", "Reorder")]
    [InlineData(typeof(WrapPanel), "Stack", "Reorder")]
    [InlineData(typeof(Grid), "Grid", "None")]
    [InlineData(typeof(DockPanel), "Dock", "None")]
    [InlineData(typeof(Border), "ContentHost", "None")]
    public void Strategy_Follows_The_Parent_Layout(System.Type parentType, string name, string semantics)
    {
        var parent = (Control)System.Activator.CreateInstance(parentType)!;
        var child = ChildOf(parent);

        var strategy = UiDesignerView.GetPlacementStrategy(child);

        Assert.Equal(name, strategy.Name);
        Assert.Equal(semantics, strategy.MoveSemantics.ToString());
    }

    [AvaloniaFact]
    public void Control_Without_A_Parent_Is_Positioned_Absolutely()
    {
        // Голый контрол вне дерева: на нём стоят GroupResizeOperationTests,
        // и семантика для него обязана совпадать с прежней.
        var strategy = UiDesignerView.GetPlacementStrategy(new Border());

        Assert.Equal("Absolute", strategy.Name);
        Assert.Equal("Reposition", strategy.MoveSemantics.ToString());
    }

    [AvaloniaFact]
    public void Layout_Cannot_Widen_A_User_Lock()
    {
        var editor = new UiDesignerView();
        var panel = new AbsolutePanel();
        var child = ChildOf(panel);
        SurfaceInteraction.SetMovePolicy(child, MovePolicy.None);

        // Раскладка позволяет двигать, пользователь запретил — запрет сильнее.
        Assert.Equal(MovePolicy.None, editor.GetEffectiveMovePolicy(child));
    }

    [AvaloniaFact]
    public void User_Policy_Cannot_Widen_The_Layout()
    {
        var editor = new UiDesignerView();
        var panel = new StackPanel();
        var child = ChildOf(panel);
        SurfaceInteraction.SetMovePolicy(child, MovePolicy.Both);

        // Пользователь разрешил, но StackPanel не читает Layout.X/Y:
        // разрешать жест значило бы обещать то, чего не произойдёт.
        Assert.Equal(MovePolicy.Both, SurfaceInteraction.GetMovePolicy(child));
        Assert.Equal(MovePolicy.None, editor.GetEffectiveMovePolicy(child));
    }

    [AvaloniaFact]
    public void Axis_Restriction_Survives_A_Permissive_Layout()
    {
        var editor = new UiDesignerView();
        var panel = new AbsolutePanel();
        var child = ChildOf(panel);
        SurfaceInteraction.SetMovePolicy(child, MovePolicy.X);

        Assert.Equal(MovePolicy.X, editor.GetEffectiveMovePolicy(child));
    }

    [AvaloniaFact]
    public void Canvas_Child_Moves_Through_Canvas_Left_And_Top()
    {
        var harness = EditorHarness.Create();
        var editor = harness.Editor;

        var canvas = new Canvas { Width = 200, Height = 200 };
        var child = new Border
        {
            Name = "CanvasChild",
            Width = 40,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        Canvas.SetLeft(child, 10);
        Canvas.SetTop(child, 20);
        canvas.Children.Add(child);

        var container = harness.PlaceContainer(0, new Point(100, 100), new Size(300, 300));
        container.GetVisualDescendants().OfType<AbsolutePanel>().First().Children.Add(canvas);
        harness.RunLayout();

        var before = editor.GetTargetPosition(child);
        editor.SetTargetPosition(child, before + new Vector(30, 15));
        harness.RunLayout();

        // Canvas игнорирует Layout.X/Y, поэтому стратегия пишет его собственные
        // присоединённые свойства. Раньше жест сюда просто не доходил.
        Assert.Equal(40, Canvas.GetLeft(child), 1);
        Assert.Equal(35, Canvas.GetTop(child), 1);
    }

    /// <summary>
    /// Позиция, присланная тягой, ставит ребёнка <see cref="Canvas"/> туда, куда прислана, сколько бы
    /// шагов ни пришло между проходами раскладки.
    /// </summary>
    /// <remarks>
    /// Тяга присылает положение на каждый шаг указателя, а шаги приходят чаще раскладки. Сдвиг, посчитанный
    /// от места, где ребёнка поставила прошлая раскладка, и прибавленный к уже записанному
    /// <c>Canvas.Left</c>, складывался: в студии двенадцать шагов тяги на 55 точек увели заметку на 342.
    /// </remarks>
    [AvaloniaFact]
    public void Canvas_Child_Lands_Where_A_Drag_Outpacing_Layout_Puts_It()
    {
        var harness = EditorHarness.Create();
        var editor = harness.Editor;

        var canvas = new Canvas { Width = 200, Height = 200 };
        var child = new Border { Name = "CanvasChild", Width = 40, Height = 40, Margin = new Thickness(3, 4, 0, 0) };
        Canvas.SetLeft(child, 10);
        Canvas.SetTop(child, 20);
        canvas.Children.Add(child);

        var container = harness.PlaceContainer(0, new Point(100, 100), new Size(300, 300));
        container.GetVisualDescendants().OfType<AbsolutePanel>().First().Children.Add(canvas);
        harness.RunLayout();

        var before = editor.GetTargetPosition(child);

        editor.SetTargetPosition(child, before + new Vector(10, 5));
        editor.SetTargetPosition(child, before + new Vector(20, 10));
        editor.SetTargetPosition(child, before + new Vector(30, 15));

        // До раскладки позиция — та, что прислана последней: жест, читающий её между шагами, видит своё.
        Assert.Equal(before + new Vector(30, 15), editor.GetTargetPosition(child));

        harness.RunLayout();

        Assert.Equal(40, Canvas.GetLeft(child), 1);
        Assert.Equal(35, Canvas.GetTop(child), 1);
        Assert.Equal(before + new Vector(30, 15), editor.GetTargetPosition(child));
    }
}
