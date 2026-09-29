using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;
using ArxisStudio.Surface;
using ArxisStudio.Surface.Nodes;

namespace ArxisStudio.Tests;

/// <summary>
/// Роли пина и провода (ADR 0017): базовый вид выполнения и делегата даёт тема, значения хоста сильнее,
/// правил у роли нет.
/// </summary>
public class PinRoleTests
{
    private static NodeStand Create() =>
        NodeStand.Create([new Point(100, 100), new Point(400, 100), new Point(400, 300)], itemTemplate: NodeStand.PortedNode);

    private static object Resource(Control control, string key)
    {
        Assert.True(control.TryFindResource(key, control.ActualThemeVariant, out var value), $"нет ключа {key}");
        return value!;
    }

    [AvaloniaFact]
    public void A_Pin_Role_Gives_The_Base_Look()
    {
        var stand = Create();
        var output = stand.PortOf(0, PortDirection.Output);
        var input = stand.PortOf(1, PortDirection.Input);
        Assert.Equal(PortShape.Circle, output.ActualPinShape);
        Assert.Null(output.ActualPinBrush);

        output.PinRole = PinRole.Execution;
        Assert.Equal(PortShape.Execution, output.ActualPinShape);
        Assert.Same(Resource(output, "NodeEditor.Pin.Execution.Brush"), output.ActualPinBrush);

        // Делегат, как в Blueprint: квадрат у выхода — «адрес события», круг у входа — «куда его отдать».
        output.PinRole = PinRole.Delegate;
        input.PinRole = PinRole.Delegate;
        Assert.Equal(PortShape.Square, output.ActualPinShape);
        Assert.Equal(PortShape.Circle, input.ActualPinShape);
        Assert.Same(Resource(output, "NodeEditor.Pin.Delegate.Brush"), output.ActualPinBrush);
        Assert.Same(Resource(input, "NodeEditor.Pin.Delegate.Brush"), input.ActualPinBrush);

        output.PinRole = PinRole.Data;
        Assert.Equal(PortShape.Circle, output.ActualPinShape);
        Assert.Null(output.ActualPinBrush);
    }

    [AvaloniaFact]
    public void A_Host_Value_Beats_The_Role()
    {
        var stand = Create();
        var port = stand.PortOf(0, PortDirection.Output);
        var brush = Brushes.Orange;
        port.PinShape = PortShape.Diamond;
        port.PinBrush = brush;

        port.PinRole = PinRole.Execution;

        Assert.Equal(PortShape.Diamond, port.ActualPinShape);
        Assert.Same(brush, port.ActualPinBrush);
    }

    [AvaloniaFact]
    public void A_Null_From_The_Host_Keeps_The_Role()
    {
        // Один шаблон порта на все пины: привязка цвета отдаёт кисть портам данных и null портам
        // выполнения. Null значит «вид роли», а не «ничего»: пятиугольник и цвет роли остаются.
        var stand = Create();
        var port = stand.PortOf(0, PortDirection.Output);
        port.PinRole = PinRole.Execution;
        port.PinShape = null;
        port.PinBrush = null;

        Assert.Equal(PortShape.Execution, port.ActualPinShape);
        Assert.Same(Resource(port, "NodeEditor.Pin.Execution.Brush"), port.ActualPinBrush);

        // Своё значение хоста — сильнее, снятое — снова роль.
        port.PinBrush = Brushes.Orange;
        Assert.Same(Brushes.Orange, port.ActualPinBrush);
        port.PinBrush = null;
        Assert.Same(Resource(port, "NodeEditor.Pin.Execution.Brush"), port.ActualPinBrush);
    }

    /// <summary>Провод с ролью и, может быть, своим цветом.</summary>
    private sealed record Wire(object From, object To, PinRole Role, object? Color = null);

    private static (NodeStand Stand, Wire Exec, Wire Delegate, Wire Data) CreateWired()
    {
        var stand = Create();
        stand.Editor.LinkSourceBinding = new Binding(nameof(Wire.From));
        stand.Editor.LinkTargetBinding = new Binding(nameof(Wire.To));
        stand.Editor.LinkRoleBinding = new Binding(nameof(Wire.Role));
        stand.Editor.LinkStrokeBinding = new Binding(nameof(Wire.Color));
        var exec = new Wire(NodeStand.Out(0), NodeStand.In(1), PinRole.Execution);
        var @delegate = new Wire(NodeStand.Out(1), NodeStand.In(2), PinRole.Delegate);
        var data = new Wire(NodeStand.Out(0), NodeStand.In(2), PinRole.Data);
        stand.Links.Add(exec);
        stand.Links.Add(@delegate);
        stand.Links.Add(data);
        stand.RunLayout();
        return (stand, exec, @delegate, data);
    }

    [AvaloniaFact]
    public void A_Link_Role_Gives_The_Wire_Its_Stroke_And_Thickness()
    {
        var (stand, exec, @delegate, data) = CreateWired();
        Link ControlOf(object item) => stand.Editor.RecordOf(item)!.Control!;

        Assert.Same(Resource(stand.Editor, "NodeEditor.Link.Execution.Stroke"), ControlOf(exec).EffectiveStroke);
        Assert.Equal((double)Resource(stand.Editor, "NodeEditor.Link.Execution.Thickness"), ControlOf(exec).StrokeThickness);
        Assert.Same(Resource(stand.Editor, "NodeEditor.Link.Delegate.Stroke"), ControlOf(@delegate).EffectiveStroke);
        Assert.Same(ControlOf(data).Stroke, ControlOf(data).EffectiveStroke);

        // Роль держит запись, а не контрол: упрощённый вид, маркеры и импульсы видят тот же цвет.
        Assert.Same(Resource(stand.Editor, "NodeEditor.Link.Delegate.Stroke"), stand.Editor.RecordOf(@delegate)!.Stroke);
    }

    [AvaloniaFact]
    public void A_Model_Color_Beats_The_Role()
    {
        var stand = Create();
        stand.Editor.LinkSourceBinding = new Binding(nameof(Wire.From));
        stand.Editor.LinkTargetBinding = new Binding(nameof(Wire.To));
        stand.Editor.LinkRoleBinding = new Binding(nameof(Wire.Role));
        stand.Editor.LinkStrokeBinding = new Binding(nameof(Wire.Color));
        var wire = new Wire(NodeStand.Out(0), NodeStand.In(1), PinRole.Delegate, Colors.Gold);
        stand.Links.Add(wire);
        stand.RunLayout();

        Assert.Same(AccentBrushes.From(Colors.Gold), stand.Editor.RecordOf(wire)!.Stroke);
    }

    [AvaloniaFact]
    public void A_New_Theme_Rereads_The_Role_Colors()
    {
        var (stand, exec, _, _) = CreateWired();
        stand.Window.RequestedThemeVariant = ThemeVariant.Dark;
        stand.RunLayout();
        var dark = stand.Editor.RecordOf(exec)!.Stroke;

        stand.Window.RequestedThemeVariant = ThemeVariant.Light;
        stand.RunLayout();

        Assert.NotSame(dark, stand.Editor.RecordOf(exec)!.Stroke);
        Assert.Same(Resource(stand.Editor, "NodeEditor.Link.Execution.Stroke"), stand.Editor.RecordOf(exec)!.Stroke);
    }
}
