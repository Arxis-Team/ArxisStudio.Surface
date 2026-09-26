using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using DesignLayout = ArxisStudio.Surface.UiDesigner.Layout;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Согласованность двух слоёв выделения на точках записи, не переведённых на общее ядро.
/// </summary>
/// <remarks>
/// Плановый разбор говорил, что запись выделения размазана по десяти местам с разными
/// резолверами владельца и батчингом в двух местах из десяти. Четыре точки после этого
/// переведены на <c>ApplySelection</c>; здесь измерены оставшиеся шесть —
/// <c>EditorSelectingState.Enter</c>, <c>RetargetSelectionForContext</c>,
/// <c>CommitSelection</c>, <c>CommitMarqueeWithinContainer</c>, <c>TryClearSelection</c>
/// и <c>TrySelectAll</c>.
/// <para>
/// Расхождений не нашлось ни в одном из восьми сценариев, поэтому тесты остались как
/// замок, а перевод не состоялся. Причина не только в отсутствии дефекта:
/// <c>ApplySelection</c> принимает <b>один</b> target, а коммит рамки пишет набор,
/// собранный по нескольким контейнерам, одной транзакцией. Разложить его на вызовы
/// по одному target'у значило бы либо потерять гарантию одного события, либо завести
/// пакетный API выделения — тот самый, который план откладывает сознательно.
/// </para>
/// </remarks>
public class SelectionCoherenceTests
{
    private static EditorHarness Create()
    {
        var harness = EditorHarness.Create(nodeCount: 2);
        harness.PlaceContainer(0, new Point(100, 100), new Size(200, 150));
        harness.PlaceContainer(1, new Point(400, 100), new Size(200, 150));
        return harness;
    }

    private static int CountEvents(EditorHarness harness, Action act)
    {
        var n = 0;
        void H(object? s, DesignSelectionChangedEventArgs e) => n++;
        harness.Editor.DesignSelectionChanged += H;
        try { act(); }
        finally { harness.Editor.DesignSelectionChanged -= H; }
        return n;
    }

    private static void Drag(EditorHarness h, Point from, Point to, RawInputModifiers mod = RawInputModifiers.None)
    {
        h.Window.MouseDown(from, MouseButton.Left, mod);
        h.Window.MouseMove(from + ((to - from) * 0.5), mod);
        h.Window.MouseMove(to, mod);
        h.Window.MouseUp(to, MouseButton.Left, mod);
        h.RunLayout();
    }

    /// <summary>Коммит рамки — одно событие. Закреплено было только начало жеста.</summary>
    [AvaloniaFact]
    public void Marquee_Commit_Raises_One_Event()
    {
        var harness = Create();
        var events = CountEvents(harness, () => Drag(harness, new Point(285, 235), new Point(105, 105)));

        Assert.True(harness.Editor.SelectedDesignTargetsCount >= 2, $"выбрано {harness.Editor.SelectedDesignTargetsCount}");
        Assert.Equal(1, events);
    }

    /// <summary>Рамка по пустому холсту снимает выделение, и тоже одним событием.</summary>
    [AvaloniaFact]
    public void Marquee_Over_Nothing_Clears_In_One_Event()
    {
        var harness = Create();
        harness.Editor.SelectDesignTarget(harness.Nested(0));

        var events = CountEvents(harness, () => Drag(harness, new Point(650, 400), new Point(750, 500)));

        Assert.Empty(harness.Editor.SelectedDesignTargets);
        Assert.Equal(1, events);
    }

    /// <summary>Рамка уровня контейнеров: слои совпадают по числу, и все target'ы — контейнеры.</summary>
    [AvaloniaFact]
    public void Container_Level_Marquee_Keeps_Both_Layers_In_Step()
    {
        var harness = Create();
        Drag(harness, new Point(50, 50), new Point(650, 300), RawInputModifiers.Control);

        Assert.Equal(2, harness.Editor.SelectedItems!.Count);
        Assert.Equal(2, harness.Editor.SelectedDesignTargetsCount);
        Assert.All(harness.Editor.SelectedDesignTargets,
            t => Assert.Equal(DesignSelectionScope.Container, t.Scope));
    }

