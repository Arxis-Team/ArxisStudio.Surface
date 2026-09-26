using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.VisualTree;
using ArxisStudio.Surface.States;

namespace ArxisStudio.Surface;

// Выделение: слой target'ов поверх индексного слоя SelectingItemsControl, их общая
// запись, публикация снимка, рамка и перечисление контейнеров.
// Часть SurfaceView; общее описание типа — в SurfaceView.cs.
public partial class SurfaceView
{
    private protected ISurfaceTargetResolver? _targetResolver;

    /// <summary>
    /// Отвечает, что внутри контейнера может стать target'ом.
    /// </summary>
    /// <remarks>
    /// Шов между ядром и содержимым (ADR 0003). Ядру достаточно контейнеров: без
    /// резолвера выбирается контейнер целиком. Дизайнер форм подставляет резолвер,
    /// который открывает вложенные контролы загруженной или размеченной формы.
    /// </remarks>
    internal ISurfaceTargetResolver TargetResolver
    {
        get => _targetResolver ??= ContainerTargetResolver.Instance;
        private protected set => _targetResolver = value;
    }

    /// <summary>
    /// Возвращает ключ группы target'а для снимка выделения.
    /// </summary>
    /// <remarks>
    /// Ядро о группах не знает и отвечает <see langword="null"/>; слой, который знает,
    /// переопределяет. Ключ входит в сравнение снимков: публикуется всё, что сравнивается.
    /// </remarks>
    internal virtual string? GetGroupKey(Control target) => null;

    /// <summary>
    /// Возвращает ближайший контейнер, внутри которого лежит target.
    /// </summary>
    /// <remarks>
    /// Для контрола внутри контейнера верхнего уровня это сам контейнер, для
    /// контрола внутри вложенного — вложенный, для вложенного контейнера — его владелец.
    /// Это единица группировки выделения: вместе выбираются только соседи по host'у.
    /// </remarks>
    internal static SurfaceItem? FindSurfaceHost(Control target)
        => target.FindAncestorOfType<SurfaceItem>();

    // Выбранные design targets в порядке приоритета: первый — primary.
    // Контейнеры и вложенные контролы лежат вместе: контейнер, выбранный целиком,
    // это просто SurfaceItem в списке. Владелец каждого target вычисляется
    // по дереву, поэтому структура не привязана к глубине вложенности.
    private protected readonly List<Control> _selectedTargets = new();

    /// <summary>
    /// Выбранные target'ы в порядке приоритета — для служб слоя редактирования.
    /// </summary>
    internal IReadOnlyList<Control> SelectedTargetList => _selectedTargets;

    /// <summary>
    /// Снимок контейнеров на время жеста; <c>null</c> вне жеста.
    /// </summary>
    private protected IReadOnlyList<SurfaceItem>? _containerSnapshot;

    /// <summary>
    /// Идентификатор свойства контейнера, в пределах которого работает текущая рамка.
    /// </summary>
    public static readonly DirectProperty<SurfaceView, SurfaceItem?> MarqueeScopeProperty =
        AvaloniaProperty.RegisterDirect<SurfaceView, SurfaceItem?>(
            nameof(MarqueeScope),
            o => o.MarqueeScope);

    /// <summary>
    /// Идентификатор primary selection target.
    /// </summary>
    public static readonly DirectProperty<SurfaceView, DesignSelectionTarget?> PrimarySelectionTargetProperty =
        AvaloniaProperty.RegisterDirect<SurfaceView, DesignSelectionTarget?>(
            nameof(PrimarySelectionTarget),
            o => o.PrimarySelectionTarget);

    /// <summary>
    /// Идентификатор коллекции всех выбранных design targets.
    /// </summary>
    public static readonly DirectProperty<SurfaceView, IReadOnlyList<DesignSelectionTarget>> SelectedDesignTargetsProperty =
        AvaloniaProperty.RegisterDirect<SurfaceView, IReadOnlyList<DesignSelectionTarget>>(
            nameof(SelectedDesignTargets),
            o => o.SelectedDesignTargets,
            (o, v) => o.SelectedDesignTargets = v);

    private protected SurfaceItem? _marqueeScope;

    /// <summary>
    /// Получает контейнер, в пределах которого сейчас работает рамка выделения,
    /// либо <see langword="null"/>, если рамка не активна или работает на уровне контейнеров.
    /// </summary>
    /// <remarks>
    /// Значение пересчитывается на каждом шаге протяжки, поэтому по нему можно
    /// подсвечивать целевой контейнер прямо во время жеста: пользователь видит,
    /// что именно попадёт в выборку, ещё до отпускания кнопки.
    /// <para>
    /// Библиотека не навязывает визуал подсветки — это решение конкретного продукта.
    /// </para>
    /// </remarks>
    public SurfaceItem? MarqueeScope
    {
        get => _marqueeScope;
        private set => SetAndRaise(MarqueeScopeProperty, ref _marqueeScope, value);
    }

    private protected DesignSelectionTarget? _primarySelectionTarget;

    /// <summary>
    /// Получает primary selection target редактора.
    /// </summary>
    public DesignSelectionTarget? PrimarySelectionTarget
    {
        get => _primarySelectionTarget;
        private set => SetAndRaise(PrimarySelectionTargetProperty, ref _primarySelectionTarget, value);
    }

    private protected IReadOnlyList<DesignSelectionTarget> _selectedDesignTargets = Array.Empty<DesignSelectionTarget>();

    /// <summary>
    /// Получает снимок всех выбранных design targets.
    /// </summary>
    public IReadOnlyList<DesignSelectionTarget> SelectedDesignTargets
    {
        get => _selectedDesignTargets;
        private set
        {
            SetAndRaise(SelectedDesignTargetsProperty, ref _selectedDesignTargets, value);
            SetAndRaise(SelectedDesignTargetsCountProperty, ref _selectedDesignTargetsCount, value.Count);
        }
    }

