using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ArxisStudio.Surface.Nodes;

namespace ArxisStudio.Tests;

/// <summary>
/// Узел графа, как его держит хост при виртуализации: положение с уведомлением и модели портов.
/// </summary>
internal sealed class NodeModel : INotifyPropertyChanged
{
    private Point _location;

    public NodeModel(string name, Point location)
    {
        Name = name;
        _location = location;
        In = new PortModel(this, "in");
        Out = new PortModel(this, "out");
    }

    public string Name { get; }

    public PortModel In { get; }

    public PortModel Out { get; }

    public Point Location
    {
        get => _location;
        set
        {
            if (_location == value)
                return;

            _location = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Location)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Порт графа: ключ связи и ссылка на свой узел — её читает <see cref="NodeEditor.PortNodeBinding"/>.
/// </summary>
internal sealed class PortModel(NodeModel node, string name)
{
    public NodeModel Node { get; } = node;

    public override string ToString() => Node.Name + "." + name;
}

/// <summary>
/// Связь графа: данные двух портов.
/// </summary>
internal sealed record LinkModel(PortModel From, PortModel To);

/// <summary>
/// Шаблон узла графа: заголовок и порты «in» и «out», ключуемые своими моделями, в прямоугольнике
/// заданного размера.
/// </summary>
internal static class GraphTemplates
{
    /// <summary>
    /// Узел размера <paramref name="size"/>.
    /// </summary>
    /// <remarks>
    /// Переработка очищает содержимое контейнера, пока он ещё в дереве, и шаблон строится и с пустыми
    /// данными — как шаблон разметки, он обязан это переносить.
    /// </remarks>
    public static IDataTemplate Node(Size size) => new FuncDataTemplate<NodeModel?>((node, _) => new StackPanel
    {
        Width = size.Width,
        Height = size.Height,
        Children =
        {
            new TextBlock { Text = node?.Name },
            new Port { Direction = PortDirection.Input, Data = node?.In, Content = "in" },
            new Port { Direction = PortDirection.Output, Data = node?.Out, Content = "out" }
        }
    }, supportsRecycling: false);
}
