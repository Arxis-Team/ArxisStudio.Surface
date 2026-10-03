using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;
using ArxisStudio.Surface.UiDesigner;

namespace ArxisStudio.Tests;

/// <summary>
/// Контрол формы — код проекта, и его сбой на замере или раскладке остаётся сбоем формы (ADR 0028):
/// элемент ловит его на своей границе, говорит причину, а холст и окно продолжают раскладываться.
/// </summary>
public class UiDesignerFormFaultTests
{
    /// <summary>Контрол проекта, который падает там, где падают чужие контролы.</summary>
    private sealed class Throwing(bool inMeasure) : Control
    {
        protected override Size MeasureOverride(Size availableSize) =>
            inMeasure ? throw new InvalidOperationException("measure failed") : base.MeasureOverride(availableSize);

        protected override Size ArrangeOverride(Size finalSize) =>
            !inMeasure ? throw new InvalidOperationException("arrange failed") : base.ArrangeOverride(finalSize);
    }

    private static (Window Host, Border Neighbour) Host(UiDesignerFormItem item)
    {
        var neighbour = new Border { Width = 40, Height = 40 };
        var host = new Window { Width = 900, Height = 600, Content = new StackPanel { Children = { item, neighbour } } };

        host.Show();
        host.UpdateLayout();

        return (host, neighbour);
    }

    [AvaloniaTheory]
    [InlineData(true, "measure failed")]
    [InlineData(false, "arrange failed")]
    public void A_Form_That_Throws_Is_Faulted_And_The_Window_Still_Lays_Out(bool inMeasure, string message)
    {
        var item = new UiDesignerFormItem { Root = new UserControl { Content = new Throwing(inMeasure) } };

        var (_, neighbour) = Host(item);

        Assert.Equal(message, item.FaultMessage);
        Assert.Contains(":faulted", item.Classes);

        // Проход раскладки не унесло: сосед по окну разложен.
        Assert.Equal(40, neighbour.Bounds.Width);
    }

    [AvaloniaFact]
    public void A_New_Root_Starts_Over()
    {
        var item = new UiDesignerFormItem { Root = new UserControl { Content = new Throwing(inMeasure: true) } };

        Host(item);

        Assert.NotNull(item.FaultMessage);

        var healthy = new TextBlock { Text = "fine" };

        item.Root = new UserControl { Content = healthy };
        item.UpdateLayout();

        Assert.Null(item.FaultMessage);
        Assert.DoesNotContain(":faulted", item.Classes);
        Assert.True(healthy.Bounds.Width > 0, "Новое содержимое меряется снова.");
    }

    [AvaloniaFact]
    public void A_Healthy_Form_Says_Nothing()
    {
        var item = new UiDesignerFormItem { Root = new UserControl { Content = new TextBlock { Text = "fine" } } };

        Host(item);

        Assert.Null(item.FaultMessage);
        Assert.DoesNotContain(":faulted", item.Classes);
    }
}
