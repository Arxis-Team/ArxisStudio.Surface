using System;
using System.Collections.Generic;
using Avalonia;
using ArxisStudio.Surface.Editing;

namespace ArxisStudio.Surface.UiDesigner;

// Пользовательские направляющие и выключатели показа инструментов: свойства слоя
// редактирования (SurfaceGuides), которые дизайнер интерфейса берёт себе, и служба,
// которая за ними стоит (UserGuideService).
// Часть UiDesignerView; общее описание типа — в UiDesignerView.cs.
public partial class UiDesignerView
{
    /// <summary>
    /// Идентификатор свойства видимости пользовательских направляющих.
    /// </summary>
    public static readonly AttachedProperty<bool> ShowGuidesProperty =
        SurfaceGuides.ShowGuidesProperty.AddOwner<UiDesignerView>();

    /// <summary>
    /// Идентификатор свойства видимости линий выравнивания.
    /// </summary>
    public static readonly AttachedProperty<bool> ShowSnapGuidesProperty =
        SurfaceGuides.ShowSnapGuidesProperty.AddOwner<UiDesignerView>();

    /// <summary>
    /// Идентификатор свойства видимости линеек.
    /// </summary>
    public static readonly AttachedProperty<bool> ShowRulersProperty =
        SurfaceGuides.ShowRulersProperty.AddOwner<UiDesignerView>();

    /// <summary>
    /// Идентификатор свойства пользовательских направляющих.
    /// </summary>
    public static readonly AttachedProperty<IEnumerable<SurfaceGuide>?> GuidesProperty =
        SurfaceGuides.GuidesProperty.AddOwner<UiDesignerView>();

    /// <summary>
    /// Идентификатор свойства направляющей, показываемой во время её перемещения.
    /// </summary>
    public static readonly AttachedProperty<SurfaceGuide?> GuidePreviewProperty =
        SurfaceGuides.GuidePreviewProperty.AddOwner<UiDesignerView>();

    /// <summary>
    /// Идентификатор свойства снимка пользовательских направляющих.
    /// </summary>
    public static readonly AttachedProperty<IReadOnlyList<SurfaceGuide>> UserGuidesProperty =
        SurfaceGuides.UserGuidesProperty.AddOwner<UiDesignerView>();

    private UserGuideService UserGuides_ => GetService<UserGuideService>()!;

    /// <summary>
    /// Получает или задает признак отображения пользовательских направляющих.
    /// </summary>
    /// <remarks>
    /// Прячет линии, но не трогает набор: <see cref="Guides"/> остаётся как был,
    /// и включение возвращает всё на место. Это выключатель показа, а не удаление.
    /// <para>
    /// Спрятанную линию нельзя ни подвинуть, ни вытянуть новую с линейки: жест по
    /// невидимому — худший вид сюрприза. А вот <b>притяжение</b> к ней продолжает
    /// работать, ровно как у сетки, которую <see cref="SurfaceView.ShowGrid"/> тоже только прячет;
    /// выключается оно отдельно, через <c>InteractionOptions.IsSnapToGuidesEnabled</c>.
    /// </para>
    /// </remarks>
    public bool ShowGuides
    {
        get => GetValue(ShowGuidesProperty);
        set => SetValue(ShowGuidesProperty, value);
    }

    /// <summary>
    /// Получает или задает признак отображения линий выравнивания и подсказок об интервалах.
    /// </summary>
    /// <remarks>
    /// Прячет только показ: сами выравнивание и интервалы продолжают работать, как сетка
    /// при выключенном <see cref="SurfaceView.ShowGrid"/>. Отключаются они через
    /// <c>InteractionOptions.IsSnapToGuidesEnabled</c> и <c>IsEqualSpacingEnabled</c>.
    /// <para>
    /// Вместе с <see cref="ShowGuides"/> это способ погасить встроенный слой целиком —
    /// то, что нужно хосту, который рисует направляющие сам, поставив свой
    /// <see cref="SnapGuideLayer"/> или собственный контрол поверх редактора.
    /// </para>
    /// </remarks>
    public bool ShowSnapGuides
    {
        get => GetValue(ShowSnapGuidesProperty);
        set => SetValue(ShowSnapGuidesProperty, value);
    }

    /// <summary>
    /// Получает или задает признак отображения линеек.
    /// </summary>
    /// <remarks>
    /// Линейка в шаблон редактора не входит — её ставит хост, — поэтому свойство
    /// не прячет её напрямую, а служит общим выключателем: <see cref="SurfaceRuler"/>
    /// следит за ним у своего <c>Editor</c> так же, как за масштабом и положением.
    /// Одна настройка гасит обе линейки, и хосту не нужно держать свой флаг.
    /// <para>
    /// Видимость ставится через <c>SetCurrentValue</c>, поэтому собственная привязка
    /// хоста к <c>IsVisible</c> переживает переключение.
    /// </para>
    /// </remarks>
    public bool ShowRulers
    {
        get => GetValue(ShowRulersProperty);
        set => SetValue(ShowRulersProperty, value);
    }

    /// <summary>
    /// Получает или задает пользовательские направляющие.
    /// </summary>
    /// <remarks>
    /// Набором владеет хост: редактор его читает, показывает и притягивает к нему элементы,
    /// но не создаёт и не удаляет записи сам — как и с деревом контролов.
    /// <para>
    /// Коллекция, реализующая <see cref="System.Collections.Specialized.INotifyCollectionChanged"/>,
    /// отслеживается: добавленная направляющая появляется и в отрисовке, и в притяжении
    /// без переприсваивания свойства.
    /// </para>
    /// </remarks>
    public IEnumerable<SurfaceGuide>? Guides
    {
        get => GetValue(GuidesProperty);
        set => SetValue(GuidesProperty, value);
    }

    /// <summary>
    /// Получает направляющую, показываемую во время её перемещения.
    /// </summary>
    public SurfaceGuide? GuidePreview => GetValue(GuidePreviewProperty);

    /// <summary>
    /// Получает снимок пользовательских направляющих.
    /// </summary>
    public IReadOnlyList<SurfaceGuide> UserGuides => GetValue(UserGuidesProperty);

    /// <summary>
    /// Возникает, когда пользователь просит изменить набор направляющих.
    /// </summary>
    /// <remarks>
    /// Набором владеет хост, поэтому редактор его не правит сам. Пока обработчик
    /// не выставил <c>Handled</c>, направляющая остаётся там, где была.
    /// <para>
    /// Без подписчика жест перемещения направляющей не начинается вовсе.
    /// </para>
    /// </remarks>
    public event EventHandler<SurfaceGuideChangeRequestedEventArgs>? GuideChangeRequested
    {
        add => UserGuides_.ChangeRequested += value;
        remove => UserGuides_.ChangeRequested -= value;
    }

    internal bool CanRequestGuideChange => UserGuides_.CanRequestChange;

    internal bool TryFindGuideAtPoint(Point viewportPoint, out SurfaceGuide guide)
        => UserGuides_.TryFindGuideAtPoint(viewportPoint, out guide);

    internal double ResolveGuidePosition(Point viewportPoint, SurfaceGuideOrientation orientation, Avalonia.Input.KeyModifiers modifiers)
        => UserGuides_.ResolvePosition(viewportPoint, orientation, modifiers);

    internal bool RequestGuideChange(SurfaceGuideChangeKind kind, SurfaceGuide guide, SurfaceGuide? original)
        => UserGuides_.RequestChange(kind, guide, original);

    internal void SetGuidePreview(SurfaceGuide? guide) => UserGuides_.SetPreview(guide);
}
