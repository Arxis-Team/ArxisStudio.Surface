using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Mixins;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ArxisStudio.Surface;

namespace ArxisStudio.Surface.UiDesigner;

/// <summary>
/// Представляет контейнер элемента редактора с поддержкой выделения,
/// перетаскивания и изменения размеров.
/// </summary>
/// <remarks>
/// Обычно экземпляры создаются автоматически <see cref="UiDesignerView"/>
/// как контейнеры для элементов <see cref="ItemsControl.ItemsSource"/>.
/// </remarks>
/// <example>
/// <code language="xml"><![CDATA[
/// <Style Selector="design|UiDesignerItem">
///     <Setter Property="Location" Value="{Binding Location, Mode=TwoWay}" />
///     <Setter Property="Width" Value="{Binding Width, Mode=TwoWay}" />
///     <Setter Property="Height" Value="{Binding Height, Mode=TwoWay}" />
/// </Style>
/// ]]></code>
/// </example>
[TemplatePart("PART_Border", typeof(Border))]
public class UiDesignerItem : SurfaceItem
{
    #region Fields
    private bool _isUpdatingLocation;
    #endregion

    #region Standard Properties

    /// <summary>
    /// Идентификатор свойства режима содержимого.
    /// </summary>
    public static readonly StyledProperty<SurfaceContentMode> ContentModeProperty =
        AvaloniaProperty.Register<UiDesignerItem, SurfaceContentMode>(
            nameof(ContentMode), SurfaceContentMode.Annotated);

    /// <summary>
    /// Получает или задает способ поиска редактируемых элементов внутри контейнера.
    /// </summary>
    /// <remarks>
    /// По умолчанию <see cref="SurfaceContentMode.Annotated"/> — совместимо с шаблонами,
    /// размеченными вручную. Для формы, загруженной из <c>.axaml</c>, задайте
    /// <see cref="SurfaceContentMode.Loaded"/>: контейнер сам погасит ввод и откроет
    /// на редактирование всё дерево разметки.
    /// </remarks>
    public SurfaceContentMode ContentMode
    {
        get => GetValue(ContentModeProperty);
        set => SetValue(ContentModeProperty, value);
    }

    #endregion

    static UiDesignerItem()
    {
        Layout.XProperty.Changed.AddClassHandler<UiDesignerItem>((item, _) => item.SyncLocationFromLayout());
        Layout.YProperty.Changed.AddClassHandler<UiDesignerItem>((item, _) => item.SyncLocationFromLayout());
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="UiDesignerItem"/>.
    /// </summary>
    public UiDesignerItem()
    {
        // Всплытием, и обработанное тоже: фокус уходит в форму и тогда, когда содержимое само зовёт
        // Focus() — из своего Loaded, из обработчика, — и об этом никто, кроме контейнера, не узнает.
        AddHandler(GettingFocusEvent, OnGettingFocus, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>
    /// Часть, в которой показано содержимое: в режиме <see cref="SurfaceContentMode.Loaded"/> фокус дальше
    /// неё не заходит.
    /// </summary>
    /// <remarks>
    /// У обычного контейнера это презентер содержимого. Наследник, у которого форма стоит в своей части
    /// шаблона, называет здесь её (ADR 0027): иначе фокус проходил бы мимо границы.
    /// </remarks>
    internal virtual Visual? ContentHost => Presenter;

    /// <summary>
    /// Не даёт загруженной форме взять клавиатуру.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Указатель до формы не доходит: его гасит тема на презентере. Клавиатура доходила — Tab гулял по
    /// полям и кнопкам макета, набранный текст шёл в его поля, а Delete и Ctrl+A редактора съедало поле,
    /// в котором стояла каретка (ADR 0027). Обход Tab тема снимает той же записью, что и попадание, а
    /// фокус, пришедший иначе — программно, из кода самого контрола, — отменяется здесь.
    /// </para>
    /// <para>
    /// Свои части контейнера — панель инструментов над ним — фокус брать могут: граница проходит по
    /// <see cref="ContentHost"/>, а не по контейнеру.
    /// </para>
    /// </remarks>
    private void OnGettingFocus(object? sender, FocusChangingEventArgs e)
    {
        if (ContentMode != SurfaceContentMode.Loaded
            || ContentHost is not { } host
            || e.NewFocusedElement is not Visual target
            || !host.IsVisualAncestorOf(target))
        {
            return;
        }

        // Отменить можно не всякую перемену фокуса; неотменимую уводят в сам контейнер — он в фокусе
        // ничего не печатает, а редактор над ним клавиатуру слышит.
        if (!e.TryCancel())
            e.TrySetNewFocusedElement(this);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Без редактора жест пишет <see cref="SurfaceItem.Location"/>, которую читает
    /// лишь <see cref="AbsolutePanel"/>.
    /// </remarks>
    internal override bool CanMoveWithoutSurface => this.GetVisualParent() is AbsolutePanel;

    /// <summary>
    /// Корень авторской разметки: с него редактор в режиме <see cref="SurfaceContentMode.Loaded"/>
    /// начинает обход редактируемых элементов.
    /// </summary>
    /// <remarks>
    /// У обычного контейнера это то, что построил презентер, а до шаблона — само содержимое. Наследник,
    /// у которого между контейнером и формой лежат его собственные служебные элементы, называет здесь
    /// форму (ADR 0020): иначе выбираемыми стали бы они.
    /// </remarks>
    internal virtual Control? AuthoredRoot =>
        (Presenter as Control)?.GetVisualChildren().OfType<Control>().FirstOrDefault() ?? Content as Control;

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

    #endregion
}
