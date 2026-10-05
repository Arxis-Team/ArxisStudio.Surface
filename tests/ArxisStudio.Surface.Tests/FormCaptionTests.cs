using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Tests;

/// <summary>
/// Подпись формы (ADR 0029): имя над формой — ручка её контейнера и экранного размера на любом масштабе.
/// </summary>
/// <remarks>
/// Стенд: окно 800 × 600, дизайнер с одной формой — пользовательский элемент 300 × 200 в точке (100, 120),
/// целиком занятый размеченным контролом. Нажатие внутри формы поэтому всегда приходится на контрол, и
/// взять форму целиком, кроме как за подпись, указателем без модификатора нечем.
/// </remarks>
public class FormCaptionTests
{
    private const string Avalonia = "https://github.com/avaloniaui";
    private const string Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private sealed record Stand(Window Window, UiDesignerView View, UiDesignerFormItem Form, Border Inner)
    {
        public Control Caption => Assert.IsAssignableFrom<Control>(Form.GetVisualDescendants().OfType<Control>().Single(part => part.Name == "PART_Caption"));

        public void RunLayout()
        {
            var manager = Window.GetLayoutManager();
            manager?.ExecuteInitialLayoutPass();
            manager?.ExecuteLayoutPass();
        }

        /// <summary>Середина части в координатах окна — туда жмут.</summary>
        public Point MiddleOf(Visual part) =>
            part.TranslatePoint(new Point(part.Bounds.Width / 2, part.Bounds.Height / 2), Window)!.Value;

        /// <summary>Прямоугольник части на экране — в координатах редактора.</summary>
        public Rect OnScreen(Visual part)
        {
            var topLeft = part.TranslatePoint(default, View)!.Value;
            var bottomRight = part.TranslatePoint(new Point(part.Bounds.Width, part.Bounds.Height), View)!.Value;

            return new Rect(topLeft, bottomRight);
        }
    }

    private static Stand Create(string? caption = "Main.axaml", object? root = null)
    {
        var inner = new Border { Background = Brushes.Gray };
        Layout.SetIsTracked(inner, true);

        var form = new UiDesignerFormItem
        {
            ContentMode = SurfaceContentMode.Annotated,
            Caption = caption,
            Location = new Point(100, 120),
            Width = 300,
            Height = 200,
            Root = root ?? new UserControl { Content = inner },
        };

        var view = new UiDesignerView
        {
            SelectionMode = SelectionMode.Multiple,
            ItemsSource = new[] { form },
        };

        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();

        var stand = new Stand(window, view, form, inner);
        stand.RunLayout();
        stand.RunLayout();
        return stand;
    }

    [AvaloniaFact]
    public void The_Caption_Stands_Above_The_Form_And_Names_It()
    {
        var stand = Create();
        var caption = stand.OnScreen(stand.Caption);
        var form = stand.OnScreen(stand.Form);

        Assert.True(stand.Caption.IsEffectivelyVisible, "подписи нет");
        Assert.Equal(form.Top, caption.Bottom, 3);
        Assert.Equal(form.Left, caption.Left, 3);
        Assert.Contains(
            stand.Caption.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == "Main.axaml");
    }

    [AvaloniaFact]
    public void A_Form_Without_A_Caption_Shows_None()
    {
        var stand = Create(caption: null);

        Assert.False(stand.Caption.IsEffectivelyVisible, "подпись без имени видна");
        Assert.DoesNotContain(":captioned", stand.Form.Classes);
    }

    [AvaloniaFact]
    public void The_Caption_Keeps_Its_Screen_Size_At_Any_Zoom()
    {
        var stand = Create();
        var atOne = stand.OnScreen(stand.Caption).Size;

        stand.View.ViewportZoom = 0.15;
        stand.RunLayout();

        var small = stand.OnScreen(stand.Caption);
        var form = stand.OnScreen(stand.Form);

        Assert.Equal(atOne.Height, small.Height, 3);
        Assert.Equal(form.Top, small.Bottom, 3);
        Assert.True(small.Width <= form.Width + 0.001, $"подпись шире формы на экране: {small.Width} и {form.Width}");
    }

