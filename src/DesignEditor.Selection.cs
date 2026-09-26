using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DesignLayout = ArxisStudio.Surface.UiDesigner.Layout;
using DesignInteraction = ArxisStudio.Surface.Editing.DesignInteraction;
using ArxisStudio.Controls;
using ArxisStudio.Surface.Editing;
using ArxisStudio.Surface.UiDesigner.Placement;
using ArxisStudio.States;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio;

// Двухуровневое выделение: запись, чтение и публикация.
// Часть DesignEditor; общее описание типа — в DesignEditor.cs.
public partial class DesignEditor
{
    internal void CommitSelection(Rect bounds, bool isCtrlPressed)
        => CommitSelection(bounds, isCtrlPressed, ShouldUseContainerInteraction(LastInputModifiers));

    /// <summary>
    /// Применяет выделение по прямоугольнику рамки.
    /// </summary>
    /// <param name="bounds">Прямоугольник в мировых координатах.</param>
    /// <param name="isCtrlPressed">Признак добавления к текущему выделению.</param>
    /// <param name="useContainerSelection">
    /// Признак работы на уровне контейнеров. Передаётся явно, а не читается из
    /// <see cref="LastInputModifiers"/>: режим фиксируется в момент нажатия, иначе
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
                    if (child is not DesignEditorItem container)
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

