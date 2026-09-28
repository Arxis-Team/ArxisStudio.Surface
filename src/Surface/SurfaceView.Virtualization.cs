using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace ArxisStudio.Surface;

// Виртуализация: что держит контейнеры развёрнутыми (ADR 0007), упрощённый вид при малом масштабе
// (ADR 0008), выбор без контейнеров и его публикация (ADR 0010). Часть SurfaceView; общее описание
// типа — в SurfaceView.cs.
public partial class SurfaceView
{
    /// <summary>
    /// Идентификатор свойства <see cref="SimplifiedZoom"/>.
    /// </summary>
    public static readonly StyledProperty<double> SimplifiedZoomProperty =
        AvaloniaProperty.Register<SurfaceView, double>(nameof(SimplifiedZoom), 0.5);

    /// <summary>
    /// Идентификатор свойства <see cref="IsSimplified"/>.
    /// </summary>
    public static readonly DirectProperty<SurfaceView, bool> IsSimplifiedProperty =
        AvaloniaProperty.RegisterDirect<SurfaceView, bool>(nameof(IsSimplified), o => o.IsSimplified);

    private int _realizationHolds;
    private bool _isSimplified;
    private object? _pressedItem;
    private ISelectionModel? _watchedSelection;
    private int _containerChanges;
    private bool _realizedSelectionChanged;
    private int _selectionWrites;
    private long _publishedSelection;

    /// <summary>
    /// Получает или задает масштаб, ниже которого элементы показываются упрощённо, без контейнеров.
    /// </summary>
    /// <remarks>
    /// ADR 0008. Ниже порога виртуализирующая панель не разворачивает ничего, кроме закреплённого, —
    /// главного выбранного, контейнера с фокусом, нажатого и элемента, который сам
    /// <see cref="SurfaceItem"/>. Ноль и
    /// меньше выключают упрощённый вид. Действует только с <see cref="ItemLocationBinding"/>: без неё
    /// положение свёрнутого элемента взять неоткуда, и развёрнуто всё.
    /// </remarks>
    public double SimplifiedZoom
    {
        get => GetValue(SimplifiedZoomProperty);
        set => SetValue(SimplifiedZoomProperty, value);
    }

    /// <summary>
    /// Получает признак упрощённого вида: задана <see cref="ItemLocationBinding"/>, а масштаб ниже
    /// <see cref="SimplifiedZoom"/>.
    /// </summary>
    /// <remarks>
    /// Сворачивает контейнеры виртуализирующая панель; панель хоста, которая не виртуализирует,
    /// разворачивает всё и при нём.
    /// </remarks>
    public bool IsSimplified
    {
        get => _isSimplified;
        private set => SetAndRaise(IsSimplifiedProperty, ref _isSimplified, value);
    }

    /// <summary>
    /// Элемент, развёрнутый нажатием в упрощённом виде: закреплён до следующего нажатия мимо него или
    /// выхода из упрощённого вида.
    /// </summary>
    /// <remarks>
    /// Без закрепления контейнер, развёрнутый правой кнопкой, ушёл бы в пул на ближайшей мере, а цель
    /// контекстного меню, открытого по нему, осталась бы у переготовленного контейнера.
    /// </remarks>
    internal object? PressedItem => _pressedItem;

    /// <summary>
    /// Возникает, когда сменилось свёрнутое — геометрия элемента без контейнера, коллекция или состав
    /// развёрнутых: слою упрощённого вида пора собрать карточки заново (ADR 0008).
    /// </summary>
    internal event EventHandler? SimplifiedContentChanged;

    /// <summary>
    /// Сообщает, что свёрнутое сменилось.
    /// </summary>
    internal void OnSimplifiedContentChanged() => SimplifiedContentChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Держит развёрнутым всё, что развёрнуто, пока удержание не освобождено.
    /// </summary>
    /// <remarks>
    /// Для жестов, которые снимают контейнеры на входе и читают снимок до конца, как рамка: свёрнутый
    /// посреди жеста контейнер ушёл бы в пул или достался другому элементу, а снимок продолжал бы
    /// считать его прежним. Въехавшее в окно разворачивается и под удержанием; свёрнуто лишнее будет
    /// на ближайшей мере после освобождения.
    /// </remarks>
    /// <returns>Удержание; освобождается один раз.</returns>
    internal IDisposable HoldRealization()
    {
        _realizationHolds++;
        return new RealizationHold(this);
    }

    /// <summary>
    /// Держит ли жест развёрнутое.
    /// </summary>
    internal bool IsRealizationHeld => _realizationHolds > 0;

