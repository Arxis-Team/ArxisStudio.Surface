using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace ArxisStudio.Surface.UiDesigner;

/// <summary>
/// Представляет панель с абсолютным позиционированием дочерних элементов.
/// </summary>
/// <remarks>
/// Панель поддерживает гибридный режим размещения: элементы могут быть расположены
/// либо по координатам <see cref="Layout.XProperty"/>/<see cref="Layout.YProperty"/>,
/// либо через стандартные свойства выравнивания Avalonia.
/// </remarks>
public class AbsolutePanel : Panel
{
    /// <summary>
    /// Идентификатор свойства прямоугольника, охватывающего все дочерние элементы панели.
    /// </summary>
    public static readonly StyledProperty<Rect> ExtentProperty =
        AvaloniaProperty.Register<AbsolutePanel, Rect>(nameof(Extent));

    /// <summary>
    /// Получает или задает прямоугольник, охватывающий все дочерние элементы.
    /// </summary>
    public Rect Extent
    {
        get => GetValue(ExtentProperty);
        set => SetValue(ExtentProperty, value);
    }

    static AbsolutePanel()
    {
        // Подписываемся на изменения координат Layout.X/Y у дочерних элементов,
        // чтобы вызвать пересчет макета.
        Layout.XProperty.Changed.AddClassHandler<Control>((s, e) => InvalidateParentLayout(s));
        Layout.YProperty.Changed.AddClassHandler<Control>((s, e) => InvalidateParentLayout(s));
    }

    private static void InvalidateParentLayout(Control control)
    {
        if (control.GetVisualParent() is AbsolutePanel panel)
        {
            panel.InvalidateMeasure();
            panel.InvalidateArrange();
        }
    }

    /// <summary>
    /// Измеряет дочерние элементы и вычисляет итоговый размер панели.
    /// </summary>
    /// <param name="availableSize">Доступный размер от родителя.</param>
    /// <returns>Размер, который панель хочет занять.</returns>
    protected override Size MeasureOverride(Size availableSize)
    {
        var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
        double minX = 0, minY = 0;
        double maxX = 0, maxY = 0;
        bool hasItems = false;

        foreach (var child in Children)
        {
            // Измеряем ребенка с бесконечностью, чтобы узнать его желаемый размер
            child.Measure(infinite);

            double x = Layout.GetX(child);
            double y = Layout.GetY(child);

            // Если координаты не заданы (NaN), считаем их 0 для расчета границ Extent
            double effectiveX = double.IsNaN(x) ? 0 : x;
            double effectiveY = double.IsNaN(y) ? 0 : y;

            var size = child.DesiredSize;

            if (size.Width > 0 && size.Height > 0)
            {
                hasItems = true;
                if (effectiveX < minX) minX = effectiveX;
                if (effectiveY < minY) minY = effectiveY;
                if (effectiveX + size.Width > maxX) maxX = effectiveX + size.Width;
                if (effectiveY + size.Height > maxY) maxY = effectiveY + size.Height;
            }
        }

        // Обновляем свойство Extent (область, занятая элементами)
        var extent = hasItems ? new Rect(minX, minY, maxX - minX, maxY - minY) : new Rect();
        SetCurrentValue(ExtentProperty, extent);

        // ФИКС:
        // Если панели дали конкретное место (availableSize не бесконечен), занимаем его полностью.
        // Это позволяет работать выравниванию (Right/Bottom) внутри фиксированных контейнеров.
        // Иначе (если бесконечность, например в ScrollViewer) - занимаем место по контенту.
        double resultW = double.IsPositiveInfinity(availableSize.Width) ? maxX : availableSize.Width;
        double resultH = double.IsPositiveInfinity(availableSize.Height) ? maxY : availableSize.Height;

        return new Size(resultW, resultH);
    }

    /// <summary>
    /// Размещает дочерние элементы согласно их координатам или выравниванию.
    /// </summary>
    /// <param name="finalSize">Финальный размер панели.</param>
    /// <returns>Фактически занятый размер.</returns>
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            // ВАЖНО: Принудительно включаем слежение за глобальными координатами для каждого ребенка.
            // Это гарантирует обновление SurfaceX/SurfaceY даже для элементов без явных Layout.X/Y.
            Layout.Track(child);

            double x = Layout.GetX(child);
            double y = Layout.GetY(child);

            // --- Расчет позиции X ---
            double finalX = 0;
            double finalW = child.DesiredSize.Width;

            if (!double.IsNaN(x))
            {
                // Приоритет 1: Абсолютная координата
                finalX = x;
            }
            else
            {
                // Приоритет 2: Выравнивание (Alignment)
                switch (child.HorizontalAlignment)
                {
                    case HorizontalAlignment.Center:
                        finalX = (finalSize.Width - child.DesiredSize.Width) / 2;
                        break;
                    case HorizontalAlignment.Right:
                        finalX = finalSize.Width - child.DesiredSize.Width;
                        break;
                    case HorizontalAlignment.Stretch:
                        finalX = 0;
                        finalW = finalSize.Width; // Растягиваем ширину
                        break;
                    default: // Left
                        finalX = 0;
                        break;
                }
            }

            // --- Расчет позиции Y ---
            double finalY = 0;
            double finalH = child.DesiredSize.Height;

            if (!double.IsNaN(y))
            {
                // Приоритет 1: Абсолютная координата
                finalY = y;
            }
            else
            {
                // Приоритет 2: Выравнивание (Alignment)
                switch (child.VerticalAlignment)
                {
                    case VerticalAlignment.Center:
                        finalY = (finalSize.Height - child.DesiredSize.Height) / 2;
                        break;
                    case VerticalAlignment.Bottom:
                        finalY = finalSize.Height - child.DesiredSize.Height;
                        break;
                    case VerticalAlignment.Stretch:
                        finalY = 0;
                        finalH = finalSize.Height; // Растягиваем высоту
                        break;
                    default: // Top
                        finalY = 0;
                        break;
                }
            }

            // Размещаем элемент
            child.Arrange(new Rect(finalX, finalY, finalW, finalH));
        }
        return finalSize;
    }
}
