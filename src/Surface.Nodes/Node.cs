using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Контейнер узла графа на поверхности <see cref="NodeEditor"/>.
/// </summary>
/// <remarks>
/// Положение, выделение и перетаскивание — ядра (<see cref="SurfaceItem"/>). Свой тип нужен узлу
/// ради своей темы — Avalonia ищет тему по точному типу — и ради портов, которые лежат в его
/// содержимом: тело узла приложение пишет в <c>ItemTemplate</c> редактора, ставя туда
/// <see cref="Port"/>. Узел, в котором лежит <see cref="Reroute"/>, получает псевдокласс
/// <c>:reroute</c>: тема снимает с него карточку.
/// <para>
/// Заголовок — часть узла, как у <see cref="Avalonia.Controls.Primitives.HeaderedContentControl"/>
/// (ADR 0009): <see cref="Header"/> и <see cref="HeaderTemplate"/>; созданному контейнеру его ставит
/// <see cref="NodeEditor.ItemHeaderBinding"/>. Базовая тема кладёт заголовок на полосу
/// <see cref="SurfaceItem.Accent"/>. Псевдоклассы для тем: <c>:header</c> — есть заголовок или полоса,
/// <c>:accent</c> — есть полоса, <c>:light-accent</c> — полоса светлая, и тёмный текст на ней
/// контрастнее светлого.
/// </para>
/// </remarks>
public class Node : SurfaceItem
{
    /// <summary>
    /// Идентификатор свойства <see cref="Header"/>.
    /// </summary>
    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<Node, object?>(nameof(Header));

    /// <summary>
    /// Идентификатор свойства <see cref="HeaderTemplate"/>.
    /// </summary>
    public static readonly StyledProperty<IDataTemplate?> HeaderTemplateProperty =
        AvaloniaProperty.Register<Node, IDataTemplate?>(nameof(HeaderTemplate));

    /// <summary>
    /// Относительная яркость, на которой белый и чёрный текст дают равный контраст — 4,58:1 (WCAG); выше
    /// неё чёрный контрастнее.
    /// </summary>
    /// <remarks>
    /// Порог держит контраст названия не ниже 4,5:1 на любой полосе только вместе с цветами темы — белым и
    /// чёрным: тёмно-серый у полос чуть светлее порога давал бы 3,7:1.
    /// </remarks>
    private const double LightLuminance = 0.179;

    /// <summary>
    /// Получает или задает заголовок узла — строку или содержимое для <see cref="HeaderTemplate"/>.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> и без полосы — области заголовка у узла нет.
    /// </remarks>
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// Получает или задает шаблон заголовка; без него строка заголовка рисуется текстом темы.
    /// </summary>
    public IDataTemplate? HeaderTemplate
    {
        get => GetValue(HeaderTemplateProperty);
        set => SetValue(HeaderTemplateProperty, value);
    }

    /// <summary>
    /// Привязка заголовка, которую контейнеру поставил редактор; снимается освобождением, как привязка
    /// положения.
    /// </summary>
    internal IDisposable? HeaderBinding { get; set; }

    /// <summary>
    /// Светлая ли полоса: на ней тёмный текст контрастнее светлого.
    /// </summary>
    internal static bool IsLight(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        var luminance = (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
        return luminance > LightLuminance;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == HeaderProperty || change.Property == AccentProperty)
        {
            PseudoClasses.Set(":header", Header != null || Accent != null);
            PseudoClasses.Set(":accent", Accent != null);
            PseudoClasses.Set(":light-accent", Accent is ISolidColorBrush solid && IsLight(solid.Color));
        }
    }

    /// <summary>
    /// Раскладывает содержимое узла и сверяет, не сдвинулись ли в нём порты.
    /// </summary>
    /// <param name="finalSize">Размер, отведённый узлу.</param>
    /// <returns>Размер, который узел занял.</returns>
    /// <remarks>
    /// Сверка стоит здесь, а не на границах порта: когда границы получает порт, его предки внутри
    /// узла — список, его контейнер, сетка — своих ещё не получили, и конец, посчитанный в этот
    /// миг, ложится мимо. К концу раскладки узла всё его поддерево разложено окончательно. Сдвиг
    /// самого узла портов внутри него не двигает, поэтому кадр перетаскивания здесь ничего не
    /// пересчитывает.
    /// </remarks>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        this.FindAncestorOfType<NodeEditor>()?.OnNodeArranged(this);
        return size;
    }

    /// <summary>
    /// Отмечает узел перевалкой — его ставит и снимает сам <see cref="Reroute"/>.
    /// </summary>
    internal void SetReroute(bool value) => PseudoClasses.Set(":reroute", value);
}
