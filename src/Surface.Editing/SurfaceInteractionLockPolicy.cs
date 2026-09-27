using Avalonia.Controls;

namespace ArxisStudio.Surface.Editing;

/// <summary>
/// Блокировки, поставленные человеком: attached-свойства <see cref="SurfaceInteraction"/>.
/// </summary>
/// <remarks>
/// Это правила <b>взаимодействия</b>: их читают точки жеста, а не швы записи. Вызов API
/// — просьба самого хоста, который пометку и поставил, поэтому
/// <see cref="SurfaceView.SetTargetGeometry"/> их не спрашивает.
/// </remarks>
internal sealed class SurfaceInteractionLockPolicy : ISurfaceInteractionPolicy
{
    public static readonly SurfaceInteractionLockPolicy Instance = new();

    private SurfaceInteractionLockPolicy()
    {
    }

    public MovePolicy GetMovePolicy(Control target) => SurfaceInteraction.GetMovePolicy(target);

    public ResizePolicy GetResizePolicy(Control target) => SurfaceInteraction.GetResizePolicy(target);
}
