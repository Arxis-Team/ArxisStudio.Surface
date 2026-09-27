using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;
using ArxisStudio.Surface;
using ArxisStudio.Surface.Editing;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Tests;

/// <summary>
/// Политики перемещения и изменения размера target.
/// </summary>
/// <remarks>
/// Логика чистая и не зависит от структуры выделения, поэтому переживёт
/// переход к рекурсивной вложенности без изменений.
/// </remarks>
public class InteractionPolicyTests
{
    private static (UiDesignerView Editor, Control Target) Create(
        MovePolicy move = MovePolicy.Both,
        ResizePolicy resize = ResizePolicy.All)
    {
        var editor = new UiDesignerView();
        var target = new Border();
        SurfaceInteraction.SetMovePolicy(target, move);
        SurfaceInteraction.SetResizePolicy(target, resize);
        return (editor, target);
    }

    [AvaloniaTheory]
    [InlineData(MovePolicy.Both, 10, 20)]
    [InlineData(MovePolicy.X, 10, 0)]
    [InlineData(MovePolicy.Y, 0, 20)]
    [InlineData(MovePolicy.None, 0, 0)]
    public void Move_Policy_Filters_Delta_Components(MovePolicy policy, double expectedX, double expectedY)
    {
        var (editor, target) = Create(move: policy);

        var filtered = editor.ApplyMovePolicy(target, new Vector(10, 20));

        Assert.Equal(expectedX, filtered.X);
        Assert.Equal(expectedY, filtered.Y);
    }

    [AvaloniaTheory]
    [InlineData(ResizeDirection.Left, true)]
    [InlineData(ResizeDirection.Right, true)]
    [InlineData(ResizeDirection.Top, false)]
    [InlineData(ResizeDirection.Bottom, false)]
    public void Horizontal_Resize_Policy_Allows_Only_Side_Handles(ResizeDirection direction, bool expected)
    {
        var (editor, target) = Create(resize: ResizePolicy.Horizontal);

        Assert.Equal(expected, editor.IsResizeAllowed(target, direction));
    }

    [AvaloniaTheory]
    [InlineData(ResizeDirection.TopLeft)]
    [InlineData(ResizeDirection.TopRight)]
    [InlineData(ResizeDirection.BottomLeft)]
    [InlineData(ResizeDirection.BottomRight)]
    public void Corner_Handles_Require_Both_Axes(ResizeDirection direction)
    {
        var (horizontalOnly, target) = Create(resize: ResizePolicy.Horizontal);
        Assert.False(horizontalOnly.IsResizeAllowed(target, direction));

        var (all, allTarget) = Create(resize: ResizePolicy.All);
        Assert.True(all.IsResizeAllowed(allTarget, direction));
    }

    [AvaloniaFact]
    public void Resize_Policy_None_Blocks_Every_Direction()
    {
        var (editor, target) = Create(resize: ResizePolicy.None);

        foreach (ResizeDirection direction in Enum.GetValues<ResizeDirection>())
            Assert.False(editor.IsResizeAllowed(target, direction));
    }

    [AvaloniaFact]
    public void Defaults_Are_Fully_Permissive()
    {
        var editor = new UiDesignerView();
        var target = new Border();

        Assert.Equal(MovePolicy.Both, SurfaceInteraction.GetMovePolicy(target));
        Assert.Equal(MovePolicy.Both, editor.GetEffectiveMovePolicy(target));
        Assert.Equal(ResizePolicy.All, editor.GetResizePolicy(target));
    }
}
