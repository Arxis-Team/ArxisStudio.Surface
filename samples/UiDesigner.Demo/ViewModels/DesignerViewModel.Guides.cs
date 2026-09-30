using System.Collections.ObjectModel;
using ArxisStudio.Surface.Editing;

namespace UiDesigner.Demo.ViewModels;

/// <summary>
/// The rulers' guides, which belong to the designer rather than to the editor.
/// </summary>
/// <remarks>
/// <para>
/// The editor draws these lines, snaps to them and asks to change them; it never adds or removes
/// one itself — the same division it applies to the control tree. So the set lives here, and
/// answering <c>GuideChangeRequested</c> is what makes dragging one off a ruler do anything.
/// </para>
/// <para>
/// They are not written to the document and are not meant to be: a guide is scaffolding for the
/// person laying a form out, not a fact about the form. Nothing in a project's markup describes
/// one, and inventing an attribute for it would put the designer's furniture in the user's file.
/// That also means they last as long as the studio is open, which is what a person expects of a
/// line they dragged onto the canvas an hour ago and no longer needs.
/// </para>
/// </remarks>
public sealed partial class DesignerViewModel
{
    /// <summary>The guide lines the rulers have produced.</summary>
    /// <remarks>
    /// Observable, because the editor tracks the collection rather than re-reading a property: a
    /// line added here shows at once, without the set being assigned again.
    /// </remarks>
    public ObservableCollection<SurfaceGuide> Guides { get; } = [];

    /// <summary>Whether the rulers are shown, and with them the way to make a guide.</summary>
    /// <remarks>
    /// One switch for both rulers, held here rather than on the editor so the menu can bind to it
    /// and the editor can be told. Guides stay in the set while the rulers are hidden — hiding the
    /// ruler is not the same request as throwing the lines away.
    /// </remarks>
    public bool ShowRulers
    {
        get;
        set => Set(ref field, value);
    } = true;

    /// <summary>Whether the guide lines themselves are drawn.</summary>
    /// <remarks>
    /// Separate from <see cref="ShowRulers"/> and from snapping. Hiding the lines leaves them in
    /// the set and leaves the pull to them working, exactly as hiding the grid does — "these lines
    /// are in my way while I look at this" is a different request from "stop pulling to them".
    /// </remarks>
    public bool ShowGuides
    {
        get;
        set => Set(ref field, value);
    } = true;

    public RelayCommand ToggleRulersCommand { get; private set; } = null!;

    public RelayCommand ToggleGuidesCommand { get; private set; } = null!;

    public RelayCommand ClearGuidesCommand { get; private set; } = null!;

    /// <summary>Applies a change the editor asked for, which is what makes the gesture real.</summary>
    /// <remarks>
    /// A move arrives as the line that was there and the line it should become; the editor has been
    /// drawing a preview all along and changes nothing until this returns true. A line dragged off
    /// the canvas arrives as a removal — the same gesture backwards, and the only way to be rid of
    /// one.
    /// </remarks>
    public bool ApplyGuideChange(SurfaceGuideChangeKind kind, SurfaceGuide guide, SurfaceGuide? original)
    {
        switch (kind)
        {
            case SurfaceGuideChangeKind.Add:
                Guides.Add(guide);

                return true;

            case SurfaceGuideChangeKind.Move:
                int at = original is { } was ? Guides.IndexOf(was) : -1;

                if (at < 0)
                {
                    return false;
                }

                Guides[at] = guide;

                return true;

            case SurfaceGuideChangeKind.Remove:
                return Guides.Remove(guide);

            default:
                return false;
        }
    }

    private void InitialiseGuides()
    {
        ToggleRulersCommand = new RelayCommand(() => ShowRulers = !ShowRulers);
        ToggleGuidesCommand = new RelayCommand(() => ShowGuides = !ShowGuides);

        ClearGuidesCommand = new RelayCommand(
            () =>
            {
                Guides.Clear();
                Log("  the guides were cleared");
            },
            () => Guides.Count > 0);
    }
}
