namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Роль пина и провода — какой базовый вид им дать (ADR 0017).
/// </summary>
/// <remarks>
/// Роль — только вид, как в Blueprint: данные — круг и цвет темы, выполнение — пятиугольник и толстый
/// светлый провод, делегат — квадрат у выхода, круг у входа и красный провод. Правил у роли нет: что
/// с чем соединяется, решает хост в <see cref="NodeEditor.ConnectValidating"/>. Цвета и толщины — ключи
/// темы <c>NodeEditor.Pin.*</c> и <c>NodeEditor.Link.*</c>; явные <see cref="Port.PinShape"/>,
/// <see cref="Port.PinBrush"/>, <see cref="NodeEditor.LinkStrokeBinding"/> и
/// <see cref="NodeEditor.LinkThicknessBinding"/> сильнее роли.
/// </remarks>
public enum PinRole
{
    /// <summary>Значение: координаты, число, текст. Вид — тема порта и провода, без роли.</summary>
    Data,

    /// <summary>Выполнение: порядок, в котором идут узлы, как Exec-пины Blueprint.</summary>
    Execution,

    /// <summary>
    /// Делегат: ссылка на событие или функцию, переданная как значение, чтобы вызвать её позже, — как
    /// красные пины событий и диспетчеров Blueprint.
    /// </summary>
    Delegate
}
