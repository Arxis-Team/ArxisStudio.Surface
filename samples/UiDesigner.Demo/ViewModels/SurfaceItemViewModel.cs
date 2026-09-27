using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UiDesigner.Demo.ViewModels;

public partial class SurfaceItemViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Location))]
    private double _x;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Location))]
    private double _y;

    public Point Location
    {
        get => new Point(X, Y);
        set
        {
            X = value.X;
            Y = value.Y;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Имя формы для панели групп.
    /// </summary>
    /// <remarks>
    /// Своё, а не снятое с контрола: у контейнера имени нет, а тип view-модели
    /// читается как «HomeScreenViewModel» — в списке это шум.
    /// </remarks>
    public string Title { get; protected set; } = "Форма";

    [ObservableProperty] private double _width = 400;
    [ObservableProperty] private double _height = 300;

    protected SurfaceItemViewModel(double x, double y)
    {
        X = x;
        Y = y;
    }
}
