using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Surface;

namespace ArxisStudio.Tests;

/// <summary>
/// Контекстные действия: как складывается запрос и кто может его остановить.
/// </summary>
/// <remarks>
/// 329 строк и девять из сорока публичных типов, у которых до сих пор не было
/// ни одного теста. Правил тут два, и оба словесные: как выбирается
/// <see cref="DesignEditorContextScope"/> и чем <c>Cancel</c> отличается от
/// <c>Handled</c> — их и закрепляем.
/// </remarks>
public class ContextActionTests
{
    /// <summary>Провайдер, отдающий заранее заданный набор действий.</summary>
    private sealed class Provider : IDesignEditorContextActionProvider
    {
        private readonly IReadOnlyList<DesignEditorContextAction> _actions;

        public Provider(params DesignEditorContextAction[] actions) => _actions = actions;

        public List<DesignEditorContextRequest> Seen { get; } = new();

        public ValueTask<IReadOnlyList<DesignEditorContextAction>> GetActionsAsync(
            SurfaceView editor, DesignEditorContextRequest request, CancellationToken cancellationToken)
        {
            Seen.Add(request);
            return ValueTask.FromResult(_actions);
        }
    }

    /// <summary>Презентер, который только считает показы.</summary>
    private sealed class Presenter : IDesignEditorContextPresenter
    {
        public int Shows { get; private set; }
        public bool Result { get; set; } = true;

        public bool TryShow(SurfaceView editor, DesignEditorContextRequest request, IReadOnlyList<DesignEditorContextAction> actions)
        {
            Shows++;
            return Result;
        }
    }

    private static DesignEditorContextAction Action(string id, string group = "", int order = 0, bool visible = true) =>
        new() { Id = id, Header = id, Group = group, Order = order, IsVisible = visible };

    private static EditorHarness Create(out Presenter presenter, params IDesignEditorContextActionProvider[] providers)
    {
        var harness = EditorHarness.Create(nodeCount: 2);
        harness.PlaceContainer(0, new Point(100, 100), new Size(200, 150));
        harness.PlaceContainer(1, new Point(400, 100), new Size(200, 150));

        presenter = new Presenter();
        harness.Editor.ContextPresenter = presenter;
        foreach (var provider in providers)
            harness.Editor.ContextActionProviders.Add(provider);

        return harness;
    }

    private static async Task<DesignEditorContextRequest> RequestAt(EditorHarness harness, Point viewportPoint)
    {
        DesignEditorContextRequest? seen = null;
        void Capture(object? sender, DesignEditorContextRequestedEventArgs e) => seen = e.Request;

        harness.Editor.ContextMenuResolved += Capture;
        try
        {
            await harness.Editor.RequestContextAsync(
                DesignEditorContextSource.Programmatic, viewportPoint, KeyModifiers.None);
        }
        finally
        {
            harness.Editor.ContextMenuResolved -= Capture;
        }

        Assert.NotNull(seen);
        return seen!;
    }

    // ---- Область запроса ------------------------------------------------------

    [AvaloniaFact]
    public async Task Empty_Space_Is_Surface_Scope()
    {
        var harness = Create(out _, new Provider(Action("a")));

        var request = await RequestAt(harness, new Point(700, 500));

        Assert.Equal(DesignEditorContextScope.Surface, request.Scope);
        Assert.Null(request.Target);
    }

    [AvaloniaFact]
    public async Task A_Nested_Control_Is_Nested_Target_Scope()
    {
        var harness = Create(out _, new Provider(Action("a")));

        var request = await RequestAt(harness, harness.CentreOf(harness.Nested(0)));

        Assert.Equal(DesignEditorContextScope.NestedTarget, request.Scope);
        Assert.Same(harness.Nested(0), request.Target!.Target);
    }