    [AvaloniaFact]
    public void A_Window_Caption_Stands_Above_Its_Title_Bar()
    {
        var window = (Window)AvaloniaRuntimeXamlLoader.Parse(
            $"<Window xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\" Width=\"300\" Height=\"200\" Title=\"Main\"><Border /></Window>");
        var stand = Create(root: window);
        var titleBar = stand.OnScreen(Assert.IsAssignableFrom<Control>(
            stand.Form.GetVisualDescendants().OfType<Control>().Single(part => part.Name == "PART_TitleBar")));
        var caption = stand.OnScreen(stand.Caption);

        Assert.Contains(":titled", stand.Form.Classes);
        Assert.Equal(titleBar.Top, caption.Bottom, 3);
    }

    [AvaloniaFact]
    public void Pressing_The_Caption_Takes_The_Form_Whole()
    {
        var stand = Create();

        // Внутри формы нажатие берёт контрол: подпись — единственная дорога к форме целиком.
        stand.Window.MouseDown(stand.MiddleOf(stand.Inner), MouseButton.Left);
        stand.Window.MouseUp(stand.MiddleOf(stand.Inner), MouseButton.Left);
        stand.RunLayout();

        Assert.Same(stand.Inner, Assert.Single(stand.View.SelectedTargets).Target);

        stand.Window.MouseDown(new Point(700, 560), MouseButton.Left);
        stand.Window.MouseUp(new Point(700, 560), MouseButton.Left);
        stand.Window.MouseDown(stand.MiddleOf(stand.Caption), MouseButton.Left);
        stand.Window.MouseUp(stand.MiddleOf(stand.Caption), MouseButton.Left);
        stand.RunLayout();

        var selected = Assert.Single(stand.View.SelectedTargets);

        Assert.Same(stand.Form, selected.Target);
        Assert.Equal(SurfaceSelectionScope.Container, selected.Scope);
    }

    /// <summary>
    /// Ручка поверх размеченного контрола берёт контейнер, а не контрол под ней: ручка — свойство части,
    /// а не места, и попадание по прямоугольникам её не перебивает.
    /// </summary>
    [AvaloniaFact]
    public void A_Handle_Over_A_Tracked_Control_Takes_The_Container()
    {
        var tracked = new Border { Background = Brushes.Gray };
        var handle = new Border
        {
            Background = Brushes.Transparent,
            Width = 60,
            Height = 30,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
        };

        Layout.SetIsTracked(tracked, true);
        SurfaceItem.SetIsHandle(handle, true);

        var item = new UiDesignerItem
        {
            ContentMode = SurfaceContentMode.Annotated,
            Location = new Point(100, 100),
            Width = 300,
            Height = 200,
            Content = new Grid { Children = { tracked, handle } },
        };
        var view = new UiDesignerView { SelectionMode = SelectionMode.Multiple, ItemsSource = new[] { item } };
        var window = new Window { Width = 800, Height = 600, Content = view };

        window.Show();
        window.GetLayoutManager()?.ExecuteInitialLayoutPass();
        window.GetLayoutManager()?.ExecuteLayoutPass();

        var onHandle = handle.TranslatePoint(new Point(30, 15), window)!.Value;

        window.MouseDown(onHandle, MouseButton.Left);
        window.MouseUp(onHandle, MouseButton.Left);
        window.GetLayoutManager()?.ExecuteLayoutPass();

        var selected = Assert.Single(view.SelectedTargets);

        Assert.Same(item, selected.Target);
        Assert.Equal(SurfaceSelectionScope.Container, selected.Scope);
    }

    [AvaloniaFact]
    public void Dragging_The_Caption_Moves_The_Form()
    {
        var stand = Create();
        var moves = new List<SurfaceEditCompletedEventArgs>();

        stand.View.EditCompleted += (_, e) => moves.Add(e);

        var from = stand.MiddleOf(stand.Caption);

        stand.Window.MouseDown(from, MouseButton.Left);
        stand.Window.MouseMove(from + new Vector(10, 5));
        stand.Window.MouseMove(from + new Vector(60, 40));
        stand.Window.MouseUp(from + new Vector(60, 40), MouseButton.Left);
        stand.RunLayout();

        Assert.Equal(new Point(160, 160), stand.Form.Location);

        var move = Assert.Single(moves);

        Assert.Equal(SurfaceEditKind.Move, move.Kind);
        Assert.Same(stand.Form, Assert.Single(move.Changes).Target);
    }
}
