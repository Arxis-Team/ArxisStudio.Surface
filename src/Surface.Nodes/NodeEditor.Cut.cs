using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using ArxisStudio.Surface.Nodes.States;
using ArxisStudio.Surface.States;

namespace ArxisStudio.Surface.Nodes;

// Разрез: отрезок, перечёркнутые связи просят удалить (ADR 0005).
// Часть NodeEditor; общее описание типа — в NodeEditor.cs.
public partial class NodeEditor
{
    /// <summary>
    /// Идентификатор свойства модификаторов разреза.
    /// </summary>
    public static readonly StyledProperty<KeyModifiers> LinkCutModifiersProperty =
        AvaloniaProperty.Register<NodeEditor, KeyModifiers>(nameof(LinkCutModifiers), KeyModifiers.Alt);

    /// <summary>
    /// Получает или задает модификаторы, с которыми протяжка левой кнопкой режет связи.
    /// </summary>
    /// <remarks>
    /// Отрезок тянется от нажатия до указателя; перечёркнутые им связи уходят на отпускании одним
    /// <see cref="LinkDeleteRequested"/> — тем же запросом, что Delete. Начать можно на пустом
    /// холсте или на связи, но не на узле. По умолчанию Alt: правая кнопка занята контекстным
    /// меню, средняя — панорамой, Ctrl — режимом контейнеров ядра. <see cref="KeyModifiers.None"/>
    /// выключает разрез.
    /// </remarks>
    public KeyModifiers LinkCutModifiers
    {
        get => GetValue(LinkCutModifiersProperty);
        set => SetValue(LinkCutModifiersProperty, value);
    }

    /// <summary>
    /// Отрезок разреза из шаблона.
    /// </summary>
    internal LinkCutPreview? CutPreview { get; private set; }

    /// <summary>
    /// Сколько раз разрез сверял отрезок с самой кривой — для стенда стоимости.
    /// </summary>
    internal int LinkCutChecks { get; private set; }

    /// <summary>
    /// Собирает связи, которые пересекает отрезок: отсев по рамке связи, затем по кривой.
    /// </summary>
    internal void CollectCrossing(Point a, Point b, HashSet<LinkRecord> result)
    {
        result.Clear();
        var segment = new Rect(
            new Point(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)),
            new Point(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y))).Inflate(1);

        foreach (var link in _recordByItem.Values)
        {
            if (!link.IsResolved || !link.WorldBounds.Intersects(segment))
                continue;

            LinkCutChecks++;
            if (link.Geometry.Intersects(a, b))
                result.Add(link);
        }
    }

    /// <summary>
    /// Элементы коллекции связей для этих связей — в том порядке, в каком они лежат у хоста.
    /// </summary>
    internal IReadOnlyList<object> ItemsInCollectionOrder(IReadOnlyCollection<LinkRecord> links)
    {
        if (links.Count == 0)
            return Array.Empty<object>();

        var wanted = new HashSet<object>();
        foreach (var link in links)
            wanted.Add(link.ItemOrSelf);

        var result = new List<object>(wanted.Count);
        if (Links != null)
        {
            foreach (var item in Links)
            {
                if (item != null && wanted.Remove(item))
                    result.Add(item);
            }
        }

        // Готовая связь, стоящая не в коллекции, — в конец.
        result.AddRange(wanted);
        return result;
    }

    /// <summary>
    /// Нажатие с модификаторами разреза начинает разрез — на пустом холсте или на связи.
    /// </summary>
    /// <remarks>
    /// Нажатие по узлу сюда не доходит: узел берёт его себе, и оно приходит обработанным.
    /// </remarks>
    private bool TryStartCut(PointerPressedEventArgs e)
    {
        var required = LinkCutModifiers;
        if (required == KeyModifiers.None || CurrentState is not EditorIdleState)
            return false;

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed || !e.KeyModifiers.HasFlag(required))
            return false;

        RecordPointerInput(point.Position, e.KeyModifiers);
        if (!IsKeyboardFocusWithin)
            Focus();

        PushState(new LinkCutState(this, e.Pointer, point.Position));
        e.Handled = true;
        return true;
    }
}
