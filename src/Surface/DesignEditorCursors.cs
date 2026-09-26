using Avalonia;
using Avalonia.Input;
using ArxisStudio.Surface.Editing;

namespace ArxisStudio.Surface;

/// <summary>
/// Представляет набор курсоров, которыми редактор показывает идущий жест.
/// </summary>
/// <remarks>
/// Объект отвечает за жесты, у которых <b>нет своего элемента под указателем</b>:
/// перемещение, панорамирование, рамку выделения, перестановку и перенос направляющей
/// начинает сам редактор по политике ввода, поэтому и курсор ставит он.
/// <para>
/// Курсоры частей, которые видно и на которые наводят, — ручки изменения размера
/// и линейка — задаются самой частью, как принято в Avalonia, а настраиваются
/// ресурсами темы (<c>DesignEditor.SelectionAdorner.Cursor*</c>,
/// <c>DesignEditor.Ruler.Cursor*</c>).
/// </para>
/// <para>
/// Значение <see langword="null"/> у любого свойства означает «библиотечный курсор
/// этого жеста», а не «курсор не менять»: так же устроен
/// <see cref="DesignEditorInteractionOptions.SnapStep"/> со своим <c>NaN</c>.
/// Чтобы курсор при жесте не менялся вовсе, задается обычная стрелка.
/// </para>
/// <para>
/// Значение читается один раз, на входе в жест: смена курсора посреди протяжки
/// описывала бы жест, который уже идёт.
/// </para>
/// </remarks>
/// <example>
/// <code language="xml"><![CDATA[
/// <design:DesignEditor.Cursors>
///     <design:DesignEditorCursors Move="SizeAll"
///                                 Blocked="No"
///                                 Pan="Hand"
///                                 Marquee="Cross"
///                                 Reorder="DragMove"
///                                 GuideHorizontal="SizeNorthSouth"
///                                 GuideVertical="SizeWestEast" />
/// </design:DesignEditor.Cursors>
/// ]]></code>
/// </example>
public class DesignEditorCursors : AvaloniaObject
{
    private static Cursor? _defaultMove;
    private static Cursor? _defaultBlocked;
    private static Cursor? _defaultPan;
    private static Cursor? _defaultMarquee;
    private static Cursor? _defaultReorder;
    private static Cursor? _defaultGuideHorizontal;
    private static Cursor? _defaultGuideVertical;

    /// <summary>
    /// Идентификатор свойства курсора перемещения.
    /// </summary>
    /// <remarks>По умолчанию: <see cref="StandardCursorType.SizeAll"/>.</remarks>
    public static readonly StyledProperty<Cursor?> MoveProperty =
        AvaloniaProperty.Register<DesignEditorCursors, Cursor?>(nameof(Move));

    /// <summary>
    /// Идентификатор свойства курсора отклонённого жеста.
    /// </summary>
    /// <remarks>По умолчанию: <see cref="StandardCursorType.No"/>.</remarks>
    public static readonly StyledProperty<Cursor?> BlockedProperty =
        AvaloniaProperty.Register<DesignEditorCursors, Cursor?>(nameof(Blocked));

    /// <summary>
    /// Идентификатор свойства курсора панорамирования.
    /// </summary>
    /// <remarks>По умолчанию: <see cref="StandardCursorType.Hand"/>.</remarks>
    public static readonly StyledProperty<Cursor?> PanProperty =
        AvaloniaProperty.Register<DesignEditorCursors, Cursor?>(nameof(Pan));

    /// <summary>
    /// Идентификатор свойства курсора рамки выделения.
    /// </summary>
    /// <remarks>По умолчанию: <see cref="StandardCursorType.Cross"/>.</remarks>
    public static readonly StyledProperty<Cursor?> MarqueeProperty =
        AvaloniaProperty.Register<DesignEditorCursors, Cursor?>(nameof(Marquee));

    /// <summary>
    /// Идентификатор свойства курсора перестановки среди соседей.
    /// </summary>
    /// <remarks>По умолчанию: <see cref="StandardCursorType.DragMove"/>.</remarks>
    public static readonly StyledProperty<Cursor?> ReorderProperty =
        AvaloniaProperty.Register<DesignEditorCursors, Cursor?>(nameof(Reorder));

    /// <summary>
    /// Идентификатор свойства курсора переноса горизонтальной направляющей.
    /// </summary>
    /// <remarks>По умолчанию: <see cref="StandardCursorType.SizeNorthSouth"/>.</remarks>
    public static readonly StyledProperty<Cursor?> GuideHorizontalProperty =
        AvaloniaProperty.Register<DesignEditorCursors, Cursor?>(nameof(GuideHorizontal));

