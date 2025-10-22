using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Controllers;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;

namespace AllMCPSolution.Services;

public interface IWineSurferTopBarService
{
    Task<WineSurferTopBarModel> BuildAsync(ClaimsPrincipal? user, string currentPath, CancellationToken cancellationToken);
}

public class WineSurferTopBarService : IWineSurferTopBarService
{
    private static readonly TimeSpan SentInvitationNotificationWindow = TimeSpan.FromDays(7);

    private readonly IUserRepository _userRepository;
    private readonly ISisterhoodInvitationRepository _sisterhoodInvitationRepository;
    private readonly IWineSurferNotificationDismissalRepository _notificationDismissalRepository;

    public WineSurferTopBarService(
        IUserRepository userRepository,
        ISisterhoodInvitationRepository sisterhoodInvitationRepository,
        IWineSurferNotificationDismissalRepository notificationDismissalRepository)
    {
        _userRepository = userRepository;
        _sisterhoodInvitationRepository = sisterhoodInvitationRepository;
        _notificationDismissalRepository = notificationDismissalRepository;
    }

    public async Task<WineSurferTopBarModel> BuildAsync(ClaimsPrincipal? user, string currentPath, CancellationToken cancellationToken)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return WineSurferTopBarModel.Empty(currentPath);
        }

        var identityName = user.Identity?.Name;
        var email = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email");
        var normalizedEmail = NormalizeEmailCandidate(email);
        var currentUserId = GetCurrentUserId(user);
        ApplicationUser? domainUser = null;
        var isAdmin = false;

        if (currentUserId.HasValue)
        {
            domainUser = await _userRepository.GetByIdAsync(currentUserId.Value, cancellationToken);
        }

        if (domainUser is null && !string.IsNullOrWhiteSpace(identityName))
        {
            domainUser = await _userRepository.FindByNameAsync(identityName, cancellationToken);
        }

        var displayName = ResolveDisplayName(domainUser?.Name, identityName, email);

        if (domainUser is not null)
        {
            currentUserId = domainUser.Id;
            normalizedEmail ??= NormalizeEmailCandidate(domainUser.Email);
            isAdmin = domainUser.IsAdmin;
        }

        if (string.IsNullOrWhiteSpace(normalizedEmail) && LooksLikeEmail(displayName))
        {
            normalizedEmail = NormalizeEmailCandidate(displayName);
        }

        IReadOnlyList<WineSurferIncomingSisterhoodInvitation> incomingInvitations = Array.Empty<WineSurferIncomingSisterhoodInvitation>();
        if (currentUserId.HasValue || normalizedEmail is not null)
        {
            var invitations = await _sisterhoodInvitationRepository.GetForInviteeAsync(currentUserId, normalizedEmail, cancellationToken);
            incomingInvitations = invitations
                .Where(invitation => invitation.Status == SisterhoodInvitationStatus.Pending)
                .Select(invitation =>
                {
                    var matchesUserId = currentUserId.HasValue && invitation.InviteeUserId == currentUserId.Value;
                    var matchesEmail = normalizedEmail is not null && string.Equals(invitation.InviteeEmail, normalizedEmail, StringComparison.Ordinal);

                    return new
                    {
                        Invitation = invitation,
                        MatchesUserId = matchesUserId,
                        MatchesEmail = matchesEmail,
                    };
                })
                .Where(entry => entry.MatchesUserId || entry.MatchesEmail)
                .Select(entry => new WineSurferIncomingSisterhoodInvitation(
                    entry.Invitation.Id,
                    entry.Invitation.SisterhoodId,
                    entry.Invitation.Sisterhood?.Name ?? "Sisterhood",
                    entry.Invitation.Sisterhood?.Description,
                    entry.Invitation.InviteeEmail,
                    entry.Invitation.Status,
                    entry.Invitation.CreatedAt,
                    entry.Invitation.UpdatedAt,
                    entry.Invitation.InviteeUserId,
                    entry.MatchesUserId,
                    entry.MatchesEmail))
                .ToList();
        }

        IReadOnlyList<WineSurferSentInvitationNotification> sentInvitationNotifications = Array.Empty<WineSurferSentInvitationNotification>();
        if (currentUserId.HasValue)
        {
            var acceptedInvitations = await _sisterhoodInvitationRepository.GetAcceptedForAdminAsync(
                currentUserId.Value,
                DateTime.UtcNow - SentInvitationNotificationWindow,
                cancellationToken);

            sentInvitationNotifications = CreateSentInvitationNotifications(acceptedInvitations);
        }

        var sections = WineSurferTopBarModel.BuildSections(
            incomingInvitations,
            sentInvitationNotifications,
            WineSurferTopBarModel.SisterhoodNotificationsUrl);

        IReadOnlyDictionary<string, IReadOnlyCollection<string>> dismissedStamps =
            WineSurferTopBarModel.EmptyDismissedNotificationSet;

        if (currentUserId.HasValue)
        {
            var categories = sections
                .Select(section => section.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (categories.Length > 0)
            {
                dismissedStamps = await _notificationDismissalRepository.GetDismissedStampsAsync(
                    currentUserId.Value,
                    categories,
                    cancellationToken);
            }
        }

        return WineSurferTopBarModel.CreateFromSisterhoodData(
            currentPath,
            incomingInvitations,
            sentInvitationNotifications,
            dismissedStamps,
            sections,
            displayName,
            isAdmin);
    }

    private static Guid? GetCurrentUserId(ClaimsPrincipal user)
    {
        var idClaim = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(idClaim, out var parsedId) ? parsedId : null;
    }

    private static string? ResolveDisplayName(string? domainName, string? identityName, string? email)
    {
        if (!string.IsNullOrWhiteSpace(domainName))
        {
            return domainName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(identityName))
        {
            return identityName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            return email.Trim();
        }

        return null;
    }

    private static string? NormalizeEmailCandidate(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return email.Trim().ToLowerInvariant();
    }

    private static bool LooksLikeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var trimmed = value.Trim();
            var address = new MailAddress(trimmed);
            return string.Equals(address.Address, trimmed, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static IReadOnlyList<WineSurferSentInvitationNotification> CreateSentInvitationNotifications(IEnumerable<SisterhoodInvitation> invitations)
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
