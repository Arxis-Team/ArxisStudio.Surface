using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Xunit;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Tests;

/// <summary>
/// Элемент формы держит корень документа, который нельзя показать (ADR 0020).
/// </summary>
/// <remarks>
/// Два теста — о посылке, а не о коде: окно действительно нельзя вложить, и содержимое, вынутое из
/// окна, действительно теряет его ресурсы. Оба найдены опытом при постройке дизайнера; утверждать их
/// дёшево, а открывать заново дорого. Набор перенесён из Markup (<c>XamlDesignSurfaceTests</c>) вместе
/// с механикой; корни строит загрузчик XAML Avalonia — элементу всё равно, откуда корень.
/// </remarks>
public class UiDesignerFormItemTests
{
    private const string Avalonia = "https://github.com/avaloniaui";
    private const string Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private const string AccentResource =
        "<Window.Resources><SolidColorBrush x:Key=\"Accent\">#FF0000</SolidColorBrush></Window.Resources>";

    private const string TextStyle =
        "<Window.Styles><Style Selector=\"TextBlock\"><Setter Property=\"FontSize\" Value=\"42\" /></Style></Window.Styles>";

    private static Window Form(
        string attributes = "",
        string declarations = "",
        string content = "<TextBlock x:Name=\"Text\" Text=\"hello\" />") =>
        Load<Window>($"<Window xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\" {attributes}>{declarations}{content}</Window>");

    private static UserControl View(string content = "<TextBlock x:Name=\"Text\" Text=\"hello\" />", string attributes = "") =>
        Load<UserControl>($"<UserControl xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\" {attributes}>{content}</UserControl>");

    private static T Load<T>(string xaml) => (T)AvaloniaRuntimeXamlLoader.Parse(xaml);

    /// <summary>
    /// Ставит элемент в живое дерево: стилям, ресурсам и шаблону нужно где работать.
    /// </summary>
    /// <remarks>
    /// Приложение тестов тёмное, поэтому хост по умолчанию — тёмный инструмент: то, чем бывает всякий
    /// настоящий дизайнер.
    /// </remarks>
    private static Window Host(UiDesignerFormItem item)
    {
        var host = new Window { Content = item, Width = 900, Height = 600 };
        host.Show();
        host.UpdateLayout();
        return host;
    }

    private static T Find<T>(Control root, string name)
        where T : Control =>
        Assert.IsType<T>(root.FindControl<Control>(name));

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    // -- Посылка --------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void A_Window_Cannot_Be_Hosted_By_Anything_Which_Is_Why_This_Item_Exists()
    {
        var window = Form();
        var border = new Border();

        Assert.ThrowsAny<InvalidOperationException>(() =>
        {
            border.Child = window;
            border.Measure(Size.Infinity);
        });
    }

    [AvaloniaFact]
    public void Content_Taken_Out_Of_A_Window_Loses_Its_Resources_Without_The_Item()
    {
        var window = Form(declarations: AccentResource);
        var text = Assert.IsType<TextBlock>(window.Content);
        Assert.True(text.TryFindResource("Accent", out _));

        window.Content = null;

        Assert.False(text.TryFindResource("Accent", out _));
    }

    // -- Показ ----------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void The_Item_Hosts_The_Content_Of_A_Window()
    {
        var window = Form();
        var item = new UiDesignerFormItem { Root = window };

        Assert.True(item.IsTopLevel);
        Assert.True(item.HasContent);
        Assert.Same(window, item.Root);

        Host(item);

        Assert.True(Find<TextBlock>(window, "Text").Bounds.Width > 0, "Форма обязана быть на экране.");
    }

