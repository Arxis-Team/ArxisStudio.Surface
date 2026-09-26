using Avalonia;
using Avalonia.Headless.XUnit;
using ArxisStudio.Surface.Editing;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Вид выравнивания, породившего линию.
/// </summary>
/// <remarks>
/// Нужен ровно для одного — покрасить выравнивание по центру иначе, чем по краю.
/// До этого <c>DesignSnapGuide</c> нёс только ориентацию и координаты, и слою
/// нечем было их различить.
/// <para>
/// Центральной линия считается, только когда центры совпали <b>с обеих сторон</b>.
/// Совпадение центра с чужим краем — всё ещё выравнивание по краю: симметрии,
/// ради которой центр и красят отдельно, в нём нет.
/// </para>
/// </remarks>
public class SnapGuideKindTests
{
    /// <summary>Сосед, у которого край на 100, центр на 150, дальний край на 200.</summary>
    private static readonly Rect Anchor = new(100, 400, 100, 100);

    [AvaloniaFact]
    public void Centre_To_Centre_Is_A_Centre_Line()
    {
        // Центр moving на 150 — там же, где центр соседа.
        var moving = new Rect(110, 100, 80, 40);

        var guide = Assert.Single(DesignSnapGuideResolver.CollectGuides(moving, new[] { Anchor }));

        Assert.Equal(DesignSnapGuideKind.Centre, guide.Kind);
        Assert.Equal(150, guide.Position);
    }

    [AvaloniaFact]
    public void Edge_To_Edge_Is_An_Edge_Line()
    {
        // Левый край moving на 100 — там же, где левый край соседа.
        var moving = new Rect(100, 100, 80, 40);

        var guides = DesignSnapGuideResolver.CollectGuides(moving, new[] { Anchor });

        Assert.All(guides, g => Assert.Equal(DesignSnapGuideKind.Edge, g.Kind));
        Assert.Contains(guides, g => g.Position == 100);
    }

    [AvaloniaFact]
    public void Centre_To_Edge_Is_Still_An_Edge_Line()
    {
        // Центр moving на 100 — это левый край соседа, а не его центр.
        var moving = new Rect(60, 100, 80, 40);

        var guide = Assert.Single(DesignSnapGuideResolver.CollectGuides(moving, new[] { Anchor }));

        Assert.Equal(100, guide.Position);
        Assert.Equal(DesignSnapGuideKind.Edge, guide.Kind);
    }

    /// <summary>
    /// Пользовательская направляющая тоже даёт центральную линию.
    /// </summary>
    /// <remarks>
    /// Она приходит прямоугольником нулевой толщины, у которого край и центр совпадают.
    /// Перебор выравниваний соседа раньше прерывался на первом совпавшем, и центр
    /// у такого прямоугольника не находился никогда — линия по центру элемента,
    /// вставшего на направляющую, красилась бы как краевая.
    /// </remarks>
    [AvaloniaFact]
    public void A_Zero_Thickness_Neighbour_Can_Produce_A_Centre_Line()
    {
        var userGuide = new Rect(150, 0, 0, 800);
        var moving = new Rect(110, 100, 80, 40);

        var guide = Assert.Single(DesignSnapGuideResolver.CollectGuides(moving, new[] { userGuide }));

        Assert.Equal(150, guide.Position);
        Assert.Equal(DesignSnapGuideKind.Centre, guide.Kind);
    }

    [AvaloniaFact]
    public void Kind_Participates_In_Equality()
    {
        var edge = new DesignSnapGuide(DesignSnapGuideOrientation.Vertical, 10, 0, 100);
        var centre = new DesignSnapGuide(DesignSnapGuideOrientation.Vertical, 10, 0, 100, DesignSnapGuideKind.Centre);

        // Иначе набор линий, сменивший только вид, не переопубликовался бы:
        // сравнение снимка идёт по значению.
        Assert.NotEqual(edge, centre);
    }
}
