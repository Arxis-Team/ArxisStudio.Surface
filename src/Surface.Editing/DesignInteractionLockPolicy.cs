using Avalonia.Controls;

namespace ArxisStudio.Surface.Editing;

/// <summary>
/// Блокировки, поставленные человеком: attached-свойства <see cref="DesignInteraction"/>.
/// </summary>
/// <remarks>
/// Это правила <b>взаимодействия</b>: их читают точки жеста, а не швы записи. Вызов API
/// — просьба самого хоста, который пометку и поставил, поэтому
/// <see cref="SurfaceView.SetDesignGeometry"/> их не спрашивает.
/// </remarks>
internal sealed class DesignInteractionLockPolicy : ISurfaceInteractionPolicy
{
    public static readonly DesignInteractionLockPolicy Instance = new();

    private DesignInteractionLockPolicy()
    {
    }

    public MovePolicy GetMovePolicy(Control target) => DesignInteraction.GetMovePolicy(target);

    public ResizePolicy GetResizePolicy(Control target) => DesignInteraction.GetResizePolicy(target);
}