    /// <summary>
    /// Шаблонный контрол внутри формы построен — это больше, чем «форма присутствует».
    /// </summary>
    /// <remarks>
    /// <see cref="TextBlock"/> шаблона не требует и рисуется из голого дерева, поэтому тесты показа
    /// проходят и у элемента, не донёсшего до формы ни одного стиля. Кнопка — нет: без темы контрола нет
    /// шаблона, на экране пусто, и исключения тоже нет — форма просто загружена и невидима.
    /// </remarks>
    [AvaloniaFact]
    public void The_Form_Stays_Where_Control_Themes_Reach_It()
    {
        var window = Form(content: "<Button x:Name=\"Press\" Content=\"press\" />");
        var item = new UiDesignerFormItem { Root = window };

        Host(item);

        var button = Find<Button>(window, "Press");
        Assert.NotNull(button.Template);
        Assert.NotEmpty(button.GetVisualChildren());
        Assert.True(button.Bounds.Width > 0);
    }

    [AvaloniaFact]
    public void A_Control_Root_Stays_Where_Control_Themes_Reach_It()
    {
        // UserControl сам шаблонный: корню надо найти свою тему раньше, чем появится презентер для кнопки.
        var root = View(
            "<StackPanel x:Name=\"Stack\"><Button x:Name=\"Press\" Content=\"press\" /></StackPanel>",
            "Width=\"400\" Height=\"200\"");
        var item = new UiDesignerFormItem { Root = root };

        Host(item);

        var button = Find<Button>(root, "Press");
        Assert.NotEmpty(root.GetVisualChildren());
        Assert.True(Find<StackPanel>(root, "Stack").Bounds.Height > 0);
        Assert.NotNull(button.Template);
        Assert.True(button.Bounds.Width > 0);
    }

    [AvaloniaFact]
    public void A_Control_Root_Is_Hosted_As_It_Stands()
    {
        var root = View();
        var item = new UiDesignerFormItem { Root = root };

        Assert.False(item.IsTopLevel);
        Assert.True(item.HasContent);
        Assert.Same(root, item.Root);

        Host(item);

        // Корень стоит в дереве под элементом сам, без заимствований.
        Assert.Same(item, root.FindAncestorOfType<UiDesignerFormItem>());
        Assert.NotNull(root.Content);
    }

    [AvaloniaFact]
    public void A_Root_That_Is_Not_A_Control_Reports_No_Content()
    {
        // Документ не обязан давать контрол: словарь ресурсов даёт словарь, App.axaml — приложение.
        var dictionary = Load<ResourceDictionary>($"<ResourceDictionary xmlns=\"{Avalonia}\" xmlns:x=\"{Xaml}\" />");
        var item = new UiDesignerFormItem { Root = dictionary };

        Assert.False(item.HasContent);
        Assert.False(item.IsTopLevel);
        Assert.Same(dictionary, item.Root);
        Assert.Contains(":empty", item.Classes);
    }

    [AvaloniaFact]
    public void A_Window_Without_Content_Reports_None()
    {
        var item = new UiDesignerFormItem { Root = Load<Window>($"<Window xmlns=\"{Avalonia}\" />") };

        Assert.True(item.IsTopLevel);
        Assert.False(item.HasContent);
        Assert.Contains(":empty", item.Classes);
    }

    [AvaloniaFact]
    public void Content_That_Is_Not_A_Control_Is_Presented_As_The_Window_Would()
    {
        var window = Load<Window>($"<Window xmlns=\"{Avalonia}\">plain text content</Window>");
        Assert.IsType<string>(window.Content);

        var item = new UiDesignerFormItem { Root = window };
        Assert.True(item.HasContent);

        Host(item);

        Assert.Contains(item.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "plain text content");

        item.Root = null;

        Assert.Same("plain text content", window.Content);
    }

    // -- Вид по корню ---------------------------------------------------------------------------------

    [AvaloniaFact]
    public void The_Kind_Of_The_Root_Is_A_Pseudo_Class_Not_A_Type()
    {
        // Корневой тег при правке меняется, а тип контейнера сменить нельзя, не потеряв место и выбор.
        var item = new UiDesignerFormItem();
        Assert.Contains(":empty", item.Classes);

        item.Root = Form();
        Assert.Contains(":window", item.Classes);
        Assert.DoesNotContain(":control", item.Classes);
        Assert.DoesNotContain(":empty", item.Classes);

        item.Root = View();
        Assert.Contains(":control", item.Classes);
        Assert.DoesNotContain(":window", item.Classes);

        item.Root = null;
        Assert.Contains(":empty", item.Classes);
        Assert.DoesNotContain(":control", item.Classes);
    }

