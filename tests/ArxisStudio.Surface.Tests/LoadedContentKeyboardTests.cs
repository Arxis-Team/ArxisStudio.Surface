using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Xunit;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Tests;

/// <summary>
/// Загруженная форма не берёт клавиатуру: Tab обходит её, а фокус, пришедший к её контролу программно,
/// отменяется (ADR 0027). Прежде Tab гулял по полям макета, а набранный текст шёл в них.
/// </summary>
public class LoadedContentKeyboardTests
{
    private const string Avalonia = "https://github.com/avaloniaui";
    private const string Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private const string Fields =
        "<StackPanel><TextBox x:Name=\"Field\" /><Button x:Name=\"Go\" Content=\"Go\" /></StackPanel>";

    private static UserControl View() =>
        (UserControl)AvaloniaRuntimeXamlLoader.Parse($"<UserControl xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\">{Fields}</UserControl>");

    private static Window Form() =>
        (Window)AvaloniaRuntimeXamlLoader.Parse($"<Window xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\">{Fields}</Window>");

    /// <summary>Кнопка до формы, форма, кнопка после — так Tab либо проходит форму насквозь, либо застревает в ней.</summary>
    private static (Window Host, Button Before, Button After) Between(Control item)
    {
        var before = new Button { Content = "Before" };
        var after = new Button { Content = "After" };
        var host = new Window { Width = 900, Height = 600, Content = new StackPanel { Children = { before, item, after } } };

        host.Show();
        host.UpdateLayout();

        return (host, before, after);
    }

    private static T Find<T>(Control root, string name)
        where T : Control =>
        Assert.IsType<T>(root.FindControl<Control>(name));

    [AvaloniaFact]
    public void Tab_Walks_Past_A_Loaded_User_Control_Form()
    {
        var view = View();
        var item = new UiDesignerFormItem { Root = view };
        var (host, before, after) = Between(item);

        before.Focus();

        // Сам контейнер — остановка: через него работает холст. Следующая — уже за формой.
        host.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);

        Assert.True(item.IsFocused, "Первая остановка после кнопки — контейнер формы.");

        host.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);

        Assert.True(after.IsFocused, "Tab ушёл в форму, а не к следующей кнопке.");
        Assert.False(Find<TextBox>(view, "Field").IsFocused);
    }

    [AvaloniaFact]
    public void Tab_Walks_Past_A_Loaded_Window_Form()
    {
        var window = Form();
        var item = new UiDesignerFormItem { Root = window };
        var (host, before, after) = Between(item);

        before.Focus();
        host.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        host.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);

        Assert.True(after.IsFocused, "Tab ушёл в окно-форму, а не к следующей кнопке.");
        Assert.False(Find<TextBox>(window, "Field").IsFocused);
    }

    [AvaloniaFact]
    public void A_Control_Of_The_Form_Asking_For_Focus_Does_Not_Get_It()
    {
        var view = View();
        var item = new UiDesignerFormItem { Root = view };
        var (host, before, _) = Between(item);

        before.Focus();

        var field = Find<TextBox>(view, "Field");

        field.Focus();

        Assert.False(field.IsFocused);
        Assert.True(before.IsFocused, "Отменённый фокус уходит туда, где был, а не в никуда.");

        // И набранный текст в макет не идёт.
        host.KeyTextInput("typed");

        Assert.True(string.IsNullOrEmpty(field.Text));
    }

    [AvaloniaFact]
    public void Content_A_Host_Annotated_Keeps_Its_Keyboard()
    {
        // Размеченный автором шаблон — часть приложения хоста, а не макет: его поле фокус берёт.
        var field = new TextBox();
        var item = new UiDesignerItem { Content = field };

        Between(item);

        Assert.True(field.Focus());
        Assert.True(field.IsFocused);
    }
}
