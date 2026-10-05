using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Tests;

/// <summary>
/// Форму одевает приложение документа, а не инструмента: его стили, ресурсы и текст, который его тема
/// даёт окну (ADR 0020, дополнение).
/// </summary>
/// <remarks>
/// Приложение тестов носит Fluent, и окно-хост получает от него свой кегль. Тема окна у приложения
/// документа здесь даёт другой — по нему и видно, чьё досталось форме.
/// </remarks>
public class UiDesignerFormApplicationTests
{
    private const string Avalonia = "https://github.com/avaloniaui";
    private const string Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static Application App(string styles = "", string resources = "") =>
        (Application)AvaloniaRuntimeXamlLoader.Parse(
            $"<Application xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\">" +
            $"<Application.Styles>{styles}</Application.Styles>" +
            $"<Application.Resources>{resources}</Application.Resources>" +
            "</Application>");

    private static UserControl View(string content = "<TextBlock x:Name=\"Text\" Text=\"hello\" />") =>
        (UserControl)AvaloniaRuntimeXamlLoader.Parse($"<UserControl xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\">{content}</UserControl>");

    private static Window Form(string attributes = "", string content = "<TextBlock x:Name=\"Text\" Text=\"hello\" />") =>
        (Window)AvaloniaRuntimeXamlLoader.Parse($"<Window xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\" {attributes}>{content}</Window>");

    private const string BigText =
        "<Style Selector=\"TextBlock\"><Setter Property=\"FontSize\" Value=\"42\" /></Style>";

    private const string Accent = "<SolidColorBrush x:Key=\"Accent\">#FF0000</SolidColorBrush>";

    /// <summary>Тема окна, какой её объявляет приложение: кегль числом, цвет — ресурсом.</summary>
    private const string WindowTheme =
        "<SolidColorBrush x:Key=\"Ink\">#00FF00</SolidColorBrush>" +
        "<ControlTheme x:Key=\"{x:Type Window}\" TargetType=\"Window\">" +
        "<Setter Property=\"FontSize\" Value=\"21\" />" +
        "<Setter Property=\"Foreground\" Value=\"{DynamicResource Ink}\" />" +
        "</ControlTheme>";

    /// <summary>Тема окна, которая красит фон: светлый и тёмный — разными цветами.</summary>
    private const string WindowBackdrop =
        "<ResourceDictionary>" +
        "<ResourceDictionary.ThemeDictionaries>" +
        "<ResourceDictionary x:Key=\"Light\"><SolidColorBrush x:Key=\"Paper\">#0000FF</SolidColorBrush></ResourceDictionary>" +
        "<ResourceDictionary x:Key=\"Dark\"><SolidColorBrush x:Key=\"Paper\">#000080</SolidColorBrush></ResourceDictionary>" +
        "</ResourceDictionary.ThemeDictionaries>" +
        "<ControlTheme x:Key=\"{x:Type Window}\" TargetType=\"Window\">" +
        "<Setter Property=\"Background\" Value=\"{DynamicResource Paper}\" />" +
        "</ControlTheme>" +
        "</ResourceDictionary>";

    private static Window Host(UiDesignerFormItem item)
    {
        var host = new Window { Content = item, Width = 900, Height = 600 };
        host.Show();
        host.UpdateLayout();
        return host;
    }

    private static TextBlock Text(Control root) => Assert.IsType<TextBlock>(root.FindControl<Control>("Text"));

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    [AvaloniaFact]
    public void The_Application_Dresses_The_Form_With_Its_Styles_And_Resources()
    {
        var application = App(BigText, Accent);
        var view = View("<TextBlock x:Name=\"Text\" Text=\"hello\" Foreground=\"{DynamicResource Accent}\" />");
        var item = new UiDesignerFormItem { Root = view, ApplicationRoot = application };

        Host(item);

        Assert.Equal(42, Text(view).FontSize);
        Assert.Equal(Colors.Red, ColorOf(Text(view).Foreground));

        // Стили взяты, а не скопированы: у стиля один владелец. Словарь остался приложению — ссылки в
        // его разметке ищут ключ от него самого, — и форма спрашивает его ресурсы через приложение.
        Assert.Empty(application.Styles);
        Assert.True(application.Resources.ContainsKey("Accent"));
    }

    [AvaloniaFact]
    public void Letting_The_Application_Go_Gives_It_Back_What_Was_Taken()
    {
        var application = App(BigText, Accent);
        var view = View();
        var item = new UiDesignerFormItem { Root = view, ApplicationRoot = application };

        Host(item);

        item.ApplicationRoot = null;
        item.UpdateLayout();

        Assert.Single(application.Styles);
        Assert.True(application.Resources.ContainsKey("Accent"));
        Assert.NotEqual(42, Text(view).FontSize);
    }

    /// <summary>
    /// Тема типа, которую даёт приложение документа, достаётся и форме, вставшей раньше приложения.
    /// </summary>
    /// <remarks>
    /// Хост показывает корень, как только документ построен, а приложение строит после — оно грузится
    /// своей очередью. Контрол формы ищет тему своего типа, входя в дерево, и находит тему инструмента
    /// или ничего; стиль, пришедший потом, переприменяется сам, а тема типа — нет. В студии поле ввода
    /// формы так и стояло без шаблона, нулевой высоты, хотя Fluent приложения уже лежал на области.
    /// </remarks>
    [AvaloniaFact]
    public void A_Theme_The_Application_Gives_A_Type_Reaches_A_Form_Shown_Before_It()
    {
        const string ButtonTheme =
            "<Styles><Styles.Resources>" +
            "<ControlTheme x:Key=\"{x:Type Button}\" TargetType=\"Button\">" +
            "<Setter Property=\"Tag\" Value=\"document\" />" +
            "</ControlTheme>" +
            "</Styles.Resources></Styles>";

        var view = View("<Button x:Name=\"Go\" Content=\"go\" />");
        var item = new UiDesignerFormItem { Root = view };

        Host(item);

        item.ApplicationRoot = App(ButtonTheme);
        item.UpdateLayout();

        Assert.Equal("document", Assert.IsType<Button>(view.FindControl<Control>("Go")).Tag);
    }

