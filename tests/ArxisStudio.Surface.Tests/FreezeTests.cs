using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Tests;

/// <summary>
/// Стоп-кадр: <see cref="SurfaceView.Freeze"/> показывает снимок и не берёт жестов (ADR 0023).
/// </summary>
/// <remarks>
/// Хост ставит кадр на время, пока снимает старое содержимое холста и ставит новое. Отсюда три вещи,
/// которые проверяются: под кадром ничего не начинается, начатое заканчивается, а сам кадр не держит
/// снятого — иначе хост не смог бы доказать, что старая сборка отпущена.
/// </remarks>
public class FreezeTests
{
    private static readonly Point ContainerLocation = new(80, 80);
    private static readonly Size ContainerSize = new(200, 150);
    private static readonly Point EmptyCanvas = new(600, 420);

    private static EditorHarness Create()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);
        return harness;
    }

    private static Image FreezeLayer(SurfaceView view) =>
        view.GetVisualDescendants().OfType<Image>().Single(image => image.Name == "PART_FreezeLayer");

    /// <summary>
    /// Где контрол стоит в своей форме на самом деле, после раскладки.
    /// </summary>
    /// <remarks>
    /// Не координаты поверхности: те отстают на проход диспетчера, и чтение до него дало бы
    /// одинаковый ноль до жеста и после — проверка прошла бы, ничего не проверив.
    /// </remarks>
    private static Point ArrangedIn(EditorHarness harness, Control control)
    {
        harness.RunLayout();
        return control.TranslatePoint(default, harness.Container(0))!.Value;
    }

    [AvaloniaFact]
    public void A_Frozen_Surface_Takes_No_Pointer_Gesture()
    {
        var harness = Create();
        var nested = harness.CentreOf(harness.Nested(0));
        var position = ArrangedIn(harness, harness.Nested(0));
        var zoom = harness.Editor.ViewportZoom;

        using var frozen = harness.Editor.Freeze();

        harness.Window.MouseDown(nested, MouseButton.Left);
        harness.Window.MouseMove(nested + new Vector(40, 0));
        harness.Window.MouseUp(nested + new Vector(40, 0), MouseButton.Left);
        harness.RunLayout();

        Assert.Equal(0, harness.Editor.SelectedTargetsCount);
        Assert.Equal(position, ArrangedIn(harness, harness.Nested(0)));

        harness.Window.MouseDown(EmptyCanvas, MouseButton.Left);
        harness.Window.MouseMove(EmptyCanvas + new Vector(40, 30));

        Assert.False(harness.Editor.IsSelecting, "Рамка на стоп-кадре не начинается.");
        Assert.False(harness.Editor.IsInteracting);

        harness.Window.MouseUp(EmptyCanvas + new Vector(40, 30), MouseButton.Left);
        harness.Window.MouseWheel(EmptyCanvas, new Vector(0, 1));

        Assert.Equal(zoom, harness.Editor.ViewportZoom);

        // Щипок пальцами и тачпада приходит готовыми событиями — так их поднимают и здесь,
        // как в PinchZoomTests.
        harness.Editor.RaiseEvent(new PinchEventArgs(1.5, EmptyCanvas) { RoutedEvent = InputElement.PinchEvent });
        harness.Editor.RaiseEvent(new PinchEndedEventArgs { RoutedEvent = InputElement.PinchEndedEvent });
        harness.Editor.RaiseEvent(new PointerDeltaEventArgs(
            InputElement.PointerTouchPadGestureMagnifyEvent,
            harness.Editor,
            new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true),
            harness.Editor,
            EmptyCanvas,
            0,
            PointerPointProperties.None,
            KeyModifiers.None,
            new Vector(0.5, 0.5)));

        Assert.Equal(zoom, harness.Editor.ViewportZoom);
    }

    [AvaloniaFact]
    public void A_Frozen_Surface_Takes_No_Key_Command()
    {
        var harness = Create();
        var nested = harness.CentreOf(harness.Nested(0));
        harness.Window.MouseDown(nested, MouseButton.Left);
        harness.Window.MouseUp(nested, MouseButton.Left);
        harness.RunLayout();

        var requests = 0;
        harness.Editor.DeleteRequested += (_, e) =>
        {
            requests++;
            e.Handled = true;
        };

        var position = ArrangedIn(harness, harness.Nested(0));

        using (harness.Editor.Freeze())
        {
            harness.Window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
            harness.Window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            harness.RunLayout();

            Assert.Equal(0, requests);
            Assert.Equal(position, ArrangedIn(harness, harness.Nested(0)));
        }

        harness.Window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);

        Assert.Equal(1, requests);
    }

    [AvaloniaFact]
    public void Thawing_Gives_The_Gestures_Back()
    {
        var harness = Create();
        var nested = harness.CentreOf(harness.Nested(0));
        var zoom = harness.Editor.ViewportZoom;

        harness.Editor.Freeze().Dispose();

        harness.Window.MouseDown(nested, MouseButton.Left);
        harness.Window.MouseUp(nested, MouseButton.Left);
        harness.Window.MouseWheel(EmptyCanvas, new Vector(0, 1));

        Assert.Equal(1, harness.Editor.SelectedTargetsCount);
        Assert.NotEqual(zoom, harness.Editor.ViewportZoom);
    }

    [AvaloniaFact]
    public void Freezes_Nest_And_The_Last_Thaw_Restores()
    {
        var harness = Create();
        var changes = new List<bool>();
        harness.Editor.PropertyChanged += (_, e) =>
        {
            if (e.Property == SurfaceView.IsFrozenProperty)
                changes.Add((bool)e.NewValue!);
        };

        var outer = harness.Editor.Freeze();
        var inner = harness.Editor.Freeze();

        inner.Dispose();
        inner.Dispose();

        Assert.True(harness.Editor.IsFrozen, "Повторное освобождение одного кадра не отпускает другой.");

        outer.Dispose();

        Assert.False(harness.Editor.IsFrozen);
        Assert.Equal(new[] { true, false }, changes);
    }

    [AvaloniaFact]
    public void A_Frozen_Frame_Covers_The_Canvas_Until_The_Thaw()
    {
        var harness = Create();
        var layer = FreezeLayer(harness.Editor);

        Assert.False(layer.IsVisible);

        using (harness.Editor.Freeze())
        {
            Assert.True(layer.IsVisible);
            Assert.IsType<RenderTargetBitmap>(layer.Source);

            // Поверх всех слоёв шаблона: и содержимого, и рамок, и направляющих.
            var parent = (Panel)layer.GetVisualParent()!;
            Assert.Same(layer, parent.Children[^1]);
        }

        Assert.False(layer.IsVisible);
        Assert.Null(layer.Source);
    }

    [AvaloniaFact]
    public void A_Gesture_Begun_Before_The_Freeze_Ends_On_It()
    {
        var harness = Create();
        var nested = harness.CentreOf(harness.Nested(0));

        harness.Window.MouseDown(nested, MouseButton.Left);
        harness.Window.MouseMove(nested + new Vector(30, 0));
        Assert.True(harness.Editor.IsInteracting);

        using (harness.Editor.Freeze())
        {
            harness.Window.MouseUp(nested + new Vector(30, 0), MouseButton.Left);

            Assert.False(harness.Editor.IsInteracting, "Отпускание доходит до жеста и на стоп-кадре.");
        }
    }

    [AvaloniaFact]
    public void A_Frozen_Frame_Holds_No_Content()
    {
        // Контроль: без кадра снятое содержимое собирается. Иначе вторая половина ничего не доказывала бы.
        Assert.False(ContentSurvivesRemoval(freeze: false), "Снятое содержимое держит что-то, кроме кадра.");

        Assert.False(
            ContentSurvivesRemoval(freeze: true),
            "Стоп-кадр держит пиксели, а не контролы: снятое под ним обязано собираться.");
    }

    /// <summary>Ставит форму, снимает её и спрашивает, жив ли её контрол.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ContentSurvivesRemoval(bool freeze)
    {
        var (window, editor, nodes, content) = Build();

        var frozen = freeze ? editor.Freeze() : null;

        nodes.Clear();
        window.GetLayoutManager()?.ExecuteLayoutPass();
        Dispatcher.UIThread.RunJobs();

        for (var i = 0; i < 3; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
        }

        var alive = content.IsAlive;

        frozen?.Dispose();
        window.Close();

        return alive;
    }

    /// <summary>
    /// Редактор с наблюдаемой коллекцией: форму с него снимает хост, как при замене сборки.
    /// </summary>
    /// <remarks>
    /// Контрол формы шаблон отдаёт только слабой ссылкой — сильная в замыкании шаблона держала бы его
    /// сама, и проверка ничего бы не проверяла.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Window Window, UiDesignerView Editor, ObservableCollection<TestNode> Nodes, WeakReference Content) Build()
    {
        var nodes = new ObservableCollection<TestNode> { new("form") };
        var made = new WeakReference(null);

        var editor = new UiDesignerView
        {
            ItemsSource = nodes,
            ItemTemplate = new FuncDataTemplate<TestNode>((_, _) =>
            {
                var content = new Border { Width = 120, Height = 80 };
                made.Target = content;
                return content;
            }, supportsRecycling: false),
        };

        var window = new Window { Width = 800, Height = 600, Content = editor };
        window.Show();
        window.GetLayoutManager()?.ExecuteInitialLayoutPass();
        window.GetLayoutManager()?.ExecuteLayoutPass();

        Assert.True(made.IsAlive, "Шаблон формы не построил содержимое.");

        return (window, editor, nodes, made);
    }
}
