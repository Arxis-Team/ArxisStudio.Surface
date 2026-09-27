using Avalonia;

namespace ArxisStudio.Surface;

/// <summary>
/// Представляет runtime-настройки взаимодействия редактора, не относящиеся к жестам ввода.
/// </summary>
/// <remarks>
/// Этот объект задает числовые параметры поведения редактора (масштабирование, пороги и ограничения).
/// Кнопки мыши и модификаторы клавиатуры настраиваются отдельно через <see cref="SurfaceInputGestures"/>.
/// </remarks>
public class SurfaceInteractionOptions : AvaloniaObject
{
    /// <summary>
    /// Идентификатор свойства коэффициента шага масштабирования колесом мыши.
    /// </summary>
    /// <remarks>
    /// Значение должно быть больше <c>1.0</c>.
    /// По умолчанию: <c>1.1</c>.
    /// </remarks>
    public static readonly StyledProperty<double> ZoomStepProperty =
        AvaloniaProperty.Register<SurfaceInteractionOptions, double>(
            nameof(ZoomStep),
            1.1);

    /// <summary>
    /// Идентификатор свойства, включающего масштабирование щипком.
    /// </summary>
    /// <remarks>
    /// Щипок тачпада и двумя пальцами на сенсорном экране. Модификаторов он не
    /// спрашивает: <c>ZoomModifiers</c> отличают масштаб от прокрутки колесом, а щипок
    /// ни с чем не спутать. По умолчанию: <see langword="true"/>.
    /// </remarks>
    public static readonly StyledProperty<bool> IsPinchZoomEnabledProperty =
        AvaloniaProperty.Register<SurfaceInteractionOptions, bool>(
            nameof(IsPinchZoomEnabled),
            true);

    /// <summary>
    /// Идентификатор свойства, включающего автопрокрутку у края.
    /// </summary>
    /// <remarks>
    /// Перетаскивание и рамка, доведённые до края редактора, сдвигают холст сами, и
    /// жест продолжается за пределы видимого. По умолчанию: <see langword="true"/>.
    /// </remarks>
    public static readonly StyledProperty<bool> IsAutoPanEnabledProperty =
        AvaloniaProperty.Register<SurfaceInteractionOptions, bool>(
            nameof(IsAutoPanEnabled),
            true);

    /// <summary>
    /// Идентификатор свойства ширины полосы у края, в которой включается автопрокрутка.
    /// </summary>
    /// <remarks>
    /// В пикселях экрана, а не холста: полоса одинакова на любом масштабе.
    /// По умолчанию: <c>32.0</c>.
    /// </remarks>
    public static readonly StyledProperty<double> AutoPanEdgeProperty =
        AvaloniaProperty.Register<SurfaceInteractionOptions, double>(
            nameof(AutoPanEdge),
            32.0);

    /// <summary>
    /// Идентификатор свойства наибольшей скорости автопрокрутки.
    /// </summary>
    /// <remarks>
    /// В пикселях экрана в секунду — у самого края и за ним. В глубине полосы скорость
    /// растёт от нуля до этой величины. По умолчанию: <c>900.0</c>.
    /// </remarks>
    public static readonly StyledProperty<double> AutoPanSpeedProperty =
        AvaloniaProperty.Register<SurfaceInteractionOptions, double>(
            nameof(AutoPanSpeed),
            900.0);

    /// <summary>
    /// Идентификатор свойства порога старта перетаскивания в пикселях.
    /// </summary>
    /// <remarks>По умолчанию: <c>3.0</c>.</remarks>
    public static readonly StyledProperty<double> DragStartThresholdProperty =
        AvaloniaProperty.Register<SurfaceInteractionOptions, double>(
            nameof(DragStartThreshold),
            3.0);

    /// <summary>
    /// Идентификатор свойства минимального размера элемента при resize.
    /// </summary>
    /// <remarks>По умолчанию: <c>10.0</c>.</remarks>
    public static readonly StyledProperty<double> ResizeMinSizeProperty =
        AvaloniaProperty.Register<SurfaceInteractionOptions, double>(
            nameof(ResizeMinSize),
            10.0);

    /// <summary>
    /// Получает или задает коэффициент шага масштабирования колесом мыши.
    /// </summary>
    public double ZoomStep
    {
        get => GetValue(ZoomStepProperty);
        set => SetValue(ZoomStepProperty, value);
    }

