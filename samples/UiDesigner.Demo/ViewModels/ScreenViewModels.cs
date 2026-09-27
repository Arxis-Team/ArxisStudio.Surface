namespace UiDesigner.Demo.ViewModels;

/// <summary>
/// Главный экран приложения: плитки комнат, сцены и потребление.
/// </summary>
public class HomeScreenViewModel : SurfaceItemViewModel
{
    public HomeScreenViewModel(double x, double y) : base(x, y)
    {
        Title = "Дом";
        Width = 700;
        Height = 460;
    }
}

/// <summary>
/// Экран отдельного устройства.
/// </summary>
public class ThermostatScreenViewModel : SurfaceItemViewModel
{
    public ThermostatScreenViewModel(double x, double y) : base(x, y)
    {
        Title = "Термостат";
        Width = 320;
        Height = 460;
    }
}

/// <summary>
/// Диалог добавления устройства.
/// </summary>
public class AddDeviceDialogViewModel : SurfaceItemViewModel
{
    public AddDeviceDialogViewModel(double x, double y) : base(x, y)
    {
        Title = "Добавление устройства";
        Width = 380;
        Height = 320;
    }
}

/// <summary>
/// Редактор сценария автоматизации.
/// </summary>
public class AutomationScreenViewModel : SurfaceItemViewModel
{
    public AutomationScreenViewModel(double x, double y) : base(x, y)
    {
        Title = "Сценарий";
        Width = 420;
        Height = 320;
    }
}

/// <summary>
/// Библиотека контролов, из которых собраны экраны.
/// </summary>
public class ComponentLibraryViewModel : SurfaceItemViewModel
{
    public ComponentLibraryViewModel(double x, double y) : base(x, y)
    {
        Title = "Библиотека компонентов";
        Width = 700;
        Height = 220;
    }
}
