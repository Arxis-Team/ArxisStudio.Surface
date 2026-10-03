using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.Media;
using Avalonia.Reactive;
using Avalonia.Styling;
using Avalonia.Threading;

namespace ArxisStudio.Surface.UiDesigner;

/// <summary>
/// Контейнер дизайнера, который держит корень документа — окно, <see cref="UserControl"/>, шаблонный
/// контрол — и показывает его формой на холсте.
/// </summary>
/// <remarks>
/// <para>
/// Самый частый документ приложения Avalonia — окно, а <see cref="Window"/> — <see cref="TopLevel"/>:
/// Avalonia привязывает его к собственному хосту при создании, и вложить его во что-либо нельзя —
/// раскладка бросает. Элемент встаёт на его место. Корень остаётся корнем: <see cref="Root"/> — само
/// окно, и правки адресуют его, а не заместителя.
/// </para>
/// <para>
/// <b>Заимствует, а не копирует.</b> Содержимое, словарь ресурсов и стили корня переносятся сюда, пока
/// корень держат, и возвращаются, когда <see cref="Root"/> сменился или снят: у словаря ресурсов
/// Avalonia один владелец, а копия потеряла бы вложенные и тематические словари. Ложатся они не на сам
/// элемент, а на внутреннюю область формы — стили окна иначе красили бы рамку элемента, — поэтому свои
/// ресурсы и стили элемента заимствование не трогает.
/// </para>
/// <para>
/// <b>Отражает, а не снимает.</b> Фон, размер, тема и контекст данных следуют за корнем: правка свойства
/// окна видна сразу, без пересборки формы и без потери фокуса внутри неё.
/// </para>
/// <para>
/// <b>В корень не пишет.</b> Истина — документ. Размер элемента — размер формы, и писатель у него один:
/// жест правит документ через хоста, и новое значение приходит сюда от корня.
/// </para>
/// <para>
/// <b>Рамка окна — данные.</b> <see cref="Title"/>, <see cref="Icon"/>, <see cref="CanResize"/> и
/// <see cref="Decorations"/> — свойства окна как окна; заголовка у заместителя нет, и рисует его тема
/// элемента по этим данным. <c>WindowState</c> нет намеренно: окно, которое никогда не показывают,
/// всегда в обычном состоянии.
/// </para>
/// <para>
/// Вид формы задают псевдоклассы, а не наследник на каждый тип корня: <c>:window</c> — корень окно,
/// <c>:control</c> — корень показан как есть, <c>:empty</c> — показывать нечего. Корневой тег при
/// правке меняется, а тип контейнера сменить нельзя, не потеряв место и выбор (ADR 0020).
/// <c>:titled</c> — окно носит заголовок: корень окно с полным оформлением. По нему тема показывает
/// заголовок и делает верхние углы формы прямыми — заголовок и форма сходятся одним прямоугольником.
/// </para>
/// <para>
/// Области формы встают в часть шаблона <c>PART_FormHost</c>. Содержимое формы не идёт в
/// <see cref="ContentControl.Content"/>: его занимает <see cref="ItemsControl"/> под модель хоста.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// var item = new UiDesignerFormItem { Location = new Point(40, 40) };
/// item.Root = session.RootObject;   // окно, UserControl или что угодно ещё
/// designer.Items.Add(item);
///
/// // После обновления, пересобравшего корень:
/// item.Root = session.RootObject;
/// ]]></code>
/// </example>
[TemplatePart(FormHostPart, typeof(Decorator))]
[PseudoClasses(WindowPseudoClass, ControlPseudoClass, EmptyPseudoClass, TitledPseudoClass, FaultedPseudoClass)]
public class UiDesignerFormItem : UiDesignerItem
{
    private const string FormHostPart = "PART_FormHost";
    private const string WindowPseudoClass = ":window";
    private const string ControlPseudoClass = ":control";
    private const string EmptyPseudoClass = ":empty";
    private const string TitledPseudoClass = ":titled";
    private const string FaultedPseudoClass = ":faulted";

    /// <summary>
    /// Свойства текста, которые содержимое наследует от окна, — и которые в дизайнере оно унаследовало бы
    /// от инструмента.
    /// </summary>
    private static readonly AvaloniaProperty[] InheritedText =
    [
        TextElement.FontFamilyProperty,
        TextElement.FontSizeProperty,
        TextElement.FontStyleProperty,
        TextElement.FontWeightProperty,
        TextElement.FontStretchProperty,
        TextElement.ForegroundProperty,
    ];

    /// <summary>
    /// Идентификатор свойства <see cref="ApplicationRoot"/>.
    /// </summary>
    public static readonly DirectProperty<UiDesignerFormItem, Application?> ApplicationRootProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerFormItem, Application?>(
            nameof(ApplicationRoot), static item => item.ApplicationRoot, static (item, value) => item.ApplicationRoot = value);

    /// <summary>
    /// Идентификатор свойства <see cref="FaultMessage"/>.
    /// </summary>
    public static readonly DirectProperty<UiDesignerFormItem, string?> FaultMessageProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerFormItem, string?>(nameof(FaultMessage), static item => item.FaultMessage);

    /// <summary>
    /// Идентификатор свойства <see cref="Root"/>.
    /// </summary>
    public static readonly DirectProperty<UiDesignerFormItem, object?> RootProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerFormItem, object?>(
            nameof(Root), static item => item.Root, static (item, value) => item.Root = value);

