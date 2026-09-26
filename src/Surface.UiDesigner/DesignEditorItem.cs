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
public class DesignEditorItem : SurfaceItem
{
    #region Fields
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
    }

    /// <inheritdoc />
    /// <remarks>
    /// Без редактора жест пишет <see cref="SurfaceItem.Location"/>, которую читает
    /// лишь <see cref="AbsolutePanel"/>.
    /// </remarks>
    internal override bool CanMoveWithoutSurface => this.GetVisualParent() is AbsolutePanel;

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