    /// <summary>
    /// Разворачивает элементы, чей прямоугольник пересекает <paramref name="bounds"/>, — до того, как
    /// рамка выберет контейнеры.
    /// </summary>
    /// <remarks>
    /// Свёрнутый элемент известен панели прямоугольником с предполагаемым размером, пока ни разу не
    /// показывался, поэтому рамку он встречает по нему.
    /// </remarks>
    internal void RealizeWithin(Rect bounds)
    {
        if (ItemsPanelRoot is VirtualizingSurfacePanel { IsVirtualizing: true } panel)
            panel.RealizeWithin(bounds);
    }

    /// <summary>
    /// Контейнер элемента: развёрнутый сейчас или развёрнутый ради этого вызова.
    /// </summary>
    /// <returns>Контейнер или <see langword="null"/>, если элемента в коллекции нет.</returns>
    internal Control? RealizeItem(object? item)
    {
        var index = ItemsView.IndexOf(item);
        if (index < 0)
            return null;

        return ContainerFromIndex(index) ?? (ItemsPanelRoot as VirtualizingSurfacePanel)?.RealizeNow(index);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SelectionProperty)
            WatchSelection(change.GetNewValue<ISelectionModel?>());
        else if (change.Property == ItemAccentBindingProperty)
        {
            // Свёрнутым полосу перечитывает панель, развёрнутым — привязка у контейнера.
            foreach (var container in GetRealizedContainers())
            {
                if (container is SurfaceItem item && !ReferenceEquals(ItemFromContainer(item), item))
                    BindAccent(item);
            }
        }
        else if (change.Property == ViewportZoomProperty
                 || change.Property == SimplifiedZoomProperty
                 || change.Property == ItemLocationBindingProperty)
        {
            IsSimplified = ItemLocationBinding != null && SimplifiedZoom > 0 && ViewportZoom < SimplifiedZoom;
            if (!IsSimplified)
                _pressedItem = null;
        }
    }

    /// <summary>
    /// Нажатие по карточке в упрощённом виде разворачивает её элемент и отдаёт нажатие его контейнеру.
    /// </summary>
    /// <remarks>
    /// ADR 0008. Туннелем, раньше всех, кто решает по нажатию мимо контейнеров: рамки ядра и попадания
    /// по связям в редакторе узлов — узел лежит поверх связи и в упрощённом виде. Разворачивается
    /// верхний свёрнутый элемент под точкой; левую кнопку дальше ведёт машина контейнера — захват,
    /// выбор, перетаскивание, двойной щелчок, — правую — контекст ядра, который находит развёрнутый
    /// контейнер по точке. Нажатие панорамы и нажатие по контейнеру не трогаются.
    /// </remarks>
    private void OnSimplifiedPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsSimplified || e.Handled || ItemsPanelRoot is not VirtualizingSurfacePanel panel)
            return;

        var point = e.GetCurrentPoint(this);
        var properties = point.Properties;
        if (!properties.IsLeftButtonPressed && !properties.IsRightButtonPressed)
            return;

        if (ShouldStartPan(properties, e.KeyModifiers)
            || (e.Source as Visual)?.FindAncestorOfType<SurfaceItem>(includeSelf: true) != null)
        {
            return;
        }

        var container = panel.RealizeAt(GetWorldPosition(point.Position));
        SetPressedItem(container == null ? null : ItemFromContainer(container), panel);

        if (container is SurfaceItem item && properties.IsLeftButtonPressed)
            item.CurrentState.OnPointerPressed(e);
    }

    /// <summary>
    /// Лежит ли под точкой карточка упрощённого вида — для слоёв выше, которым элемент без контейнера
    /// закрывает своё, как узел закрывает связь под ним.
    /// </summary>
    internal bool IsOverSimplifiedItem(Point world) =>
        IsSimplified && ItemsPanelRoot is VirtualizingSurfacePanel panel && panel.IsCollapsedAt(world);

    private void SetPressedItem(object? item, VirtualizingSurfacePanel panel)
    {
        if (ReferenceEquals(_pressedItem, item))
            return;

        // Прежний нажатый свернётся на ближайшей мере, если его не держит ничто другое.
        _pressedItem = item;
        panel.InvalidateMeasure();
    }

    private void ForgetPressedItem(NotifyCollectionChangedEventArgs e)
    {
        if (_pressedItem != null
            && (e.Action == NotifyCollectionChangedAction.Reset || e.OldItems?.Contains(_pressedItem) == true))
        {
            _pressedItem = null;
        }
    }

    /// <summary>
    /// Держит ли выбор контейнер элемента развёрнутым.
    /// </summary>
    /// <remarks>
    /// ADR 0010. Выбор — данные индексного слоя, а слой target'ов описывает развёрнутое: выбранный
    /// элемент за окном остаётся выбранным без контейнера. Держат главный выбранный —
    /// <see cref="PrimarySelectionTarget"/> развёрнут всегда — и выбранный не целиком, с вложенным
    /// target'ом: вложенный target — контрол, и без контейнера его нет.
    /// </remarks>
    internal bool KeepsSelectedRealized(int index, Control container)
    {
        if (!Selection.IsSelected(index))
            return false;

        if (container is not SurfaceItem item)
            return true;

        // Главный — опубликованный: выбор хостом слоя target'ов не пишет, а главного называет снимок.
        if (_primarySelectionTarget is { } primary && IsOwnedByContainer(primary.Target, item))
            return true;

        foreach (var target in _selectedTargets)
        {
            if (target is not SurfaceItem && IsOwnedByContainer(target, item))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Открывает смену контейнеров панелью — разворачивание и сворачивание: выбор, который они
    /// задевают, публикуется в конце одним снимком и без события.
    /// </summary>
    /// <remarks>
    /// Выбор при этом не меняется, меняется только то, что у него развёрнуто: событие выделения —
    /// о выборе, а не о панораме (ADR 0010). Без этого каждый выбранный, въехавший в окно, отмечал бы
    /// выбор на контейнере и пересобирал снимок сам.
    /// </remarks>
    /// <returns>Смена; закрывается один раз.</returns>
    internal ContainerChange BeginContainerChange()
    {
        _containerChanges++;
        return new ContainerChange(this);
    }

    /// <summary>
    /// Выбор отметился на контейнере или выбранный контейнер сдвинулся: пересобрать снимок — сейчас или
    /// в конце идущей записи выбора и смены контейнеров.
    /// </summary>
    internal void OnSelectedContainerChanged()
    {
        if (_selectionWrites > 0)
            return;

        if (_containerChanges > 0)
        {
            _realizedSelectionChanged = true;
            return;
        }

        RefreshSelectionOverlay();
    }

    /// <summary>
    /// Открывает запись выбора поверхностью: выбор отметится на контейнерах, и отмечающие не
    /// пересобирают снимок — его соберёт пишущий, один раз, когда закончит.
    /// </summary>
    /// <returns>Запись; закрывается один раз.</returns>
    private protected SelectionWrite WriteSelection()
    {
        _selectionWrites++;
        return new SelectionWrite(this);
    }

    /// <summary>
    /// Выбранные элементы без контейнера: индекс, элемент и прямоугольник в мировых координатах.
    /// </summary>
    internal List<CollapsedSelected> CollapsedSelection()
    {
        var result = new List<CollapsedSelected>();
        if (ItemsPanelRoot is not VirtualizingSurfacePanel { IsVirtualizing: true } panel)
            return result;

        foreach (var index in Selection.SelectedIndexes)
        {
            if (ContainerFromIndex(index) == null && panel.TryGetCollapsedBounds(index, out var bounds))
                result.Add(new CollapsedSelected(index, ItemsView[index], bounds));
        }

        return result;
    }

    /// <summary>
    /// Двигает выбранный элемент без контейнера записью в модель и пишет сдвиг в открытую единицу
    /// правки (ADR 0010).
    /// </summary>
    /// <returns><see langword="false"/>, если модель записи не приняла: привязка положения односторонняя.</returns>
    internal bool MoveCollapsed(CollapsedSelected selected, Point location)
    {
        if (ItemsPanelRoot is not VirtualizingSurfacePanel panel || !panel.TryMoveCollapsed(selected.Index, location))
            return false;

        if (selected.Item != null && !_suppressEditRecording)
            _activeEdit?.RecordItemPosition(selected.Item, selected.Bounds.Position, location);

        return true;
    }

    /// <summary>
    /// Разворачивает выбранный элемент, чья модель не приняла сдвиг, — он поедет прежним путём, швом
    /// записи контейнера.
    /// </summary>
    /// <returns>Target взаимодействия контейнера или <see langword="null"/>.</returns>
    internal Control? RealizeForMove(CollapsedSelected selected) =>
        (ItemsPanelRoot as VirtualizingSurfacePanel)?.RealizeNow(selected.Index) is SurfaceItem container
            ? ResolveInteractionTarget(container)
            : null;

    /// <summary>
    /// Ставит свёрнутый элемент в положение записью в модель — для отмены и повтора сдвига, который не
    /// разворачивает элемент (ADR 0010).
    /// </summary>
    /// <returns><see langword="false"/>, если элемент развёрнут, его нет или модель записи не приняла.</returns>
    internal bool TryMoveCollapsedItem(object? item, Point location)
    {
        if (ItemsPanelRoot is not VirtualizingSurfacePanel panel)
            return false;

        var index = panel.CollapsedIndexOf(item);
        return index >= 0 && panel.TryMoveCollapsed(index, location);
    }

    /// <summary>
    /// Сдвигает выбранные без контейнера на дельту — стрелками; не принявших запись разворачивает.
    /// </summary>
    private void MoveCollapsedSelection(Vector delta)
    {
        foreach (var selected in CollapsedSelection())
        {
            var location = selected.Bounds.Position + delta;
            if (!MoveCollapsed(selected, location) && RealizeForMove(selected) is { } target)
                SetTargetPosition(target, location);
        }
    }

    /// <summary>
    /// Публикует снимок выделения без события: сменилось только развёрнутое.
    /// </summary>
    private void PublishRealizedSelection()
    {
        CleanupSelectionTargets();
        var primary = _selectedTargets.Count > 0 ? _selectedTargets[0] : null;
        var primaryItem = primary as SurfaceItem ?? (primary != null ? FindSurfaceHost(primary) : null);
        ApplySelectionSnapshot(CreateSelectionTargetsSnapshot(primaryItem, primary), realizationOnly: true);
    }

    private void WatchSelection(ISelectionModel? model)
    {
        if (ReferenceEquals(_watchedSelection, model))
            return;

        if (_watchedSelection != null)
            _watchedSelection.SelectionChanged -= OnSelectionModelChanged;

        _watchedSelection = model;
        if (model != null)
            model.SelectionChanged += OnSelectionModelChanged;
    }

    /// <summary>
    /// Выбор мимо контейнеров — хостом или «выбрать всё» — индексный слой отмечает только на
    /// развёрнутых, и до слоя target'ов он доходит только отсюда.
    /// </summary>
    private void OnSelectionModelChanged(object? sender, SelectionModelSelectionChangedEventArgs e)
    {
        if (ItemsPanelRoot is not VirtualizingSurfacePanel { IsVirtualizing: true } panel)
            return;

        // Снятые с выбора отпускают свои контейнеры на ближайшей мере, а не на ближайшей панораме.
        if (e.DeselectedIndexes.Count > 0)
            panel.InvalidateMeasure();

        // Выбранная карточка рисуется рамкой выбора: её слою пора собраться заново.
        if (IsSimplified)
            OnSimplifiedContentChanged();

        // Выбор свёрнутых на контейнерах не отмечается, и отметка его не опубликует: событие
        // выделения обязано прийти и тогда (ADR 0010). Пишущая поверхность соберёт снимок сама.
        if (_selectionWrites == 0 && (TouchesCollapsed(e.SelectedIndexes) || TouchesCollapsed(e.DeselectedIndexes)))
            RefreshSelectionOverlay();
    }

    /// <summary>
    /// Отпечаток выбора — какие элементы выбраны, без порядка: по нему публикация отличает смену
    /// выбора от смены развёрнутого (ADR 0010).
    /// </summary>
    /// <remarks>
    /// По элементам, а не по индексам: удаление невыбранного сдвигает индексы, а выбор не меняет.
    /// Сумма смешанных хешей от порядка не зависит.
    /// </remarks>
    private long SelectionSignature()
    {
        var items = SelectedItems;
        if (items == null)
            return 0;

        long signature = items.Count;
        foreach (var item in items)
        {
            var mixed = unchecked((ulong)(uint)(item == null ? 0 : RuntimeHelpers.GetHashCode(item)) * 0x9E3779B97F4A7C15UL);
            signature = unchecked(signature + (long)(mixed ^ (mixed >> 29)));
        }

        return signature;
    }

    private bool TouchesCollapsed(IReadOnlyList<int> indexes)
    {
        foreach (var index in indexes)
        {
            if (ContainerFromIndex(index) == null)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Выбранный элемент без контейнера.
    /// </summary>
    /// <param name="Index">Индекс в коллекции.</param>
    /// <param name="Item">Элемент.</param>
    /// <param name="Bounds">Прямоугольник в мировых координатах на момент чтения.</param>
    internal readonly record struct CollapsedSelected(int Index, object? Item, Rect Bounds);

    /// <summary>
    /// Смена контейнеров панелью: закрытие последней публикует развёрнутое выбранное.
    /// </summary>
    internal readonly struct ContainerChange(SurfaceView view) : IDisposable
    {
        public void Dispose()
        {
            if (--view._containerChanges > 0 || !view._realizedSelectionChanged)
                return;

            view._realizedSelectionChanged = false;
            view.PublishRealizedSelection();
        }
    }

    /// <summary>
    /// Запись выбора поверхностью; снимок собирает пишущий.
    /// </summary>
    private protected readonly struct SelectionWrite(SurfaceView view) : IDisposable
    {
        public void Dispose() => view._selectionWrites--;
    }

    private sealed class RealizationHold(SurfaceView view) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released)
                return;

            _released = true;
            if (--view._realizationHolds == 0)
                view.ItemsPanelRoot?.InvalidateMeasure();
        }
    }
}
