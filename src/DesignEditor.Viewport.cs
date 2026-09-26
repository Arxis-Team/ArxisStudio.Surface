using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DesignLayout = ArxisStudio.Surface.UiDesigner.Layout;
using DesignInteraction = ArxisStudio.Surface.Editing.DesignInteraction;
using ArxisStudio.Controls;
using ArxisStudio.Surface.Editing;
using ArxisStudio.Surface.UiDesigner.Placement;
using ArxisStudio.States;

namespace ArxisStudio;

// Панорамирование, зум и трансформации viewport'а.
// Часть DesignEditor; общее описание типа — в DesignEditor.cs.
public partial class DesignEditor
{
    /// <summary>
    /// Подключает обработчики, зависящие от visual tree, после присоединения редактора к дереву.
    /// </summary>
    /// <param name="e">Аргументы присоединения к visual tree.</param>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // e.RootVisual в Avalonia 12 не гарантированно TopLevel, поэтому host ищется
        // подъемом по дереву, а не приведением корня.
        SetScalingHost(TopLevel.GetTopLevel(this));
        UpdateTransforms();
    }

    /// <summary>
    /// Освобождает обработчики, зависящие от visual tree, перед отсоединением редактора.
    /// </summary>
    /// <param name="e">Аргументы отсоединения от visual tree.</param>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        SetScalingHost(null);
    }

    private void SetScalingHost(TopLevel? topLevel)
    {
        if (ReferenceEquals(_scalingHost, topLevel))
            return;

        if (_scalingHost != null)
            _scalingHost.ScalingChanged -= OnScreenScalingChanged;

        _scalingHost = topLevel;

        if (_scalingHost != null)
            _scalingHost.ScalingChanged += OnScreenScalingChanged;
    }

    private void OnScreenScalingChanged(object? sender, EventArgs e) => UpdateTransforms();

    private void UpdateTransforms()
    {
        _scaleTransform.ScaleX = ViewportZoom;
        _scaleTransform.ScaleY = ViewportZoom;

        double x = -ViewportLocation.X * ViewportZoom;
        double y = -ViewportLocation.Y * ViewportZoom;

        _translateTransform.X = x;
        _translateTransform.Y = y;

        // В Avalonia 12 IRenderRoot больше не публичный: RenderScaling берется с TopLevel.
        // Проверка через "не > 0" отсекает и 0, и NaN: иначе деление ниже дало бы
        // нечисловой transform для фона и сетки.
        double renderScaling = _scalingHost?.RenderScaling ?? 1.0;
        if (!(renderScaling > 0))
            renderScaling = 1.0;

        _dpiTranslateTransform.X = Math.Round(x * renderScaling) / renderScaling;
        _dpiTranslateTransform.Y = Math.Round(y * renderScaling) / renderScaling;

        // Группы собраны в конструкторе и содержат эти же трансформации, поэтому
        // мутации выше видны через них сразу. Пересобирать их заново на каждом кадре
        // приходилось только ради обратного масштаба оверлеев: тот конвертер снимал
        // матрицу в момент преобразования и перевычислялся лишь при смене
        // идентичности значения. Теперь оверлеи привязаны к ViewportZoom напрямую.
    }

    /// <summary>
    /// Преобразует экранную точку в мировые координаты холста.
    /// </summary>
    /// <param name="screenPoint">Точка в координатах контрола.</param>
    /// <returns>Точка в координатах содержимого редактора.</returns>
    /// <example>
    /// Это полезно, когда нужно разместить новый элемент в позиции курсора с учетом текущего зума и панорамирования.
    /// </example>
    public Point GetWorldPosition(Point screenPoint)
        => (screenPoint / ViewportZoom) + ViewportLocation;

    /// <summary>
    /// Возвращает последнюю известную позицию указателя для текущего ввода.
    /// </summary>
    /// <param name="relativeTo">Параметр сохранен для совместимости с будущими реализациями.</param>
    /// <returns>Последняя позиция указателя в координатах редактора.</returns>
    public Point GetPositionForInput(Visual relativeTo)
        => _lastMousePosition;

    /// <summary>
    /// Выполняет масштабирование относительно текущей позиции курсора.
    /// </summary>
    /// <param name="e">Аргументы колесика мыши.</param>
    public void HandleZoom(PointerWheelEventArgs e) => TryHandleZoom(e);

    /// <summary>
    /// Масштабирует viewport и сообщает, состоялось ли масштабирование.
    /// </summary>
    /// <remarks>
    /// Публичный <see cref="HandleZoom"/> остаётся void ради совместимости;
    /// ответ нужен только редактору, чтобы решить судьбу <c>e.Handled</c>.
    /// </remarks>
    internal bool TryHandleZoom(PointerWheelEventArgs e)
    {
        if (!ShouldHandleZoom(e.KeyModifiers))
            return false;

        var zoomStep = InteractionOptions.ZoomStep > 1.0 ? InteractionOptions.ZoomStep : 1.1;
        double prevZoom = ViewportZoom;
        double newZoom = e.Delta.Y > 0 ? prevZoom * zoomStep : prevZoom / zoomStep;
        newZoom = Math.Max(GetValue(MinZoomProperty), Math.Min(GetValue(MaxZoomProperty), newZoom));

        if (Math.Abs(newZoom - prevZoom) > ZoomTolerance)
        {
            Point mousePos = e.GetPosition(this);
            Vector correction = (Vector)mousePos / prevZoom - (Vector)mousePos / newZoom;
            ViewportZoom = newZoom;
            ViewportLocation += correction;
        }

        // Колесо потреблено даже когда масштаб упёрся в Min/Max: жест был наш,
        // и отдавать его наружу на границе диапазона значило бы, что у края
        // зума страница вдруг начинает прокручиваться.
        return true;
    }

    /// <summary>
    /// Смещает viewport так, чтобы указанная мировая точка оказалась в центре видимой области редактора.
    /// </summary>
    /// <param name="worldPoint">Точка в координатах содержимого редактора.</param>
    /// <remarks>
    /// Метод не изменяет <see cref="ViewportZoom"/> и пересчитывает только <see cref="ViewportLocation"/>.
    /// </remarks>
    public void CenterOn(Point worldPoint)
    {
        var visibleWorldSize = new Size(Bounds.Width / ViewportZoom, Bounds.Height / ViewportZoom);
        ViewportLocation = new Point(
            worldPoint.X - (visibleWorldSize.Width / 2),
            worldPoint.Y - (visibleWorldSize.Height / 2));
    }

    /// <summary>
    /// Смещает viewport так, чтобы центр указанной области оказался в центре видимой области редактора.
    /// </summary>
    /// <param name="bounds">Прямоугольная область в мировых координатах.</param>
    /// <remarks>
    /// Метод не изменяет <see cref="ViewportZoom"/> и использует геометрический центр переданного прямоугольника.
    /// </remarks>
    public void CenterOn(Rect bounds)
    {
        CenterOn(bounds.Center);
    }

    /// <summary>
    /// Смещает viewport так, чтобы указанный элемент оказался в центре видимой области редактора.
    /// </summary>
    /// <param name="item">Элемент, который необходимо центрировать в области просмотра.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="item"/> равен <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Выбрасывается, если <paramref name="item"/> не принадлежит текущему экземпляру <see cref="DesignEditor"/>.
    /// </exception>
    /// <remarks>
    /// Метод изменяет только <see cref="ViewportLocation"/> и не изменяет <see cref="ViewportZoom"/>.
    /// <para>
    /// Если размер элемента превышает размер видимой области, элемент не масштабируется и не вписывается целиком:
    /// в центр видимой области помещается только геометрический центр элемента.
    /// </para>
    /// <para>
    /// Метод использует текущие <see cref="DesignEditorItem.Location"/> и <see cref="Visual.Bounds"/> элемента.
    /// Для корректного результата элемент должен принадлежать текущему редактору и иметь актуальный layout.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// editor.CenterOnItem(container);
    /// ]]></code>
    /// </example>
    public void CenterOnItem(DesignEditorItem item)
    {
        if (item == null)
            throw new ArgumentNullException(nameof(item));

        if (!ReferenceEquals(item.FindAncestorOfType<DesignEditor>(), this))
            throw new InvalidOperationException("The specified item does not belong to this DesignEditor.");

        if (TryGetDesignBounds(item, out var bounds))
        {
            CenterOn(bounds.Center);
            return;
        }

        var fallbackCenter = new Point(
            item.Location.X + (item.Bounds.Width / 2),
            item.Location.Y + (item.Bounds.Height / 2));

        CenterOn(fallbackCenter);
    }

    /// <summary>
    /// Изменяет положение и масштаб viewport так, чтобы указанная область целиком поместилась в видимой области редактора.
    /// </summary>
    /// <param name="bounds">Прямоугольная область в мировых координатах, которую необходимо вписать в окно.</param>
    /// <remarks>
    /// Метод изменяет <see cref="ViewportLocation"/> и <see cref="ViewportZoom"/>.
    /// <para>
    /// Для более аккуратного отображения вокруг области добавляется внутренний отступ.
    /// Итоговый масштаб ограничивается значениями <see cref="MinZoom"/> и <see cref="MaxZoom"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// editor.FitToView(new Rect(100, 100, 640, 360));
    /// ]]></code>
    /// </example>
    public void FitToView(Rect bounds)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var paddedBounds = bounds.Inflate(FitToViewPadding);
        var targetWidth = Math.Max(1.0, paddedBounds.Width);
        var targetHeight = Math.Max(1.0, paddedBounds.Height);

        var zoomX = Bounds.Width / targetWidth;
        var zoomY = Bounds.Height / targetHeight;
        var newZoom = Math.Min(zoomX, zoomY);
        newZoom = Math.Max(MinZoom, Math.Min(MaxZoom, newZoom));

        ViewportZoom = newZoom;
        CenterOn(paddedBounds.Center);
    }

    /// <summary>
    /// Изменяет положение и масштаб viewport так, чтобы указанный элемент целиком поместился в видимой области редактора.
    /// </summary>
    /// <param name="item">Элемент, который необходимо вписать в окно редактора.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="item"/> равен <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Выбрасывается, если <paramref name="item"/> не принадлежит текущему экземпляру <see cref="DesignEditor"/>.
    /// </exception>
    /// <remarks>
    /// Метод использует текущие <see cref="DesignEditorItem.Location"/> и <see cref="Visual.Bounds"/> элемента
    /// и делегирует расчет геометрии перегрузке <see cref="FitToView(Rect)"/>.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// editor.FitToView(container);
    /// ]]></code>
    /// </example>
    public void FitToView(DesignEditorItem item)
    {
        if (item == null)
            throw new ArgumentNullException(nameof(item));

        if (!ReferenceEquals(item.FindAncestorOfType<DesignEditor>(), this))
            throw new InvalidOperationException("The specified item does not belong to this DesignEditor.");

        if (TryGetDesignBounds(item, out var bounds))
        {
            FitToView(bounds);
            return;
        }

        FitToView(new Rect(item.Location, item.Bounds.Size));
    }

    /// <summary>
    /// Смещает viewport так, чтобы центр области, охватывающей все выбранные элементы, оказался в центре видимой области редактора.
    /// </summary>
    /// <remarks>
    /// Метод не изменяет <see cref="ViewportZoom"/>. Если в редакторе нет выбранных элементов, вызов игнорируется.
    /// </remarks>
    public void CenterOnSelection()
    {
        if (TryGetSelectedDesignBounds(out var bounds, out _, out _, out _, out _, out _, out _, out _))
            CenterOn(bounds);
    }

    /// <summary>
    /// Изменяет положение и масштаб viewport так, чтобы все выбранные элементы целиком поместились в видимой области редактора.
    /// </summary>
    /// <remarks>
    /// Если в редакторе нет выбранных элементов, вызов игнорируется.
    /// </remarks>
    public void FitSelectionToView()
    {
        if (TryGetSelectedDesignBounds(out var bounds, out _, out _, out _, out _, out _, out _, out _))
            FitToView(bounds);
    }
}