    /// <summary>
    /// Возникает при изменении набора выбранных design targets.
    /// </summary>
    /// <remarks>
    /// Это не то же, что унаследованное <see cref="SelectingItemsControl.SelectionChanged"/>:
    /// то работает на уровне элементов <c>ItemsSource</c>, а это — на уровне design targets,
    /// включая вложенные контролы и вложенные контейнеры.
    /// <para>
    /// Событие возникает только при фактической смене набора или primary target.
    /// Перетаскивание и изменение размера его не поднимают, хотя внутренний снимок
    /// пересобирается на каждом кадре.
    /// </para>
    /// </remarks>
    public event EventHandler<DesignSelectionChangedEventArgs>? DesignSelectionChanged;

    internal void CommitSelection(Rect bounds, bool isCtrlPressed)
        => CommitSelection(bounds, isCtrlPressed, ShouldUseContainerInteraction(LastInputModifiers));

    /// <summary>
    /// Применяет выделение по прямоугольнику рамки.
    /// </summary>
    /// <param name="bounds">Прямоугольник в мировых координатах.</param>
    /// <param name="isCtrlPressed">Признак добавления к текущему выделению.</param>
    /// <param name="useContainerSelection">
    /// Признак работы на уровне контейнеров. Передаётся явно, а не читается из
    /// <see cref="SurfaceView.LastInputModifiers"/>: режим фиксируется в момент нажатия, иначе
    /// отпускание модификатора посреди протяжки меняло бы смысл начатого жеста.
    /// </param>
    internal void CommitSelection(Rect bounds, bool isCtrlPressed, bool useContainerSelection)
    {
        if (Presenter?.Panel == null) return;

        // Владелец определяется итоговым прямоугольником — тем же правилом,
        // по которому во время протяжки обновлялся MarqueeScope.
        var marqueeOwner = useContainerSelection ? null : FindContainerForMarquee(bounds);

        // Владелец рамки может быть вложенным, а индексный выбор работает только
        // с item'ами верхнего уровня — сверять и выбирать нужно владеющий item.
        var marqueeOwnerItem = marqueeOwner != null ? ResolveOwningItem(marqueeOwner) : null;

        if (!useContainerSelection && isCtrlPressed && marqueeOwnerItem != null && !CanAddNestedTargetToContainer(marqueeOwnerItem))
            return;

        using (Selection.BatchUpdate())
        {
            if (!isCtrlPressed)
            {
                Selection.Clear();
                // Целевой набор накапливается по контейнерам, поэтому чистится
                // один раз здесь, а не внутри каждой итерации.
                _selectedTargets.Clear();
            }

            if (marqueeOwner != null)
            {
                // Рамка попала внутрь конкретного контейнера — работаем в его пределах.
                var selectedAny = CommitMarqueeWithinContainer(marqueeOwner, marqueeOwnerItem, bounds, isCtrlPressed);

                // Пустая рамка — это клик по пустой области контейнера,
                // и он должен выбрать сам контейнер, как и до появления рамки внутри.
                if (!selectedAny && !isCtrlPressed)
                {
                    var ownerIndex = IndexFromContainer(marqueeOwnerItem ?? marqueeOwner);
                    if (ownerIndex >= 0)
                    {
                        SetSingleSelectedTarget(marqueeOwner);
                        Selection.Select(ownerIndex);
                    }
                }
            }
            else
            {
                foreach (var child in Presenter.Panel.Children)
                {
                    if (child is not SurfaceItem container)
                        continue;

                    if (useContainerSelection)
                    {
                        if (!TryGetContainerWorldBounds(container, out var containerBounds) ||
                            !bounds.Intersects(containerBounds))
                            continue;

                        AddSelectedTarget(container);
                        Selection.Select(IndexFromContainer(container));
                        continue;
                    }

                    CommitMarqueeWithinContainer(container, container, bounds, isCtrlPressed);
                }
            }
        }

        RefreshSelectionOverlay();
    }

    /// <summary>
    /// Выбирает design targets внутри <paramref name="scope"/>, попавшие в рамку.
    /// </summary>
    /// <param name="scope">Контейнер, в пределах которого ищутся targets. Может быть вложенным.</param>
    /// <param name="ownerItem">Item верхнего уровня, на который адресуется индексный выбор.</param>
    /// <param name="bounds">Прямоугольник рамки в мировых координатах.</param>
    /// <param name="isAdditive">Признак добавления к текущему выбору.</param>
    /// <returns><see langword="true"/>, если хотя бы один target попал в выделение.</returns>
    private protected bool CommitMarqueeWithinContainer(
        SurfaceItem scope,
        SurfaceItem? ownerItem,
        Rect bounds,
        bool isAdditive)
    {
        ownerItem ??= ResolveOwningItem(scope);
        if (ownerItem == null)
            return false;

        var nestedTargets = new List<Control>();
        foreach (var target in TargetResolver.EnumerateCandidates(scope))
        {
            if (!TargetResolver.IsSelectable(target, scope))
                continue;

            // Рамка выбирает соседей внутри одного host'а, а не смесь уровней:
            // иначе в выборку попадали бы и вложенный контейнер, и его содержимое.
            if (!ReferenceEquals(FindSurfaceHost(target), scope))
                continue;

            if (Geometry.TryGetBounds(target, out var targetBounds) && bounds.Intersects(targetBounds))
                nestedTargets.Add(target);
        }

        if (nestedTargets.Count == 0)
            return false;

        foreach (var target in nestedTargets)
            AddSelectedTarget(target);

        Selection.Select(IndexFromContainer(ownerItem));
        return true;
    }

    private protected void AddSelectedTarget(Control target)
    {
        if (!_selectedTargets.Contains(target))
            _selectedTargets.Add(target);
    }

    private protected void SetSingleSelectedTarget(Control target)
    {
        _selectedTargets.Clear();
        _selectedTargets.Add(target);
    }

