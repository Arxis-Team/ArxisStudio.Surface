using Avalonia;
using Avalonia.Markup.Xaml;

namespace ArxisStudio.Tests;

/// <summary>
/// Приложение для headless-тестов: подключает Fluent и тему библиотеки,
/// чтобы у контролов редактора применялись реальные ControlTheme и работал
/// <c>PART_ItemsPresenter</c>.
/// </summary>
public class TestApp : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
}
