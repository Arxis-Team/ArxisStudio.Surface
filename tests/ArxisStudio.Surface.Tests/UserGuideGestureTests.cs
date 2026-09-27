using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface.Editing;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Tests;

/// <summary>
/// Перемещение и удаление пользовательской направляющей указателем.
/// </summary>
/// <remarks>
/// Жест устроен как перестановка среди соседей и по той же причине: набором владеет
/// хост, поэтому во время протяжки редактор ничего не меняет — показывает превью, —
/// а на отпускании один раз просит. Пока обработчик не выставил <c>Handled</c>,
/// линия остаётся там, где была.
/// <para>
/// Редактор в стенде намеренно не занимает всё окно: только так проверяется отпускание
/// <b>за его пределами</b>, которым направляющая убирается. В headless движение мыши
/// за границу окна не доставляется вовсе, и тест на полноэкранном редакторе проходил бы
/// независимо от того, работает правило или нет.
/// </para>
/// </remarks>
public class UserGuideGestureTests
{
    private const double Inset = 40;
    private static readonly Point ContainerLocation = new(100, 100);
    private static readonly Size ContainerSize = new(200, 150);

    /// <summary>Координата вертикальной направляющей в мировых единицах.</summary>
    private const double GuideX = 150;

    private sealed record Log(List<SurfaceGuideChangeRequestedEventArgs> Requests);

    /// <summary>Переводит точку редактора в координаты окна.</summary>
    private static Point InWindow(double x, double y) => new(x + Inset, y + Inset);