    /// <summary>
    /// Идентификатор свойства <see cref="IsTopLevel"/>.
    /// </summary>
    public static readonly DirectProperty<UiDesignerFormItem, bool> IsTopLevelProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerFormItem, bool>(nameof(IsTopLevel), static item => item.IsTopLevel);

    /// <summary>
    /// Идентификатор свойства <see cref="HasContent"/>.
    /// </summary>
    public static readonly DirectProperty<UiDesignerFormItem, bool> HasContentProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerFormItem, bool>(nameof(HasContent), static item => item.HasContent);

    /// <summary>
    /// Идентификатор свойства <see cref="FormBackground"/>.
    /// </summary>
    public static readonly DirectProperty<UiDesignerFormItem, IBrush?> FormBackgroundProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerFormItem, IBrush?>(
            nameof(FormBackground), static item => item.FormBackground);

    /// <summary>
    /// Идентификатор свойства <see cref="FormThemeVariant"/>.
    /// </summary>
    public static readonly DirectProperty<UiDesignerFormItem, ThemeVariant> FormThemeVariantProperty =
        AvaloniaProperty.RegisterDirect<UiDesignerFormItem, ThemeVariant>(
            nameof(FormThemeVariant), static item => item.FormThemeVariant);

    /// <summary>
    /// Идентификатор свойства <see cref="Title"/>.
    /// </summary>
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<UiDesignerFormItem, string?>(nameof(Title));

    /// <summary>
    /// Идентификатор свойства <see cref="Icon"/>.
    /// </summary>
    public static readonly StyledProperty<WindowIcon?> IconProperty =
        AvaloniaProperty.Register<UiDesignerFormItem, WindowIcon?>(nameof(Icon));

    /// <summary>
    /// Идентификатор свойства <see cref="CanResize"/>.
    /// </summary>
    public static readonly StyledProperty<bool> CanResizeProperty =
        AvaloniaProperty.Register<UiDesignerFormItem, bool>(nameof(CanResize), defaultValue: true);

    /// <summary>
    /// Идентификатор свойства <see cref="Decorations"/>.
    /// </summary>
    public static readonly StyledProperty<WindowDecorations> DecorationsProperty =
        AvaloniaProperty.Register<UiDesignerFormItem, WindowDecorations>(
            nameof(Decorations), defaultValue: WindowDecorations.Full);

    /// <summary>
    /// Идентификатор свойства <see cref="ApplicationThemeVariant"/>.
    /// </summary>
    public static readonly StyledProperty<ThemeVariant> ApplicationThemeVariantProperty =
        AvaloniaProperty.Register<UiDesignerFormItem, ThemeVariant>(
            nameof(ApplicationThemeVariant), defaultValue: ThemeVariant.Default);

    /// <summary>
    /// Какой элемент держит какой корень.
    /// </summary>
    /// <remarks>
    /// Корень держит один элемент, и без этой проверки ошибка тихая: второй занял бы то, что оставил
    /// первый, — пустое содержимое и пустой словарь, — и записал бы себя хозяином. Кто отпустил бы корень
    /// последним, вернул бы окну эти подмены и опустошил его, а первое отпускание выглядело бы удачным.
    /// </remarks>
    private static readonly ConditionalWeakTable<TopLevel, UiDesignerFormItem> StandingIn = new();

    /// <summary>
    /// Область корня: несёт запрошенную им тему, его контекст данных, его ресурсы и стили.
    /// </summary>
    /// <remarks>
    /// Вариант темы — область, а не значение, и объявляет её <see cref="TopLevel"/> — то самое, чего в
    /// дереве нет. Без области форма, запросившая светлую тему в тёмном инструменте, показывалась бы
    /// тёмной. Ресурсы и стили корня ложатся сюда же, а не на элемент: на элементе стили окна красили бы
    /// его собственную рамку.
    /// </remarks>
    private readonly ThemeVariantScope _scope = new();

    /// <summary>
    /// Стоит там, где у документа стояло бы его приложение.
    /// </summary>
    /// <remarks>
    /// Корень без объявленной темы значит не «темы нет», а «как скажет приложение», и при работе
    /// приложение — следующая область вверх. В дизайнере следующая область вверх — сам дизайнер, и без
    /// этого слоя нерешившая форма брала бы тему инструмента. Запрос самого корня привязан к
    /// <see cref="_scope"/>, внутри, и побеждает, как и при работе.
    /// </remarks>
    private readonly ThemeVariantScope _application = new();

    private readonly List<IDisposable> _mirrors = new();
    private readonly List<IStyle> _borrowedStyles = new();

    /// <summary>Граница, за которой сбой раскладки формы остаётся сбоем формы, а не холста.</summary>
    private readonly FaultBarrier _barrier;

    /// <summary>Что взято у приложения документа и стоит на <see cref="_application"/>.</summary>
    private readonly List<IStyle> _applicationStyles = new();

    private readonly List<IDataTemplate> _applicationTemplates = new();

    /// <summary>Наследуемый текст из темы окна приложения: привязки и значения уровня стиля, снимаемые освобождением.</summary>
    private readonly List<IDisposable> _applicationText = new();

