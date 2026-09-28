using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Utilities;
using Avalonia.VisualTree;

namespace ArxisStudio.Surface;

/// <summary>
/// Панель холста, у которой контейнеры есть только у элементов в видимой области с запасом
/// (ADR 0007).
/// </summary>
/// <remarks>
/// Панель верхнего уровня у <see cref="SurfaceView"/> и редактора узлов. Координаты панели — мировые,
/// как у <see cref="SurfacePanel"/>: она лежит в слое, к которому уже применена трансформация
/// viewport. Ребёнок меряется бесконечностью и встаёт в свой <see cref="SurfaceItem.Location"/>.
/// <para>
/// Виртуализирует она, только когда задана <see cref="SurfaceView.ItemLocationBinding"/>: без неё
/// положение элемента без контейнера взять неоткуда, и панель разворачивает всё — поведение
/// <see cref="SurfacePanel"/>. С привязкой панель держит геометрию каждого элемента: положение
/// читается той же привязкой без контейнера и обновляется по <see cref="INotifyPropertyChanged"/>
/// элемента, размер — измеренный при последнем показе, а до него
/// <see cref="SurfaceView.EstimatedItemSize"/>. По этой геометрии считаются охват и миникарта, а
/// контейнер есть у элемента, чей прямоугольник пересекает видимую область, расширенную на
/// <see cref="RealizationMargin"/>. Сворачивается он только за вдвое большим запасом — иначе элемент
/// на границе разворачивался бы и сворачивался на каждом кадре панорамы.
/// </para>
/// <para>
/// Не сворачиваются главный выбранный, выбранный не целиком и контейнер с фокусом клавиатуры, а
/// элемент, который сам контейнер, не сворачивается никогда; прочий выбранный сворачивается и остаётся
/// выбранным — выбор — данные (ADR 0010). Свёрнутый контейнер уходит в пул по ключу и готовится для
/// другого элемента; его состояние, не привязанное к данным, — заданные руками размер или
/// <c>ZIndex</c> — при этом не сохраняется.
/// </para>
/// </remarks>
public partial class VirtualizingSurfacePanel : VirtualizingPanel
{
    /// <summary>
    /// Идентификатор свойства области, занятой элементами.
    /// </summary>
    public static readonly StyledProperty<Rect> ExtentProperty =
        AvaloniaProperty.Register<VirtualizingSurfacePanel, Rect>(nameof(Extent));

    /// <summary>
    /// Идентификатор свойства запаса разворачивания.
    /// </summary>
    public static readonly StyledProperty<double> RealizationMarginProperty =
        AvaloniaProperty.Register<VirtualizingSurfacePanel, double>(nameof(RealizationMargin), 200);

    private static readonly AttachedProperty<object?> RecycleKeyProperty =
        AvaloniaProperty.RegisterAttached<VirtualizingSurfacePanel, Control, object?>("RecycleKey");

    private static readonly object s_itemIsItsOwnContainer = new();

    // Геометрия каждого элемента по его индексу. У развёрнутого она снята с контейнера, у остальных —
    // прочитана привязкой и измерена при последнем показе.
    private readonly List<Slot> _slots = new();

    // Развёрнутые по индексу — по порядку, чтобы обход и дети шли в порядке элементов.
    private readonly SortedDictionary<int, Control> _realized = new();
    private readonly Dictionary<Control, int> _indexOf = new();
    private readonly Dictionary<object, Stack<Control>> _pool = new();

    // Сдвинутые с прошлой расстановки: только их расстановка и ставит заново.
    private readonly HashSet<Control> _moved = new();

    // Индекс элемента по нему самому — для тех, кто спрашивает геометрию элемента, а не индекса;
    // после правки коллекции сбрасывается и собирается заново, когда спросят. Сбрасывается целиком, а
    // не чистится: так он не держит ушедшие элементы до следующего вопроса и не стоит прохода по
    // себе на каждой вставке.
    private Dictionary<object, int>? _indexByItem;

    // Модели, за которыми панель следит, со счётом их вхождений в коллекцию, и сменившиеся с прошлой меры.
    private readonly Dictionary<INotifyPropertyChanged, int> _tracked = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _changed = new(ReferenceEqualityComparer.Instance);
    private readonly ModelTracker _tracker;
    private readonly List<KeyValuePair<int, Control>> _scratch = new();

    private SurfaceView? _view;
    private PointBindingReader? _reader;
    private PointBindingWriter? _writer;
    private object? _writing;
    private ObjectBindingReader? _accentReader;
    private Rect _extent;
    private bool _extentStale = true;
    private bool _slotsStale = true;
    private bool _arrangeAll = true;
    private bool _isInLayout;

