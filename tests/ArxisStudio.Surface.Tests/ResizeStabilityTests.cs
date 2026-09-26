using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Xunit;
using ArxisStudio.Surface;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Surface.Editing;

namespace ArxisStudio.Tests;

/// <summary>
/// Устойчивость изменения размера к расхождению запрошенной и применённой геометрии.
/// </summary>
/// <remarks>
/// <c>Thumb.DragDelta</c> отдаёт смещение относительно самой ручки (см.
/// <c>ThumbDeltaProbeTests</c>). Ручка стоит на краю выделения, то есть на
/// <b>применённой</b> геометрии. Поэтому непогашенный остаток — всё, что съели
/// привязка к сетке, <c>Max</c> или границы формы — приходит в следующей дельте
/// повторно. Накопление таких дельт от исходного размера складывает остаток
/// снова и снова, и рамка вместе с контролом начинает прыгать.
/// </remarks>
public class ResizeStabilityTests
{
    private static readonly Point ContainerLocation = new(100, 100);
    private static readonly Size ContainerSize = new(200, 150);

    private const double SnapStep = 20;
    private const double PointerStep = 7;
    private const int StepCount = 10;

    /// <summary>
    /// Прогоняет протяжку так, как её видит редактор: ручка стоит на применённом
    /// крае, поэтому дельта считается от него, а не от точки нажатия.
    /// </summary>
    private static List<double> DragRightEdge(EditorHarness harness, DesignEditorItem container, Control target)
    {
        var state = new ItemResizingState(container, target, ResizeDirection.Right);
        container.PushState(state);

        var widths = new List<double>();
        var pointerRight = Right(harness, target);
        var appliedRight = pointerRight;

        for (var i = 0; i < StepCount; i++)
        {
            pointerRight += PointerStep;

            state.OnResizeDelta(new ResizeDeltaEventArgs(
                new Vector(pointerRight - appliedRight, 0),
                ResizeDirection.Right,
                DesignEditorItem.ResizeDeltaEvent));

            harness.RunLayout();

            appliedRight = Right(harness, target);
            widths.Add(harness.Editor.GetDesignSize(target).Width);
        }

        return widths;
    }

    private static double Right(EditorHarness harness, Control target)
        => harness.Editor.GetDesignPosition(target).X + harness.Editor.GetDesignSize(target).Width;

    [AvaloniaFact]
    public void Width_Never_Goes_Backwards_While_The_Pointer_Moves_Forward()
    {
        var harness = EditorHarness.Create(snapToGrid: true);
        harness.Editor.InteractionOptions.SnapStep = SnapStep;
        var container = harness.PlaceContainer(0, ContainerLocation, ContainerSize);

        var widths = DragRightEdge(harness, container, harness.Nested(0));

        for (var i = 1; i < widths.Count; i++)
        {
            Assert.True(
                widths[i] >= widths[i - 1] - 0.01,
                $"ширина откатилась на шаге {i}: {string.Join(", ", widths)}");
        }
    }

    [AvaloniaFact]
    public void Width_Follows_The_Pointer_Within_One_Snap_Step()
    {
        var harness = EditorHarness.Create(snapToGrid: true);
        harness.Editor.InteractionOptions.SnapStep = SnapStep;
        var container = harness.PlaceContainer(0, ContainerLocation, ContainerSize);
        var target = harness.Nested(0);

        var expected = harness.Editor.GetDesignSize(target).Width + (PointerStep * StepCount);
        var widths = DragRightEdge(harness, container, target);

        // Указатель ушёл на 70; итог обязан отличаться не больше чем на шаг сетки.
        // При накоплении дельт остаток складывался повторно и ширина улетала кратно.
        Assert.InRange(widths[^1], expected - SnapStep, expected + SnapStep);
    }

