using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using ArxisStudio.Controls;
using Xunit;
using ArxisStudio.Surface.Editing;

namespace ArxisStudio.Tests;

/// <summary>
/// Замена слоя направляющих собственным.
/// </summary>
/// <remarks>
/// Слой ставится <b>рядом</b> с редактором, как линейка, а не вместо части его шаблона:
/// свой шаблон редактору написать всё равно нельзя — он называет типы, которые остаются
/// internal, — а поверх редактора хост кладёт что угодно сам.
/// <para>
/// Отсюда и состав опубликованного: сами данные (<c>SnapGuides</c>, <c>SpacingHints</c>,
/// <c>UserGuides</c>, <c>GuidePreview</c>), модель линии и слой, который их рисует.
/// Резолверы остались внутри — считать заново хосту незачем.
/// </para>
/// </remarks>
public class GuideLayerReplacementTests
{
    private sealed record Stand(EditorHarness Harness, SnapGuideLayer Layer, ObservableCollection<DesignGuide> Guides);

    private static Stand Create()
    {
        var harness = EditorHarness.Create();

        var guides = new ObservableCollection<DesignGuide> { DesignGuide.Vertical(150) };
        harness.Editor.Guides = guides;

        // Ровно та композиция, которую собирает хост: редактор и его собственный слой
        // в одной ячейке, слой сверху и без ввода.
        var layer = new SnapGuideLayer { Editor = harness.Editor, IsHitTestVisible = false };
        var overlay = new Grid();

        harness.Window.Content = null;
        overlay.Children.Add(harness.Editor);
        overlay.Children.Add(layer);
        harness.Window.Content = overlay;

        harness.RunLayout();
        harness.PlaceContainer(0, new Point(100, 100), new Size(200, 150));
        return new Stand(harness, layer, guides);
    }

    /// <summary>Встроенный слой из шаблона редактора.</summary>
    private static SnapGuideLayer BuiltIn(EditorHarness harness) =>
        harness.Editor.GetVisualDescendants().OfType<SnapGuideLayer>().First();

    // ---- Композиция -----------------------------------------------------------

    /// <summary>
    /// Слою снаружи достаточно одного <c>Editor</c>.
    /// </summary>
    /// <remarks>
    /// Шесть отдельных привязок обязаны были бы совпадать между собой, и рассинхронить
    /// их проще, чем заметить. То же решение, что у линейки.
    /// </remarks>
    [AvaloniaFact]
    public void One_Property_Wires_The_Whole_Layer()
    {
        var stand = Create();

        stand.Harness.Editor.ViewportZoom = 2.0;
        stand.Harness.Editor.ViewportLocation = new Point(30, 40);
        stand.Harness.RunLayout();

        Assert.Equal(2.0, stand.Layer.ViewportZoom);
        Assert.Equal(new Point(30, 40), stand.Layer.ViewportLocation);
        Assert.Equal(DesignGuide.Vertical(150), Assert.Single(stand.Layer.UserGuides!));
    }

    [AvaloniaFact]
    public void A_Guide_Added_Later_Reaches_The_Layer()
    {
        var stand = Create();

        stand.Guides.Add(DesignGuide.Horizontal(220));
        stand.Harness.RunLayout();

        Assert.Equal(2, stand.Layer.UserGuides!.Count);
    }

    /// <summary>
    /// Линии и подсказки жеста доходят до внешнего слоя.
    /// </summary>
    [AvaloniaFact]
    public void Gesture_Data_Reaches_The_Layer()
    {
        var stand = Create();
        var from = stand.Harness.CentreOf(stand.Harness.Nested(0));

        stand.Harness.Window.MouseDown(from, MouseButton.Left);
        stand.Harness.Window.MouseMove(from + new Vector(6, 4));
        stand.Harness.Window.MouseMove(from + new Vector(40, 30));
        stand.Harness.RunLayout();

        Assert.Equal(stand.Harness.Editor.SnapGuides, stand.Layer.Guides);
        Assert.Equal(stand.Harness.Editor.SpacingHints, stand.Layer.SpacingHints);

        stand.Harness.Window.MouseUp(from + new Vector(40, 30), MouseButton.Left);
    }

