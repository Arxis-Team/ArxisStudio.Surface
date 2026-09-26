using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Utilities;

namespace ArxisStudio.Surface;

// Настройка ввода: жесты, числовые параметры, курсоры и решения, которые из них следуют.
// Часть SurfaceView; общее описание типа — в SurfaceView.cs.
public partial class SurfaceView
{
    /// <summary>
    /// Идентификатор объекта с настройками input gestures редактора.
    /// </summary>
    public static readonly DirectProperty<SurfaceView, DesignEditorInputGestures> InputGesturesProperty =
        AvaloniaProperty.RegisterDirect<SurfaceView, DesignEditorInputGestures>(
            nameof(InputGestures),
            o => o.InputGestures,
            (o, v) => o.InputGestures = v);

    /// <summary>
    /// Идентификатор объекта с runtime-настройками взаимодействия редактора.
    /// </summary>
    public static readonly DirectProperty<SurfaceView, DesignEditorInteractionOptions> InteractionOptionsProperty =
        AvaloniaProperty.RegisterDirect<SurfaceView, DesignEditorInteractionOptions>(
            nameof(InteractionOptions),
            o => o.InteractionOptions,
            (o, v) => o.InteractionOptions = v);

    /// <summary>
    /// Идентификатор объекта с курсорами жестов редактора.
    /// </summary>
    public static readonly DirectProperty<SurfaceView, DesignEditorCursors> CursorsProperty =
        AvaloniaProperty.RegisterDirect<SurfaceView, DesignEditorCursors>(
            nameof(Cursors),
            o => o.Cursors,
            (o, v) => o.Cursors = v);

    /// <summary>
    /// Идентификатор модификаторов, принудительно переключающих взаимодействие на уровень контейнера.
    /// </summary>
    public static readonly DirectProperty<SurfaceView, KeyModifiers> ContainerInteractionModifiersProperty =
        AvaloniaProperty.RegisterDirect<SurfaceView, KeyModifiers>(
            nameof(ContainerInteractionModifiers),
            o => o.ContainerInteractionModifiers,
            (o, v) => o.ContainerInteractionModifiers = v);

    /// <summary>
    /// Идентификатор модификаторов additive selection.
    /// </summary>
    public static readonly DirectProperty<SurfaceView, KeyModifiers> AdditiveSelectionModifiersProperty =
        AvaloniaProperty.RegisterDirect<SurfaceView, KeyModifiers>(
            nameof(AdditiveSelectionModifiers),
            o => o.AdditiveSelectionModifiers,
            (o, v) => o.AdditiveSelectionModifiers = v);

    private DesignEditorInputGestures _inputGestures = new DesignEditorInputGestures();

    /// <summary>
    /// Получает или задает набор настраиваемых input gestures редактора.
    /// </summary>
    /// <remarks>
    /// Это основная точка конфигурации горячих клавиш и модификаторов взаимодействия.
    /// Свойство можно задавать из AXAML, styles, code-behind или через привязки.
    /// </remarks>
    public DesignEditorInputGestures InputGestures
    {
        get => _inputGestures;
        set
        {
            var gestures = value ?? new DesignEditorInputGestures();
            DetachInputGestures(_inputGestures);
            SetAndRaise(InputGesturesProperty, ref _inputGestures, gestures);
            AttachInputGestures(gestures);
            SetAndRaise(ContainerInteractionModifiersProperty, ref _containerInteractionModifiers, gestures.ContainerInteractionModifiers);
            SetAndRaise(AdditiveSelectionModifiersProperty, ref _additiveSelectionModifiers, gestures.AdditiveSelectionModifiers);
        }
    }

