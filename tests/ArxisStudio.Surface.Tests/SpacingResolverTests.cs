using Avalonia;
using Avalonia.Headless.XUnit;
using ArxisStudio.Surface.Editing;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Арифметика равных интервалов.
/// </summary>
/// <remarks>
/// Отличие от выравнивания в том, что сравниваются <b>зазоры</b>, а не координаты.
/// Отсюда два требования, которых у выравнивания нет: сосед должен лежать
/// с элементом в одном ряду — то есть перекрываться по другой оси, — и иметь
/// ненулевую площадь, потому что интервал бывает между элементами, а не до линии.
/// </remarks>
public class SpacingResolverTests
{
    /// <summary>Левый сосед ряда: занимает 0..100 по X.</summary>
    private static readonly Rect Left = new(0, 100, 100, 60);

    /// <summary>Правый сосед того же ряда: начинается на 300.</summary>
    private static readonly Rect Right = new(300, 100, 100, 60);

    private static readonly Rect[] Row = { Left, Right };

    [AvaloniaFact]
    public void The_Element_Is_Centred_Between_Its_Row_Neighbours()
    {
        // Свободного места 200, элемент шириной 40 — по 80 с каждой стороны,
        // то есть ровное положение это X = 180.
        var moving = new Rect(174, 110, 40, 40);

        var found = SurfaceSpacingResolver.TryResolveOffset(
            moving, Row, tolerance: 8, out var offset, out var spacedX, out var spacedY);

        Assert.True(found);
        Assert.True(spacedX);
        Assert.False(spacedY);
        Assert.Equal(6, offset.X);
        Assert.Equal(0, offset.Y);
    }

    [AvaloniaFact]
    public void Beyond_The_Tolerance_Nothing_Is_Found()
    {
        var moving = new Rect(160, 110, 40, 40);

        Assert.False(SurfaceSpacingResolver.TryResolveOffset(
            moving, Row, tolerance: 8, out _, out _, out _));
    }

    /// <summary>
    /// Сосед из другого ряда в расчёт не идёт.
    /// </summary>
    /// <remarks>
    /// «Слева» и «справа» осмысленны внутри ряда: коробка этажом выше стоит слева
    /// по координате, но интервала с ней не образует.
    /// </remarks>
    [AvaloniaFact]
    public void A_Neighbour_From_Another_Row_Is_Ignored()
    {
        var elsewhere = new Rect(0, 400, 100, 60);
        var moving = new Rect(174, 110, 40, 40);

        Assert.False(SurfaceSpacingResolver.TryResolveOffset(
            moving, new[] { elsewhere, Right }, tolerance: 8, out _, out _, out _));
    }

    /// <summary>
    /// Направляющая соседом по интервалу не является.
    /// </summary>
    /// <remarks>
    /// Она приходит прямоугольником нулевой толщины. Интервал до линии — это уже
    /// не «расстояние между элементами», и подсказка о нём вводила бы в заблуждение.
    /// </remarks>
    [AvaloniaFact]
    public void A_Zero_Area_Neighbour_Is_Not_A_Spacing_Partner()
    {
        var guide = new Rect(100, 0, 0, 800);
        var moving = new Rect(174, 110, 40, 40);

        Assert.False(SurfaceSpacingResolver.TryResolveOffset(
            moving, new[] { guide, Right }, tolerance: 8, out _, out _, out _));
    }

    [AvaloniaFact]
    public void One_Sided_Rows_Produce_Nothing()
    {
        var moving = new Rect(174, 110, 40, 40);

        Assert.False(SurfaceSpacingResolver.TryResolveOffset(
            moving, new[] { Left }, tolerance: 8, out _, out _, out _));
    }

    [AvaloniaFact]
    public void The_Nearest_Neighbour_On_Each_Side_Wins()
    {
        // Дальний сосед слева не должен перебить ближнего: интервал считается
        // до того, что рядом, иначе элемент уезжал бы к краю макета.
        var far = new Rect(-400, 100, 100, 60);
        var moving = new Rect(174, 110, 40, 40);

        var found = SurfaceSpacingResolver.TryResolveOffset(
            moving, new[] { far, Left, Right }, tolerance: 8, out var offset, out _, out _);

        Assert.True(found);
        Assert.Equal(6, offset.X);
    }

    [AvaloniaFact]
    public void The_Vertical_Axis_Works_The_Same_Way()
    {
        var above = new Rect(100, 0, 60, 100);
        var below = new Rect(100, 300, 60, 100);
        var moving = new Rect(110, 176, 40, 40);

        var found = SurfaceSpacingResolver.TryResolveOffset(
            moving, new[] { above, below }, tolerance: 8, out var offset, out var spacedX, out var spacedY);

        Assert.True(found);
        Assert.False(spacedX);
        Assert.True(spacedY);
        Assert.Equal(4, offset.Y);
    }

