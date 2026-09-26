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

namespace ArxisStudio.Surface.UiDesigner;

// Пересборка состояния оверлея выделения.
// Часть DesignEditor; общее описание типа — в DesignEditor.cs.
public partial class DesignEditor
{
    private bool TryGetSelectedDesignBounds(
        out Rect bounds,
        out int selectedCount,
        out DesignEditorItem? primaryItem,
        out Control? primaryControl,
        out IReadOnlyList<SelectionAdornerInfo> secondaryAdorners,
        out bool hasMultipleNestedSelection,
        out bool hasMultipleContainerSelection,
        out bool hasGroupSelection)
    {
        bounds = default;
        selectedCount = 0;
        primaryItem = null;
        primaryControl = null;
        secondaryAdorners = Array.Empty<SelectionAdornerInfo>();
        hasMultipleNestedSelection = false;
        hasMultipleContainerSelection = false;
        hasGroupSelection = false;

        var items = SelectedItems;
        if (items == null || items.Count == 0)
            return false;

        var perTargetBounds = new List<SelectionAdornerInfo>();
        var hasBounds = false;
        var containerTargetCount = 0;
        var nestedTargetCount = 0;
        double left = 0;
        double top = 0;
        double right = 0;
        double bottom = 0;

        foreach (var item in items)
        {
            var container = ContainerFromItem(item) as DesignEditorItem;
            if (container == null && item is DesignEditorItem directItem)
                container = directItem;

            if (container == null)
                continue;

            foreach (var selectionTarget in ResolveSelectionTargets(container))
            {
                if (!TryGetDesignBounds(selectionTarget, out var itemBounds))
                    continue;

                selectedCount++;
                primaryItem ??= container;
                primaryControl ??= selectionTarget;

                // Контейнером считается любой DesignEditorItem, а не только владелец:
                // вложенные контейнеры должны попадать в группу контейнеров,
                // иначе их множественный выбор рисуется как nested-группа.
                if (selectionTarget is DesignEditorItem)
                    containerTargetCount++;
                else
                    nestedTargetCount++;

                perTargetBounds.Add(new SelectionAdornerInfo
                {
                    Container = container,
                    Target = selectionTarget,
                    Bounds = itemBounds,
                    Role = SelectionAdornerRole.Secondary,
                    ResizePolicy = GetResizePolicy(selectionTarget),
                    MovePolicy = GetEffectiveMovePolicy(selectionTarget)
                });

                if (!hasBounds)
                {
                    left = itemBounds.Left;
                    top = itemBounds.Top;
                    right = itemBounds.Right;
                    bottom = itemBounds.Bottom;
                    hasBounds = true;
                    continue;
                }

                left = Math.Min(left, itemBounds.Left);
                top = Math.Min(top, itemBounds.Top);
                right = Math.Max(right, itemBounds.Right);
                bottom = Math.Max(bottom, itemBounds.Bottom);
            }
        }

        if (!hasBounds)
            return false;

        bounds = new Rect(left, top, right - left, bottom - top);
        hasMultipleContainerSelection = selectedCount > 1 && containerTargetCount == selectedCount;
        hasMultipleNestedSelection = selectedCount > 1 && nestedTargetCount == selectedCount;

        if (hasMultipleNestedSelection)
        {
            // Рамка рисуется на кластер, а не на target: группа целиком — одна рамка,
            // одиночный контрол — своя. Контуры участников группы по отдельности сказали
            // бы, что элементов несколько, а жест применяется к ним как к одному.
            var clusters = BuildClusterAdorners(perTargetBounds);

            // Единственный кластер-группа — это прежний случай «выделение равно группе»:
            // его рисует PART_GroupSelectionAdorner, и вторичные адорнеры не нужны.
            hasGroupSelection = clusters.Count == 1 && clusters[0].Role == SelectionAdornerRole.Group;

            if (!hasGroupSelection)
            {
                foreach (var adorner in clusters)
                {
                    adorner.ShowHandles = true;
                    adorner.IsInteractive = true;
                }

                secondaryAdorners = clusters;
            }
        }

        return true;
    }