    [AvaloniaFact]
    public void Group_Width_Never_Goes_Backwards()
    {
        var harness = EditorHarness.Create(snapToGrid: true);
        harness.Editor.InteractionOptions.SnapStep = SnapStep;
        harness.PlaceContainer(0, ContainerLocation, new Size(400, 300));

        var nested = harness.Nested(0);
        var sibling = harness.Named(0, "Sibling");

        var nestedBounds = new Rect(110, 110, EditorHarness.NestedWidth, EditorHarness.NestedHeight);
        var siblingBounds = new Rect(200, 110, EditorHarness.NestedWidth, EditorHarness.NestedHeight);
        var groupBounds = nestedBounds.Union(siblingBounds);

        var operation = new GroupResizeOperation(
            ResizeDirection.Right,
            groupBounds,
            new[]
            {
                new GroupResizeTarget(nested, nestedBounds),
                new GroupResizeTarget(sibling, siblingBounds)
            },
            minSize: 10);

        var widths = new List<double>();
        var pointerRight = groupBounds.Right;
        var appliedRight = pointerRight;

        for (var i = 0; i < StepCount; i++)
        {
            pointerRight += PointerStep;
            operation.Update(harness.Editor, new Vector(pointerRight - appliedRight, 0));
            harness.RunLayout();

            appliedRight = Right(harness, sibling);
            widths.Add(appliedRight);
        }

        // Групповая рамка накапливала дельты так же, как одиночная,
        // поэтому прыгала вместе со всеми target'ами.
        for (var i = 1; i < widths.Count; i++)
        {
            Assert.True(
                widths[i] >= widths[i - 1] - 0.01,
                $"правый край группы откатился на шаге {i}: {string.Join(", ", widths)}");
        }
    }

    [AvaloniaFact]
    public void Contained_Resize_Does_Not_Oscillate_At_The_Form_Edge()
    {
        var harness = EditorHarness.Create(snapToGrid: false);
        var container = harness.PlaceContainer(0, ContainerLocation, ContainerSize);
        var target = harness.Nested(0);

        var state = new ItemResizingState(container, target, ResizeDirection.Right);
        container.PushState(state);

        // Указатель уезжает далеко за форму: применённый край упирается,
        // и весь остаток приходит обратно в дельте на каждом шаге.
        var pointerRight = Right(harness, target);
        var appliedRight = pointerRight;
        var widths = new List<double>();

        for (var i = 0; i < 6; i++)
        {
            pointerRight += 100;

            state.OnResizeDelta(new ResizeDeltaEventArgs(
                new Vector(pointerRight - appliedRight, 0),
                ResizeDirection.Right,
                DesignEditorItem.ResizeDeltaEvent));

            harness.RunLayout();
            appliedRight = Right(harness, target);
            widths.Add(harness.Editor.GetDesignSize(target).Width);
        }

        var limit = ContainerLocation.X + ContainerSize.Width;

        // Упёршись в край формы, размер обязан замереть, а не пульсировать.
        Assert.Equal(widths[^2], widths[^1], 1);
        Assert.InRange(appliedRight, limit - 0.01, limit + 0.01);
    }

    /// <summary>
    /// Дельта считается от указателя, а не от того, успела ли переехать ручка.
    /// </summary>
    /// <remarks>
    /// <c>Thumb.DragDelta</c> отдаёт смещение относительно самой ручки, и приращённым оно
    /// бывает лишь пока ручка успевает за указателем. Без layout-прохода между двумя
    /// движениями ручка стоит, и каждая дельта несёт всё расстояние заново — 10, 20, 30,
    /// левая колонка <c>ThumbDeltaProbeTests</c>. Складывая их, редактор растил ширину
    /// квадратично: замер на живом демо давал +116 на протяжку в 20 пикселей.
    /// <para>
    /// Здесь ручка не двигается вовсе, а указатель уезжает на 10, 20, 30. Ширина обязана
    /// следовать за указателем.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void Resize_Follows_The_Pointer_When_The_Handle_Lags()
    {
        var harness = EditorHarness.Create();
        var container = harness.PlaceContainer(0, ContainerLocation, ContainerSize);
        var target = harness.Nested(0);

        var start = Right(harness, target);
        var y = harness.Editor.GetDesignPosition(target).Y + 5;
        var width0 = harness.Editor.GetDesignSize(target).Width;

        harness.Window.MouseMove(new Point(start, y));
        var state = new ItemResizingState(container, target, ResizeDirection.Right);
        container.PushState(state);

        for (var i = 1; i <= 3; i++)
        {
            harness.Window.MouseMove(new Point(start + (i * 10), y));

            // Ручка не переехала: Thumb отдаёт накопленную дельту.
            state.OnResizeDelta(new ResizeDeltaEventArgs(
                new Vector(i * 10, 0),
                ResizeDirection.Right,
                DesignEditorItem.ResizeDeltaEvent));

            harness.RunLayout();
        }

        Assert.Equal(width0 + 30, harness.Editor.GetDesignSize(target).Width, 1);
    }

