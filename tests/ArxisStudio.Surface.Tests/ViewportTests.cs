using Avalonia;
using Avalonia.Headless.XUnit;
using Xunit;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Tests;

/// <summary>
/// Перевод координат и навигация viewport.
/// </summary>
public class ViewportTests
{
    [AvaloniaFact]
    public void GetWorldPosition_Accounts_For_Zoom_And_Pan()
    {
        var editor = new DesignEditor
        {
            ViewportZoom = 2.0,
            ViewportLocation = new Point(100, 50)
        };

        // (200,100)/2 = (100,50); + ViewportLocation = (200,100)
        Assert.Equal(new Point(200, 100), editor.GetWorldPosition(new Point(200, 100)));
    }

    [AvaloniaFact]
    public void GetWorldPosition_Is_Identity_At_Default_Viewport()
    {
        var editor = new DesignEditor();

        Assert.Equal(new Point(42, 17), editor.GetWorldPosition(new Point(42, 17)));
    }

    [AvaloniaFact]
    public void CenterOn_Puts_World_Point_In_The_Middle_Of_The_Viewport()
    {
        var harness = EditorHarness.Create();
        var editor = harness.Editor;
        var target = new Point(500, 400);

        editor.CenterOn(target);

        // Центр видимой области в мировых координатах должен совпасть с target.
        var centre = editor.ViewportLocation + new Vector(
            editor.Bounds.Width / editor.ViewportZoom / 2,
            editor.Bounds.Height / editor.ViewportZoom / 2);

        Assert.Equal(target.X, centre.X, 3);
        Assert.Equal(target.Y, centre.Y, 3);
    }

    [AvaloniaFact]
    public void CenterOn_Does_Not_Change_Zoom()
    {
        var harness = EditorHarness.Create();
        harness.Editor.ViewportZoom = 2.5;

        harness.Editor.CenterOn(new Point(1000, 1000));

        Assert.Equal(2.5, harness.Editor.ViewportZoom, 3);
    }

    [AvaloniaFact]
    public void FitToView_Clamps_To_MinZoom_For_Huge_Areas()
    {
        var harness = EditorHarness.Create();

        harness.Editor.FitToView(new Rect(0, 0, 100_000, 100_000));

        Assert.Equal(harness.Editor.MinZoom, harness.Editor.ViewportZoom, 6);
    }

    [AvaloniaFact]
    public void FitToView_Clamps_To_MaxZoom_For_Tiny_Areas()
    {
        var harness = EditorHarness.Create();

        harness.Editor.FitToView(new Rect(0, 0, 1, 1));

        Assert.Equal(harness.Editor.MaxZoom, harness.Editor.ViewportZoom, 6);
    }

    [AvaloniaFact]
    public void FitToView_Is_Ignored_When_Editor_Has_No_Size()
    {
        // Редактор вне дерева: Bounds пустые, деление на ноль недопустимо.
        var editor = new DesignEditor();
        var before = editor.ViewportZoom;

        editor.FitToView(new Rect(0, 0, 100, 100));

        Assert.Equal(before, editor.ViewportZoom, 6);
    }

    // ---- Наведение на контейнер -------------------------------------------------

    /// <summary>
    /// Наведение на контейнер меряет контейнер, а не то, что внутри него выбрано.
    /// </summary>
    /// <remarks>
    /// Перегрузка для <see cref="DesignEditorItem"/> считала границы через
    /// <c>ResolveSelectionTarget</c>, то есть по вложенному контролу, который был бы
    /// выбран внутри формы. Кнопки «Center» и «Fit» в демо наводились из-за этого
    /// на карточку внутри экрана вместо самого экрана и совпадали с «Center Sel»
    /// и «Fit Sel» до последнего знака.
    /// <para>
    /// Что ветка была ошибочной, видно и без замера: fallback того же метода считает
    /// по <c>item.Location</c> и <c>item.Bounds</c> — по самому контейнеру.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void CenterOnItem_Centres_The_Container_Not_Its_Nested_Target()
    {
        var harness = EditorHarness.Create();
        var container = harness.PlaceContainer(0, new Point(100, 100), new Size(400, 300));

        harness.Editor.CenterOnItem(container);
        harness.RunLayout();

        var centre = harness.Editor.GetWorldPosition(
            new Point(harness.Editor.Bounds.Width / 2, harness.Editor.Bounds.Height / 2));

        Assert.Equal(300, centre.X, 1);
        Assert.Equal(250, centre.Y, 1);
    }

    /// <summary>
    /// Вписывание контейнера считает по контейнеру.
    /// </summary>
    /// <remarks>
    /// Вложенный контрол стенда много меньше контейнера, поэтому масштабы
    /// у двух прочтений расходятся, и подмена видна числом.
    /// </remarks>
    [AvaloniaFact]
    public void FitToView_Uses_The_Container_Bounds()
    {
        var harness = EditorHarness.Create();
        var container = harness.PlaceContainer(0, new Point(100, 100), new Size(400, 300));

        harness.Editor.FitToView(container);
        harness.RunLayout();

        // Стенд 800x600, контейнер 400x300 — вписывание даёт около 1,9.
        // Вложенный контрол 60x40; вписав его, редактор ушёл бы к верхней границе зума.
        Assert.InRange(harness.Editor.ViewportZoom, 1.0, 3.0);
    }
}
