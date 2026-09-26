using Avalonia;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio;

internal interface IInteractionOperation
{
    void Update(DesignEditor editor, Vector worldDelta);

    void Complete(DesignEditor editor);
}