    /// <summary>
    /// Получает или задает значение, включающее масштабирование щипком.
    /// </summary>
    public bool IsPinchZoomEnabled
    {
        get => GetValue(IsPinchZoomEnabledProperty);
        set => SetValue(IsPinchZoomEnabledProperty, value);
    }

    /// <summary>
    /// Получает или задает значение, включающее автопрокрутку у края.
    /// </summary>
    public bool IsAutoPanEnabled
    {
        get => GetValue(IsAutoPanEnabledProperty);
        set => SetValue(IsAutoPanEnabledProperty, value);
    }

    /// <summary>
    /// Получает или задает ширину полосы автопрокрутки у края в пикселях экрана.
    /// </summary>
    public double AutoPanEdge
    {
        get => GetValue(AutoPanEdgeProperty);
        set => SetValue(AutoPanEdgeProperty, value);
    }

    /// <summary>
    /// Получает или задает наибольшую скорость автопрокрутки в пикселях экрана в секунду.
    /// </summary>
    public double AutoPanSpeed
    {
        get => GetValue(AutoPanSpeedProperty);
        set => SetValue(AutoPanSpeedProperty, value);
    }

    /// <summary>
    /// Получает или задает порог старта перетаскивания в пикселях.
    /// </summary>
    public double DragStartThreshold
    {
        get => GetValue(DragStartThresholdProperty);
        set => SetValue(DragStartThresholdProperty, value);
    }

    /// <summary>
    /// Получает или задает минимальный размер элемента при resize.
    /// </summary>
    public double ResizeMinSize
    {
        get => GetValue(ResizeMinSizeProperty);
        set => SetValue(ResizeMinSizeProperty, value);
    }

    /// <summary>
    /// Идентификатор свойства привязки к сетке.
    /// </summary>
    public static readonly StyledProperty<bool> IsSnapToGridEnabledProperty =
        AvaloniaProperty.Register<SurfaceInteractionOptions, bool>(
            nameof(IsSnapToGridEnabled),
            true);

    /// <summary>
    /// Идентификатор свойства ограничения размера границами владеющего контейнера.
    /// </summary>
    public static readonly StyledProperty<bool> IsResizeContainedToParentProperty =
        AvaloniaProperty.Register<SurfaceInteractionOptions, bool>(
            nameof(IsResizeContainedToParent),
            true);

    /// <summary>
    /// Получает или задает признак ограничения изменения размера границами
    /// владеющего <see cref="SurfaceItem"/>.
    /// </summary>
    /// <remarks>
    /// Включено по умолчанию: в дизайнере форм контрол, вылезший за свою форму, —
    /// почти всегда ошибка ввода. Без ограничения он продолжает расти, форма его
    /// обрезает, и ручки выделения оказываются на пустом холсте.
    /// <para>
    /// Выключать имеет смысл там, где overflow задуман — например, бейдж или тень,
    /// намеренно выступающие за край карточки.
    /// </para>
    /// </remarks>
    public bool IsResizeContainedToParent
    {
        get => GetValue(IsResizeContainedToParentProperty);
        set => SetValue(IsResizeContainedToParentProperty, value);
    }

    /// <summary>
    /// Идентификатор свойства шага привязки.
    /// </summary>
    public static readonly StyledProperty<double> SnapStepProperty =
        AvaloniaProperty.Register<SurfaceInteractionOptions, double>(
            nameof(SnapStep),
            double.NaN);

    /// <summary>
    /// Получает или задает признак привязки к сетке при перетаскивании и изменении размера.
    /// </summary>
    /// <remarks>
    /// Привязка исправляет неточный ввод указателем и намеренно не влияет на смещение
    /// стрелками: там пользователь уже задал точную величину.
    /// </remarks>
    public bool IsSnapToGridEnabled
    {
        get => GetValue(IsSnapToGridEnabledProperty);
        set => SetValue(IsSnapToGridEnabledProperty, value);
    }

    /// <summary>
    /// Получает или задает шаг привязки в мировых единицах.
    /// </summary>
    /// <remarks>
    /// <see cref="double.NaN"/> означает «следовать за сеткой»: шаг берётся из
    /// <see cref="SurfaceGrid.CellSize"/> шаблона редактора. Так настройки
    /// не расходятся: сетка не может обещать одну структуру, а привязка давать другую.
    /// </remarks>
    public double SnapStep
    {
        get => GetValue(SnapStepProperty);
        set => SetValue(SnapStepProperty, value);
    }

