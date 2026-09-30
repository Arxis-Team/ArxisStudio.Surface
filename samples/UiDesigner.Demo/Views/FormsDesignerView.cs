using System;
using ArxisStudio.Surface.UiDesigner;
using Avalonia.Controls;
using UiDesigner.Demo.ViewModels;

namespace UiDesigner.Demo.Views;

/// <summary>
/// The editor, giving each form the card the form already owns.
/// </summary>
/// <remarks>
/// <para>
/// An editor makes a container for every item it is given, and the usual way to make it a form's
/// container is <c>ItemRootBinding</c>: the editor creates a <c>UiDesignerFormItem</c> and binds its
/// root. That is the right shape for a host whose forms appear when the canvas lays them out.
/// </para>
/// <para>
/// This designer's forms exist before that. A form is published, asked whether it has anything to
/// show and measured for the log while the canvas has yet to arrange anything, and its root is let
/// go and taken again around every update — all of it by the view model, which therefore has to
/// hold the card rather than wait to be given one. So the card is made with the form, and this is
/// the one line that tells the editor to use it: the ordinary extension point of any
/// <see cref="ItemsControl"/>, not a hook the library had to grow.
/// </para>
/// <para>
/// The editor leaves the root of such a container alone when it lets the container go — it only
/// returns what its own binding gave — which is what a form that moves between generations needs:
/// the view model decides when the root is released.
/// </para>
/// </remarks>
public sealed class FormsDesignerView : UiDesignerView
{
    /// <summary>The library's theme is looked up by exact type, and this type adds no look.</summary>
    protected override Type StyleKeyOverride => typeof(UiDesignerView);

    /// <inheritdoc />
    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) =>
        item is FormViewModel form ? form.Card : base.CreateContainerForItemOverride(item, index, recycleKey);
}