        UpdateSelectionOverlayState();
    }

    /// <summary>
    /// Выбирает design targets внутри <paramref name="scope"/>, попавшие в рамку.
    /// </summary>
    /// <param name="scope">Контейнер, в пределах которого ищутся targets. Может быть вложенным.</param>
    /// <param name="ownerItem">Item верхнего уровня, на который адресуется индексный выбор.</param>
    /// <param name="bounds">Прямоугольник рамки в мировых координатах.</param>
    /// <param name="isAdditive">Признак добавления к текущему выбору.</param>
    /// <returns><see langword="true"/>, если хотя бы один target попал в выделение.</returns>
    private bool CommitMarqueeWithinContainer(
        DesignEditorItem scope,
        DesignEditorItem? ownerItem,
        Rect bounds,
        bool isAdditive)
    {
        ownerItem ??= ResolveOwningItem(scope);
        if (ownerItem == null)
            return false;

        var nestedTargets = new List<Control>();
        foreach (var target in EnumerateSelectionCandidates(scope))
        {
            if (!IsSelectableTarget(target, scope))
                continue;

            // Рамка выбирает соседей внутри одного host'а, а не смесь уровней:
            // иначе в выборку попадали бы и вложенный контейнер, и его содержимое.
            if (!ReferenceEquals(FindDesignHost(target), scope))
                continue;

            if (TryGetDesignBounds(target, out var targetBounds) && bounds.Intersects(targetBounds))
                nestedTargets.Add(target);
        }

        if (nestedTargets.Count == 0)
            return false;

        foreach (var target in nestedTargets)
            AddSelectedTarget(target);

        Selection.Select(IndexFromContainer(ownerItem));
        return true;
    }

    /// <summary>
    /// Держит подписки на свойства текущих selection targets в актуальном состоянии.
    /// </summary>
    /// <remarks>
    /// Подписка идёт на разрешённые targets из <see cref="SelectedDesignTargets"/>,
    /// а не на <c>_selectedTargets</c>: если у выбранного item'а нет явного target,
    /// его геометрию задаёт default target, и следить нужно за ним.
    /// </remarks>
    private void SyncSelectedTargetSubscriptions()
    {
        var targets = SelectedDesignTargets;

        for (var i = _subscribedTargets.Count - 1; i >= 0; i--)
        {
            var subscribed = _subscribedTargets[i];

            var stillSelected = false;
            for (var j = 0; j < targets.Count; j++)
            {
                if (ReferenceEquals(targets[j].Target, subscribed))
                {
                    stillSelected = true;
                    break;
                }
            }

            if (stillSelected)
                continue;

            subscribed.PropertyChanged -= OnSelectedTargetPropertyChanged;
            _subscribedTargets.RemoveAt(i);
        }

        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i].Target;
            if (_subscribedTargets.Contains(target))
                continue;

            target.PropertyChanged += OnSelectedTargetPropertyChanged;
            _subscribedTargets.Add(target);
        }
    }

    private void OnSelectedTargetPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty ||
            e.Property == DesignLayout.DesignXProperty ||
            e.Property == DesignLayout.DesignYProperty ||
            e.Property == DesignInteraction.ResizePolicyProperty ||
            e.Property == DesignInteraction.MovePolicyProperty)
        {
            UpdateSelectionOverlayState();
        }
    }

    private void AddSelectedTarget(Control target)
    {
        if (!_selectedTargets.Contains(target))
            _selectedTargets.Add(target);
    }

    private void SetSingleSelectedTarget(Control target)
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
    private void ToggleClusterInSelection(IReadOnlyList<Control> cluster)
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

    private void ToggleTargetInSelection(Control target)
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
    private void SyncContainerItemSelection(DesignEditorItem container)
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
    private DesignEditorItem? ResolveOwningItemForTarget(Control target)
    {
        var container = target as DesignEditorItem ?? FindDesignHost(target);
        return container == null ? null : ResolveOwningItem(container);
    }

    private void UpdateSelectionAdornerPolicies()
    {
        var primaryResizePolicy = _primarySelectionControl != null
            ? GetResizePolicy(_primarySelectionControl)
            : ArxisStudio.Surface.ResizePolicy.None;
        var primaryMovePolicy = _primarySelectionControl != null
            ? GetEffectiveMovePolicy(_primarySelectionControl)
            : ArxisStudio.Surface.MovePolicy.None;

        if (_selectionAdorner != null)
        {
            _selectionAdorner.ResizePolicy = primaryResizePolicy;
            _selectionAdorner.MovePolicy = primaryMovePolicy;
        }

        var groupResizePolicy = ArxisStudio.Surface.ResizePolicy.None;
        var groupMovePolicy = ArxisStudio.Surface.MovePolicy.None;

        // Условие обязано совпадать с тем, по которому шаблон показывает рамку.
        // Locked-визуал — это не отдельное оформление, а adorner с политиками
        // None/None: рамка, которой политики не посчитали, выглядит заблокированной
        // и ручки у неё неинтерактивны. Одна причина на оба симптома.
        if (ShowsGroupFrame && SelectedDesignTargets.Count > 1)
        {
            groupResizePolicy = ArxisStudio.Surface.ResizePolicy.All;
            groupMovePolicy = ArxisStudio.Surface.MovePolicy.Both;

            foreach (var selectedTarget in SelectedDesignTargets)
            {
                groupResizePolicy &= GetResizePolicy(selectedTarget.Target);
                groupMovePolicy &= GetEffectiveMovePolicy(selectedTarget.Target);
            }
        }

        if (_groupSelectionAdorner != null)
        {
            _groupSelectionAdorner.ResizePolicy = groupResizePolicy;
            _groupSelectionAdorner.MovePolicy = groupMovePolicy;
        }
    }

    internal void UpdateSelectionTargetFromPoint(DesignEditorItem container, Point screenPoint, KeyModifiers modifiers, int clickCount = 1)
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

        UpdateSelectionOverlayState();
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
    internal void CollapseSelectionTo(DesignEditorItem container)
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
    /// Пишет слой design target'ов по точке нажатия.
    /// </summary>
    /// <remarks>
    /// Оверлей отсюда не пересобирается: это половина транзакции, и пересборка
    /// на её середине публиковала бы состояние, которого пользователь не просил.
    /// </remarks>
    private void ApplyTargetFromPoint(DesignEditorItem container, Point screenPoint, KeyModifiers modifiers, int clickCount)
    {
        if (ShouldUseContainerInteraction(modifiers))
        {
            if (ShouldUseAdditiveSelection(modifiers))
            {
                // Группа контейнеров набирается тем же правилом, что и группа
                // вложенных target'ов. Раньше эта ветка всегда заменяла выбор,
                // и добавить второй контейнер кликом было нельзя вовсе.
                ToggleTargetInSelection(container);
                SyncContainerItemSelection(container);
            }
            else
            {
                SetSingleSelectedTarget(container);
            }

            return;
        }

        var worldPoint = GetWorldPosition(screenPoint);
        if (!TryResolveSelectionTargetAtPoint(container, worldPoint, out var target))
        {
            // Клик по области без designer-metadata внутри контейнера
            // переводит selection target на уровень контейнера.
            SetSingleSelectedTarget(container);
            return;
        }

        // Вход в группу и выход из неё решаются до записи: двойной клик открывает
        // группу под курсором, любой клик мимо неё закрывает открытую. Иначе режим
        // пережил бы жест, ради которого его включали.
        UpdateEnteredGroup(target, clickCount);

        var isAdditive = ShouldUseAdditiveSelection(modifiers);
        // Грубая проверка по владельцу верхнего уровня плюс точная по design host:
        // target уже известен, поэтому уровень вложенности можно сверить честно.
        if (isAdditive && (!CanAddNestedTargetToContainer(container) || !SharesDesignHostWithSelection(target)))
            return;

        var groupHost = FindDesignHost(target) ?? container;
        if (!isAdditive)
        {
            // Клик по участнику закрытой группы выбирает её целиком.
            if (ExpandToGroup(groupHost, target) is { Count: > 1 } members)
            {
                _selectedTargets.Clear();
                foreach (var member in members)
                    AddSelectedTarget(member);

                return;
            }

            // Внутри открытой группы клик выбирает участника поодиночке. Общее правило
            // «клик по уже выбранному участнику группы её не схлопывает» здесь не годится:
            // ради этого в группу и входили.
            if (IsInsideEnteredGroup(target))
            {
                SetSingleSelectedTarget(target);
                return;
            }
        }

        if (isAdditive)
        {
            // Группа остаётся одним элементом и когда её добавляют к уже выбранному:
            // раскрытие до кластера — свойство клика по участнику, а не свойство
            // замены выделения.
            ToggleClusterInSelection(ExpandToGroup(groupHost, target));
        }
        else
        {
            var index = _selectedTargets.IndexOf(target);
            if (_selectedTargets.Count > 1 && index >= 0)
            {
                // Обычный клик по уже выбранному target внутри группы
                // не должен схлопывать multi-selection. Переносим target в начало,
                // чтобы он стал primary selection target.
                if (index > 0)
                {
                    _selectedTargets.RemoveAt(index);
                    _selectedTargets.Insert(0, target);
                }
            }
            else
            {
                SetSingleSelectedTarget(target);
            }
        }
    }

    /// <summary>
    /// Возвращает <see cref="DesignEditorItem"/> верхнего уровня, которому принадлежит
    /// указанный контейнер, либо <see langword="null"/>, если он не в этом редакторе.
    /// </summary>
    /// <remarks>
    /// Контейнеры могут быть вложены друг в друга, но индексная модель выбора Avalonia
    /// знает только контейнеры собственного <c>ItemsSource</c>. Поэтому выбор всегда
    /// маршрутизируется на владеющий item верхнего уровня, а сам вложенный контейнер
    /// участвует как design target — это и даёт дерево любой глубины без отказа от
    /// <see cref="SelectingItemsControl"/>.
    /// </remarks>
    internal DesignEditorItem? ResolveOwningItem(DesignEditorItem container)
    {
        var current = container;
        while (current != null)
        {
            if (IndexFromContainer(current) >= 0)
                return current;

            current = current.FindAncestorOfType<DesignEditorItem>();
        }

        return null;
    }

    internal Control ResolveInteractionTarget(DesignEditorItem container)
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
    private enum SelectionIntent
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
    /// <c>IsSelectableTarget(target, FindDesignHost(target))</c> в режиме <c>Loaded</c> была
    /// тавтологией — её ветка это <c>ReferenceEquals(FindDesignHost(control), owner)</c>,
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
    private bool ApplySelection(Control target, SelectionIntent intent)
    {
        // Жест владеет выделением, пока идёт. Вызов извне посреди него ставит
        // контрол в группу, которую перетаскивают, — он уезжает вместе с ней,
        // а лишняя правка попадает в чужую единицу редактирования.
        if (IsSelecting || CurrentState is not EditorIdleState)
            return false;

        var host = target as DesignEditorItem is { } item && !ReferenceEquals(item, target)
            ? null
            : FindDesignHost(target);

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

        UpdateSelectionOverlayState();

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
    private bool ApplySelection(IReadOnlyList<Control> targets, SelectionIntent intent)
    {
        if (targets.Count == 0)
            return false;

        if (targets.Count == 1)
            return ApplySelection(targets[0], intent);

        if (IsSelecting || CurrentState is not EditorIdleState)
            return false;

        DesignEditorItem? host = null;
        foreach (var target in targets)
        {
            var current = FindDesignHost(target);
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

        UpdateSelectionOverlayState();

        if (!IsKeyboardFocusWithin)
            Focus();

        return true;
    }

    /// <summary>
    /// Проверяет, что target вообще редактируем в своём host'е.
    /// </summary>
    /// <remarks>
    /// Мало спросить <see cref="IsSelectableTarget"/>: в режиме <c>Loaded</c> он отсекает
    /// только чужой контейнер, а внутренности шаблонов отсекает сам обход авторской
    /// разметки. Указателю этого хватает, потому что он и берёт кандидатов из обхода;
    /// публичному входу target приносят снаружи, поэтому обход нужно спросить явно.
    /// </remarks>
    private static bool IsEditableTarget(Control target, DesignEditorItem host)
    {
        if (!IsSelectableTarget(target, host))
            return false;

        foreach (var candidate in EnumerateSelectionCandidates(host))
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
    private void MaterialiseImplicitTargets()
    {
        var items = SelectedItems;
        if (items == null)
            return;

        foreach (var item in items)
        {
            if (ContainerFromItem(item) is not DesignEditorItem container)
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

    private static Control ResolveSelectionTarget(DesignEditorItem item)
    {
        if (item.FindAncestorOfType<DesignEditor>() is { } editor)
        {
            // Первый по приоритету target, принадлежащий этому item'у.
            // Сам item в списке означает, что он выбран целиком.
            foreach (var selected in editor._selectedTargets)
            {
                if (IsOwnedByContainer(selected, item))
                    return selected;
            }
        }

        return ResolveDefaultSelectionTarget(item);
    }

    private static Control ResolveDefaultSelectionTarget(DesignEditorItem item)
    {
        foreach (var control in EnumerateSelectionCandidates(item))
        {
            if (IsSelectableTarget(control, item))
                return control;
        }

        return item;
    }

    private static Control ResolveSelectionTarget(DesignEditorItem item, Visual? source)
    {
        var current = source;
        while (current != null && !ReferenceEquals(current, item))
        {
            if (current is Control control &&
                IsOwnedByContainer(control, item) &&
                HasDesignerLayoutMetadata(control))
            {
                return control;
            }

            current = current.GetVisualParent();
        }

        return ResolveSelectionTarget(item);
    }

    private bool TryResolveSelectionTargetAtPoint(DesignEditorItem item, Point worldPoint, out Control target)
    {
        Control? bestMatch = null;
        Rect bestBounds = default;
        var bestDepth = -1;

        foreach (var control in EnumerateSelectionCandidates(item))
        {
            if (!IsSelectableTarget(control, item))
                continue;

            if (!TryGetDesignBounds(control, out var bounds) || !bounds.Contains(worldPoint))
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

    private static Control ResolveSelectionTarget(Control root)
    {
        if (HasDesignerLayoutMetadata(root))
            return root;

        foreach (var descendant in root.GetVisualDescendants())
        {
            if (descendant is Control control && HasDesignerLayoutMetadata(control))
                return control;
        }

        return root;
    }

    /// <summary>
    /// Определяет, может ли контрол быть design target внутри указанного контейнера.
    /// </summary>
    /// <remarks>
    /// В режиме <see cref="DesignContentMode.Loaded"/> размечать содержимое некому,
    /// поэтому редактируется всё, что автор написал в разметке. Внутренности
    /// контролов при этом отсекает <c>TemplatedParent</c>: у частей шаблона он задан,
    /// у элементов из <c>.axaml</c> — нет. Иначе клик по кнопке выбирал бы её
    /// внутренний <c>TextBlock</c>.
    /// </remarks>
    private static bool IsSelectableTarget(Control control, DesignEditorItem owner)
    {
        if (owner.ContentMode != DesignContentMode.Loaded)
            return HasDesignerLayoutMetadata(control);

        // Внутренности контролов отсекает уже сам обход авторской разметки,
        // здесь остаётся только не залезть в чужой контейнер.
        return ReferenceEquals(FindDesignHost(control), owner);
    }

    private static bool HasDesignerLayoutMetadata(Control control)
    {
        return DesignLayout.GetIsTracked(control)
            || !double.IsNaN(DesignLayout.GetX(control))
            || !double.IsNaN(DesignLayout.GetY(control));
    }

    internal static void EnsureTracked(Control control)
    {
        // Track идемпотентен. Прежний guard по GetIsTracked не работал:
        // Track не выставляет публичное IsTracked, поэтому для контролов
        // с одними Layout.X/Y условие было истинно всегда.
        DesignLayout.Track(control);
    }

    private static int GetVisualDepth(Control control, Visual root)
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

    private void CleanupSelectionTargets()
    {
        if (_selectedTargets.Count == 0)
            return;

        var selectedContainers = new HashSet<DesignEditorItem>();
        var items = SelectedItems;
        if (items != null)
        {
            foreach (var item in items)
            {
                var container = ContainerFromItem(item) as DesignEditorItem;
                if (container == null && item is DesignEditorItem directItem)
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

    internal IReadOnlyList<Control> ResolveSelectionTargets(DesignEditorItem item)
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
    /// <see cref="UpdateSelectionOverlayState"/> вызывается из двух десятков мест,
    /// в том числе на каждом кадре перетаскивания и на каждое изменение геометрии.
    /// Снимок при этом пересобирается всегда, но выделение меняется редко, поэтому
    /// без этой проверки и свойства, и событие срабатывали бы на изменение геометрии.
    /// </remarks>
    private void ApplySelectionSnapshot(IReadOnlyList<DesignSelectionTarget> next)
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
    private static bool AreSameTargets(
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

    private static IReadOnlyList<DesignSelectionTarget> Difference(
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

    private IReadOnlyList<DesignSelectionTarget> CreateSelectionTargetsSnapshot(DesignEditorItem? primaryItem, Control? primaryControl)
    {
        var result = new List<DesignSelectionTarget>();
        var dedup = new HashSet<Control>();

        if (primaryItem != null && primaryControl != null && dedup.Add(primaryControl))
            result.Add(new DesignSelectionTarget(primaryItem, primaryControl, _groupStore));

        var items = SelectedItems;
        if (items == null)
            return result;

        foreach (var item in items)
        {
            var container = ContainerFromItem(item) as DesignEditorItem;
            if (container == null && item is DesignEditorItem directItem)
                container = directItem;

            if (container == null)
                continue;

            foreach (var target in ResolveSelectionTargets(container))
            {
                if (!dedup.Add(target))
                    continue;

                result.Add(new DesignSelectionTarget(container, target, _groupStore));
            }
        }

        return result;
    }

    /// <summary>
    /// Возвращает ближайший design host target'а — контейнер, непосредственно
    /// внутри которого он лежит.
    /// </summary>
    /// <remarks>
    /// Для контрола внутри контейнера верхнего уровня это сам контейнер, для
    /// контрола внутри вложенного — вложенный, для вложенного контейнера — его владелец.
    /// Это единица группировки выделения: вместе выбираются только соседи по host'у.
    /// </remarks>
    private static DesignEditorItem? FindDesignHost(Control target)
        => target.FindAncestorOfType<DesignEditorItem>();

    private IEnumerable<Control> EnumerateSelectedTargets() => _selectedTargets;

    /// <summary>
    /// Проверяет, что target лежит в том же design host, что и всё текущее выделение.
    /// </summary>
    /// <remarks>
    /// Правило точнее, чем «один item верхнего уровня»: два контрола из соседних
    /// вложенных контейнеров принадлежат одному item'у, но разным host'ам,
    /// и группировать их вместе нельзя.
    /// </remarks>
    private bool SharesDesignHostWithSelection(Control target)
    {
        var host = FindDesignHost(target);

        foreach (var selected in EnumerateSelectedTargets())
        {
            if (ReferenceEquals(selected, target))
                continue;

            if (!ReferenceEquals(FindDesignHost(selected), host))
                return false;
        }

        return true;
    }

    internal bool CanAddNestedTargetToContainer(DesignEditorItem container)
    {
        var items = SelectedItems;
        if (items == null || items.Count == 0)
            return true;

        DesignEditorItem? owner = null;
        foreach (var item in items)
        {
            var selectedContainer = ContainerFromItem(item) as DesignEditorItem;
            if (selectedContainer == null && item is DesignEditorItem directItem)
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

    private static bool IsOwnedByContainer(Visual visual, DesignEditorItem container)
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

    private static IEnumerable<Control> EnumerateSelectionCandidates(DesignEditorItem item)
    {
        if (item.ContentMode == DesignContentMode.Loaded)
            return EnumerateAuthoredContent(item);

        return EnumerateVisualCandidates(item);
    }

    private static IEnumerable<Control> EnumerateVisualCandidates(DesignEditorItem item)
    {
        foreach (var descendant in item.GetVisualDescendants())
        {
            if (descendant is Control control &&
                !ReferenceEquals(control, item) &&
                IsOwnedByContainer(control, item))
            {
                yield return control;
            }
        }
    }

    /// <summary>
    /// Перечисляет элементы, написанные в разметке, не спускаясь во внутренности контролов.
    /// </summary>
    /// <remarks>
    /// Обход идёт по тем связям, которые автор задал сам: дети панели, ребёнок
    /// декоратора, контент — если это контрол. У кнопки с текстовым контентом
    /// спускаться некуда, поэтому она остаётся листом.
    /// <para>
    /// Проверки <c>TemplatedParent</c> здесь недостаточно: презентер порождает
    /// из строкового контента <c>AccessText</c>, у которого <c>TemplatedParent</c>
    /// пуст, и клик по кнопке выбирал бы её надпись.
    /// </para>
    /// </remarks>
    private static IEnumerable<Control> EnumerateAuthoredContent(DesignEditorItem item)
    {
        var root = (item.Presenter as Control)?.GetVisualChildren().OfType<Control>().FirstOrDefault()
                   ?? item.Content as Control;

        return root == null ? Array.Empty<Control>() : Descend(root);

        static IEnumerable<Control> Descend(Control control)
        {
            yield return control;

            foreach (var child in AuthoredChildren(control))
            {
                foreach (var nested in Descend(child))
                    yield return nested;
            }
        }
    }

    private static IEnumerable<Control> AuthoredChildren(Control control)
    {
        switch (control)
        {
            case Panel panel:
                foreach (var child in panel.Children)
                    yield return child;
                break;

            case Decorator { Child: { } decorated }:
                yield return decorated;
                break;

            case ContentControl { Content: Control content }:
                yield return content;
                break;

            case ContentPresenter { Content: Control presented }:
                yield return presented;
                break;
        }
    }

    /// <summary>
    /// Перечисляет контейнеры редактора на любой глубине вложенности.
    /// </summary>
    /// <remarks>
    /// Во время жеста, снявшего снимок через <see cref="BeginContainerSnapshot"/>,
    /// отдаёт этот снимок вместо нового обхода.
    /// </remarks>
    private IEnumerable<DesignEditorItem> EnumerateContainers() =>
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
    /// Снимок берётся один раз по той же причине, что и соседи в <see cref="BeginSnapGuides"/>,
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

    private IEnumerable<DesignEditorItem> EnumerateContainersCore()
    {
        if (Presenter?.Panel == null)
            yield break;

        foreach (var child in Presenter.Panel.Children)
        {
            if (child is not DesignEditorItem container)
                continue;

            yield return container;

            foreach (var descendant in container.GetVisualDescendants())
            {
                if (descendant is DesignEditorItem nested)
                    yield return nested;
            }
        }
    }

    /// <summary>
    /// Возвращает геометрию контейнера в мировых координатах.
    /// </summary>
    /// <remarks>
    /// Для вложенных контейнеров <see cref="DesignEditorItem.Location"/> задан
    /// относительно родительской панели, поэтому позиция берётся из design-геометрии,
    /// а на неё падает fallback только для контейнеров верхнего уровня.
    /// </remarks>
    private bool TryGetContainerWorldBounds(DesignEditorItem container, out Rect bounds)
    {
        if (TryGetDesignBounds((Control)container, out bounds))
            return true;

        if (container.Bounds.Width <= 0 || container.Bounds.Height <= 0)
        {
            bounds = default;
            return false;
        }

        bounds = new Rect(container.Location, container.Bounds.Size);
        return true;
    }

    private static int GetContainerDepth(DesignEditorItem container)
    {
        var depth = 0;
        var current = container.GetVisualParent();

        while (current != null)
        {
            if (current is DesignEditorItem)
                depth++;

            current = current.GetVisualParent();
        }

        return depth;
    }

    private DesignEditorItem? FindContainerAtWorldPoint(Point worldPoint)
    {
        DesignEditorItem? bestMatch = null;
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

    private DesignEditorItem? FindContainerForMarquee(Rect bounds)
    {
        // Владельцем рамки становится самый глубокий контейнер, который целиком её
        // содержит: рамка внутри вложенного контейнера работает в его пределах,
        // а рамка, вышедшая за его границы, поднимается к владельцу.
        DesignEditorItem? containing = null;
        var containingDepth = -1;

        DesignEditorItem? bestOverlap = null;
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
}