    /// <summary>
    /// Идентификатор свойства курсора переноса вертикальной направляющей.
    /// </summary>
    /// <remarks>По умолчанию: <see cref="StandardCursorType.SizeWestEast"/>.</remarks>
    public static readonly StyledProperty<Cursor?> GuideVerticalProperty =
        AvaloniaProperty.Register<DesignEditorCursors, Cursor?>(nameof(GuideVertical));

    /// <summary>
    /// Получает или задает курсор перемещения выделения.
    /// </summary>
    public Cursor? Move
    {
        get => GetValue(MoveProperty);
        set => SetValue(MoveProperty, value);
    }

    /// <summary>
    /// Получает или задает курсор жеста, отклонённого политикой перемещения.
    /// </summary>
    /// <remarks>
    /// Ставится, когда перетаскивание не начнётся: у target'а
    /// <see cref="MovePolicy.None"/> или в группе смешаны заблокированные
    /// и свободные участники. Это то же правило, по которому редактор не предлагает
    /// жест, который ничего не делает, — только выраженное курсором.
    /// </remarks>
    public Cursor? Blocked
    {
        get => GetValue(BlockedProperty);
        set => SetValue(BlockedProperty, value);
    }

    /// <summary>
    /// Получает или задает курсор панорамирования viewport.
    /// </summary>
    public Cursor? Pan
    {
        get => GetValue(PanProperty);
        set => SetValue(PanProperty, value);
    }

    /// <summary>
    /// Получает или задает курсор протяжки рамки выделения.
    /// </summary>
    public Cursor? Marquee
    {
        get => GetValue(MarqueeProperty);
        set => SetValue(MarqueeProperty, value);
    }

    /// <summary>
    /// Получает или задает курсор перестановки среди соседей.
    /// </summary>
    /// <remarks>
    /// Жест отдельный от перемещения: в потоковой раскладке перетаскивание меняет
    /// порядок детей, а не координаты, — поэтому и курсор у него свой.
    /// </remarks>
    public Cursor? Reorder
    {
        get => GetValue(ReorderProperty);
        set => SetValue(ReorderProperty, value);
    }

    /// <summary>
    /// Получает или задает курсор переноса горизонтальной направляющей.
    /// </summary>
    /// <remarks>
    /// Горизонтальная направляющая ездит по вертикали, отсюда и умолчание
    /// <see cref="StandardCursorType.SizeNorthSouth"/>.
    /// </remarks>
    public Cursor? GuideHorizontal
    {
        get => GetValue(GuideHorizontalProperty);
        set => SetValue(GuideHorizontalProperty, value);
    }

    /// <summary>
    /// Получает или задает курсор переноса вертикальной направляющей.
    /// </summary>
    public Cursor? GuideVertical
    {
        get => GetValue(GuideVerticalProperty);
        set => SetValue(GuideVerticalProperty, value);
    }

    /// <summary>
    /// Возвращает курсор перемещения с учётом умолчания.
    /// </summary>
    internal Cursor ResolveMove() =>
        Move ?? (_defaultMove ??= new Cursor(StandardCursorType.SizeAll));

    /// <summary>
    /// Возвращает курсор отклонённого жеста с учётом умолчания.
    /// </summary>
    internal Cursor ResolveBlocked() =>
        Blocked ?? (_defaultBlocked ??= new Cursor(StandardCursorType.No));

    /// <summary>
    /// Возвращает курсор панорамирования с учётом умолчания.
    /// </summary>
    internal Cursor ResolvePan() =>
        Pan ?? (_defaultPan ??= new Cursor(StandardCursorType.Hand));

    /// <summary>
    /// Возвращает курсор рамки выделения с учётом умолчания.
    /// </summary>
    internal Cursor ResolveMarquee() =>
        Marquee ?? (_defaultMarquee ??= new Cursor(StandardCursorType.Cross));

    /// <summary>
    /// Возвращает курсор перестановки с учётом умолчания.
    /// </summary>
    internal Cursor ResolveReorder() =>
        Reorder ?? (_defaultReorder ??= new Cursor(StandardCursorType.DragMove));

    /// <summary>
    /// Возвращает курсор переноса направляющей с учётом её ориентации.
    /// </summary>
    /// <param name="vertical">Направляющая вертикальная, то есть переносится по горизонтали.</param>
    /// <remarks>
    /// Флаг, а не ориентация направляющей: направляющие — инструмент слоя редактирования,
    /// и ядро не называет его типов (ADR 0003).
    /// </remarks>
    internal Cursor ResolveGuide(bool vertical) =>
        vertical
            ? GuideVertical ?? (_defaultGuideVertical ??= new Cursor(StandardCursorType.SizeWestEast))
            : GuideHorizontal ?? (_defaultGuideHorizontal ??= new Cursor(StandardCursorType.SizeNorthSouth));
}
