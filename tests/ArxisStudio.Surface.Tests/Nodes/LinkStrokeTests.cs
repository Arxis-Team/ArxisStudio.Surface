using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface;
using ArxisStudio.Surface.Nodes;

namespace ArxisStudio.Tests;

/// <summary>
/// Цвет провода из модели (ADR 0009): <see cref="NodeEditor.LinkStrokeBinding"/> в обоих видах.
/// </summary>
/// <remarks>
/// Стенд: окно 800 × 600, редактор узлов над рядом из четырёх узлов 120 × 80 через 200 и цепочкой
/// связей между ними — у первой цвет, у второй кисть, у третьей цвета нет; привязки положения, узла
/// порта и цвета провода заданы.
/// </remarks>
public class LinkStrokeTests
{
    private static readonly Size NodeSize = new(120, 80);
    private static readonly IBrush Blue = new ImmutableSolidColorBrush(Colors.Blue);

    private sealed class Wire(PortModel from, PortModel to, object? color) : INotifyPropertyChanged
    {
        private object? _color = color;

        public PortModel From { get; } = from;

        public PortModel To { get; } = to;

        public object? Color
        {
            get => _color;
            set
            {
                if (Equals(_color, value))
                    return;

                _color = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Color)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed record Stand(Window Window, NodeEditor Editor, ObservableCollection<object> Links)
    {
        public Wire Wire(int index) => (Wire)Links[index];

        public Link Control(int index) => Editor.RecordOf(Links[index])!.Control!;

        public SimplifiedLinkLayer Layer => Editor.GetVisualDescendants().OfType<SimplifiedLinkLayer>().Single();

        public void RunLayout()
        {
            var manager = Window.GetLayoutManager();
            manager?.ExecuteInitialLayoutPass();
            manager?.ExecuteLayoutPass();
        }
    }

    private static Stand Create()
    {
        var nodes = new ObservableCollection<object>();
        for (var i = 0; i < 4; i++)
            nodes.Add(new NodeModel("Узел " + i, new Point(i * 200, 0)));

        NodeModel Node(int index) => (NodeModel)nodes[index];
        var links = new ObservableCollection<object>
        {
            new Wire(Node(0).Out, Node(1).In, Colors.Red),
            new Wire(Node(1).Out, Node(2).In, Blue),
            new Wire(Node(2).Out, Node(3).In, null)
        };

        var editor = new NodeEditor
        {
            ItemsSource = nodes,
            Links = links,
            ItemTemplate = GraphTemplates.Node(NodeSize),
            ItemLocationBinding = new Binding(nameof(NodeModel.Location)),
            PortNodeBinding = new Binding(nameof(PortModel.Node)),
            EstimatedItemSize = NodeSize,
            LinkSourceBinding = new Binding(nameof(Wire.From)),
            LinkTargetBinding = new Binding(nameof(Wire.To)),
            LinkStrokeBinding = new Binding(nameof(Wire.Color))
        };

        var window = new Window { Width = 800, Height = 600, Content = editor };
        window.Show();
        var stand = new Stand(window, editor, links);
        stand.RunLayout();
        return stand;
    }

    [AvaloniaFact]
    public void A_Wire_Takes_Its_Color_From_The_Model()
    {
        // Цвет — кистью из общего кеша ядра, кисть — как есть; без значения — цвет темы.
        var stand = Create();

        Assert.Same(AccentBrushes.From(Colors.Red), stand.Control(0).EffectiveStroke);
        Assert.Same(Blue, stand.Control(1).EffectiveStroke);
        Assert.Same(stand.Control(2).Stroke, stand.Control(2).EffectiveStroke);
    }

    [AvaloniaFact]
    public void Gesture_States_Keep_The_Theme_Color()
    {
        // Выбранный и подсвеченный провод виден выбранным и подсвеченным, а не своим типом.
        var stand = Create();

        stand.Editor.SelectLink(stand.Links[0]);
        stand.RunLayout();
        Assert.Same(stand.Control(0).Stroke, stand.Control(0).EffectiveStroke);

        stand.Editor.SetLinkHighlighted(stand.Editor.RecordOf(stand.Links[1])!, true);
        Assert.Same(stand.Control(1).Stroke, stand.Control(1).EffectiveStroke);

        stand.Editor.SetLinkHighlighted(stand.Editor.RecordOf(stand.Links[1])!, false);
        Assert.Same(Blue, stand.Control(1).EffectiveStroke);
    }

    [AvaloniaFact]
    public void Editing_The_Model_Recolors_The_Wire()
    {
        var stand = Create();

        stand.Wire(2).Color = Colors.Green;

        Assert.Same(AccentBrushes.From(Colors.Green), stand.Control(2).EffectiveStroke);
    }

    [AvaloniaFact]
    public void The_Simplified_Layer_Keeps_The_Colors()
    {
        // Ниже порога провода без контролов рисует слой — по геометрии на цвет модели; правка модели
        // перекрашивает и там.
        var stand = Create();
        stand.Editor.ViewportZoom = 0.25;
        stand.RunLayout();
        stand.Window.CaptureRenderedFrame();
        Assert.Equal(0, stand.Editor.RealizedLinks);
        Assert.Equal(3, stand.Layer.Links);
        Assert.Equal(2, stand.Layer.StrokeGroups);

        stand.Wire(2).Color = Colors.Green;
        stand.RunLayout();
        stand.Window.CaptureRenderedFrame();
        Assert.Equal(3, stand.Layer.StrokeGroups);
    }

    [AvaloniaFact]
    public void A_New_Stroke_Binding_Recolors_Every_Wire()
    {
        var stand = Create();

        stand.Editor.LinkStrokeBinding = null;
        Assert.Same(stand.Control(0).Stroke, stand.Control(0).EffectiveStroke);

        stand.Editor.LinkStrokeBinding = new Binding(nameof(Wire.Color));
        Assert.Same(AccentBrushes.From(Colors.Red), stand.Control(0).EffectiveStroke);
    }
}
