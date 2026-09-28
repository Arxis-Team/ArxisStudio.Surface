using System;
using System.Collections.Generic;
using ArxisStudio.Surface.Editing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering.SceneGraph;
using SkiaSharp;

namespace ArxisStudio.Surface.Nodes;

/// <summary>
/// Связи упрощённого вида: кривые записей без контролов, когда масштаб ниже
/// <see cref="SurfaceView.SimplifiedZoom"/> (ADR 0008).
/// </summary>
/// <remarks>
/// Стоит в шаблоне редактора под панелью связей, во весь его размер, и переводит мир в экран
/// трансформацией viewport сам — как слой карточек ядра, и по той же причине: слой без размера под
/// трансформацией рендерер отсекает. Ниже порога контрол есть только у закреплённой связи и у связи
/// развёрнутого узла; остальные слой рисует сам, выбранные — поверх, своей кистью. Снимок кривых с
/// сеткой ячеек по их рамкам держится, пока не сменится запись без контрола, состав развёрнутых или
/// выбор, и над порогом не держится. Толщина — в пикселях экрана.
/// <para>
/// Рисует слой своей операцией, а не геометрией (ADR 0011): композитор меряет границы каждой новой
/// записи геометрии, и кривые десяти тысяч связей на каждом кадре панорамы стоили сотни миллисекунд.
/// Операция берёт из сетки только видимые связи и рисует каждую ломаной, отрезков — по её длине на
/// экране (<see cref="CurveSegments"/>); связь короче пикселя не рисуется. Холст Skia из аренды рисует их
/// одним путём на перо (ADR 0012).
/// </para>
/// </remarks>
internal sealed class SimplifiedLinkLayer : Control
{
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<SimplifiedLinkLayer, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> SelectedStrokeProperty =
        AvaloniaProperty.Register<SimplifiedLinkLayer, IBrush?>(nameof(SelectedStroke));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<SimplifiedLinkLayer, double>(nameof(StrokeThickness), 1);

    /// <summary>
    /// Номер кисти у записи снимка: кисть темы.
    /// </summary>
    private const int ThemeStroke = -1;

    /// <summary>
    /// Номер кисти у записи снимка: кисть выбора.
    /// </summary>
    private const int SelectedStrokeIndex = -2;

    private NodeEditor? _editor;
    private Snapshot? _snapshot;
    private bool _stale = true;

    static SimplifiedLinkLayer()
    {
        AffectsRender<SimplifiedLinkLayer>(StrokeProperty, SelectedStrokeProperty, StrokeThicknessProperty);
    }

    /// <summary>
    /// Кисть связи.
    /// </summary>
    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>
    /// Кисть выбранной связи.
    /// </summary>
    public IBrush? SelectedStroke
    {
        get => GetValue(SelectedStrokeProperty);
        set => SetValue(SelectedStrokeProperty, value);
    }

    /// <summary>
    /// Толщина связи в пикселях экрана.
    /// </summary>
    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>
    /// Сколько невыбранных связей в последней сборке — для тестов.
    /// </summary>
    internal int Links { get; private set; }

    /// <summary>
    /// Сколько выбранных связей в последней сборке — для тестов.
    /// </summary>
    internal int SelectedLinks { get; private set; }

    /// <summary>
    /// Сколько разных цветов модели у проводов последней сборки — для тестов.
    /// </summary>
    internal int StrokeGroups => _snapshot?.Palette.Length ?? 0;

    /// <summary>
    /// Сколько раз связи собирались заново — для тестов и стенда.
    /// </summary>
    internal int Rebuilds { get; private set; }

    /// <summary>
    /// Сколько раз связи нарисованы — для тестов.
    /// </summary>
    internal int Draws { get; private set; }

    /// <summary>
    /// Сколько связей нарисовал бы кадр сейчас и сколько в них отрезков — тем же отбором, что у
    /// операции; для тестов и стенда.
    /// </summary>
    internal (int Visible, int Segments) CountVisible()
    {
        if (_snapshot is not { } snapshot || _editor is not { } editor)
            return default;

        var (world, zoom) = Viewport(editor, out _);
        var (visible, segments) = (0, 0);
        foreach (var i in snapshot.Grid.Within(world))
        {
            var c = snapshot.Curves[i];
            visible++;
            segments += CurveSegments.Count(c.Source, c.SourceControl, c.TargetControl, c.Target, zoom);
        }

        return (visible, segments);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        if (_editor is not { IsSimplified: true } editor)
            return;

        if (_stale)
            Rebuild(editor);

        if (_snapshot is not { } snapshot)
            return;

        var (world, zoom) = Viewport(editor, out var matrix);
        context.Custom(new LinksOperation(
            new Rect(Bounds.Size), snapshot, matrix, world, zoom, StrokeThickness,
            Stroke?.ToImmutable(), SelectedStroke?.ToImmutable()));

        Draws++;
    }

