namespace ArxisStudio.Surface.UiDesigner;

internal readonly struct SelectionInteractionCapabilities
{
    public SelectionInteractionCapabilities(
        int selectedTargetsCount,
        bool isNestedGroupSelection,
        bool hasAnyMoveLockedTarget,
        bool hasAnyMoveEnabledTarget)
    {
        SelectedTargetsCount = selectedTargetsCount;
        IsNestedGroupSelection = isNestedGroupSelection;
        HasAnyMoveLockedTarget = hasAnyMoveLockedTarget;
        HasAnyMoveEnabledTarget = hasAnyMoveEnabledTarget;
    }

    public int SelectedTargetsCount { get; }
    public bool IsNestedGroupSelection { get; }
    public bool HasAnyMoveLockedTarget { get; }
    public bool HasAnyMoveEnabledTarget { get; }

    public bool HasMixedMovePolicies => HasAnyMoveLockedTarget && HasAnyMoveEnabledTarget;

    public bool CanMoveNestedGroup =>
        IsNestedGroupSelection &&
        SelectedTargetsCount > 1 &&
        !HasAnyMoveLockedTarget;
}
