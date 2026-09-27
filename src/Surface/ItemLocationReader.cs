using Avalonia;
using Avalonia.Data;

namespace ArxisStudio.Surface;

/// <summary>
/// Читает положение элемента привязкой <see cref="SurfaceView.ItemLocationBinding"/> без контейнера.
/// </summary>
/// <remarks>
/// Один на панель: привязка ставится один раз, а читается сменой контекста данных. Так положение
/// неразвёрнутого элемента даёт та же привязка, что у контейнера, и модели хоста не нужны ни тип, ни
/// интерфейс библиотеки (ADR 0007). Дерева у читателя нет, поэтому источник привязки — сам элемент:
/// <c>ElementName</c> и <c>RelativeSource</c> здесь ничего не найдут.
/// </remarks>
internal sealed class ItemLocationReader : StyledElement
{
    private static readonly StyledProperty<Point> ValueProperty =
        AvaloniaProperty.Register<ItemLocationReader, Point>("Value");

    /// <summary>
    /// Инициализирует читатель привязкой положения.
    /// </summary>
    public ItemLocationReader(BindingBase binding)
    {
        Binding = binding;
        Bind(ValueProperty, binding);
    }

    /// <summary>
    /// Привязка, которой читатель читает.
    /// </summary>
    public BindingBase Binding { get; }

    /// <summary>
    /// Положение элемента, которое дала бы его контейнеру привязка.
    /// </summary>
    public Point Read(object? item)
    {
        DataContext = item;
        return GetValue(ValueProperty);
    }

    /// <summary>
    /// Отпускает последний прочитанный элемент: читатель живёт с панелью, а элемент может уйти раньше.
    /// </summary>
    public void Release() => DataContext = null;
}