    /// <summary>
    /// Жест без своего указателя считает по дельте, даже если мышь до этого возили.
    /// </summary>
    /// <remarks>
    /// Снимок указателя переживает жест, поэтому «указатель известен» и «указатель
    /// двигался внутри этого жеста» — разные вещи. Первая версия правки перепутала их,
    /// и жест, поднятый хостом через публичные <c>ResizeStarted</c>/<c>ResizeDelta</c>,
    /// молча переставал что-либо менять: поправка выходила нулевой, потому что
    /// сравнивалась с положением, где мышь побывала до жеста.
    /// </remarks>
    [AvaloniaFact]
    public void A_Gesture_Without_Its_Own_Pointer_Falls_Back_To_The_Delta()
    {
        var harness = EditorHarness.Create();
        var container = harness.PlaceContainer(0, ContainerLocation, ContainerSize);
        var target = harness.Nested(0);
        var width0 = harness.Editor.GetDesignSize(target).Width;

        // Мышь побывала где-то в редакторе и больше не двигается.
        harness.Window.MouseMove(new Point(500, 400));

        var state = new ItemResizingState(container, target, ResizeDirection.Right);
        container.PushState(state);

        for (var i = 0; i < 3; i++)
        {
            state.OnResizeDelta(new ResizeDeltaEventArgs(
                new Vector(10, 0),
                ResizeDirection.Right,
                DesignEditorItem.ResizeDeltaEvent));

            harness.RunLayout();
        }

        Assert.Equal(width0 + 30, harness.Editor.GetDesignSize(target).Width, 1);
    }

    /// <summary>
    /// Правило допуска указателя к жесту.
    /// </summary>
    /// <remarks>
    /// Проверяется предикат напрямую: в headless-стенде указатель один, и разыграть
    /// перо рядом с мышью нечем — а именно от этого случая половина правила и стоит.
    /// Оба условия нужны, и каждое закрывает ошибку, которая уже была сделана:
    /// чужое устройство уводило бы край к себе, а снимок, переживший жест, выдавал бы
    /// нулевую поправку вместо дельты.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(1, 6, 1, 5, true)]    // то же устройство, движение после начала
    [InlineData(2, 6, 1, 5, false)]   // чужое устройство
    [InlineData(1, 5, 1, 5, false)]   // движения после начала не было
    [InlineData(1, 4, 1, 5, false)]   // снимок старше начала жеста
    public void The_Gesture_Only_Takes_Its_Own_Pointer(
        int currentId, int currentCount, int grabId, int grabCount, bool expected)
    {
        var current = new PointerSample(currentId, new Point(100, 50), currentCount);
        var grab = new PointerSample(grabId, new Point(10, 10), grabCount);

        Assert.Equal(expected, DesignEditor.TryGetGesturePointer(current, grab, out var world));
        if (expected)
            Assert.Equal(new Point(100, 50), world);
    }

    [AvaloniaFact]
    public void Without_A_Grab_Sample_The_Pointer_Is_Not_Used()
    {
        var current = new PointerSample(1, new Point(100, 50), 7);

        Assert.False(DesignEditor.TryGetGesturePointer(current, null, out _));
        Assert.False(DesignEditor.TryGetGesturePointer(null, current, out _));
    }
}
