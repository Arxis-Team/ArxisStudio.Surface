using Avalonia;
using Avalonia.Controls;
using ArxisStudio.Surface.Editing;

namespace ArxisStudio.Surface.UiDesigner;

/// <summary>
/// Принадлежность контрола design-time группе.
/// </summary>
/// <remarks>
/// Группа — это <b>пометка на контролах</b>, а не узел дерева. Редактор дерево не правит
/// (см. ADR 0001), поэтому переложить участников в новый контейнер он не может и не должен:
/// структурная правка принадлежит тому, кто владеет разметкой. Зато пометка живёт ровно там
/// же, где живут <see cref="Layout"/> и <see cref="SurfaceInteraction"/>, — на самом контроле,
/// — и хост сохраняет её тем же способом, которым уже сохраняет <c>Layout.X</c>/<c>Y</c>.
/// <para>
/// Это <b>умолчание</b>, а не единственное место: у группы нет читателя в рантайме, поэтому
/// записанная в разметку она делает документ зависимым от сборки редактора. Хост, которому
/// эта цена не подходит, задаёт своё <see cref="IDesignGroupStore"/> через
/// <c>DesignEditor.GroupStore</c>, и тогда редактор сюда не пишет вовсе — см. ADR 0002.
/// </para>
/// <para>
/// Отсюда и тип: строка пишется в разметке и читается человеком.
/// </para>
/// <code language="xml"><![CDATA[
/// <Border attached:DesignGroup.Id="toolbar" />
/// ]]></code>
/// <para>
/// Значение сравнивается по <see cref="System.StringComparison.Ordinal"/>: это идентификатор, а не текст
/// на языке пользователя, и совпадение по регистру здесь означает совпадение группы.
/// </para>
/// </remarks>
public static class DesignGroup
{
    /// <summary>
    /// Идентификатор attached-свойства группы.
    /// </summary>
    /// <remarks>
    /// Наследование выключено: группа принадлежит конкретным контролам, а не поддереву.
    /// Наследуемое значение затянуло бы в группу каждого ребёнка участника, и раскрыть
    /// такую группу было бы нечем.
    /// </remarks>
    public static readonly AttachedProperty<string?> IdProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>(
            "Id",
            typeof(DesignGroup),
            null,
            inherits: false);

    /// <summary>
    /// Возвращает идентификатор группы контрола.
    /// </summary>
    /// <param name="target">Контрол.</param>
    /// <returns>Идентификатор группы или <see langword="null"/>, если контрол не сгруппирован.</returns>
    public static string? GetId(AvaloniaObject target) => target.GetValue(IdProperty);

    /// <summary>
    /// Задает идентификатор группы контрола.
    /// </summary>
    /// <param name="target">Контрол.</param>
    /// <param name="value">Идентификатор группы или <see langword="null"/>.</param>
    /// <remarks>
    /// Прямая запись группу меняет, но мимо контракта изменений: в
    /// <see cref="SurfaceView.EditCompleted"/> она не попадёт и отменить её будет нечем.
    /// Редактор увидит её, только если пользуется библиотечным хранилищем
    /// <see cref="DesignGroupAttachedStore"/>; со своим хранилищем это свойство ему не видно.
    /// Из редактора пользоваться надо <see cref="DesignEditor.GroupSelection"/> и
    /// <see cref="DesignEditor.UngroupSelection"/>; это свойство — для разметки и для хоста,
    /// который восстанавливает сохранённый макет.
    /// </remarks>
    public static void SetId(AvaloniaObject target, string? value) => target.SetValue(IdProperty, value);
}
