using System;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Плоские свойства модификаторов и набор жестов под ними.
/// </summary>
/// <remarks>
/// <see cref="SurfaceView.ContainerInteractionModifiers"/> и
/// <see cref="SurfaceView.AdditiveSelectionModifiers"/> оставлены ради совместимости
/// и дублируют <see cref="SurfaceInputGestures"/>. Значение у них одно — геттер
/// читает у набора, — а уведомления держатся на ретрансляторе, и вот он без тестов
/// расходится.
/// <para>
/// Разошлось это ровно один раз и именно так, как расходится дублирование: чтение было
/// верным всегда, а уведомления при записи в набор не было вовсе. Привязка к плоскому
/// свойству показывала прежнее значение — при верном значении внутри.
/// </para>
/// <para>
/// Каждый случай проверяется на обоих свойствах. Первая версия проверяла уведомления
/// только на одном, и ветку второго можно было удалить целиком, не уронив ни одного
/// теста, — половина правки держалась ни на чём.
/// </para>
/// </remarks>
public class InputGestureBridgeTests
{
    private static readonly Point ContainerLocation = new(100, 100);
    private static readonly Size ContainerSize = new(200, 150);

    private static Point NestedCentre => new(
        ContainerLocation.X + EditorHarness.NestedOffset + (EditorHarness.NestedWidth / 2),
        ContainerLocation.Y + EditorHarness.NestedOffset + (EditorHarness.NestedHeight / 2));

    private static Point SiblingCentre => new(
        ContainerLocation.X + EditorHarness.SiblingOffset + (EditorHarness.NestedWidth / 2),
        ContainerLocation.Y + EditorHarness.NestedOffset + (EditorHarness.NestedHeight / 2));

