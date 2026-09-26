using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Mixins;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using ArxisStudio.States;
using ArxisStudio.Surface;

namespace ArxisStudio.Surface.UiDesigner;

/// <summary>
/// Представляет контейнер элемента редактора с поддержкой выделения,
/// перетаскивания и изменения размеров.
/// </summary>
/// <remarks>
/// Обычно экземпляры создаются автоматически <see cref="DesignEditor"/>
/// как контейнеры для элементов <see cref="ItemsControl.ItemsSource"/>.
/// </remarks>
/// <example>
/// <code language="xml"><![CDATA[
/// <Style Selector="design|DesignEditorItem">
///     <Setter Property="Location" Value="{Binding Location, Mode=TwoWay}" />
///     <Setter Property="Width" Value="{Binding Width, Mode=TwoWay}" />
///     <Setter Property="Height" Value="{Binding Height, Mode=TwoWay}" />
/// </Style>
/// ]]></code>
/// </example>
[TemplatePart("PART_Border", typeof(Border))]
[PseudoClasses(":dragging", ":resizing")]
public class DesignEditorItem : SurfaceItem
{
    #region Fields
    private readonly Stack<DesignEditorItemState> _states = new();
    private bool _isUpdatingLocation;
    #endregion

    #region Standard Properties

    /// <summary>
    /// Идентификатор свойства режима содержимого.
    /// </summary>
    public static readonly StyledProperty<DesignContentMode> ContentModeProperty =
        AvaloniaProperty.Register<DesignEditorItem, DesignContentMode>(
            nameof(ContentMode), DesignContentMode.Annotated);

    /// <summary>
    /// Получает или задает способ поиска редактируемых элементов внутри контейнера.
    /// </summary>
    /// <remarks>
    /// По умолчанию <see cref="DesignContentMode.Annotated"/> — совместимо с шаблонами,
    /// размеченными вручную. Для формы, загруженной из <c>.axaml</c>, задайте
    /// <see cref="DesignContentMode.Loaded"/>: контейнер сам погасит ввод и откроет
    /// на редактирование всё дерево разметки.
    /// </remarks>
    public DesignContentMode ContentMode
    {
        get => GetValue(ContentModeProperty);
        set => SetValue(ContentModeProperty, value);
    }

    #endregion

    /// <summary>
    /// Получает текущее состояние контейнера.
    /// </summary>
    internal DesignEditorItemState CurrentState => _states.Count > 0 ? _states.Peek() : null!;

    static DesignEditorItem()
    {
        Layout.XProperty.Changed.AddClassHandler<DesignEditorItem>((item, _) => item.SyncLocationFromLayout());
        Layout.YProperty.Changed.AddClassHandler<DesignEditorItem>((item, _) => item.SyncLocationFromLayout());
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignEditorItem"/>.
    /// </summary>
    public DesignEditorItem()
    {
        _states.Push(new ItemIdleState(this));
    }

    /// <summary>
    /// Реагирует на изменение свойств контейнера и переносит <see cref="SurfaceItem.Location"/>
    /// в attached-свойства <c>Layout.X</c>/<c>Layout.Y</c>.
    /// </summary>
    /// <param name="change">Аргументы изменения свойства.</param>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LocationProperty)
        {
            if (_isUpdatingLocation)
                return;

            try
            {
                _isUpdatingLocation = true;
                Layout.SetX(this, Location.X);
                Layout.SetY(this, Location.Y);
            }
            finally
            {
                _isUpdatingLocation = false;
            }
        }
    }

    private void SyncLocationFromLayout()
    {
        if (_isUpdatingLocation)
            return;

        var x = Layout.GetX(this);
        var y = Layout.GetY(this);

        if (double.IsNaN(x) && double.IsNaN(y))
            return;

        var nextLocation = new Point(
            double.IsNaN(x) ? Location.X : x,
            double.IsNaN(y) ? Location.Y : y);

        if (Math.Abs(nextLocation.X - Location.X) < 0.01 &&
            Math.Abs(nextLocation.Y - Location.Y) < 0.01)
            return;

        try
        {
            _isUpdatingLocation = true;
            SetCurrentValue(LocationProperty, nextLocation);
        }
        finally
        {
            _isUpdatingLocation = false;
        }
    }

    #region State Machine Management

    /// <summary>
    /// Помещает новое состояние контейнера в стек и делает его активным.
    /// </summary>
    /// <param name="state">Новое состояние.</param>
    internal void PushState(DesignEditorItemState state)
    {
        var previous = CurrentState;
        _states.Push(state);
        state.Enter(previous);
        UpdatePseudoClassesState(state);
    }

    /// <summary>
    /// Завершает текущее состояние контейнера и возвращается к предыдущему.
    /// </summary>
    internal void PopState()
    {
        if (_states.Count > 1)
        {
            var current = _states.Pop();
            current.Exit();
            CurrentState.ReEnter(current);
            UpdatePseudoClassesState(CurrentState);
        }
    }

    private void UpdatePseudoClassesState(DesignEditorItemState state)
    {
        PseudoClasses.Set(":dragging", state is ItemDraggingState);
        PseudoClasses.Set(":resizing", state is ItemResizingState);
    }

    #endregion

    /// <summary>
    /// Передает событие нажатия указателя в текущее состояние контейнера.
    /// </summary>
    /// <param name="e">Аргументы указателя.</param>
    protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); if (!e.Handled) CurrentState.OnPointerPressed(e); }

    /// <summary>
    /// Передает событие перемещения указателя в текущее состояние контейнера.
    /// </summary>
    /// <param name="e">Аргументы указателя.</param>
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); CurrentState.OnPointerMoved(e); }

    /// <summary>
    /// Передает событие отпускания указателя в текущее состояние контейнера.
    /// </summary>
    /// <param name="e">Аргументы указателя.</param>
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); CurrentState.OnPointerReleased(e); }

    /// <summary>
    /// Сбрасывает вложенные состояния, если контейнер теряет захват указателя.
    /// </summary>
    /// <param name="e">Аргументы потери захвата указателя.</param>
    /// <inheritdoc />
    /// <remarks>
    /// Стек разбирается до базового состояния, а базовому о брошенном жесте говорится
    /// отдельно: снять его нечем, а нажатие, за которым не последует отпускания,
    /// иначе осталось бы у него записанным.
    /// </remarks>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        while (_states.Count > 1)
            PopState();

        CurrentState.OnPointerCaptureLost();
    }
}
