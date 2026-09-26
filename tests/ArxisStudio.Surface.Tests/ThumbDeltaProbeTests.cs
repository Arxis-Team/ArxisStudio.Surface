using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Семантика <see cref="Thumb.DragDelta"/> в Avalonia 12.
/// </summary>
/// <remarks>
/// От неё зависит вся арифметика resize: состояние накапливает дельты и считает
/// от исходного размера, что верно только если дельты приращённые.
/// </remarks>
public class ThumbDeltaProbeTests
{
    private sealed record Probe(List<Vector> Deltas);

    private static Probe Measure(bool thumbFollowsPointer)
    {
        var deltas = new List<Vector>();

        var thumb = new Thumb
        {
            // Без фона контрол не хиттестится и нажатие до него не доходит.
            Background = Avalonia.Media.Brushes.Red,
            Template = new Avalonia.Controls.Templates.FuncControlTemplate<Thumb>((_, _) =>
                new Border { Background = Avalonia.Media.Brushes.Red }),
            Width = 20,
            Height = 20,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };

        var canvas = new Canvas
        {
            Width = 400,
            Height = 400,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        canvas.Children.Add(thumb);
        Canvas.SetLeft(thumb, 100);
        Canvas.SetTop(thumb, 100);

        var window = new Window { Width = 500, Height = 500, Content = canvas };
        window.Show();
        RunLayout(window);

        thumb.DragDelta += (_, e) =>
        {
            deltas.Add(e.Vector);

            // Боевой случай: ручка едет вслед за указателем, потому что адорнер
            // перестраивается по новым границам выделения.
            if (thumbFollowsPointer)
            {
                Canvas.SetLeft(thumb, Canvas.GetLeft(thumb) + e.Vector.X);
                RunLayout(window);
            }
        };

        var start = new Point(110, 110);
        window.MouseDown(start, MouseButton.Left);

        for (var i = 1; i <= 3; i++)
            window.MouseMove(start + new Vector(10 * i, 0));

        window.MouseUp(start + new Vector(30, 0), MouseButton.Left);

        return new Probe(deltas);
    }

    private static void RunLayout(Window window)
    {
        var manager = window.GetLayoutManager();
        manager?.ExecuteInitialLayoutPass();
        manager?.ExecuteLayoutPass();
    }

    [AvaloniaFact]
    public void Delta_Is_Measured_From_The_Thumb_Itself()
    {
        // Ручка стоит на месте — смещение отсчитывается от точки нажатия
        // и растёт: 10, 20, 30.
        var still = Measure(thumbFollowsPointer: false);

        Assert.Equal(3, still.Deltas.Count);
        Assert.Equal(10, still.Deltas[0].X, 1);
        Assert.Equal(20, still.Deltas[1].X, 1);
        Assert.Equal(30, still.Deltas[2].X, 1);
    }

    [AvaloniaFact]
    public void Delta_Becomes_Incremental_When_The_Thumb_Keeps_Up()
    {
        // Ручка едет вслед за указателем — её система координат сдвигается,
        // и каждое смещение приходит уже приращённым.
        var moving = Measure(thumbFollowsPointer: true);

        Assert.Equal(3, moving.Deltas.Count);
        Assert.All(moving.Deltas, d => Assert.Equal(10, d.X, 1));
    }

    [AvaloniaFact]
    public void Unapplied_Remainder_Comes_Back_In_The_Next_Delta()
    {
        // Главное следствие для resize: если ручка отстала — а она отстаёт,
        // когда привязка, Max или границы формы съели часть запрошенного, —
        // непогашенный остаток приходит повторно. Накапливать такие дельты
        // нельзя: остаток сложится сам с собой.
        var still = Measure(thumbFollowsPointer: false);

        var accumulated = 0.0;
        foreach (var delta in still.Deltas)
            accumulated += delta.X;

        // Указатель ушёл на 30, а сумма дельт — 60.
        Assert.Equal(60, accumulated, 1);
    }
}