    /// <summary>Правый клик по ребёнку формы, выбранной целиком, переводит выбор на ребёнка в обоих слоях.</summary>
    [AvaloniaFact]
    public async Task Right_Click_Retarget_Keeps_Both_Layers_In_Step()
    {
        var harness = Create();
        harness.Editor.SelectDesignTarget(harness.Container(0));
        Assert.Equal(DesignSelectionScope.Container, harness.Editor.PrimarySelectionTarget!.Scope);

        harness.Window.MouseDown(new Point(120, 120), MouseButton.Right);
        harness.Window.MouseUp(new Point(120, 120), MouseButton.Right);
        harness.RunLayout();
        await Task.Yield();

        Assert.Single(harness.Editor.SelectedItems!);
        Assert.Single(harness.Editor.SelectedDesignTargets);
        Assert.Same(harness.Nested(0), harness.Editor.PrimarySelectionTarget!.Target);
    }

    /// <summary>Ctrl+A → Escape → Ctrl+A возвращает ровно то же самое: ни остатка, ни удвоения.</summary>
    [AvaloniaFact]
    public void Select_All_Survives_A_Round_Trip()
    {
        var harness = Create();
        harness.Editor.Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        var first = harness.Editor.SelectedDesignTargetsCount;
        harness.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        harness.Window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);

        Assert.Equal(2, first);
        Assert.Equal(first, harness.Editor.SelectedDesignTargetsCount);
        Assert.Equal(first, harness.Editor.SelectedItems!.Count);
    }

    /// <summary>Повторная additive-рамка по тем же целям их не удваивает.</summary>
    [AvaloniaFact]
    public void Additive_Marquee_Does_Not_Duplicate()
    {
        var harness = Create();
        Drag(harness, new Point(285, 235), new Point(105, 105));
        var first = harness.Editor.SelectedDesignTargetsCount;

        Drag(harness, new Point(285, 235), new Point(105, 105), RawInputModifiers.Shift);

        Assert.Equal(first, harness.Editor.SelectedDesignTargetsCount);
    }

    /// <summary>Вкладывает контейнер в форму 0, с одним размеченным ребёнком.</summary>
    private static DesignEditorItem AddNested(EditorHarness harness)
    {
        var child = new Border { Name = "InnerChild", Width = 40, Height = 30 };
        DesignLayout.SetX(child, 10);
        DesignLayout.SetY(child, 10);
        var content = new AbsolutePanel();
        content.Children.Add(child);

        var inner = new DesignEditorItem
        {
            Name = "Inner",
            Width = 120,
            Height = 100,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Content = content,
        };
        DesignLayout.SetX(inner, 40);
        DesignLayout.SetY(inner, 40);

        ((Panel)harness.Nested(0).GetVisualParent()!).Children.Add(inner);
        harness.RunLayout();
        return inner;
    }

    /// <summary>
    /// Рамка внутри вложенного контейнера: индексный слой держит item верхнего уровня,
    /// слой target'ов — вложенного ребёнка.
    /// </summary>
    /// <remarks>
    /// Здесь и должны были разойтись три резолвера владельца, если бы расходились:
    /// индексный выбор работает только с item'ами верхнего уровня, а владелец рамки
    /// в этом сценарии вложенный.
    /// </remarks>
    [AvaloniaFact]
    public void Nested_Marquee_Puts_The_Top_Level_Owner_In_The_Index_Layer()
    {
        var harness = Create();
        AddNested(harness);

        // Inner на (140,140)-(260,240), InnerChild на (150,150)-(190,180).
        harness.Editor.CommitSelection(new Rect(145, 145, 100, 80), isCtrlPressed: false);
        harness.RunLayout();

        var targets = harness.Editor.SelectedDesignTargets.Select(t => (Control)t.Target).ToList();
        Assert.Contains(targets, t => t.Name == "InnerChild");
        Assert.Single(harness.Editor.SelectedItems!);
        Assert.Same(harness.Nodes[0], harness.Editor.SelectedItems![0]);
    }

    /// <summary>То же для правого клика: контекст ходит своим резолвером, а ответ обязан совпасть.</summary>
    [AvaloniaFact]
    public async Task Nested_Right_Click_Keeps_Both_Layers_In_Step()
    {
        var harness = Create();
        AddNested(harness);

        harness.Window.MouseDown(new Point(160, 160), MouseButton.Right);
        harness.Window.MouseUp(new Point(160, 160), MouseButton.Right);
        harness.RunLayout();
        await Task.Yield();

        Assert.Single(harness.Editor.SelectedItems!);
        Assert.Same(harness.Nodes[0], harness.Editor.SelectedItems![0]);
        Assert.Equal("InnerChild", ((Control)harness.Editor.PrimarySelectionTarget!.Target).Name);
    }
}
