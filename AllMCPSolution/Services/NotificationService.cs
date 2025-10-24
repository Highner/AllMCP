using AllMCPSolution.Controllers;

namespace AllMCPSolution.Services;

public static class NotificationService
{
    public static IReadOnlyList<WineSurferSentInvitationNotification> CreateSentInvitationNotifications(IEnumerable<SisterhoodInvitation> invitations)
    {
        if (invitations is null)
        {
            return Array.Empty<WineSurferSentInvitationNotification>();
        }

        return invitations
            .Where(invitation => invitation is not null && invitation.Status == SisterhoodInvitationStatus.Accepted)
            .Select(invitation =>
            {
                var inviteeName = string.IsNullOrWhiteSpace(invitation.InviteeUser?.Name)
                    ? invitation.InviteeUser?.UserName
                    : invitation.InviteeUser!.Name;

                return new WineSurferSentInvitationNotification(
                    invitation.Id,
                    invitation.SisterhoodId,
                    invitation.Sisterhood?.Name ?? "Sisterhood",
                    invitation.InviteeEmail,
                    inviteeName,
                    invitation.UpdatedAt);
            })
            .OrderByDescending(notification => notification.UpdatedAtUtc)
            .ThenBy(notification => notification.InviteeName ?? notification.InviteeEmail, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}