    [AvaloniaFact]
    public void A_Form_Is_Loaded_Content_By_Default()
    {
        Assert.Equal(SurfaceContentMode.Loaded, new UiDesignerFormItem().ContentMode);
        Assert.Equal(SurfaceContentMode.Annotated, new UiDesignerItem().ContentMode);
    }

    // -- Что содержимое иначе потеряло бы ---------------------------------------------------------------

    [AvaloniaFact]
    public void The_Roots_Resources_Still_Reach_The_Content()
    {
        var window = Form(declarations: AccentResource);
        var item = new UiDesignerFormItem { Root = window };

        Host(item);

        Assert.True(Find<TextBlock>(window, "Text").TryFindResource("Accent", out _));
    }

    [AvaloniaFact]
    public void A_Style_Declared_On_The_Root_Still_Applies()
    {
        var window = Form(declarations: TextStyle);
        var item = new UiDesignerFormItem { Root = window };

        Host(item);

        Assert.Equal(42d, Find<TextBlock>(window, "Text").FontSize);
    }

    [AvaloniaFact]
    public void A_Style_Declared_On_The_Root_Does_Not_Reach_The_Items_Own_Parts()
    {
        // Причина, по которой стили корня ложатся на внутреннюю область, а не на сам элемент: селектор
        // Border из стилей окна попал бы в рамку элемента — а позже и в кнопки его заголовка.
        var window = Form(
            declarations: "<Window.Styles><Style Selector=\"Border\"><Setter Property=\"Tag\" Value=\"styled\" /></Style></Window.Styles>",
            content: "<Border x:Name=\"Inside\" />");
        var item = new UiDesignerFormItem { Root = window };

        Host(item);

        Assert.Equal("styled", Find<Border>(window, "Inside").Tag);
        var own = item.GetVisualDescendants().OfType<Border>().Where(border => border.TemplatedParent == item).ToList();
        Assert.NotEmpty(own);
        Assert.All(own, border => Assert.Null(border.Tag));
    }

    [AvaloniaFact]
    public void Resources_Move_And_Setting_No_Root_Gives_Them_Back()
    {
        var window = Form(declarations: AccentResource);
        var text = Assert.IsType<TextBlock>(window.Content);
        var item = new UiDesignerFormItem { Root = window };

        // Перенесены, а не общие: у словаря Avalonia один владелец. Пока корень держат, у него пустой
        // словарь, а документ говорит прежнее — его и читают правки.
        Assert.True(text.TryFindResource("Accent", out _));
        Assert.False(window.TryFindResource("Accent", out _));

        item.Root = null;

        Assert.True(window.TryFindResource("Accent", out _));
    }

    /// <summary>
    /// Причина, по которой ресурсы переносят, а не копируют.
    /// </summary>
    /// <remarks>
    /// Копия достаёт только то, что может перечислить, а вложенный словарь — отдельный объект со своим
    /// владельцем. Копирование молча сплющило бы структуру, на которой стоит форма с общей палитрой.
    /// </remarks>
    [AvaloniaFact]
    public void Merged_Dictionaries_Stay_Intact()
    {
        var window = Form(declarations:
            "<Window.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries>"
            + "<ResourceDictionary><SolidColorBrush x:Key=\"Paper\">#000000</SolidColorBrush></ResourceDictionary>"
            + "</ResourceDictionary.MergedDictionaries></ResourceDictionary></Window.Resources>");
        var text = Find<TextBlock>(window, "Text");
        Assert.True(text.TryFindResource("Paper", out _), "Само окно обязано его находить.");

        var item = new UiDesignerFormItem { Root = window };
        Host(item);

        Assert.True(text.TryFindResource("Paper", out var paper), "Элемент обязан его находить.");
        Assert.Equal(Colors.Black, ColorOf(paper as IBrush));
    }

