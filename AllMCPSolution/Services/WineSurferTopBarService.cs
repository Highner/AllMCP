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

    public WineSurferTopBarService(
        IUserRepository userRepository,
        ISisterhoodInvitationRepository sisterhoodInvitationRepository)
    {
        _userRepository = userRepository;
        _sisterhoodInvitationRepository = sisterhoodInvitationRepository;
    }

    public async Task<WineSurferTopBarModel> BuildAsync(ClaimsPrincipal? user, string currentPath, CancellationToken cancellationToken)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return new WineSurferTopBarModel(
                currentPath,
                Array.Empty<WineSurferIncomingSisterhoodInvitation>(),
                Array.Empty<WineSurferSentInvitationNotification>());
        }

        var displayName = user.Identity?.Name;
        var email = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email");
        var normalizedEmail = NormalizeEmailCandidate(email);
        var currentUserId = GetCurrentUserId(user);
        ApplicationUser? domainUser = null;

        if (currentUserId.HasValue)
        {
            domainUser = await _userRepository.GetByIdAsync(currentUserId.Value, cancellationToken);
        }

        if (domainUser is null && !string.IsNullOrWhiteSpace(displayName))
        {
            domainUser = await _userRepository.FindByNameAsync(displayName, cancellationToken);
        }

        if (domainUser is not null)
        {
            currentUserId = domainUser.Id;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = domainUser.Name;
            }

            normalizedEmail ??= NormalizeEmailCandidate(domainUser.Email);
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = email;
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

        return new WineSurferTopBarModel(currentPath, incomingInvitations, sentInvitationNotifications);
    }

    private static Guid? GetCurrentUserId(ClaimsPrincipal user)
    {
        var idClaim = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(idClaim, out var parsedId) ? parsedId : null;
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
