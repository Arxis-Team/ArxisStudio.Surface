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
using SurfaceLayout = ArxisStudio.Surface.UiDesigner.Layout;
using SurfaceInteraction = ArxisStudio.Surface.Editing.SurfaceInteraction;
using ArxisStudio.Surface.Editing;
using ArxisStudio.Surface.UiDesigner.Placement;
using ArxisStudio.Surface.States;

namespace ArxisStudio.Surface.UiDesigner;

// Двухуровневое выделение: запись, чтение и публикация.
// Часть UiDesignerView; общее описание типа — в UiDesignerView.cs.
public partial class UiDesignerView
{
    /// <summary>
    /// Держит подписки на свойства текущих selection targets в актуальном состоянии.
    /// </summary>
    /// <remarks>
    /// Подписка идёт на разрешённые targets из <see cref="SurfaceView.SelectedTargets"/>,
    /// а не на <c>_selectedTargets</c>: если у выбранного item'а нет явного target,
    /// его геометрию задаёт default target, и следить нужно за ним.
    /// </remarks>
    private void SyncSelectedTargetSubscriptions()
    {
        var targets = SelectedTargets;

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
            e.Property == SurfaceLayout.SurfaceXProperty ||
            e.Property == SurfaceLayout.SurfaceYProperty ||
            e.Property == SurfaceInteraction.ResizePolicyProperty ||
            e.Property == SurfaceInteraction.MovePolicyProperty)
        {
            UpdateSelectionOverlayState();
        }
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
        if (ShowsGroupFrame && SelectedTargets.Count > 1)
        {
            groupResizePolicy = ArxisStudio.Surface.ResizePolicy.All;
            groupMovePolicy = ArxisStudio.Surface.MovePolicy.Both;

            foreach (var selectedTarget in SelectedTargets)
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

    /// <summary>
    /// Пишет слой target'ов по точке нажатия.
    /// </summary>
    /// <remarks>
    /// Оверлей отсюда не пересобирается: это половина транзакции, и пересборка
    /// на её середине публиковала бы состояние, которого пользователь не просил.
    /// </remarks>
    private protected override void ApplyTargetFromPoint(SurfaceItem surfaceItem, Point screenPoint, KeyModifiers modifiers, int clickCount)
    {
        // У дизайнера интерфейса каждый контейнер — UiDesignerItem.
        var container = (UiDesignerItem)surfaceItem;

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
        if (isAdditive && (!CanAddNestedTargetToContainer(container) || !SharesTargetHostWithSelection(target)))
            return;

        var groupHost = FindTargetHost(target) ?? container;
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
    /// Определяет, может ли контрол быть target внутри указанного контейнера.
    /// </summary>
    /// <remarks>
    /// В режиме <see cref="SurfaceContentMode.Loaded"/> размечать содержимое некому,
    /// поэтому редактируется всё, что автор написал в разметке. Внутренности
    /// контролов при этом отсекает <c>TemplatedParent</c>: у частей шаблона он задан,
    /// у элементов из <c>.axaml</c> — нет. Иначе клик по кнопке выбирал бы её
    /// внутренний <c>TextBlock</c>.
    /// </remarks>
    internal static bool IsSelectableTarget(Control control, UiDesignerItem owner)
    {
        if (owner.ContentMode != SurfaceContentMode.Loaded)
            return HasLayoutMetadata(control);

        // Внутренности контролов отсекает уже сам обход авторской разметки,
        // здесь остаётся только не залезть в чужой контейнер.
        return ReferenceEquals(FindTargetHost(control), owner);
    }

    private static bool HasLayoutMetadata(Control control)
    {
        return SurfaceLayout.GetIsTracked(control)
            || !double.IsNaN(SurfaceLayout.GetX(control))
            || !double.IsNaN(SurfaceLayout.GetY(control));
    }

    internal static void EnsureTracked(Control control)
    {
        // Track идемпотентен. Прежний guard по GetIsTracked не работал:
        // Track не выставляет публичное IsTracked, поэтому для контролов
        // с одними Layout.X/Y условие было истинно всегда.
        SurfaceLayout.Track(control);
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
    private static UiDesignerItem? FindTargetHost(Control target)
        => target.FindAncestorOfType<UiDesignerItem>();

    internal static IEnumerable<Control> EnumerateSelectionCandidates(UiDesignerItem item)
    {
        if (item.ContentMode == SurfaceContentMode.Loaded)
            return EnumerateAuthoredContent(item);

        return EnumerateVisualCandidates(item);
    }

    private static IEnumerable<Control> EnumerateVisualCandidates(UiDesignerItem item)
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
    /// декоратора, контент и элементы списка — если это контролы. У кнопки с
    /// текстовым контентом спускаться некуда, поэтому она остаётся листом.
    /// <para>
    /// Проверки <c>TemplatedParent</c> здесь недостаточно: презентер порождает
    /// из строкового контента <c>AccessText</c>, у которого <c>TemplatedParent</c>
    /// пуст, и клик по кнопке выбирал бы её надпись.
    /// </para>
    /// </remarks>
    private static IEnumerable<Control> EnumerateAuthoredContent(UiDesignerItem item)
    {
        var root = item.AuthoredRoot;

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

            // Элементы, написанные в разметке, — вкладки TabControl, строки ListBox, пункты меню. Данные
            // из ItemsSource контролами не являются, и обход их не берёт. Вкладка — ContentControl, и её
            // страница идёт следом; невыбранная страница вне дерева, рамки у неё нет, и выбрать её нельзя.
            case ItemsControl items:
                foreach (var item in items.Items)
                {
                    if (item is Control element)
                        yield return element;
                }

                break;
        }
    }
}
