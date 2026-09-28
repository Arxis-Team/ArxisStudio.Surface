using Avalonia;
using Avalonia.Data;

namespace ArxisStudio.Surface;

/// <summary>
/// Читает привязку хоста у объекта без контрола: положение элемента без контейнера
/// (<see cref="SurfaceView.ItemLocationBinding"/>), узел порта без живого порта.
/// </summary>
/// <remarks>
/// Один на привязку: она ставится один раз, а читается сменой контекста данных. Так значение даёт
/// та же привязка, что у контрола, и модели хоста не нужны ни тип, ни интерфейс библиотеки
/// (ADR 0007). Дерева у читателя нет, поэтому источник привязки — сам объект: <c>ElementName</c> и
/// <c>RelativeSource</c> здесь ничего не найдут.
/// <para>
/// Свойство, в которое читается значение, у каждого наследника своего типа — такого же, как у
/// контрола, куда привязку ставит хост: тогда и преобразование значения то же.
/// </para>
/// </remarks>
internal abstract class BindingReader : StyledElement
{
    /// <summary>
    /// Инициализирует читатель привязкой.
    /// </summary>
    protected BindingReader(BindingBase binding) => Binding = binding;

    /// <summary>
    /// Привязка, которой читатель читает.
    /// </summary>
    public BindingBase Binding { get; }

    /// <summary>
    /// Отпускает последний прочитанный объект: читатель живёт дольше, а объект может уйти раньше.
    /// </summary>
    public void Release() => DataContext = null;
}

/// <summary>
/// Читает положение — как его дала бы привязка <see cref="SurfaceItem.Location"/>.
/// </summary>
internal sealed class PointBindingReader : BindingReader
{
    private static readonly StyledProperty<Point> ValueProperty =
        AvaloniaProperty.Register<PointBindingReader, Point>("Value");

    /// <summary>
    /// Инициализирует читатель привязкой.
    /// </summary>
    public PointBindingReader(BindingBase binding)
        : base(binding) => Bind(ValueProperty, binding);

    /// <summary>
    /// Значение, которое дала бы привязка контролу с этим контекстом данных.
    /// </summary>
    public Point Read(object? source)
    {
        DataContext = source;
        return GetValue(ValueProperty);
    }
}

/// <summary>
/// Пишет положение в модель той же привязкой в обратную сторону — как её записал бы
/// <see cref="SurfaceItem.Location"/>, двусторонний по умолчанию.
/// </summary>
/// <remarks>
/// ADR 0010: так двигается выбранный элемент без контейнера. Привязка, которая в модель не пишет, —
/// односторонняя или с источником только для чтения, — запись не примет, и проверяет это тот, кто
/// пишет, чтением.
/// </remarks>
internal sealed class PointBindingWriter : BindingReader
{
    private static readonly StyledProperty<Point> ValueProperty =
        AvaloniaProperty.Register<PointBindingWriter, Point>("Value", defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Инициализирует писатель привязкой.
    /// </summary>
    public PointBindingWriter(BindingBase binding)
        : base(binding) => Bind(ValueProperty, binding);

    /// <summary>
    /// Пишет значение в модель, как его записал бы контрол с этим контекстом данных.
    /// </summary>
    public void Write(object? source, Point value)
    {
        DataContext = source;
        SetCurrentValue(ValueProperty, value);
    }
}

/// <summary>
/// Читает объект — как его дала бы привязка свойству типа <see cref="object"/>.
/// </summary>
internal sealed class ObjectBindingReader : BindingReader
{
    private static readonly StyledProperty<object?> ValueProperty =
        AvaloniaProperty.Register<ObjectBindingReader, object?>("Value");

    /// <summary>
    /// Инициализирует читатель привязкой.
    /// </summary>
    public ObjectBindingReader(BindingBase binding)
        : base(binding) => Bind(ValueProperty, binding);

    /// <summary>
    /// Значение, которое дала бы привязка контролу с этим контекстом данных.
    /// </summary>
    public object? Read(object? source)
    {
        DataContext = source;
        return GetValue(ValueProperty);
    }
}
