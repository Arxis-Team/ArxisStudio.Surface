using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Правка выбранного без контейнера (ADR 0010): сдвиг записью в модель, единица правки и отмена.
/// </summary>
/// <remarks>
/// Стенд: окно 800 × 600, поверхность над сеткой 20 × 20 моделей — столбцы через 150, ряды через 100,
/// элемент 100 × 60 — с привязкой положения. Холст в начале координат, масштаб 1: развёрнут левый верхний
/// угол сетки, дальний угол свёрнут.
/// </remarks>
public class SelectionWithoutContainersTests
{
    private static readonly Size ItemSize = new(100, 60);

    private sealed class Place(Point location) : INotifyPropertyChanged
    {
        private Point _location = location;

        public Point Location
        {
            get => _location;
            set
            {
                if (_location == value)
                    return;

                _location = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Location)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed record Stand(Window Window, SurfaceView View, ObservableCollection<object> Items)
    {
        public VirtualizingSurfacePanel Panel => View.GetVisualDescendants().OfType<VirtualizingSurfacePanel>().Single();

        public Place Model(int index) => (Place)Items[index];

        public SurfaceItem? Container(int index) => View.ContainerFromIndex(index) as SurfaceItem;

        public int Far => Items.Count - 1;

        public void RunLayout()
        {
            var manager = Window.GetLayoutManager();
            manager?.ExecuteInitialLayoutPass();
            manager?.ExecuteLayoutPass();
        }

        public void SelectAll()
        {
            View.Focus();
            Window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            RunLayout();
        }
    }

    private static Point LocationOf(int index) => new(index % 20 * 150, index / 20 * 100);

    private static Stand Create(BindingMode mode = BindingMode.Default)
    {
        var items = new ObservableCollection<object>();
        for (var i = 0; i < 400; i++)
            items.Add(new Place(LocationOf(i)));

        var view = new SurfaceView
        {
            ItemsSource = items,
            ItemLocationBinding = new Binding(nameof(Place.Location)) { Mode = mode },
            EstimatedItemSize = ItemSize,
            ItemTemplate = new FuncDataTemplate<Place>((_, _) => new Border { Width = ItemSize.Width, Height = ItemSize.Height })
        };

        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        var stand = new Stand(window, view, items);
        stand.RunLayout();
        return stand;
    }

    private static void Drag(Stand stand, Vector by)
    {
        var from = new Point(50, 30);
        stand.Window.MouseDown(from, MouseButton.Left);
        stand.Window.MouseMove(from + new Vector(10, 5));
        stand.Window.MouseMove(from + by);
        stand.Window.MouseUp(from + by, MouseButton.Left);
        stand.RunLayout();
    }

    [AvaloniaFact]
    public void A_Drag_Moves_The_Collapsed_Selection_Through_The_Model()
    {
        // Весь выбор едет на дельту источника; свёрнутые — записью в модель, не разворачиваясь, и их
        // сдвиги приходят отдельным списком той же единицы правки.
        var stand = Create();
        stand.SelectAll();
        var realized = stand.Panel.RealizedCount;
        var edits = new List<SurfaceEditCompletedEventArgs>();
        stand.View.EditCompleted += (_, e) => edits.Add(e);

        Drag(stand, new Vector(40, 30));

        Assert.All(Enumerable.Range(0, stand.Items.Count), i =>
            Assert.Equal(LocationOf(i) + new Vector(40, 30), stand.Model(i).Location));
        Assert.Null(stand.Container(stand.Far));
        Assert.Equal(realized, stand.Panel.RealizedCount);

        var edit = Assert.Single(edits);
        Assert.Equal(stand.Items.Count, edit.Changes.Count + edit.ItemChanges.Count);
        var far = Assert.Single(edit.ItemChanges, c => ReferenceEquals(c.Item, stand.Model(stand.Far)));
        Assert.Equal(LocationOf(stand.Far), far.OldLocation);
        Assert.Equal(LocationOf(stand.Far) + new Vector(40, 30), far.NewLocation);
        Assert.All(edit.Changes, c => Assert.Same(c.Target, stand.View.ContainerFromItem(c.Item!)));
    }

    [AvaloniaFact]
    public void Arrows_Move_The_Collapsed_Selection()
    {
        var stand = Create();
        stand.SelectAll();
        var step = stand.View.InteractionOptions.NudgeStep;
        var edits = new List<SurfaceEditCompletedEventArgs>();
        stand.View.EditCompleted += (_, e) => edits.Add(e);

        stand.Window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);

        Assert.Equal(LocationOf(stand.Far) + new Vector(step, 0), stand.Model(stand.Far).Location);
        Assert.Equal(LocationOf(0) + new Vector(step, 0), stand.Model(0).Location);
        Assert.Null(stand.Container(stand.Far));
        Assert.NotEmpty(Assert.Single(edits).ItemChanges);
    }