    /// <summary>
    /// Сворачивает адорнеры участников в адорнеры кластеров.
    /// </summary>
    /// <remarks>
    /// Кластеры считает <see cref="BuildClusters"/> — та же точка, которой пользуются
    /// группировка и роспуск. Своя копия правила «выбраны все участники» здесь однажды
    /// уже была, и это ровно тот случай, когда показанное и применённое расходятся.
    /// <para>
    /// Считается в пределах владеющей формы: выделение может лежать в нескольких формах
    /// сразу, а путь группы осмыслен только внутри своей.
    /// </para>
    /// </remarks>
    private List<SelectionAdornerInfo> BuildClusterAdorners(IReadOnlyList<SelectionAdornerInfo> perTarget)
    {
        var infoByTarget = new Dictionary<Control, SelectionAdornerInfo>();
        var byHost = new Dictionary<DesignEditorItem, List<Control>>();
        var hostOrder = new List<DesignEditorItem>();

        foreach (var info in perTarget)
        {
            if (info.Target is not { } target || FindDesignHost(target) is not { } host)
                continue;

            infoByTarget[target] = info;

            if (!byHost.TryGetValue(host, out var controls))
            {
                controls = new List<Control>();
                byHost[host] = controls;
                hostOrder.Add(host);
            }

            controls.Add(target);
        }

        var clusterOf = new Dictionary<Control, SelectionCluster>();
        foreach (var host in hostOrder)
        {
            foreach (var cluster in BuildClusters(host, byHost[host]))
            {
                foreach (var member in cluster.Members)
                    clusterOf[member] = cluster;
            }
        }

        var emitted = new HashSet<SelectionCluster>();
        var clusters = new List<SelectionAdornerInfo>(perTarget.Count);

        foreach (var info in perTarget)
        {
            if (info.Target is { } target
                && clusterOf.TryGetValue(target, out var cluster)
                && cluster.IsGroup)
            {
                if (emitted.Add(cluster))
                    clusters.Add(CreateClusterAdorner(cluster, infoByTarget));

                continue;
            }

            clusters.Add(info);
        }

        return clusters;
    }

    /// <summary>
    /// Собирает адорнер кластера-группы: одна рамка на весь состав.
    /// </summary>
    /// <remarks>
    /// Политики складываются пересечением: заблокированный участник запирает весь кластер —
    /// то же правило, по которому смешанная группа locked/unlocked не двигается вовсе.
    /// </remarks>
    private static SelectionAdornerInfo CreateClusterAdorner(
        SelectionCluster cluster,
        Dictionary<Control, SelectionAdornerInfo> infoByTarget)
    {
        var first = infoByTarget[cluster.Members[0]];
        var bounds = first.Bounds;
        var resize = first.ResizePolicy;
        var move = first.MovePolicy;

        for (var i = 1; i < cluster.Members.Count; i++)
        {
            var info = infoByTarget[cluster.Members[i]];
            bounds = bounds.Union(info.Bounds);
            resize &= info.ResizePolicy;
            move &= info.MovePolicy;
        }

        return new SelectionAdornerInfo
        {
            Container = cluster.Host,
            Target = cluster.Members[0],
            Members = cluster.Members,
            Bounds = bounds,
            Role = SelectionAdornerRole.Group,
            ResizePolicy = resize,
            MovePolicy = move
        };
    }

    private void UpdateSelectionOverlayState()
    {
        SyncEnteredGroup();

        if (TryGetSelectedDesignBounds(
                out var bounds,
                out var selectedCount,
                out var primaryItem,
                out var primaryControl,
                out var secondaryAdorners,
                out var hasMultipleNestedSelection,
                out var hasMultipleContainerSelection,
                out var hasGroupSelection))
        {
            CleanupSelectionTargets();
            SelectionBounds = bounds;
            SecondarySelectionAdorners = secondaryAdorners;
            HasSingleSelection = selectedCount == 1;
            HasMultipleSelection = selectedCount > 1;
            HasMultipleNestedSelection = hasMultipleNestedSelection && !hasGroupSelection;
            HasMultipleContainerSelection = hasMultipleContainerSelection;
            HasGroupSelection = hasGroupSelection;
            ShowsGroupFrame = hasMultipleContainerSelection || hasGroupSelection;
            _primarySelectionItem = primaryItem;
            _primarySelectionControl = primaryControl;
            UpdatePrimaryPlacementReadout(primaryControl);
            ApplySelectionSnapshot(CreateSelectionTargetsSnapshot(primaryItem, primaryControl));
            SyncSelectedTargetSubscriptions();
            UpdateSelectionAdornerPolicies();
            return;
        }

        _selectedTargets.Clear();
        SelectionBounds = default;
        SecondarySelectionAdorners = Array.Empty<SelectionAdornerInfo>();
        HasSingleSelection = false;
        HasMultipleSelection = false;
        HasMultipleNestedSelection = false;
        HasMultipleContainerSelection = false;
        HasGroupSelection = false;
        ShowsGroupFrame = false;
        _primarySelectionItem = null;
        _primarySelectionControl = null;
        UpdatePrimaryPlacementReadout(null);
        ApplySelectionSnapshot(Array.Empty<DesignSelectionTarget>());
        SyncSelectedTargetSubscriptions();
        UpdateSelectionAdornerPolicies();
    }

    /// <summary>
    /// Обновляет диагностический вывод о размещении primary target.
    /// </summary>
    private void UpdatePrimaryPlacementReadout(Control? primaryControl)
    {
        if (primaryControl == null)
        {
            PrimarySelectionPlacement = null;
            PrimarySelectionMovePolicy = ArxisStudio.Surface.MovePolicy.None;
            PrimarySelectionResizePolicy = ArxisStudio.Surface.ResizePolicy.None;
            return;
        }

        PrimarySelectionPlacement = GetPlacementStrategy(primaryControl).Name;
        PrimarySelectionMovePolicy = GetEffectiveMovePolicy(primaryControl);
        PrimarySelectionResizePolicy = GetResizePolicy(primaryControl);
    }
}
