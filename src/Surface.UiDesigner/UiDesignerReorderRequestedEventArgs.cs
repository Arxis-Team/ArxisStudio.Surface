using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Surface.UiDesigner;

/// <summary>
/// Аргументы запроса на перестановку контрола среди соседей.
/// </summary>
/// <remarks>
/// Деревом контролов редактор не владеет: он распознаёт жест, показывает точку
/// вставки и сообщает намерение. Саму перестановку, её запись и отмену выполняет
/// библиотека разметки. Пока запрос не помечен <see cref="Handled"/>,
/// порядок остаётся прежним.
/// <para>
/// Это структурная правка, поэтому она не попадает в <see cref="SurfaceView.EditCompleted"/>:
/// там живёт геометрия и порядок перекрытия — то, чем редактор распоряжается сам.
/// </para>
/// </remarks>
public sealed class UiDesignerReorderRequestedEventArgs : EventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="UiDesignerReorderRequestedEventArgs"/>.
    /// </summary>
    /// <param name="target">Контрол, который требуется переставить.</param>
    /// <param name="oldIndex">Текущая позиция среди соседей.</param>
    /// <param name="newIndex">Запрошенная позиция среди соседей.</param>
    /// <param name="anchor">Сосед, перед которым встаёт контрол, либо <see langword="null"/>.</param>
    public UiDesignerReorderRequestedEventArgs(Control target, int oldIndex, int newIndex, Control? anchor)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        OldIndex = oldIndex;
        NewIndex = newIndex;
        Anchor = anchor;
    }

    /// <summary>
    /// Получает контрол, который требуется переставить.
    /// </summary>
    public Control Target { get; }

    /// <summary>
    /// Получает текущую позицию среди соседей.
    /// </summary>
    public int OldIndex { get; }

    /// <summary>
    /// Получает запрошенную позицию среди соседей.
    /// </summary>
    /// <remarks>
    /// Индекс задан <b>после</b> удаления контрола из коллекции — так его понимает
    /// <c>Children.Move</c>, и так же им пользуются оба обработчика в репозитории.
    /// Проще говоря, это итоговая позиция контрола среди соседей.
    /// </remarks>
    public int NewIndex { get; }

    /// <summary>
    /// Получает соседа, перед которым встаёт контрол, либо <see langword="null"/>,
    /// если он уходит в конец.
    /// </summary>
    /// <remarks>
    /// Ссылка, а не число, и в этом весь смысл: индекс осмыслен только против той
    /// же коллекции <c>Panel.Children</c>, которую редактор только что измерил.
    /// Библиотека разметки владеет исходным деревом, где узел может не иметь
    /// ровно одного соответствия среди визуальных детей, — по ссылке она найдёт
    /// точку вставки в своих терминах, по индексу не всегда.
    /// </remarks>
    public Control? Anchor { get; }

    /// <summary>
    /// Получает или задает признак того, что перестановка выполнена приложением.
    /// </summary>
    public bool Handled { get; set; }
}
