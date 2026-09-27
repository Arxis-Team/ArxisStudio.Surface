using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AvaDevTools;

namespace Nodes.Demo;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

#if DEBUG
        // На приложении, а не на окне — как в демо дизайнера интерфейса: F12 из любого окна открывает
        // один и тот же DevTools.
        this.AttachAvaDevTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow { DataContext = GraphDocument.CreateSample() };

        base.OnFrameworkInitializationCompleted();
    }
}
