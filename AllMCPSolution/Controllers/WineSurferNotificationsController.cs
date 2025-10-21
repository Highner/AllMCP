using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AllMCPSolution.Controllers;

[Authorize]
[ApiController]
[Route("wine-surfer/notifications")]
public class WineSurferNotificationsController : Controller
{
    private readonly IWineSurferNotificationDismissalRepository _dismissalRepository;
    private readonly ISisterhoodInvitationRepository _sisterhoodInvitationRepository;
    private readonly ISisterhoodRepository _sisterhoodRepository;
    private readonly IUserRepository _userRepository;

    public WineSurferNotificationsController(
        IWineSurferNotificationDismissalRepository dismissalRepository,
        ISisterhoodInvitationRepository sisterhoodInvitationRepository,
        ISisterhoodRepository sisterhoodRepository,
        IUserRepository userRepository)
    {
        _dismissalRepository = dismissalRepository;
        _sisterhoodInvitationRepository = sisterhoodInvitationRepository;
        _sisterhoodRepository = sisterhoodRepository;
        _userRepository = userRepository;
    }

    [HttpPost("dismiss")]
    public async Task<IActionResult> Dismiss([FromBody] DismissNotificationRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var (userId, normalizedEmail) = await ResolveUserContextAsync(User, cancellationToken);
        if (!userId.HasValue)
        {
            return Forbid();
        }

        if (!TryParseStamp(request.Stamp, out var invitationId))
        {
            return BadRequest("Invalid notification stamp.");
        }

        if (string.IsNullOrWhiteSpace(request.Category))
        {
            return BadRequest("Category is required.");
        }

        var normalizedCategory = NormalizeCategory(request.Category);

        switch (normalizedCategory)
        {
            case "sisterhood.pending":
            {
                var validationResult = await ValidatePendingInvitationAsync(invitationId, userId.Value, normalizedEmail, cancellationToken);
                if (validationResult is IActionResult pendingFailure)
                {
                    return pendingFailure;
                }

                break;
            }

            case "sisterhood.accepted":
            {
                var validationResult = await ValidateAcceptedInvitationAsync(invitationId, userId.Value, cancellationToken);
                if (validationResult is IActionResult acceptedFailure)
                {
                    return acceptedFailure;
                }

                break;
            }

            default:
                return BadRequest("Unsupported notification category.");
        }

        await _dismissalRepository.UpsertAsync(userId.Value, normalizedCategory, request.Stamp, DateTime.UtcNow, cancellationToken);

        var updatedDismissals = await _dismissalRepository.GetDismissedStampsAsync(
            userId.Value,
            new[] { normalizedCategory },
            cancellationToken);

        return Ok(new DismissNotificationResponse(updatedDismissals));
    }

    private async Task<IActionResult?> ValidatePendingInvitationAsync(
        Guid invitationId,
        Guid userId,
        string? normalizedEmail,
        CancellationToken cancellationToken)
    {
        var invitation = await _sisterhoodInvitationRepository.GetByIdAsync(invitationId, cancellationToken);
        if (invitation is null)
        {
            return NotFound();
        }

        if (invitation.Status != SisterhoodInvitationStatus.Pending)
        {
            return BadRequest("Notification is no longer available.");
        }

        var matchesUserId = invitation.InviteeUserId.HasValue && invitation.InviteeUserId.Value == userId;
        var matchesEmail = !string.IsNullOrWhiteSpace(normalizedEmail)
            && string.Equals(invitation.InviteeEmail, normalizedEmail, StringComparison.Ordinal);

        return matchesUserId || matchesEmail ? null : Forbid();
    }

    private async Task<IActionResult?> ValidateAcceptedInvitationAsync(
        Guid invitationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var invitation = await _sisterhoodInvitationRepository.GetByIdAsync(invitationId, cancellationToken);
        if (invitation is null)
        {
            return NotFound();
        }

        if (invitation.Status != SisterhoodInvitationStatus.Accepted || invitation.SisterhoodId == Guid.Empty)
        {
            return BadRequest("Notification is no longer available.");
        }

        var isAdmin = await _sisterhoodRepository.IsAdminAsync(invitation.SisterhoodId, userId, cancellationToken);
        return isAdmin ? null : Forbid();
    }

    private async Task<(Guid? UserId, string? NormalizedEmail)> ResolveUserContextAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var displayName = user.Identity?.Name;
        var email = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email");
        var normalizedEmail = NormalizeEmailCandidate(email);
        var currentUserId = GetCurrentUserId(user);
        Models.ApplicationUser? domainUser = null;

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
            normalizedEmail ??= NormalizeEmailCandidate(domainUser.Email);
        }

        if (string.IsNullOrWhiteSpace(normalizedEmail) && LooksLikeEmail(displayName))
        {
            normalizedEmail = NormalizeEmailCandidate(displayName);
        }

        return (currentUserId, normalizedEmail);
    }

    private static bool TryParseStamp(string? stamp, out Guid invitationId)
    {
        invitationId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(stamp))
        {
            return false;
        }

        var separatorIndex = stamp.IndexOf('|');
        if (separatorIndex <= 0)
        {
            return Guid.TryParse(stamp, out invitationId);
        }

        var idPart = stamp[..separatorIndex];
        return Guid.TryParse(idPart, out invitationId);
    }

    private static Guid? GetCurrentUserId(ClaimsPrincipal user)
    {
        var idClaim = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(idClaim, out var parsedId) ? parsedId : null;
    }

    private static string NormalizeCategory(string category) => category.Trim().ToLowerInvariant();

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

    public record DismissNotificationRequest
    {
        [Required]
        public string Category { get; init; } = string.Empty;

        [Required]
        public string Stamp { get; init; } = string.Empty;
    }

    public record DismissNotificationResponse(IReadOnlyDictionary<string, IReadOnlyCollection<string>> DismissedStamps);
}
