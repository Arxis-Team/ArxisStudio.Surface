using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ArxisStudio;
using DesignEditor.Demo.ViewModels;
using Editor = ArxisStudio.Surface.UiDesigner.DesignEditor;
using ArxisStudio.Surface;
using ArxisStudio.Surface.Editing;

namespace DesignEditor.Demo.Views;

/// <summary>
/// Панель свойств выбранного элемента.
/// </summary>
/// <remarks>
/// Показывает <b>главный выбранный</b> target: групповые действия у демо и так есть —
/// в контекстном меню и в панели групп, — а панель свойств отвечает на вопрос «что это
/// за элемент и какие у него числа».
/// <para>
/// Разделов пять, и они делятся ровно по тому, чем распоряжается редактор.
/// Геометрию и порядок перекрытия пишет он сам — через <see cref="Editor.SetTargetGeometry"/>
/// и <c>BringToFront</c>/<c>SendToBack</c>, — поэтому такие правки попадают в
/// <see cref="Editor.EditCompleted"/> и отменяются. Раскладка, оформление и политики
/// редактирования — обычные свойства Avalonia и attached-пометки: их панель пишет
/// напрямую, и в контракт изменений они не попадают. Разделы это честно помечают.
/// </para>
/// </remarks>
public partial class PropertiesPanel : UserControl
{
    /// <summary>Идентификатор свойства редактора.</summary>
    public static readonly StyledProperty<Editor?> EditorProperty =
        AvaloniaProperty.Register<PropertiesPanel, Editor?>(nameof(Editor));

    /// <summary>Идентификатор свойства раскрытых подсказок.</summary>
    public static readonly StyledProperty<bool> IsGestureHelpExpandedProperty =
        AvaloniaProperty.Register<PropertiesPanel, bool>(nameof(IsGestureHelpExpanded));

    private Editor? _attached;

    /// <summary>Признак заполнения полей: правки в этот момент не применяются.</summary>
    private bool _filling;

    /// <summary>Признак того, что пользователь сейчас правит поле.</summary>
    /// <remarks>
    /// Пока фокус в поле, панель себя не перечитывает: иначе набранное затиралось бы
    /// на каждом кадре чужого жеста.
    /// </remarks>
    private bool _typing;

    /// <summary>Инициализирует панель.</summary>
    public PropertiesPanel()
    {
        // Панель — сама себе модель, как и панель групп: строит она себя из редактора.
        DataContext = this;

        // InitializeComponent здесь сгенерированный: в демо включён Avalonia.NameGenerator,
        // и поля x:Name заполняет именно он. Своя однострочная версия с AvaloniaXamlLoader
        // перекрывала бы его — разметка загружалась бы, а поля оставались пустыми.
        InitializeComponent();

        HAlign.ItemsSource = Enum.GetValues<HorizontalAlignment>();
        VAlign.ItemsSource = Enum.GetValues<VerticalAlignment>();
        MovePolicyBox.ItemsSource = Enum.GetValues<MovePolicy>();
        ResizePolicyBox.ItemsSource = new[] { ResizePolicy.None, ResizePolicy.Horizontal, ResizePolicy.Vertical, ResizePolicy.All };

        foreach (var box in new[] { PosX, PosY, SizeW, SizeH, MarginBox, OpacityBox, CornerBox, BackgroundBox })
        {
            box.KeyDown += Field_OnKeyDown;
            box.GotFocus += Field_OnGotFocus;
            box.LostFocus += Field_OnLostFocus;
        }

        HAlign.SelectionChanged += Choice_OnChanged;
        VAlign.SelectionChanged += Choice_OnChanged;
        MovePolicyBox.SelectionChanged += Choice_OnChanged;
        ResizePolicyBox.SelectionChanged += Choice_OnChanged;
    }