    [AvaloniaFact]
    public void Another_Application_Replaces_The_First_And_The_First_Gets_Its_Own_Back()
    {
        var first = App(BigText);
        var second = App("<Style Selector=\"TextBlock\"><Setter Property=\"FontSize\" Value=\"30\" /></Style>");
        var view = View();
        var item = new UiDesignerFormItem { Root = view, ApplicationRoot = first };

        Host(item);

        item.ApplicationRoot = second;
        item.UpdateLayout();

        Assert.Single(first.Styles);
        Assert.Empty(second.Styles);
        Assert.Equal(30, Text(view).FontSize);
    }

    /// <summary>
    /// Форма-контрол стоит на фоне, которым тема приложения одевает окно: при работе она в окне и будет.
    /// Без этого контрол без своего фона показывал холст насквозь, и тёмный текст светлой темы терялся на
    /// тёмном холсте.
    /// </summary>
    [AvaloniaFact]
    public void A_Control_Form_Stands_On_The_Background_The_Application_Gives_A_Window()
    {
        var item = new UiDesignerFormItem
        {
            Root = View(),
            ApplicationThemeVariant = ThemeVariant.Light,
            ApplicationRoot = App(resources: WindowBackdrop),
        };

        Host(item);

        Assert.Equal(Color.Parse("#0000FF"), ColorOf(item.FormBackground));

        item.ApplicationThemeVariant = ThemeVariant.Dark;

        Assert.Equal(Color.Parse("#000080"), ColorOf(item.FormBackground));

        item.ApplicationRoot = null;

        Assert.Null(item.FormBackground);
    }

    /// <summary>
    /// Окно о своём фоне говорит само: объявленный документом фон тема окна приложения не подменяет.
    /// </summary>
    [AvaloniaFact]
    public void A_Window_Keeps_The_Background_It_Declares_Under_An_Application()
    {
        var item = new UiDesignerFormItem
        {
            Root = Form("Background=\"#FF0000\""),
            ApplicationThemeVariant = ThemeVariant.Light,
            ApplicationRoot = App(resources: WindowBackdrop),
        };

        Host(item);

        Assert.Equal(Color.Parse("#FF0000"), ColorOf(item.FormBackground));
    }

    /// <summary>Приложение без темы окна фона форме-контролу не даёт: она остаётся прозрачной.</summary>
    [AvaloniaFact]
    public void A_Control_Form_Of_An_Application_Without_A_Window_Theme_Has_No_Background()
    {
        var item = new UiDesignerFormItem { Root = View(), ApplicationRoot = App() };

        Host(item);

        Assert.Null(item.FormBackground);
    }

    [AvaloniaFact]
    public void The_Text_Of_A_Form_Comes_From_The_Theme_The_Application_Gives_A_Window()
    {
        var view = View();
        var item = new UiDesignerFormItem { Root = view, ApplicationRoot = App(resources: WindowTheme) };

        Host(item);

        // Не 14 от Fluent приложения тестов: при работе содержимое наследует текст от окна программы.
        Assert.Equal(21, Text(view).FontSize);
        Assert.Equal(Colors.Lime, ColorOf(Text(view).Foreground));
    }

    [AvaloniaFact]
    public void Without_An_Application_The_Text_Is_The_Tools_As_Before()
    {
        var view = View();
        var item = new UiDesignerFormItem { Root = view };

        Host(item);

        var reference = new TextBlock();
        var host = new Window { Content = reference };

        host.Show();

        Assert.Equal(reference.FontSize, Text(view).FontSize);
    }

    [AvaloniaFact]
    public void A_Window_Declaring_Its_Font_Gives_It_To_Its_Content_Over_The_Applications()
    {
        var window = Form("FontSize=\"17\"");
        var item = new UiDesignerFormItem { Root = window, ApplicationRoot = App(resources: WindowTheme) };

        Host(item);

        Assert.Equal(17, Text(window).FontSize);

        // Цвет окно не объявило, и он — приложения.
        Assert.Equal(Colors.Lime, ColorOf(Text(window).Foreground));
    }

    [AvaloniaFact]
    public void A_Window_Declaring_No_Font_Leaves_Its_Content_The_Applications()
    {
        var window = Form();
        var item = new UiDesignerFormItem { Root = window, ApplicationRoot = App(resources: WindowTheme) };

        Host(item);

        Assert.Equal(21, Text(window).FontSize);
    }

    [AvaloniaFact]
    public void A_Font_The_Window_Declares_Later_Reaches_Its_Content()
    {
        var window = Form();
        var item = new UiDesignerFormItem { Root = window };

        Host(item);

        window.FontSize = 19;
        item.UpdateLayout();

        Assert.Equal(19, Text(window).FontSize);

        // Снятое объявление снимает и отражение.
        window.ClearValue(TemplatedControl.FontSizeProperty);
        item.UpdateLayout();

        Assert.NotEqual(19, Text(window).FontSize);
    }
}
