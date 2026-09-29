using Avalonia;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Связь, как её держит редактор: ключи концов, кривая, рамка и состояние — есть у неё контрол или
/// нет (ADR 0007).
/// </summary>
/// <remarks>
/// Запись есть у каждого элемента <see cref="NodeEditor.Links"/>, а контрол <see cref="Link"/> — только
/// у развёрнутой связи: при виртуализации — у видимой, без неё — у всех. Попадание, разрез, выбор и
/// миникарта идут по записям, а контрол показывает то, что в записи, и переходит от связи к связи.
/// Готовая <see cref="Link"/> из коллекции сама себе контрол и развёрнута всегда.
/// </remarks>
internal sealed class LinkRecord
{
    /// <summary>
    /// Инициализирует запись элемента коллекции связей.
    /// </summary>
    public LinkRecord(object item)
    {
        Item = item;
        Own = item as Link;
    }

    /// <summary>
    /// Элемент <see cref="NodeEditor.Links"/>; готовая связь — сама себе элемент.
    /// </summary>
    public object Item { get; }

    /// <summary>
    /// Готовая связь из коллекции: концы на ней задаёт хост, и развёрнута она всегда.
    /// </summary>
    public Link? Own { get; }

    /// <summary>
    /// Контрол, который сейчас показывает запись, или <see langword="null"/>, если связь свёрнута.
    /// </summary>
    public Link? Control { get; set; }

    /// <summary>
    /// Ключ порта-источника.
    /// </summary>
    public object? Source { get; set; }

    /// <summary>
    /// Ключ порта-цели.
    /// </summary>
    public object? Target { get; set; }

    /// <summary>
    /// Ключ источника, под которым запись стоит в смежности редактора.
    /// </summary>
    public object? RegisteredSource { get; set; }

    /// <summary>
    /// Ключ цели, под которым запись стоит в смежности редактора.
    /// </summary>
    public object? RegisteredTarget { get; set; }

    /// <summary>
    /// Найдены ли оба конца: только такая связь рисуется.
    /// </summary>
    public bool IsResolved { get; set; }

    private LinkGeometry _geometry;
    private LinkPath? _path;

    /// <summary>
    /// Кривая в мировых координатах.
    /// </summary>
    public LinkGeometry Geometry
    {
        get => _geometry;
        set
        {
            _geometry = value;
            _path = null;
        }
    }

    /// <summary>
    /// Кривая с накопленной длиной — считается при первом вопросе и живёт до смены кривой (ADR 0014).
    /// </summary>
    public LinkPath Path => _path ??= _geometry.Path();

    /// <summary>
    /// Роль провода из модели (<see cref="NodeEditor.LinkRoleBinding"/>).
    /// </summary>
    public PinRole Role { get; set; }

    /// <summary>
    /// Толщина провода: из модели (<see cref="NodeEditor.LinkThicknessBinding"/>), иначе роли; без обеих —
    /// толщина темы.
    /// </summary>
    public double? Thickness { get; set; }

    /// <summary>
    /// Маркер связи из модели (<see cref="NodeEditor.LinkMarkerBinding"/>).
    /// </summary>
    public LinkMarker? Marker { get; set; }

    /// <summary>
    /// Прямоугольник кривой на холсте с запасом на толщину линии.
    /// </summary>
    public Rect WorldBounds { get; set; }

    /// <summary>
    /// Прямоугольник, по которому запись лежит в ячейках окна связей; <see langword="null"/> — не лежит
    /// (ADR 0011).
    /// </summary>
    public Rect? IndexedBounds { get; set; }

    /// <summary>
    /// Цвет провода: из модели (<see cref="NodeEditor.LinkStrokeBinding"/>), иначе роли; без обоих — цвет
    /// темы.
    /// </summary>
    public Avalonia.Media.IBrush? Stroke { get; set; }

    /// <summary>
    /// Выбрана ли связь.
    /// </summary>
    public bool IsSelected { get; set; }

    /// <summary>
    /// Под указателем ли связь.
    /// </summary>
    public bool IsHighlighted { get; set; }

    /// <summary>
    /// Перечёркнута ли связь протягиваемым разрезом.
    /// </summary>
    public bool IsCutting { get; set; }

    /// <summary>
    /// Отцеплён ли конец связи и тянется ли он.
    /// </summary>
    public bool IsDetaching { get; set; }

    /// <summary>
    /// Элемент, которым связь называют приложению.
    /// </summary>
    public object ItemOrSelf => Item;
}
