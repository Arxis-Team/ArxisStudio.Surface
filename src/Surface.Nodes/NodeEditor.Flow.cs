using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Data;
using Avalonia.Metadata;

namespace ArxisStudio.Surface.Nodes;

// Поток по проводам (ADR 0014): маркеры направления и импульсы отладки, как пузыри на проводах
// исполнения Blueprint. Часть NodeEditor; общее описание типа — в NodeEditor.cs.
public partial class NodeEditor
{
    /// <summary>
    /// Идентификатор свойства <see cref="IsLinkPulseEnabled"/>.
    /// </summary>
    public static readonly StyledProperty<bool> IsLinkPulseEnabledProperty =
        AvaloniaProperty.Register<NodeEditor, bool>(nameof(IsLinkPulseEnabled), true);

    /// <summary>
    /// Идентификатор свойства <see cref="AreLinkMarkersVisible"/>.
    /// </summary>
    public static readonly StyledProperty<bool> AreLinkMarkersVisibleProperty =
        AvaloniaProperty.Register<NodeEditor, bool>(nameof(AreLinkMarkersVisible), true);

    /// <summary>
    /// Идентификатор свойства <see cref="LinkMarker"/>.
    /// </summary>
    public static readonly StyledProperty<LinkMarker?> LinkMarkerProperty =
        AvaloniaProperty.Register<NodeEditor, LinkMarker?>(nameof(LinkMarker));

    /// <summary>
    /// Идентификатор свойства <see cref="LinkMarkerBinding"/>.
    /// </summary>
    public static readonly StyledProperty<BindingBase?> LinkMarkerBindingProperty =
        AvaloniaProperty.Register<NodeEditor, BindingBase?>(nameof(LinkMarkerBinding));

    /// <summary>
    /// Идентификатор свойства <see cref="LinkCurve"/>.
    /// </summary>
    public static readonly StyledProperty<LinkCurve?> LinkCurveProperty =
        AvaloniaProperty.Register<NodeEditor, LinkCurve?>(nameof(LinkCurve));

    /// <summary>
    /// Идентификатор свойства <see cref="LinkPulse"/>.
    /// </summary>
    public static readonly StyledProperty<LinkPulse?> LinkPulseProperty =
        AvaloniaProperty.Register<NodeEditor, LinkPulse?>(nameof(LinkPulse));

    // Вид импульса без заданного: свой у каждого редактора — общий объект в умолчании свойства правка
    // одного хоста меняла бы у всех.
    private readonly LinkPulse _defaultPulse = new();

    // Горящие импульсы по записи связи: когда зажжён последний раз, с каким видом и когда начался бег
    // фигур — продление его не сбивает.
    private readonly Dictionary<LinkRecord, ActivePulse> _pulses = new();

    private static readonly Stopwatch PulseStopwatch = Stopwatch.StartNew();

    /// <summary>
    /// Получает или задает, горят ли импульсы на проводах. По умолчанию — да.
    /// </summary>
    /// <remarks>
    /// Выключатель отладки: выключенный редактор <see cref="PulseLink(object, LinkPulse?)"/> не
    /// слушает, а горящие импульсы гасит сразу, — анимация не отвлекает, когда проверяют саму игру или
    /// приложение. Хост связывает его со своей настройкой, в том числе с просьбой человека о меньшем
    /// движении на экране.
    /// </remarks>
    public bool IsLinkPulseEnabled
    {
        get => GetValue(IsLinkPulseEnabledProperty);
        set => SetValue(IsLinkPulseEnabledProperty, value);
    }

    /// <summary>
    /// Получает или задает, показываются ли маркеры на проводах. По умолчанию — да.
    /// </summary>
    /// <remarks>
    /// Выключает маркеры, не трогая их настройку — <see cref="LinkMarker"/> и
    /// <see cref="LinkMarkerBinding"/>: включённые снова, они вернутся теми же.
    /// </remarks>
    public bool AreLinkMarkersVisible
    {
        get => GetValue(AreLinkMarkersVisibleProperty);
        set => SetValue(AreLinkMarkersVisibleProperty, value);
    }

    /// <summary>
    /// Получает или задает маркер для всех проводов. По умолчанию маркеров нет.
    /// </summary>
    /// <remarks>
    /// Действует, пока не задана <see cref="LinkMarkerBinding"/>: с ней маркер у каждой связи свой.
    /// </remarks>
    public LinkMarker? LinkMarker
    {
        get => GetValue(LinkMarkerProperty);
        set => SetValue(LinkMarkerProperty, value);
    }

    /// <summary>
    /// Получает или задает привязку, дающую маркер связи по её модели.
    /// </summary>
    /// <remarks>
    /// Применяется к элементу <see cref="Links"/>; ответ — <see cref="Nodes.LinkMarker"/> или
    /// <see langword="null"/>, у такой связи маркера нет. Так провод исполнения несёт стрелки, а провод
    /// данных — ничего, как в Blueprint. Смену ответа редактор читает по <c>PropertyChanged</c> модели,
    /// как цвет провода. Задана привязка — <see cref="LinkMarker"/> не действует.
    /// </remarks>
    [AssignBinding]
    [InheritDataTypeFromItems(nameof(Links))]
    public BindingBase? LinkMarkerBinding
    {
        get => GetValue(LinkMarkerBindingProperty);
        set => SetValue(LinkMarkerBindingProperty, value);
    }

    /// <summary>
    /// Получает или задает вид импульса по умолчанию — для вызова <see cref="PulseLink(object, LinkPulse?)"/>
    /// без своего. Без значения — вид Blueprint: круги, 192 единицы в секунду через 64, секунда и
    /// угасание за 400 мс, кисти темы.
    /// </summary>
    public LinkPulse? LinkPulse
    {
        get => GetValue(LinkPulseProperty);
        set => SetValue(LinkPulseProperty, value);
    }

