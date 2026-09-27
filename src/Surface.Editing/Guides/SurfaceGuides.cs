using System;
using System.Collections.Generic;
using Avalonia;

namespace ArxisStudio.Surface.Editing;

/// <summary>
/// Пользовательские направляющие и выключатели показа инструментов на поверхности.
/// </summary>
/// <remarks>
/// Свойства присоединённые, потому что направляющие и линейки — инструмент слоя
/// редактирования, а не ядра (ADR 0003). Редактор, у которого есть свой слой направляющих
/// в шаблоне, берёт их себе через <c>AddOwner</c> и привязывает шаблон как к собственным.
/// <para>
/// <see cref="UserGuidesProperty"/> и <see cref="GuidePreviewProperty"/> пишет только служба
/// направляющих: первое — снимок <see cref="GuidesProperty"/>, второе — превью жеста.
/// Присоединённое свойство Avalonia не бывает открытым только на чтение.
/// </para>
/// </remarks>
public static class SurfaceGuides
{
    /// <summary>
    /// Идентификатор свойства набора пользовательских направляющих.
    /// </summary>
    /// <remarks>
    /// Набором владеет хост. Коллекция с <c>INotifyCollectionChanged</c> отслеживается:
    /// добавленная линия действует сразу, без переприсваивания свойства.
    /// </remarks>
    public static readonly AttachedProperty<IEnumerable<SurfaceGuide>?> GuidesProperty =
        AvaloniaProperty.RegisterAttached<AvaloniaObject, IEnumerable<SurfaceGuide>?>(
            "Guides", typeof(SurfaceGuides));

    /// <summary>
    /// Идентификатор свойства видимости пользовательских направляющих.
    /// </summary>
    public static readonly AttachedProperty<bool> ShowGuidesProperty =
        AvaloniaProperty.RegisterAttached<AvaloniaObject, bool>("ShowGuides", typeof(SurfaceGuides), true);

    /// <summary>
    /// Идентификатор свойства видимости линий выравнивания.
    /// </summary>
    public static readonly AttachedProperty<bool> ShowSnapGuidesProperty =
        AvaloniaProperty.RegisterAttached<AvaloniaObject, bool>("ShowSnapGuides", typeof(SurfaceGuides), true);

    /// <summary>
    /// Идентификатор свойства видимости линеек.
    /// </summary>
    public static readonly AttachedProperty<bool> ShowRulersProperty =
        AvaloniaProperty.RegisterAttached<AvaloniaObject, bool>("ShowRulers", typeof(SurfaceGuides), true);

    /// <summary>
    /// Идентификатор свойства снимка пользовательских направляющих.
    /// </summary>
    public static readonly AttachedProperty<IReadOnlyList<SurfaceGuide>> UserGuidesProperty =
        AvaloniaProperty.RegisterAttached<AvaloniaObject, IReadOnlyList<SurfaceGuide>>(
            "UserGuides", typeof(SurfaceGuides), Array.Empty<SurfaceGuide>());

    /// <summary>
    /// Идентификатор свойства превью переносимой или вытягиваемой направляющей.
    /// </summary>
    public static readonly AttachedProperty<SurfaceGuide?> GuidePreviewProperty =
        AvaloniaProperty.RegisterAttached<AvaloniaObject, SurfaceGuide?>("GuidePreview", typeof(SurfaceGuides));

    static SurfaceGuides()
    {
        // Смену набора слушает служба той поверхности, на которой он задан: хосту
        // незачем знать, что за направляющими кто-то следит.
        GuidesProperty.Changed.AddClassHandler<SurfaceView>(
            (view, e) => view.GetService<UserGuideService>()?.OnGuidesSourceChanged(e));
    }

    /// <summary>Возвращает набор пользовательских направляющих.</summary>
    public static IEnumerable<SurfaceGuide>? GetGuides(AvaloniaObject target) => target.GetValue(GuidesProperty);

    /// <summary>Задаёт набор пользовательских направляющих.</summary>
    public static void SetGuides(AvaloniaObject target, IEnumerable<SurfaceGuide>? value) => target.SetValue(GuidesProperty, value);

    /// <summary>Возвращает признак видимости пользовательских направляющих.</summary>
    public static bool GetShowGuides(AvaloniaObject target) => target.GetValue(ShowGuidesProperty);

    /// <summary>Задаёт признак видимости пользовательских направляющих.</summary>
    public static void SetShowGuides(AvaloniaObject target, bool value) => target.SetValue(ShowGuidesProperty, value);

    /// <summary>Возвращает признак видимости линий выравнивания.</summary>
    public static bool GetShowSnapGuides(AvaloniaObject target) => target.GetValue(ShowSnapGuidesProperty);

    /// <summary>Задаёт признак видимости линий выравнивания.</summary>
    public static void SetShowSnapGuides(AvaloniaObject target, bool value) => target.SetValue(ShowSnapGuidesProperty, value);

    /// <summary>Возвращает признак видимости линеек.</summary>
    public static bool GetShowRulers(AvaloniaObject target) => target.GetValue(ShowRulersProperty);

    /// <summary>Задаёт признак видимости линеек.</summary>
    public static void SetShowRulers(AvaloniaObject target, bool value) => target.SetValue(ShowRulersProperty, value);

    /// <summary>Возвращает снимок пользовательских направляющих.</summary>
    public static IReadOnlyList<SurfaceGuide> GetUserGuides(AvaloniaObject target) => target.GetValue(UserGuidesProperty);

    /// <summary>Задаёт снимок пользовательских направляющих. Вызывает служба направляющих.</summary>
    public static void SetUserGuides(AvaloniaObject target, IReadOnlyList<SurfaceGuide> value) => target.SetValue(UserGuidesProperty, value);

    /// <summary>Возвращает превью направляющей.</summary>
    public static SurfaceGuide? GetGuidePreview(AvaloniaObject target) => target.GetValue(GuidePreviewProperty);

    /// <summary>Задаёт превью направляющей. Вызывает служба направляющих.</summary>
    public static void SetGuidePreview(AvaloniaObject target, SurfaceGuide? value) => target.SetValue(GuidePreviewProperty, value);
}