    [AvaloniaFact]
    public void Styles_Move_And_Setting_No_Root_Gives_Them_Back()
    {
        var window = Form(declarations: TextStyle);
        var item = new UiDesignerFormItem { Root = window };

        Assert.Empty(window.Styles);

        item.Root = null;

        Assert.Single(window.Styles);
    }

    [AvaloniaFact]
    public void The_Items_Own_Resources_And_Styles_Are_Left_Alone()
    {
        // Корень ложится на внутреннюю область, поэтому своё у элемента не трогается вовсе.
        var window = Form(declarations: AccentResource + TextStyle);
        var item = new UiDesignerFormItem();
        var own = new Style();
        item.Resources["Frame"] = Brushes.Fuchsia;
        item.Styles.Add(own);

        item.Root = window;

        Assert.Same(own, Assert.Single(item.Styles));
        Assert.True(item.TryFindResource("Frame", out _));
        Assert.True(Find<TextBlock>(window, "Text").TryFindResource("Accent", out _));

        item.Root = null;

        Assert.Same(own, Assert.Single(item.Styles));
        Assert.True(item.TryFindResource("Frame", out _));
        Assert.Single(window.Styles);
    }

    // -- Отражение, а не снимок -----------------------------------------------------------------------

    [AvaloniaFact]
    public void The_Background_Is_Mirrored()
    {
        var item = new UiDesignerFormItem { Root = Form("Background=\"#FF0000\"") };

        Assert.Equal(Colors.Red, ColorOf(item.FormBackground));
    }

    /// <summary>
    /// Фон, которого документ не просил, — не фон формы.
    /// </summary>
    /// <remarks>
    /// Окно всегда приходит с каким-нибудь фоном — тему даёт приложение, под которым работает дизайнер, —
    /// и отразить его напрямую значило бы покрасить каждую нерешившую форму в цвет инструмента. Приоритет
    /// здесь тот, которым пользуется тема.
    /// </remarks>
    [AvaloniaFact]
    public void A_Themed_Default_Background_Is_Not_Shown_But_A_Local_One_Is()
    {
        var window = Form();
        window.SetValue(TemplatedControl.BackgroundProperty, Brushes.Black, BindingPriority.Style);

        var item = new UiDesignerFormItem { Root = window };
        Assert.Null(item.FormBackground);

        window.SetValue(TemplatedControl.BackgroundProperty, Brushes.Lime);

        Assert.Equal(Colors.Lime, ColorOf(item.FormBackground));
    }

    [AvaloniaFact]
    public void An_Edit_To_The_Root_Shows_Without_Setting_It_Again()
    {
        var window = Form("Background=\"#FF0000\" Title=\"Orders\"");
        var item = new UiDesignerFormItem { Root = window };

        window.Background = Brushes.Lime;
        window.Title = "Invoices";

        Assert.Equal(Colors.Lime, ColorOf(item.FormBackground));
        Assert.Equal("Invoices", item.Title);
    }

    [AvaloniaFact]
    public void The_Declared_Size_Becomes_The_Items_Size()
    {
        // Размер элемента — размер формы: вложенного контейнера со своим размером нет.
        var window = Form("Width=\"800\" Height=\"450\"");
        var item = new UiDesignerFormItem { Root = window };

        Assert.Equal(800d, item.Width);
        Assert.Equal(450d, item.Height);

        window.Width = 640;

        Assert.Equal(640d, item.Width);
    }

    [AvaloniaFact]
    public void A_Control_Roots_Declared_Size_Becomes_The_Items_Size_Too()
    {
        var item = new UiDesignerFormItem { Root = View(attributes: "Width=\"400\" Height=\"200\"") };

        Assert.Equal(400d, item.Width);
        Assert.Equal(200d, item.Height);
    }

