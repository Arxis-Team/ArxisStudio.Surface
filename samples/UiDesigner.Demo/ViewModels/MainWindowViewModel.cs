using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ArxisStudio.Surface.Editing;

namespace UiDesigner.Demo.ViewModels;

/// <summary>
/// Строка в подсказке о жестах: сочетание и что оно делает.
/// </summary>
/// <param name="Keys">Сочетание, как его нажимает пользователь.</param>
/// <param name="Description">Что происходит.</param>
public sealed record GestureHint(string Keys, string Description);

public partial class MainWindowViewModel : ObservableObject
{
    public ObservableCollection<SurfaceItemViewModel> Elements { get; } = new();

    /// <summary>
    /// Пользовательские направляющие макета.
    /// </summary>
    /// <remarks>
    /// Набором владеет хост, поэтому он и живёт здесь, а не в редакторе. Три линии
    /// поставлены по краям колонок экранов — к ним удобно подтягивать элементы,
    /// не выцеливая координату мышью.
    ///
    /// Показ по умолчанию выключен (<c>ShowGuides="False"</c> в разметке): набор
    /// остаётся, но пустой холст встречает пользователя без лишних линий. Включается
    /// переключателем <c>guides</c> в шапке.
    /// </remarks>
    public ObservableCollection<ArxisStudio.Surface.Editing.SurfaceGuide> Guides { get; } = new()
    {
        ArxisStudio.Surface.Editing.SurfaceGuide.Vertical(560),
        ArxisStudio.Surface.Editing.SurfaceGuide.Vertical(1180),
        ArxisStudio.Surface.Editing.SurfaceGuide.Horizontal(420),
    };

    // Коллекция выделенных элементов (Avalonia биндит сюда object)
    [ObservableProperty]
    private ObservableCollection<object> _selectedElements = new();

    // НОВОЕ: Активный элемент (первый из выделенных) для отображения в панели свойств
    // Вычисляемое свойство, которое обновляется при изменении SelectedElements
    public SurfaceItemViewModel? ActiveItem => SelectedElements.FirstOrDefault() as SurfaceItemViewModel;
    public bool HasSelection => SelectedElements.Count > 0;

    [ObservableProperty]
    private double _zoom = 1.0;

    [RelayCommand]
    public void ResetZoom()
    {
        Zoom = 1.0;
    }

    /// <summary>
    /// Шаги сетки, доступные в верхней панели.
    /// </summary>
    /// <remarks>
    /// Шаг 1 — это привязка к целым единицам, то есть практически свободное
    /// размещение. Сама сетка при этом на обычном масштабе не рисуется: у неё есть
    /// порог детализации (<c>Surface.Grid.MinCellSize</c>), и клетка мельче него
    /// превратилась бы в сплошную заливку. С приближением она возвращается крупными
    /// линиями: на 500 % видно шаг в пять единиц. Притяжение при этом работает
    /// независимо от показа — как и при выключенном <c>ShowGrid</c>.
    /// </remarks>
    public IReadOnlyList<double> GridCellSizes { get; } = new double[] { 1, 5, 10, 20, 25, 40, 50 };

    /// <summary>
    /// Шаг сетки, действующий при запуске.
    /// </summary>
    /// <remarks>
    /// Пять единиц — это шаг, к которому притягивает, а не тот, который видно: на обычном
    /// масштабе клетка мельче порога детализации и не рисуется, зато размещение перестаёт
    /// прыгать двадцатками. Крупные линии возвращаются с приближением.
    /// </remarks>
    [ObservableProperty]
    private double _gridCellSize = 5;

    public MainWindowViewModel()
    {
        // Заполняем демо-данными
        // Экраны и формы будущего приложения: каждый лежит в своём контейнере,
        // а корень его разметки — обычная панель Avalonia, поэтому редактор сам
        // определяет, что с ним можно делать.
        Elements.Add(new HomeScreenViewModel(80, 80));
        Elements.Add(new ThermostatScreenViewModel(820, 80));
        Elements.Add(new AddDeviceDialogViewModel(1180, 80));
        Elements.Add(new AutomationScreenViewModel(820, 580));
        Elements.Add(new ComponentLibraryViewModel(80, 580));

        SelectedElements.CollectionChanged += OnSelectedElementsCollectionChanged;
    }

    // Подписка переезжает вместе с коллекцией: раньше она ставилась один раз
    // в конструкторе, и замена SelectedElements через сеттер тихо оставляла
    // ActiveItem/HasSelection без обновлений.
    partial void OnSelectedElementsChanged(
        ObservableCollection<object>? oldValue,
        ObservableCollection<object> newValue)
    {
        if (oldValue != null)
            oldValue.CollectionChanged -= OnSelectedElementsCollectionChanged;

        newValue.CollectionChanged += OnSelectedElementsCollectionChanged;
        OnSelectedElementsCollectionChanged(newValue, null!);
    }

    private void OnSelectedElementsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs? e)
    {
        OnPropertyChanged(nameof(ActiveItem));
        OnPropertyChanged(nameof(HasSelection));
    }
}
