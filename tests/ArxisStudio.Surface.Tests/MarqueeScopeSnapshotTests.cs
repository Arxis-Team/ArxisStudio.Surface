using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Xunit;
using DesignLayout = ArxisStudio.Surface.UiDesigner.Layout;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Снимок контейнеров на время жеста рамки.
/// </summary>
/// <remarks>
/// Область действия рамки пересчитывается на каждом кадре протяжки, и до снимка
/// каждый кадр заново обходил поддерево каждого контейнера верхнего уровня. Цена
/// такого обхода растёт не по числу форм, а по размеру всего макета — стенд и числа
/// лежат в <see cref="MarqueeCostProbeTests"/>.
/// <para>
/// Но фиксируется здесь не цена, а вытекающая из снимка семантика: набор
/// контейнеров берётся один раз на входе в жест. То же решение и по той же
/// причине уже принято для соседей при выравнивании — рамка не должна охватывать
/// одно, а применяться к другому.
/// </para>
/// </remarks>
public class MarqueeScopeSnapshotTests
{
    private static readonly Point ContainerLocation = new(100, 100);
    private static readonly Size ContainerSize = new(200, 150);

    /// <summary>Пустая область контейнера — та, с которой начинается рамка.</summary>
    private static Point EmptyArea => new(
        ContainerLocation.X + ContainerSize.Width - 20,
        ContainerLocation.Y + ContainerSize.Height - 20);

    /// <summary>
    /// Точка внутри добавленного контейнера и вне всех размеченных контролов формы.
    /// </summary>
    /// <remarks>
    /// Пустое место нужно именно здесь: там, где лежит размеченный контрол, до цели
    /// дотягивается обход внутри контейнера, и устаревший снимок остаётся незаметен.
    /// </remarks>
    private static Point InsideAddedOnly => new(
        ContainerLocation.X + 80,
        ContainerLocation.Y + 120);

    /// <summary>
    /// Добавляет вложенный контейнер, целиком накрывающий область рамки.
    /// </summary>
    /// <remarks>
    /// Свежий обход дерева нашёл бы его и отдал бы рамке как более глубокого
    /// владельца; снимок, снятый до добавления, — нет. На этой разнице тест и стоит.
    /// </remarks>
    private static DesignEditorItem AddNestedContainer(EditorHarness harness)
    {
        var panel = (Panel)harness.Nested(0).GetVisualParent()!;

        // Содержимое обязательно: контейнер без единого target'а разрешению контекста
        // отвечать нечем, и тест падал бы не на снимке, а на пустой форме.
        var child = new Border { Name = "AddedChild", Width = 40, Height = 30 };
        DesignLayout.SetX(child, 5);
        DesignLayout.SetY(child, 5);

        var content = new AbsolutePanel();
        content.Children.Add(child);

        // Вложенный контейнер позиционируется как обычный ребёнок AbsolutePanel —
        // через Layout.X/Y и выравнивание по левому верхнему углу. Location здесь
        // не работает: это свойство контейнера верхнего уровня.
        var nested = new DesignEditorItem
        {
            Name = "Added",
            Width = ContainerSize.Width,
            Height = ContainerSize.Height,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Content = content,
        };

        // Layout.X/Y контейнеру намеренно не задаются. С ними он становится ещё и
        // target'ом внешней формы, и устаревший снимок находил бы его в обход себя:
        // обход контейнеров снят, а обход целей внутри контейнера — нет.
        // AbsolutePanel и без них ставит его в левый верхний угол.
        panel.Children.Add(nested);
        harness.RunLayout();
        return nested;
    }

