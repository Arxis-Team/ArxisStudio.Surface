using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Selection;

namespace ArxisStudio.Surface;

// Виртуализация: что держит контейнеры развёрнутыми и разворачивание выбранного до сборки снимка
// выделения (ADR 0007), упрощённый вид при малом масштабе (ADR 0008). Часть SurfaceView; общее
// описание типа — в SurfaceView.cs.
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
    private bool _realizingSelection;
    private bool _isSimplified;
    private ISelectionModel? _watchedSelection;

    /// <summary>
    /// Получает или задает масштаб, ниже которого элементы показываются упрощённо, без контейнеров.
    /// </summary>
    /// <remarks>
    /// ADR 0008. Ниже порога виртуализирующая панель не разворачивает ничего, кроме закреплённого, —
    /// выбранных, контейнера с фокусом и элемента, который сам <see cref="SurfaceItem"/>. Ноль и
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
        else if (change.Property == ViewportZoomProperty
                 || change.Property == SimplifiedZoomProperty
                 || change.Property == ItemLocationBindingProperty)
            IsSimplified = ItemLocationBinding != null && SimplifiedZoom > 0 && ViewportZoom < SimplifiedZoom;
    }

    /// <summary>
    /// Разворачивает выбранные элементы, у которых нет контейнера.
    /// </summary>
    /// <remarks>
    /// Единая точка перед сборкой снимка выделения: слой target'ов держит контейнеры, и выбранное
    /// хостом, рамкой за краем или «выбрать всё» обязано их получить раньше, чем снимок уйдёт наружу, —
    /// иначе первым событием ушло бы выделение без свёрнутых, а вторым — полное. Разворачивание само
    /// отмечает выбор на новых контейнерах и зовёт пересборку ещё раз; эти вложенные вызовы
    /// пропускаются, снимок соберёт тот, кто начал.
    /// </remarks>
    /// <returns>
    /// <see langword="false"/>, если разворачивание уже идёт выше по стеку и снимок собирать рано.
    /// </returns>
    private protected bool RealizeSelectedItems()
    {
        if (_realizingSelection)
            return false;

        if (ItemsPanelRoot is not VirtualizingSurfacePanel { IsVirtualizing: true } panel)
            return true;

        _realizingSelection = true;
        try
        {
            // Снимком: подготовка контейнера вправе тронуть выбор.
            foreach (var index in Selection.SelectedIndexes.ToArray())
            {
                if (ContainerFromIndex(index) == null)
                    panel.RealizeNow(index);
            }
        }
        finally
        {
            _realizingSelection = false;
        }

        return true;
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

        foreach (var index in e.SelectedIndexes)
        {
            if (ContainerFromIndex(index) != null)
                continue;

            RefreshSelectionOverlay();
            return;
        }
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