    /// <summary>
    /// Добавляет target в выделение или убирает его оттуда.
    /// </summary>
    /// <remarks>
    /// Повторный additive-клик снимает target, но не даёт опустошить выделение
    /// полностью: пустой выбор — это результат клика по холсту, а не по элементу.
    /// </remarks>
    /// <summary>
    /// Добавляет кластер к выделению или убирает его целиком.
    /// </summary>
    /// <remarks>
    /// Половина группы в выделении описывала бы состояние, которого пользователь не
    /// заказывал: добавляли группой — и убирать надо группой. Выделение при этом не
    /// опустошается, как и при обычном toggle одного target'а.
    /// </remarks>
    private protected void ToggleClusterInSelection(IReadOnlyList<Control> cluster)
    {
        if (cluster.Count <= 1)
        {
            ToggleTargetInSelection(cluster[0]);
            return;
        }

        var whole = true;
        foreach (var member in cluster)
        {
            if (_selectedTargets.Contains(member))
                continue;

            whole = false;
            break;
        }

        if (!whole)
        {
            foreach (var member in cluster)
            {
                if (!_selectedTargets.Contains(member))
                    _selectedTargets.Add(member);
            }

            return;
        }

        if (_selectedTargets.Count <= cluster.Count)
            return;

        foreach (var member in cluster)
            _selectedTargets.Remove(member);
    }

    private protected void ToggleTargetInSelection(Control target)
    {
        if (!_selectedTargets.Contains(target))
        {
            _selectedTargets.Add(target);
            return;
        }

        if (_selectedTargets.Count > 1)
            _selectedTargets.Remove(target);
    }

    /// <summary>
    /// Приводит индексную модель Avalonia в соответствие со слоем design target'ов.
    /// </summary>
    /// <remarks>
    /// Оверлей обходит <c>SelectedItems</c> и для каждого item'а спрашивает его
    /// targets. Контейнер, попавший в <c>Selection</c> без собственного target'а,
    /// подменяется вложенным по умолчанию — и группа контейнеров превращается
    /// в смешанную. Поэтому оба слоя обязаны меняться вместе.
    /// </remarks>
    private protected void SyncContainerItemSelection(SurfaceItem container)
    {
        var index = IndexFromContainer(container);
        if (index < 0)
            return;

        if (_selectedTargets.Contains(container))
            Selection.Select(index);
        else
            Selection.Deselect(index);
    }

    /// <summary>
    /// Возвращает item верхнего уровня, которому принадлежит target.
    /// </summary>
    private protected SurfaceItem? ResolveOwningItemForTarget(Control target)
    {
        var container = target as SurfaceItem ?? FindSurfaceHost(target);
        return container == null ? null : ResolveOwningItem(container);
    }

    /// <summary>
    /// Схлопывает выделение до одного контейнера.
    /// </summary>
    /// <remarks>
    /// Одна транзакция: двумя записями подряд наружу публиковалось промежуточное
    /// пустое выделение, и обычный клик по контейнеру внутри группы стоил трёх
    /// событий вместо одного. Запись индексного слоя принадлежит редактору —
    /// состояние контейнера только сообщает о жесте.
    /// </remarks>
    internal void CollapseSelectionTo(SurfaceItem container)
    {
        var index = IndexFromContainer(container);
        if (index < 0)
            return;

        using (Selection.BatchUpdate())
        {
            Selection.Clear();
            Selection.Select(index);
        }
    }

    /// <summary>
    /// Возвращает <see cref="SurfaceItem"/> верхнего уровня, которому принадлежит
    /// указанный контейнер, либо <see langword="null"/>, если он не в этом редакторе.
    /// </summary>
    /// <remarks>
    /// Контейнеры могут быть вложены друг в друга, но индексная модель выбора Avalonia
    /// знает только контейнеры собственного <c>ItemsSource</c>. Поэтому выбор всегда
    /// маршрутизируется на владеющий item верхнего уровня, а сам вложенный контейнер
    /// участвует как design target — это и даёт дерево любой глубины без отказа от
    /// <see cref="SelectingItemsControl"/>.
    /// </remarks>
    internal SurfaceItem? ResolveOwningItem(SurfaceItem container)
    {
        var current = container;
        while (current != null)
        {
            if (IndexFromContainer(current) >= 0)
                return current;

            current = current.FindAncestorOfType<SurfaceItem>();
        }

        return null;
    }

    internal Control ResolveInteractionTarget(SurfaceItem container)
    {
        if (_selectedTargets.Contains(container) || ShouldUseContainerInteraction(LastInputModifiers))
            return container;

        return ResolveSelectionTarget(container);
    }

    /// <summary>
    /// Пересчитывает контейнер, в пределах которого работает текущая рамка выделения.
    /// </summary>
    /// <param name="worldBounds">Прямоугольник рамки в мировых координатах.</param>
    /// <param name="useContainerSelection">Признак работы рамки на уровне контейнеров.</param>
    /// <remarks>
    /// Вызывается на каждом шаге протяжки, поэтому <see cref="MarqueeScope"/> отражает
    /// текущий прямоугольник, а не точку нажатия. Иначе рамка, начатая внутри контейнера,
    /// оставалась бы привязанной к нему при любой протяжке, и её визуальный охват
    /// обещал бы выборку, которой не происходит.
    /// </remarks>
    internal void UpdateMarqueeScope(Rect worldBounds, bool useContainerSelection)
    {
        MarqueeScope = useContainerSelection ? null : FindContainerForMarquee(worldBounds);
    }

    internal void ClearMarqueeScope() => MarqueeScope = null;