    [AvaloniaFact]
    public void The_Item_Never_Writes_Back_To_The_Root()
    {
        // Писатель у размера один — документ. Два писателя одного значения — это форма, дрожащая на
        // каждом кадре протяжки.
        var window = Form("Width=\"800\" Background=\"#FF0000\"");
        var item = new UiDesignerFormItem { Root = window };

        item.Width = 123;
        item.Background = Brushes.Blue;

        Assert.Equal(800d, window.Width);
        Assert.Equal(Colors.Red, ColorOf(window.Background));
    }

    // -- Тема: два слоя -------------------------------------------------------------------------------

    [AvaloniaFact]
    public void The_Roots_Requested_Theme_Variant_Reaches_The_Content()
    {
        var window = Form("RequestedThemeVariant=\"Light\"");
        var item = new UiDesignerFormItem { Root = window };

        Host(item);

        Assert.Equal(ThemeVariant.Light, Find<TextBlock>(window, "Text").ActualThemeVariant);
    }

    /// <summary>
    /// Документ, не решивший ничего, показывает тему своего приложения, а не инструмента.
    /// </summary>
    [AvaloniaFact]
    public void The_Application_Variant_Reaches_A_Form_That_Declares_None()
    {
        var window = Form();
        var item = new UiDesignerFormItem { ApplicationThemeVariant = ThemeVariant.Light, Root = window };

        Host(item);

        Assert.Equal(ThemeVariant.Light, Find<TextBlock>(window, "Text").ActualThemeVariant);
    }

    [AvaloniaFact]
    public void The_Application_Variant_Reaches_A_Control_Root()
    {
        // У UserControl своего свойства темы нет: без слоя приложения он брал бы тему инструмента всегда.
        var root = View();
        var item = new UiDesignerFormItem { ApplicationThemeVariant = ThemeVariant.Light, Root = root };

        Host(item);

        Assert.Equal(ThemeVariant.Light, Find<TextBlock>(root, "Text").ActualThemeVariant);
    }

    [AvaloniaFact]
    public void The_Documents_Own_Request_Still_Wins_Over_The_Applications()
    {
        var window = Form("RequestedThemeVariant=\"Dark\"");
        var item = new UiDesignerFormItem { ApplicationThemeVariant = ThemeVariant.Light, Root = window };

        Host(item);

        Assert.Equal(ThemeVariant.Dark, Find<TextBlock>(window, "Text").ActualThemeVariant);
    }

    [AvaloniaFact]
    public void The_Application_Variant_Paints_The_Themed_Window_Background()
    {
        // Нерешившее окно при работе не прозрачно: его красит тема приложения. Элемент, знающий
        // приложение, показывает этот фон; не знающий — ничего.
        var item = new UiDesignerFormItem { ApplicationThemeVariant = ThemeVariant.Light, Root = Form() };

        Host(item);

        Assert.NotNull(item.FormBackground);
    }

    [AvaloniaFact]
    public void A_Host_That_Learns_The_Application_Late_Is_Followed()
    {
        var window = Form();
        var item = new UiDesignerFormItem { Root = window };
        Host(item);
        Assert.Null(item.FormBackground);

        item.ApplicationThemeVariant = ThemeVariant.Light;

        Assert.NotNull(item.FormBackground);
        Assert.Equal(ThemeVariant.Light, Find<TextBlock>(window, "Text").ActualThemeVariant);
    }

    [AvaloniaFact]
    public void The_Lent_Variant_Leaves_No_Trace_On_The_Root()
    {
        var window = Form();
        var item = new UiDesignerFormItem { ApplicationThemeVariant = ThemeVariant.Light, Root = window };

        // Пока корень держат, он живёт под темой своего приложения — это и делает его тематические
        // значения значениями приложения, а не инструмента.
        Assert.Equal(ThemeVariant.Light, window.RequestedThemeVariant);

        item.Root = null;

        Assert.Equal(ThemeVariant.Default, window.RequestedThemeVariant);
    }