    // ---- Повтор шага ----------------------------------------------------------

    /// <summary>Ряд с уже заданным шагом 50: 0..100, затем 150..250.</summary>
    private static readonly Rect[] Run =
    {
        new(0, 100, 100, 60),
        new(150, 100, 100, 60)
    };

    /// <summary>
    /// Элемент в конце ряда подхватывает шаг, который там уже стоит.
    /// </summary>
    /// <remarks>
    /// Положение посередине здесь невозможно — справа никого нет, — и без повтора
    /// шага интервалы не помогли бы вовсе. Ровно этот случай и есть «продолжить ряд».
    /// </remarks>
    [AvaloniaFact]
    public void A_Run_Continues_Its_Existing_Step()
    {
        var moving = new Rect(294, 110, 40, 40);

        var found = SurfaceSpacingResolver.TryResolveOffset(
            moving, Run, tolerance: 8, out var offset, out var spacedX, out _);

        Assert.True(found);
        Assert.True(spacedX);
        Assert.Equal(6, offset.X);
    }

    /// <summary>
    /// Шаг подхватывается и с другой стороны.
    /// </summary>
    [AvaloniaFact]
    public void The_Step_Is_Read_From_Whichever_Side_Has_One()
    {
        var after = new[]
        {
            new Rect(300, 100, 100, 60),
            new Rect(450, 100, 100, 60)
        };

        var moving = new Rect(216, 110, 40, 40);

        var found = SurfaceSpacingResolver.TryResolveOffset(
            moving, after, tolerance: 8, out var offset, out _, out _);

        Assert.True(found);
        Assert.Equal(-6, offset.X);
    }

    /// <summary>
    /// Когда подходят оба способа, побеждает ближайший.
    /// </summary>
    /// <remarks>
    /// То же правило, что и у выравнивания: кандидатов несколько, выигрывает тот,
    /// до которого меньше идти. Иначе выбор зависел бы от порядка проверок, а не
    /// от того, куда человек ведёт элемент.
    /// </remarks>
    [AvaloniaFact]
    public void The_Nearest_Candidate_Wins()
    {
        // Ряд 0..100 и 150..250 задаёт шаг 50, значит повтор ставит элемент на 300.
        // Дальний сосед 400..500 даёт положение посередине — 305.
        var neighbours = new[]
        {
            new Rect(0, 100, 100, 60),
            new Rect(150, 100, 100, 60),
            new Rect(400, 100, 100, 60)
        };

        SurfaceSpacingResolver.TryResolveOffset(
            new Rect(302, 110, 40, 40), neighbours, tolerance: 8, out var nearRepeat, out _, out _);

        SurfaceSpacingResolver.TryResolveOffset(
            new Rect(304, 110, 40, 40), neighbours, tolerance: 8, out var nearCentre, out _, out _);

        Assert.Equal(300, 302 + nearRepeat.X);
        Assert.Equal(305, 304 + nearCentre.X);
    }

    [AvaloniaFact]
    public void A_Continued_Run_Shows_Both_Gaps()
    {
        var hints = SurfaceSpacingResolver.CollectHints(new Rect(300, 110, 40, 40), Run);

        Assert.Equal(2, hints.Count);
        Assert.All(hints, h => Assert.Equal(50, h.End - h.Start, 3));
        Assert.Equal(100, hints[0].Start);
        Assert.Equal(250, hints[1].Start);
    }

    /// <summary>
    /// Цепочка длиннее двух показывается целиком.
    /// </summary>
    [AvaloniaFact]
    public void A_Longer_Chain_Is_Shown_Whole()
    {
        var run = new[]
        {
            new Rect(0, 100, 100, 60),
            new Rect(150, 100, 100, 60),
            new Rect(300, 100, 100, 60)
        };

        var hints = SurfaceSpacingResolver.CollectHints(new Rect(450, 110, 40, 40), run);

        Assert.Equal(3, hints.Count);
        Assert.All(hints, h => Assert.Equal(50, h.End - h.Start, 3));
    }

    /// <summary>
    /// Равенство в стороне от элемента подсказкой не становится.
    /// </summary>
    /// <remarks>
    /// Цепочка ищется от зазоров, прилегающих к элементу: два ровных зазора где-то
    /// поодаль к его положению отношения не имеют.
    /// </remarks>
    [AvaloniaFact]
    public void Equality_Away_From_The_Element_Is_Not_A_Hint()
    {
        var run = new[]
        {
            new Rect(0, 100, 100, 60),
            new Rect(150, 100, 100, 60),
            new Rect(300, 100, 100, 60)
        };

        // Элемент встал в 90 от последнего — его зазор ни с чем не совпал.
        Assert.Empty(SurfaceSpacingResolver.CollectHints(new Rect(490, 110, 40, 40), run));
    }

    // ---- Изменение размера ----------------------------------------------------