    private Application? _applicationRoot;
    private ApplicationResources? _applicationResources;
    private string? _faultMessage;

    private Decorator? _host;
    private object? _root;
    private bool _isTopLevel;
    private bool _hasContent;
    private IBrush? _formBackground;
    private ThemeVariant _formThemeVariant = ThemeVariant.Default;

    /// <summary>Корень, у которого взято содержимое и которому его возвращать.</summary>
    private TopLevel? _donor;

    private object? _borrowed;
    private IResourceDictionary? _borrowedResources;
    private Control? _authored;
    private bool _variantMirrored;

    /// <summary>Сколько отдач корня ещё не закончено (<see cref="SuspendRoot"/>).</summary>
    private int _suspendCount;

    /// <summary>Корень, которому отдано взятое, — его и берут заново.</summary>
    private TopLevel? _suspended;

    /// <summary>Эпоха отдач: растёт со сменой корня, и освобождения прежних эпох ничего не делают.</summary>
    private int _suspendEpoch;

    static UiDesignerFormItem()
    {
        // Форма пришла целиком: размечать её некому, и жить своей жизнью она не должна.
        ContentModeProperty.OverrideDefaultValue<UiDesignerFormItem>(SurfaceContentMode.Loaded);
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="UiDesignerFormItem"/>.
    /// </summary>
    public UiDesignerFormItem()
    {
        _application.Child = _scope;
        _barrier = new FaultBarrier(this) { Child = _application };

        // На всю жизнь элемента: оба конца — его собственные объекты, утекать нечему.
        _application.Bind(
            ThemeVariantScope.RequestedThemeVariantProperty,
            this.GetObservable(ApplicationThemeVariantProperty));

        UpdateKind();
    }

    /// <summary>
    /// Получает или задает корень документа, который элемент держит; <see langword="null"/> отпускает.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Это настоящий корень — само окно, а не заместитель: его адресуют правки, и его берёт инспектор.
    /// Для корня, пересобранного обновлением, свойство ставят заново; элемент остаётся тем же и держит
    /// место, размер и выбор.
    /// </para>
    /// <para>
    /// Взять содержимое корня — значит его забрать: контрол не лежит в двух логических деревьях, а у
    /// словаря ресурсов один владелец. Пока корень держат, он не отдаёт ни содержимого, ни ресурсов, ни
    /// стилей; документ при этом не меняется и говорит то же, что говорил.
    /// </para>
    /// <para>
    /// Корень, который можно вложить как есть, элемент просто показывает — без заимствований: он в
    /// дереве и несёт свои ресурсы, стили и тему сам. Объект, который не контрол, — приложение, словарь
    /// ресурсов — показывать нечем; об этом говорит <see cref="HasContent"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Этот корень уже держит другой элемент, либо вызов пришёл не из потока интерфейса.
    /// </exception>
    public object? Root
    {
        get => _root;
        set
        {
            // Заимствование и возврат правят объекты Avalonia; спросить поток первым — значит не оставить
            // корень наполовину возвращённым, когда откажет уже вторая запись.
            Dispatcher.UIThread.VerifyAccess();

            if (ReferenceEquals(_root, value))
                return;

            if (value is TopLevel wanted && StandingIn.TryGetValue(wanted, out var holder) && !ReferenceEquals(holder, this))
            {
                throw new InvalidOperationException(
                    "Этот корень уже держит другой UiDesignerFormItem. Корень держит один элемент: сначала "
                    + "отпустите его там (Root = null).");
            }

            Release();

            var old = _root;
            _root = value;
            Hold(value);
            RaisePropertyChanged(RootProperty, old, value);
        }
    }

    /// <summary>
    /// Возвращает корню всё взятое у него — содержимое, ресурсы и стили — до освобождения результата и
    /// берёт заново, когда его освобождают.
    /// </summary>
    /// <returns>Взятие заново. Повторное освобождение ничего не делает.</returns>
    /// <remarks>
    /// <para>
    /// Для того, кто пишет в дерево корня и читает его, — сессии разметки на время каждой её записи
    /// (ADR 0025). Пока элемент держит окно, окно пусто: запись ушла бы в окно, которое никто не видит,
    /// а обход окна нашёл бы окно без детей.
    /// </para>
    /// <para>
    /// <see cref="Root"/> при этом не меняется, и не меняется ничего, что элемент отражает от корня: ни
    /// размер, ни фон, ни заголовок, ни тема. Берётся заново то, что стоит в корне к моменту
    /// освобождения, — запись, заменившая содержимое окна, приходит на холст без отдельного шага.
    /// </para>
    /// <para>
    /// Вызовы вкладываются; заимствует заново последнее освобождение. Корень, сменённый за время отдачи,
    /// заново не заимствуется: элемент уже держит новый. Корню, вложенному как есть, отдавать нечего, и
    /// результат ничего не делает. Звать и освобождать — из потока интерфейса.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Вызов не из потока интерфейса.</exception>
    public IDisposable SuspendRoot()
    {
        Dispatcher.UIThread.VerifyAccess();

        // Вложенная отдача: взятое уже у корня. Отдача кончается со сменой корня (Release), поэтому
        // идущая отдача — всегда отдача нынешнего.
        if (_suspended is not null)
        {
            _suspendCount++;
            return new Suspension(this, _suspendEpoch);
        }

        if (_donor is not { } top)
            return Suspension.Nothing;

        _suspended = top;
        _suspendCount = 1;

        _scope.Child = null;
        Return();

        return new Suspension(this, _suspendEpoch);
    }

    /// <summary>
    /// Заканчивает одну отдачу корня; последняя берёт его заново.
    /// </summary>
    /// <remarks>
    /// Освобождение помнит эпоху, в которой отдавали. Смена <see cref="Root"/> начинает новую
    /// (<see cref="Release"/>), и освобождение прежней ничего не делает — даже если корень с тех пор
    /// поставили тот же и отдают снова: иначе прежняя отдача забрала бы корень у новой посреди записи.
    /// </remarks>
    private void Resume(int epoch)
    {
        Dispatcher.UIThread.VerifyAccess();

        if (epoch != _suspendEpoch || _suspended is not { } top || --_suspendCount > 0)
            return;

        _suspended = null;

        Borrow(top);

        HasContent = _scope.Child is not null;
        UpdateKind();
    }

    /// <summary>Освобождение отдачи корня: срабатывает один раз.</summary>
    private sealed class Suspension(UiDesignerFormItem? item, int epoch) : IDisposable
    {
        public static readonly IDisposable Nothing = new Suspension(null, 0);

        private UiDesignerFormItem? _item = item;

        public void Dispose() => Interlocked.Exchange(ref _item, null)?.Resume(epoch);
    }

    /// <summary>
    /// Получает признак того, что корень — <see cref="TopLevel"/> и элемент стоит на его месте.
    /// </summary>
    /// <remarks>
    /// <see langword="false"/> — корень вложен как есть; данные рамки тогда молчат, и ничего не
    /// отражается.
    /// </remarks>
    public bool IsTopLevel
    {
        get => _isTopLevel;
        private set => SetAndRaise(IsTopLevelProperty, ref _isTopLevel, value);
    }

    /// <summary>
    /// Получает признак того, что есть что показать.
    /// </summary>
    /// <remarks>
    /// Документ не обязан давать контрол: <c>App.axaml</c> даёт приложение, словарь ресурсов — словарь.
    /// Это повод сказать об этом, а не упасть: без проверки хост рисует пустую карточку и считает, что
    /// форма показана.
    /// </remarks>
    public bool HasContent
    {
        get => _hasContent;
        private set => SetAndRaise(HasContentProperty, ref _hasContent, value);
    }

    /// <summary>
    /// Получает фон формы — тот, что был бы у неё при работе; рисует его тема элемента.
    /// </summary>
    /// <remarks>
    /// Фон корня не отражается напрямую. Окно без объявленного фона всё равно его имеет: тему даёт
    /// приложение, под которым работает сам дизайнер, и показать её — значит покрасить каждую нерешившую
    /// форму в цвет инструмента, выдав его за цвет формы. Показывается фон, заданный документом, либо
    /// тематический — когда хост назвал тему приложения (<see cref="ApplicationThemeVariant"/>); иначе
    /// <see langword="null"/>, и сквозь форму видна карточка хоста.
    /// </remarks>
    public IBrush? FormBackground
    {
        get => _formBackground;
        private set => SetAndRaise(FormBackgroundProperty, ref _formBackground, value);
    }

    /// <summary>
    /// Получает тему, под которой живёт форма: запрошенную её корнем, а без запроса — тему приложения.
    /// </summary>
    /// <remarks>
    /// Нужна рамке окна: рамка — часть окна и носит его тему, а лежит она в шаблоне элемента, вне областей
    /// формы, и без этого свойства взяла бы тему инструмента — светлое окно под тёмным заголовком.
    /// <see cref="ThemeVariant.Default"/> — не решили ни документ, ни хост: наследуется тема инструмента.
    /// </remarks>
    public ThemeVariant FormThemeVariant
    {
        get => _formThemeVariant;
        private set => SetAndRaise(FormThemeVariantProperty, ref _formThemeVariant, value);
    }

    /// <summary>
    /// Привязка корня, которую контейнеру поставил редактор (<see cref="UiDesignerView.ItemRootBinding"/>);
    /// снимается, когда контейнер отпускают.
    /// </summary>
    internal IDisposable? RootBinding { get; set; }

    /// <summary>
    /// Получает заголовок окна — для рамки, которую рисует тема.
    /// </summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        private set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Получает значок окна — для рамки, которую рисует тема.
    /// </summary>
    public WindowIcon? Icon
    {
        get => GetValue(IconProperty);
        private set => SetValue(IconProperty, value);
    }

    /// <summary>
    /// Получает признак того, что окно объявляет себя изменяемым по размеру.
    /// </summary>
    public bool CanResize
    {
        get => GetValue(CanResizeProperty);
        private set => SetValue(CanResizeProperty, value);
    }

    /// <summary>
    /// Получает оформление, которое окно просит у системы.
    /// </summary>
    public WindowDecorations Decorations
    {
        get => GetValue(DecorationsProperty);
        private set => SetValue(DecorationsProperty, value);
    }

    /// <summary>
    /// Получает или задает тему, которую дало бы форме приложение документа, когда хост её знает.
    /// </summary>
    /// <remarks>
    /// <para>
    /// У действующей темы формы два слоя: что объявил её корень и что запросило её приложение для всего,
    /// что не объявило ничего. Первый элемент берёт у самого корня; второй знает только хост — это
    /// сведение о проекте, а не о документе и не об инструменте.
    /// </para>
    /// <para>
    /// По умолчанию <see cref="ThemeVariant.Default"/> — наследуется тема инструмента: честный ответ
    /// хоста, который не знает. Хост, который знает, даёт тему, к которой пришло бы приложение: для
    /// приложения, которое само говорит «по умолчанию», это тема платформы, а не инструмента. Тёмный
    /// дизайнер над светлым приложением показывает светлую форму.
    /// </para>
    /// </remarks>
    public ThemeVariant ApplicationThemeVariant
    {
        get => GetValue(ApplicationThemeVariantProperty);
        set => SetValue(ApplicationThemeVariantProperty, value);
    }

    /// <summary>
    /// Получает или задает приложение документа — то, во что программа одевает свои формы, —
    /// когда хост его загрузил; <see langword="null"/> отпускает.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Форма сама не говорит, как выглядит: тему задаёт её приложение — <c>Application.Styles</c> в
    /// <c>App.axaml</c>, — и ресурсы, которые она называет, объявлены там же. В дизайнере над формой
    /// стоит приложение инструмента, и без этого слоя форма одевалась бы им (ADR 0020, дополнение).
    /// </para>
    /// <para>
    /// <b>Заимствует, а не копирует</b>, как и корень-окно: стили и шаблоны данных приложения ложатся на
    /// область, стоящую на месте приложения, и возвращаются ему, когда свойство сменили или сняли. У стиля
    /// один владелец, поэтому приложение — своё на каждую форму. Словарь ресурсов остаётся приложению:
    /// ссылка <c>{DynamicResource}</c> в его разметке ищет ключ от него самого, и область спрашивает его
    /// ресурсы через приложение.
    /// </para>
    /// <para>
    /// Наследуемые свойства текста — шрифт, кегль, начертание, цвет — область берёт у темы окна этого
    /// приложения: при работе их задаёт окну тема, и содержимое наследует их от окна. Содержимое,
    /// вынутое из окна, унаследовало бы их от инструмента.
    /// </para>
    /// <para>
    /// Вариант темы приложения хост задаёт отдельно (<see cref="ApplicationThemeVariant"/>): он известен
    /// и без загруженного приложения.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Вызов не из потока интерфейса.</exception>
    public Application? ApplicationRoot
    {
        get => _applicationRoot;
        set
        {
            Dispatcher.UIThread.VerifyAccess();

            if (ReferenceEquals(_applicationRoot, value))
                return;

            ReturnApplication();

            var old = _applicationRoot;
            _applicationRoot = value;
            BorrowApplication(value);
            RaisePropertyChanged(ApplicationRootProperty, old, value);
        }
    }

    /// <summary>
    /// Получает сообщение сбоя, с которым содержимое формы упало на замере или раскладке, или
    /// <see langword="null"/>, пока оно не падало.
    /// </summary>
    /// <remarks>
    /// Контрол формы — код проекта, и упасть в своём <c>MeasureOverride</c> он вправе. Без границы его
    /// исключение уносило бы весь проход раскладки — холст и окно хоста вместе с ним. Элемент ловит его
    /// на своей границе (ADR 0028): форма перестаёт меряться и занимает нулевой размер, элемент метит
    /// себя <c>:faulted</c>, а причина — здесь. Новый корень начинает заново. Сбой отрисовки этим не
    /// ловится: отрисовка идёт мимо раскладки.
    /// </remarks>
    public string? FaultMessage
    {
        get => _faultMessage;
        private set => SetAndRaise(FaultMessageProperty, ref _faultMessage, value);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Форма, а не области элемента: области — его служебные части, и выбирать их нечего. Содержимое,
    /// которое не контрол, показывает обёртка элемента — авторской разметки в ней нет.
    /// </remarks>
    internal override Control? AuthoredRoot => _authored;

    /// <inheritdoc />
    /// <remarks>Часть шаблона, в которой стоят области формы.</remarks>
    internal override Visual? ContentHost => _host;

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_host != null)
            _host.Child = null;

        _host = e.NameScope.Find<Decorator>(FormHostPart);
        if (_host != null)
            _host.Child = _barrier;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Хост может узнать тему приложения позже, чем поставил корень.
        if (change.Property == ApplicationThemeVariantProperty)
        {
            if (_donor is { } top)
            {
                MirrorApplicationVariant(top);
                ShowBackground(top);
            }

            UpdateFormThemeVariant();
        }
        else if (change.Property == DecorationsProperty)
        {
            UpdateKind();
        }
    }

