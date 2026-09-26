using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ArxisStudio.Surface.Editing;

/// <summary>
/// Пользовательские направляющие: снимок набора хоста, притяжение к ним и жест переноса.
/// </summary>
/// <remarks>
/// Служба слоя редактирования (ADR 0003). Набором владеет хост: служба его читает,
/// показывает и просит изменить через <see cref="ChangeRequested"/>, но записи не
/// создаёт и не удаляет — как и поверхность не правит дерево контролов.
/// </remarks>
internal sealed class UserGuideService
{
    /// <summary>
    /// Радиус захвата направляющей указателем, в пикселях экрана.
    /// </summary>
    /// <remarks>
    /// В пикселях, а не в мировых единицах, по той же причине, что и
    /// <c>SnapGuideTolerance</c>: попасть в линию надо там, где она видна.
    /// </remarks>
    private const double GuideGrabPixels = 4.0;

    private readonly SurfaceView _view;

    public UserGuideService(SurfaceView view)
    {
        _view = view;

        // Нажатие на направляющую перехватывается в фазе туннелирования: линия
        // нарисована поверх всего, значит и жест должна забирать раньше контейнера
        // под ней. Через всплытие это не сделать — контейнер обработает нажатие
        // первым, захватит указатель и начнёт своё перетаскивание.
        view.AddHandler(InputElement.PointerPressedEvent, OnTunnelPointerPressed, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Запрос на изменение набора направляющих. Отправитель — поверхность.
    /// </summary>
    public event EventHandler<DesignGuideChangeRequestedEventArgs>? ChangeRequested;

    private IReadOnlyList<DesignGuide> Current => SurfaceGuides.GetUserGuides(_view);

    /// <summary>
    /// Обрабатывает смену коллекции пользовательских направляющих.
    /// </summary>
    internal void OnGuidesSourceChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyCollectionChanged oldNotify)
            oldNotify.CollectionChanged -= OnGuidesCollectionChanged;

        if (e.NewValue is INotifyCollectionChanged newNotify)
            newNotify.CollectionChanged += OnGuidesCollectionChanged;

        Rebuild();
    }

    private void OnGuidesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    /// <summary>
    /// Пересобирает снимок пользовательских направляющих.
    /// </summary>
    /// <remarks>
    /// Снимок сравнивается с текущим той же дисциплиной, что и выделение: совпавший
    /// не публикуется вовсе. Хост вправе переприсвоить эквивалентную коллекцию,
    /// и перерисовывать слой из-за этого не за чем.
    /// </remarks>
    private void Rebuild()
    {
        var source = SurfaceGuides.GetGuides(_view);
        var next = source == null
            ? Array.Empty<DesignGuide>()
            : source.ToArray();

        var current = Current;
        if (next.Length == current.Count)
        {
            var same = true;
            for (var i = 0; i < next.Length; i++)
            {
                if (next[i] == current[i])
                    continue;

                same = false;
                break;
            }

            if (same)
                return;
        }

        SurfaceGuides.SetUserGuides(_view, next);
    }

    /// <summary>
    /// Отдаёт направляющие соседями для выравнивания.
    /// </summary>
    /// <remarks>
    /// Направляющая приходит в резолвер прямоугольником нулевой толщины: тогда её
    /// ближний край, центр и дальний край совпадают, и правило «каждый кандидат
    /// с каждым» само сводится к одному сравнению. Отдельной ветки под неё не нужно.
    /// <para>
    /// Протяжённость берётся у содержимого поверхности: она влияет только на длину
    /// линии, показанной во время жеста, — саму направляющую слой рисует через
    /// весь viewport независимо от этого прямоугольника.
    /// </para>
    /// </remarks>
    public IEnumerable<Rect> CollectNeighbours()
    {
        var guides = Current;
        if (guides.Count == 0)
            return Array.Empty<Rect>();

        var extent = _view.ItemsExtent;
        var result = new List<Rect>(guides.Count);
        for (var i = 0; i < guides.Count; i++)
        {
            var guide = guides[i];
            result.Add(guide.Orientation == DesignGuideOrientation.Vertical
                ? new Rect(guide.Position, extent.Y, 0, extent.Height)
                : new Rect(extent.X, guide.Position, extent.Width, 0));
        }

        return result;
    }

