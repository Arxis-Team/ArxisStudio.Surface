using System;
using System.Collections.Generic;
using Avalonia;

namespace ArxisStudio.Surface.Editing;

/// <summary>
/// Результаты привязки, которые поверхность показывает во время жеста: линии
/// выравнивания и подсказки о равных интервалах.
/// </summary>
/// <remarks>
/// Свойства присоединённые, потому что привязка — инструмент слоя редактирования, а не
/// ядра (ADR 0003): их ставит служба привязки на ту поверхность, к которой подключена.
/// Редактор, у которого есть свой слой направляющих в шаблоне, берёт их себе через
/// <c>AddOwner</c> и привязывает шаблон как к собственным.
/// <para>
/// Пишет их только служба привязки. Присоединённое свойство Avalonia не бывает
/// открытым только на чтение, но значение, записанное снаружи, первый же кадр жеста
/// заменит своим.
/// </para>
/// </remarks>
public static class SurfaceSnapping
{
    /// <summary>
    /// Идентификатор свойства линий выравнивания, найденных во время жеста.
    /// </summary>
    public static readonly AttachedProperty<IReadOnlyList<DesignSnapGuide>> SnapGuidesProperty =
        AvaloniaProperty.RegisterAttached<AvaloniaObject, IReadOnlyList<DesignSnapGuide>>(
            "SnapGuides", typeof(SurfaceSnapping), Array.Empty<DesignSnapGuide>());

    /// <summary>
    /// Идентификатор свойства подсказок о равных интервалах.
    /// </summary>
    public static readonly AttachedProperty<IReadOnlyList<DesignSpacingHint>> SpacingHintsProperty =
        AvaloniaProperty.RegisterAttached<AvaloniaObject, IReadOnlyList<DesignSpacingHint>>(
            "SpacingHints", typeof(SurfaceSnapping), Array.Empty<DesignSpacingHint>());

    /// <summary>
    /// Возвращает линии выравнивания, найденные во время жеста.
    /// </summary>
    public static IReadOnlyList<DesignSnapGuide> GetSnapGuides(AvaloniaObject target) => target.GetValue(SnapGuidesProperty);

    /// <summary>
    /// Задаёт линии выравнивания. Вызывает служба привязки.
    /// </summary>
    public static void SetSnapGuides(AvaloniaObject target, IReadOnlyList<DesignSnapGuide> value) => target.SetValue(SnapGuidesProperty, value);

    /// <summary>
    /// Возвращает подсказки о равных интервалах.
    /// </summary>
    public static IReadOnlyList<DesignSpacingHint> GetSpacingHints(AvaloniaObject target) => target.GetValue(SpacingHintsProperty);

    /// <summary>
    /// Задаёт подсказки о равных интервалах. Вызывает служба привязки.
    /// </summary>
    public static void SetSpacingHints(AvaloniaObject target, IReadOnlyList<DesignSpacingHint> value) => target.SetValue(SpacingHintsProperty, value);
}
