using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Selection;

namespace ArxisStudio.Surface;

// Виртуализация: что держит контейнеры развёрнутыми и разворачивание выбранного до сборки снимка
// выделения (ADR 0007). Часть SurfaceView; общее описание типа — в SurfaceView.cs.
public partial class SurfaceView
{
    private int _realizationHolds;
    private bool _realizingSelection;
    private ISelectionModel? _watchedSelection;

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