    private static (EditorHarness Harness, ObservableCollection<SurfaceGuide> Guides) Create()
    {
        var nodes = new List<TestNode> { new("guide0") };

        var editor = new DesignEditor
        {
            ItemsSource = nodes,
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<TestNode>((_, _) =>
            {
                var nested = new Border { Name = "Nested", Width = 60, Height = 40 };
                Layout.SetX(nested, 10);
                Layout.SetY(nested, 10);

                var panel = new AbsolutePanel();
                panel.Children.Add(nested);
                return panel;
            }, supportsRecycling: false)
        };

        editor.InteractionOptions.IsSnapToGridEnabled = false;
        editor.InteractionOptions.IsSnapToGuidesEnabled = false;

        var guides = new ObservableCollection<SurfaceGuide> { SurfaceGuide.Vertical(GuideX) };
        editor.Guides = guides;

        var window = new Window
        {
            Width = 800,
            Height = 600,
            Content = new Border { Margin = new Thickness(Inset), Child = editor }
        };

        window.Show();

        var harness = EditorHarness.Adopt(window, editor, nodes);
        harness.RunLayout();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);
        return (harness, guides);
    }

    private static Log Listen(EditorHarness harness, bool handle = false)
    {
        var log = new Log(new List<SurfaceGuideChangeRequestedEventArgs>());
        harness.Editor.GuideChangeRequested += (_, e) =>
        {
            log.Requests.Add(e);
            e.Handled = handle;
        };

        return log;
    }

    /// <summary>Тянет направляющую из точки редактора в точку редактора.</summary>
    private static void DragGuide(EditorHarness harness, Point from, Point to, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        harness.Window.MouseDown(InWindow(from.X, from.Y), MouseButton.Left, modifiers);
        harness.Window.MouseMove(InWindow(to.X, to.Y), modifiers);
        harness.Window.MouseUp(InWindow(to.X, to.Y), MouseButton.Left, modifiers);
        harness.RunLayout();
    }

    // ---- Запрос ---------------------------------------------------------------

    [AvaloniaFact]
    public void Dragging_A_Guide_Asks_To_Move_It()
    {
        var (harness, _) = Create();
        var log = Listen(harness);

        DragGuide(harness, new Point(GuideX, 300), new Point(GuideX + 60, 300));

        var request = Assert.Single(log.Requests);
        Assert.Equal(SurfaceGuideChangeKind.Move, request.Kind);
        Assert.Equal(SurfaceGuide.Vertical(GuideX), request.Original);
        Assert.Equal(SurfaceGuide.Vertical(GuideX + 60), request.Guide);
    }

    /// <summary>
    /// Невыполненный запрос ничего не меняет.
    /// </summary>
    /// <remarks>
    /// Это и есть граница ответственности: набор принадлежит хосту, и редактор,
    /// не дождавшись <c>Handled</c>, обязан оставить линию на месте.
    /// </remarks>
    [AvaloniaFact]
    public void An_Unhandled_Request_Leaves_The_Guide_Alone()
    {
        var (harness, guides) = Create();
        Listen(harness);

        DragGuide(harness, new Point(GuideX, 300), new Point(GuideX + 60, 300));

        Assert.Equal(SurfaceGuide.Vertical(GuideX), Assert.Single(guides));
        Assert.Equal(SurfaceGuide.Vertical(GuideX), Assert.Single(harness.Editor.UserGuides));
    }

    [AvaloniaFact]
    public void A_Handled_Request_Moves_The_Line()
    {
        var (harness, guides) = Create();
        harness.Editor.GuideChangeRequested += (_, e) =>
        {
            guides[guides.IndexOf(e.Original!.Value)] = e.Guide;
            e.Handled = true;
        };

        DragGuide(harness, new Point(GuideX, 300), new Point(GuideX + 60, 300));

        Assert.Equal(SurfaceGuide.Vertical(GuideX + 60), Assert.Single(harness.Editor.UserGuides));
    }

    [AvaloniaFact]
    public void Returning_To_The_Original_Position_Asks_For_Nothing()
    {
        var (harness, _) = Create();
        var log = Listen(harness);

        harness.Window.MouseDown(InWindow(GuideX, 300), MouseButton.Left);
        harness.Window.MouseMove(InWindow(GuideX + 60, 300));
        harness.Window.MouseMove(InWindow(GuideX, 300));
        harness.Window.MouseUp(InWindow(GuideX, 300), MouseButton.Left);
        harness.RunLayout();

        Assert.Empty(log.Requests);
    }

    /// <summary>
    /// Обход подписчиков останавливается на первом выполнившем запрос.
    /// </summary>
    [AvaloniaFact]
    public void Only_The_First_Handler_Applies_The_Request()
    {
        var (harness, _) = Create();
        var seen = 0;

        harness.Editor.GuideChangeRequested += (_, e) => { seen++; e.Handled = true; };
        harness.Editor.GuideChangeRequested += (_, _) => seen++;

        DragGuide(harness, new Point(GuideX, 300), new Point(GuideX + 60, 300));

        Assert.Equal(1, seen);
    }

    /// <summary>
    /// Без подписчика жеста нет вовсе.
    /// </summary>
    /// <remarks>
    /// То же правило, что у перестановки: вести линию за курсором, зная, что на
    /// отпускании ничего не произойдёт, — значит обещать несуществующее.
    /// </remarks>
    [AvaloniaFact]
    public void Without_A_Subscriber_The_Gesture_Does_Not_Start()
    {
        var (harness, _) = Create();

        harness.Window.MouseDown(InWindow(GuideX, 300), MouseButton.Left);
        harness.Window.MouseMove(InWindow(GuideX + 60, 300));
        harness.RunLayout();

        Assert.Null(harness.Editor.GuidePreview);

        // Нажатие досталось обычному маршруту: по пустому холсту это рамка.
        Assert.True(harness.Editor.IsSelecting);

        harness.Window.MouseUp(InWindow(GuideX + 60, 300), MouseButton.Left);
    }

    // ---- Показ ----------------------------------------------------------------

    /// <summary>
    /// Спрятанную направляющую нельзя схватить.
    /// </summary>
    /// <remarks>
    /// Жест по невидимому объекту читается как самопроизвольное поведение редактора,
    /// поэтому <c>ShowGuides</c> закрывает и захват, а не только отрисовку.
    /// </remarks>
    [AvaloniaFact]
    public void A_Hidden_Guide_Cannot_Be_Grabbed()
    {
        var (harness, _) = Create();
        var log = Listen(harness);
        harness.Editor.ShowGuides = false;

        DragGuide(harness, new Point(GuideX, 300), new Point(GuideX + 60, 300));

        Assert.Empty(log.Requests);
    }

    /// <summary>
    /// Показ выключает показ, а не набор.
    /// </summary>
    /// <remarks>
    /// Это не удаление: <c>Guides</c> остаётся как был, и притяжение к спрятанной линии
    /// продолжает работать — ровно как у сетки, которую <c>ShowGrid</c> тоже только прячет.
    /// </remarks>
    [AvaloniaFact]
    public void Hiding_Keeps_The_Set_And_The_Snapping()
    {
        var (harness, guides) = Create();
        harness.Editor.ShowGuides = false;

        Assert.Single(guides);
        Assert.Single(harness.Editor.UserGuides);

        harness.Editor.ShowGuides = true;
        Listen(harness);

        // И линия по-прежнему на месте: её снова можно схватить.
        DragGuide(harness, new Point(GuideX, 300), new Point(GuideX + 60, 300));
        Assert.Equal(SurfaceGuide.Vertical(GuideX), Assert.Single(guides));
    }

    // ---- Удаление -------------------------------------------------------------

    /// <summary>
    /// Уведённая за пределы редактора направляющая убирается.
    /// </summary>
    [AvaloniaFact]
    public void Dropping_Outside_The_Editor_Asks_To_Remove_It()
    {
        var (harness, _) = Create();
        var log = Listen(harness);

        // Отпускание в отступе окна: внутри окна, снаружи редактора.
        harness.Window.MouseDown(InWindow(GuideX, 300), MouseButton.Left);
        harness.Window.MouseMove(new Point(10, 300 + Inset));
        harness.Window.MouseUp(new Point(10, 300 + Inset), MouseButton.Left);
        harness.RunLayout();

        var request = Assert.Single(log.Requests);
        Assert.Equal(SurfaceGuideChangeKind.Remove, request.Kind);
        Assert.Equal(SurfaceGuide.Vertical(GuideX), request.Guide);
    }

    // ---- Превью ---------------------------------------------------------------

    [AvaloniaFact]
    public void The_Preview_Follows_The_Pointer_And_Clears_After()
    {
        var (harness, _) = Create();
        Listen(harness);

        harness.Window.MouseDown(InWindow(GuideX, 300), MouseButton.Left);
        harness.Window.MouseMove(InWindow(GuideX + 60, 300));
        harness.RunLayout();

        Assert.Equal(SurfaceGuide.Vertical(GuideX + 60), harness.Editor.GuidePreview);

        harness.Window.MouseUp(InWindow(GuideX + 60, 300), MouseButton.Left);
        harness.RunLayout();

        Assert.Null(harness.Editor.GuidePreview);
    }

    // ---- Приоритет жеста ------------------------------------------------------

    /// <summary>
    /// Направляющая забирает нажатие у контейнера под ней.
    /// </summary>
    /// <remarks>
    /// Линия нарисована поверх всего, поэтому и жест должна получать первой. Через
    /// всплытие это недостижимо: контейнер обработает нажатие раньше, захватит
    /// указатель и начнёт своё перетаскивание. Отсюда перехват в фазе туннелирования.
    /// </remarks>
    [AvaloniaFact]
    public void A_Guide_Wins_The_Press_Over_The_Container_Under_It()
    {
        var (harness, _) = Create();
        Listen(harness);

        // Точка лежит и на направляющей, и внутри контейнера (100..300 по X).
        var onBoth = new Point(GuideX, ContainerLocation.Y + 20);

        harness.Window.MouseDown(InWindow(onBoth.X, onBoth.Y), MouseButton.Left);
        harness.Window.MouseMove(InWindow(onBoth.X + 40, onBoth.Y));
        harness.RunLayout();

        Assert.NotNull(harness.Editor.GuidePreview);
        Assert.Empty(harness.Editor.SelectedTargets);

        harness.Window.MouseUp(InWindow(onBoth.X + 40, onBoth.Y), MouseButton.Left);
    }

    /// <summary>
    /// Мимо направляющей нажатие идёт обычным маршрутом.
    /// </summary>
    /// <remarks>
    /// Перехват задуман так, чтобы его влияние на обычный ввод было ровно нулевым,
    /// и проверяется это тем, что выбор контейнера продолжает работать.
    /// </remarks>
    [AvaloniaFact]
    public void A_Press_Away_From_The_Guide_Selects_As_Usual()
    {
        var (harness, _) = Create();
        Listen(harness);

        harness.Window.MouseDown(InWindow(ContainerLocation.X + 40, ContainerLocation.Y + 30), MouseButton.Left);
        harness.Window.MouseUp(InWindow(ContainerLocation.X + 40, ContainerLocation.Y + 30), MouseButton.Left);
        harness.RunLayout();

        Assert.Null(harness.Editor.GuidePreview);
        Assert.NotEmpty(harness.Editor.SelectedTargets);
    }

    // ---- Привязка -------------------------------------------------------------

    [AvaloniaFact]
    public void The_Dragged_Guide_Snaps_To_The_Grid()
    {
        var (harness, _) = Create();
        harness.Editor.InteractionOptions.IsSnapToGridEnabled = true;
        var log = Listen(harness);

        DragGuide(harness, new Point(GuideX, 300), new Point(213, 300));

        // Шаг сетки 20: 213 садится на 220.
        Assert.Equal(SurfaceGuide.Vertical(220), Assert.Single(log.Requests).Guide);
    }

    [AvaloniaFact]
    public void The_Bypass_Modifier_Places_It_Exactly()
    {
        var (harness, _) = Create();
        harness.Editor.InteractionOptions.IsSnapToGridEnabled = true;
        var log = Listen(harness);

        DragGuide(harness, new Point(GuideX, 300), new Point(213, 300), RawInputModifiers.Alt);

        Assert.Equal(SurfaceGuide.Vertical(213), Assert.Single(log.Requests).Guide);
    }
}