    /// <summary>
    /// Корень, которого держат, несёт действующую тему сам: свою или одолженную у приложения
    /// (<see cref="MirrorApplicationVariant"/>). Прочим корням тему даёт только приложение.
    /// </summary>
    private void UpdateFormThemeVariant() =>
        FormThemeVariant = _donor?.RequestedThemeVariant ?? ApplicationThemeVariant;

    private void Hold(object? root)
    {
        switch (root)
        {
            case TopLevel top:
                IsTopLevel = true;
                Borrow(top);
                Project(top);
                break;

            case Control control:
                // Корню, вложенному как есть, отражать нечего — ресурсы, стили и тему он несёт сам, — но
                // изоляция нужна и ему: локальный null обрывает наследование, и модель хоста не придёт
                // форме её данными через шаблон, привязанный к ней.
                _scope.DataContext = null;
                _scope.Child = control;
                _authored = control;
                FollowSize(control);
                break;
        }

        HasContent = _scope.Child is not null;
        UpdateFormThemeVariant();
        UpdateKind();
    }

    /// <summary>
    /// Отпускает корень и возвращает всё взятое.
    /// </summary>
    private void Release()
    {
        // Отдача кончается вместе с корнем: взятое у него уже возвращено, и брать его заново некому.
        _suspendEpoch++;
        _suspended = null;
        _suspendCount = 0;

        foreach (var mirror in _mirrors)
            mirror.Dispose();

        _mirrors.Clear();

        _scope.Child = null;
        _scope.ClearValue(DataContextProperty);
        _authored = null;

        foreach (var property in InheritedText)
            _scope.ClearValue(property);

        Return();

        FormBackground = null;
        IsTopLevel = false;
        HasContent = false;
        UpdateFormThemeVariant();

        // Новый корень начинает заново: упало прежнее содержимое, а не элемент.
        ClearFault();
    }