    /// <summary>
    /// Рисует кадр путём Skia на данный холст — тем, каким операция рисует из аренды; для тестов.
    /// </summary>
    /// <returns><see langword="false"/>, если рисовать нечего или кисть не сплошная.</returns>
    internal bool RenderTo(SKCanvas canvas)
    {
        if (_editor is not { } editor || _snapshot is not { } snapshot)
            return false;

        var (world, zoom) = Viewport(editor, out var matrix);
        var operation = new LinksOperation(
            new Rect(Bounds.Size), snapshot, matrix, world, zoom, StrokeThickness,
            Stroke?.ToImmutable(), SelectedStroke?.ToImmutable());
        return operation.CanPaint && operation.RenderSkia(canvas, 1);
    }

    /// <summary>
    /// Видимая часть мира и масштаб — по трансформации viewport, которой слой рисует.
    /// </summary>
    private (Rect World, double Zoom) Viewport(NodeEditor editor, out Matrix matrix)
    {
        matrix = editor.ViewportTransform?.Value ?? Matrix.Identity;
        var bounds = new Rect(Bounds.Size);
        var world = matrix.TryInvert(out var inverse) ? bounds.TransformToAABB(inverse) : bounds;
        return (world, Math.Max(editor.ViewportZoom, 0.0001));
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Watch(TemplatedParent as NodeEditor);
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Watch(null);
    }

    private void Watch(NodeEditor? editor)
    {
        if (_editor != null)
        {
            _editor.SimplifiedLinksChanged -= OnLinksChanged;
            _editor.PropertyChanged -= OnEditorPropertyChanged;
        }

        _editor = editor;
        Forget();

        if (_editor != null)
        {
            _editor.SimplifiedLinksChanged += OnLinksChanged;
            _editor.PropertyChanged += OnEditorPropertyChanged;
        }

        InvalidateVisual();
    }

    private void OnLinksChanged(object? sender, EventArgs e)
    {
        _stale = true;
        if (_editor is { IsSimplified: true })
            InvalidateVisual();
    }

