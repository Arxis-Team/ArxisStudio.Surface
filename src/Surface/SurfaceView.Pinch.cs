using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ArxisStudio.Surface;

// Масштабирование щипком: тачпад и сенсорный экран.
// Часть SurfaceView; общее описание типа — в SurfaceView.cs.
public partial class SurfaceView
{
    // Масштаб и точка холста под пальцами в начале щипка. Щипок пальцами сообщает
    // масштаб накопленным от начала жеста, поэтому считать надо от них, а не от
    // текущего масштаба: иначе каждое событие умножало бы уже умноженное.
    private double _pinchStartZoom;
    private Point _pinchWorldAnchor;
    private bool _isPinching;

    /// <summary>
    /// Подключает оба источника щипка.
    /// </summary>
    /// <remarks>
    /// Щипок тачпада приходит готовым событием указателя — на macOS; на Windows
    /// точный тачпад присылает его колесом с <c>Ctrl</c>, и его обслуживает масштаб
    /// колесом. Щипок пальцами распознаёт <see cref="PinchGestureRecognizer"/>: только
    /// касание и перо, мышь он не слушает.
    /// </remarks>
    private void AttachPinchZoom()
    {
        AddHandler(PointerTouchPadGestureMagnifyEvent, OnTouchPadMagnify);
        GestureRecognizers.Add(new PinchGestureRecognizer());
        AddHandler(PinchEvent, OnPinch);
        AddHandler(PinchEndedEvent, OnPinchEnded);
    }

    /// <summary>
    /// Щипок тачпада: приращение масштаба за событие.
    /// </summary>
    /// <remarks>
    /// Платформа кладёт в <c>Delta</c> долю, на которую изменился масштаб с прошлого
    /// события (на macOS — <c>magnification</c> в обе составляющие), поэтому новый
    /// масштаб — текущий, умноженный на <c>1 + Δ</c>.
    /// </remarks>
    private void OnTouchPadMagnify(object? sender, PointerDeltaEventArgs e)
    {
        if (!InteractionOptions.IsPinchZoomEnabled || IsFrozen)
            return;

        var factor = 1 + e.Delta.X;
        if (factor <= 0 || double.IsNaN(factor))
            return;

        ZoomAt(ViewportZoom * factor, e.GetPosition(this));

        // Как и колесо у края диапазона: жест был наш, и отдавать его наружу незачем.
        e.Handled = true;
    }

    /// <summary>
    /// Щипок пальцами: масштаб от начала жеста и точка между пальцами.
    /// </summary>
    /// <remarks>
    /// Точка холста, оказавшаяся между пальцами в начале щипка, остаётся под ними весь
    /// жест. Так щипок заодно и панорамирует: пальцы, сдвинутые вместе, ведут холст за
    /// собой — привычное по картам и Figma поведение, и отдельного жеста под него не нужно.
    /// </remarks>
    private void OnPinch(object? sender, PinchEventArgs e)
    {
        // Распознаватель видит касания и тогда, когда нажатие взял стоп-кадр.
        if (!InteractionOptions.IsPinchZoomEnabled || IsFrozen)
            return;

        if (!_isPinching)
        {
            _isPinching = true;
            _pinchStartZoom = ViewportZoom;
            _pinchWorldAnchor = GetWorldPosition(e.ScaleOrigin);
            UpdateIsInteracting();
        }

        if (e.Scale > 0 && !double.IsNaN(e.Scale))
        {
            var zoom = Math.Max(MinZoom, Math.Min(MaxZoom, _pinchStartZoom * e.Scale));
            ViewportZoom = zoom;
            ViewportLocation = _pinchWorldAnchor - (Vector)e.ScaleOrigin / zoom;
        }

        e.Handled = true;
    }

    private void OnPinchEnded(object? sender, PinchEndedEventArgs e)
    {
        _isPinching = false;
        UpdateIsInteracting();
    }
}