    [AvaloniaFact]
    public void A_Declared_Variant_Is_Never_Overwritten()
    {
        var window = Form("RequestedThemeVariant=\"Dark\"");
        var item = new UiDesignerFormItem { ApplicationThemeVariant = ThemeVariant.Light, Root = window };

        Assert.Equal(ThemeVariant.Dark, window.RequestedThemeVariant);

        item.Root = null;

        Assert.Equal(ThemeVariant.Dark, window.RequestedThemeVariant);
    }

    [AvaloniaFact]
    public void An_Unset_Application_Variant_Keeps_The_Hosts_Own()
    {
        var window = Form();
        var item = new UiDesignerFormItem { Root = window };

        Host(item);

        Assert.Equal(ThemeVariant.Dark, Find<TextBlock>(window, "Text").ActualThemeVariant);
    }

    // -- Контекст данных ------------------------------------------------------------------------------

    [AvaloniaFact]
    public void The_Roots_Data_Context_Reaches_The_Content()
    {
        var window = Form();
        window.DataContext = "the form's own";

        _ = new UiDesignerFormItem { Root = window };

        Assert.Equal("the form's own", Find<TextBlock>(window, "Text").DataContext);
    }

    /// <summary>
    /// Форме нельзя показывать данные хоста.
    /// </summary>
    /// <remarks>
    /// Контекст данных контейнера — модель хоста из <c>ItemsSource</c>, и он наследуется вниз. Без своего
    /// значения на области формы форма без данных времени разработки молча рисовалась бы по модели
    /// дизайнера, и совпавшие привязки выглядели бы рабочими.
    /// </remarks>
    [AvaloniaFact]
    public void The_Hosts_Data_Context_Does_Not_Reach_The_Form()
    {
        var window = Form();
        var item = new UiDesignerFormItem { Root = window, DataContext = "the designer's" };

        Host(item);

        Assert.Null(Find<TextBlock>(window, "Text").DataContext);
    }

    [AvaloniaFact]
    public void The_Hosts_Data_Context_Does_Not_Reach_A_Control_Root_Either()
    {
        var root = View();
        var item = new UiDesignerFormItem { Root = root, DataContext = "the designer's" };

        Host(item);

        Assert.Null(root.DataContext);
    }

    // -- Рамка — данные -------------------------------------------------------------------------------

    [AvaloniaFact]
    public void The_Windows_Chrome_Is_Published_As_Data()
    {
        var item = new UiDesignerFormItem
        {
            Root = Form("Title=\"Orders\" CanResize=\"False\" WindowDecorations=\"BorderOnly\"")
        };

        Assert.Equal("Orders", item.Title);
        Assert.False(item.CanResize);
        Assert.Equal(WindowDecorations.BorderOnly, item.Decorations);
    }

    [AvaloniaFact]
    public void A_Control_Root_Publishes_No_Chrome()
    {
        var item = new UiDesignerFormItem { Root = View() };

        Assert.Null(item.Title);
    }

    // -- Корень держит один элемент ---------------------------------------------------------------------

    /// <summary>
    /// Второй элемент получает отказ, а не тихую поломку.
    /// </summary>
    /// <remarks>
    /// Без проверки второй занял бы то, что оставил первый, — пустое содержимое и пустой словарь, — и
    /// записал бы себя хозяином; кто отпустил бы корень последним, вернул бы окну эти подмены.
    /// </remarks>
    [AvaloniaFact]
    public void A_Second_Item_Is_Refused_The_Same_Root()
    {
        var window = Form(declarations: AccentResource);
        var content = window.Content;
        var first = new UiDesignerFormItem { Root = window };
        var second = new UiDesignerFormItem();

        Assert.Throws<InvalidOperationException>(() => second.Root = window);

        // Отказ оставляет и корень, и второй элемент как были.
        Assert.Null(second.Root);
        Assert.False(second.IsTopLevel);

        first.Root = null;

        Assert.Same(content, window.Content);
        Assert.True(window.TryFindResource("Accent", out _));
    }

