using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Nodes.StateMachine.Machine;

namespace Nodes.StateMachine.Preview;

/// <summary>
/// Приложение, которым управляет машина: показывает экран активного состояния.
/// </summary>
/// <remarks>
/// Экраны — обычные контролы Avalonia. Их кнопки не меняют экран сами, а поднимают событие машины
/// (<see cref="MachineRunner.Fire"/>): куда перейти, решает граф на холсте, и тот же щелчок «Войти»
/// ведёт туда, куда его направил последний провод. То, что вводит человек, — логин, пароль, открытая
/// строка — пишется в <see cref="MachineContext"/>, и условия переходов читают его на каждом такте.
/// </remarks>
public sealed class AppPreview : ContentControl
{
    private readonly MachineRunner _runner;

    public AppPreview(MachineRunner runner)
    {
        _runner = runner;
        _runner.PropertyChanged += OnRunnerChanged;
        Show();
    }

    private void OnRunnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MachineRunner.Screen) or nameof(MachineRunner.IsRunning))
            Show();
    }

    private void Show() => Content = _runner.Screen is { } screen ? Build(screen) : Idle();

    private Control Idle() => Screen(
        "Машина стоит",
        Muted("«▶ Запустить» — и приложение пойдёт по графу: здесь будет экран активного состояния, а его кнопки станут событиями переходов."));

    private Control Build(AppScreen screen) => screen switch
    {
        AppScreen.Login => Screen(
            "Вход в систему",
            Field("Логин", nameof(MachineContext.Login), password: false),
            Field("Пароль", nameof(MachineContext.Password), password: true),
            Muted($"Верный пароль — «{MachineContext.ValidPassword}»."),
            Action("Войти", "войти", accent: true)),

        AppScreen.Checking => Screen(
            "Проверка",
            new ProgressBar { IsIndeterminate = true },
            Muted("Проверяем учётные данные…")),

        AppScreen.LoginError => Screen(
            "Ошибка входа",
            Muted("Неверный логин или пароль."),
            Action("Повторить", "повторить", accent: true)),

        AppScreen.Home => Screen(
            "Главная",
            List(),
            Row(Action("Открыть", "открыть", accent: true), Action("Настройки", "настройки"), Action("Выйти", "выйти"))),

        AppScreen.Details => Screen(
            "Карточка",
            Bound(nameof(MachineContext.SelectedItem), size: 18),
            Muted("Подробности выбранной строки."),
            Action("Назад", "назад")),

        AppScreen.Settings => Screen(
            "Настройки",
            ThemeSwitch(),
            Action("Назад", "назад")),

        AppScreen.Offline => Screen(
            "Нет сети",
            Muted("Соединение потеряно. Включите «Сеть» на панели запуска — машина вернётся ко входу.")),

        _ => Idle()
    };

    private static Control Screen(string title, params Control[] parts)
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.SemiBold });
        panel.Children.AddRange(parts);
        return new Border { Padding = new Thickness(16), Child = panel };
    }

    private static TextBlock Muted(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 };

    private Control Field(string label, string property, bool password)
    {
        var box = new TextBox { PasswordChar = password ? '•' : default, PlaceholderText = label };
        box.Bind(TextBox.TextProperty, new Binding(property) { Source = _runner.Context, Mode = BindingMode.TwoWay });
        return new StackPanel { Spacing = 4, Children = { new TextBlock { Text = label, Opacity = 0.7 }, box } };
    }

    private TextBlock Bound(string property, double size)
    {
        var text = new TextBlock { FontSize = size };
        text.Bind(TextBlock.TextProperty, new Binding(property) { Source = _runner.Context });
        return text;
    }

    private Button Action(string text, string eventName, bool accent = false)
    {
        var button = new Button { Content = text };
        if (accent)
            button.Classes.Add("accent");

        button.Click += (_, _) => _runner.Fire(eventName);
        return button;
    }

    private static StackPanel Row(params Control[] parts)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.AddRange(parts);
        return row;
    }

    /// <summary>
    /// Строки главной: выбор пишет в контекст, «Открыть» берёт выбранную.
    /// </summary>
    private ListBox List()
    {
        var list = new ListBox
        {
            ItemsSource = new[] { "Заказ № 1042", "Заказ № 1043", "Заказ № 1044" },
            SelectedIndex = 0,
            MaxHeight = 160
        };
        _runner.Context.SelectedItem = "Заказ № 1042";
        list.SelectionChanged += (_, _) => _runner.Context.SelectedItem = list.SelectedItem as string ?? string.Empty;
        return list;
    }

    /// <summary>
    /// Настройка, которая правда что-то меняет: тема всего окна.
    /// </summary>
    private static ToggleSwitch ThemeSwitch()
    {
        var app = Application.Current!;
        var toggle = new ToggleSwitch
        {
            Content = "Тёмная тема",
            IsChecked = app.ActualThemeVariant == ThemeVariant.Dark
        };
        toggle.IsCheckedChanged += (_, _) =>
            app.RequestedThemeVariant = toggle.IsChecked == true ? ThemeVariant.Dark : ThemeVariant.Light;
        return toggle;
    }
}
