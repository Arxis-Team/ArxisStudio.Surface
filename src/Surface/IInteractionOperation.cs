using Avalonia;

namespace ArxisStudio.Surface;

internal interface IInteractionOperation
{
    void Update(SurfaceView editor, Vector worldDelta);

    void Complete(SurfaceView editor);
}
