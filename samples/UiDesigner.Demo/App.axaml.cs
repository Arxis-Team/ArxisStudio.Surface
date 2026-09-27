using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AvaDevTools;
using UiDesigner.Demo.ViewModels;
using UiDesigner.Demo.Views;

namespace UiDesigner.Demo;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

#if DEBUG
        // Подключение на приложении, а не на окне: тогда дерево инструментов
        // укоренено в Application, каждое окно демо — его узел, и F12 из любого
        // открывает один и тот же DevTools. У окна вызов инспектировал бы только его —
        // сейчас окно одно, и разница проявилась бы ровно в тот день, когда
        // появится второе.
        this.AttachAvaDevTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
