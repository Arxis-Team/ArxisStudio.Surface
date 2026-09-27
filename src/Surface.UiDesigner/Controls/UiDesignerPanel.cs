namespace ArxisStudio.Surface.UiDesigner;

/// <summary>
/// Специализированная панель, используемая как корневой холст в <see cref="UiDesignerView"/>.
/// <para>
/// Служит маркером для системы <see cref="Layout"/>.
/// Глобальные координаты (SurfaceX/SurfaceY) рассчитываются относительно ближайшего родителя этого типа,
/// игнорируя вложенные пользовательские <see cref="AbsolutePanel"/>.
/// </para>
/// </summary>
internal class UiDesignerPanel : AbsolutePanel
{
    // Логика полностью наследуется от AbsolutePanel.
    // Класс нужен только для идентификации корня редактора.
}
