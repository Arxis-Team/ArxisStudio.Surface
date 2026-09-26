using System;
using System.Collections.Specialized;
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
using DesignLayout = ArxisStudio.Attached.Layout;
using DesignInteraction = ArxisStudio.Attached.DesignInteraction;
using ArxisStudio.Controls;
using ArxisStudio.Surface.Editing;
using ArxisStudio.Placement;
using ArxisStudio.States;

namespace ArxisStudio;

// Пользовательские направляющие: источник, притяжение к ним и жест перемещения.
// Часть DesignEditor; общее описание типа — в DesignEditor.cs.
public partial class DesignEditor
{
    /// <summary>
    /// Обрабатывает смену коллекции пользовательских направляющих.
    /// </summary>
    /// <param name="e">Аргументы смены свойства.</param>
    private void OnGuidesSourceChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyCollectionChanged oldNotify)
            oldNotify.CollectionChanged -= OnGuidesCollectionChanged;

        if (e.NewValue is INotifyCollectionChanged newNotify)
            newNotify.CollectionChanged += OnGuidesCollectionChanged;

        RebuildUserGuides();
    }

    private void OnGuidesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RebuildUserGuides();

    /// <summary>
    /// Пересобирает снимок пользовательских направляющих.
    /// </summary>
    /// <remarks>
    /// Снимок сравнивается с текущим той же дисциплиной, что и выделение: совпавший
    /// не публикуется вовсе. Хост вправе переприсвоить эквивалентную коллекцию,
    /// и перерисовывать слой из-за этого не за чем.
    /// </remarks>
    private void RebuildUserGuides()
    {
        var source = Guides;
        var next = source == null
            ? Array.Empty<DesignGuide>()
            : source.ToArray();

        if (next.Length == _userGuides.Count)
        {
            var same = true;
            for (var i = 0; i < next.Length; i++)
            {
                if (next[i] == _userGuides[i])
                    continue;

                same = false;
                break;
            }

            if (same)
                return;
        }

        UserGuides = next;
    }

    /// <summary>
    /// Добавляет пользовательские направляющие в список соседей.
    /// </summary>
    /// <remarks>
    /// Направляющая приходит в резолвер прямоугольником нулевой толщины: тогда её
    /// ближний край, центр и дальний край совпадают, и правило «каждый кандидат
    /// с каждым» само сводится к одному сравнению. Отдельной ветки под неё не нужно.
    /// <para>
    /// Протяжённость берётся у содержимого редактора: она влияет только на длину
    /// линии, показанной во время жеста, — саму направляющую слой рисует через
    /// весь viewport независимо от этого прямоугольника.
    /// </para>
    /// </remarks>
    private void AddUserGuideNeighbours(List<Rect> neighbours)
    {
        var guides = _userGuides;
        if (guides.Count == 0)
            return;

        var extent = ItemsExtent;
        for (var i = 0; i < guides.Count; i++)
        {
            var guide = guides[i];
            neighbours.Add(guide.Orientation == DesignGuideOrientation.Vertical
                ? new Rect(guide.Position, extent.Y, 0, extent.Height)
                : new Rect(extent.X, guide.Position, extent.Width, 0));
        }
    }

    /// <summary>
    /// Радиус захвата направляющей указателем, в пикселях экрана.
    /// </summary>
    /// <remarks>
    /// В пикселях, а не в мировых единицах, по той же причине, что и
    /// <c>SnapGuideTolerance</c>: попасть в линию надо там, где она видна.
    /// </remarks>
    private const double GuideGrabPixels = 4.0;

    /// <summary>
    /// Получает признак того, что запрос на изменение направляющих кто-то слушает.
    /// </summary>
    /// <remarks>
    /// Без подписчика жеста нет вовсе — то же правило, что и у перестановки: вести
    /// линию за курсором, зная, что на отпускании ничего не произойдёт, значит
    /// обещать пользователю несуществующее.
    /// </remarks>
    internal bool CanRequestGuideChange => GuideChangeRequested != null;

    /// <summary>
    /// Ищет направляющую под точкой в координатах редактора.
    /// </summary>
    /// <param name="viewportPoint">Точка в координатах редактора.</param>
    /// <param name="guide">Найденная направляющая.</param>
    /// <returns><see langword="true"/>, если направляющая найдена.</returns>
    internal bool TryFindGuideAtPoint(Point viewportPoint, out DesignGuide guide)
    {
        guide = default;

        // Спрятанную линию не за что хватать: жест по невидимому объекту
        // выглядит как самопроизвольное поведение редактора.
        if (!ShowGuides)
            return false;

        var guides = _userGuides;
        if (guides.Count == 0)
            return false;

        var zoom = ViewportZoom;
        if (!(zoom > 0))
            return false;

        var world = GetWorldPosition(viewportPoint);
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
    /// <param name="viewportPoint">Точка в координатах редактора.</param>
    /// <param name="orientation">Ось направляющей.</param>
    /// <param name="modifiers">Модификаторы текущего ввода.</param>
    /// <remarks>
    /// Привязка к сетке действует и здесь: направляющую ставят по макету, а он стоит
    /// на той же сетке. Модификатор обхода снимает её так же, как и при перетаскивании.
    /// </remarks>
    internal double ResolveGuidePosition(Point viewportPoint, DesignGuideOrientation orientation, KeyModifiers modifiers)
    {
        var world = GetWorldPosition(viewportPoint);
        var value = orientation == DesignGuideOrientation.Vertical ? world.X : world.Y;

        return ShouldSnap(modifiers) ? SnapCoordinate(value) : value;
    }

    /// <summary>
    /// Поднимает запрос на изменение набора направляющих.
    /// </summary>
    /// <param name="kind">Вид изменения.</param>
    /// <param name="guide">Направляющая после изменения.</param>
    /// <param name="original">Направляющая до изменения.</param>
    /// <returns><see langword="true"/>, если запрос выполнен.</returns>
    /// <remarks>
    /// Обход подписчиков останавливается на первом выполнившем: запрос описывает набор,
    /// снятый до правки, и следующему он говорил бы о состоянии, которого уже нет.
    /// </remarks>
    internal bool RequestGuideChange(DesignGuideChangeKind kind, DesignGuide guide, DesignGuide? original)
    {
        var handler = GuideChangeRequested;
        if (handler == null)
            return false;

        var args = new DesignGuideChangeRequestedEventArgs(kind, guide, original);
        foreach (var entry in handler.GetInvocationList())
        {
            ((EventHandler<DesignGuideChangeRequestedEventArgs>)entry)(this, args);
            if (args.Handled)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Перехватывает нажатие на направляющую до того, как его увидит содержимое.
    /// </summary>
    /// <param name="sender">Источник события.</param>
    /// <param name="e">Аргументы нажатия.</param>
    /// <remarks>
    /// Ничего не делает, пока под указателем нет направляющей: обработчик задуман так,
    /// чтобы его влияние на обычный ввод было ровно нулевым.
    /// </remarks>
    private void OnTunnelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!CanRequestGuideChange || e.Handled)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var point = e.GetPosition(this);
        if (!TryFindGuideAtPoint(point, out var guide))
            return;

        // Нажатие дальше не пойдёт, поэтому то, что обычно делает OnPointerPressed,
        // приходится сделать здесь: без этого жест считал бы позицию и модификаторы
        // от предыдущего ввода.
        SetLastInputModifiers(e.KeyModifiers);
        _lastMousePosition = point;

        if (!IsKeyboardFocusWithin)
            Focus();

        PushState(new EditorGuideDraggingState(this, e.Pointer, guide));
        e.Handled = true;
    }

    /// <summary>
    /// Показывает направляющую в положении, которое она займёт на отпускании.
    /// </summary>
    /// <param name="guide">Направляющая или <see langword="null"/>, чтобы убрать превью.</param>
    /// <remarks>
    /// Само перемещение применяется на отпускании, а не покадрово: набором владеет хост,
    /// и просить его о правке на каждом кадре протяжки значило бы двадцать запросов
    /// в секунду вместо одного. Превью — то же по смыслу, что индикатор точки вставки
    /// при перестановке.
    /// </remarks>
    internal void SetGuidePreview(DesignGuide? guide) => GuidePreview = guide;
}
