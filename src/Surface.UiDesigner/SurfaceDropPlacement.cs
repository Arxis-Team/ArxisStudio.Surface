using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Surface.UiDesigner;

/// <summary>
/// Как брошенный контрол встанет в родителя — это решает раскладка родителя.
/// </summary>
public enum SurfaceDropKind
{
    /// <summary>
    /// Родитель ставит детей по координатам: <see cref="Canvas"/> или <see cref="AbsolutePanel"/>.
    /// Место — <see cref="SurfaceDropPlacement.Position"/>.
    /// </summary>
    Position,

    /// <summary>
    /// Родитель — панель, которая расставляет детей сама. Контрол встаёт перед
    /// <see cref="SurfaceDropPlacement.Anchor"/>, а без него — в конец. В потоке (<see cref="StackPanel"/>,
    /// <see cref="WrapPanel"/>) место выбирается правилом перестановки; в прочих панелях — конец.
    /// </summary>
    Insert,

    /// <summary>
    /// Родитель держит одного ребёнка, и сейчас его нет: пустые <see cref="Border"/>,
    /// <see cref="ContentControl"/>, страница <see cref="TabControl"/>, окно без содержимого.
    /// </summary>
    Content,

    /// <summary>
    /// Родитель — <see cref="Grid"/>. Место — ячейка <see cref="SurfaceDropPlacement.Row"/>,
    /// <see cref="SurfaceDropPlacement.Column"/>.
    /// </summary>
    Cell,
}

/// <summary>
/// Куда ляжет контрол, брошенный в точку холста: ответ <see cref="UiDesignerView.TryResolveDropPlacement"/>.
/// </summary>
/// <remarks>
/// <para>
/// Бросок решает дизайнер, а исполняет хост (ADR 0024). Редактор дерево не правит (ADR 0001): он
/// называет родителя, место в нём и прямоугольник, который это место показывает. Документ меняет
/// хост — тот, кто владеет разметкой.
/// </para>
/// <para>
/// Место в родителе называет <see cref="Anchor"/>, а не только <see cref="Index"/>. Индекс осмыслен
/// против той коллекции <c>Panel.Children</c>, которую измерил редактор, а у документа своё дерево. Сосед
/// переводится в элемент документа картой объектов хоста, как
/// <see cref="UiDesignerReorderRequestedEventArgs.Anchor"/>.
/// </para>
/// </remarks>
/// <param name="Container">Контейнер формы, в которую идёт бросок.</param>
/// <param name="Parent">
/// Контрол, который примет брошенный. Для окна без содержимого — сам корень документа
/// (<see cref="UiDesignerFormItem.Root"/>), для пустой страницы <see cref="TabControl"/> — её <see cref="TabItem"/>.
/// </param>
/// <param name="Kind">Как контрол встанет в родителя.</param>
/// <param name="Index">
/// Позиция в <c>Children</c> родителя, перед которой встанет контрол; число детей — в конец. У
/// <see cref="SurfaceDropKind.Content"/> — ноль.
/// </param>
/// <param name="Position">
/// Точка броска в координатах родителя — место для <see cref="SurfaceDropKind.Position"/>. Родителю вне
/// дерева холста — корню-окну — ноль.
/// </param>
/// <param name="Row">Строка ячейки у <see cref="SurfaceDropKind.Cell"/>; у прочих — ноль.</param>
/// <param name="Column">Столбец ячейки у <see cref="SurfaceDropKind.Cell"/>; у прочих — ноль.</param>
/// <param name="Anchor">
/// Сосед, перед которым встанет контрол, у <see cref="SurfaceDropKind.Insert"/>; <see langword="null"/> — в конец.
/// </param>
/// <param name="Indicator">
/// Что показать, в координатах поверхности. Линия нулевой толщины — место между соседями в потоке;
/// прямоугольник — область, которая примет контрол: панель, ячейка, пустой хост.
/// </param>
public sealed record SurfaceDropPlacement(
    UiDesignerItem Container,
    Control Parent,
    SurfaceDropKind Kind,
    int Index,
    Point Position,
    int Row,
    int Column,
    Control? Anchor,
    Rect Indicator)
{
    /// <summary>Получает признак того, что индикатор — линия между соседями, а не область.</summary>
    public bool IsLine => Indicator.Width == 0 || Indicator.Height == 0;
}