    /// <summary>
    /// Получает признак того, что запрос на изменение направляющих кто-то слушает.
    /// </summary>
    /// <remarks>
    /// Без подписчика жеста нет вовсе — то же правило, что и у перестановки: вести
    /// линию за курсором, зная, что на отпускании ничего не произойдёт, значит
    /// обещать пользователю несуществующее.
    /// </remarks>
    public bool CanRequestChange => ChangeRequested != null;

    /// <summary>
    /// Ищет направляющую под точкой в координатах поверхности.
    /// </summary>
    public bool TryFindGuideAtPoint(Point viewportPoint, out DesignGuide guide)
    {
        guide = default;

        // Спрятанную линию не за что хватать: жест по невидимому объекту
        // выглядит как самопроизвольное поведение редактора.
        if (!SurfaceGuides.GetShowGuides(_view))
            return false;

        var guides = Current;
        if (guides.Count == 0)
            return false;

        var zoom = _view.ViewportZoom;
        if (!(zoom > 0))
            return false;

        var world = _view.GetWorldPosition(viewportPoint);
        var best = double.MaxValue;
        var found = false;

        for (var i = 0; i < guides.Count; i++)
        {
            var candidate = guides[i];
            var world1D = candidate.Orientation == DesignGuideOrientation.Vertical ? world.X : world.Y;
            var distance = Math.Abs(world1D - candidate.Position) * zoom;

            if (distance > GuideGrabPixels || distance >= best)
                continue;

            best = distance;
            guide = candidate;
            found = true;
        }

        return found;
    }

    /// <summary>
    /// Переводит точку указателя в координату направляющей.
    /// </summary>
    /// <remarks>
    /// Привязка к сетке действует и здесь: направляющую ставят по макету, а он стоит
    /// на той же сетке. Модификатор обхода снимает её так же, как и при перетаскивании.
    /// </remarks>
    public double ResolvePosition(Point viewportPoint, DesignGuideOrientation orientation, KeyModifiers modifiers)
    {
        var world = _view.GetWorldPosition(viewportPoint);
        var value = orientation == DesignGuideOrientation.Vertical ? world.X : world.Y;

        return _view.GetService<SnapService>() is { } snap && snap.ShouldSnap(modifiers)
            ? snap.SnapCoordinate(value)
            : value;
    }

    /// <summary>
    /// Поднимает запрос на изменение набора направляющих.
    /// </summary>
    /// <returns><see langword="true"/>, если запрос выполнен.</returns>
    /// <remarks>
    /// Обход подписчиков останавливается на первом выполнившем: запрос описывает набор,
    /// снятый до правки, и следующему он говорил бы о состоянии, которого уже нет.
    /// </remarks>
    public bool RequestChange(DesignGuideChangeKind kind, DesignGuide guide, DesignGuide? original)
    {
        var handler = ChangeRequested;
        if (handler == null)
            return false;

        var args = new DesignGuideChangeRequestedEventArgs(kind, guide, original);
        foreach (var entry in handler.GetInvocationList())
        {
            ((EventHandler<DesignGuideChangeRequestedEventArgs>)entry)(_view, args);
            if (args.Handled)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Показывает направляющую в положении, которое она займёт на отпускании.
    /// </summary>
    /// <remarks>
    /// Само перемещение применяется на отпускании, а не покадрово: набором владеет хост,
    /// и просить его о правке на каждом кадре протяжки значило бы двадцать запросов
    /// в секунду вместо одного.
    /// </remarks>
    public void SetPreview(DesignGuide? guide) => SurfaceGuides.SetGuidePreview(_view, guide);

    /// <summary>
    /// Перехватывает нажатие на направляющую до того, как его увидит содержимое.
    /// </summary>
    /// <remarks>
    /// Ничего не делает, пока под указателем нет направляющей: обработчик задуман так,
    /// чтобы его влияние на обычный ввод было ровно нулевым.
    /// </remarks>
    private void OnTunnelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!CanRequestChange || e.Handled)
            return;

        if (!e.GetCurrentPoint(_view).Properties.IsLeftButtonPressed)
            return;

        var point = e.GetPosition(_view);
        if (!TryFindGuideAtPoint(point, out var guide))
            return;

        // Нажатие дальше не пойдёт, поэтому то, что обычно делает OnPointerPressed,
        // приходится сделать здесь: без этого жест считал бы позицию и модификаторы
        // от предыдущего ввода.
        _view.RecordPointerInput(point, e.KeyModifiers);

        if (!_view.IsKeyboardFocusWithin)
            _view.Focus();

        _view.PushState(new EditorGuideDraggingState(_view, this, e.Pointer, guide));
        e.Handled = true;
    }
}