    /// <summary>
    /// Выбирает контрол как design target — то же, что клик по нему на поверхности.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Существует ради хоста, у которого есть собственное дерево. Выделение с поверхности он
    /// получал и раньше — через <see cref="DesignSelectionChanged"/>, — а вот обратной дороги не
    /// было вовсе: клик по строке в дереве не мог выбрать контрол на канве. Приложению оставалось
    /// либо лезть во внутренности редактора, либо не иметь дерева.
    /// </para>
    /// <para>
    /// Принимает и сам контейнер: выбрать форму целиком — такое же состояние, как выбрать контрол
    /// внутри неё, и указатель его достаёт. Владелец разрешается тем же путём, что и на пути
    /// указателя, поэтому контрол во вложенном контейнере выбирается, а не отвергается: ближайший
    /// design host у него вложенный, а индекс есть только у item'а верхнего уровня.
    /// </para>
    /// <para>
    /// Оба слоя выбора меняются здесь вместе, и это не осторожность, а необходимость: контейнер,
    /// попавший в <c>Selection</c> без собственной записи в слое target'ов, подменяется вложенным
    /// target'ом по умолчанию, и группа контейнеров молча становится смешанной.
    /// </para>
    /// <para>
    /// Снять выделение этим методом нельзя, и отдельного метода для этого нет, потому что он не
    /// нужен: <c>SelectedItems.Clear()</c> снимает и индексный слой, и слой target'ов — обработчик
    /// на <c>IsSelected</c> перестраивает оверлей, а тот на пустом выборе вычищает target'ы. Сказано
    /// здесь, потому что искать это в другом свойстве никто не догадается.
    /// </para>
    /// <para>
    /// Набор выделяется по одному вызову на контрол, и это стоит одного
    /// <see cref="DesignSelectionChanged"/> на каждый: три строки в дереве — три события и три
    /// перестроения оверлея. Пакетной формы («выделение теперь вот это») пока нет намеренно —
    /// заводить её стоит под потребителя с мультивыбором, а не заранее.
    /// </para>
    /// </remarks>
    /// <param name="target">Контрол или контейнер, который нужно выбрать.</param>
    /// <param name="additive">Добавить к текущему выделению, а не заменить его.</param>
    /// <returns><see langword="true"/>, если контрол редактируем и после вызова выбран.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> равен <see langword="null"/>.</exception>
    public bool SelectDesignTarget(Control target, bool additive = false)
    {
        ArgumentNullException.ThrowIfNull(target);

        return ApplySelection(target, additive ? SelectionIntent.Add : SelectionIntent.Replace);
    }

    /// <summary>
    /// Намерение записи выделения.
    /// </summary>
    private protected enum SelectionIntent
    {
        /// <summary>Заменить выделение целиком.</summary>
        Replace,

        /// <summary>Добавить к текущему выделению.</summary>
        Add
    }

    /// <summary>
    /// Единственная точка записи выделения: пишет оба слоя за одну транзакцию.
    /// </summary>
    /// <remarks>
    /// Оба слоя обязаны меняться вместе, и до появления этой точки правило держалось
    /// в каждой точке записи своими руками — с разными резолверами владельца, разным
    /// набором guard'ов и батчингом в двух местах из десяти. Отсюда и брались состояния,
    /// которые указатель построить не может, а публичный API строил.
    /// <para>
    /// Четыре вещи, которых не делала ни одна прежняя точка записи:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <b>Один резолвер владельца.</b> Владелец — item верхнего уровня, у него есть индекс;
    /// host — ближайший контейнер, он решает редактируемость. Оба ответа нужны, вход один.
    /// </description></item>
    /// <item><description>
    /// <b>Ворота редактируемости, которые нельзя обойти выбором аргумента.</b> Проверка
    /// <c>TargetResolver.IsSelectable(target, FindSurfaceHost(target))</c> в режиме <c>Loaded</c> была
    /// тавтологией — её ветка это <c>ReferenceEquals(FindSurfaceHost(control), owner)</c>,
    /// а owner вызывающий вычислял тем же <c>FindDesignHost</c>. Теперь target обязан
    /// оказаться среди кандидатов своего host'а: ровно тем списком пользуется указатель,
    /// и именно он не спускается внутрь шаблонов.
    /// </description></item>
    /// <item><description>
    /// <b>Оба слоя внутри одного <c>BatchUpdate</c>.</b> Без него замена публиковала
    /// два <see cref="DesignSelectionChanged"/>, первый — с пустым выделением: <c>Clear()</c>
    /// синхронно доходит до обработчика <c>IsSelected</c>, тот пересобирает оверлей и
    /// публикует пустой снимок. Хост, зеркалящий выделение в своё дерево, гасил подсветку
    /// между двумя событиями одного вызова.
    /// </description></item>
    /// <item><description>
    /// <b>Материализация неявного target'а на записи.</b> Контейнер, попавший в индексный
    /// слой без записи в слое target'ов, читался как «выбран его ребёнок по умолчанию» —
    /// и потому <c>additive</c> поверх него молча терял то, к чему добавлял.
    /// </description></item>
    /// </list>
    /// </remarks>
    private protected bool ApplySelection(Control target, SelectionIntent intent)
    {
        // Жест владеет выделением, пока идёт. Вызов извне посреди него ставит
        // контрол в группу, которую перетаскивают, — он уезжает вместе с ней,
        // а лишняя правка попадает в чужую единицу редактирования.
        if (IsSelecting || CurrentState is not EditorIdleState)
            return false;

        var host = target as SurfaceItem is { } item && !ReferenceEquals(item, target)
            ? null
            : FindSurfaceHost(target);

        var owner = ResolveOwningItemForTarget(target);
        if (owner == null)
            return false;

        var index = IndexFromContainer(owner);
        if (index < 0)
            return false;

        if (host != null && !IsEditableTarget(target, host))
            return false;

        if (intent == SelectionIntent.Add && !SharesDesignHostWithSelection(target))
            return false;

        using (Selection.BatchUpdate())
        {
            if (intent == SelectionIntent.Replace)
            {
                Selection.Clear();
                SetSingleSelectedTarget(target);
            }
            else
            {
                MaterialiseImplicitTargets();
                AddSelectedTarget(target);
            }

            Selection.Select(index);
        }

        RefreshSelectionOverlay();

        // Тот же жест, что и у указателя: без фокуса клавиатура до редактора
        // не доходит, и выделение, заданное хостом, нельзя сдвинуть стрелками.
        if (!IsKeyboardFocusWithin)
            Focus();

        return SelectedDesignTargets.Any(selected => ReferenceEquals(selected.Target, target));
    }

