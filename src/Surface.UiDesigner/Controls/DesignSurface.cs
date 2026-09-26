namespace ArxisStudio.Surface.UiDesigner;

/// <summary>
/// Специализированная панель, используемая как корневой холст в <see cref="DesignEditor"/>.
/// <para>
/// Служит маркером для системы <see cref="Layout"/>.
/// Глобальные координаты (DesignX/DesignY) рассчитываются относительно ближайшего родителя этого типа,
/// игнорируя вложенные пользовательские <see cref="AbsolutePanel"/>.
/// </para>
/// </summary>
internal class DesignSurface : AbsolutePanel
{
    // Логика полностью наследуется от AbsolutePanel.
    // Класс нужен только для идентификации корня редактора.
}
