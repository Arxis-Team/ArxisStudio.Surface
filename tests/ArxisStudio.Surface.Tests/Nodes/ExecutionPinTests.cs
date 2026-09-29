using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface.Nodes;

namespace ArxisStudio.Tests;

/// <summary>
/// Пины и провода выполнения (ADR 0016): вид штырька и толщина провода задаются хостом, а что считать
/// выполнением, а что данными, решает его граф.
/// </summary>
public class ExecutionPinTests
{
    private static readonly IBrush White = new ImmutableSolidColorBrush(Colors.White);

    private static Path PinOf(Port port) =>
        port.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "PART_Pin");

    private static NodeStand Create() =>
        NodeStand.Create([new Point(100, 100), new Point(400, 100)], itemTemplate: NodeStand.PortedNode);

    [AvaloniaFact]
    public void The_Pin_Takes_Its_Shape_From_The_Port()
    {
        var stand = Create();
        var port = stand.PortOf(0, PortDirection.Output);
        var circle = PinOf(port).Data;

        port.PinShape = PortShape.Execution;
        stand.RunLayout();

        Assert.NotSame(circle, PinOf(port).Data);
        Assert.Same(port.PinData, PinOf(port).Data);

        // Своя форма без геометрии — круг, с геометрией — она.
        port.PinShape = PortShape.Custom;
        Assert.Same(circle, port.PinData);
        var star = Geometry.Parse("M 0,0 L 4,10 L 8,0 Z");
        port.PinGeometry = star;
        Assert.Same(star, port.PinData);
    }

    [AvaloniaFact]
    public void The_Pin_Brush_Outlines_An_Empty_Pin_And_Fills_A_Connected_One()
    {
        // Как пин Blueprint: пустой — обводка цвета вида, подключённый — заливка.
        var stand = Create();
        var output = stand.PortOf(0, PortDirection.Output);
        output.PinBrush = White;
        stand.RunLayout();

        Assert.Same(White, PinOf(output).Stroke);
        Assert.NotSame(White, PinOf(output).Fill);

        stand.Connect(0, 1);
        Assert.Same(White, PinOf(output).Fill);
    }

    [AvaloniaFact]
    public void Gesture_Feedback_Beats_The_Pin_Brush()
    {
        var stand = Create();
        var input = stand.PortOf(1, PortDirection.Input);
        input.PinBrush = White;
        stand.RunLayout();

        input.SetAcceptance(false);
        stand.RunLayout();

        Assert.NotSame(White, PinOf(input).Stroke);
        Assert.True(stand.Editor.TryFindResource("NodeEditor.Port.RefusingStroke", stand.Editor.ActualThemeVariant, out var refusing));
        Assert.Same(refusing, PinOf(input).Stroke);
    }

    /// <summary>Провод с толщиной из модели.</summary>
    private sealed class Wire(object from, object to, double? thickness) : INotifyPropertyChanged
    {
        private double? _thickness = thickness;

        public object From { get; } = from;

        public object To { get; } = to;

        public double? Thickness
        {
            get => _thickness;
            set
            {
                _thickness = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thickness)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    [AvaloniaFact]
    public void A_Wire_Takes_Its_Thickness_From_The_Model()
    {
        // Провод выполнения толще провода данных; без значения — толщина темы. Правка модели —
        // новая толщина, снятое значение возвращает тему.
        var stand = NodeStand.Create([new Point(100, 100), new Point(400, 100), new Point(400, 300)], itemTemplate: NodeStand.PortedNode);
        stand.Editor.LinkSourceBinding = new Binding(nameof(Wire.From));
        stand.Editor.LinkTargetBinding = new Binding(nameof(Wire.To));
        stand.Editor.LinkThicknessBinding = new Binding(nameof(Wire.Thickness));
        var exec = new Wire(NodeStand.Out(0), NodeStand.In(1), 4);
        var data = new Wire(NodeStand.Out(0), NodeStand.In(2), null);
        stand.Links.Add(exec);
        stand.Links.Add(data);
        stand.RunLayout();

        Link ControlOf(object item) => stand.Editor.RecordOf(item)!.Control!;
        var theme = ControlOf(data).StrokeThickness;
        Assert.Equal(4, ControlOf(exec).StrokeThickness);
        Assert.NotEqual(4, theme);
        Assert.True(stand.Editor.RecordOf(exec)!.WorldBounds.Height > stand.Editor.RecordOf(exec)!.Geometry.Bounds.Height + 3,
            "рамка провода держит запас на его толщину");

        exec.Thickness = 6;
        Assert.Equal(6, ControlOf(exec).StrokeThickness);

        exec.Thickness = null;
        Assert.Equal(theme, ControlOf(exec).StrokeThickness);
    }
}
