using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Mail;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Controllers;

[Route("wine-surfer")]
public class WineSurferController : Controller
{
    private static readonly IReadOnlyDictionary<string, (double Longitude, double Latitude)> RegionCoordinates =
        new Dictionary<string, (double Longitude, double Latitude)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Bordeaux"] = (-0.58, 44.84),
            ["Burgundy"] = (4.75, 47.0),
            ["Champagne"] = (4.05, 49.05),
            ["Rhône"] = (4.8, 45.0),
            ["Rhone"] = (4.8, 45.0),
            ["Loire"] = (-0.5, 47.5),
            ["Provence"] = (6.2, 43.5),
            ["Tuscany"] = (11.0, 43.4),
            ["Piedmont"] = (8.0, 44.7),
            ["Veneto"] = (11.5, 45.5),
            ["Ribera del Duero"] = (-3.75, 41.7),
            ["Ribera Del Duero"] = (-3.75, 41.7),
            ["Rioja"] = (-2.43, 42.4),
            ["Douro"] = (-7.8, 41.1),
            ["Douro Valley"] = (-7.8, 41.1),
            ["Mosel"] = (6.7, 49.8),
            ["Rheingau"] = (8.0, 50.0),
            ["Nahe"] = (7.75, 49.8),
            ["Finger Lakes"] = (-76.9, 42.7),
            ["Napa Valley"] = (-122.3, 38.5),
            ["Sonoma"] = (-122.5, 38.3),
            ["Willamette Valley"] = (-123.0, 45.2),
            ["Columbia Valley"] = (-119.5, 46.2),
            ["Marlborough"] = (173.9, -41.5),
            ["Central Otago"] = (169.2, -45.0),
            ["Barossa"] = (138.95, -34.5),
            ["McLaren Vale"] = (138.5, -35.2),
            ["Mc Laren Vale"] = (138.5, -35.2),
            ["Yarra Valley"] = (145.5, -37.7),
            ["Coonawarra"] = (140.8, -37.3),
            ["Maipo"] = (-70.55, -33.6),
            ["Maipo Valley"] = (-70.55, -33.6),
            ["Mendoza"] = (-68.85, -32.9),
            ["Mendoza Valley"] = (-68.85, -32.9),
            ["Stellenbosch"] = (18.86, -33.9)
        };

    private static readonly IReadOnlyDictionary<string, (double Longitude, double Latitude)> CountryCoordinates =
        new Dictionary<string, (double Longitude, double Latitude)>(StringComparer.OrdinalIgnoreCase)
        {
            ["France"] = (2.21, 46.23),
            ["Italy"] = (12.57, 41.87),
            ["Spain"] = (-3.75, 40.46),
            ["Portugal"] = (-8.0, 39.69),
            ["Germany"] = (10.45, 51.17),
            ["Austria"] = (14.55, 47.52),
            ["Switzerland"] = (8.23, 46.82),
            ["United States"] = (-98.58, 39.83),
            ["United States of America"] = (-98.58, 39.83),
            ["USA"] = (-98.58, 39.83),
            ["U.S.A."] = (-98.58, 39.83),
            ["US"] = (-98.58, 39.83),
            ["Canada"] = (-106.35, 56.13),
            ["Chile"] = (-70.67, -33.45),
            ["Argentina"] = (-63.62, -38.42),
            ["Australia"] = (133.78, -25.27),
            ["New Zealand"] = (174.78, -41.28),
            ["South Africa"] = (22.94, -30.56),
            ["England"] = (-1.17, 52.36),
            ["United Kingdom"] = (-3.44, 55.38),
            ["UK"] = (-3.44, 55.38),
            ["Scotland"] = (-4.2, 56.82),
            ["Ireland"] = (-8.0, 53.41),
            ["Japan"] = (138.25, 36.2),
            ["China"] = (104.2, 35.86),
            ["Georgia"] = (43.36, 42.32),
            ["Greece"] = (22.0, 39.07),
            ["Hungary"] = (19.5, 47.16),
            ["Slovenia"] = (14.82, 46.15),
            ["Croatia"] = (15.2, 45.1),
            ["Uruguay"] = (-55.77, -32.52)
        };

    private readonly IWineRepository _wineRepository;
    private readonly IUserRepository _userRepository;
    private readonly ISisterhoodRepository _sisterhoodRepository;
    private readonly ISisterhoodInvitationRepository _sisterhoodInvitationRepository;
    private readonly ISipSessionRepository _sipSessionRepository;

    public WineSurferController(
        IWineRepository wineRepository,
        IUserRepository userRepository,
        ISisterhoodRepository sisterhoodRepository,
        ISisterhoodInvitationRepository sisterhoodInvitationRepository,
        ISipSessionRepository sipSessionRepository)
    {
        _wineRepository = wineRepository;
        _userRepository = userRepository;
        _sisterhoodRepository = sisterhoodRepository;
        _sisterhoodInvitationRepository = sisterhoodInvitationRepository;
        _sipSessionRepository = sipSessionRepository;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var wines = await _wineRepository.GetAllAsync(cancellationToken);

        var highlightPoints = wines
            .Where(w => w.SubAppellation?.Appellation?.Region is not null)
            .Select(w => new
            {
                Wine = w,
                Region = w.SubAppellation!.Appellation!.Region!
            })
            .GroupBy(entry => entry.Region.Id)
            .Select(group =>
            {
                var region = group.First().Region;
                var metrics = CalculateRegionInventoryMetrics(group.Select(entry => entry.Wine));
                return CreateHighlightPoint(region, metrics);
            })
            .Where(point => point is not null)
            .Cast<MapHighlightPoint>()
            .OrderBy(point => point.Label)
            .ToList();

        WineSurferCurrentUser? currentUser = null;
        if (User?.Identity?.IsAuthenticated == true)
        {
            var displayName = User.Identity?.Name;
            ApplicationUser? domainUser = null;

            if (!string.IsNullOrWhiteSpace(displayName))
            {
                domainUser = await _userRepository.FindByNameAsync(displayName, cancellationToken);
            }

            var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");

            if (!string.IsNullOrWhiteSpace(displayName) || !string.IsNullOrWhiteSpace(email) || domainUser is not null)
            {
                currentUser = new WineSurferCurrentUser(
                    domainUser?.Id,
                    displayName ?? email ?? string.Empty,
                    email,
                    domainUser?.TasteProfile);
            }
        }

        var model = new WineSurferLandingViewModel(highlightPoints, currentUser);
        Response.ContentType = "text/html; charset=utf-8";
        return View("Index", model);
    }

    [HttpGet("sisterhoods")]
    public async Task<IActionResult> Sisterhoods(CancellationToken cancellationToken)
    {
        Response.ContentType = "text/html; charset=utf-8";

        var statusMessage = TempData["SisterhoodStatus"] as string;
        var errorMessage = TempData["SisterhoodError"] as string;

        var isAuthenticated = User?.Identity?.IsAuthenticated == true;
        string? displayName = null;
        Guid? currentUserId = null;
        IReadOnlyList<WineSurferSisterhoodSummary> sisterhoods = Array.Empty<WineSurferSisterhoodSummary>();
        IReadOnlyList<WineSurferIncomingSisterhoodInvitation> incomingInvitations = Array.Empty<WineSurferIncomingSisterhoodInvitation>();

        if (isAuthenticated)
        {
            displayName = User.Identity?.Name;
            var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
            var normalizedEmail = NormalizeEmailCandidate(email);

            currentUserId = GetCurrentUserId();

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

            if (currentUserId.HasValue)
            {
                var membership = await _sisterhoodRepository.GetForUserAsync(currentUserId.Value, cancellationToken);
                sisterhoods = membership
                    .Select(s =>
                    {
                        var memberEntries = s.Memberships
                            .Select(m =>
                            {
                                var displayName = string.IsNullOrWhiteSpace(m.User?.Name)
                                    ? m.User?.UserName ?? "Member"
                                    : m.User!.Name;

                                var member = new WineSurferSisterhoodMember(
                                    m.UserId,
                                    displayName,
                                    m.IsAdmin,
                                    m.UserId == currentUserId,
                                    GetAvatarLetter(displayName));

                                return new
                                {
                                    DisplayName = displayName,
                                    Member = member
                                };
                            })
                            .OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        var memberSummaries = memberEntries
                            .Select(entry => entry.Member)
                            .ToList();

                        var isAdmin = s.Memberships.Any(m => m.UserId == currentUserId && m.IsAdmin);
                        var favoriteRegion = CalculateFavoriteRegion(s);
                        var pendingInvitations = (s.Invitations ?? Array.Empty<SisterhoodInvitation>())
                            .Where(invitation => invitation.Status == SisterhoodInvitationStatus.Pending)
                            .Select(invitation =>
                            {
                                var inviteeName = string.IsNullOrWhiteSpace(invitation.InviteeUser?.Name)
                                    ? invitation.InviteeUser?.UserName
                                    : invitation.InviteeUser!.Name;

                                var summary = new WineSurferSisterhoodInvitationSummary(
                                    invitation.Id,
                                    invitation.InviteeEmail,
                                    invitation.Status,
                                    invitation.CreatedAt,
                                    invitation.UpdatedAt,
                                    invitation.InviteeUserId,
                                    inviteeName);

                                var sortKey = string.IsNullOrWhiteSpace(inviteeName)
                                    ? invitation.InviteeEmail
                                    : inviteeName;

                                return new { SortKey = sortKey, Summary = summary };
                            })
                            .OrderBy(entry => entry.SortKey, StringComparer.OrdinalIgnoreCase)
                            .Select(entry => entry.Summary)
                            .ToList();

                        var sessionSummaries = (s.SipSessions ?? Array.Empty<SipSession>())
                            .Select(session => new
                            {
                                SortKey = session.ScheduledAt ?? session.Date ?? session.CreatedAt,
                                Session = new WineSurferSipSessionSummary(
                                    session.Id,
                                    session.Name,
                                    session.Description,
                                    session.ScheduledAt,
                                    session.Date,
                                    session.Location,
                                    session.CreatedAt,
                                    session.UpdatedAt)
                            })
                            .OrderBy(entry => entry.SortKey)
                            .ThenBy(entry => entry.Session.Name, StringComparer.OrdinalIgnoreCase)
                            .Select(entry => entry.Session)
                            .ToList();

                        return new WineSurferSisterhoodSummary(
                            s.Id,
                            s.Name,
                            s.Description,
                            memberSummaries.Count,
                            isAdmin,
                            memberSummaries,
                            favoriteRegion,
                            pendingInvitations,
                            sessionSummaries);
                    })
                    .ToList();

                incomingInvitations = (await _sisterhoodInvitationRepository.GetForInviteeAsync(currentUserId, normalizedEmail, cancellationToken))
                    .Select(invitation =>
                    {
                        var matchesUserId = currentUserId.HasValue && invitation.InviteeUserId == currentUserId.Value;
                        var matchesEmail = normalizedEmail is not null && string.Equals(invitation.InviteeEmail, normalizedEmail, StringComparison.Ordinal);
                        return new WineSurferIncomingSisterhoodInvitation(
                            invitation.Id,
                            invitation.SisterhoodId,
                            invitation.Sisterhood?.Name ?? "Sisterhood",
                            invitation.Sisterhood?.Description,
                            invitation.InviteeEmail,
                            invitation.Status,
                            invitation.CreatedAt,
                            invitation.UpdatedAt,
                            invitation.InviteeUserId,
                            matchesUserId,
                            matchesEmail);
                    })
                    .ToList();
            }
        }

        var model = new WineSurferSisterhoodsViewModel(isAuthenticated, displayName, sisterhoods, currentUserId, statusMessage, errorMessage, incomingInvitations);
        return View("~/Views/Sisterhoods/Index.cshtml", model);
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        var users = await _userRepository.GetAllAsync(cancellationToken);

        var response = users
            .Where(u => !string.IsNullOrWhiteSpace(u.Name))
            .OrderBy(u => u.Name)
            .Select(u => new WineSurferUserSummary(u.Id, u.Name, u.TasteProfile ?? string.Empty))
            .ToList();

        return Json(response);
    }

    [Authorize]
    [HttpPost("sisterhoods/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSisterhood(CreateSisterhoodRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["SisterhoodError"] = "Please provide a name for your sisterhood.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        try
        {
            var sisterhood = await _sisterhoodRepository.CreateWithAdminAsync(
                request.Name,
                request.Description,
                currentUserId.Value,
                cancellationToken);

            TempData["SisterhoodStatus"] = $"Created '{sisterhood.Name}'. You're the first admin.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["SisterhoodError"] = ex.Message;
        }
        catch (Exception)
        {
            TempData["SisterhoodError"] = "We couldn't create that sisterhood just now. Please try again.";
        }

        return RedirectToAction(nameof(Sisterhoods));
    }

    [Authorize]
    [HttpPost("sisterhoods/update-details")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSisterhoodDetails(UpdateSisterhoodDetailsRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || request.SisterhoodId == Guid.Empty)
        {
            TempData["SisterhoodError"] = "We couldn't update that sisterhood.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var trimmedNameCandidate = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedNameCandidate))
        {
            TempData["SisterhoodError"] = "Please provide a valid sisterhood name.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var trimmedName = trimmedNameCandidate!;
        var trimmedDescription = string.IsNullOrWhiteSpace(request.Description)
            ? null
            : request.Description!.Trim();

        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        var sisterhood = await _sisterhoodRepository.GetByIdAsync(request.SisterhoodId, cancellationToken);
        if (sisterhood is null)
        {
            TempData["SisterhoodError"] = "That sisterhood was already removed.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var isAdmin = await _sisterhoodRepository.IsAdminAsync(request.SisterhoodId, currentUserId.Value, cancellationToken);
        if (!isAdmin)
        {
            TempData["SisterhoodError"] = "Only admins can update sisterhood details.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var existingDescription = sisterhood.Description ?? string.Empty;
        var nextDescription = trimmedDescription ?? string.Empty;
        if (string.Equals(sisterhood.Name, trimmedName, StringComparison.Ordinal)
            && string.Equals(existingDescription, nextDescription, StringComparison.Ordinal))
        {
            TempData["SisterhoodStatus"] = "No changes to save.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        try
        {
            await _sisterhoodRepository.UpdateAsync(new Sisterhood
            {
                Id = request.SisterhoodId,
                Name = trimmedName,
                Description = trimmedDescription,
            }, cancellationToken);

            TempData["SisterhoodStatus"] = $"Updated '{trimmedName}'.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["SisterhoodError"] = ex.Message;
        }
        catch (DbUpdateException)
        {
            TempData["SisterhoodError"] = "We couldn't update that sisterhood right now. Please try again.";
        }

        return RedirectToAction(nameof(Sisterhoods));
    }

    [Authorize]
    [HttpPost("sisterhoods/invite-member")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InviteSisterhoodMember(InviteSisterhoodMemberRequest request, CancellationToken cancellationToken)
    {
        var expectsJson = Request.Headers["Accept"].Any(h => h.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            || Request.Headers["X-Requested-With"].Any(h => string.Equals(h, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase));

        IActionResult Error(string message, int statusCode = StatusCodes.Status400BadRequest)
        {
            if (expectsJson)
            {
                return StatusCode(statusCode, new { success = false, message });
            }

            TempData["SisterhoodError"] = message;
            return RedirectToAction(nameof(Sisterhoods));
        }

        IActionResult Success(string message, bool inviteeExists, string? inviteeEmail, string? mailtoLink)
        {
            if (expectsJson)
            {
                return Ok(new
                {
                    success = true,
                    message,
                    inviteeExists,
                    inviteeEmail,
                    mailtoLink,
                    shouldLaunchMailClient = !inviteeExists && !string.IsNullOrEmpty(mailtoLink)
                });
            }

            TempData["SisterhoodStatus"] = message;
            return RedirectToAction(nameof(Sisterhoods));
        }

        if (!ModelState.IsValid || request.SisterhoodId == Guid.Empty)
        {
            return Error("We couldn't understand that invite.");
        }

        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            if (expectsJson)
            {
                return Unauthorized(new { success = false, message = "You need to sign in before inviting members." });
            }

            return Challenge();
        }

        var isAdmin = await _sisterhoodRepository.IsAdminAsync(request.SisterhoodId, currentUserId.Value, cancellationToken);
        if (!isAdmin)
        {
            return Error("Only sisterhood admins can invite members.", StatusCodes.Status403Forbidden);
        }

        var sisterhood = await _sisterhoodRepository.GetByIdAsync(request.SisterhoodId, cancellationToken);
        if (sisterhood is null)
        {
            return Error("That sisterhood no longer exists.", StatusCodes.Status404NotFound);
        }

        ApplicationUser? invitee = null;
        if (request.UserId.HasValue)
        {
            invitee = await _userRepository.GetByIdAsync(request.UserId.Value, cancellationToken);
        }

        var trimmedName = string.IsNullOrWhiteSpace(request.MemberName)
            ? null
            : request.MemberName.Trim();
        var normalizedEmailCandidate = NormalizeEmailCandidate(request.MemberEmail);
        var nameLooksLikeEmail = request.IsEmail || LooksLikeEmail(trimmedName);

        if (invitee is null && string.IsNullOrEmpty(normalizedEmailCandidate) && nameLooksLikeEmail)
        {
            normalizedEmailCandidate = NormalizeEmailCandidate(trimmedName);
        }

        if (invitee is null && !string.IsNullOrEmpty(normalizedEmailCandidate))
        {
            invitee = await _userRepository.FindByEmailAsync(normalizedEmailCandidate, cancellationToken);
        }

        if (invitee is null && !string.IsNullOrEmpty(trimmedName) && !nameLooksLikeEmail)
        {
            invitee = await _userRepository.FindByNameAsync(trimmedName, cancellationToken);
        }

        string? inviteeEmail = null;
        if (invitee is not null)
        {
            inviteeEmail = NormalizeEmailCandidate(invitee.Email);

            if (string.IsNullOrEmpty(inviteeEmail) && !string.IsNullOrEmpty(normalizedEmailCandidate))
            {
                inviteeEmail = normalizedEmailCandidate;
            }
        }

        if (string.IsNullOrEmpty(inviteeEmail) && !string.IsNullOrEmpty(normalizedEmailCandidate))
        {
            inviteeEmail = normalizedEmailCandidate;
        }

        if (invitee is null && string.IsNullOrEmpty(inviteeEmail))
        {
            if (!string.IsNullOrEmpty(trimmedName) && !nameLooksLikeEmail)
            {
                return Error("We couldn't find that Wine Surfer user.");
            }

            return Error("Please provide a valid email address for the invitation.");
        }

        if (string.IsNullOrEmpty(inviteeEmail))
        {
            return Error("We couldn't determine where to send that invitation.");
        }

        if (invitee is not null && invitee.Id == currentUserId)
        {
            return Error("You're already part of this sisterhood.");
        }

        if (invitee is not null)
        {
            var existingMembership = await _sisterhoodRepository.GetMembershipAsync(request.SisterhoodId, invitee.Id, cancellationToken);
            if (existingMembership is not null)
            {
                var inviteeDisplayName = string.IsNullOrWhiteSpace(invitee.Name)
                    ? invitee.UserName ?? inviteeEmail
                    : invitee.Name;

                return Error($"{inviteeDisplayName} is already a member of {sisterhood.Name}.", StatusCodes.Status409Conflict);
            }
        }

        SisterhoodInvitation? existingInvitation = null;
        try
        {
            existingInvitation = await _sisterhoodInvitationRepository.GetAsync(request.SisterhoodId, inviteeEmail, cancellationToken);
        }
        catch (ArgumentException)
        {
            return Error("That doesn't look like a valid email address.");
        }

        try
        {
            await _sisterhoodInvitationRepository.CreateOrUpdatePendingAsync(
                request.SisterhoodId,
                inviteeEmail,
                invitee?.Id,
                cancellationToken);
        }
        catch (ArgumentException)
        {
            return Error("That doesn't look like a valid email address.");
        }
        catch (Exception)
        {
            return Error("We couldn't send that invitation right now.", StatusCodes.Status500InternalServerError);
        }

        var inviteeLabel = invitee is not null
            ? (!string.IsNullOrWhiteSpace(invitee.Name)
                ? invitee.Name
                : invitee.UserName ?? inviteeEmail)
            : inviteeEmail;

        string statusMessage = existingInvitation switch
        {
            null => $"Invitation sent to {inviteeLabel}.",
            { Status: SisterhoodInvitationStatus.Pending } => $"An invitation is already pending for {inviteeLabel}. We've refreshed it.",
            _ => $"Invitation reactivated for {inviteeLabel}.",
        };

        var fullStatusMessage = statusMessage + " They'll be able to join once they accept.";
        string? mailtoLink = null;
        if (invitee is null && !string.IsNullOrEmpty(inviteeEmail))
        {
            var subject = $"Join {sisterhood.Name} on Wine Surfer";
            var signupLink = Url.ActionLink("Register", "Account")
                ?? Url.Action("Register", "Account")
                ?? "/account/register";
            var body = $"We'd love to have you in the {sisterhood.Name} sisterhood on Wine Surfer. Sign up at {signupLink} using this email address and accept the invite we just sent.";
            mailtoLink = $"mailto:{inviteeEmail}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
        }

        return Success(fullStatusMessage, invitee is not null, inviteeEmail, mailtoLink);
    }

    [Authorize]
    [HttpPost("sisterhoods/invitations/accept")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AcceptSisterhoodInvitation(ManageSisterhoodInvitationRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || request.InvitationId == Guid.Empty)
        {
            TempData["SisterhoodError"] = "We couldn't process that invitation.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        var invitation = await _sisterhoodInvitationRepository.GetByIdAsync(request.InvitationId, cancellationToken);
        if (invitation is null)
        {
            TempData["SisterhoodError"] = "That invitation is no longer available.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var normalizedEmail = NormalizeEmailCandidate(User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email"));
        if (string.IsNullOrWhiteSpace(normalizedEmail) && LooksLikeEmail(User.Identity?.Name))
        {
            normalizedEmail = NormalizeEmailCandidate(User.Identity?.Name);
        }

        if (normalizedEmail is null)
        {
            var domainUser = await _userRepository.GetByIdAsync(currentUserId.Value, cancellationToken);
            normalizedEmail = NormalizeEmailCandidate(domainUser?.Email);
        }

        var matchesUserId = invitation.InviteeUserId == currentUserId.Value;
        var matchesEmail = normalizedEmail is not null && string.Equals(invitation.InviteeEmail, normalizedEmail, StringComparison.Ordinal);

        if (!matchesUserId && !matchesEmail)
        {
            TempData["SisterhoodError"] = "That invitation isn't assigned to you.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        if (invitation.Status == SisterhoodInvitationStatus.Accepted)
        {
            TempData["SisterhoodStatus"] = $"You're already part of {invitation.Sisterhood?.Name ?? "that sisterhood"}.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        if (invitation.Status != SisterhoodInvitationStatus.Pending)
        {
            TempData["SisterhoodError"] = "That invitation is no longer open.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var added = await _sisterhoodRepository.AddUserToSisterhoodAsync(invitation.SisterhoodId, currentUserId.Value, cancellationToken);
        var alreadyMember = false;
        if (!added)
        {
            var membership = await _sisterhoodRepository.GetMembershipAsync(invitation.SisterhoodId, currentUserId.Value, cancellationToken);
            if (membership is null)
            {
                TempData["SisterhoodError"] = "We couldn't add you to that sisterhood right now.";
                return RedirectToAction(nameof(Sisterhoods));
            }

            alreadyMember = true;
        }

        await _sisterhoodInvitationRepository.UpdateStatusAsync(invitation.SisterhoodId, invitation.InviteeEmail, SisterhoodInvitationStatus.Accepted, currentUserId.Value, cancellationToken);

        var sisterhoodName = invitation.Sisterhood?.Name ?? "this sisterhood";
        TempData["SisterhoodStatus"] = alreadyMember
            ? $"You're already part of {sisterhoodName}."
            : $"You're now part of {sisterhoodName}.";

        return RedirectToAction(nameof(Sisterhoods));
    }

    [Authorize]
    [HttpPost("sisterhoods/invitations/decline")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeclineSisterhoodInvitation(ManageSisterhoodInvitationRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || request.InvitationId == Guid.Empty)
        {
            TempData["SisterhoodError"] = "We couldn't process that invitation.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        var invitation = await _sisterhoodInvitationRepository.GetByIdAsync(request.InvitationId, cancellationToken);
        if (invitation is null)
        {
            TempData["SisterhoodError"] = "That invitation is no longer available.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var normalizedEmail = NormalizeEmailCandidate(User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email"));
        if (string.IsNullOrWhiteSpace(normalizedEmail) && LooksLikeEmail(User.Identity?.Name))
        {
            normalizedEmail = NormalizeEmailCandidate(User.Identity?.Name);
        }

        if (normalizedEmail is null)
        {
            var domainUser = await _userRepository.GetByIdAsync(currentUserId.Value, cancellationToken);
            normalizedEmail = NormalizeEmailCandidate(domainUser?.Email);
        }

        var matchesUserId = invitation.InviteeUserId == currentUserId.Value;
        var matchesEmail = normalizedEmail is not null && string.Equals(invitation.InviteeEmail, normalizedEmail, StringComparison.Ordinal);

        if (!matchesUserId && !matchesEmail)
        {
            TempData["SisterhoodError"] = "That invitation isn't assigned to you.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        if (invitation.Status == SisterhoodInvitationStatus.Declined)
        {
            TempData["SisterhoodStatus"] = "You've already declined that invitation.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        if (invitation.Status == SisterhoodInvitationStatus.Accepted)
        {
            TempData["SisterhoodError"] = "You've already accepted that invitation.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        if (invitation.Status != SisterhoodInvitationStatus.Pending)
        {
            TempData["SisterhoodError"] = "That invitation is no longer open.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        await _sisterhoodInvitationRepository.UpdateStatusAsync(invitation.SisterhoodId, invitation.InviteeEmail, SisterhoodInvitationStatus.Declined, currentUserId.Value, cancellationToken);

        var sisterhoodName = invitation.Sisterhood?.Name ?? "that sisterhood";
        TempData["SisterhoodStatus"] = $"Declined the invitation to {sisterhoodName}.";

        return RedirectToAction(nameof(Sisterhoods));
    }

    [Authorize]
    [HttpPost("sisterhoods/invitations/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSisterhoodInvitation(ManageSisterhoodInvitationRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || request.InvitationId == Guid.Empty)
        {
            TempData["SisterhoodError"] = "We couldn't process that invitation.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        var invitation = await _sisterhoodInvitationRepository.GetByIdAsync(request.InvitationId, cancellationToken);
        if (invitation is null)
        {
            TempData["SisterhoodError"] = "That invitation was already removed.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var normalizedEmail = NormalizeEmailCandidate(User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email"));
        if (string.IsNullOrWhiteSpace(normalizedEmail) && LooksLikeEmail(User.Identity?.Name))
        {
            normalizedEmail = NormalizeEmailCandidate(User.Identity?.Name);
        }

        if (normalizedEmail is null)
        {
            var domainUser = await _userRepository.GetByIdAsync(currentUserId.Value, cancellationToken);
            normalizedEmail = NormalizeEmailCandidate(domainUser?.Email);
        }

        var matchesUserId = invitation.InviteeUserId == currentUserId.Value;
        var matchesEmail = normalizedEmail is not null && string.Equals(invitation.InviteeEmail, normalizedEmail, StringComparison.Ordinal);

        if (!matchesUserId && !matchesEmail)
        {
            TempData["SisterhoodError"] = "That invitation isn't assigned to you.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var deleted = await _sisterhoodInvitationRepository.DeleteAsync(request.InvitationId, cancellationToken);
        if (!deleted)
        {
            TempData["SisterhoodError"] = "We couldn't remove that invitation right now.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        TempData["SisterhoodStatus"] = "Invitation removed.";
        return RedirectToAction(nameof(Sisterhoods));
    }

    [Authorize]
    [HttpPost("sisterhoods/remove-member")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveSisterhoodMember(ModifySisterhoodMemberRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || request.SisterhoodId == Guid.Empty || request.UserId == Guid.Empty)
        {
            TempData["SisterhoodError"] = "We couldn't process that removal.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        var isSelfRemoval = request.UserId == currentUserId.Value;

        if (!isSelfRemoval)
        {
            var isAdmin = await _sisterhoodRepository.IsAdminAsync(request.SisterhoodId, currentUserId.Value, cancellationToken);
            if (!isAdmin)
            {
                TempData["SisterhoodError"] = "Only admins can remove members.";
                return RedirectToAction(nameof(Sisterhoods));
            }
        }

        var membership = await _sisterhoodRepository.GetMembershipAsync(request.SisterhoodId, request.UserId, cancellationToken);
        if (membership is null)
        {
            TempData["SisterhoodError"] = "That member is no longer part of this sisterhood.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        if (!isSelfRemoval && membership.IsAdmin)
        {
            TempData["SisterhoodError"] = "Admins can only remove non-admin members. Update their role before removing them.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        if (membership.IsAdmin)
        {
            var allMemberships = await _sisterhoodRepository.GetMembershipsAsync(request.SisterhoodId, cancellationToken);
            var adminCount = allMemberships.Count(m => m.IsAdmin);
            if (adminCount <= 1)
            {
                TempData["SisterhoodError"] = isSelfRemoval
                    ? "You need at least one admin in the sisterhood. Promote another member before leaving."
                    : "You need at least one admin in the sisterhood. Promote another member before removing this admin.";
                return RedirectToAction(nameof(Sisterhoods));
            }
        }

        var removed = await _sisterhoodRepository.RemoveUserFromSisterhoodAsync(request.SisterhoodId, request.UserId, cancellationToken);
        if (!removed)
        {
            if (membership.IsAdmin)
            {
                TempData["SisterhoodError"] = isSelfRemoval
                    ? "You need at least one admin in the sisterhood. Promote another member before leaving."
                    : "You need at least one admin in the sisterhood. Promote another member before removing this admin.";
            }
            else
            {
                TempData["SisterhoodError"] = isSelfRemoval
                    ? "We couldn't remove you from that sisterhood right now."
                    : "We couldn't remove that member right now.";
            }

            return RedirectToAction(nameof(Sisterhoods));
        }

        var sisterhood = await _sisterhoodRepository.GetByIdAsync(request.SisterhoodId, cancellationToken);
        if (isSelfRemoval)
        {
            TempData["SisterhoodStatus"] = sisterhood is null
                ? "You left the sisterhood."
                : $"You left {sisterhood.Name}.";
        }
        else
        {
            TempData["SisterhoodStatus"] = sisterhood is null
                ? "Member removed."
                : $"Removed {membership.User?.Name ?? "that member"} from {sisterhood.Name}.";
        }

        return RedirectToAction(nameof(Sisterhoods));
    }

    [Authorize]
    [HttpPost("sisterhoods/update-admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSisterhoodAdmin(UpdateSisterhoodAdminRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || request.SisterhoodId == Guid.Empty || request.UserId == Guid.Empty)
        {
            TempData["SisterhoodError"] = "We couldn't update that admin setting.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        var isAdmin = await _sisterhoodRepository.IsAdminAsync(request.SisterhoodId, currentUserId.Value, cancellationToken);
        if (!isAdmin)
        {
            TempData["SisterhoodError"] = "Only admins can change other admins.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var membership = await _sisterhoodRepository.GetMembershipAsync(request.SisterhoodId, request.UserId, cancellationToken);
        if (membership is null)
        {
            TempData["SisterhoodError"] = "That member is no longer part of this sisterhood.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        if (!request.MakeAdmin && membership.IsAdmin)
        {
            var memberships = await _sisterhoodRepository.GetMembershipsAsync(request.SisterhoodId, cancellationToken);
            var adminCount = memberships.Count(m => m.IsAdmin);
            if (adminCount <= 1)
            {
                TempData["SisterhoodError"] = "At least one admin is required. Promote another member before demoting this admin.";
                return RedirectToAction(nameof(Sisterhoods));
            }
        }

        var updated = await _sisterhoodRepository.SetAdminStatusAsync(request.SisterhoodId, request.UserId, request.MakeAdmin, cancellationToken);
        if (!updated)
        {
            TempData["SisterhoodError"] = "We couldn't update that member right now.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var sisterhood = await _sisterhoodRepository.GetByIdAsync(request.SisterhoodId, cancellationToken);
        var targetName = membership.User?.Name ?? "That member";
        TempData["SisterhoodStatus"] = request.MakeAdmin
            ? $"{targetName} is now an admin of {sisterhood?.Name ?? "this sisterhood"}."
            : $"{targetName} is no longer an admin.";

        return RedirectToAction(nameof(Sisterhoods));
    }

    [Authorize]
    [HttpPost("sisterhoods/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSisterhood(DeleteSisterhoodRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || request.SisterhoodId == Guid.Empty)
        {
            TempData["SisterhoodError"] = "We couldn't delete that sisterhood.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        var isAdmin = await _sisterhoodRepository.IsAdminAsync(request.SisterhoodId, currentUserId.Value, cancellationToken);
        if (!isAdmin)
        {
            TempData["SisterhoodError"] = "Only admins can delete a sisterhood.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var sisterhood = await _sisterhoodRepository.GetByIdAsync(request.SisterhoodId, cancellationToken);
        if (sisterhood is null)
        {
            TempData["SisterhoodError"] = "That sisterhood was already removed.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        await _sisterhoodRepository.DeleteAsync(request.SisterhoodId, cancellationToken);
        TempData["SisterhoodStatus"] = $"Deleted {sisterhood.Name}.";

        return RedirectToAction(nameof(Sisterhoods));
    }

    [Authorize]
    [HttpPost("sisterhoods/sessions/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSipSession(CreateSipSessionRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || request.SisterhoodId == Guid.Empty)
        {
            TempData["SisterhoodError"] = "We couldn't plan that sip session.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            TempData["SisterhoodError"] = "Please provide a name for the sip session.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        var isAdmin = await _sisterhoodRepository.IsAdminAsync(request.SisterhoodId, currentUserId.Value, cancellationToken);
        if (!isAdmin)
        {
            TempData["SisterhoodError"] = "Only admins can manage sip sessions.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var sisterhood = await _sisterhoodRepository.GetByIdAsync(request.SisterhoodId, cancellationToken);
        if (sisterhood is null)
        {
            TempData["SisterhoodError"] = "That sisterhood no longer exists.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var sipSession = new SipSession
        {
            SisterhoodId = request.SisterhoodId,
            Name = request.Name,
            Description = request.Description,
            ScheduledAt = NormalizeToUtc(request.ScheduledAt),
            Date = request.ScheduledAt?.Date,
            Location = request.Location ?? string.Empty
        };

        try
        {
            await _sipSessionRepository.AddAsync(sipSession, cancellationToken);

            var scheduledDisplay = request.ScheduledAt?.ToString("f");
            TempData["SisterhoodStatus"] = scheduledDisplay is null
                ? $"Planned '{sipSession.Name}'."
                : $"Planned '{sipSession.Name}' for {scheduledDisplay}.";
        }
        catch (ArgumentException ex)
        {
            TempData["SisterhoodError"] = ex.Message;
        }
        catch (Exception)
        {
            TempData["SisterhoodError"] = "We couldn't plan that sip session right now. Please try again.";
        }

        return RedirectToAction(nameof(Sisterhoods));
    }

    [Authorize]
    [HttpPost("sisterhoods/sessions/update")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSipSession(UpdateSipSessionRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || request.SisterhoodId == Guid.Empty || request.SipSessionId == Guid.Empty)
        {
            TempData["SisterhoodError"] = "We couldn't update that sip session.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            TempData["SisterhoodError"] = "Please provide a name for the sip session.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        var isAdmin = await _sisterhoodRepository.IsAdminAsync(request.SisterhoodId, currentUserId.Value, cancellationToken);
        if (!isAdmin)
        {
            TempData["SisterhoodError"] = "Only admins can manage sip sessions.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var session = await _sipSessionRepository.GetByIdAsync(request.SipSessionId, cancellationToken);
        if (session is null || session.SisterhoodId != request.SisterhoodId)
        {
            TempData["SisterhoodError"] = "That sip session could not be found.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        session.Name = request.Name;
        session.Description = request.Description;
        session.Location = request.Location ?? string.Empty;
        session.ScheduledAt = NormalizeToUtc(request.ScheduledAt);
        session.Date = request.ScheduledAt?.Date;

        try
        {
            await _sipSessionRepository.UpdateAsync(session, cancellationToken);

            var scheduledDisplay = request.ScheduledAt?.ToString("f");
            TempData["SisterhoodStatus"] = scheduledDisplay is null
                ? $"Updated '{session.Name}'."
                : $"Updated '{session.Name}' for {scheduledDisplay}.";
        }
        catch (ArgumentException ex)
        {
            TempData["SisterhoodError"] = ex.Message;
        }
        catch (Exception)
        {
            TempData["SisterhoodError"] = "We couldn't update that sip session right now. Please try again.";
        }

        return RedirectToAction(nameof(Sisterhoods));
    }

    [Authorize]
    [HttpPost("sisterhoods/sessions/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSipSession(DeleteSipSessionRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || request.SisterhoodId == Guid.Empty || request.SipSessionId == Guid.Empty)
        {
            TempData["SisterhoodError"] = "We couldn't remove that sip session.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        var isAdmin = await _sisterhoodRepository.IsAdminAsync(request.SisterhoodId, currentUserId.Value, cancellationToken);
        if (!isAdmin)
        {
            TempData["SisterhoodError"] = "Only admins can manage sip sessions.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var session = await _sipSessionRepository.GetByIdAsync(request.SipSessionId, cancellationToken);
        if (session is null || session.SisterhoodId != request.SisterhoodId)
        {
            TempData["SisterhoodError"] = "That sip session was already removed.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        try
        {
            await _sipSessionRepository.DeleteAsync(request.SipSessionId, cancellationToken);
            TempData["SisterhoodStatus"] = $"Removed '{session.Name}'.";
        }
        catch (Exception)
        {
            TempData["SisterhoodError"] = "We couldn't remove that sip session right now. Please try again.";
        }

        return RedirectToAction(nameof(Sisterhoods));
    }

    private static DateTime? NormalizeToUtc(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var dateTime = value.Value;
        return dateTime.Kind switch
        {
            DateTimeKind.Utc => dateTime,
            DateTimeKind.Local => dateTime.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dateTime, DateTimeKind.Local).ToUniversalTime()
        };
    }

    private Guid? GetCurrentUserId()
    {
        var idClaim = User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(idClaim, out var parsedId) ? parsedId : null;
    }

    private static string GetAvatarLetter(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "?";
        }

        var trimmed = displayName.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? "?"
            : trimmed.Substring(0, 1).ToUpperInvariant();
    }

    private static WineSurferSisterhoodFavoriteRegion? CalculateFavoriteRegion(Sisterhood sisterhood)
    {
        if (sisterhood is null)
        {
            return null;
        }

        var topRegion = sisterhood.Memberships
            .Where(membership => membership.User?.TastingNotes is not null)
            .SelectMany(membership => membership.User!.TastingNotes)
            .Select(note => new
            {
                note.Score,
                Region = note.Bottle?.WineVintage?.Wine?.SubAppellation?.Appellation?.Region
            })
            .Where(entry => entry.Score.HasValue && entry.Region is not null)
            .GroupBy(entry => new
            {
                entry.Region!.Id,
                entry.Region.Name,
                CountryName = entry.Region.Country?.Name
            })
            .Select(group => new
            {
                group.Key.Id,
                group.Key.Name,
                group.Key.CountryName,
                AverageScore = group.Average(entry => entry.Score!.Value)
            })
            .OrderByDescending(entry => entry.AverageScore)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (topRegion is null)
        {
            return null;
        }

        return new WineSurferSisterhoodFavoriteRegion(
            topRegion.Id,
            topRegion.Name,
            topRegion.CountryName,
            Math.Round(topRegion.AverageScore, 2, MidpointRounding.AwayFromZero));
    }

    private static RegionInventoryMetrics CalculateRegionInventoryMetrics(IEnumerable<Wine> wines)
    {
        var bottleList = wines
            .SelectMany(wine => wine.WineVintages ?? Enumerable.Empty<WineVintage>())
            .SelectMany(vintage => vintage.Bottles ?? Enumerable.Empty<Bottle>())
            .ToList();

        var cellared = bottleList.Count(bottle => !bottle.IsDrunk);
        var consumed = bottleList.Count(bottle => bottle.IsDrunk);

        var tastingNotes = bottleList
            .SelectMany(bottle => bottle.TastingNotes ?? Enumerable.Empty<TastingNote>())
            .Where(note => note.Score.HasValue)
            .ToList();

        var scoreValues = tastingNotes
            .Select(note => note.Score!.Value)
            .ToList();

        decimal? averageScore = scoreValues.Count > 0
            ? Math.Round(scoreValues.Average(), 1, MidpointRounding.AwayFromZero)
            : null;

        var userAverageScores = tastingNotes
            .GroupBy(note => note.UserId)
            .Select(group => new RegionUserAverageScore(
                group.Key,
                Math.Round(group.Average(note => note.Score!.Value), 1, MidpointRounding.AwayFromZero)))
            .OrderBy(entry => entry.UserId)
            .ToList();

        return new RegionInventoryMetrics(cellared, consumed, averageScore, userAverageScores);
    }

    public class CreateSisterhoodRequest
    {
        [Required]
        [StringLength(256, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [StringLength(1024)]
        public string? Description { get; set; }
    }

    public class UpdateSisterhoodDetailsRequest
    {
        [Required]
        public Guid SisterhoodId { get; set; }

        [Required]
        [StringLength(256, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [StringLength(1024)]
        public string? Description { get; set; }
    }

    public class InviteSisterhoodMemberRequest : IValidatableObject
    {
        [Required]
        public Guid SisterhoodId { get; set; }

        public Guid? UserId { get; set; }

        [StringLength(256)]
        public string? MemberName { get; set; }

        [StringLength(256)]
        [EmailAddress]
        public string? MemberEmail { get; set; }

        public bool IsEmail { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (UserId.HasValue)
            {
                yield break;
            }

            var trimmedName = string.IsNullOrWhiteSpace(MemberName) ? null : MemberName.Trim();
            var trimmedEmail = string.IsNullOrWhiteSpace(MemberEmail) ? null : MemberEmail.Trim();

            if (trimmedName is null && trimmedEmail is null)
            {
                yield return new ValidationResult(
                    "Please provide a username or email address.",
                    new[] { nameof(MemberName), nameof(MemberEmail) });
                yield break;
            }

            if (IsEmail && trimmedEmail is null)
            {
                yield return new ValidationResult(
                    "A valid email address is required to send an email invite.",
                    new[] { nameof(MemberEmail) });
            }

            if (trimmedEmail is null && trimmedName is not null && trimmedName.Length < 2)
            {
                yield return new ValidationResult(
                    "Usernames must be at least two characters.",
                    new[] { nameof(MemberName) });
            }
        }
    }

    public class ManageSisterhoodInvitationRequest
    {
        [Required]
        public Guid InvitationId { get; set; }
    }

    public class ModifySisterhoodMemberRequest
    {
        [Required]
        public Guid SisterhoodId { get; set; }

        [Required]
        public Guid UserId { get; set; }
    }

    public class UpdateSisterhoodAdminRequest : ModifySisterhoodMemberRequest
    {
        public bool MakeAdmin { get; set; }
    }

    public class DeleteSisterhoodRequest
    {
        [Required]
        public Guid SisterhoodId { get; set; }
    }

    public abstract class SipSessionRequestBase
    {
        [Required]
        public Guid SisterhoodId { get; set; }

        [Required]
        [StringLength(256)]
        public string Name { get; set; } = string.Empty;

        [StringLength(2048)]
        public string? Description { get; set; }

        [StringLength(256)]
        public string? Location { get; set; }

        public DateTime? ScheduledAt { get; set; }
    }

    public class CreateSipSessionRequest : SipSessionRequestBase
    {
    }

    public class UpdateSipSessionRequest : SipSessionRequestBase
    {
        [Required]
        public Guid SipSessionId { get; set; }
    }

    public class DeleteSipSessionRequest
    {
        [Required]
        public Guid SisterhoodId { get; set; }

        [Required]
        public Guid SipSessionId { get; set; }
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

    private static MapHighlightPoint? CreateHighlightPoint(
        Region region,
        RegionInventoryMetrics metrics)
    {
        var countryName = region.Country?.Name;
        if (!string.IsNullOrWhiteSpace(region.Name) && RegionCoordinates.TryGetValue(region.Name, out var regionCoord))
        {
            return new MapHighlightPoint(
                region.Name,
                countryName ?? string.Empty,
                regionCoord.Latitude,
                regionCoord.Longitude,
                metrics.BottlesCellared,
                metrics.BottlesConsumed,
                metrics.AverageScore,
                metrics.UserAverageScores);
        }

        if (!string.IsNullOrWhiteSpace(countryName) && CountryCoordinates.TryGetValue(countryName, out var countryCoord))
        {
            var label = string.IsNullOrWhiteSpace(region.Name)
                ? countryName
                : $"{region.Name}, {countryName}";
            return new MapHighlightPoint(
                label,
                countryName,
                countryCoord.Latitude,
                countryCoord.Longitude,
                metrics.BottlesCellared,
                metrics.BottlesConsumed,
                metrics.AverageScore,
                metrics.UserAverageScores);
        }

        return null;
    }
}

public record WineSurferLandingViewModel(IReadOnlyList<MapHighlightPoint> HighlightPoints, WineSurferCurrentUser? CurrentUser);

public record WineSurferSisterhoodsViewModel(
    bool IsAuthenticated,
    string? DisplayName,
    IReadOnlyList<WineSurferSisterhoodSummary> Sisterhoods,
    Guid? CurrentUserId,
    string? StatusMessage,
    string? ErrorMessage,
    IReadOnlyList<WineSurferIncomingSisterhoodInvitation> IncomingInvitations);

public record WineSurferSisterhoodSummary(
    Guid Id,
    string Name,
    string? Description,
    int MemberCount,
    bool CanManage,
    IReadOnlyList<WineSurferSisterhoodMember> Members,
    WineSurferSisterhoodFavoriteRegion? FavoriteRegion,
    IReadOnlyList<WineSurferSisterhoodInvitationSummary> PendingInvitations,
    IReadOnlyList<WineSurferSipSessionSummary> SipSessions);

public record WineSurferSipSessionSummary(
    Guid Id,
    string Name,
    string? Description,
    DateTime? ScheduledAtUtc,
    DateTime? Date,
    string Location,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public record WineSurferSisterhoodMember(Guid Id, string DisplayName, bool IsAdmin, bool IsCurrentUser, string AvatarLetter);

public record WineSurferSisterhoodFavoriteRegion(Guid RegionId, string Name, string? CountryName, decimal AverageScore);

public record WineSurferSisterhoodInvitationSummary(
    Guid Id,
    string Email,
    SisterhoodInvitationStatus Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    Guid? InviteeUserId,
    string? InviteeName);

public record MapHighlightPoint(
    string Label,
    string? Country,
    double Latitude,
    double Longitude,
    int BottlesCellared,
    int BottlesConsumed,
    decimal? AverageScore,
    IReadOnlyList<RegionUserAverageScore> UserAverageScores);

public record WineSurferIncomingSisterhoodInvitation(
    Guid Id,
    Guid SisterhoodId,
    string SisterhoodName,
    string? SisterhoodDescription,
    string InviteeEmail,
    SisterhoodInvitationStatus Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    Guid? InviteeUserId,
    bool MatchesUserId,
    bool MatchesEmail);

public record RegionInventoryMetrics(
    int BottlesCellared,
    int BottlesConsumed,
    decimal? AverageScore,
    IReadOnlyList<RegionUserAverageScore> UserAverageScores);

public record RegionUserAverageScore(Guid UserId, decimal AverageScore);

public record WineSurferUserSummary(Guid Id, string Name, string TasteProfile);
public record WineSurferCurrentUser(Guid? DomainUserId, string DisplayName, string? Email, string? TasteProfile);