    /// <summary>
    /// Берёт то, что Avalonia не даёт держать двоим.
    /// </summary>
    private void Borrow(TopLevel top)
    {
        _donor = top;
        StandingIn.Add(top, this);

        _borrowed = top.Content;
        top.Content = null;

        // Содержимое окна не обязано быть контролом: строку или модель окно показало бы своим
        // ContentTemplate. Потерять его — нарисовать пустую карточку, неотличимую от пустой формы.
        _authored = _borrowed as Control;
        _scope.Child = _borrowed switch
        {
            Control control => control,
            null => null,
            _ => new ContentControl { Content = _borrowed, ContentTemplate = top.ContentTemplate }
        };

        // Вливается в словарь области, а не встаёт на его место: второго владельца Avalonia не даёт,
        // поэтому корень сперва отпускает словарь, — но отпущенный, он вливается целым, с вложенными
        // и тематическими словарями.
        _borrowedResources = top.Resources;
        top.Resources = new ResourceDictionary();
        _scope.Resources.MergedDictionaries.Add(_borrowedResources);

        _borrowedStyles.AddRange(top.Styles);
        top.Styles.Clear();
        foreach (var style in _borrowedStyles)
            _scope.Styles.Add(style);

        MirrorApplicationVariant(top);
    }

    /// <summary>
    /// Даёт корню тему, которую дало бы ему приложение.
    /// </summary>
    /// <remarks>
    /// Пока корень держат, он вне дерева, и тема приложения к нему не приходит ниоткуда: все тематические
    /// значения, что он ещё несёт, решались бы под темой инструмента. Запись темы приложения на время
    /// заимствования делает их значениями приложения — тематический фон, показанный ниже, выбран ею.
    /// Тему, объявленную документом, запись не трогает — документ и при работе старше приложения, — и
    /// она снимается раньше, чем корень возвращают.
    /// </remarks>
    private void MirrorApplicationVariant(TopLevel top)
    {
        var documentDecided = !_variantMirrored
            && top.GetDiagnostic(TopLevel.RequestedThemeVariantProperty).Priority <= BindingPriority.LocalValue;

        if (documentDecided)
            return;

        if (ApplicationThemeVariant != ThemeVariant.Default)
        {
            top.SetValue(TopLevel.RequestedThemeVariantProperty, ApplicationThemeVariant);
            _variantMirrored = true;
        }
        else if (_variantMirrored)
        {
            top.ClearValue(TopLevel.RequestedThemeVariantProperty);
            _variantMirrored = false;
        }
    }