    /// <summary>
    /// Идентификатор свойства привязки к направляющим.
    /// </summary>
    public static readonly StyledProperty<bool> IsSnapToGuidesEnabledProperty =
        AvaloniaProperty.Register<SurfaceInteractionOptions, bool>(
            nameof(IsSnapToGuidesEnabled),
            true);

    /// <summary>
    /// Идентификатор свойства подсказок о равных интервалах.
    /// </summary>
    public static readonly StyledProperty<bool> IsEqualSpacingEnabledProperty =
        AvaloniaProperty.Register<SurfaceInteractionOptions, bool>(
            nameof(IsEqualSpacingEnabled),
            true);

    /// <summary>
    /// Идентификатор свойства радиуса захвата направляющей, в пикселях экрана.
    /// </summary>
    public static readonly StyledProperty<double> SnapGuideToleranceProperty =
        AvaloniaProperty.Register<SurfaceInteractionOptions, double>(
            nameof(SnapGuideTolerance),
            6.0);

    /// <summary>
    /// Получает или задает признак выравнивания по краям и центрам соседей
    /// при перетаскивании.
    /// </summary>
    /// <remarks>
    /// Направляющая сильнее сетки на той оси, которую она заняла: сетка задаёт
    /// регулярность, а направляющая — отношение к конкретному соседу, и оно точнее.
    /// Ось, где направляющей не нашлось, по-прежнему идёт на сетку.
    /// </remarks>
    public bool IsSnapToGuidesEnabled
    {
        get => GetValue(IsSnapToGuidesEnabledProperty);
        set => SetValue(IsSnapToGuidesEnabledProperty, value);
    }

    /// <summary>
    /// Получает или задает радиус захвата направляющей в пикселях экрана.
    /// </summary>
    /// <remarks>
    /// Значение задано в пикселях экрана, а не в мировых единицах, и делится на
    /// <see cref="SurfaceView.ViewportZoom"/>. Иначе на отдалении направляющая
    /// хватала бы элемент с расстояния, на котором пользователь её не видит.
    /// </remarks>
    public double SnapGuideTolerance
    {
        get => GetValue(SnapGuideToleranceProperty);
        set => SetValue(SnapGuideToleranceProperty, value);
    }

    /// <summary>
    /// Получает или задает признак подсказок о равных интервалах.
    /// </summary>
    /// <remarks>
    /// Включено по умолчанию, как сетка и направляющие: выключенная подсказка,
    /// о которой надо знать, чтобы её включить, не помогает никому.
    /// <para>
    /// Радиус захвата общий с направляющими — <see cref="SnapGuideTolerance"/>:
    /// это одна и та же величина «насколько близко надо подвести».
    /// </para>
    /// </remarks>
    public bool IsEqualSpacingEnabled
    {
        get => GetValue(IsEqualSpacingEnabledProperty);
        set => SetValue(IsEqualSpacingEnabledProperty, value);
    }

    /// <summary>
    /// Идентификатор свойства шага смещения стрелками.
    /// </summary>
    public static readonly StyledProperty<double> NudgeStepProperty =
        AvaloniaProperty.Register<SurfaceInteractionOptions, double>(
            nameof(NudgeStep),
            1.0);

    /// <summary>
    /// Идентификатор свойства увеличенного шага смещения стрелками.
    /// </summary>
    public static readonly StyledProperty<double> LargeNudgeStepProperty =
        AvaloniaProperty.Register<SurfaceInteractionOptions, double>(
            nameof(LargeNudgeStep),
            10.0);

    /// <summary>
    /// Получает или задает шаг смещения выделения стрелками, в мировых единицах.
    /// </summary>
    public double NudgeStep
    {
        get => GetValue(NudgeStepProperty);
        set => SetValue(NudgeStepProperty, value);
    }

    /// <summary>
    /// Получает или задает шаг смещения стрелками с модификатором.
    /// </summary>
    /// <remarks>
    /// Модификатор задаётся через <see cref="SurfaceInputGestures.LargeNudgeModifiers"/>.
    /// Значение обычно кратно шагу сетки, чтобы крупное смещение попадало по ячейкам.
    /// </remarks>
    public double LargeNudgeStep
    {
        get => GetValue(LargeNudgeStepProperty);
        set => SetValue(LargeNudgeStepProperty, value);
    }
}
