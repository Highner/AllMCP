using System;
using System.Collections.Generic;

namespace AllMCPSolution.Models;

public class SisterhoodUserSelectModalViewModel
{
    public string OverlayId { get; init; } = "sisterhood-user-select-overlay";

    public string ModalId { get; init; } = "sisterhood-user-select-modal";

    public string Title { get; init; } = "Select fellow surfers";

    public string TitleElementId { get; init; } = "sisterhood-user-select-title";

    public string CloseButtonLabel { get; init; } = "Close select users dialog";

    public string IntroText { get; init; } = "Choose the fellow surfers you'd like to include.";

    public string FilterLabel { get; init; } = "Filter";

    public string FilterPlaceholder { get; init; } = "Filter by name or email";

    public string EmptyMessage { get; init; } = "No users match your filter.";

    public string NoUsersMessage { get; init; } = "You don't have any connected surfers yet.";

    public string StatusEmptyMessage { get; init; } = "No fellow surfers selected.";

    public string StatusSingularFormat { get; init; } = "{0} fellow surfer selected.";

    public string StatusPluralFormat { get; init; } = "{0} fellow surfers selected.";

    public string SubmitButtonText { get; init; } = "Select users";

    public string SubmitButtonAriaLabel { get; init; } = "Submit selected users";

    public string CancelButtonText { get; init; } = "Cancel";

    public string FieldName { get; init; } = "SelectedUserIds";

    public string FormMethod { get; init; } = "post";

    public string? FormAction { get; init; }

    public IReadOnlyList<SisterhoodConnectionUser> Users { get; init; } = Array.Empty<SisterhoodConnectionUser>();
}
