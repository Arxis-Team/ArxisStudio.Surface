using Avalonia.Controls;

namespace ArxisStudio.Surface;

/// <summary>
/// Контейнер элемента на <see cref="SurfaceView"/>: обёртка, которая знает положение
/// и размер своего содержимого, но не его смысл.
/// </summary>
/// <remarks>
/// Члены переезжают сюда из контейнера дизайнера форм по шагам (ADR 0003);
/// пока класс — точка, от которой он наследуется.
/// </remarks>
public class SurfaceItem : ContentControl
{
}
