using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Surface;

// Политики взаимодействия: пересечение участников, которых добавили слои выше.
// Часть SurfaceView; общее описание типа — в SurfaceView.cs.
public partial class SurfaceView
{
    private readonly List<ISurfaceInteractionPolicy> _interactionPolicies = new();

    /// <summary>
    /// Добавляет участника политики взаимодействия.
    /// </summary>
    private protected void AddInteractionPolicy(ISurfaceInteractionPolicy policy) => _interactionPolicies.Add(policy);

    /// <summary>
    /// Возвращает оси, по которым target действительно можно двигать.
    /// </summary>
    /// <remarks>
    /// Правило одно: <c>effective = a &amp; b &amp; …</c> по всем участникам. Каждый задаёт
    /// потолок, и ни один не расширяет другого. Все точки жестов спрашивают именно этот
    /// метод, а не участников по отдельности.
    /// </remarks>
    internal MovePolicy GetEffectiveMovePolicy(Control control)
    {
        var effective = MovePolicy.Both;

        foreach (var policy in _interactionPolicies)
        {
            effective &= policy.GetMovePolicy(control);
            if (effective == MovePolicy.None)
                break;
        }

        return effective;
    }

    /// <summary>
    /// Возвращает стороны, за которые target действительно можно тянуть.
    /// </summary>
    internal ResizePolicy GetResizePolicy(Control control)
    {
        var effective = ResizePolicy.All;

        foreach (var policy in _interactionPolicies)
        {
            effective &= policy.GetResizePolicy(control);
            if (effective == ResizePolicy.None)
                break;
        }

        return effective;
    }

    internal Vector ApplyMovePolicy(Control control, Vector delta)
        => ApplyMovePolicy(delta, GetEffectiveMovePolicy(control));

    internal bool IsResizeAllowed(Control control, ResizeDirection direction)
        => IsResizeAllowed(GetResizePolicy(control), direction);

    private protected static Vector ApplyMovePolicy(Vector delta, MovePolicy policy)
    {
        var x = policy.HasFlag(MovePolicy.X) ? delta.X : 0d;
        var y = policy.HasFlag(MovePolicy.Y) ? delta.Y : 0d;
        return new Vector(x, y);
    }

    private protected static bool IsResizeAllowed(ResizePolicy policy, ResizeDirection direction)
    {
        return direction switch
        {
            ResizeDirection.Left => policy.HasFlag(ResizePolicy.Left),
            ResizeDirection.Top => policy.HasFlag(ResizePolicy.Top),
            ResizeDirection.Right => policy.HasFlag(ResizePolicy.Right),
            ResizeDirection.Bottom => policy.HasFlag(ResizePolicy.Bottom),
            ResizeDirection.TopLeft => policy.HasFlag(ResizePolicy.Top) &&
                                       policy.HasFlag(ResizePolicy.Left),
            ResizeDirection.TopRight => policy.HasFlag(ResizePolicy.Top) &&
                                        policy.HasFlag(ResizePolicy.Right),
            ResizeDirection.BottomLeft => policy.HasFlag(ResizePolicy.Bottom) &&
                                          policy.HasFlag(ResizePolicy.Left),
            ResizeDirection.BottomRight => policy.HasFlag(ResizePolicy.Bottom) &&
                                           policy.HasFlag(ResizePolicy.Right),
            _ => false
        };
    }
}