    /// <summary>
    /// Ретранслятор изменений набора жестов в плоские свойства редактора.
    /// </summary>
    /// <remarks>
    /// Отдельный объект нужен ради времени жизни. Плоские
    /// <see cref="ContainerInteractionModifiers"/> и <see cref="AdditiveSelectionModifiers"/>
    /// читают значение у набора и потому по чтению верны всегда — а уведомления держатся
    /// на подписке. Обычная подписка редактора на набор укладывает делегат в сам набор,
    /// то есть набор начинает держать редактор: набор, общий на несколько редакторов —
    /// а именно так его раздаёт один <c>Setter</c> в стиле, — не отпускал бы ни одного
    /// из них никогда. Замерено: пять редакторов на общем наборе переживали принудительную
    /// сборку все пять, на своём — ни одного.
    /// <para>
    /// Слабое событие Avalonia держит подписчика слабо, поэтому набор ссылается на мост
    /// слабо, мост на редактор — сильно, а редактор владеет мостом. Всё трое собираются
    /// вместе, и набор при этом не держит никого.
    /// </para>
    /// </remarks>
    private sealed class InputGestureBridge : IWeakEventSubscriber<AvaloniaPropertyChangedEventArgs>
    {
        private readonly SurfaceView _editor;

        public InputGestureBridge(SurfaceView editor) => _editor = editor;

        public void OnEvent(object? sender, WeakEvent ev, AvaloniaPropertyChangedEventArgs e) =>
            _editor.OnInputGesturesPropertyChanged(e);
    }

    private readonly InputGestureBridge _inputGestureBridge;

    private void AttachInputGestures(DesignEditorInputGestures gestures) =>
        WeakEvents.AvaloniaPropertyChanged.Subscribe(gestures, _inputGestureBridge);

    /// <summary>
    /// Отписывает редактор от заменённого набора.
    /// </summary>
    /// <remarks>
    /// Для времени жизни это уже не нужно — за него отвечает слабое событие. Отписка
    /// прекращает лишние вызовы от набора, которым редактор больше не пользуется;
    /// испортить значение они и так не могут, потому что ретранслятор читает текущий
    /// набор, а не отправителя.
    /// </remarks>
    private void DetachInputGestures(DesignEditorInputGestures gestures) =>
        WeakEvents.AvaloniaPropertyChanged.Unsubscribe(gestures, _inputGestureBridge);

