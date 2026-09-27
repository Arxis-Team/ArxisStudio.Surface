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

namespace ArxisStudio.Surface.UiDesigner;

// Порядок перекрытия: ZIndex и порядок среди детей панели.
// Часть UiDesignerView; общее описание типа — в UiDesignerView.cs.
public partial class UiDesignerView
{
    /// <summary>
    /// Перемещает выделение на передний план.
    /// </summary>
    /// <returns><see langword="true"/>, если порядок был изменён.</returns>
    public bool BringToFront() => TryReorder(SurfaceOrderPlacement.Front);

    /// <summary>
    /// Перемещает выделение на задний план.
    /// </summary>
    /// <returns><see langword="true"/>, если порядок был изменён.</returns>
    public bool SendToBack() => TryReorder(SurfaceOrderPlacement.Back);

    /// <summary>
    /// Поднимает выделение на одну позицию.
    /// </summary>
    /// <returns><see langword="true"/>, если порядок был изменён.</returns>
    public bool BringForward() => TryReorder(SurfaceOrderPlacement.Forward);

    /// <summary>
    /// Опускает выделение на одну позицию.
    /// </summary>
    /// <returns><see langword="true"/>, если порядок был изменён.</returns>
    public bool SendBackward() => TryReorder(SurfaceOrderPlacement.Backward);

    private enum SurfaceOrderPlacement
    {
        Front,
        Back,
        Forward,
        Backward
    }

    /// <summary>
    /// Меняет порядок перекрытия выбранных targets.
    /// </summary>
    /// <remarks>
    /// Порядок осмыслен только среди соседей по родительской панели, поэтому targets
    /// группируются по родителю и переставляются независимо в каждой группе.
    /// <para>
    /// Внутри группы <c>ZIndex</c> нормализуется в последовательность 0..n-1. Иначе
    /// перестановка на одну позицию не работала бы: по умолчанию у всех соседей
    /// <c>ZIndex</c> равен нулю, и менять местами было бы нечего. Первая операция
    /// поэтому затрагивает всю группу, последующие — только сдвинутые элементы,
    /// потому что фильтр no-op отбрасывает совпавшие.
    /// </para>
    /// </remarks>
    private bool TryReorder(SurfaceOrderPlacement placement)
    {
        var targets = SelectedTargets;
        if (targets.Count == 0)
            return false;

        var groups = new Dictionary<Visual, List<Control>>();
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i].Target;
            if (target.GetVisualParent() is not { } parent)
                continue;

            if (!groups.TryGetValue(parent, out var members))
                groups[parent] = members = new List<Control>();

            members.Add(target);
        }

        if (groups.Count == 0)
            return false;

        BeginEdit(SurfaceEditKind.Order);

        foreach (var pair in groups)
            ReorderWithinParent(pair.Key, pair.Value, placement);

        CommitEdit();
        return true;
    }

    private void ReorderWithinParent(Visual parent, List<Control> moving, SurfaceOrderPlacement placement)
    {
        var siblings = new List<Control>();
        foreach (var child in parent.GetVisualChildren())
        {
            if (child is Control control)
                siblings.Add(control);
        }

        if (siblings.Count <= 1)
            return;

        // Текущий видимый порядок: сначала ZIndex, при равенстве — порядок в панели.
        var order = new List<Control>(siblings);
        order.Sort((a, b) =>
        {
            var byZ = a.ZIndex.CompareTo(b.ZIndex);
            return byZ != 0 ? byZ : siblings.IndexOf(a).CompareTo(siblings.IndexOf(b));
        });

        var isMoving = new HashSet<Control>(moving);
        var arranged = placement switch
        {
            SurfaceOrderPlacement.Front => Partition(order, isMoving, movedLast: true),
            SurfaceOrderPlacement.Back => Partition(order, isMoving, movedLast: false),
            SurfaceOrderPlacement.Forward => Shift(order, isMoving, forward: true),
            _ => Shift(order, isMoving, forward: false)
        };

        for (var i = 0; i < arranged.Count; i++)
            SetTargetZIndex(arranged[i], i);
    }

    private static List<Control> Partition(List<Control> order, HashSet<Control> moving, bool movedLast)
    {
        var stationary = new List<Control>();
        var moved = new List<Control>();

        foreach (var control in order)
            (moving.Contains(control) ? moved : stationary).Add(control);

        var result = new List<Control>(order.Count);
        if (movedLast)
        {
            result.AddRange(stationary);
            result.AddRange(moved);
        }
        else
        {
            result.AddRange(moved);
            result.AddRange(stationary);
        }

        return result;
    }

    private static List<Control> Shift(List<Control> order, HashSet<Control> moving, bool forward)
    {
        var result = new List<Control>(order);

        if (forward)
        {
            // С конца, иначе сдвинутый элемент обгонял бы соседа дважды.
            for (var i = result.Count - 2; i >= 0; i--)
            {
                if (moving.Contains(result[i]) && !moving.Contains(result[i + 1]))
                    (result[i], result[i + 1]) = (result[i + 1], result[i]);
            }
        }
        else
        {
            for (var i = 1; i < result.Count; i++)
            {
                if (moving.Contains(result[i]) && !moving.Contains(result[i - 1]))
                    (result[i], result[i - 1]) = (result[i - 1], result[i]);
            }
        }

        return result;
    }
}