    private void OnEditorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == SurfaceView.IsSimplifiedProperty)
        {
            // Над порогом кривые не нужны и не держатся: на большом графе это тысячи фигур.
            Forget();
            InvalidateVisual();
        }
        else if ((e.Property == SurfaceView.ViewportZoomProperty || e.Property == SurfaceView.ViewportLocationProperty)
                 && _editor is { IsSimplified: true })
        {
            InvalidateVisual();
        }
    }

    private void Forget()
    {
        _snapshot = null;
        _stale = true;
    }

    private void Rebuild(NodeEditor editor)
    {
        _stale = false;
        Rebuilds++;

        // Провода без цвета модели — кистью темы, с цветом — номером в палитре, выбранные — кистью
        // выбора. Рамка кривой для сетки — по контрольным точкам: кривая лежит внутри их оболочки.
        var curves = new List<MinimapCurve>();
        var rects = new List<Rect>();
        var strokes = new List<int>();
        var palette = new List<IImmutableBrush>();
        Dictionary<IBrush, int>? paletteIndex = null;
        var (count, selectedCount) = (0, 0);
        foreach (var record in editor.LinkRecords)
        {
            if (record.Control != null || !record.IsResolved)
                continue;

            int stroke;
            if (record.IsSelected)
            {
                stroke = SelectedStrokeIndex;
                selectedCount++;
            }
            else
            {
                count++;
                stroke = ThemeStroke;
                if (record.Stroke is { } brush)
                {
                    paletteIndex ??= new Dictionary<IBrush, int>(ReferenceEqualityComparer.Instance);
                    if (!paletteIndex.TryGetValue(brush, out stroke))
                    {
                        stroke = palette.Count;
                        paletteIndex[brush] = stroke;
                        palette.Add(brush.ToImmutable());
                    }
                }
            }

            var g = record.Geometry;
            var curve = new MinimapCurve(g.Source, g.SourceControl, g.TargetControl, g.Target);
            curves.Add(curve);
            rects.Add(Hull(curve));
            strokes.Add(stroke);
        }

        _snapshot = curves.Count > 0
            ? new Snapshot(CellGrid.Build(rects.ToArray()), curves.ToArray(), strokes.ToArray(), selectedCount, palette.ToArray())
            : null;
        Links = count;
        SelectedLinks = selectedCount;
    }

    private static Rect Hull(MinimapCurve c)
    {
        var left = Math.Min(Math.Min(c.Source.X, c.SourceControl.X), Math.Min(c.TargetControl.X, c.Target.X));
        var top = Math.Min(Math.Min(c.Source.Y, c.SourceControl.Y), Math.Min(c.TargetControl.Y, c.Target.Y));
        var right = Math.Max(Math.Max(c.Source.X, c.SourceControl.X), Math.Max(c.TargetControl.X, c.Target.X));
        var bottom = Math.Max(Math.Max(c.Source.Y, c.SourceControl.Y), Math.Max(c.TargetControl.Y, c.Target.Y));
        return new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Связи без контролов, собранные на UI-потоке: кривые по сетке и номер кисти. Неизменяемо — его
    /// читает поток отрисовки.
    /// </summary>
    private sealed class Snapshot(CellGrid grid, MinimapCurve[] curves, int[] strokes, int selectedCount, IImmutableBrush[] palette)
    {
        public CellGrid Grid { get; } = grid;

        public MinimapCurve[] Curves { get; } = curves;

        public int[] Strokes { get; } = strokes;

        public int SelectedCount { get; } = selectedCount;

        public IImmutableBrush[] Palette { get; } = palette;
    }

    /// <summary>
    /// Кадр связей упрощённого вида: только видимые, ломаными по длине на экране.
    /// </summary>
    private sealed class LinksOperation(
        Rect bounds, Snapshot snapshot, Matrix matrix, Rect world, double zoom, double thickness,
        IImmutableBrush? stroke, IImmutableBrush? selectedStroke)
        : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        /// <summary>
        /// Все ли кисти холст из аренды нарисует.
        /// </summary>
        public bool CanPaint =>
            SkiaCanvas.CanPaint(stroke) && SkiaCanvas.CanPaint(selectedStroke) && Array.TrueForAll(snapshot.Palette, SkiaCanvas.CanPaint);

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            if (CanPaint)
            {
                using var lease = SkiaCanvas.TryLease(context);
                if (lease != null)
                {
                    RenderSkia(lease.SkCanvas, lease.CurrentOpacity);
                    return;
                }
            }

            RenderImmediate(context);
        }

        /// <summary>
        /// Путь Skia: один путь на перо — тему, каждый цвет модели и выбор.
        /// </summary>
        public bool RenderSkia(SKCanvas canvas, double opacity)
        {
            // Номер пути: 0 — тема, 1 — выбор, дальше — палитра.
            var paths = new SKPath?[snapshot.Palette.Length + 2];
            try
            {
                foreach (var i in snapshot.Grid.Within(world))
                {
                    var index = snapshot.Strokes[i] switch
                    {
                        SelectedStrokeIndex => 1,
                        ThemeStroke => 0,
                        var p => p + 2
                    };
                    var c = snapshot.Curves[i];
                    CurveSegments.Append(paths[index] ??= new SKPath(), c.Source, c.SourceControl, c.TargetControl, c.Target, zoom);
                }

                var width = thickness / zoom;
                SkiaCanvas.Enter(canvas, bounds, matrix);
                try
                {
                    Draw(canvas, paths[0], stroke, width, opacity);
                    for (var p = 0; p < snapshot.Palette.Length; p++)
                        Draw(canvas, paths[p + 2], snapshot.Palette[p], width, opacity);

                    // Выбранные — поверх, чтобы соседний провод их не закрыл.
                    Draw(canvas, paths[1], selectedStroke, width, opacity);
                }
                finally
                {
                    canvas.Restore();
                }
            }
            finally
            {
                foreach (var path in paths)
                    path?.Dispose();
            }

            return true;
        }

        private static void Draw(SKCanvas canvas, SKPath? path, IImmutableBrush? brush, double width, double opacity)
        {
            if (path == null || brush == null)
                return;

            using var paint = SkiaCanvas.Stroke(SkiaCanvas.Color(brush, opacity), width);
            canvas.DrawPath(path, paint);
        }

        /// <summary>
        /// Запасной путь: по отрезку через контекст.
        /// </summary>
        private void RenderImmediate(ImmediateDrawingContext context)
        {
            // Мир под трансформацией viewport: пиксель экрана — это 1 / zoom мировых единиц.
            var width = thickness / zoom;
            var themePen = stroke != null ? new ImmutablePen(stroke, width) : null;
            var palettePens = new ImmutablePen[snapshot.Palette.Length];
            for (var i = 0; i < palettePens.Length; i++)
                palettePens[i] = new ImmutablePen(snapshot.Palette[i], width);

            using (context.PushClip(bounds))
            using (context.PushPreTransform(matrix))
            {
                foreach (var i in snapshot.Grid.Within(world))
                {
                    var index = snapshot.Strokes[i];
                    if (index == SelectedStrokeIndex)
                        continue;

                    if ((index >= 0 ? palettePens[index] : themePen) is { } pen)
                        Draw(context, pen, snapshot.Curves[i]);
                }

                // Выбранные — поверх, чтобы соседний провод их не закрыл.
                if (selectedStroke != null && snapshot.SelectedCount > 0)
                {
                    var selectedPen = new ImmutablePen(selectedStroke, width);
                    foreach (var i in snapshot.Grid.Within(world))
                    {
                        if (snapshot.Strokes[i] == SelectedStrokeIndex)
                            Draw(context, selectedPen, snapshot.Curves[i]);
                    }
                }
            }
        }

        private void Draw(ImmediateDrawingContext context, ImmutablePen pen, MinimapCurve c) =>
            CurveSegments.Draw(context, pen, c.Source, c.SourceControl, c.TargetControl, c.Target, zoom);
    }
}
