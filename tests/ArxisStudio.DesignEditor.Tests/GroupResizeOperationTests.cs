using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;
using ArxisStudio.Surface;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Tests;

/// <summary>
/// Пропорциональное изменение размера группы контейнеров.
/// </summary>
/// <remarks>
/// Контролы намеренно не помещаются в дерево: <c>SetDesignPosition</c> тогда пишет
/// прямо в <c>Layout.DesignX/DesignY</c>, а <c>GetDesignPosition</c> читает их обратно.
/// Это делает тест проверкой чистой арифметики операции.
/// </remarks>
public class GroupResizeOperationTests
{
    private const double MinSize = 10;

    private static Control Target(double width, double height)
        => new Border { Width = width, Height = height };

    private static Rect BoundsOf(DesignEditor editor, Control control)
        => new(editor.GetDesignPosition(control), editor.GetDesignSize(control));

    [AvaloniaFact]
    public void Targets_Scale_Proportionally()
    {
        var editor = new DesignEditor();
        var left = Target(100, 20);
        var right = Target(100, 20);

        var operation = new GroupResizeOperation(
            ResizeDirection.Right,
            new Rect(0, 0, 200, 20),
            new[]
            {
                new GroupResizeTarget(left, new Rect(0, 0, 100, 20)),
                new GroupResizeTarget(right, new Rect(100, 0, 100, 20))
            },
            MinSize);

        // Ширина группы 200 -> 100, масштаб 0.5.
        operation.Update(editor, new Vector(-100, 0));

        Assert.Equal(new Rect(0, 0, 50, 20), BoundsOf(editor, left));
        Assert.Equal(new Rect(50, 0, 50, 20), BoundsOf(editor, right));
    }

    [AvaloniaFact]
    public void Group_Scale_Is_Limited_By_The_Most_Constrained_Target()
    {
        var editor = new DesignEditor();
        var small = Target(20, 20);
        var large = Target(100, 20);

        var operation = new GroupResizeOperation(
            ResizeDirection.Right,
            new Rect(0, 0, 120, 20),
            new[]
            {
                new GroupResizeTarget(small, new Rect(0, 0, 20, 20)),
                new GroupResizeTarget(large, new Rect(20, 0, 100, 20))
            },
            MinSize);

        // Запрошенный масштаб 30/120 = 0.25 опустил бы small до 5 при минимуме 10.
        // Масштаб группы зажимается до 10/20 = 0.5.
        operation.Update(editor, new Vector(-90, 0));

        var smallBounds = BoundsOf(editor, small);
        var largeBounds = BoundsOf(editor, large);

        Assert.Equal(MinSize, smallBounds.Width, 6);
        Assert.Equal(50, largeBounds.Width, 6);

        // Главное: соседи не наезжают друг на друга. До исправления позиция
        // считалась от незажатого масштаба, и small (0..10) перекрывал large (5..30).
        Assert.True(
            smallBounds.Right <= largeBounds.X + 1e-6,
            $"target'ы перекрылись: {smallBounds} и {largeBounds}");
    }

    [AvaloniaFact]
    public void Vertical_Constraint_Does_Not_Affect_Horizontal_Scale()
    {
        var editor = new DesignEditor();
        // Высота уже на минимуме, ширина — нет.
        var target = Target(100, MinSize);

        var operation = new GroupResizeOperation(
            ResizeDirection.BottomRight,
            new Rect(0, 0, 100, MinSize),
            new[] { new GroupResizeTarget(target, new Rect(0, 0, 100, MinSize)) },
            MinSize);

        operation.Update(editor, new Vector(-50, -50));

        var bounds = BoundsOf(editor, target);

        Assert.Equal(50, bounds.Width, 6);
        Assert.Equal(MinSize, bounds.Height, 6);
    }
}
