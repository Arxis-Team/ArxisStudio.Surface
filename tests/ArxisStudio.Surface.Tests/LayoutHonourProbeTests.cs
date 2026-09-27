using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Xunit;
using SurfaceLayout = ArxisStudio.Surface.UiDesigner.Layout;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Tests;

/// <summary>
/// Таблица возможностей раскладок: какие панели honours запрошенный размер
/// и локальные координаты <c>Layout.X</c>/<c>Layout.Y</c>.
/// </summary>
/// <remarks>
/// Снято с реальной Avalonia 12, а не по памяти. Редактор выводит из этой таблицы,
/// какие жесты имеют смысл, поэтому она — вход для стратегий размещения,
/// а не иллюстрация к ним. Итог короткий: размер honours все, позицию — только
/// <see cref="AbsolutePanel"/>.
/// </remarks>
public class LayoutHonourProbeTests
{
    private const double PanelSize = 200;
    private const double RequestedSize = 77;
    private const double RequestedOffset = 33;

    /// <summary>Вид родительской панели в зонде.</summary>
    public enum ParentKind
    {
        /// <summary>Собственная панель редактора.</summary>
        Absolute,

        /// <summary>Штатный <see cref="Avalonia.Controls.Canvas"/>.</summary>
        Canvas,

        /// <summary>Вертикальный <see cref="StackPanel"/>.</summary>
        StackVertical,

        /// <summary>Горизонтальный <see cref="StackPanel"/>.</summary>
        StackHorizontal,

        /// <summary>Одноклеточный <see cref="Avalonia.Controls.Grid"/>.</summary>
        Grid,

        /// <summary><see cref="DockPanel"/> с единственным ребёнком.</summary>
        Dock,

        /// <summary><see cref="WrapPanel"/>.</summary>
        Wrap,

        /// <summary><see cref="Avalonia.Controls.Border"/> — контент-хост, не панель.</summary>
        Border
    }

    private sealed record Probe(bool WidthHonoured, bool HeightHonoured, bool XHonoured, bool YHonoured);

    [AvaloniaTheory]
    [InlineData(ParentKind.Absolute, true)]
    [InlineData(ParentKind.Absolute, false)]
    [InlineData(ParentKind.Canvas, true)]
    [InlineData(ParentKind.Canvas, false)]
    [InlineData(ParentKind.StackVertical, true)]
    [InlineData(ParentKind.StackVertical, false)]
    [InlineData(ParentKind.StackHorizontal, true)]
    [InlineData(ParentKind.StackHorizontal, false)]
    [InlineData(ParentKind.Grid, true)]
    [InlineData(ParentKind.Grid, false)]
    [InlineData(ParentKind.Dock, true)]
    [InlineData(ParentKind.Dock, false)]
    [InlineData(ParentKind.Wrap, true)]
    [InlineData(ParentKind.Wrap, false)]
    [InlineData(ParentKind.Border, true)]
    [InlineData(ParentKind.Border, false)]
    public void Explicit_Size_Is_Honoured_By_Every_Parent(ParentKind kind, bool stretch)
    {
        // Явный Width/Height Avalonia применяет до выравнивания, поэтому Stretch
        // его не съедает. Отсюда следствие для редактора: resize осмыслен всегда,
        // и выводить ResizePolicy из alignment не нужно.
        var probe = Measure(kind, stretch);

        Assert.True(probe.WidthHonoured, $"{kind}, stretch={stretch}: ширина не применилась");
        Assert.True(probe.HeightHonoured, $"{kind}, stretch={stretch}: высота не применилась");
    }

    [AvaloniaTheory]
    [InlineData(ParentKind.Absolute, true)]
    [InlineData(ParentKind.Canvas, false)]
    [InlineData(ParentKind.StackVertical, false)]
    [InlineData(ParentKind.StackHorizontal, false)]
    [InlineData(ParentKind.Grid, false)]
    [InlineData(ParentKind.Dock, false)]
    [InlineData(ParentKind.Wrap, false)]
    [InlineData(ParentKind.Border, false)]
    public void Local_Coordinates_Are_Honoured_Only_By_AbsolutePanel(ParentKind kind, bool expected)
    {
        // Единственный читатель Layout.X/Y во всей библиотеке — AbsolutePanel.ArrangeOverride.
        // Для всех остальных родителей запись позиции уходит в пустоту, и редактор
        // обязан либо сменить смысл жеста, либо его не предлагать.
        // Canvas в этот список не входит намеренно: ему нужны Canvas.Left/Top.
        var probe = Measure(kind, stretch: false);

        Assert.Equal(expected, probe.XHonoured);
        Assert.Equal(expected, probe.YHonoured);
    }

    private static Probe Measure(ParentKind kind, bool stretch)
    {
        var child = new Border
        {
            Name = "Probe",
            HorizontalAlignment = stretch ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
            VerticalAlignment = stretch ? VerticalAlignment.Stretch : VerticalAlignment.Top
        };

        Control parent = kind switch
        {
            ParentKind.Absolute => new AbsolutePanel(),
            ParentKind.Canvas => new Canvas(),
            ParentKind.StackVertical => new StackPanel { Orientation = Orientation.Vertical },
            ParentKind.StackHorizontal => new StackPanel { Orientation = Orientation.Horizontal },
            ParentKind.Grid => new Grid(),
            ParentKind.Dock => new DockPanel(),
            ParentKind.Wrap => new WrapPanel(),
            _ => new Border()
        };

        if (parent is Panel panel)
            panel.Children.Add(child);
        else
            ((Border)parent).Child = child;

        parent.Width = PanelSize;
        parent.Height = PanelSize;
        parent.HorizontalAlignment = HorizontalAlignment.Left;
        parent.VerticalAlignment = VerticalAlignment.Top;

        var window = new Window { Width = 400, Height = 400, Content = parent };
        window.Show();
        RunLayout(window);

        // Запрошенный размер: ровно то, что пишет SetTargetSize.
        child.Width = RequestedSize;
        child.Height = RequestedSize;
        RunLayout(window);

        var widthHonoured = Near(child.Bounds.Width, RequestedSize);
        var heightHonoured = Near(child.Bounds.Height, RequestedSize);

        // Локальные координаты: ровно то, во что превращается SetTargetPosition.
        SurfaceLayout.SetX(child, RequestedOffset);
        SurfaceLayout.SetY(child, RequestedOffset);
        RunLayout(window);

        var offset = child.TranslatePoint(new Point(0, 0), parent) ?? default;

        return new Probe(
            widthHonoured,
            heightHonoured,
            Near(offset.X, RequestedOffset),
            Near(offset.Y, RequestedOffset));
    }

    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) < 0.5;

    private static void RunLayout(Window window)
    {
        var manager = window.GetLayoutManager();
        manager?.ExecuteInitialLayoutPass();
        manager?.ExecuteLayoutPass();
    }
}