    [AvaloniaFact]
    public void Undo_And_Redo_Write_The_Model_Without_Realizing()
    {
        var stand = Create();
        using var history = new SurfaceHistory(stand.View);
        stand.SelectAll();
        Drag(stand, new Vector(40, 30));
        var realized = stand.Panel.RealizedCount;

        Assert.True(history.Undo());
        stand.RunLayout();
        Assert.All(Enumerable.Range(0, stand.Items.Count), i => Assert.Equal(LocationOf(i), stand.Model(i).Location));
        Assert.Null(stand.Container(stand.Far));
        Assert.Equal(realized, stand.Panel.RealizedCount);

        Assert.True(history.Redo());
        stand.RunLayout();
        Assert.Equal(LocationOf(stand.Far) + new Vector(40, 30), stand.Model(stand.Far).Location);
        Assert.Null(stand.Container(stand.Far));
    }

    [AvaloniaFact]
    public void Undoing_A_Move_Of_An_Item_That_Collapsed_Since_Writes_The_Model()
    {
        // Сдвиг развёрнутого, свернувшегося к отмене, — в модель, а не разворачиванием.
        var stand = Create();
        var edits = new List<SurfaceEditCompletedEventArgs>();
        stand.View.EditCompleted += (_, e) => edits.Add(e);
        Drag(stand, new Vector(40, 30));
        var change = Assert.Single(Assert.Single(edits).Changes);

        stand.View.Selection.Clear();
        stand.View.Focus();
        stand.View.ViewportLocation = new Point(-5000, -5000);
        stand.RunLayout();
        Assert.Null(stand.Container(0));

        stand.View.Revert(change);

        Assert.Equal(LocationOf(0), stand.Model(0).Location);
        Assert.Null(stand.Container(0));
    }

    [AvaloniaFact]
    public void Delete_Names_Every_Selected_Item()
    {
        // Targets — у развёрнутых, элементы — весь выбор.
        var stand = Create();
        stand.SelectAll();
        var requests = new List<SurfaceDeleteRequestedEventArgs>();
        stand.View.DeleteRequested += (_, e) =>
        {
            requests.Add(e);
            e.Handled = true;
        };

        stand.Window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);

        var request = Assert.Single(requests);
        Assert.Equal(stand.Items, request.Items);
        Assert.Equal(stand.Panel.RealizedCount, request.Targets.Count);
    }

    [AvaloniaFact]
    public void Delete_Asks_For_A_Selection_With_Nothing_Realized()
    {
        // Выбор рамкой за окном — без единого развёрнутого, и запрос всё равно уходит. Клавиша —
        // событием на поверхности, а не фокусом: фокус разворачивает выбранный.
        var stand = Create();
        stand.View.CommitSelection(new Rect(2000, 1200, 700, 500), isCtrlPressed: false, useContainerSelection: true);
        Assert.Empty(stand.View.SelectedTargets);
        var requests = new List<SurfaceDeleteRequestedEventArgs>();
        stand.View.DeleteRequested += (_, e) => requests.Add(e);

        stand.View.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete });

        var request = Assert.Single(requests);
        Assert.Equal(stand.View.SelectedItems!.Cast<object?>(), request.Items);
        Assert.Empty(request.Targets);
    }

    [AvaloniaFact]
    public void A_One_Way_Binding_Realizes_What_It_Cannot_Write()
    {
        // Модель записи не принимает — элемент едет прежним путём: разворачивается и двигается
        // контейнером, а модель остаётся на месте.
        var stand = Create(BindingMode.OneWay);
        stand.SelectAll();
        var from = new Point(50, 30);

        stand.Window.MouseDown(from, MouseButton.Left);
        stand.Window.MouseMove(from + new Vector(10, 5));
        stand.Window.MouseMove(from + new Vector(40, 30));

        var far = stand.Container(stand.Far);
        Assert.NotNull(far);
        Assert.Equal(LocationOf(stand.Far) + new Vector(40, 30), far.Location);
        Assert.Equal(LocationOf(stand.Far), stand.Model(stand.Far).Location);
        stand.Window.MouseUp(from + new Vector(40, 30), MouseButton.Left);
    }
}