    private static EditorHarness Create()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);
        return harness;
    }

    // Оба свойства проходят одни и те же случаи; признак выбирает, какое проверяется.
    private static AvaloniaProperty Flat(bool container) => container
        ? UiDesignerView.ContainerInteractionModifiersProperty
        : UiDesignerView.AdditiveSelectionModifiersProperty;

    private static void WriteSet(SurfaceInputGestures set, bool container, KeyModifiers value)
    {
        if (container)
            set.ContainerInteractionModifiers = value;
        else
            set.AdditiveSelectionModifiers = value;
    }

    private static void WriteFlat(UiDesignerView editor, bool container, KeyModifiers value)
    {
        if (container)
            editor.ContainerInteractionModifiers = value;
        else
            editor.AdditiveSelectionModifiers = value;
    }

    private static KeyModifiers Read(UiDesignerView editor, bool container) => container
        ? editor.ContainerInteractionModifiers
        : editor.AdditiveSelectionModifiers;

    private static int CountRaises(UiDesignerView editor, AvaloniaProperty property, Action change)
    {
        var raised = 0;
        void Handler(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == property)
                raised++;
        }

        editor.PropertyChanged += Handler;
        try
        {
            change();
        }
        finally
        {
            editor.PropertyChanged -= Handler;
        }

        return raised;
    }

    // ---- Согласованность двух сторон -------------------------------------------

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Writing_The_Flat_Property_Reaches_The_Set(bool container)
    {
        var harness = Create();

        WriteFlat(harness.Editor, container, KeyModifiers.Alt);

        Assert.Equal(KeyModifiers.Alt, Read(harness.Editor, container));
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Writing_The_Set_Reaches_The_Flat_Property(bool container)
    {
        var harness = Create();

        WriteSet(harness.Editor.InputGestures, container, KeyModifiers.Alt);

        Assert.Equal(KeyModifiers.Alt, Read(harness.Editor, container));
    }

    [AvaloniaFact]
    public void Replacing_The_Set_Reaches_The_Flat_Property()
    {
        var harness = Create();

        harness.Editor.InputGestures = new SurfaceInputGestures
        {
            AdditiveSelectionModifiers = KeyModifiers.Meta
        };

        Assert.Equal(KeyModifiers.Meta, harness.Editor.AdditiveSelectionModifiers);
    }

    // ---- Уведомления ------------------------------------------------------------

    /// <summary>
    /// Запись в набор поднимает уведомление плоского свойства.
    /// </summary>
    /// <remarks>
    /// Это и был дефект: настройка через набор — путь, рекомендованный документацией, —
    /// уведомления не поднимала, и привязка к плоскому свойству оставалась со старым
    /// значением. Заметить это по значению нельзя: геттер читает у набора и врать не может.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Writing_The_Set_Raises_The_Flat_Property(bool container)
    {
        var harness = Create();

        var raised = CountRaises(
            harness.Editor,
            Flat(container),
            () => WriteSet(harness.Editor.InputGestures, container, KeyModifiers.Alt));

        Assert.Equal(1, raised);
    }

    /// <summary>
    /// Запись через плоское свойство поднимает уведомление ровно один раз.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Writing_The_Flat_Property_Raises_Once(bool container)
    {
        var harness = Create();

        var raised = CountRaises(
            harness.Editor,
            Flat(container),
            () => WriteFlat(harness.Editor, container, KeyModifiers.Alt));

        Assert.Equal(1, raised);
    }

    /// <summary>
    /// К моменту уведомления геттер уже отвечает новым значением.
    /// </summary>
    /// <remarks>
    /// Плоский сеттер писал сначала своё поле, а в набор — после, и поднятое между этими
    /// двумя записями событие заставало геттер со старым значением: <c>NewValue</c> уже
    /// <c>Alt</c>, а чтение свойства ещё <c>Shift</c>. Подписчик, перечитывающий источник
    /// вместо <c>NewValue</c> — конвертер, <c>MultiBinding</c>, обработчик хоста, — работал
    /// по устаревшему. Теперь сеттер пишет только в набор, а событие поднимает ретранслятор
    /// после записи.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_Getter_Already_Answers_With_The_New_Value(bool container)
    {
        var harness = Create();
        var seen = default(KeyModifiers?);

        void Handler(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Flat(container))
                seen = Read(harness.Editor, container);
        }

        harness.Editor.PropertyChanged += Handler;
        WriteFlat(harness.Editor, container, KeyModifiers.Alt);
        harness.Editor.PropertyChanged -= Handler;

        Assert.Equal(KeyModifiers.Alt, seen);
    }

    /// <summary>
    /// После замены уведомления идут от нового набора.
    /// </summary>
    /// <remarks>
    /// Подписку на новый набор не проверял никто: без неё значение всё равно верно —
    /// его задаёт сам сеттер замены, — а вот дальнейшие записи в набор молчали.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_Replacement_Set_Raises_The_Flat_Property(bool container)
    {
        var harness = Create();
        harness.Editor.InputGestures = new SurfaceInputGestures();

        var raised = CountRaises(
            harness.Editor,
            Flat(container),
            () => WriteSet(harness.Editor.InputGestures, container, KeyModifiers.Alt));

        Assert.Equal(1, raised);
    }

    /// <summary>
    /// Заменённый набор редактор больше не ведёт.
    /// </summary>
    /// <remarks>
    /// Проверяется здесь именно исход, а не сама отписка: ретранслятор читает текущий
    /// набор, а не отправителя, поэтому даже оставшаяся подписка значение испортить
    /// не может. Отписка нужна, чтобы брошенный набор не дёргал редактор впустую;
    /// за время жизни отвечает не она, а слабое событие — см. соседний тест.
    /// </remarks>
    [AvaloniaFact]
    public void A_Replaced_Set_No_Longer_Drives_The_Editor()
    {
        var harness = Create();
        var abandoned = harness.Editor.InputGestures;

        harness.Editor.InputGestures = new SurfaceInputGestures();
        abandoned.AdditiveSelectionModifiers = KeyModifiers.Alt;

        Assert.Equal(KeyModifiers.Shift, harness.Editor.AdditiveSelectionModifiers);
    }

    // ---- Время жизни ------------------------------------------------------------

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference MakeEditor(SurfaceInputGestures? shared)
    {
        var editor = new UiDesignerView();
        if (shared != null)
            editor.InputGestures = shared;

        return new WeakReference(editor);
    }

    private static int AliveAfterCollect(Func<WeakReference> make, int count)
    {
        var refs = new List<WeakReference>();
        for (var i = 0; i < count; i++)
            refs.Add(make());

        for (var i = 0; i < 3; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
        }

        return refs.Count(r => r.IsAlive);
    }

    /// <summary>
    /// Общий набор не держит редакторы.
    /// </summary>
    /// <remarks>
    /// Ретранслятор подписан слабым событием Avalonia, а не обычным <c>+=</c>: обычная
    /// подписка кладёт делегат в сам набор, и набор начинает держать редактор. Набор,
    /// общий на несколько редакторов — а именно так его раздаёт один <c>Setter</c>
    /// в стиле, и публичная документация свойства этот путь прямо предлагает, — не
    /// отпускал бы ни одного из них никогда.
    /// <para>
    /// Замерено на обычной подписке: все пять редакторов переживали принудительную
    /// сборку. Контрольная половина теста нужна, чтобы отличить «не держит» от «сборка
    /// вообще ничего не собрала»: без неё тест проходил бы на любой реализации.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void A_Shared_Set_Does_Not_Hold_The_Editors()
    {
        var shared = new SurfaceInputGestures();

        var withShared = AliveAfterCollect(() => MakeEditor(shared), 5);
        var withOwn = AliveAfterCollect(() => MakeEditor(null), 5);

        Assert.Equal(0, withOwn);
        Assert.Equal(0, withShared);
        GC.KeepAlive(shared);
    }

    // ---- Настройка действует ----------------------------------------------------

    /// <summary>
    /// Заданный модификатор добавляет к выделению.
    /// </summary>
    [AvaloniaFact]
    public void The_Configured_Additive_Modifier_Adds()
    {
        var harness = Create();
        harness.Editor.InputGestures.AdditiveSelectionModifiers = KeyModifiers.Alt;

        Click(harness, NestedCentre, RawInputModifiers.None);
        Click(harness, SiblingCentre, RawInputModifiers.Alt);

        Assert.Equal(2, harness.Editor.SelectedTargetsCount);
    }

    /// <summary>
    /// Прежний модификатор после этого добавлять перестаёт.
    /// </summary>
    /// <remarks>
    /// Половина без этой проверки прошла бы и на редакторе, который добавляет к выделению
    /// по любому нажатию: настройка считается работающей, только если она и включает,
    /// и выключает.
    /// </remarks>
    [AvaloniaFact]
    public void The_Previous_Additive_Modifier_Stops_Adding()
    {
        var harness = Create();
        harness.Editor.InputGestures.AdditiveSelectionModifiers = KeyModifiers.Alt;

        Click(harness, NestedCentre, RawInputModifiers.None);
        Click(harness, SiblingCentre, RawInputModifiers.Shift);

        Assert.Equal(1, harness.Editor.SelectedTargetsCount);
        Assert.Equal("Sibling", harness.Editor.PrimarySelectionTarget?.Target.Name);
    }

    private static void Click(EditorHarness harness, Point point, RawInputModifiers modifiers)
    {
        harness.Window.MouseDown(point, MouseButton.Left, modifiers);
        harness.Window.MouseUp(point, MouseButton.Left, modifiers);
        harness.RunLayout();
    }
}
