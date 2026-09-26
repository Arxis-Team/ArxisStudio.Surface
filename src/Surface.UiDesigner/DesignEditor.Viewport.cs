using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DesignLayout = ArxisStudio.Surface.UiDesigner.Layout;
using DesignInteraction = ArxisStudio.Surface.Editing.DesignInteraction;
using ArxisStudio.Controls;
using ArxisStudio.Surface.Editing;
using ArxisStudio.Surface.UiDesigner.Placement;
using ArxisStudio.States;

namespace ArxisStudio.Surface.UiDesigner;

// Панорамирование, зум и трансформации viewport'а.
// Часть DesignEditor; общее описание типа — в DesignEditor.cs.
public partial class DesignEditor
{
    /// <summary>
    /// Смещает viewport так, чтобы указанный элемент оказался в центре видимой области редактора.
    /// </summary>
    /// <param name="item">Элемент, который необходимо центрировать в области просмотра.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="item"/> равен <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Выбрасывается, если <paramref name="item"/> не принадлежит текущему экземпляру <see cref="DesignEditor"/>.
    /// </exception>
    /// <remarks>
    /// Метод изменяет только <see cref="SurfaceView.ViewportLocation"/> и не изменяет <see cref="SurfaceView.ViewportZoom"/>.
    /// <para>
    /// Если размер элемента превышает размер видимой области, элемент не масштабируется и не вписывается целиком:
    /// в центр видимой области помещается только геометрический центр элемента.
    /// </para>
    /// <para>
    /// Метод использует текущие <see cref="SurfaceItem.Location"/> и <see cref="Visual.Bounds"/> элемента.
    /// Для корректного результата элемент должен принадлежать текущему редактору и иметь актуальный layout.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// editor.CenterOnItem(container);
    /// ]]></code>
    /// </example>
    public void CenterOnItem(DesignEditorItem item)
    {
        if (item == null)
            throw new ArgumentNullException(nameof(item));

        if (!ReferenceEquals(item.FindAncestorOfType<DesignEditor>(), this))
            throw new InvalidOperationException("The specified item does not belong to this DesignEditor.");

        if (TryGetDesignBounds(item, out var bounds))
        {
            CenterOn(bounds.Center);
            return;
        }

        var fallbackCenter = new Point(
            item.Location.X + (item.Bounds.Width / 2),
            item.Location.Y + (item.Bounds.Height / 2));

        CenterOn(fallbackCenter);
    }

    /// <summary>
    /// Изменяет положение и масштаб viewport так, чтобы указанный элемент целиком поместился в видимой области редактора.
    /// </summary>
    /// <param name="item">Элемент, который необходимо вписать в окно редактора.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="item"/> равен <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Выбрасывается, если <paramref name="item"/> не принадлежит текущему экземпляру <see cref="DesignEditor"/>.
    /// </exception>
    /// <remarks>
    /// Метод использует текущие <see cref="SurfaceItem.Location"/> и <see cref="Visual.Bounds"/> элемента
    /// и делегирует расчет геометрии перегрузке <see cref="SurfaceView.FitToView(Rect)"/>.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// editor.FitToView(container);
    /// ]]></code>
    /// </example>
    public void FitToView(DesignEditorItem item)
    {
        if (item == null)
            throw new ArgumentNullException(nameof(item));

        if (!ReferenceEquals(item.FindAncestorOfType<DesignEditor>(), this))
            throw new InvalidOperationException("The specified item does not belong to this DesignEditor.");

        if (TryGetDesignBounds(item, out var bounds))
        {
            FitToView(bounds);
            return;
        }

        FitToView(new Rect(item.Location, item.Bounds.Size));
    }

    /// <summary>
    /// Смещает viewport так, чтобы центр области, охватывающей все выбранные элементы, оказался в центре видимой области редактора.
    /// </summary>
    /// <remarks>
    /// Метод не изменяет <see cref="SurfaceView.ViewportZoom"/>. Если в редакторе нет выбранных элементов, вызов игнорируется.
    /// </remarks>
    public void CenterOnSelection()
    {
        if (TryGetSelectedDesignBounds(out var bounds, out _, out _, out _, out _, out _, out _, out _))
            CenterOn(bounds);
    }

    /// <summary>
    /// Изменяет положение и масштаб viewport так, чтобы все выбранные элементы целиком поместились в видимой области редактора.
    /// </summary>
    /// <remarks>
    /// Если в редакторе нет выбранных элементов, вызов игнорируется.
    /// </remarks>
    public void FitSelectionToView()
    {
        if (TryGetSelectedDesignBounds(out var bounds, out _, out _, out _, out _, out _, out _, out _))
            FitToView(bounds);
    }
}
