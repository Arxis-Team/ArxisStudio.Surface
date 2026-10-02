using System;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArxisStudio.Surface;

// Стоп-кадр: снимок холста поверх живого содержимого и отказ от жестов, пока хост его перестраивает.
// Часть SurfaceView; общее описание типа — в SurfaceView.cs.
public partial class SurfaceView
{
    /// <summary>Часть шаблона, которая показывает стоп-кадр.</summary>
    private const string FreezeLayerPart = "PART_FreezeLayer";

    /// <summary>
    /// Идентификатор свойства <see cref="IsFrozen"/>.
    /// </summary>
    public static readonly DirectProperty<SurfaceView, bool> IsFrozenProperty =
        AvaloniaProperty.RegisterDirect<SurfaceView, bool>(nameof(IsFrozen), o => o.IsFrozen);

    private bool _isFrozen;
    private int _freezeCount;
    private Image? _freezeLayer;
    private RenderTargetBitmap? _frame;

    /// <summary>
    /// Получает признак того, что холст стоит на стоп-кадре (<see cref="Freeze"/>).
    /// </summary>
    public bool IsFrozen
    {
        get => _isFrozen;
        private set => SetAndRaise(IsFrozenProperty, ref _isFrozen, value);
    }

    /// <summary>
    /// Останавливает холст на кадре, пока результат не освобождён: показывает снимок того, что видно
    /// сейчас, и не берёт новых жестов.
    /// </summary>
    /// <returns>Отпускание стоп-кадра. Повторное освобождение ничего не делает.</returns>
    /// <remarks>
    /// <para>
    /// Для хоста, который перестраивает содержимое холста целиком — снимает формы старой сборки и ставит
    /// формы новой (ADR 0023). Без стоп-кадра холст на это время пустеет и мигает, а щелчок, пришедший в
    /// середину, попадает в наполовину собранное.
    /// </para>
    /// <para>
    /// Кадр — пиксели, а не контролы: <see cref="RenderTargetBitmap"/> части шаблона, показанный в
    /// <c>PART_FreezeLayer</c> поверх всех слоёв. Живые контролы кадр не держит, поэтому хост может
    /// отпустить старое содержимое и доказать, что оно собрано, пока кадр на экране.
    /// </para>
    /// <para>
    /// Не берутся нажатие, колесо, щипок и клавиатурные команды. Начатый жест доходит до конца:
    /// движение и отпускание проходят, иначе жест остался бы висеть. Клавиша, которую холст не взял,
    /// уходит дальше — окну. Шаблон без <c>PART_FreezeLayer</c> кадра не показывает, но жесты так же не
    /// берёт.
    /// </para>
    /// <para>
    /// Вызовы вкладываются: кадр снимается первым и убирается последним освобождением. Звать и
    /// освобождать — из потока интерфейса.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Вызов не из потока интерфейса.</exception>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// using (view.Freeze())
    /// {
    ///     await host.ReplaceFormsAsync();   // старое снято, новое поставлено — под кадром
    /// }
    /// ]]></code>
    /// </example>
    public IDisposable Freeze()
    {
        Dispatcher.UIThread.VerifyAccess();

        if (_freezeCount++ == 0)
        {
            ShowFrame();
            IsFrozen = true;
        }

        return new Thaw(this);
    }

    /// <summary>
    /// Отпускает один стоп-кадр; последний убирает снимок и возвращает жесты.
    /// </summary>
    private void Unfreeze()
    {
        Dispatcher.UIThread.VerifyAccess();

        if (--_freezeCount > 0)
            return;

        HideFrame();
        IsFrozen = false;
    }

    /// <summary>
    /// Снимает то, что холст показывает сейчас, и ставит снимок поверх.
    /// </summary>
    /// <remarks>
    /// Снимается корень шаблона, а не сам холст: он стоит в начале координат холста, и снимок не
    /// зависит от того, учитывает ли отрисовка положение снимаемого в его родителе. Слой кадра в этот
    /// момент спрятан и в снимок не попадает.
    /// </remarks>
    private void ShowFrame()
    {
        if (_freezeLayer is not { } layer
            || this.GetVisualChildren().FirstOrDefault() is not Visual root
            || Bounds.Width < 1
            || Bounds.Height < 1)
        {
            return;
        }

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var pixels = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(Bounds.Width * scaling)),
            Math.Max(1, (int)Math.Ceiling(Bounds.Height * scaling)));

        var frame = new RenderTargetBitmap(pixels, new Vector(96 * scaling, 96 * scaling));
        frame.Render(root);

        _frame = frame;
        layer.Source = frame;
        layer.IsVisible = true;
    }

    private void HideFrame()
    {
        if (_freezeLayer is { } layer)
        {
            layer.Source = null;
            layer.IsVisible = false;
        }

        _frame?.Dispose();
        _frame = null;
    }

    /// <summary>
    /// Находит слой кадра в шаблоне; холсту, стоящему на кадре, переносит снимок на новый слой.
    /// </summary>
    private void FindFreezeLayer(INameScope scope)
    {
        if (_freezeLayer is { } previous)
        {
            previous.Source = null;
            previous.IsVisible = false;
        }

        _freezeLayer = scope.Find<Image>(FreezeLayerPart);

        if (_freezeLayer is { } layer && _frame is { } frame)
        {
            layer.Source = frame;
            layer.IsVisible = true;
        }
    }

    /// <summary>Не даёт нажатию на стоп-кадре дойти ни до холста, ни до содержимого.</summary>
    /// <remarks>
    /// Туннелем и первым из обработчиков холста: так нажатие не достаётся ни контейнерам, ни службам
    /// инструментов, ни упрощённому виду, которые слушают его там же.
    /// </remarks>
    private void OnFrozenPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsFrozen)
            e.Handled = true;
    }

    /// <summary>Не даёт колесу на стоп-кадре ни масштабировать холст, ни уйти наружу.</summary>
    private void OnFrozenPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (IsFrozen)
            e.Handled = true;
    }

    /// <summary>Освобождение стоп-кадра: срабатывает один раз.</summary>
    private sealed class Thaw(SurfaceView view) : IDisposable
    {
        private SurfaceView? _view = view;

        public void Dispose() => Interlocked.Exchange(ref _view, null)?.Unfreeze();
    }
}