    /// <summary>
    /// Показывает фон, который у формы был бы на самом деле.
    /// </summary>
    /// <remarks>
    /// Вопрос — откуда значение, и <c>IsSet</c> на него не отвечает: он истинен и для тематического, и
    /// для объявленного. Отвечает приоритет. Одна перемена отсюда не видна: смена приоритета при том же
    /// значении — документ объявил локально ту самую кисть, которую уже давала тема, — уведомления не
    /// поднимает, и подписаться на приоритет в Avalonia не на что. Заново поставленный корень её
    /// перечитывает.
    /// </remarks>
    private void ShowBackground(TopLevel top)
    {
        var declared = top.GetDiagnostic(TemplatedControl.BackgroundProperty);

        FormBackground =
            declared.Priority <= BindingPriority.LocalValue || ApplicationThemeVariant != ThemeVariant.Default
                ? declared.Value as IBrush
                : null;
    }

    /// <summary>
    /// Возвращает всё взятое — в порядке, обратном тому, как брали.
    /// </summary>
    private void Return()
    {
        if (_donor is null)
            return;

        if (_variantMirrored)
        {
            _donor.ClearValue(TopLevel.RequestedThemeVariantProperty);
            _variantMirrored = false;
        }

        // Ровно взятые, и снятые здесь раньше, чем добавленные там: коллекция, у которой забирают
        // владельца, отпускает первой.
        foreach (var style in _borrowedStyles)
        {
            _scope.Styles.Remove(style);
            _donor.Styles.Add(style);
        }

        _borrowedStyles.Clear();

        if (_borrowedResources is not null)
        {
            _scope.Resources.MergedDictionaries.Remove(_borrowedResources);
            _donor.Resources = _borrowedResources;
            _borrowedResources = null;
        }

        _donor.Content = _borrowed;

        StandingIn.Remove(_donor);

        _donor = null;
        _borrowed = null;
    }