    [AvaloniaFact]
    public async Task A_Hit_Inside_A_Group_Is_Selection_Scope()
    {
        var harness = Create(out _, new Provider(Action("a")));

        // Группа именно из тех target'ов, по которым потом щёлкаем: «внутри
        // выделения» считается по самому target'у, а не по его контейнеру.
        // Ctrl+A набирает контейнеры, и клик по их ребёнку — это уже не попадание
        // в выделение, а отдельный вложенный элемент.
        harness.Editor.SelectDesignTarget(harness.Nested(0));
        harness.Editor.SelectDesignTarget(harness.Named(0, "Sibling"), additive: true);
        harness.RunLayout();
        Assert.Equal(2, harness.Editor.SelectedDesignTargetsCount);

        var request = await RequestAt(harness, harness.CentreOf(harness.Nested(0)));

        // Правило из README: попадание внутрь текущей группы описывает группу,
        // а не отдельный элемент под курсором.
        Assert.Equal(DesignEditorContextScope.Selection, request.Scope);
        Assert.True(request.Selection.Count > 1);
    }

    // ---- Сбор действий --------------------------------------------------------

    [AvaloniaFact]
    public async Task Invisible_Actions_Do_Not_Reach_The_Host()
    {
        var provider = new Provider(Action("visible"), Action("hidden", visible: false));
        var harness = Create(out var presenter, provider);

        IReadOnlyList<DesignEditorContextAction>? resolved = null;
        harness.Editor.ContextMenuResolved += (_, e) => resolved = e.Actions;

        await RequestAt(harness, new Point(700, 500));

        Assert.Equal(new[] { "visible" }, resolved!.Select(a => a.Id));
        Assert.Equal(1, presenter.Shows);
    }

    [AvaloniaFact]
    public async Task Actions_Are_Ordered_By_Group_Then_Order()
    {
        var harness = Create(out _,
            new Provider(Action("b2", group: "b", order: 2), Action("a1", group: "a", order: 1)),
            new Provider(Action("b1", group: "b", order: 1)));

        IReadOnlyList<DesignEditorContextAction>? resolved = null;
        harness.Editor.ContextMenuResolved += (_, e) => resolved = e.Actions;

        await RequestAt(harness, new Point(700, 500));

        Assert.Equal(new[] { "a1", "b1", "b2" }, resolved!.Select(a => a.Id));
    }

    // ---- Две разные остановки -------------------------------------------------

    [AvaloniaFact]
    public async Task Cancel_Stops_Everything()
    {
        var harness = Create(out var presenter, new Provider(Action("a")));
        var resolved = 0;

        harness.Editor.ContextMenuRequesting += (_, e) => e.Cancel = true;
        harness.Editor.ContextMenuResolved += (_, _) => resolved++;

        await harness.Editor.RequestContextAsync(
            DesignEditorContextSource.Programmatic, new Point(700, 500), KeyModifiers.None);

        // Cancel — «контекста не будет вовсе»: ни показа, ни отчёта о результате.
        Assert.Equal(0, presenter.Shows);
        Assert.Equal(0, resolved);
    }

    [AvaloniaFact]
    public async Task Handled_Skips_The_Presenter_But_Still_Reports()
    {
        var harness = Create(out var presenter, new Provider(Action("a")));
        DesignEditorContextRequestedEventArgs? reported = null;

        harness.Editor.ContextMenuRequesting += (_, e) => e.Handled = true;
        harness.Editor.ContextMenuResolved += (_, e) => reported = e;

        await harness.Editor.RequestContextAsync(
            DesignEditorContextSource.Programmatic, new Point(700, 500), KeyModifiers.None);

        // Handled — «покажу сам»: презентер молчит, но отчёт приходит,
        // и в нём сказано, что контекст показан. Это не то же, что Cancel.
        Assert.Equal(0, presenter.Shows);
        Assert.NotNull(reported);
        Assert.True(reported!.Handled);
    }

    [AvaloniaFact]
    public async Task A_Host_Can_Replace_The_Action_List()
    {
        var harness = Create(out _, new Provider(Action("from-provider")));
        IReadOnlyList<DesignEditorContextAction>? resolved = null;

        harness.Editor.ContextMenuRequesting += (_, e) => e.Actions = new[] { Action("from-host") };
        harness.Editor.ContextMenuResolved += (_, e) => resolved = e.Actions;

        await RequestAt(harness, new Point(700, 500));

        Assert.Equal(new[] { "from-host" }, resolved!.Select(a => a.Id));
    }