    [AvaloniaFact]
    public void The_Dragged_Guide_Preview_Reaches_The_Layer()
    {
        var stand = Create();
        stand.Harness.Editor.GuideChangeRequested += (_, _) => { };

        stand.Harness.Window.MouseDown(new Point(150, 300), MouseButton.Left);
        stand.Harness.Window.MouseMove(new Point(210, 300));
        stand.Harness.RunLayout();

        Assert.Equal(DesignGuide.Vertical(210), stand.Layer.GuidePreview);

        stand.Harness.Window.MouseUp(new Point(210, 300), MouseButton.Left);
    }

    // ---- Гашение встроенного --------------------------------------------------

    /// <summary>
    /// Два выключателя вместе гасят встроенный слой целиком.
    /// </summary>
    /// <remarks>
    /// Они раздельные, потому что описывают разные вещи: подсказку, живущую внутри
    /// жеста, и линию, поставленную человеком. Хосту, рисующему всё сам, нужны оба.
    /// </remarks>
    [AvaloniaFact]
    public void Both_Switches_Silence_The_Built_In_Layer()
    {
        var stand = Create();
        var builtIn = BuiltIn(stand.Harness);

        Assert.True(builtIn.ShowSnapGuides);
        Assert.True(builtIn.ShowUserGuides);

        stand.Harness.Editor.ShowSnapGuides = false;
        stand.Harness.Editor.ShowGuides = false;
        stand.Harness.RunLayout();

        Assert.False(builtIn.ShowSnapGuides);
        Assert.False(builtIn.ShowUserGuides);
    }

    /// <summary>
    /// Выключатели гасят встроенный слой, но не слой хоста.
    /// </summary>
    /// <remarks>
    /// Иначе заменить отрисовку было бы нечем: гася встроенный слой, хост гасил бы
    /// и свой. Данные слой снаружи получает, видимостью распоряжается сам.
    /// </remarks>
    [AvaloniaFact]
    public void Silencing_The_Built_In_Layer_Leaves_The_Replacement_Alone()
    {
        var stand = Create();

        stand.Harness.Editor.ShowSnapGuides = false;
        stand.Harness.Editor.ShowGuides = false;
        stand.Harness.RunLayout();

        Assert.True(stand.Layer.ShowSnapGuides);
        Assert.True(stand.Layer.ShowUserGuides);
        Assert.Equal(DesignGuide.Vertical(150), Assert.Single(stand.Layer.UserGuides!));
    }

    /// <summary>
    /// Погашенный показ не выключает саму привязку.
    /// </summary>
    /// <remarks>
    /// То же правило, что у сетки при <c>ShowGrid="False"</c>: выключатель прячет,
    /// а не отменяет. Хост, рисующий направляющие сам, ожидает, что притяжение
    /// продолжит работать.
    /// </remarks>
    [AvaloniaFact]
    public void Hiding_Does_Not_Disable_Snapping()
    {
        var stand = Create();
        stand.Harness.Editor.InteractionOptions.IsSnapToGuidesEnabled = true;
        stand.Harness.Editor.ShowSnapGuides = false;
        stand.Harness.RunLayout();

        // Nested занимает 110..170, Sibling начинается на 210. Сдвиг на 37 не доводит
        // правый край трёх единиц, и выравнивание их добирает — при выключенном показе.
        var nested = stand.Harness.Nested(0);
        var from = stand.Harness.CentreOf(nested);

        stand.Harness.Window.MouseDown(from, MouseButton.Left);
        stand.Harness.Window.MouseMove(from + new Vector(6, 0));
        stand.Harness.Window.MouseMove(from + new Vector(37, 0));
        stand.Harness.Window.MouseUp(from + new Vector(37, 0), MouseButton.Left);
        stand.Harness.RunLayout();

        Assert.Equal(150, stand.Harness.Editor.GetDesignPosition(nested).X);
    }
}