    /// <summary>
    /// Записывает выделение из нескольких target'ов одной транзакцией.
    /// </summary>
    /// <remarks>
    /// Пакетная запись была отложена до появления потребителя, и потребитель — группа:
    /// клик по её участнику обязан выбрать всех сразу, а разложить это на вызовы по
    /// одному target'у нельзя, не потеряв гарантию одного события. Правила те же, что
    /// и у записи одного: все участники обязаны жить в одной форме, иначе индексный слой
    /// и слой target'ов разойдутся.
    /// </remarks>
    private protected bool ApplySelection(IReadOnlyList<Control> targets, SelectionIntent intent)
    {
        if (targets.Count == 0)
            return false;

        if (targets.Count == 1)
            return ApplySelection(targets[0], intent);

        if (IsSelecting || CurrentState is not EditorIdleState)
            return false;

        SurfaceItem? host = null;
        foreach (var target in targets)
        {
            var current = FindSurfaceHost(target);
            if (current == null || !IsEditableTarget(target, current))
                return false;

            if (host == null)
                host = current;
            else if (!ReferenceEquals(host, current))
                return false;
        }

        var owner = ResolveOwningItemForTarget(targets[0]);
        if (owner == null)
            return false;

        var index = IndexFromContainer(owner);
        if (index < 0)
            return false;

        using (Selection.BatchUpdate())
        {
            if (intent == SelectionIntent.Replace)
            {
                Selection.Clear();
                _selectedTargets.Clear();
            }
            else
            {
                MaterialiseImplicitTargets();
            }

            foreach (var target in targets)
                AddSelectedTarget(target);

            Selection.Select(index);
        }

        RefreshSelectionOverlay();

        if (!IsKeyboardFocusWithin)
            Focus();

        return true;
    }