    static VirtualizingSurfacePanel()
    {
        SurfaceItem.LocationProperty.Changed.AddClassHandler<SurfaceItem>((item, _) =>
        {
            if (item.GetVisualParent() is VirtualizingSurfacePanel panel)
                panel.OnChildMoved(item);
        });
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VirtualizingSurfacePanel"/>.
    /// </summary>
    public VirtualizingSurfacePanel()
    {
        _tracker = new ModelTracker(this);
    }

    /// <summary>
    /// Получает область в мировых координатах, которую занимают элементы, — и развёрнутые, и нет.
    /// </summary>
    /// <remarks>
    /// Её читает <see cref="SurfaceView.ItemsExtent"/>. Сдвиг элемента растит её объединением;
    /// сжаться она может, только если с края ушёл элемент, который его и задавал, — тогда она
    /// пересчитывается по геометрии всех элементов в ближайшей расстановке.
    /// </remarks>
    public Rect Extent
    {
        get => GetValue(ExtentProperty);
        set => SetValue(ExtentProperty, value);
    }

    /// <summary>
    /// Получает или задает запас вокруг видимой области в пикселях экрана, в котором элементы уже
    /// разворачиваются.
    /// </summary>
    /// <remarks>
    /// Чем он больше, тем реже на панораме видно, как элемент появляется у края, и тем больше
    /// контейнеров живёт одновременно. Сворачиваются элементы за вдвое большим запасом.
    /// </remarks>
    public double RealizationMargin
    {
        get => GetValue(RealizationMarginProperty);
        set => SetValue(RealizationMarginProperty, value);
    }

    /// <summary>
    /// Сколько раз панель меряла детей — для стенда стоимости.
    /// </summary>
    internal int MeasuredChildren { get; private set; }

    /// <summary>
    /// Сколько раз панель расставляла детей — для стенда стоимости.
    /// </summary>
    internal int ArrangedChildren { get; private set; }

    /// <summary>
    /// Сколько контейнеров развёрнуто сейчас.
    /// </summary>
    internal int RealizedCount => _realized.Count;

    /// <summary>
    /// Сколько раз геометрия дочитывалась проходом по всей коллекции — для стенда.
    /// </summary>
    internal int SlotPasses { get; private set; }

    /// <summary>
    /// Виртуализирует ли панель: без привязки положения она разворачивает всё.
    /// </summary>
    internal bool IsVirtualizing => _view?.ItemLocationBinding != null;

    /// <summary>
    /// Прямоугольники всех элементов в мировых координатах — и развёрнутых, и нет; без площади — не
    /// отдаются.
    /// </summary>
    internal IEnumerable<Rect> EnumerateItemBounds()
    {
        for (var i = 0; i < _slots.Count; i++)
        {
            var bounds = BoundsOf(_slots[i]);
            if (bounds.Width > 0 && bounds.Height > 0)
                yield return bounds;
        }
    }

    /// <summary>
    /// Прямоугольник элемента в мировых координатах — развёрнутого или нет.
    /// </summary>
    /// <returns><see langword="false"/>, если элемента в коллекции нет или геометрия ещё не прочитана.</returns>
    internal bool TryGetItemBounds(object? item, out Rect bounds)
    {
        bounds = default;
        if (item == null || _slotsStale)
            return false;

        var index = IndexOfItem(item);
        if (index < 0 || index >= _slots.Count)
            return false;

        bounds = BoundsOf(_slots[index]);
        return true;
    }

    /// <summary>
    /// Индекс элемента в коллекции; <c>-1</c>, если его нет.
    /// </summary>
    private int IndexOfItem(object item)
    {
        if (_indexByItem == null)
        {
            var items = Items;
            _indexByItem = new Dictionary<object, int>(items.Count);
            for (var i = items.Count - 1; i >= 0; i--)
            {
                if (items[i] is { } each)
                    _indexByItem[each] = i;
            }
        }

        return _indexByItem.TryGetValue(item, out var index) ? index : -1;
    }

    /// <summary>
    /// Разворачивает элемент сейчас, вне прохода раскладки, и ставит его на место.
    /// </summary>
    /// <returns>
    /// Контейнер элемента или <see langword="null"/>, если индекса нет или идёт проход раскладки:
    /// посреди него панель обходит развёрнутые, и новый контейнер сломал бы обход.
    /// </returns>
    internal Control? RealizeNow(int index)
    {
        if (_realized.TryGetValue(index, out var realized))
            return realized;

        var items = Items;
        if (_isInLayout || _view == null || ItemContainerGenerator == null || index < 0 || index >= items.Count)
            return null;

        SyncSlots(items);
        using var change = _view.BeginContainerChange();
        var container = Realize(index, items[index]);
        MeasuredChildren++;
        container.Measure(Size.Infinity);
        SetMeasuredSize(index, container.DesiredSize);
        ArrangeChild(container);
        InvalidateArrange();
        return container;
    }

    /// <summary>
    /// Разворачивает элементы, чей прямоугольник пересекает <paramref name="bounds"/>.
    /// </summary>
    internal void RealizeWithin(Rect bounds)
    {
        if (!IsVirtualizing)
            return;

        SyncSlots(Items);
        for (var i = 0; i < _slots.Count; i++)
        {
            if (!_realized.ContainsKey(i) && bounds.Intersects(BoundsOf(_slots[i])))
                RealizeNow(i);
        }
    }

    /// <summary>
    /// Прямоугольник свёрнутого элемента в мировых координатах.
    /// </summary>
    /// <returns><see langword="false"/>, если элемент развёрнут, индекса нет или панель не виртуализирует.</returns>
    internal bool TryGetCollapsedBounds(int index, out Rect bounds)
    {
        bounds = default;
        if (!IsVirtualizing || _realized.ContainsKey(index))
            return false;

        SyncStructure();
        if (index < 0 || index >= _slots.Count)
            return false;

        bounds = BoundsOf(_slots[index]);
        return true;
    }

    /// <summary>
    /// Индекс свёрнутого элемента или <c>-1</c>, если он развёрнут или его нет.
    /// </summary>
    internal int CollapsedIndexOf(object? item)
    {
        if (item == null || !IsVirtualizing)
            return -1;

        SyncStructure();
        var index = IndexOfItem(item);
        return index >= 0 && !_realized.ContainsKey(index) ? index : -1;
    }

    /// <summary>
    /// Двигает свёрнутый элемент: пишет положение в модель привязкой положения в обратную сторону
    /// (ADR 0010).
    /// </summary>
    /// <remarks>
    /// Модель, изменившаяся на записи, сообщит о себе и сама, но панель ставит ячейку сразу: модель без
    /// <see cref="INotifyPropertyChanged"/> не сообщит ничего, а держащие на элементе своё — концы
    /// связей — узнают о сдвиге в том же кадре.
    /// </remarks>
    /// <returns>
    /// <see langword="false"/>, если модель записи не приняла — привязка односторонняя, — или элемент
    /// развёрнут; положение тогда не меняется.
    /// </returns>
    internal bool TryMoveCollapsed(int index, Point location)
    {
        if (!IsVirtualizing || _realized.ContainsKey(index) || Writer() is not { } writer || Reader() is not { } reader)
            return false;

        var items = Items;
        SyncStructure();
        if (index < 0 || index >= _slots.Count)
            return false;

        // Своё эхо модели панель не слушает: ячейку она ставит сама, а пересчёт по эху проходил бы
        // всю коллекцию на каждой записи — сдвиг большого выбора стоил бы квадрат его размера.
        var item = items[index];
        _writing = item;
        try
        {
            writer.Write(item, location);
            writer.Release();
        }
        finally
        {
            _writing = null;
        }

        var written = Normalize(reader.Read(item));
        reader.Release();

        if (Math.Abs(written.X - location.X) > 0.01 || Math.Abs(written.Y - location.Y) > 0.01)
            return false;

        if (_slots[index].Location != written)
        {
            ChangeSlot(index, _slots[index] with { Location = written });
            _view?.OnContentChanged();
            _view?.OnItemGeometryChanged(item);
        }

        return true;
    }

    /// <summary>
    /// Приводит геометрию к коллекции и дочитывает сменившиеся модели — один раз перед пакетом вопросов
    /// о свёрнутых (<see cref="TryGetCollapsedBounds"/>), которые сами дочитывать не станут.
    /// </summary>
    internal void Sync() => SyncSlots(Items);

    /// <summary>
    /// Индексы свёрнутых элементов, чей прямоугольник пересекает <paramref name="bounds"/>, — рамка
    /// выбирает их данными, не разворачивая (ADR 0010).
    /// </summary>
    internal List<int> CollapsedIndicesWithin(Rect bounds)
    {
        var result = new List<int>();
        if (!IsVirtualizing)
            return result;

        SyncSlots(Items);
        for (var i = 0; i < _slots.Count; i++)
        {
            if (!_realized.ContainsKey(i) && bounds.Intersects(BoundsOf(_slots[i])))
                result.Add(i);
        }

        return result;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_view is not { } view || ItemContainerGenerator == null)
            return default;

        _isInLayout = true;
        try
        {
            return MeasureRealized(view, availableSize);
        }
        finally
        {
            _isInLayout = false;
        }
    }