    /// <summary>
    /// При resize неподвижный край задаёт зазор, с которым сравнивается второй.
    /// </summary>
    /// <remarks>
    /// Это и есть отличие от перетаскивания: там элемент целиком встаёт посередине
    /// и оба зазора считаются заново, здесь один из них уже задан.
    /// </remarks>
    [AvaloniaFact]
    public void The_Far_Edge_Repeats_The_Fixed_Gap()
    {
        // Левый зазор 50 (150 - 100), значит правый край обязан встать на 250.
        var proposed = new Rect(150, 110, 94, 40);

        var found = SurfaceSpacingResolver.TryResolveEdge(
            proposed, Row, tolerance: 8, xAxis: true, farEdge: true, out var resolved);

        Assert.True(found);
        Assert.Equal(250, resolved);
    }

    [AvaloniaFact]
    public void The_Near_Edge_Repeats_The_Fixed_Gap()
    {
        // Правый зазор 50 (300 - 250), значит левый край обязан встать на 150.
        var proposed = new Rect(156, 110, 94, 40);

        var found = SurfaceSpacingResolver.TryResolveEdge(
            proposed, Row, tolerance: 8, xAxis: true, farEdge: false, out var resolved);

        Assert.True(found);
        Assert.Equal(150, resolved);
    }

    /// <summary>
    /// Ради равенства элемент не схлопывается.
    /// </summary>
    /// <remarks>
    /// Стенд подобран так, что отказ даёт именно это правило, а не радиус захвата:
    /// край стоит на 206, ровное значение — 200, до него шесть при допуске восемь.
    /// Но 200 это и есть неподвижный левый край, то есть нулевая ширина.
    /// </remarks>
    [AvaloniaFact]
    public void Collapsing_The_Element_Is_Refused()
    {
        var proposed = new Rect(200, 110, 6, 40);

        Assert.False(SurfaceSpacingResolver.TryResolveEdge(
            proposed, Row, tolerance: 8, xAxis: true, farEdge: true, out _));
    }

    /// <summary>
    /// Потянутый край тоже подхватывает шаг, стоящий дальше по ряду.
    /// </summary>
    [AvaloniaFact]
    public void A_Resized_Edge_Continues_The_Run()
    {
        var after = new[]
        {
            new Rect(300, 100, 100, 60),
            new Rect(450, 100, 100, 60)
        };

        // Шаг между соседями 50, значит правый край обязан встать на 250.
        var proposed = new Rect(0, 110, 244, 40);

        var found = SurfaceSpacingResolver.TryResolveEdge(
            proposed, after, tolerance: 8, xAxis: true, farEdge: true, out var resolved);

        Assert.True(found);
        Assert.Equal(250, resolved);
    }

    [AvaloniaFact]
    public void An_Edge_Beyond_The_Tolerance_Is_Left_Alone()
    {
        var proposed = new Rect(150, 110, 80, 40);

        Assert.False(SurfaceSpacingResolver.TryResolveEdge(
            proposed, Row, tolerance: 8, xAxis: true, farEdge: true, out _));
    }

    // ---- Подсказки ------------------------------------------------------------

    [AvaloniaFact]
    public void Equal_Gaps_Produce_A_Pair_Of_Hints()
    {
        var bounds = new Rect(180, 110, 40, 40);

        var hints = SurfaceSpacingResolver.CollectHints(bounds, Row);

        Assert.Equal(2, hints.Count);
        Assert.All(hints, h => Assert.Equal(SurfaceSnapGuideOrientation.Vertical, h.Orientation));

        // Оба отрезка меряют по 80 и лежат на одной высоте — середине общего перекрытия.
        Assert.All(hints, h => Assert.Equal(80, h.End - h.Start, 3));
        Assert.Equal(hints[0].Position, hints[1].Position, 3);

        // Первый — от левого соседа до элемента, второй — от элемента до правого.
        Assert.Equal(100, hints[0].Start);
        Assert.Equal(180, hints[0].End);
        Assert.Equal(220, hints[1].Start);
        Assert.Equal(300, hints[1].End);
    }

    [AvaloniaFact]
    public void Unequal_Gaps_Produce_Nothing()
    {
        var bounds = new Rect(200, 110, 40, 40);

        Assert.Empty(SurfaceSpacingResolver.CollectHints(bounds, Row));
    }

    /// <summary>
    /// Подсказка ложится на середину общего перекрытия, а не на край элемента.
    /// </summary>
    [AvaloniaFact]
    public void The_Hint_Sits_In_The_Shared_Band()
    {
        var bounds = new Rect(180, 110, 40, 40);

        var hint = SurfaceSpacingResolver.CollectHints(bounds, Row)[0];

        // Общая полоса всех трёх — 110..150, её середина 130.
        Assert.Equal(130, hint.Position, 3);
    }
}