    /// <summary>
    /// Проверяет, что target вообще редактируем в своём host'е.
    /// </summary>
    /// <remarks>
    /// Мало спросить <c>IsSelectableTarget</c>: в режиме <c>Loaded</c> он отсекает
    /// только чужой контейнер, а внутренности шаблонов отсекает сам обход авторской
    /// разметки. Указателю этого хватает, потому что он и берёт кандидатов из обхода;
    /// публичному входу target приносят снаружи, поэтому обход нужно спросить явно.
    /// </remarks>
    private protected bool IsEditableTarget(Control target, SurfaceItem host)
    {
        if (!TargetResolver.IsSelectable(target, host))
            return false;

        foreach (var candidate in TargetResolver.EnumerateCandidates(host))
        {
            if (ReferenceEquals(candidate, target))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Записывает неявные target'ы контейнеров, выбранных только в индексном слое.
    /// </summary>
    /// <remarks>
    /// Такой контейнер читается через <see cref="ResolveSelectionTargets"/> как его
    /// ребёнок по умолчанию, но в <c>_selectedTargets</c> его нет — и добавление
    /// к выделению теряло его молча. Материализация делает чтение чистым.
    /// </remarks>
    private protected void MaterialiseImplicitTargets()
    {
        var items = SelectedItems;
        if (items == null)
            return;

        foreach (var item in items)
        {
            if (ContainerFromItem(item) is not SurfaceItem container)
                continue;

            var owned = false;
            foreach (var selected in _selectedTargets)
            {
                if (!IsOwnedByContainer(selected, container))
                    continue;

                owned = true;
                break;
            }

            if (!owned)
                AddSelectedTarget(ResolveDefaultSelectionTarget(container));
        }
    }

    private protected Control ResolveSelectionTarget(SurfaceItem item)
    {
        // Первый по приоритету target, принадлежащий этому item'у.
        // Сам item в списке означает, что он выбран целиком.
        foreach (var selected in _selectedTargets)
        {
            if (IsOwnedByContainer(selected, item))
                return selected;
        }

        return ResolveDefaultSelectionTarget(item);
    }

    private protected Control ResolveDefaultSelectionTarget(SurfaceItem item)
    {
        foreach (var control in TargetResolver.EnumerateCandidates(item))
        {
            if (TargetResolver.IsSelectable(control, item))
                return control;
        }

        return item;
    }

    private protected bool TryResolveSelectionTargetAtPoint(SurfaceItem item, Point worldPoint, out Control target)
    {
        Control? bestMatch = null;
        Rect bestBounds = default;
        var bestDepth = -1;

        foreach (var control in TargetResolver.EnumerateCandidates(item))
        {
            if (!TargetResolver.IsSelectable(control, item))
                continue;

            if (!Geometry.TryGetBounds(control, out var bounds) || !bounds.Contains(worldPoint))
                continue;

            var depth = GetVisualDepth(control, item);
            if (bestMatch == null ||
                depth > bestDepth ||
                (depth == bestDepth && bounds.Width * bounds.Height < bestBounds.Width * bestBounds.Height))
            {
                bestMatch = control;
                bestBounds = bounds;
                bestDepth = depth;
            }
        }

        if (bestMatch == null)
        {
            target = null!;
            return false;
        }

        target = bestMatch;
        return true;
    }

    private protected static int GetVisualDepth(Control control, Visual root)
    {
        var depth = 0;
        var current = control as Visual;
        while (current != null && !ReferenceEquals(current, root))
        {
            depth++;
            current = current.GetVisualParent();
        }

        return depth;
    }

    private protected void CleanupSelectionTargets()
    {
        if (_selectedTargets.Count == 0)
            return;

        var selectedContainers = new HashSet<SurfaceItem>();
        var items = SelectedItems;
        if (items != null)
        {
            foreach (var item in items)
            {
                var container = ContainerFromItem(item) as SurfaceItem;
                if (container == null && item is SurfaceItem directItem)
                    container = directItem;

                if (container != null)
                    selectedContainers.Add(container);
            }
        }

        // Target выживает, пока его владелец верхнего уровня остаётся выбранным.
        // Владелец вычисляется по дереву, поэтому правило одинаково работает
        // для любой глубины вложенности.
        _selectedTargets.RemoveAll(target =>
        {
            var owner = ResolveOwningItemForTarget(target);
            return owner == null || !selectedContainers.Contains(owner);
        });
    }

    internal IReadOnlyList<Control> ResolveSelectionTargets(SurfaceItem item)
    {
        // Targets этого item'а в порядке приоритета. Сам item в списке означает,
        // что он выбран целиком; вложенные контейнеры попадают сюда наравне
        // с обычными контролами.
        List<Control>? owned = null;
        foreach (var target in _selectedTargets)
        {
            if (!IsOwnedByContainer(target, item))
                continue;

            (owned ??= new List<Control>()).Add(target);
        }

        // Item выбран, а вложенного target'а никто не называл — значит выбрана форма
        // целиком. Прежний ответ «его первый ребёнок» был неявным target'ом: из-за него
        // хост, выбравший форму через SelectedIndex, читал Scope как NestedTarget,
        // а группа контейнеров молча становилась смешанной.
        return owned ?? (IReadOnlyList<Control>)new Control[] { item };
    }

    /// <summary>
    /// Публикует новый снимок выделения, если он действительно отличается от текущего.
    /// </summary>
    /// <remarks>
    /// <c>UpdateSelectionOverlayState</c> вызывается из двух десятков мест,
    /// в том числе на каждом кадре перетаскивания и на каждое изменение геометрии.
    /// Снимок при этом пересобирается всегда, но выделение меняется редко, поэтому
    /// без этой проверки и свойства, и событие срабатывали бы на изменение геометрии.
    /// </remarks>
    private protected void ApplySelectionSnapshot(IReadOnlyList<DesignSelectionTarget> next)
    {
        var previous = _selectedDesignTargets;
        if (AreSameTargets(previous, next))
            return;

        var previousPrimary = _primarySelectionTarget;

        SelectedDesignTargets = next;
        PrimarySelectionTarget = next.Count > 0 ? next[0] : null;

        var handler = DesignSelectionChanged;
        if (handler == null)
            return;

        handler(this, new DesignSelectionChangedEventArgs(
            previous,
            next,
            Difference(next, previous),
            Difference(previous, next),
            previousPrimary,
            PrimarySelectionTarget));
    }

    /// <summary>
    /// Сравнивает наборы по контролам, а не по обёрткам <see cref="DesignSelectionTarget"/>:
    /// обёртки пересоздаются на каждой пересборке.
    /// </summary>
    /// <remarks>
    /// Вместе с контролом сравнивается и его группа: правило одно — <b>публикуется всё,
    /// что сравнивается</b>. Группировка уже выбранного набора не меняет ни состав, ни
    /// порядок, поэтому сравнение по одним target'ам признало бы снимок неизменившимся,
    /// и хост, читающий <see cref="DesignSelectionTarget.GroupId"/>, остался бы со старым
    /// значением.
    /// </remarks>
    private protected static bool AreSameTargets(
        IReadOnlyList<DesignSelectionTarget> left,
        IReadOnlyList<DesignSelectionTarget> right)
    {
        if (left.Count != right.Count)
            return false;

        // Порядок значим: первый элемент — primary target.
        for (var i = 0; i < left.Count; i++)
        {
            if (!ReferenceEquals(left[i].Target, right[i].Target))
                return false;

            if (!string.Equals(left[i].GroupId, right[i].GroupId, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private protected static IReadOnlyList<DesignSelectionTarget> Difference(
        IReadOnlyList<DesignSelectionTarget> source,
        IReadOnlyList<DesignSelectionTarget> exclude)
    {
        List<DesignSelectionTarget>? result = null;

        for (var i = 0; i < source.Count; i++)
        {
            var candidate = source[i];
            var found = false;

            for (var j = 0; j < exclude.Count; j++)
            {
                if (ReferenceEquals(exclude[j].Target, candidate.Target))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                (result ??= new List<DesignSelectionTarget>()).Add(candidate);
        }

        return result ?? (IReadOnlyList<DesignSelectionTarget>)Array.Empty<DesignSelectionTarget>();
    }

    private protected IReadOnlyList<DesignSelectionTarget> CreateSelectionTargetsSnapshot(SurfaceItem? primaryItem, Control? primaryControl)
    {
        var result = new List<DesignSelectionTarget>();
        var dedup = new HashSet<Control>();

        if (primaryItem != null && primaryControl != null && dedup.Add(primaryControl))
            result.Add(new DesignSelectionTarget(primaryItem, primaryControl, GetGroupKey(primaryControl)));

        var items = SelectedItems;
        if (items == null)
            return result;

        foreach (var item in items)
        {
            var container = ContainerFromItem(item) as SurfaceItem;
            if (container == null && item is SurfaceItem directItem)
                container = directItem;

            if (container == null)
                continue;

            foreach (var target in ResolveSelectionTargets(container))
            {
                if (!dedup.Add(target))
                    continue;

                result.Add(new DesignSelectionTarget(container, target, GetGroupKey(target)));
            }
        }

        return result;
    }

    private protected IEnumerable<Control> EnumerateSelectedTargets() => _selectedTargets;

    /// <summary>
    /// Проверяет, что target лежит в том же design host, что и всё текущее выделение.
    /// </summary>
    /// <remarks>
    /// Правило точнее, чем «один item верхнего уровня»: два контрола из соседних
    /// вложенных контейнеров принадлежат одному item'у, но разным host'ам,
    /// и группировать их вместе нельзя.
    /// </remarks>
    private protected bool SharesDesignHostWithSelection(Control target)
    {
        var host = FindSurfaceHost(target);

        foreach (var selected in EnumerateSelectedTargets())
        {
            if (ReferenceEquals(selected, target))
                continue;

            if (!ReferenceEquals(FindSurfaceHost(selected), host))
                return false;
        }

        return true;
    }

    internal bool CanAddNestedTargetToContainer(SurfaceItem container)
    {
        var items = SelectedItems;
        if (items == null || items.Count == 0)
            return true;

        SurfaceItem? owner = null;
        foreach (var item in items)
        {
            var selectedContainer = ContainerFromItem(item) as SurfaceItem;
            if (selectedContainer == null && item is SurfaceItem directItem)
                selectedContainer = directItem;

            if (selectedContainer == null)
                continue;

            if (owner == null)
            {
                owner = selectedContainer;
                continue;
            }

            if (!ReferenceEquals(owner, selectedContainer))
                return false;
        }

        return owner == null || ReferenceEquals(owner, container);
    }

    private protected static bool IsOwnedByContainer(Visual visual, SurfaceItem container)
    {
        var current = visual;
        while (current != null)
        {
            if (ReferenceEquals(current, container))
                return true;

            current = current.GetVisualParent();
        }

        return false;
    }

    /// <summary>
    /// Перечисляет контейнеры редактора на любой глубине вложенности.
    /// </summary>
    /// <remarks>
    /// Во время жеста, снявшего снимок через <see cref="BeginContainerSnapshot"/>,
    /// отдаёт этот снимок вместо нового обхода.
    /// </remarks>
    internal IEnumerable<SurfaceItem> EnumerateContainers() =>
        _containerSnapshot ?? EnumerateContainersCore();

    /// <summary>
    /// Снимает список контейнеров на время жеста.
    /// </summary>
    /// <remarks>
    /// Обход стоит не по числу контейнеров, а по размеру всего макета: <see cref="EnumerateContainersCore"/>
    /// спускается в поддерево каждого контейнера верхнего уровня, а рамка спрашивает его на
    /// каждом кадре протяжки. Цена поэтому растёт вместе с макетом, а не с числом форм,
    /// и упирается в неё как раз тот проект, который дорос до неё. Стенд, на котором это
    /// видно, лежит в тестах (<c>MarqueeCostProbeTests</c>) и печатает свои числа: он же
    /// показывает, что со снимком размер форм на цену кадра перестаёт влиять вовсе.
    /// <para>
    /// Снимок берётся один раз по той же причине, что и соседи в <c>BeginSnapGuides</c>,
    /// и это не только про цену: набор контейнеров внутри жеста меняться не должен, иначе
    /// рамка охватывала бы одно, а применяла к другому. Окно снимка накрывает и
    /// <c>CommitSelection</c> — он вызывается до выхода из состояния, — поэтому
    /// применённое выделение считается по тем же контейнерам, которые рамка измеряла.
    /// </para>
    /// </remarks>
    internal void BeginContainerSnapshot() => _containerSnapshot = EnumerateContainersCore().ToList();

    /// <summary>
    /// Отпускает снимок контейнеров: следующий обход снова идёт по дереву.
    /// </summary>
    internal void EndContainerSnapshot() => _containerSnapshot = null;

    private protected IEnumerable<SurfaceItem> EnumerateContainersCore()
    {
        if (Presenter?.Panel == null)
            yield break;

        foreach (var child in Presenter.Panel.Children)
        {
            if (child is not SurfaceItem container)
                continue;

            yield return container;

            foreach (var descendant in container.GetVisualDescendants())
            {
                if (descendant is SurfaceItem nested)
                    yield return nested;
            }
        }
    }

    /// <summary>
    /// Возвращает геометрию контейнера в мировых координатах.
    /// </summary>
    /// <remarks>
    /// Для вложенных контейнеров <see cref="SurfaceItem.Location"/> задан
    /// относительно родительской панели, поэтому позиция берётся из design-геометрии,
    /// а на неё падает fallback только для контейнеров верхнего уровня.
    /// </remarks>
    private protected bool TryGetContainerWorldBounds(SurfaceItem container, out Rect bounds)
    {
        if (Geometry.TryGetBounds((Control)container, out bounds))
            return true;

        if (container.Bounds.Width <= 0 || container.Bounds.Height <= 0)
        {
            bounds = default;
            return false;
        }

        bounds = new Rect(container.Location, container.Bounds.Size);
        return true;
    }

    private protected static int GetContainerDepth(SurfaceItem container)
    {
        var depth = 0;
        var current = container.GetVisualParent();

        while (current != null)
        {
            if (current is SurfaceItem)
                depth++;

            current = current.GetVisualParent();
        }

        return depth;
    }

    private protected SurfaceItem? FindContainerAtWorldPoint(Point worldPoint)
    {
        SurfaceItem? bestMatch = null;
        var bestDepth = -1;

        foreach (var container in EnumerateContainers())
        {
            if (!TryGetContainerWorldBounds(container, out var bounds) || !bounds.Contains(worldPoint))
                continue;

            // Глубочайший контейнер под точкой: вложенный перекрывает владельца.
            var depth = GetContainerDepth(container);
            if (depth > bestDepth)
            {
                bestMatch = container;
                bestDepth = depth;
            }
        }

        return bestMatch;
    }

    private protected SurfaceItem? FindContainerForMarquee(Rect bounds)
    {
        // Владельцем рамки становится самый глубокий контейнер, который целиком её
        // содержит: рамка внутри вложенного контейнера работает в его пределах,
        // а рамка, вышедшая за его границы, поднимается к владельцу.
        SurfaceItem? containing = null;
        var containingDepth = -1;

        SurfaceItem? bestOverlap = null;
        var bestArea = 0.0;

        foreach (var container in EnumerateContainers())
        {
            if (!TryGetContainerWorldBounds(container, out var containerBounds))
                continue;

            if (containerBounds.Contains(bounds))
            {
                var depth = GetContainerDepth(container);
                if (depth > containingDepth)
                {
                    containing = container;
                    containingDepth = depth;
                }

                continue;
            }

            var intersection = containerBounds.Intersect(bounds);
            if (intersection.Width <= 0 || intersection.Height <= 0)
                continue;

            var area = intersection.Width * intersection.Height;
            if (area > bestArea)
            {
                bestArea = area;
                bestOverlap = container;
            }
        }

        return containing ?? bestOverlap;
    }

    /// <summary>
    /// Решает, набирает ли рамка контейнеры целиком.
    /// </summary>
    /// <param name="viewportPoint">Точка нажатия в координатах редактора.</param>
    /// <param name="modifiers">Модификаторы на момент нажатия.</param>
    /// <remarks>
    /// Рамка, начатая на пустом холсте, набирает формы, а не их содержимое: снаружи
    /// контейнеров выбирать содержимое не за что — пользователь видит формы и обводит
    /// формы. Начатая внутри формы — работает в её пределах, как и раньше.
    /// <para>
    /// <see cref="DesignEditorInputGestures.ContainerInteractionModifiers"/> остаётся
    /// способом потребовать контейнеров и изнутри формы.
    /// </para>
    /// <para>
    /// Спрашивается один раз, на входе в жест: режим рамки не должен меняться посреди
    /// протяжки — в отличие от её владельца, который пересчитывается каждый кадр.
    /// </para>
    /// </remarks>
    internal bool ShouldUseContainerMarquee(Point viewportPoint, KeyModifiers modifiers)
    {
        if (ShouldUseContainerInteraction(modifiers))
            return true;

        return FindContainerAtWorldPoint(GetWorldPosition(viewportPoint)) == null;
    }

    /// <summary>
    /// Идентификатор количества выбранных design targets.
    /// </summary>
    public static readonly DirectProperty<SurfaceView, int> SelectedDesignTargetsCountProperty =
        AvaloniaProperty.RegisterDirect<SurfaceView, int>(
            nameof(SelectedDesignTargetsCount),
            o => o.SelectedDesignTargetsCount);

    private protected int _selectedDesignTargetsCount;

    /// <summary>
    /// Получает количество выбранных design targets.
    /// </summary>
    public int SelectedDesignTargetsCount => _selectedDesignTargetsCount;

    /// <summary>
    /// Определяет, должен ли контейнер уступить нажатие рамке выделения.
    /// </summary>
    /// <param name="container">Контейнер, получивший нажатие. Может быть вложенным.</param>
    /// <param name="viewportPoint">Точка нажатия в координатах редактора.</param>
    /// <param name="modifiers">Модификаторы ввода.</param>
    /// <remarks>
    /// Решение принимает редактор, а не состояние контейнера: политика ввода живёт
    /// в <see cref="SurfaceView.InputGestures"/>, и контейнеру знать о ней незачем. Уступив жест,
    /// контейнер не захватывает указатель и не помечает событие обработанным,
    /// поэтому нажатие всплывает до редактора обычным маршрутом.
    /// <para>
    /// Контейнер удерживает жест, если нажат <see cref="DesignEditorInputGestures.ContainerInteractionModifiers"/>,
    /// если контейнер уже выбран целиком, либо если под точкой есть design target.
    /// </para>
    /// </remarks>
    internal bool ShouldDeferPressToMarquee(SurfaceItem container, Point viewportPoint, KeyModifiers modifiers)
    {
        if (InputGestures.ContainerEmptyAreaDrag != ContainerEmptyAreaDragGesture.Marquee)
            return false;

        if (ShouldUseContainerInteraction(modifiers))
            return false;

        // Уже выбранный контейнер перетаскивается без модификаторов —
        // иначе его нельзя было бы двигать мышью вовсе.
        if (_selectedTargets.Contains(container))
            return false;

        var worldPoint = GetWorldPosition(viewportPoint);
        return !TryResolveSelectionTargetAtPoint(container, worldPoint, out _);
    }

    internal void UpdateSelectionTargetFromPoint(SurfaceItem container, Point screenPoint, KeyModifiers modifiers, int clickCount = 1)
    {
        // Оба слоя пишутся одной транзакцией. Раньше индексный слой писало состояние
        // контейнера, а этот метод дописывал слой target'ов уже после — и между двумя
        // записями оверлей успевал пересобраться на промежуточном состоянии.
        using (Selection.BatchUpdate())
        {
            if (!container.IsSelected)
            {
                if (!ShouldUseAdditiveSelection(modifiers))
                    Selection.Clear();

                var ownerIndex = IndexFromContainer(container);
                if (ownerIndex >= 0)
                    Selection.Select(ownerIndex);
            }

            ApplyTargetFromPoint(container, screenPoint, modifiers, clickCount);
        }

        RefreshSelectionOverlay();
    }

    /// <summary>
    /// Пишет слой target'ов по точке нажатия внутри контейнера.
    /// </summary>
    /// <remarks>
    /// Правило ядра — уровень контейнеров: аддитивный клик добавляет контейнер к выбору
    /// или снимает его, обычный — выбирает только его. Слой, у которого внутри контейнера
    /// есть свои target'ы, переопределяет правило целиком.
    /// <para>
    /// Оверлей отсюда не пересобирается: это половина транзакции, и пересборка
    /// на её середине публиковала бы состояние, которого пользователь не просил.
    /// </para>
    /// </remarks>
    private protected virtual void ApplyTargetFromPoint(SurfaceItem container, Point screenPoint, KeyModifiers modifiers, int clickCount)
    {
        if (ShouldUseAdditiveSelection(modifiers))
        {
            ToggleTargetInSelection(container);
            SyncContainerItemSelection(container);
            return;
        }

        SetSingleSelectedTarget(container);
    }
}