    /// <summary>
    /// Привязывает всё, что содержимое унаследовало бы от корня.
    /// </summary>
    private void Project(TopLevel top)
    {
        _mirrors.Add(top.GetPropertyChangedObservable(TemplatedControl.BackgroundProperty)
            .Subscribe(new AnonymousObserver<AvaloniaPropertyChangedEventArgs>(_ => ShowBackground(top))));
        ShowBackground(top);

        FollowSize(top);

        _mirrors.Add(_scope.Bind(
            ThemeVariantScope.RequestedThemeVariantProperty,
            top.GetObservable(TopLevel.RequestedThemeVariantProperty)));
        _mirrors.Add(top.GetObservable(TopLevel.RequestedThemeVariantProperty)
            .Subscribe(new AnonymousObserver<ThemeVariant?>(_ => UpdateFormThemeVariant())));

        // То, что находят последним и принимают за другое. Данные времени разработки стоят на корне —
        // Design.DataContext есть свойство окна, — и вынутое из окна содержимое выходит и из этого
        // контекста: привязки пустеют, форма сжимается в ноль и выглядит незагрузившейся, а на место её
        // данных приходят данные хоста.
        _mirrors.Add(_scope.Bind(DataContextProperty, top.GetObservable(DataContextProperty)));

        // Шрифт и цвет текста, объявленные на самом окне: содержимое наследует их от окна, а вынутое из
        // него — унаследовало бы от того, что стоит выше. Только объявленные документом: значение по
        // умолчанию у окна вне дерева перебило бы тему приложения, которая лежит на области выше.
        foreach (var property in InheritedText)
        {
            _mirrors.Add(top.GetPropertyChangedObservable(property)
                .Subscribe(new AnonymousObserver<AvaloniaPropertyChangedEventArgs>(_ => MirrorText(top, property))));
            MirrorText(top, property);
        }

        if (top is Window window)
        {
            _mirrors.Add(this.Bind(TitleProperty, window.GetObservable(Window.TitleProperty)));
            _mirrors.Add(this.Bind(IconProperty, window.GetObservable(Window.IconProperty)));
            _mirrors.Add(this.Bind(CanResizeProperty, window.GetObservable(Window.CanResizeProperty)));
            _mirrors.Add(this.Bind(DecorationsProperty, window.GetObservable(Window.WindowDecorationsProperty)));
        }
    }

    /// <summary>
    /// Размер элемента — размер формы: объявленные корнем ширина и высота приходят элементу.
    /// </summary>
    /// <remarks>
    /// Текущим значением, а не привязкой: привязка хоста к размеру контейнера остаётся на месте и
    /// получает то же значение. Обратно в корень не пишется ничего — размер, поставленный элементу,
    /// корня не меняет.
    /// <para>
    /// Приходит только объявленное. Корень без ширины или высоты (<see cref="double.NaN"/>) ничего не
    /// объявил, и размер элемента остаётся тем, что поставил хост: иначе карточка, которой хост дал
    /// размер по умолчанию, сжималась бы до содержимого у каждой формы без объявленного размера.
    /// </para>
    /// </remarks>
    private void FollowSize(Control root)
    {
        _mirrors.Add(root.GetObservable(WidthProperty).Subscribe(new AnonymousObserver<double>(width =>
        {
            if (!double.IsNaN(width))
                SetCurrentValue(WidthProperty, width);
        })));
        _mirrors.Add(root.GetObservable(HeightProperty).Subscribe(new AnonymousObserver<double>(height =>
        {
            if (!double.IsNaN(height))
                SetCurrentValue(HeightProperty, height);
        })));
    }

    /// <summary>Переносит на область корня свойство текста, если его объявил сам корень.</summary>
    private void MirrorText(TopLevel top, AvaloniaProperty property)
    {
        if (top.GetDiagnostic(property).Priority <= BindingPriority.LocalValue)
            _scope.SetValue(property, top.GetValue(property));
        else
            _scope.ClearValue(property);
    }

    /// <summary>
    /// Берёт у приложения документа то, во что оно одевает формы, и ставит на область приложения.
    /// </summary>
    private void BorrowApplication(Application? application)
    {
        if (application is null)
            return;

        // Стили и шаблоны снимаются у приложения раньше, чем добавляются сюда: коллекция, у которой
        // забирают владельца, отпускает первой.
        _applicationStyles.AddRange(application.Styles);
        application.Styles.Clear();
        foreach (var style in _applicationStyles)
            _application.Styles.Add(style);

        // Словарь остаётся приложению: ссылки в его разметке ищут ключ от него самого. Форма спрашивает
        // его ресурсы через посредника (ApplicationResources).
        _applicationResources = new ApplicationResources(application);
        _application.Resources.MergedDictionaries.Add(_applicationResources);

        _applicationTemplates.AddRange(application.DataTemplates);
        application.DataTemplates.Clear();
        foreach (var template in _applicationTemplates)
            _application.DataTemplates.Add(template);

        InheritWindowText();
    }