    [AvaloniaFact]
    public async Task Without_Actions_The_Presenter_Is_Not_Called()
    {
        var harness = Create(out var presenter);
        DesignEditorContextRequestedEventArgs? reported = null;
        harness.Editor.ContextMenuResolved += (_, e) => reported = e;

        await harness.Editor.RequestContextAsync(
            DesignEditorContextSource.Programmatic, new Point(700, 500), KeyModifiers.None);

        // Пустое меню не показывают, но хост об этом узнаёт.
        Assert.Equal(0, presenter.Shows);
        Assert.NotNull(reported);
        Assert.False(reported!.Handled);
    }

    /// <summary>Провайдер, который отвечает только когда его отпустят.</summary>
    private sealed class SlowProvider : IDesignEditorContextActionProvider
    {
        private readonly TaskCompletionSource _gate = new();

        public int Calls { get; private set; }
        public int Cancelled { get; private set; }

        public void Release() => _gate.TrySetResult();

        public async ValueTask<IReadOnlyList<DesignEditorContextAction>> GetActionsAsync(
            SurfaceView editor, DesignEditorContextRequest request, CancellationToken cancellationToken)
        {
            Calls++;

            using var registration = cancellationToken.Register(() => Cancelled++);
            await _gate.Task;
            cancellationToken.ThrowIfCancellationRequested();

            return new[] { Action("slow") };
        }
    }

    [AvaloniaFact]
    public void A_Second_Right_Click_Cancels_The_Pending_Request()
    {
        var provider = new SlowProvider();
        var harness = Create(out var presenter, provider);
        var point = new Point(700, 500);

        harness.Window.MouseDown(point, MouseButton.Right);
        harness.Window.MouseUp(point, MouseButton.Right);
        harness.Window.MouseDown(point, MouseButton.Right);
        harness.Window.MouseUp(point, MouseButton.Right);
        harness.RunLayout();

        provider.Release();
        harness.RunLayout();

        // Провайдер объявлен асинхронным, значит он вправе ходить в свою модель.
        // Без отмены оба запроса доходили до конца и меню разрешалось дважды —
        // второй показ поверх первого.
        Assert.Equal(2, provider.Calls);
        Assert.Equal(1, provider.Cancelled);
        Assert.True(presenter.Shows <= 1);
    }

    // ---- Правый клик и выделение ---------------------------------------------

    [AvaloniaFact]
    public void Right_Click_Retargets_The_Selection()
    {
        var harness = Create(out _, new Provider(Action("a")));
        harness.Editor.SelectDesignTarget(harness.Nested(0));
        harness.RunLayout();

        var sibling = harness.Named(0, "Sibling");
        harness.Window.MouseDown(harness.CentreOf(sibling), MouseButton.Right);
        harness.Window.MouseUp(harness.CentreOf(sibling), MouseButton.Right);
        harness.RunLayout();

        // Меню обязано относиться к тому, по чему щёлкнули, поэтому правый клик
        // сначала переводит выделение на этот target.
        Assert.Same(sibling, harness.Editor.PrimarySelectionTarget!.Target);
    }

    /// <summary>
    /// Правый клик внутри выделения его не трогает.
    /// </summary>
    /// <remarks>
    /// Оборотная сторона предыдущего правила: переводить выделение нужно, только когда
    /// щёлкнули мимо него. Схлопывать выбранное до одного target'а значило бы отобрать
    /// у пользователя то, ради чего он меню и открывал, — над двумя выбранными
    /// элементами команда группировки становилась недоступной.
    /// </remarks>
    [AvaloniaFact]
    public void Right_Click_Inside_The_Selection_Keeps_It()
    {
        var harness = Create(out _, new Provider(Action("a")));
        var sibling = harness.Named(0, "Sibling");

        harness.Editor.SelectDesignTarget(harness.Nested(0));
        harness.Editor.SelectDesignTarget(sibling, additive: true);
        harness.RunLayout();
        Assert.Equal(2, harness.Editor.SelectedDesignTargetsCount);

        harness.Window.MouseDown(harness.CentreOf(sibling), MouseButton.Right);
        harness.Window.MouseUp(harness.CentreOf(sibling), MouseButton.Right);
        harness.RunLayout();

        Assert.Equal(2, harness.Editor.SelectedDesignTargetsCount);
        Assert.True(harness.Editor.CanGroupSelection());
    }
}