    [AvaloniaFact]
    public void The_Same_Root_Set_Again_Changes_Nothing()
    {
        var window = Form();
        var item = new UiDesignerFormItem { Root = window };
        var changes = 0;
        item.PropertyChanged += (_, e) => changes += e.Property == UiDesignerFormItem.RootProperty ? 1 : 0;

        item.Root = window;

        Assert.Equal(0, changes);
        Assert.True(item.HasContent);
    }

    // -- Жизнь ----------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Setting_No_Root_Gives_The_Content_Back()
    {
        var window = Form();
        var content = window.Content;
        var item = new UiDesignerFormItem { Root = window };
        Assert.Null(window.Content);

        item.Root = null;

        Assert.Same(content, window.Content);
        Assert.Null(item.Root);
        Assert.False(item.HasContent);
        Assert.False(item.IsTopLevel);
    }

    [AvaloniaFact]
    public void Another_Root_Releases_The_First()
    {
        // Корень, пересобранный обновлением, ставят заново: элемент тот же, место и выбор при нём.
        var first = Form("Title=\"First\"");
        var second = Form("Title=\"Second\"");
        var firstContent = first.Content;
        var item = new UiDesignerFormItem { Root = first };

        item.Root = second;

        Assert.Same(firstContent, first.Content);
        Assert.Same(second, item.Root);
        Assert.Equal("Second", item.Title);
    }

    [AvaloniaFact]
    public async Task The_Root_Is_Set_Only_From_The_Interface_Thread()
    {
        var window = Form();
        var content = window.Content;
        var item = new UiDesignerFormItem { Root = window };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Task.Run(() => item.Root = null, TestContext.Current.CancellationToken));

        // Отказ пришёл первым: корень не остался наполовину возвращённым.
        Assert.Same(window, item.Root);
        Assert.Null(window.Content);

        item.Root = null;
        Assert.Same(content, window.Content);
    }

    // -- В редакторе ----------------------------------------------------------------------------------

    [AvaloniaFact]
    public void The_Authored_Markup_Starts_At_The_Form_Not_At_The_Items_Scopes()
    {
        // Между контейнером и формой лежат области темы и контекста. Они — служебные части элемента, и
        // редактор в режиме Loaded не должен предлагать их к выбору.
        var window = Form(content: "<StackPanel x:Name=\"Stack\"><Button x:Name=\"Press\" Content=\"press\" /></StackPanel>");
        var item = new UiDesignerFormItem { Root = window };
        Host(item);

        var candidates = UiDesignerView.EnumerateSelectionCandidates(item).ToList();

        Assert.Equal(
            new Control[] { Find<StackPanel>(window, "Stack"), Find<Button>(window, "Press") },
            candidates);
        Assert.DoesNotContain(candidates, candidate => candidate is ThemeVariantScope);
    }

    [AvaloniaFact]
    public void A_Click_Inside_The_Form_Selects_The_Control_Under_The_Pointer()
    {
        // Элемент формы хост кладёт в Items сам: контейнер, переданный элементом, редактор берёт как есть.
        var window = Form(
            "Width=\"300\" Height=\"200\"",
            content: "<StackPanel><Button x:Name=\"Press\" Content=\"press\" Height=\"30\" /></StackPanel>");
        var item = new UiDesignerFormItem { Root = window, Location = new Point(100, 100) };
        var editor = new UiDesignerView { SelectionMode = SelectionMode.Multiple };
        editor.InteractionOptions.IsSnapToGridEnabled = false;
        editor.Items.Add(item);

        var host = new Window { Width = 800, Height = 600, Content = editor };
        host.Show();
        host.UpdateLayout();

        var button = Find<Button>(window, "Press");
        var clicked = false;
        button.Click += (_, _) => clicked = true;
        var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), host)!.Value;

        host.MouseDown(centre, MouseButton.Left);
        host.MouseUp(centre, MouseButton.Left);
        host.UpdateLayout();

        Assert.False(clicked, "Загруженная форма не должна жить своей жизнью.");
        Assert.Same(button, editor.PrimarySelectionTarget!.Target);
        Assert.Same(item, editor.PrimarySelectionTarget.Container);
    }
}
