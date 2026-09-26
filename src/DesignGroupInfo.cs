using System;
using System.Collections.Generic;
using Avalonia.Controls;
using ArxisStudio.Grouping;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio;

/// <summary>
/// Представляет design-time группу и её состав внутри одной формы.
/// </summary>
/// <remarks>
/// Снимок, снятый в момент запроса: группа — это пометка на контролах
/// (<see cref="DesignGroup"/>), а деревом владеет хост, поэтому редактор
/// не может ни закэшировать состав, ни узнать о правке, которой не делал.
/// Отсюда и способ получения — запрос <see cref="DesignEditor.GetGroups"/>, а не
/// свойство со снимком, как у выделения.
/// <para>
/// Личность группы — это её <see cref="Path"/> целиком, а не <see cref="Id"/>: одинаковые
/// имена уровней на разных ветках дерева не сталкиваются. Пути уникальны в пределах
/// контейнера: <c>group-1</c> в двух формах — это две разные группы.
/// </para>
/// </remarks>
public sealed class DesignGroupInfo
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignGroupInfo"/>.
    /// </summary>
    /// <param name="container">Форма, которой принадлежит группа.</param>
    /// <param name="path">Путь группы от внешнего уровня к внутреннему.</param>
    /// <param name="members">Контролы, лежащие в группе непосредственно.</param>
    /// <param name="groups">Вложенные группы.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если любой из аргументов равен <see langword="null"/>.</exception>
    public DesignGroupInfo(
        DesignEditorItem container,
        string path,
        IReadOnlyList<Control> members,
        IReadOnlyList<DesignGroupInfo> groups)
    {
        Container = container ?? throw new ArgumentNullException(nameof(container));
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Members = members ?? throw new ArgumentNullException(nameof(members));
        Groups = groups ?? throw new ArgumentNullException(nameof(groups));
        Id = DesignGroupPath.Leaf(path) ?? path;
    }

    /// <summary>
    /// Получает форму, которой принадлежит группа.
    /// </summary>
    public DesignEditorItem Container { get; }

    /// <summary>
    /// Получает полный путь группы — её личность внутри формы.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Получает идентификатор уровня — последний сегмент <see cref="Path"/>.
    /// </summary>
    /// <remarks>
    /// Это то, что пользователь видит и правит в панели групп. Сравнивать группы по нему
    /// нельзя: у вложенных групп на разных ветках он может совпадать.
    /// </remarks>
    public string Id { get; }

    /// <summary>
    /// Получает контролы, лежащие в группе непосредственно, в порядке обхода разметки.
    /// </summary>
    /// <remarks>
    /// Контролы вложенных групп сюда не входят — они лежат в <see cref="Groups"/>.
    /// Весь состав целиком отдаёт <see cref="DesignEditor.GetGroupMembers"/>.
    /// <para>
    /// Порядок — тот же, которым редактор перечисляет кандидатов на выбор, то есть
    /// порядок разметки, а не порядок, в котором участников выделили. Панель групп
    /// у хоста не должна переставляться от вызова к вызову.
    /// </para>
    /// </remarks>
    public IReadOnlyList<Control> Members { get; }

    /// <summary>
    /// Получает вложенные группы.
    /// </summary>
    public IReadOnlyList<DesignGroupInfo> Groups { get; }
}