    /// <summary>
    /// Даёт области корня наследуемый текст, который тема приложения даёт окну.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Тема окна ищется только в том, что объявило приложение: выше стоит инструмент, и его тема окна —
    /// его, а не программы.
    /// </para>
    /// <para>
    /// Ложится текст на область корня, а не приложения: она стоит на месте окна — того, кого тема
    /// одевает, — и носит запрошенный корнем вариант темы. Значение, данное привязкой —
    /// <c>{DynamicResource}</c>, как у тем Avalonia, — находит ресурс от неё вверх, в варианте окна. Текст,
    /// объявленный самим окном, ставится той же области значением и сильнее темы, как при работе.
    /// </para>
    /// <para>
    /// Уровень — шаблонный, как у значений темы окна: стиль инструмента, который одевает саму
    /// <see cref="ThemeVariantScope"/> — Fluent ставит ей цвет текста, — стоит ниже и формы не перекрасит.
    /// Без приложения документа его стиль остаётся в силе: цвет текста под вариантом формы лучше, чем цвет
    /// инструмента.
    /// </para>
    /// </remarks>
    private void InheritWindowText()
    {
        if (WindowTheme() is not { } theme)
            return;

        foreach (var setter in theme.Setters.OfType<Setter>())
        {
            if (setter.Property is not { } declared
                || InheritedText.FirstOrDefault(property => property == declared) is not { } property)
            {
                continue;
            }

            if (setter.Value is BindingBase binding)
                _applicationText.Add(_scope.Bind(property, binding));
            else if (_scope.SetValue(property, setter.Value, BindingPriority.Template) is { } set)
                _applicationText.Add(set);
        }
    }

    /// <summary>Тема окна из того, что объявило приложение документа: его ресурсы, затем его стили, последний первым.</summary>
    private ControlTheme? WindowTheme()
    {
        var variant = _application.ActualThemeVariant;

        if (_applicationRoot is { } application
            && application.TryGetResource(typeof(Window), variant, out var declared)
            && declared is ControlTheme theme)
        {
            return theme;
        }

        for (var i = _applicationStyles.Count - 1; i >= 0; i--)
        {
            if (_applicationStyles[i] is IResourceProvider provider
                && provider.TryGetResource(typeof(Window), variant, out var styled)
                && styled is ControlTheme fromStyles)
            {
                return fromStyles;
            }
        }

        return null;
    }

    /// <summary>Возвращает приложению документа всё взятое — в порядке, обратном тому, как брали.</summary>
    private void ReturnApplication()
    {
        foreach (var text in _applicationText)
            text.Dispose();

        _applicationText.Clear();

        if (_applicationRoot is not { } application)
            return;

        foreach (var template in _applicationTemplates)
        {
            _application.DataTemplates.Remove(template);
            application.DataTemplates.Add(template);
        }

        _applicationTemplates.Clear();

        if (_applicationResources is not null)
        {
            _application.Resources.MergedDictionaries.Remove(_applicationResources);
            _applicationResources = null;
        }

        foreach (var style in _applicationStyles)
        {
            _application.Styles.Remove(style);
            application.Styles.Add(style);
        }

        _applicationStyles.Clear();
    }

    /// <summary>Записывает сбой содержимого: форма больше не меряется, пока корень не сменят.</summary>
    private void Fault(Exception error)
    {
        if (_faultMessage is not null)
            return;

        // Сменить содержимое посреди прохода нельзя, а свойство и псевдокласс — можно: перерисовка
        // элемента просится, а не делается здесь.
        FaultMessage = error.Message.Length > 0 ? error.Message : error.GetType().Name;
        PseudoClasses.Set(FaultedPseudoClass, true);
    }

    private void ClearFault()
    {
        if (_faultMessage is null)
            return;

        FaultMessage = null;
        PseudoClasses.Set(FaultedPseudoClass, false);
        _barrier.InvalidateMeasure();
    }

    /// <summary>
    /// Граница между раскладкой элемента и раскладкой формы: исключение формы кончается здесь.
    /// </summary>
    /// <remarks>
    /// Отказ процесса ловить нечем: после нехватки памяти дизайнер всё равно не продолжится, и делать вид,
    /// что форма просто не разложилась, значило бы скрыть настоящую причину.
    /// </remarks>
    private sealed class FaultBarrier(UiDesignerFormItem item) : Decorator
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            if (item._faultMessage is not null)
                return default;

            try
            {
                return base.MeasureOverride(availableSize);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                item.Fault(error);

                return default;
            }
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (item._faultMessage is not null)
                return finalSize;

            try
            {
                return base.ArrangeOverride(finalSize);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                item.Fault(error);

                return finalSize;
            }
        }
    }

    private void UpdateKind()
    {
        PseudoClasses.Set(WindowPseudoClass, _root is Window);
        PseudoClasses.Set(ControlPseudoClass, _root is Control and not TopLevel);
        PseudoClasses.Set(EmptyPseudoClass, !_hasContent);

        // Заголовок есть только у окна с полным оформлением. Решается здесь, а не селекторами темы:
        // от того же ответа зависят и сам заголовок, и углы трёх частей под ним, и разойтись им нельзя.
        PseudoClasses.Set(TitledPseudoClass, _root is Window && Decorations == WindowDecorations.Full);
    }
}