    /// <summary>
    /// Жесты редактора для подсказки.
    /// </summary>
    /// <remarks>
    /// Список лежит здесь, а не в разметке: рукописная сетка от него отстала — каждая
    /// строка стоила восемнадцати строк XAML с ручной нумерацией <c>Grid.Row</c>, и
    /// дописать в неё жест было дороже, чем не дописать.
    /// <para>
    /// И здесь, а не во вью-модели окна: панель ставит себе <c>DataContext = this</c>,
    /// поэтому привязка <c>{Binding Gestures}</c>, заданная снаружи, замыкалась бы на
    /// саму панель и приезжала пустой. Список описывает редактор, а не данные
    /// приложения, так что жить рядом с тем, кто его показывает, ему и место.
    /// </para>
    /// <para>
    /// Модификаторы названы теми, что задаёт демо (<c>InputGestures</c> в разметке) и
    /// что стоит по умолчанию у самого редактора: <c>Alt</c> — обход привязки у указателя
    /// и изменение размера у стрелок, <c>Ctrl</c> — работа с контейнером, <c>Shift</c> —
    /// добавление к выделению.
    /// </para>
    /// </remarks>
    public IReadOnlyList<GestureHint> Gestures { get; } = new GestureHint[]
    {
        new("Left Click", "Выбрать вложенный контрол"),
        new("Double Click", "Войти в группу и выбрать её участника"),
        new("Shift + Click", "Добавить вложенный контрол в той же форме"),
        new("Right Click", "Контекстное меню"),
        new("Left Drag", "Тянуть выделение или рамку по пустой области"),
        new("Drag ручки", "Изменить размер выделения"),
        new("Alt + Drag", "Вести без привязки к сетке и направляющим"),
        new("Middle Drag", "Панорамирование viewport"),
        new("Wheel", "Масштабирование viewport"),
        new("Ctrl + Click", "Выбрать DesignEditorItem"),
        new("Ctrl + Drag", "Переместить выбранный DesignEditorItem"),
        new("Ctrl + Shift", "Добавить item или рамочное выделение контейнеров"),
        new("Drag с линейки", "Вытянуть направляющую"),
        new("Drag линии", "Переместить направляющую; увести за край — убрать"),
        new("← ↑ → ↓", "Сместить выделение на шаг"),
        new("Shift + ← ↑ → ↓", "Сместить крупным шагом"),
        new("Alt + ← ↑ → ↓", "Изменить размер: правый и нижний край"),
        new("Alt + Shift + ← ↑ → ↓", "Изменить размер крупным шагом"),
        new("Ctrl + Z", "Отменить"),
        new("Ctrl + X", "Повторить"),
        new("Delete", "Удалить выбранные элементы"),
        new("Esc / Ctrl + A", "Снять выделение / выбрать все"),
    };

    /// <summary>
    /// Получает или задает признак раскрытой подсказки о жестах.
    /// </summary>
    /// <remarks>
    /// Состояние держит сама панель: показ подсказки — свойство её вида, а не данных
    /// приложения. Свёрнута по умолчанию — развёрнутая занимает нижнюю треть панели,
    /// а нужна один раз.
    /// </remarks>
    public bool IsGestureHelpExpanded
    {
        get => GetValue(IsGestureHelpExpandedProperty);
        set => SetValue(IsGestureHelpExpandedProperty, value);
    }