    private Size MeasureRealized(SurfaceView view, Size availableSize)
    {
        var items = Items;
        SyncSlots(items);

        if (IsVirtualizing)
        {
            // Выбор, который задевают разворачивание и сворачивание, публикуется в конце одним
            // снимком и без события (ADR 0010).
            using var change = view.BeginContainerChange();

            // Ниже порога упрощённого вида окна нет вовсе (ADR 0008): развёрнуто только закреплённое.
            var simplified = view.IsSimplified;
            var (realize, keep) = Windows(view);

            // Уходят вышедшие из окна с запасом, кроме закреплённых; под удержанием жеста — никто.
            _scratch.Clear();
            if (!view.IsRealizationHeld)
            {
                foreach (var pair in _realized)
                {
                    if ((simplified || !keep.Intersects(BoundsOf(_slots[pair.Key]))) && !IsPinned(view, pair.Key, pair.Value))
                        _scratch.Add(pair);
                }
            }

            foreach (var pair in _scratch)
                Recycle(pair.Key, pair.Value);

            // Приходят вошедшие в окно и те, кто сам себе контейнер; выбранное — данные, и за окном
            // контейнер ему не нужен (ADR 0010).
            for (var i = 0; i < items.Count; i++)
            {
                if (!_realized.ContainsKey(i)
                    && ((!simplified && realize.Intersects(BoundsOf(_slots[i]))) || items[i] is SurfaceItem))
                {
                    Realize(i, items[i]);
                }
            }
        }
        else
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (!_realized.ContainsKey(i))
                    Realize(i, items[i]);
            }
        }

        var infinite = Size.Infinity;
        foreach (var pair in _realized)
        {
            MeasuredChildren++;
            pair.Value.Measure(infinite);
            SetMeasuredSize(pair.Key, pair.Value.DesiredSize);
        }

        // После меры — сменился ли чей-то размер или состав развёрнутых — расставляются все.
        _arrangeAll = true;
        if (_extentStale)
        {
            _extentStale = false;
            _extent = ComputeExtent();
        }

        SetCurrentValue(ExtentProperty, _extent);

        // Как и панель ядра: в холсте панель меряют бесконечностью, и тогда её размер — дальний угол
        // содержимого; данное конечное место она занимает целиком.
        return new Size(
            double.IsPositiveInfinity(availableSize.Width) ? Math.Max(0, _extent.Right) : availableSize.Width,
            double.IsPositiveInfinity(availableSize.Height) ? Math.Max(0, _extent.Bottom) : availableSize.Height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        _isInLayout = true;
        try
        {
            ArrangeRealized();
        }
        finally
        {
            _isInLayout = false;
        }

        return finalSize;
    }

    private void ArrangeRealized()
    {
        if (_arrangeAll)
        {
            foreach (var pair in _realized)
                ArrangeChild(pair.Value);

            _arrangeAll = false;
        }
        else
        {
            foreach (var moved in _moved)
            {
                // Свёрнутый после сдвига ставить уже некуда.
                if (_indexOf.ContainsKey(moved))
                    ArrangeChild(moved);
            }
        }

        _moved.Clear();
        PushExtent();
    }

    /// <inheritdoc />
    protected override void OnItemsControlChanged(ItemsControl? oldValue)
    {
        base.OnItemsControlChanged(oldValue);

        if (oldValue != null)
            oldValue.PropertyChanged -= OnViewPropertyChanged;

        // Прежние дети уходят вместе с прежним владельцем: панель отпускает всё, что о них помнила.
        _realized.Clear();
        _indexOf.Clear();
        _pool.Clear();
        _moved.Clear();
        UntrackAll();
        _slots.Clear();
        _slotsStale = true;
        _extentStale = true;
        _indexByItem = null;
        _reader = null;
        _writer = null;

        _view = ItemsControl as SurfaceView;
        if (ItemsControl != null)
            ItemsControl.PropertyChanged += OnViewPropertyChanged;

        InvalidateMeasure();
    }

    /// <inheritdoc />
    protected override void OnItemsChanged(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(items, e);
        _indexByItem = null;
        OnCollapsedChanged();

        if (_slotsStale)
        {
            // Геометрия и так перечитается целиком; развёрнутые индексы всё равно обязаны остаться верными.
            RemoveAllContainers();
            InvalidateMeasure();
            return;
        }

        System.Collections.IList? left = null;
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                InsertItems(e.NewStartingIndex, e.NewItems!.Count, items);
                break;
            case NotifyCollectionChangedAction.Remove:
                RemoveItems(e.OldStartingIndex, e.OldItems!);
                left = e.OldItems;
                break;
            case NotifyCollectionChangedAction.Replace:
                RemoveItems(e.OldStartingIndex, e.OldItems!);
                InsertItems(e.NewStartingIndex, e.NewItems!.Count, items);
                left = e.OldItems;
                break;
            case NotifyCollectionChangedAction.Move when e.OldStartingIndex >= 0:
                RemoveItems(e.OldStartingIndex, e.OldItems!);
                var insertAt = e.NewStartingIndex;
                if (insertAt > e.OldStartingIndex)
                    insertAt -= e.OldItems!.Count - 1;
                InsertItems(insertAt, e.NewItems!.Count, items);
                left = e.OldItems;
                break;
            default:
                RemoveAllContainers();
                UntrackAll();
                _slots.Clear();
                _changed.Clear();
                _slotsStale = true;
                break;
        }

        _extentStale = true;
        InvalidateMeasure();

        // Ушедший или переставленный свёрнутым о себе не сообщит ничем: его портов нет, а держащие на
        // нём своё должны узнать, где он теперь и есть ли он вообще. Сообщается, когда хранилище снова
        // сходится с коллекцией: посреди перестановки индекс элемента указал бы на чужую ячейку.
        if (left != null)
        {
            foreach (var item in left)
                _view?.OnItemGeometryChanged(item);
        }
    }

    /// <summary>
    /// Разворачивает элемент для кнопки навигации и просит показать его, не двигая холст сама.
    /// </summary>
    /// <remarks>
    /// <see cref="Avalonia.Controls.Primitives.SelectingItemsControl"/> зовёт это на каждой смене
    /// выбора. Обычная панель поверхности отвечает на это <c>BringIntoView</c>, который никто не
    /// ловит, — и холст стоит; показать элемент поверхность умеет сама
    /// (<see cref="SurfaceView.CenterOnItem"/>).
    /// </remarks>
    /// <param name="index">Индекс элемента.</param>
    /// <returns>Контейнер элемента или <see langword="null"/>.</returns>
    protected override Control? ScrollIntoView(int index)
    {
        var container = RealizeNow(index);
        container?.BringIntoView();
        return container;
    }

    /// <inheritdoc />
    protected override Control? ContainerFromIndex(int index) =>
        _realized.TryGetValue(index, out var container) ? container : null;

    /// <inheritdoc />
    protected override int IndexFromContainer(Control container) =>
        _indexOf.TryGetValue(container, out var index) ? index : -1;

    /// <summary>
    /// Отдаёт развёрнутые контейнеры в порядке элементов — снимком: обходящий вправе менять выбор, а
    /// выбор разворачивает.
    /// </summary>
    /// <returns>Развёрнутые контейнеры.</returns>
    protected override IEnumerable<Control>? GetRealizedContainers() => new List<Control>(_realized.Values);

    /// <summary>
    /// Навигации стрелками между контейнерами нет: стрелки у поверхности сдвигают выбранное.
    /// </summary>
    /// <returns><see langword="null"/>.</returns>
    protected override IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap) => null;

    private void OnViewPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == SurfaceView.ItemLocationBindingProperty)
        {
            // Другая привязка — другие положения: контейнеры готовятся заново, геометрия читается заново.
            RecycleAll();
            UntrackAll();
            _slots.Clear();
            _changed.Clear();
            _reader = null;
            _writer = null;
            _slotsStale = true;
            InvalidateMeasure();
        }
        else if (e.Property == SurfaceView.ItemAccentBindingProperty)
        {
            // Другая полоса у всех: читается заново, контейнеры не трогаются.
            RereadAccents();
        }
        else if (e.Property == SurfaceView.EstimatedItemSizeProperty)
        {
            // Размер тех, кто ни разу не показывался, — а с ним их прямоугольники у всех, кто их читает.
            _extentStale = true;
            OnCollapsedChanged();
            _view?.OnItemGeometryChanged(null);
            InvalidateMeasure();
        }
        else if (e.Property == SurfaceView.IsSimplifiedProperty)
        {
            // Через порог всё видимое на нём разом сворачивается или разворачивается.
            InvalidateMeasure();
        }
        else if (IsVirtualizing
                 && _view is { IsSimplified: false }
                 && (e.Property == SurfaceView.ViewportLocationProperty
                     || e.Property == SurfaceView.ViewportZoomProperty
                     || e.Property == BoundsProperty))
        {
            // В упрощённом виде окна нет, и панораме с масштабом разворачивать нечего.
            InvalidateMeasure();
        }
    }

    private void OnModelChanged(object? model)
    {
        if (model == null || !IsVirtualizing || ReferenceEquals(model, _writing))
            return;

        // Развёрнутый элемент держит свой контейнер, и положение ему уже перенесла привязка; мера
        // ему не нужна — иначе каждый кадр жеста, записав положение в модель, перемерял бы всех
        // развёрнутых.
        if (IndexOfItem(model) is var index and >= 0 && _realized.ContainsKey(index))
            return;

        _changed.Add(model);
        InvalidateMeasure();
    }

    private void OnChildMoved(SurfaceItem item)
    {
        if (!_indexOf.TryGetValue(item, out var index))
            return;

        if (!_arrangeAll)
            _moved.Add(item);

        // Охват, который надо пересчитывать, пересчитает расстановка: сдвиг идёт каждый кадр жеста.
        ChangeSlot(index, _slots[index] with { Location = LocationOf(item) });
        if (!_extentStale)
            SetCurrentValue(ExtentProperty, _extent);

        InvalidateArrange();
    }

    /// <summary>
    /// Приводит геометрию к коллекции: перечитывает целиком после сброса, а после правок моделей —
    /// только сменившиеся.
    /// </summary>
    private void SyncSlots(IReadOnlyList<object?> items)
    {
        if (_slotsStale || _slots.Count != items.Count)
        {
            RebuildSlots(items);
            return;
        }

        if (_changed.Count == 0)
            return;

        SlotPasses++;
        var reader = Reader();
        var accents = AccentReader();
        List<object>? moved = null;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is not { } item || !_changed.Contains(item) || _realized.ContainsKey(i))
                continue;

            var before = _slots[i].Location;
            ChangeSlot(i, _slots[i] with { Location = Normalize(reader?.Read(item) ?? default), Accent = ReadAccent(accents, item) });
            if (_slots[i].Location != before)
                (moved ??= new List<object>()).Add(item);
        }

        reader?.Release();
        accents?.Release();
        _changed.Clear();
        if (moved == null)
            return;

        // Развёрнутый элемент сообщает о себе сам — сменой границ контейнера; свёрнутый — только так:
        // холсту целиком и тем, кто держит на нём своё, как концы связей.
        _view?.OnContentChanged();
        foreach (var item in moved)
            _view?.OnItemGeometryChanged(item);
    }

    /// <summary>
    /// Приводит к коллекции только состав ячеек, не дочитывая сменившихся моделей: для вопросов по
    /// одному элементу, которые идут пакетом, — дочитка проходит всю коллекцию.
    /// </summary>
    private void SyncStructure()
    {
        var items = Items;
        if (_slotsStale || _slots.Count != items.Count)
            RebuildSlots(items);
    }

    private void RebuildSlots(IReadOnlyList<object?> items)
    {
        _slotsStale = false;
        _extentStale = true;
        _changed.Clear();
        UntrackAll();
        _slots.Clear();

        var reader = Reader();
        var accents = AccentReader();
        for (var i = 0; i < items.Count; i++)
        {
            var accent = ReadAccent(accents, items[i]);
            if (_realized.TryGetValue(i, out var container))
                _slots.Add(new Slot(LocationOf(container), container.DesiredSize, container.IsMeasureValid, accent));
            else
                _slots.Add(new Slot(Normalize(reader?.Read(items[i]) ?? default), default, false, accent));

            if (reader != null)
                Track(items[i]);
        }

        reader?.Release();
        accents?.Release();
        OnCollapsedChanged();

        // Прочитано всё заново: каждый, кто держит на геометрии своё, перечитывает его.
        _view?.OnItemGeometryChanged(null);
    }

    private void InsertItems(int index, int count, IReadOnlyList<object?> items)
    {
        Shift(index, count);

        var reader = Reader();
        var accents = AccentReader();
        for (var k = 0; k < count; k++)
        {
            var item = items[index + k];
            _slots.Insert(index + k, new Slot(Normalize(reader?.Read(item) ?? default), default, false, ReadAccent(accents, item)));
            if (reader != null)
                Track(item);
        }

        reader?.Release();
        accents?.Release();
    }

    private void RemoveItems(int index, System.Collections.IList removed)
    {
        var count = removed.Count;
        for (var i = index; i < index + count; i++)
        {
            if (_realized.TryGetValue(i, out var container))
                RemoveContainer(i, container);
        }

        foreach (var item in removed)
            Untrack(item);

        _slots.RemoveRange(index, count);
        Shift(index + count, -count);
    }

    /// <summary>
    /// Сдвигает индексы развёрнутых начиная с <paramref name="from"/>.
    /// </summary>
    private void Shift(int from, int by)
    {
        _scratch.Clear();
        foreach (var pair in _realized)
        {
            if (pair.Key >= from)
                _scratch.Add(pair);
        }

        foreach (var pair in _scratch)
            _realized.Remove(pair.Key);

        foreach (var pair in _scratch)
        {
            var next = pair.Key + by;
            _realized[next] = pair.Value;
            _indexOf[pair.Value] = next;
            ItemContainerGenerator?.ItemContainerIndexChanged(pair.Value, pair.Key, next);
        }
    }

    private Control Realize(int index, object? item)
    {
        var generator = ItemContainerGenerator!;

        // Контейнер известен панели до подготовки: подготовка отмечает выбор, и снимок выделения
        // спрашивает контейнер по индексу раньше, чем тот встанет в детей.
        if (!generator.NeedsContainer(item, index, out var recycleKey))
        {
            var own = (Control)item!;
            Register(index, own);
            if (!own.IsSet(RecycleKeyProperty))
            {
                generator.PrepareItemContainer(own, own, index);
                InsertChild(index, own);
                own.SetValue(RecycleKeyProperty, s_itemIsItsOwnContainer);
                generator.ItemContainerPrepared(own, item, index);
            }

            SyncSlot(index, own);
            return own;
        }

        Control container;
        if (recycleKey != null && _pool.TryGetValue(recycleKey, out var pooled) && pooled.Count > 0)
        {
            container = pooled.Pop();
            container.SetCurrentValue(IsVisibleProperty, true);
        }
        else
        {
            container = generator.CreateContainer(item, index, recycleKey);
            container.SetValue(RecycleKeyProperty, recycleKey);
        }

        Register(index, container);
        generator.PrepareItemContainer(container, item, index);
        InsertChild(index, container);
        generator.ItemContainerPrepared(container, item, index);
        SyncSlot(index, container);
        return container;
    }

    /// <summary>
    /// Сворачивает контейнер элемента, ушедшего из окна: элемент остаётся, его геометрия — тоже.
    /// </summary>
    private void Recycle(int index, Control container)
    {
        if (container.GetValue(RecycleKeyProperty) == s_itemIsItsOwnContainer)
            return;

        SyncSlot(index, container);

        // Правку полосы, пока элемент был развёрнут, панель не слушала: карточке нужна нынешняя.
        if (AccentReader() is { } accents)
        {
            _slots[index] = _slots[index] with { Accent = ReadAccent(accents, Items[index]) };
            accents.Release();
        }

        RemoveContainer(index, container);
    }

    private void RecycleAll()
    {
        _scratch.Clear();
        _scratch.AddRange(_realized);
        foreach (var pair in _scratch)
            Recycle(pair.Key, pair.Value);
    }

    /// <summary>
    /// Убирает контейнер из детей: свой — в пул по ключу, элемент-контейнер — просто из детей.
    /// </summary>
    private void RemoveContainer(int index, Control container)
    {
        Unregister(index, container);

        var key = container.GetValue(RecycleKeyProperty);
        if (key == s_itemIsItsOwnContainer)
        {
            container.ClearValue(RecycleKeyProperty);
            RemoveInternalChild(container);
            return;
        }

        ItemContainerGenerator!.ClearItemContainer(container);
        if (key == null)
        {
            RemoveInternalChild(container);
            return;
        }

        if (!_pool.TryGetValue(key, out var pooled))
            _pool[key] = pooled = new Stack<Control>();

        pooled.Push(container);
        container.SetCurrentValue(IsVisibleProperty, false);
        RemoveInternalChild(container);
    }

    private void RemoveAllContainers()
    {
        _scratch.Clear();
        _scratch.AddRange(_realized);
        foreach (var pair in _scratch)
            RemoveContainer(pair.Key, pair.Value);
    }

    private void Register(int index, Control container)
    {
        _realized[index] = container;
        _indexOf[container] = index;
        OnCollapsedChanged();
    }

    private void Unregister(int index, Control container)
    {
        _realized.Remove(index);
        _indexOf.Remove(container);
        _moved.Remove(container);
        OnCollapsedChanged();
    }

    /// <summary>
    /// Ставит контейнер в детей по порядку индексов: от него зависит, кто рисуется поверх.
    /// </summary>
    private void InsertChild(int index, Control container)
    {
        var children = Children;
        int lo = 0, hi = children.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (_indexOf.TryGetValue(children[mid], out var at) && at < index)
                lo = mid + 1;
            else
                hi = mid;
        }

        InsertInternalChild(lo, container);
    }

    private bool IsPinned(SurfaceView view, int index, Control container) =>
        view.KeepsSelectedRealized(index, container)
        || container.IsKeyboardFocusWithin
        || container.GetValue(RecycleKeyProperty) == s_itemIsItsOwnContainer
        || (view.PressedItem is { } pressed && ReferenceEquals(Items[index], pressed));

    private (Rect Realize, Rect Keep) Windows(SurfaceView view)
    {
        var zoom = Math.Max(view.ViewportZoom, 0.0001);
        var visible = new Rect(view.ViewportLocation, view.Bounds.Size / zoom);
        var margin = Math.Max(0, RealizationMargin) / zoom;
        return (visible.Inflate(margin), visible.Inflate(margin * 2));
    }

    private void ArrangeChild(Control child)
    {
        ArrangedChildren++;
        child.Arrange(new Rect(LocationOf(child), child.DesiredSize));
    }

    private void SyncSlot(int index, Control container) =>
        ChangeSlot(index, _slots[index] with { Location = LocationOf(container) });

    private void SetMeasuredSize(int index, Size size)
    {
        var slot = _slots[index];
        if (!slot.IsMeasured || slot.Size != size)
            ChangeSlot(index, slot with { Size = size, IsMeasured = true });
    }

    /// <summary>
    /// Меняет геометрию элемента и ведёт охват: растит объединением, а если с края ушёл тот, кто его
    /// задавал, — отмечает к пересчёту.
    /// </summary>
    private void ChangeSlot(int index, Slot next)
    {
        var previous = _slots[index];
        if (previous == next)
            return;

        _slots[index] = next;

        // Развёрнутый рисует себя сам; карточки меняются только от свёрнутого.
        if (!_realized.ContainsKey(index))
            OnCollapsedChanged();

        var before = BoundsOf(previous);
        var after = BoundsOf(next);
        if (_extentStale || before == after)
            return;

        if (!HasArea(_extent) || (HasArea(before) && TouchesEdge(before, _extent)))
            _extentStale = true;
        else if (HasArea(after) && !_extent.Contains(after))
            _extent = _extent.Union(after);
    }

    private void PushExtent()
    {
        if (_extentStale)
        {
            _extentStale = false;
            _extent = ComputeExtent();
        }

        SetCurrentValue(ExtentProperty, _extent);
    }

    private Rect ComputeExtent()
    {
        Rect? extent = null;
        foreach (var slot in _slots)
        {
            var bounds = BoundsOf(slot);
            if (!HasArea(bounds))
                continue;

            extent = extent is { } current ? current.Union(bounds) : bounds;
        }

        return extent ?? default;
    }

    private Rect BoundsOf(Slot slot)
    {
        var size = slot.IsMeasured ? slot.Size : IsVirtualizing ? _view!.EstimatedItemSize : default;
        return new Rect(slot.Location, size);
    }

    private ObjectBindingReader? AccentReader()
    {
        if (_view?.ItemAccentBinding is not { } binding)
            return null;

        if (_accentReader == null || !ReferenceEquals(_accentReader.Binding, binding))
            _accentReader = new ObjectBindingReader(binding);

        return _accentReader;
    }

    /// <summary>
    /// Полоса элемента: кисть как есть, цвет — кистью из общего кеша, той же, что у контейнера.
    /// </summary>
    private static IBrush? ReadAccent(ObjectBindingReader? reader, object? item) =>
        reader == null ? null : AccentBrushes.From(reader.Read(item));

    private void RereadAccents()
    {
        var items = Items;
        if (_slotsStale || _slots.Count != items.Count)
            return;

        var accents = AccentReader();
        for (var i = 0; i < items.Count; i++)
            ChangeSlot(i, _slots[i] with { Accent = ReadAccent(accents, items[i]) });

        accents?.Release();
    }

    private PointBindingWriter? Writer()
    {
        if (_view?.ItemLocationBinding is not { } binding)
            return null;

        if (_writer == null || !ReferenceEquals(_writer.Binding, binding))
            _writer = new PointBindingWriter(binding);

        return _writer;
    }

    private PointBindingReader? Reader()
    {
        if (_view?.ItemLocationBinding is not { } binding)
            return null;

        if (_reader == null || !ReferenceEquals(_reader.Binding, binding))
            _reader = new PointBindingReader(binding);

        return _reader;
    }

    private void Track(object? item)
    {
        if (item is not INotifyPropertyChanged model)
            return;

        _tracked.TryGetValue(model, out var count);
        _tracked[model] = count + 1;
        if (count == 0)
            WeakEvents.ThreadSafePropertyChanged.Subscribe(model, _tracker);
    }

    private void Untrack(object? item)
    {
        if (item is not INotifyPropertyChanged model || !_tracked.TryGetValue(model, out var count))
            return;

        if (count > 1)
        {
            _tracked[model] = count - 1;
            return;
        }

        _tracked.Remove(model);
        WeakEvents.ThreadSafePropertyChanged.Unsubscribe(model, _tracker);
    }

    private void UntrackAll()
    {
        foreach (var model in _tracked.Keys)
            WeakEvents.ThreadSafePropertyChanged.Unsubscribe(model, _tracker);

        _tracked.Clear();
    }

    private static bool HasArea(Rect bounds) => bounds.Width > 0 && bounds.Height > 0;

    private static bool TouchesEdge(Rect bounds, Rect extent) =>
        bounds.X <= extent.X || bounds.Y <= extent.Y || bounds.Right >= extent.Right || bounds.Bottom >= extent.Bottom;

    private static Point LocationOf(Control child) =>
        child is SurfaceItem item ? Normalize(item.Location) : default;

    private static Point Normalize(Point location) =>
        double.IsNaN(location.X) || double.IsNaN(location.Y) ? default : location;

    /// <summary>
    /// Геометрия элемента, известная без его контейнера.
    /// </summary>
    /// <param name="Location">Положение в мировых координатах.</param>
    /// <param name="Size">Измеренный размер; до первой меры не значит ничего.</param>
    /// <param name="IsMeasured">Мерялся ли контейнер элемента хоть раз.</param>
    /// <param name="Accent">Полоса заголовка в упрощённом виде (ADR 0008).</param>
    private readonly record struct Slot(Point Location, Size Size, bool IsMeasured, IBrush? Accent = null);

    /// <summary>
    /// Слушает модели слабо: коллекция хоста живёт дольше панели, и обычная подписка держала бы её.
    /// </summary>
    private sealed class ModelTracker(VirtualizingSurfacePanel panel) : IWeakEventSubscriber<PropertyChangedEventArgs>
    {
        public void OnEvent(object? sender, WeakEvent ev, PropertyChangedEventArgs e) => panel.OnModelChanged(sender);
    }
}
