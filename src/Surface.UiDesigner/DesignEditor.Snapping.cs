using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ArxisStudio.Surface.Editing;

namespace ArxisStudio.Surface.UiDesigner;

// Привязка к сетке и направляющие выравнивания: служба слоя редактирования
// (SnapService) и то, как дизайнер форм её подключает.
// Часть DesignEditor; общее описание типа — в DesignEditor.cs.
public partial class DesignEditor
{
    /// <summary>
    /// Идентификатор свойства линий выравнивания, найденных во время жеста.
    /// </summary>
    public static readonly AttachedProperty<IReadOnlyList<DesignSnapGuide>> SnapGuidesProperty =
        SurfaceSnapping.SnapGuidesProperty.AddOwner<DesignEditor>();

    /// <summary>
    /// Идентификатор свойства подсказок о равных интервалах.
    /// </summary>
    public static readonly AttachedProperty<IReadOnlyList<DesignSpacingHint>> SpacingHintsProperty =
        SurfaceSnapping.SpacingHintsProperty.AddOwner<DesignEditor>();

    private readonly SnapService _snap;

    private SnapService Snap => _snap;

    /// <summary>
    /// Получает линии выравнивания, найденные во время жеста.
    /// </summary>
    public IReadOnlyList<DesignSnapGuide> SnapGuides => GetValue(SnapGuidesProperty);

    /// <summary>
    /// Получает подсказки о равных интервалах, найденные во время жеста.
    /// </summary>
    public IReadOnlyList<DesignSpacingHint> SpacingHints => GetValue(SpacingHintsProperty);

    internal bool ShouldSnap(KeyModifiers modifiers) => Snap.ShouldSnap(modifiers);

    internal double ResolveSnapStep() => Snap.ResolveStep();

    internal double SnapCoordinate(double value) => Snap.SnapCoordinate(value);
}