    /// <summary>Получает или задает редактор, свойства которого показывает панель.</summary>
    public Editor? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == EditorProperty)
            Attach(change.GetNewValue<Editor?>());
    }

    private void Attach(Editor? editor)
    {
        if (_attached != null)
        {
            _attached.SurfaceSelectionChanged -= OnSelectionChanged;
            _attached.EditCompleted -= OnEditCompleted;
            _attached.PropertyChanged -= OnEditorPropertyChanged;
        }

        _attached = editor;

        if (_attached == null)
        {
            Fill(null);
            return;
        }

        _attached.SurfaceSelectionChanged += OnSelectionChanged;
        _attached.EditCompleted += OnEditCompleted;
        _attached.PropertyChanged += OnEditorPropertyChanged;
        Fill(_attached.PrimarySelectionTarget?.Target);
    }

    private void OnSelectionChanged(object? sender, SurfaceSelectionChangedEventArgs e) => Refresh();

    /// <summary>
    /// Следит за геометрией выделения, а не только за составом.
    /// </summary>
    /// <remarks>
    /// Геометрию меняют не только поля панели: жест, нюдж стрелками и отмена. Отмена при
    /// этом ничего не публикует — она применяет уже записанное изменение, — поэтому
    /// событий контракта здесь мало, и панель осталась бы с прежними числами. Хуже того,
    /// следующий уход фокуса записал бы их обратно.
    /// </remarks>
    private void OnEditorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_typing)
            return;

        if (e.Property == Editor.SelectionBoundsProperty || e.Property == Editor.PrimarySelectionTargetProperty)
            Refresh();
    }

    private void OnEditCompleted(object? sender, SurfaceEditCompletedEventArgs e)
    {
        // Пока фокус в поле, перечитывать нельзя: правка панели публикуется синхронно,
        // а design-координаты к этому моменту ещё прежние — набранное число затёрлось бы
        // старым, и следующий уход фокуса записал бы старое обратно.
        if (!_typing)
            Refresh();
    }

    /// <summary>Перечитывает свойства выбранного элемента.</summary>
    public void Refresh() => Fill(_attached?.PrimarySelectionTarget?.Target);

    private Control? Target => _attached?.PrimarySelectionTarget?.Target;

    private void Fill(Control? target)
    {
        _filling = true;
        try
        {
            Sections.IsEnabled = target != null;
            TargetName.Text = target == null ? "ничего не выбрано" : Describe(target);

            if (target == null || _attached is not { } editor)
            {
                foreach (var box in new[] { PosX, PosY, SizeW, SizeH, MarginBox, OpacityBox, CornerBox, BackgroundBox })
                    box.Text = string.Empty;

                PlacementNote.Text = string.Empty;
                return;
            }

            var bounds = editor.SelectionBounds;
            PosX.Text = Format(bounds.X);
            PosY.Text = Format(bounds.Y);
            SizeW.Text = Format(bounds.Width);
            SizeH.Text = Format(bounds.Height);

            // Раскладка вправе не отдать положение, и поле об этом говорит само:
            // включённое поле, которое ничего не меняет, читается как поломка.
            var canMove = editor.PrimarySelectionMovePolicy != MovePolicy.None;
            PosX.IsEnabled = canMove;
            PosY.IsEnabled = canMove;

            var canResize = editor.PrimarySelectionResizePolicy != ResizePolicy.None;
            SizeW.IsEnabled = canResize;
            SizeH.IsEnabled = canResize;

            PlacementNote.Text = canMove
                ? $"Раскладка: {editor.PrimarySelectionPlacement}"
                : $"Раскладка: {editor.PrimarySelectionPlacement} — положением распоряжается она, поэтому X и Y недоступны";

            MarginBox.Text = target.Margin.ToString();
            HAlign.SelectedItem = target.HorizontalAlignment;
            VAlign.SelectedItem = target.VerticalAlignment;

            OpacityBox.Text = Format(target.Opacity);
            CornerBox.Text = CornerOf(target) is { } corner ? Format(corner.TopLeft) : string.Empty;
            CornerBox.IsEnabled = CornerOf(target) != null;
            BackgroundBox.Text = BackgroundOf(target) is ISolidColorBrush solid ? solid.Color.ToString() : string.Empty;
            BackgroundBox.IsEnabled = HasBackground(target);

            MovePolicyBox.SelectedItem = SurfaceInteraction.GetMovePolicy(target);
            ResizePolicyBox.SelectedItem = SurfaceInteraction.GetResizePolicy(target);
        }
        finally
        {
            _filling = false;
        }
    }

    private static string Describe(Control target) =>
        string.IsNullOrEmpty(target.Name) ? target.GetType().Name : $"{target.GetType().Name} ({target.Name})";

    private static string Format(double value) =>
        value.ToString("0.##", CultureInfo.CurrentCulture);

    private static bool TryParse(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
        || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static CornerRadius? CornerOf(Control target) => target switch
    {
        Border border => border.CornerRadius,
        TemplatedControl templated => templated.CornerRadius,
        _ => null
    };

    private static IBrush? BackgroundOf(Control target) => target switch
    {
        Border border => border.Background,
        Panel panel => panel.Background,
        TemplatedControl templated => templated.Background,
        _ => null
    };

    private static bool HasBackground(Control target) => target is Border or Panel or TemplatedControl;

    private void Field_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Apply();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            _typing = false;
            Refresh();
            e.Handled = true;
        }
    }

    private void Field_OnGotFocus(object? sender, RoutedEventArgs e) => _typing = true;

    private void Field_OnLostFocus(object? sender, RoutedEventArgs e)
    {
        _typing = false;
        Apply();
    }

    private void Choice_OnChanged(object? sender, SelectionChangedEventArgs e) => Apply();

    /// <summary>
    /// Применяет содержимое полей к выбранному элементу.
    /// </summary>
    /// <remarks>
    /// Геометрия уходит одним вызовом <see cref="Editor.SetTargetGeometry"/>: это шов
    /// редактора, поэтому правка попадает в контракт изменений и отменяется. Остальное —
    /// прямая запись свойств Avalonia, и разделы об этом предупреждают.
    /// <para>
    /// Нечитаемое значение не применяется и молча возвращается прежним при следующем
    /// обновлении: панель не место для сообщений об ошибках ввода.
    /// </para>
    /// </remarks>
    private void Apply()
    {
        if (_filling || Target is not { } target || _attached is not { } editor)
            return;

        var bounds = editor.SelectionBounds;
        var x = TryParse(PosX.Text, out var px) ? px : bounds.X;
        var y = TryParse(PosY.Text, out var py) ? py : bounds.Y;
        var w = TryParse(SizeW.Text, out var pw) ? pw : bounds.Width;
        var h = TryParse(SizeH.Text, out var ph) ? ph : bounds.Height;

        var next = new Rect(x, y, Math.Max(0, w), Math.Max(0, h));
        if (next != bounds)
            editor.SetTargetGeometry(target, next);

        if (TryParseThickness(MarginBox.Text, out var margin))
            target.Margin = margin;

        if (HAlign.SelectedItem is HorizontalAlignment horizontal)
            target.HorizontalAlignment = horizontal;

        if (VAlign.SelectedItem is VerticalAlignment vertical)
            target.VerticalAlignment = vertical;

        if (TryParse(OpacityBox.Text, out var opacity))
            target.Opacity = Math.Clamp(opacity, 0, 1);

        ApplyCorner(target);
        ApplyBackground(target);

        if (MovePolicyBox.SelectedItem is MovePolicy movePolicy)
            SurfaceInteraction.SetMovePolicy(target, movePolicy);

        if (ResizePolicyBox.SelectedItem is ResizePolicy resizePolicy)
            SurfaceInteraction.SetResizePolicy(target, resizePolicy);

        // Перечитывать поля здесь нельзя: design-координаты отстают на проход
        // диспетчера, и введённое число тут же затиралось бы прежним. Панель
        // обновится сама, когда редактор опубликует новый SelectionBounds.
    }

    /// <summary>Разбирает Margin: у Thickness нет TryParse, а Parse бросает.</summary>
    private static bool TryParseThickness(string? text, out Thickness thickness)
    {
        thickness = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            thickness = Thickness.Parse(text);
            return true;
        }
        catch (FormatException)
        {
            // Нечитаемое значение не применяется: следующее обновление вернёт прежнее.
            return false;
        }
    }

    private void ApplyCorner(Control target)
    {
        if (!TryParse(CornerBox.Text, out var radius))
            return;

        var corner = new CornerRadius(Math.Max(0, radius));
        switch (target)
        {
            case Border border:
                border.CornerRadius = corner;
                break;

            case TemplatedControl templated:
                templated.CornerRadius = corner;
                break;
        }
    }

    private void ApplyBackground(Control target)
    {
        var text = BackgroundBox.Text;
        if (string.IsNullOrWhiteSpace(text) || !Color.TryParse(text, out var color))
            return;

        var brush = new SolidColorBrush(color);
        switch (target)
        {
            case Border border:
                border.Background = brush;
                break;

            case Panel panel:
                panel.Background = brush;
                break;

            case TemplatedControl templated:
                templated.Background = brush;
                break;
        }
    }

    private void ToggleGestures_OnClick(object? sender, RoutedEventArgs e) =>
        IsGestureHelpExpanded = !IsGestureHelpExpanded;

    private void BringToFront_OnClick(object? sender, RoutedEventArgs e) => _attached?.BringToFront();

    private void BringForward_OnClick(object? sender, RoutedEventArgs e) => _attached?.BringForward();

    private void SendBackward_OnClick(object? sender, RoutedEventArgs e) => _attached?.SendBackward();

    private void SendToBack_OnClick(object? sender, RoutedEventArgs e) => _attached?.SendToBack();
}