    /// <summary>
    /// Получает или задает изгиб проводов. Без значения — правило и числа Blueprint (ADR 0015).
    /// </summary>
    /// <remarks>
    /// Новое значение пересчитывает кривые всех связей; правка полей уже заданного объекта — нет.
    /// </remarks>
    public LinkCurve? LinkCurve
    {
        get => GetValue(LinkCurveProperty);
        set => SetValue(LinkCurveProperty, value);
    }

    /// <summary>
    /// Сколько импульсов горит сейчас — для тестов и стенда.
    /// </summary>
    internal int ActivePulses => _pulses.Count;

    /// <summary>
    /// Часы импульсов; тест подменяет их: в безголовом режиме время стоит.
    /// </summary>
    internal Func<TimeSpan> PulseClock { get; set; } = static () => PulseStopwatch.Elapsed;

    /// <summary>
    /// Возникает, когда сменилось то, что рисует слой потока: кривая, состав или состояние связей,
    /// маркеры, импульсы.
    /// </summary>
    internal event EventHandler? LinkFlowChanged;

    /// <summary>
    /// Зажигает импульс на проводе — провод сработал.
    /// </summary>
    /// <remarks>
    /// Провод вспыхивает, и по нему от выхода ко входу бегут фигуры, пока импульс не погаснет. Вызов по
    /// горящей связи продлевает импульс с начала его жизни, не сбивая бег фигур. Элемент ищется в
    /// <see cref="Links"/>; связи там нет — вызов ничего не делает. Выключенный
    /// <see cref="IsLinkPulseEnabled"/> — тоже.
    /// </remarks>
    /// <param name="link">Элемент коллекции связей.</param>
    /// <param name="pulse">Вид этого импульса; без него — <see cref="LinkPulse"/> редактора.</param>
    /// <returns><see langword="true"/>, если импульс зажжён или продлён.</returns>
    public bool PulseLink(object link, LinkPulse? pulse = null)
    {
        ArgumentNullException.ThrowIfNull(link);

        if (!IsLinkPulseEnabled || RecordOf(link) is not { } record)
            return false;

        var now = PulseClock();
        var style = pulse ?? LinkPulse ?? _defaultPulse;
        _pulses[record] = _pulses.TryGetValue(record, out var active)
            ? active with { Lit = now, Style = style }
            : new ActivePulse(now, now, style);

        OnLinkFlowChanged();
        return true;
    }

    /// <summary>
    /// Гасит все импульсы сразу — например, когда остановили отлаживаемую машину.
    /// </summary>
    public void ClearLinkPulses()
    {
        if (_pulses.Count == 0)
            return;

        _pulses.Clear();
        OnLinkFlowChanged();
    }

    /// <summary>
    /// Маркер связи: из привязки, если она задана, иначе редактора; <see langword="null"/> — маркеров нет
    /// или они выключены.
    /// </summary>
    internal LinkMarker? MarkerOf(LinkRecord record) =>
        !AreLinkMarkersVisible ? null
        : LinkMarkerBinding != null ? record.Marker
        : LinkMarker;

    /// <summary>
    /// Горят ли маркеры хоть у одной связи — иначе слою незачем обходить записи.
    /// </summary>
    internal bool HasMarkers => AreLinkMarkersVisible && (LinkMarkerBinding != null || LinkMarker != null);

    /// <summary>
    /// Горящие импульсы на момент <paramref name="now"/>: погасшие и ушедшие из коллекции снимаются.
    /// </summary>
    internal IReadOnlyList<(LinkRecord Record, ActivePulse Pulse)> LivePulses(TimeSpan now)
    {
        List<LinkRecord>? over = null;
        var live = new List<(LinkRecord, ActivePulse)>(_pulses.Count);
        foreach (var (record, pulse) in _pulses)
        {
            if (pulse.Style.IsOver(now - pulse.Lit) || !ReferenceEquals(RecordOf(record.Item), record))
                (over ??= new List<LinkRecord>()).Add(record);
            else
                live.Add((record, pulse));
        }

        if (over != null)
        {
            foreach (var record in over)
                _pulses.Remove(record);
        }

        return live;
    }

    /// <summary>
    /// Записи, чья рамка пересекает прямоугольник мира, — по ячейкам окна связей, каждая один раз.
    /// </summary>
    internal void CollectLinksWithin(Rect world, List<LinkRecord> into)
    {
        EnsureLinkCells();
        var seen = new HashSet<LinkRecord>();
        var (c0, r0, c1, r1) = LinkCellsOf(world);
        for (var y = r0; y <= r1; y++)
        {
            for (var x = c0; x <= c1; x++)
            {
                if (!_linkCells.TryGetValue((x, y), out var cell))
                    continue;

                foreach (var record in cell)
                {
                    if (record.IsResolved && world.Intersects(record.WorldBounds) && seen.Add(record))
                        into.Add(record);
                }
            }
        }
    }

    private void OnLinkFlowChanged() => LinkFlowChanged?.Invoke(this, EventArgs.Empty);

    private void OnFlowPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == IsLinkPulseEnabledProperty)
        {
            if (!change.GetNewValue<bool>())
                ClearLinkPulses();
        }
        else if (change.Property == AreLinkMarkersVisibleProperty || change.Property == LinkMarkerProperty)
        {
            OnLinkFlowChanged();
        }
        else if (change.Property == LinkCurveProperty)
        {
            RefreshAllLinks();
        }
    }

    /// <summary>
    /// Горящий импульс: когда начался бег фигур, когда зажжён последний раз и каким видом.
    /// </summary>
    internal readonly record struct ActivePulse(TimeSpan Started, TimeSpan Lit, LinkPulse Style);
}
