using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Узел-перевалка: вход и выход в одной точке, в центре кольца (ADR 0005).
/// </summary>
/// <remarks>
/// Излом связи в редакторе узлов — узел, а не точка на связи: хост ставит в разрыв связи узел со
/// своими данными, а в его <c>ItemTemplate</c> — этот контрол. Ключи портов — <see cref="Input"/> и
/// <see cref="Output"/>, и задавать нужно оба: иначе оба порта возьмут ключом данные узла.
/// <para>
/// Попав в <see cref="Node"/>, перевалка ставит узлу псевдокласс <c>:reroute</c> — тема снимает с
/// него карточку, — а себе повторяет его выбор псевдоклассом <c>:selected</c>. Нажатие по центру
/// начинает связь из выхода: его штырёк лежит сверху. Нажатие по кольцу тянет узел.
/// </para>
/// <para>
/// Касательные у перевалки горизонтальны, как у любого порта: связь, уведённая через неё назад,
/// петляет.
/// </para>
/// </remarks>
public class Reroute : TemplatedControl
{
    /// <summary>
    /// Идентификатор свойства ключа входа.
    /// </summary>
    public static readonly StyledProperty<object?> InputProperty =
        AvaloniaProperty.Register<Reroute, object?>(nameof(Input));

    /// <summary>
    /// Идентификатор свойства ключа выхода.
    /// </summary>
    public static readonly StyledProperty<object?> OutputProperty =
        AvaloniaProperty.Register<Reroute, object?>(nameof(Output));

    private Node? _node;
    private IDisposable? _selection;

    /// <summary>
    /// Получает или задает ключ входа перевалки — данные, которыми связь называет свою цель.
    /// </summary>
    public object? Input
    {
        get => GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    /// <summary>
    /// Получает или задает ключ выхода перевалки — данные, которыми связь называет свой источник.
    /// </summary>
    public object? Output
    {
        get => GetValue(OutputProperty);
        set => SetValue(OutputProperty, value);
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _node = this.FindAncestorOfType<Node>();
        if (_node == null)
            return;

        _node.SetReroute(true);
        _selection = _node.GetObservable(SurfaceItem.IsSelectedProperty)
            .Subscribe(new SelectionSink(this));
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _selection?.Dispose();
        _selection = null;
        _node?.SetReroute(false);
        _node = null;
        PseudoClasses.Set(":selected", false);
    }

    private sealed class SelectionSink(Reroute reroute) : IObserver<bool>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(bool value) => reroute.PseudoClasses.Set(":selected", value);
    }
}