    private static EditorHarness CreatePlaced()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);
        return harness;
    }

    [AvaloniaFact]
    public void A_Container_Added_Mid_Gesture_Does_Not_Take_Over_The_Marquee()
    {
        var harness = CreatePlaced();
        var from = EmptyArea;
        var to = new Point(ContainerLocation.X + 5, ContainerLocation.Y + 5);

        harness.Window.MouseDown(from, MouseButton.Left);
        harness.Window.MouseMove(from + ((to - from) * 0.5));

        var scopeBeforeChange = harness.Editor.MarqueeScope;
        var added = AddNestedContainer(harness);

        harness.Window.MouseMove(to);

        // Владелец рамки остался тем, кого жест увидел на входе. Без снимка сюда
        // приехал бы только что добавленный контейнер: он глубже и содержит рамку
        // целиком, то есть выигрывает у владельца по обоим правилам отбора.
        Assert.Same(scopeBeforeChange, harness.Editor.MarqueeScope);
        Assert.NotSame(added, harness.Editor.MarqueeScope);

        harness.Window.MouseUp(to, MouseButton.Left);
    }

    /// <summary>
    /// Снимок обязан сниматься на выходе из жеста, а не просто пересобираться на входе
    /// в следующий.
    /// </summary>
    /// <remarks>
    /// Разница видна только чтению <b>между</b> жестами, и первая версия этого теста её
    /// не ловила: она проверяла второй рамкой, а вход в рамку снимает снимок заново,
    /// поэтому утёкший список молча подменялся правильным. Проверять надо путь, который
    /// в состояние рамки не входит вовсе — разрешение контекста ходит по тем же
    /// контейнерам и работает на голом редакторе.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_Snapshot_Does_Not_Outlive_The_Gesture()
    {
        var harness = CreatePlaced();
        var from = EmptyArea;
        var to = new Point(ContainerLocation.X + 5, ContainerLocation.Y + 5);

        harness.Window.MouseDown(from, MouseButton.Left);
        harness.Window.MouseMove(from + ((to - from) * 0.5));
        harness.Window.MouseMove(to);
        harness.Window.MouseUp(to, MouseButton.Left);
        harness.RunLayout();

        AddNestedContainer(harness);

        SurfaceContextRequest? seen = null;
        harness.Editor.ContextMenuResolved += (_, e) => seen = e.Request;

        await harness.Editor.RequestContextAsync(
            SurfaceContextSource.Programmatic,
            InsideAddedOnly,
            KeyModifiers.None);

        // Жест кончился — дерево снова читается как есть, и добавленный контейнер
        // виден. Останься снимок жить дальше, редактор до конца своих дней отвечал бы
        // по списку контейнеров из давно законченной протяжки.
        Assert.NotNull(seen);

        // Целью стал сам добавленный контейнер: точка лежит в нём и вне всего
        // размеченного. По устаревшему снимку его не существует, и ответом была бы
        // форма, внутри которой он лежит.
        Assert.Equal("Added", (seen!.Target?.Target as Control)?.Name);
    }

    /// <summary>
    /// Потеря захвата — второй выход из жеста, и снимок обязан сниматься на нём тоже.
    /// </summary>
    /// <remarks>
    /// Проверяется тем же способом и по той же причине, что и обычное отпускание:
    /// вторым жестом рамки это не видно вовсе, потому что вход в рамку снимает
    /// снимок заново.
    /// </remarks>
    [AvaloniaFact]
    public async Task Losing_Capture_Releases_The_Snapshot()
    {
        var harness = CreatePlaced();
        var from = EmptyArea;

        IPointer? pointer = null;
        void Capture(object? sender, PointerPressedEventArgs e) => pointer ??= e.Pointer;

        harness.Editor.AddHandler(InputElement.PointerPressedEvent, Capture, handledEventsToo: true);
        harness.Window.MouseDown(from, MouseButton.Left);
        harness.Editor.RemoveHandler(InputElement.PointerPressedEvent, (EventHandler<PointerPressedEventArgs>)Capture);

        harness.Window.MouseMove(from + new Vector(-40, -30));

        pointer!.Capture(null);
        Assert.False(harness.Editor.IsSelecting);

        AddNestedContainer(harness);

        SurfaceContextRequest? seen = null;
        harness.Editor.ContextMenuResolved += (_, e) => seen = e.Request;

        await harness.Editor.RequestContextAsync(
            SurfaceContextSource.Programmatic,
            InsideAddedOnly,
            KeyModifiers.None);

        Assert.NotNull(seen);

        // Целью стал сам добавленный контейнер: точка лежит в нём и вне всего
        // размеченного. По устаревшему снимку его не существует, и ответом была бы
        // форма, внутри которой он лежит.
        Assert.Equal("Added", (seen!.Target?.Target as Control)?.Name);
    }
}