    private void OnInputGesturesPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == DesignEditorInputGestures.ContainerInteractionModifiersProperty)
        {
            SetAndRaise(
                ContainerInteractionModifiersProperty,
                ref _containerInteractionModifiers,
                _inputGestures.ContainerInteractionModifiers);
        }
        else if (e.Property == DesignEditorInputGestures.AdditiveSelectionModifiersProperty)
        {
            SetAndRaise(
                AdditiveSelectionModifiersProperty,
                ref _additiveSelectionModifiers,
                _inputGestures.AdditiveSelectionModifiers);
        }
    }

    private DesignEditorInteractionOptions _interactionOptions = new DesignEditorInteractionOptions();

    /// <summary>
    /// Получает или задает runtime-настройки взаимодействия редактора, не относящиеся к жестам ввода.
    /// </summary>
    /// <remarks>
    /// В этом объекте настраиваются числовые параметры поведения, такие как шаг zoom,
    /// порог начала drag и минимальный размер при resize.
    /// </remarks>
    public DesignEditorInteractionOptions InteractionOptions
    {
        get => _interactionOptions;
        set
        {
            var options = value ?? new DesignEditorInteractionOptions();
            SetAndRaise(InteractionOptionsProperty, ref _interactionOptions, options);
        }
    }

    private DesignEditorCursors _cursors = new DesignEditorCursors();

    /// <summary>
    /// Получает или задает курсоры, которыми редактор показывает идущий жест.
    /// </summary>
    /// <remarks>
    /// Здесь настраиваются курсоры жестов, у которых нет своего элемента под указателем:
    /// перемещения, панорамирования, рамки, перестановки и переноса направляющей. Курсоры
    /// ручек изменения размера и линейки задаются ресурсами темы — их видно и на них наводят,
    /// поэтому курсор принадлежит самой части.
    /// <para>
    /// Значение читается на входе в жест, поэтому подписки на набор редактору не нужно:
    /// смена курсора посреди протяжки описывала бы жест, который уже идёт.
    /// </para>
    /// </remarks>
    public DesignEditorCursors Cursors
    {
        get => _cursors;
        set
        {
            var cursors = value ?? new DesignEditorCursors();
            SetAndRaise(CursorsProperty, ref _cursors, cursors);
        }
    }

    private KeyModifiers _containerInteractionModifiers = KeyModifiers.Control;

    /// <summary>
    /// Получает или задает модификаторы клавиатуры, которые принудительно переключают selection,
    /// drag и resize на уровень <see cref="SurfaceItem"/>.
    /// </summary>
    /// <remarks>
    /// Совместимое сокращенное свойство над <see cref="InputGestures"/>.
    /// Для нового кода рекомендуется использовать <see cref="InputGestures"/> напрямую.
    /// </remarks>
    public KeyModifiers ContainerInteractionModifiers
    {
        get => InputGestures.ContainerInteractionModifiers;
        // Запись идёт только в набор: уведомление плоского свойства поднимает
        // ретранслятор — уже после того, как источник правды обновлён. Своё
        // SetAndRaise здесь поднимало бы событие раньше записи, и подписчик,
        // перечитавший геттер, получал бы прежнее значение.
        set => InputGestures.ContainerInteractionModifiers = value;
    }

    private KeyModifiers _additiveSelectionModifiers = KeyModifiers.Shift;

    /// <summary>
    /// Получает или задает модификаторы additive selection.
    /// </summary>
    /// <remarks>
    /// Совместимое сокращенное свойство над <see cref="InputGestures"/>.
    /// Для нового кода рекомендуется использовать <see cref="InputGestures"/> напрямую.
    /// </remarks>
    public KeyModifiers AdditiveSelectionModifiers
    {
        get => InputGestures.AdditiveSelectionModifiers;
        // Запись идёт только в набор: уведомление плоского свойства поднимает
        // ретранслятор — уже после того, как источник правды обновлён. Своё
        // SetAndRaise здесь поднимало бы событие раньше записи, и подписчик,
        // перечитавший геттер, получал бы прежнее значение.
        set => InputGestures.AdditiveSelectionModifiers = value;
    }

    internal void SetLastInputModifiers(KeyModifiers modifiers)
    {
        LastInputModifiers = modifiers;
    }

    internal bool ShouldUseContainerInteraction(KeyModifiers modifiers)
    {
        var requiredModifiers = InputGestures.ContainerInteractionModifiers;
        return requiredModifiers != KeyModifiers.None && modifiers.HasFlag(requiredModifiers);
    }

    internal bool ShouldUseAdditiveSelection(KeyModifiers modifiers)
    {
        var requiredModifiers = InputGestures.AdditiveSelectionModifiers;
        return requiredModifiers != KeyModifiers.None && modifiers.HasFlag(requiredModifiers);
    }

    internal bool ShouldStartPan(PointerPointProperties pointerProperties, KeyModifiers modifiers)
    {
        return MatchesModifiers(modifiers, InputGestures.PanModifiers)
               && IsPointerButtonPressed(pointerProperties, InputGestures.PanButton);
    }

    internal bool ShouldStartMarquee(PointerPointProperties pointerProperties, KeyModifiers modifiers)
    {
        return MatchesModifiers(modifiers, InputGestures.MarqueeModifiers)
               && IsPointerButtonPressed(pointerProperties, InputGestures.MarqueeButton);
    }

    internal bool ShouldHandleZoom(KeyModifiers modifiers)
    {
        return MatchesModifiers(modifiers, InputGestures.ZoomModifiers);
    }

    private protected static bool MatchesModifiers(KeyModifiers actual, KeyModifiers required)
    {
        return required == KeyModifiers.None || actual.HasFlag(required);
    }

    private static bool IsPointerButtonPressed(PointerPointProperties pointerProperties, DesignEditorPointerButton button)
    {
        return button switch
        {
            DesignEditorPointerButton.Left => pointerProperties.IsLeftButtonPressed,
            DesignEditorPointerButton.Middle => pointerProperties.IsMiddleButtonPressed,
            DesignEditorPointerButton.Right => pointerProperties.IsRightButtonPressed,
            _ => false
        };
    }

    internal KeyModifiers LastInputModifiers { get; private protected set; }

    private const double ZoomTolerance = 0.0001;

    private protected Point _lastMousePosition;
}
