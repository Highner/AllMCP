using System;
using System.ClientModel;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;
using AllMCPSolution.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Identity;
using OpenAI.Chat;
using AllMCPSolution.Services;

namespace AllMCPSolution.Controllers;

[Route("wine-surfer")]
public class WineSurferController : WineSurferControllerBase
{
    [Route("debug/config")]
    public IActionResult ConfigTest([FromServices] IConfiguration config)
    {
        var apiKey = config["OpenAI:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
            return Ok("OpenAI API key not found 😕");
        return Ok("OpenAI API key is loaded ✅");
    }

   

    private readonly IWineRepository _wineRepository;

    private readonly ISisterhoodRepository _sisterhoodRepository;
    private readonly ISisterhoodInvitationRepository _sisterhoodInvitationRepository;
    private readonly ISipSessionRepository _sipSessionRepository;
    private readonly IBottleRepository _bottleRepository;
    private readonly IBottleLocationRepository _bottleLocationRepository;
    private readonly ITastingNoteRepository _tastingNoteRepository;
    private readonly IChatGptService _chatGptService;
    private readonly IChatGptPromptService _chatGptPromptService;
    private static readonly TimeSpan SentInvitationNotificationWindow = TimeSpan.FromDays(7);


    private static readonly JsonDocumentOptions SipSessionFoodSuggestionJsonDocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };
    private static readonly HashSet<string> SurfEyeSupportedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/jpg",
        "image/png",
        "image/webp"
    };

    private readonly IWineSurferTopBarService _topBarService;
    private readonly ISuggestedAppellationService _suggestedAppellationService;

    public WineSurferController(
        IWineRepository wineRepository,
        IUserRepository userRepository,
        ISisterhoodRepository sisterhoodRepository,
        ISisterhoodInvitationRepository sisterhoodInvitationRepository,
        ISipSessionRepository sipSessionRepository,
        IBottleRepository bottleRepository,
        IBottleLocationRepository bottleLocationRepository,
        ITastingNoteRepository tastingNoteRepository,
        IChatGptService chatGptService,
        IChatGptPromptService chatGptPromptService,
        IWineSurferTopBarService topBarService,
        ISuggestedAppellationService suggestedAppellationService,
        UserManager<ApplicationUser> userManager) : base(userManager, userRepository)
    {
        _wineRepository = wineRepository;
        _sisterhoodRepository = sisterhoodRepository;
        _sisterhoodInvitationRepository = sisterhoodInvitationRepository;
        _sipSessionRepository = sipSessionRepository;
        _bottleRepository = bottleRepository;
        _bottleLocationRepository = bottleLocationRepository;
        _tastingNoteRepository = tastingNoteRepository;
        _chatGptService = chatGptService;
        _chatGptPromptService = chatGptPromptService;
        _topBarService = topBarService;
        _suggestedAppellationService = suggestedAppellationService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var currentPath = HttpContext?.Request?.Path.Value ?? string.Empty;
        ViewData["WineSurferTopBarModel"] = await _topBarService.BuildAsync(User, currentPath, cancellationToken);

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

        var now = DateTime.UtcNow;
        const int upcomingSipSessionLimit = 4;

        WineSurferCurrentUser? currentUser = null;
        IReadOnlyList<WineSurferIncomingSisterhoodInvitation> incomingInvitations = Array.Empty<WineSurferIncomingSisterhoodInvitation>();
        Guid? currentUserId = null;

        if (User?.Identity?.IsAuthenticated == true)
        {
            var identityName = User.Identity?.Name;
            var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
            var normalizedEmail = StringUtilities.NormalizeEmailCandidate(email);
            currentUserId = GetCurrentUserId();
            ApplicationUser? domainUser = null;

            if (currentUserId.HasValue)
            {
                domainUser = await _userRepository.GetByIdAsync(currentUserId.Value, cancellationToken);
            }

            if (domainUser is null && !string.IsNullOrWhiteSpace(identityName))
            {
                domainUser = await _userRepository.FindByNameAsync(identityName, cancellationToken);
            }

            var displayName = StringUtilities.ResolveDisplayName(domainUser?.Name, identityName, email);

            if (domainUser is not null)
            {
                currentUserId = domainUser.Id;
                normalizedEmail ??= StringUtilities.NormalizeEmailCandidate(domainUser.Email);
            }

            var (domainUserSummary, domainUserProfile) = TasteProfileUtilities.GetActiveTasteProfileTexts(domainUser);

            if (string.IsNullOrWhiteSpace(normalizedEmail) && StringUtilities.LooksLikeEmail(displayName))
            {
                normalizedEmail = StringUtilities.NormalizeEmailCandidate(displayName);
            }

            if (!string.IsNullOrWhiteSpace(displayName) || !string.IsNullOrWhiteSpace(email) || domainUser is not null)
            {
                currentUser = new WineSurferCurrentUser(
                    domainUser?.Id,
                    displayName ?? email ?? string.Empty,
                    email,
                    domainUserSummary,
                    domainUserProfile,
                    domainUser?.IsAdmin == true);
            }

            if (currentUserId.HasValue || normalizedEmail is not null)
            {
                incomingInvitations = (await _sisterhoodInvitationRepository.GetForInviteeAsync(currentUserId, normalizedEmail, cancellationToken))
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
        }

        IReadOnlyList<WineSurferUpcomingSipSession> upcomingSipSessions = Array.Empty<WineSurferUpcomingSipSession>();
        if (currentUserId.HasValue)
        {
            upcomingSipSessions = (await _sipSessionRepository.GetUpcomingAsync(now, upcomingSipSessionLimit, currentUserId.Value, cancellationToken))
                .Select(session =>
                {
                    var summary = new WineSurferSipSessionSummary(
                        session.Id,
                        session.Name,
                        session.Description,
                        session.ScheduledAt,
                        session.Date,
                        session.Location ?? string.Empty,
                        session.CreatedAt,
                        session.UpdatedAt,
                        CreateBottleSummaries(session.Bottles, currentUserId)
                    );
                    return new WineSurferUpcomingSipSession(
                        session.SisterhoodId,
                        session.Sisterhood?.Name ?? "Sisterhood",
                        session.Sisterhood?.Description,
                        summary
                    );
                })
                .ToList();
        }

        IReadOnlyList<WineSurferSentInvitationNotification> sentInvitationNotifications = Array.Empty<WineSurferSentInvitationNotification>();
        if (currentUserId.HasValue)
        {
            var acceptedInvitations = await _sisterhoodInvitationRepository.GetAcceptedForAdminAsync(
                currentUserId.Value,
                now - SentInvitationNotificationWindow,
                cancellationToken);

            sentInvitationNotifications = NotificationService.CreateSentInvitationNotifications(acceptedInvitations);
        }

        IReadOnlyList<WineSurferSisterhoodOption> manageableSisterhoods = Array.Empty<WineSurferSisterhoodOption>();
        IReadOnlyList<WineSurferSipSessionBottle> favoriteBottles = Array.Empty<WineSurferSipSessionBottle>();
        IReadOnlyCollection<Guid> inventoryWineIds = Array.Empty<Guid>();
        IReadOnlyList<WineSurferSuggestedAppellation> suggestedAppellations = Array.Empty<WineSurferSuggestedAppellation>();
        if (currentUserId.HasValue)
        {
            var adminSisterhoods = await _sisterhoodRepository.GetAdminForUserAsync(currentUserId.Value, cancellationToken);
            manageableSisterhoods = adminSisterhoods
                .Select(s => new WineSurferSisterhoodOption(s.Id, s.Name, s.Description))
                .ToList();

            var ownedBottles = await _bottleRepository.GetForUserAsync(currentUserId.Value, cancellationToken);
            if (ownedBottles.Count > 0)
            {
                var ownedWineIds = new HashSet<Guid>();
                foreach (var bottle in ownedBottles)
                {
                    var wineId = bottle.WineVintage?.Wine?.Id ?? Guid.Empty;
                    if (wineId != Guid.Empty)
                    {
                        ownedWineIds.Add(wineId);
                    }
                }

                if (ownedWineIds.Count > 0)
                {
                    inventoryWineIds = ownedWineIds;
                }

                favoriteBottles = CreateBottleSummaries(ownedBottles, currentUserId)
                    .Where(bottle => bottle.CurrentUserScore.HasValue)
                    .OrderByDescending(bottle => bottle.CurrentUserScore!.Value)
                    .ThenBy(bottle => bottle.WineName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(bottle => bottle.Vintage ?? int.MaxValue)
                    .ThenBy(bottle => bottle.Label, StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .ToList();
            }

            suggestedAppellations = await _suggestedAppellationService.GetForUserAsync(currentUserId.Value, cancellationToken);
            if (suggestedAppellations.Count > 0)
            {
                var suggestionHighlights = new List<MapHighlightPoint>(suggestedAppellations.Count);
                foreach (var suggestion in suggestedAppellations)
                {
                    var highlight = CreateSuggestedHighlightPoint(suggestion);
                    if (highlight is null)
                    {
                        continue;
                    }

                    suggestionHighlights.Add(highlight);
                }

                if (suggestionHighlights.Count > 0)
                {
                    highlightPoints.AddRange(suggestionHighlights);
                }
            }
        }

        if (highlightPoints.Count > 1)
        {
            highlightPoints = highlightPoints
                .OrderBy(point => point.IsSuggested ? 1 : 0)
                .ThenBy(point => point.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        await SetInventoryAddModalViewDataAsync(currentUserId, cancellationToken);

        var model = new WineSurferLandingViewModel(
            highlightPoints,
            currentUser,
            incomingInvitations,
            upcomingSipSessions,
            sentInvitationNotifications,
            manageableSisterhoods,
            favoriteBottles,
            suggestedAppellations,
            inventoryWineIds);
        Response.ContentType = "text/html; charset=utf-8";
        return View("Index", model);
    }

   

    [HttpGet("sessions/{sipSessionId:guid}")]
    public async Task<IActionResult> SipSession(Guid sipSessionId, CancellationToken cancellationToken)
    {
        var currentPath = HttpContext?.Request?.Path.Value ?? string.Empty;
        ViewData["WineSurferTopBarModel"] = await _topBarService.BuildAsync(User, currentPath, cancellationToken);
        var session = await _sipSessionRepository.GetByIdAsync(sipSessionId, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }

        var model = await BuildSipSessionDetailViewModelAsync(session, cancellationToken);

        Response.ContentType = "text/html; charset=utf-8";
        return View("SipSession", model);
    }

    [HttpPost("sessions/{sipSessionId:guid}/suggest-food")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SuggestSipSessionFood(Guid sipSessionId, CancellationToken cancellationToken)
    {
        var session = await _sipSessionRepository.GetByIdAsync(sipSessionId, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }

        var sessionBottles = session.Bottles ?? Array.Empty<SipSessionBottle>();
        if (sessionBottles.Count == 0)
        {
            var emptyModel = await BuildSipSessionDetailViewModelAsync(
                session,
                cancellationToken,
                Array.Empty<string>(),
                "Add bottles to this sip session before requesting food pairings.");
            Response.ContentType = "text/html; charset=utf-8";
            return View("SipSession", emptyModel);
        }

        var prompt = _chatGptPromptService.BuildSipSessionFoodSuggestionPrompt(session);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            var fallbackModel = await BuildSipSessionDetailViewModelAsync(
                session,
                cancellationToken,
                Array.Empty<string>(),
                "We couldn't collect enough information about these wines to suggest pairings.");
            Response.ContentType = "text/html; charset=utf-8";
            return View("SipSession", fallbackModel);
        }

        ChatCompletion completion;
        try
        {
            completion = await _chatGptService.GetChatCompletionAsync(
                new ChatMessage[]
                {
                    new SystemChatMessage(_chatGptPromptService.SipSessionFoodSuggestionSystemPrompt),
                    new UserChatMessage(prompt)
                },

                ct: cancellationToken);
        }
        catch (ChatGptServiceNotConfiguredException)
        {
            var errorModel = await BuildSipSessionDetailViewModelAsync(
                session,
                cancellationToken,
                Array.Empty<string>(),
                "Food pairing suggestions are not configured.");
            Response.ContentType = "text/html; charset=utf-8";
            return View("SipSession", errorModel);
        }
        catch (ClientResultException)
        {
            var errorModel = await BuildSipSessionDetailViewModelAsync(
                session,
                cancellationToken,
                Array.Empty<string>(),
                "We couldn't reach the pairing assistant. Please try again.");
            Response.ContentType = "text/html; charset=utf-8";
            return View("SipSession", errorModel);
        }
        catch (Exception)
        {
            var errorModel = await BuildSipSessionDetailViewModelAsync(
                session,
                cancellationToken,
                Array.Empty<string>(),
                "We couldn't request food pairings right now. Please try again.");
            Response.ContentType = "text/html; charset=utf-8";
            return View("SipSession", errorModel);
        }

        var completionText = StringUtilities.ExtractCompletionText(completion);
        if (!TryParseSipSessionFoodSuggestions(completionText, out var suggestions, out var cheeseSuggestion))
        {
            var errorModel = await BuildSipSessionDetailViewModelAsync(
                session,
                cancellationToken,
                Array.Empty<string>(),
                "We couldn't understand the pairing assistant's response. Please try again.");
            Response.ContentType = "text/html; charset=utf-8";
            return View("SipSession", errorModel);
        }

        if (suggestions.Count > 0)
        {
            var payload = new SipSessionFoodSuggestionPayload(suggestions, cheeseSuggestion);
            var serializedSuggestions = JsonSerializer.Serialize(payload);
            await _sipSessionRepository.UpdateFoodSuggestionAsync(session.Id, serializedSuggestions, cancellationToken);
            session.FoodSuggestion = serializedSuggestions;
        }

        var model = await BuildSipSessionDetailViewModelAsync(
            session,
            cancellationToken,
            suggestions,
            null,
            cheeseSuggestion);
        Response.ContentType = "text/html; charset=utf-8";
        return View("SipSession", model);
    }

    private async Task<WineSurferSipSessionDetailViewModel> BuildSipSessionDetailViewModelAsync(
        SipSession session,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? foodSuggestions = null,
        string? foodSuggestionError = null,
        string? cheeseSuggestion = null)
    {
        if (session is null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        var now = DateTime.UtcNow;
        WineSurferCurrentUser? currentUser = null;
        IReadOnlyList<WineSurferIncomingSisterhoodInvitation> incomingInvitations = Array.Empty<WineSurferIncomingSisterhoodInvitation>();
        IReadOnlyList<WineSurferSentInvitationNotification> sentInvitationNotifications = Array.Empty<WineSurferSentInvitationNotification>();
        IReadOnlyList<WineSurferSipSessionBottle> availableBottles = Array.Empty<WineSurferSipSessionBottle>();
        Guid? currentUserId = null;
        string domainUserSummary = string.Empty;
        string domainUserProfile = string.Empty;

        if (User?.Identity?.IsAuthenticated == true)
        {
            var identityName = User.Identity?.Name;
            var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
            var normalizedEmail = StringUtilities.NormalizeEmailCandidate(email);
            currentUserId = GetCurrentUserId();
            ApplicationUser? domainUser = null;

            if (currentUserId.HasValue)
            {
                domainUser = await _userRepository.GetByIdAsync(currentUserId.Value, cancellationToken);
            }

            if (domainUser is null && !string.IsNullOrWhiteSpace(identityName))
            {
                domainUser = await _userRepository.FindByNameAsync(identityName, cancellationToken);
            }

            var (resolvedSummary, resolvedProfile) = TasteProfileUtilities.GetActiveTasteProfileTexts(domainUser);
            domainUserSummary = resolvedSummary;
            domainUserProfile = resolvedProfile;

            var displayName = StringUtilities.ResolveDisplayName(domainUser?.Name, identityName, email);

            if (domainUser is not null)
            {
                currentUserId = domainUser.Id;
                normalizedEmail ??= StringUtilities.NormalizeEmailCandidate(domainUser.Email);
            }

            if (string.IsNullOrWhiteSpace(normalizedEmail) && StringUtilities.LooksLikeEmail(displayName))
            {
                normalizedEmail = StringUtilities.NormalizeEmailCandidate(displayName);
            }

            if (!string.IsNullOrWhiteSpace(displayName) || !string.IsNullOrWhiteSpace(email) || domainUser is not null)
            {
                currentUser = new WineSurferCurrentUser(
                    domainUser?.Id,
                    displayName ?? email ?? string.Empty,
                    email,
                    domainUserSummary,
                    domainUserProfile,
                    domainUser?.IsAdmin == true);
            }

            if (currentUserId.HasValue || normalizedEmail is not null)
            {
                incomingInvitations = (await _sisterhoodInvitationRepository.GetForInviteeAsync(currentUserId, normalizedEmail, cancellationToken))
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
        }

        if (currentUserId.HasValue)
        {
            var availableBottleEntities = await _bottleRepository.GetAvailableForUserAsync(
                currentUserId.Value,
                cancellationToken);
            availableBottles = CreateBottleSummaries(availableBottleEntities, currentUserId);

            var acceptedInvitations = await _sisterhoodInvitationRepository.GetAcceptedForAdminAsync(
                currentUserId.Value,
                now - SentInvitationNotificationWindow,
                cancellationToken);

            sentInvitationNotifications = NotificationService.CreateSentInvitationNotifications(acceptedInvitations);
        }

        var canManageSession = false;
        if (currentUserId.HasValue)
        {
            var memberships = session.Sisterhood?.Memberships ?? Array.Empty<SisterhoodMembership>();
            canManageSession = memberships.Any(membership =>
                membership.UserId == currentUserId.Value && membership.IsAdmin);
        }

        IReadOnlyDictionary<Guid, decimal>? sisterhoodAverageScores = null;
        var sessionBottles = session.Bottles ?? Array.Empty<SipSessionBottle>();
        var sisterhoodMemberIds = session.Sisterhood?.Memberships
            ?.Select(membership => membership.UserId)
            .Where(userId => userId != Guid.Empty)
            .Distinct()
            .ToList() ?? new List<Guid>();

        if (sessionBottles.Count > 0 && sisterhoodMemberIds.Count > 0)
        {
            var wineVintageIds = sessionBottles
                .Select(link => link.Bottle?.WineVintageId ?? Guid.Empty)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            if (wineVintageIds.Count > 0)
            {
                var averages = await _tastingNoteRepository.GetAverageScoresForWineVintagesByUsersAsync(
                    wineVintageIds,
                    sisterhoodMemberIds,
                    cancellationToken);

                if (averages.Count > 0)
                {
                    sisterhoodAverageScores = averages;
                }
            }
        }

        var summary = new WineSurferSipSessionSummary(
            session.Id,
            session.Name,
            session.Description,
            session.ScheduledAt,
            session.Date,
            session.Location ?? string.Empty,
            session.CreatedAt,
            session.UpdatedAt,
            CreateBottleSummaries(session.Bottles, currentUserId, sisterhoodAverageScores));

        IReadOnlyList<string>? suggestionCandidates = foodSuggestions;
        var cheeseCandidate = string.IsNullOrWhiteSpace(cheeseSuggestion)
            ? null
            : cheeseSuggestion.Trim();

        if ((suggestionCandidates is null || suggestionCandidates.Count == 0) &&
            TryParseSipSessionFoodSuggestions(session.FoodSuggestion, out var storedSuggestions, out var storedCheese) &&
            storedSuggestions.Count > 0)
        {
            suggestionCandidates = storedSuggestions;
            if (string.IsNullOrWhiteSpace(cheeseCandidate) && !string.IsNullOrWhiteSpace(storedCheese))
            {
                cheeseCandidate = storedCheese.Trim();
            }
        }

        IReadOnlyList<string> normalizedSuggestions;
        if (suggestionCandidates is null || suggestionCandidates.Count == 0)
        {
            normalizedSuggestions = Array.Empty<string>();
        }
        else
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var suggestion in suggestionCandidates)
            {
                var trimmed = suggestion?.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }

                if (seen.Add(trimmed))
                {
                    list.Add(trimmed);
                    if (list.Count == 3)
                    {
                        break;
                    }
                }
            }

            normalizedSuggestions = list;
        }

        var normalizedFoodSuggestionError = string.IsNullOrWhiteSpace(foodSuggestionError)
            ? null
            : foodSuggestionError.Trim();

        if (normalizedSuggestions.Count > 0)
        {
            normalizedFoodSuggestionError = null;
        }

        var normalizedCheeseSuggestion = string.IsNullOrWhiteSpace(cheeseCandidate)
            ? null
            : cheeseCandidate.Trim();

        if (normalizedSuggestions.Count == 0)
        {
            normalizedCheeseSuggestion = null;
        }

        return new WineSurferSipSessionDetailViewModel(
            summary,
            session.SisterhoodId,
            session.Sisterhood?.Name ?? "Sisterhood",
            session.Sisterhood?.Description,
            canManageSession,
            currentUser,
            incomingInvitations,
            sentInvitationNotifications,
            false,
            Array.Empty<WineSurferSisterhoodOption>(),
            availableBottles,
            normalizedSuggestions,
            normalizedFoodSuggestionError,
            normalizedCheeseSuggestion);
    }

    [Authorize]
    [HttpGet("sessions/create")]
    public async Task<IActionResult> PlanSipSession(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        WineSurferCurrentUser? currentUser = null;
        IReadOnlyList<WineSurferIncomingSisterhoodInvitation> incomingInvitations = Array.Empty<WineSurferIncomingSisterhoodInvitation>();
        IReadOnlyList<WineSurferSentInvitationNotification> sentInvitationNotifications = Array.Empty<WineSurferSentInvitationNotification>();

        var identityName = User.Identity?.Name;
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
        var normalizedEmail = StringUtilities.NormalizeEmailCandidate(email);
        var currentUserId = GetCurrentUserId();
        ApplicationUser? domainUser = null;
        string domainUserSummary = string.Empty;
        string domainUserProfile = string.Empty;

        if (currentUserId.HasValue)
        {
            domainUser = await _userRepository.GetByIdAsync(currentUserId.Value, cancellationToken);
        }

        if (domainUser is null && !string.IsNullOrWhiteSpace(identityName))
        {
            domainUser = await _userRepository.FindByNameAsync(identityName, cancellationToken);
        }

        var (resolvedSummary, resolvedProfile) = TasteProfileUtilities.GetActiveTasteProfileTexts(domainUser);
        domainUserSummary = resolvedSummary;
        domainUserProfile = resolvedProfile;

        var displayName = StringUtilities.ResolveDisplayName(domainUser?.Name, identityName, email);

        if (domainUser is not null)
        {
            currentUserId = domainUser.Id;
            normalizedEmail ??= StringUtilities.NormalizeEmailCandidate(domainUser.Email);
        }

        if (string.IsNullOrWhiteSpace(normalizedEmail) && StringUtilities.LooksLikeEmail(displayName))
        {
            normalizedEmail = StringUtilities.NormalizeEmailCandidate(displayName);
        }

        if (!string.IsNullOrWhiteSpace(displayName) || !string.IsNullOrWhiteSpace(email) || domainUser is not null)
        {
            currentUser = new WineSurferCurrentUser(
                domainUser?.Id,
                displayName ?? email ?? string.Empty,
                email,
                domainUserSummary,
                domainUserProfile,
                domainUser?.IsAdmin == true);
        }

        if (currentUserId.HasValue || normalizedEmail is not null)
        {
            incomingInvitations = (await _sisterhoodInvitationRepository.GetForInviteeAsync(currentUserId, normalizedEmail, cancellationToken))
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

        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        if (currentUserId.HasValue)
        {
            var acceptedInvitations = await _sisterhoodInvitationRepository.GetAcceptedForAdminAsync(
                currentUserId.Value,
                now - SentInvitationNotificationWindow,
                cancellationToken);

            sentInvitationNotifications = NotificationService.CreateSentInvitationNotifications(acceptedInvitations);
        }

        var manageableSisterhoods = (await _sisterhoodRepository.GetAdminForUserAsync(currentUserId.Value, cancellationToken))
            .Select(s => new WineSurferSisterhoodOption(s.Id, s.Name, s.Description))
            .ToList();

        var selectedSisterhoodId = manageableSisterhoods.Count == 1 ? manageableSisterhoods[0].Id : Guid.Empty;
        var sisterhoodName = manageableSisterhoods.Count == 1
            ? manageableSisterhoods[0].Name
            : "Select a sisterhood";
        var sisterhoodDescription = manageableSisterhoods.Count == 1
            ? manageableSisterhoods[0].Description
            : null;

        var sessionSummary = new WineSurferSipSessionSummary(
            Guid.Empty,
            string.Empty,
            null,
            null,
            null,
            string.Empty,
            now,
            now,
            Array.Empty<WineSurferSipSessionBottle>());

        var model = new WineSurferSipSessionDetailViewModel(
            sessionSummary,
            selectedSisterhoodId,
            sisterhoodName,
            sisterhoodDescription,
            manageableSisterhoods.Count > 0,
            currentUser,
            incomingInvitations,
            sentInvitationNotifications,
            true,
            manageableSisterhoods,
            Array.Empty<WineSurferSipSessionBottle>());

        Response.ContentType = "text/html; charset=utf-8";
        return View("SipSession", model);
    }

    [HttpGet("sisterhoods")]
    public async Task<IActionResult> Sisterhoods(CancellationToken cancellationToken)
    {
        var currentPath = HttpContext?.Request?.Path.Value ?? string.Empty;
        ViewData["WineSurferTopBarModel"] = await _topBarService.BuildAsync(User, currentPath, cancellationToken);
        Response.ContentType = "text/html; charset=utf-8";

        var statusMessage = TempData["SisterhoodStatus"] as string;
        var errorMessage = TempData["SisterhoodError"] as string;

        var now = DateTime.UtcNow;
        var isAuthenticated = User?.Identity?.IsAuthenticated == true;
        string? displayName = null;
        Guid? currentUserId = null;
        var isAdmin = false;
        IReadOnlyList<WineSurferSisterhoodSummary> sisterhoods = Array.Empty<WineSurferSisterhoodSummary>();
        IReadOnlyList<WineSurferIncomingSisterhoodInvitation> incomingInvitations = Array.Empty<WineSurferIncomingSisterhoodInvitation>();
        IReadOnlyList<WineSurferSipSessionBottle> availableBottles = Array.Empty<WineSurferSipSessionBottle>();
        IReadOnlyList<WineSurferSentInvitationNotification> sentInvitationNotifications = Array.Empty<WineSurferSentInvitationNotification>();

        if (isAuthenticated)
        {
            var identityName = User.Identity?.Name;
            var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
            var normalizedEmail = StringUtilities.NormalizeEmailCandidate(email);

            currentUserId = GetCurrentUserId();

            ApplicationUser? domainUser = null;
            if (currentUserId.HasValue)
            {
                domainUser = await _userRepository.GetByIdAsync(currentUserId.Value, cancellationToken);
            }

            if (domainUser is null && !string.IsNullOrWhiteSpace(identityName))
            {
                domainUser = await _userRepository.FindByNameAsync(identityName, cancellationToken);
            }

            displayName = StringUtilities.ResolveDisplayName(domainUser?.Name, identityName, email);

            if (domainUser is not null)
            {
                currentUserId = domainUser.Id;
                normalizedEmail ??= StringUtilities.NormalizeEmailCandidate(domainUser.Email);
                isAdmin = domainUser.IsAdmin;
            }

            if (string.IsNullOrWhiteSpace(normalizedEmail) && StringUtilities.LooksLikeEmail(displayName))
            {
                normalizedEmail = StringUtilities.NormalizeEmailCandidate(displayName);
            }

            if (currentUserId.HasValue)
            {
                var membership = await _sisterhoodRepository.GetForUserAsync(currentUserId.Value, cancellationToken);
                var availableBottleEntities = await _bottleRepository.GetAvailableForUserAsync(currentUserId.Value, cancellationToken);
                availableBottles = CreateBottleSummaries(availableBottleEntities, currentUserId);
                var acceptedInvitations = await _sisterhoodInvitationRepository.GetAcceptedForAdminAsync(
                    currentUserId.Value,
                    now - SentInvitationNotificationWindow,
                    cancellationToken);

                sentInvitationNotifications = NotificationService.CreateSentInvitationNotifications(acceptedInvitations);
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
                                    session.UpdatedAt,
                                    CreateBottleSummaries(session.Bottles, currentUserId))
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
        }

        var model = new WineSurferSisterhoodsViewModel(isAuthenticated, displayName, isAdmin, sisterhoods, currentUserId, statusMessage, errorMessage, incomingInvitations, sentInvitationNotifications, availableBottles);
        return View("~/Views/Sisterhoods/Index.cshtml", model);
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers([FromQuery] string? query, CancellationToken cancellationToken)
    {
        var trimmed = query?.Trim();

        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length < 3)
        {
            return Json(Array.Empty<WineSurferUserSummary>());
        }

        var users = await _userRepository.SearchByNameOrEmailAsync(trimmed, 10, cancellationToken);

        var response = users
            .Where(u => !string.IsNullOrWhiteSpace(u.Name))
            .OrderBy(u => u.Name)
            .Select(u =>
            {
                var (summary, profile) = TasteProfileUtilities.GetActiveTasteProfileTexts(u);
                return new WineSurferUserSummary(
                    u.Id,
                    u.Name,
                    u.Email ?? string.Empty,
                    summary,
                    profile);
            })
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
        var normalizedEmailCandidate = StringUtilities.NormalizeEmailCandidate(request.MemberEmail);
        var nameLooksLikeEmail = request.IsEmail || StringUtilities.LooksLikeEmail(trimmedName);

        if (invitee is null && string.IsNullOrEmpty(normalizedEmailCandidate) && nameLooksLikeEmail)
        {
            normalizedEmailCandidate = StringUtilities.NormalizeEmailCandidate(trimmedName);
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
            inviteeEmail = StringUtilities.NormalizeEmailCandidate(invitee.Email);

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
            var registerRouteValues = new { email = inviteeEmail };
            var signupLink = Url.ActionLink("Register", "Account", registerRouteValues)
                ?? Url.Action("Register", "Account", registerRouteValues)
                ?? "/account/register";
            signupLink = StringUtilities.EnsureEmailQueryParameter(signupLink, inviteeEmail);
            var body =
                $"Dear Wine Lover,\n\n" +
                $"We'd be thrilled to welcome you to the {sisterhood.Name} sisterhood on Wine Surfer—" +
                "a vibrant community of surfers who share discoveries, tasting notes, and good company.\n\n" +
                $"{signupLink}\n\n" +
                "Use this email address when you sign up and accept the invitation waiting for you. " +
                "We're excited to ride the next wave of wine adventures together!\n\n" +
                "Cheers,\nThe Wine Surfer Crew";
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

        var normalizedEmail = StringUtilities.NormalizeEmailCandidate(User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email"));
        if (string.IsNullOrWhiteSpace(normalizedEmail) && StringUtilities.LooksLikeEmail(User.Identity?.Name))
        {
            normalizedEmail = StringUtilities.NormalizeEmailCandidate(User.Identity?.Name);
        }

        if (normalizedEmail is null)
        {
            var domainUser = await _userRepository.GetByIdAsync(currentUserId.Value, cancellationToken);
            normalizedEmail = StringUtilities.NormalizeEmailCandidate(domainUser?.Email);
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

        var normalizedEmail = StringUtilities.NormalizeEmailCandidate(User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email"));
        if (string.IsNullOrWhiteSpace(normalizedEmail) && StringUtilities.LooksLikeEmail(User.Identity?.Name))
        {
            normalizedEmail = StringUtilities.NormalizeEmailCandidate(User.Identity?.Name);
        }

        if (normalizedEmail is null)
        {
            var domainUser = await _userRepository.GetByIdAsync(currentUserId.Value, cancellationToken);
            normalizedEmail = StringUtilities.NormalizeEmailCandidate(domainUser?.Email);
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

        var normalizedEmail = StringUtilities.NormalizeEmailCandidate(User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email"));
        if (string.IsNullOrWhiteSpace(normalizedEmail) && StringUtilities.LooksLikeEmail(User.Identity?.Name))
        {
            normalizedEmail = StringUtilities.NormalizeEmailCandidate(User.Identity?.Name);
        }

        if (normalizedEmail is null)
        {
            var domainUser = await _userRepository.GetByIdAsync(currentUserId.Value, cancellationToken);
            normalizedEmail = StringUtilities.NormalizeEmailCandidate(domainUser?.Email);
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
    [HttpPost("sisterhoods/invitations/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemovePendingSisterhoodInvitation(RemovePendingSisterhoodInvitationRequest request, CancellationToken cancellationToken)
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

        IActionResult Success(string message)
        {
            if (expectsJson)
            {
                return Ok(new
                {
                    success = true,
                    message,
                    request.InvitationId,
                    request.SisterhoodId
                });
            }

            TempData["SisterhoodStatus"] = message;
            return RedirectToAction(nameof(Sisterhoods));
        }

        if (!ModelState.IsValid || request.SisterhoodId == Guid.Empty || request.InvitationId == Guid.Empty)
        {
            return Error("We couldn't process that invitation.");
        }

        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            if (expectsJson)
            {
                return Unauthorized(new { success = false, message = "You need to sign in before managing invitations." });
            }

            return Challenge();
        }

        var isAdmin = await _sisterhoodRepository.IsAdminAsync(request.SisterhoodId, currentUserId.Value, cancellationToken);
        if (!isAdmin)
        {
            return Error("Only sisterhood admins can remove invitations.", StatusCodes.Status403Forbidden);
        }

        var invitation = await _sisterhoodInvitationRepository.GetByIdAsync(request.InvitationId, cancellationToken);
        if (invitation is null || invitation.SisterhoodId != request.SisterhoodId)
        {
            return Error("That invitation is no longer available.", StatusCodes.Status404NotFound);
        }

        if (invitation.Status != SisterhoodInvitationStatus.Pending)
        {
            return Error("That invitation is no longer pending.", StatusCodes.Status409Conflict);
        }

        var deleted = await _sisterhoodInvitationRepository.DeleteAsync(request.InvitationId, cancellationToken);
        if (!deleted)
        {
            return Error("We couldn't remove that invitation right now.", StatusCodes.Status500InternalServerError);
        }

        return Success("Invitation removed.");
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

        var (scheduledAtLocal, scheduledDateLocal) = ResolveSchedule(request);

        var sipSession = new SipSession
        {
            SisterhoodId = request.SisterhoodId,
            Name = request.Name,
            Description = request.Description,
            ScheduledAt = NormalizeToUtc(scheduledAtLocal),
            Date = scheduledDateLocal?.Date,
            Location = request.Location ?? string.Empty
        };

        try
        {
            await _sipSessionRepository.AddAsync(sipSession, cancellationToken);

            var scheduledDisplay = scheduledAtLocal?.ToString("f") ?? scheduledDateLocal?.ToString("D");
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
    [HttpPost("[action]")]
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

        var (scheduledAtLocal, scheduledDateLocal) = ResolveSchedule(request);

        session.Name = request.Name;
        session.Description = request.Description;
        session.Location = request.Location ?? string.Empty;
        session.ScheduledAt = NormalizeToUtc(scheduledAtLocal);
        session.Date = scheduledDateLocal?.Date;

        try
        {
            await _sipSessionRepository.UpdateAsync(session, cancellationToken);

            var scheduledDisplay = scheduledAtLocal?.ToString("f") ?? scheduledDateLocal?.ToString("D");
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

        if (!string.IsNullOrWhiteSpace(request.ReturnUrl) && Url.IsLocalUrl(request.ReturnUrl))
        {
            return Redirect(request.ReturnUrl);
        }

        return RedirectToAction(nameof(Sisterhoods));
    }

    [Authorize]
    [HttpPost("sisterhoods/sessions/contribute-bottles")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ContributeSipSessionBottles(ContributeSipSessionBottlesRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || request is null || request.SisterhoodId == Guid.Empty || request.SipSessionId == Guid.Empty)
        {
            TempData["SisterhoodError"] = "We couldn't add those bottles.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        var membership = await _sisterhoodRepository.GetMembershipAsync(request.SisterhoodId, currentUserId.Value, cancellationToken);
        if (membership is null)
        {
            TempData["SisterhoodError"] = "You must be part of this sisterhood to contribute bottles.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var bottleIds = request.BottleIds
            ?.Where(id => id != Guid.Empty)
            .Distinct()
            .ToList() ?? new List<Guid>();

        if (bottleIds.Count == 0)
        {
            TempData["SisterhoodError"] = "Please select at least one bottle to contribute.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var session = await _sipSessionRepository.GetByIdAsync(request.SipSessionId, cancellationToken);
        if (session is null || session.SisterhoodId != request.SisterhoodId)
        {
            TempData["SisterhoodError"] = "That sip session could not be found.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        try
        {
            var addedCount = await _sipSessionRepository.AddBottlesToSessionAsync(request.SipSessionId, currentUserId.Value, bottleIds, cancellationToken);
            if (addedCount > 0)
            {
                var noun = addedCount == 1 ? "bottle" : "bottles";
                TempData["SisterhoodStatus"] = $"Added {addedCount} {noun} to '{session.Name}'.";
            }
            else
            {
                TempData["SisterhoodError"] = "We couldn't add those bottles to the sip session.";
            }
        }
        catch (Exception)
        {
            TempData["SisterhoodError"] = "We couldn't add those bottles right now. Please try again.";
        }

        return RedirectToAction(nameof(SipSession), new { sipSessionId = request.SipSessionId });
    }

    [Authorize]
    [HttpPost("sisterhoods/sessions/remove-bottle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveSipSessionBottle(RemoveSipSessionBottleRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid ||
            request is null ||
            request.SisterhoodId == Guid.Empty ||
            request.SipSessionId == Guid.Empty ||
            request.BottleId == Guid.Empty)
        {
            TempData["SisterhoodError"] = "We couldn't remove that bottle.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        var membership = await _sisterhoodRepository.GetMembershipAsync(request.SisterhoodId, currentUserId.Value, cancellationToken);
        if (membership is null)
        {
            TempData["SisterhoodError"] = "You must be part of this sisterhood to manage bottles.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var session = await _sipSessionRepository.GetByIdAsync(request.SipSessionId, cancellationToken);
        if (session is null || session.SisterhoodId != request.SisterhoodId)
        {
            TempData["SisterhoodError"] = "That sip session could not be found.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        try
        {
            var removed = await _sipSessionRepository.RemoveBottleFromSessionAsync(request.SipSessionId, currentUserId.Value, request.BottleId, cancellationToken);
            if (removed)
            {
                TempData["SisterhoodStatus"] = $"Removed a bottle from '{session.Name}'.";
            }
            else
            {
                TempData["SisterhoodError"] = "We couldn't remove that bottle from the sip session.";
            }
        }
        catch (Exception)
        {
            TempData["SisterhoodError"] = "We couldn't remove that bottle right now. Please try again.";
        }

        if (!string.IsNullOrWhiteSpace(request.ReturnUrl) && Url.IsLocalUrl(request.ReturnUrl))
        {
            return Redirect(request.ReturnUrl);
        }

        return RedirectToAction(nameof(Sisterhoods));
    }

    [Authorize]
    [HttpPost("sisterhoods/sessions/reveal-bottle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevealSipSessionBottle(RevealSipSessionBottleRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid ||
            request is null ||
            request.SisterhoodId == Guid.Empty ||
            request.SipSessionId == Guid.Empty ||
            request.BottleId == Guid.Empty)
        {
            TempData["SisterhoodError"] = "We couldn't reveal that bottle.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        var membership = await _sisterhoodRepository.GetMembershipAsync(request.SisterhoodId, currentUserId.Value, cancellationToken);
        if (membership is null)
        {
            TempData["SisterhoodError"] = "You must be part of this sisterhood to reveal bottles.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var session = await _sipSessionRepository.GetByIdAsync(request.SipSessionId, cancellationToken);
        if (session is null || session.SisterhoodId != request.SisterhoodId)
        {
            TempData["SisterhoodError"] = "That sip session could not be found.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var sessionBottle = session.Bottles?
            .FirstOrDefault(link => link.BottleId == request.BottleId);
        if (sessionBottle?.Bottle is null || sessionBottle.Bottle.UserId != currentUserId.Value)
        {
            TempData["SisterhoodError"] = "Only the contributor can reveal this bottle.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        try
        {
            var revealed = await _sipSessionRepository.RevealBottleInSessionAsync(
                request.SipSessionId,
                currentUserId.Value,
                request.BottleId,
                cancellationToken);

            if (revealed)
            {
                var bottleLabel = CreateBottleLabel(sessionBottle.Bottle);
                TempData["SisterhoodStatus"] = $"Revealed {bottleLabel} for '{session.Name}'.";
            }
            else
            {
                TempData["SisterhoodError"] = "We couldn't reveal that bottle.";
            }
        }
        catch (Exception)
        {
            TempData["SisterhoodError"] = "We couldn't reveal that bottle right now. Please try again.";
        }

        return RedirectToAction(nameof(SipSession), new { sipSessionId = request.SipSessionId });
    }

    [Authorize]
    [HttpPost("sisterhoods/sessions/drink-bottle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DrinkSipSessionBottle(DrinkSipSessionBottleRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid ||
            request is null ||
            request.SisterhoodId == Guid.Empty ||
            request.SipSessionId == Guid.Empty ||
            request.BottleId == Guid.Empty)
        {
            TempData["SisterhoodError"] = "We couldn't mark that bottle as enjoyed.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        var membership = await _sisterhoodRepository.GetMembershipAsync(request.SisterhoodId, currentUserId.Value, cancellationToken);
        if (membership is null)
        {
            TempData["SisterhoodError"] = "You must be part of this sisterhood to manage bottles.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var session = await _sipSessionRepository.GetByIdAsync(request.SipSessionId, cancellationToken);
        if (session is null || session.SisterhoodId != request.SisterhoodId)
        {
            TempData["SisterhoodError"] = "That sip session could not be found.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var sessionBottle = session.Bottles?
            .FirstOrDefault(link =>
                link.BottleId == request.BottleId &&
                link.Bottle is not null &&
                link.Bottle.UserId == currentUserId.Value);
        if (sessionBottle?.Bottle is null)
        {
            TempData["SisterhoodError"] = "Only the bottle contributor can mark it as enjoyed.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var bottle = sessionBottle.Bottle;
        var drunkAt = DetermineSipSessionDrunkAt(session);
        var bottleLabel = CreateBottleLabel(bottle);

        try
        {
            var marked = await _bottleRepository.MarkAsDrunkAsync(request.BottleId, currentUserId.Value, drunkAt, cancellationToken);
            if (marked)
            {
                TempData["SisterhoodStatus"] = $"Marked {bottleLabel} as enjoyed during '{session.Name}'.";
            }
            else
            {
                TempData["SisterhoodError"] = "We couldn't mark that bottle as enjoyed.";
            }
        }
        catch (Exception)
        {
            TempData["SisterhoodError"] = "We couldn't mark that bottle as enjoyed right now. Please try again.";
        }

        return RedirectToAction(nameof(Sisterhoods));
    }

    [Authorize]
    [HttpPost("sisterhoods/sessions/rate-bottle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RateSipSessionBottle(RateSipSessionBottleRequest request, CancellationToken cancellationToken)
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

        IActionResult Success(
            string message,
            Guid noteId,
            string note,
            decimal? scoreValue,
            decimal? averageScore,
            decimal? sisterhoodAverageScore)
        {
            if (expectsJson)
            {
                return Ok(new
                {
                    success = true,
                    message,
                    noteId,
                    note,
                    score = scoreValue,
                    averageScore,
                    sisterhoodAverageScore
                });
            }

            TempData["SisterhoodStatus"] = message;
            return RedirectToAction(nameof(Sisterhoods));
        }

        if (!ModelState.IsValid ||
            request is null ||
            request.SisterhoodId == Guid.Empty ||
            request.SipSessionId == Guid.Empty ||
            request.BottleId == Guid.Empty)
        {
            return Error("We couldn't save that tasting note.");
        }

        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        var membership = await _sisterhoodRepository.GetMembershipAsync(request.SisterhoodId, currentUserId.Value, cancellationToken);
        if (membership is null)
        {
            return Error("You must be part of this sisterhood to leave a tasting note.", StatusCodes.Status403Forbidden);
        }

        var session = await _sipSessionRepository.GetByIdAsync(request.SipSessionId, cancellationToken);
        if (session is null || session.SisterhoodId != request.SisterhoodId)
        {
            return Error("That sip session could not be found.", StatusCodes.Status404NotFound);
        }

        var sessionBottle = session.Bottles?
            .FirstOrDefault(link => link.BottleId == request.BottleId);
        if (sessionBottle?.Bottle is null)
        {
            return Error("That bottle is no longer part of the sip session.", StatusCodes.Status404NotFound);
        }

        var bottle = sessionBottle.Bottle;
        var noteText = request.Note?.Trim();

        decimal? score = null;
        var rawScore = request.Score?.Trim();
        if (!string.IsNullOrEmpty(rawScore))
        {
            if (decimal.TryParse(rawScore, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                if (parsed < 0 || parsed > 10)
                {
                    return Error("Score must be between 0 and 10.");
                }
                score = Math.Round(parsed, 1, MidpointRounding.AwayFromZero);
            }
            else
            {
                // Try current culture as a fallback (e.g., '8,8')
                if (decimal.TryParse(rawScore, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out var parsedLocal))
                {
                    if (parsedLocal < 0 || parsedLocal > 10)
                    {
                        return Error("Score must be between 0 and 10.");
                    }
                    score = Math.Round(parsedLocal, 1, MidpointRounding.AwayFromZero);
                }
                else
                {
                    return Error("Score format is invalid.");
                }
            }
        }

        var bottleLabel = CreateBottleLabel(bottle);

        var hasNote = !string.IsNullOrWhiteSpace(noteText);
        if (!hasNote && score is null)
        {
            return Error("Add a tasting note or score.");
        }

        var noteToSave = hasNote ? noteText! : string.Empty;

        try
        {
            var existingNotes = await _tastingNoteRepository.GetByBottleIdAsync(request.BottleId, cancellationToken);
            var noteList = existingNotes.ToList();
            var existing = noteList.FirstOrDefault(note => note.UserId == currentUserId.Value);

            string message;

            if (existing is not null)
            {
                existing.Note = noteToSave;
                existing.Score = score;
                await _tastingNoteRepository.UpdateAsync(existing, cancellationToken);
                message = $"Updated your tasting note for {bottleLabel}.";
            }
            else
            {
                var entity = new TastingNote
                {
                    BottleId = request.BottleId,
                    UserId = currentUserId.Value,
                    Note = noteToSave,
                    Score = score
                };

                await _tastingNoteRepository.AddAsync(entity, cancellationToken);
                noteList.Add(entity);
                existing = entity;
                message = $"Saved a tasting note for {bottleLabel}.";
            }

            var noteId = existing.Id;

            decimal? averageScore = null;
            var scoreValues = noteList
                .Where(n => n.Score.HasValue)
                .Select(n => n.Score!.Value)
                .ToList();

            if (scoreValues.Count > 0)
            {
                averageScore = Math.Round(scoreValues.Average(), 1, MidpointRounding.AwayFromZero);
            }

            decimal? sisterhoodAverageScore = null;
            var sisterhoodMemberships = session.Sisterhood?.Memberships ?? Array.Empty<SisterhoodMembership>();
            var memberUserIds = sisterhoodMemberships
                .Select(membership => membership.UserId)
                .Where(userId => userId != Guid.Empty)
                .Distinct()
                .ToList();

            if (memberUserIds.Count > 0)
            {
                var averages = await _tastingNoteRepository.GetAverageScoresForWineVintagesByUsersAsync(
                    new[] { bottle.WineVintageId },
                    memberUserIds,
                    cancellationToken);

                if (averages.TryGetValue(bottle.WineVintageId, out var sisterhoodAverage))
                {
                    sisterhoodAverageScore = sisterhoodAverage;
                }
            }

            return Success(
                message,
                noteId,
                existing.Note ?? string.Empty,
                existing.Score,
                averageScore,
                sisterhoodAverageScore);
        }
        catch (Exception)
        {
            return Error("We couldn't save that tasting note right now. Please try again.", StatusCodes.Status500InternalServerError);
        }
    }
    [Authorize]
    [HttpPost("sisterhoods/sessions/delete-note")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSipSessionBottleNote(DeleteSipSessionBottleNoteRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid ||
            request.SisterhoodId == Guid.Empty ||
            request.SipSessionId == Guid.Empty ||
            request.BottleId == Guid.Empty ||
            request.NoteId == Guid.Empty)
        {
            TempData["SisterhoodError"] = "We couldn't delete that tasting note.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        var membership = await _sisterhoodRepository.GetMembershipAsync(request.SisterhoodId, currentUserId.Value, cancellationToken);
        if (membership is null)
        {
            TempData["SisterhoodError"] = "You must be part of this sisterhood to delete a tasting note.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var session = await _sipSessionRepository.GetByIdAsync(request.SipSessionId, cancellationToken);
        if (session is null || session.SisterhoodId != request.SisterhoodId)
        {
            TempData["SisterhoodError"] = "That sip session could not be found.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var sessionBottle = session.Bottles?
            .FirstOrDefault(link => link.BottleId == request.BottleId);
        if (sessionBottle?.Bottle is null)
        {
            TempData["SisterhoodError"] = "That bottle is no longer part of the sip session.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var bottle = sessionBottle.Bottle;
        var note = bottle.TastingNotes?.FirstOrDefault(n => n.Id == request.NoteId);
        if (note is null)
        {
            TempData["SisterhoodError"] = "That tasting note could not be found.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        if (note.UserId != currentUserId.Value)
        {
            TempData["SisterhoodError"] = "You can only delete your own tasting notes.";
            return RedirectToAction(nameof(Sisterhoods));
        }

        var bottleLabel = CreateBottleLabel(bottle);

        try
        {
            await _tastingNoteRepository.DeleteAsync(note.Id, cancellationToken);
            TempData["SisterhoodStatus"] = $"Deleted your tasting note for {bottleLabel}.";
        }
        catch (Exception)
        {
            TempData["SisterhoodError"] = "We couldn't delete that tasting note right now. Please try again.";
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

        if (!string.IsNullOrWhiteSpace(request.ReturnUrl) && Url.IsLocalUrl(request.ReturnUrl))
        {
            return Redirect(request.ReturnUrl);
        }

        return RedirectToAction(nameof(Sisterhoods));
    }

    private static DateTime DetermineSipSessionDrunkAt(SipSession session)
    {
        if (session is null)
        {
            return DateTime.UtcNow;
        }

        var normalizedScheduledAt = NormalizeToUtc(session.ScheduledAt);
        if (normalizedScheduledAt.HasValue)
        {
            return normalizedScheduledAt.Value;
        }

        if (session.Date.HasValue)
        {
            var date = session.Date.Value;
            return date.Kind switch
            {
                DateTimeKind.Utc => date,
                DateTimeKind.Local => date.ToUniversalTime(),
                _ => DateTime.SpecifyKind(date, DateTimeKind.Local).ToUniversalTime()
            };
        }

        return DateTime.UtcNow;
    }

    private static (DateTime? ScheduledAtLocal, DateTime? ScheduledDateLocal) ResolveSchedule(SipSessionRequestBase request)
    {
        if (request is null)
        {
            return (null, null);
        }

        DateTime? scheduledDateLocal = null;
        DateTime? scheduledAtLocal = null;

        if (request.ScheduledDate.HasValue)
        {
            var normalizedDate = NormalizeToLocal(request.ScheduledDate.Value).Date;
            scheduledDateLocal = DateTime.SpecifyKind(normalizedDate, DateTimeKind.Local);

            if (request.ScheduledTime.HasValue)
            {
                var combined = scheduledDateLocal.Value.Add(request.ScheduledTime.Value);
                scheduledAtLocal = DateTime.SpecifyKind(combined, DateTimeKind.Local);
            }
            else if (request.ScheduledAt.HasValue)
            {
                var existingLocal = NormalizeToLocal(request.ScheduledAt.Value);
                var combined = scheduledDateLocal.Value.Add(existingLocal.TimeOfDay);
                scheduledAtLocal = DateTime.SpecifyKind(combined, DateTimeKind.Local);
            }
        }
        else if (request.ScheduledAt.HasValue)
        {
            var existingLocal = NormalizeToLocal(request.ScheduledAt.Value);
            scheduledDateLocal = DateTime.SpecifyKind(existingLocal.Date, DateTimeKind.Local);

            if (request.ScheduledTime.HasValue)
            {
                var combined = scheduledDateLocal.Value.Add(request.ScheduledTime.Value);
                scheduledAtLocal = DateTime.SpecifyKind(combined, DateTimeKind.Local);
            }
            else
            {
                scheduledAtLocal = DateTime.SpecifyKind(existingLocal, DateTimeKind.Local);
            }
        }

        return (scheduledAtLocal, scheduledDateLocal);
    }

    private static DateTime NormalizeToLocal(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value.ToLocalTime(),
            DateTimeKind.Local => value,
            _ => DateTime.SpecifyKind(value, DateTimeKind.Local)
        };
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

    private async Task SetInventoryAddModalViewDataAsync(Guid? currentUserId, CancellationToken cancellationToken)
    {
        InventoryAddModalViewModel viewModel;

        if (!currentUserId.HasValue)
        {
            viewModel = new InventoryAddModalViewModel();
        }
        else
        {
            var bottleLocations = await _bottleLocationRepository.GetAllAsync(cancellationToken);
            var userLocations = bottleLocations
                .Where(location => location.UserId == currentUserId.Value)
                .OrderBy(location => location.Name)
                .Select(location => new BottleLocationOption
                {
                    Id = location.Id,
                    Name = location.Name,
                    Capacity = location.Capacity
                })
                .ToList();

            viewModel = new InventoryAddModalViewModel
            {
                Locations = userLocations
            };
        }

        ViewData["InventoryAddModal"] = viewModel;
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

    public class RemovePendingSisterhoodInvitationRequest
    {
        [Required]
        public Guid SisterhoodId { get; set; }

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

        [DataType(DataType.Date)]
        public DateTime? ScheduledDate { get; set; }

        [DataType(DataType.Time)]
        public TimeSpan? ScheduledTime { get; set; }
    }

    public class CreateSipSessionRequest : SipSessionRequestBase
    {
    }

    public class UpdateSipSessionRequest : SipSessionRequestBase
    {
        [Required]
        public Guid SipSessionId { get; set; }

        [StringLength(512)]
        public string? ReturnUrl { get; set; }
    }

    public class DeleteSipSessionRequest
    {
        [Required]
        public Guid SisterhoodId { get; set; }

        [Required]
        public Guid SipSessionId { get; set; }

        public string? ReturnUrl { get; set; }
    }

    public class ContributeSipSessionBottlesRequest
    {
        [Required]
        public Guid SisterhoodId { get; set; }

        [Required]
        public Guid SipSessionId { get; set; }

        public List<Guid> BottleIds { get; set; } = new();
    }

    public class RemoveSipSessionBottleRequest
    {
        [Required]
        public Guid SisterhoodId { get; set; }

        [Required]
        public Guid SipSessionId { get; set; }

        [Required]
        public Guid BottleId { get; set; }

        public string? ReturnUrl { get; set; }
    }

    public class RevealSipSessionBottleRequest
    {
        [Required]
        public Guid SisterhoodId { get; set; }

        [Required]
        public Guid SipSessionId { get; set; }

        [Required]
        public Guid BottleId { get; set; }
    }

    public class DrinkSipSessionBottleRequest
    {
        [Required]
        public Guid SisterhoodId { get; set; }

        [Required]
        public Guid SipSessionId { get; set; }

        [Required]
        public Guid BottleId { get; set; }
    }

    public class RateSipSessionBottleRequest
    {
        [Required]
        public Guid SisterhoodId { get; set; }

        [Required]
        public Guid SipSessionId { get; set; }

        [Required]
        public Guid BottleId { get; set; }

        [StringLength(2048)]
        public string? Note { get; set; }

        // Accept as raw string to avoid culture-specific model binding issues; we'll parse manually.
        public string? Score { get; set; }
    }

    public class DeleteSipSessionBottleNoteRequest
    {
        [Required]
        public Guid SisterhoodId { get; set; }

        [Required]
        public Guid SipSessionId { get; set; }

        [Required]
        public Guid BottleId { get; set; }

        [Required]
        public Guid NoteId { get; set; }
    }



   



    private static string CreateBottleLabel(Bottle? bottle)
    {
        if (bottle is null)
        {
            return "Bottle";
        }

        var wineName = bottle.WineVintage?.Wine?.Name;
        var labelBase = string.IsNullOrWhiteSpace(wineName)
            ? "Bottle"
            : wineName!.Trim();

        var vintage = bottle.WineVintage?.Vintage;
        if (vintage.HasValue && vintage.Value > 0)
        {
            labelBase = $"{labelBase} {vintage.Value}";
        }

        return labelBase;
    }

    

    

    

   


    private sealed record SipSessionFoodSuggestionPayload(
        [property: JsonPropertyName("suggestions")] IReadOnlyList<string> Suggestions,
        [property: JsonPropertyName("cheese")] string? Cheese);

    private static bool TryParseSipSessionFoodSuggestions(
        string? content,
        out IReadOnlyList<string> suggestions,
        out string? cheeseSuggestion)
    {
        suggestions = Array.Empty<string>();
        cheeseSuggestion = null;
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var segment = StringUtilities.ExtractJsonSegment(content);

        try
        {
            using var document = JsonDocument.Parse(segment, SipSessionFoodSuggestionJsonDocumentOptions);
            var root = document.RootElement;

            IEnumerable<JsonElement> candidateElements;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("suggestions", out var suggestionsElement) &&
                suggestionsElement.ValueKind == JsonValueKind.Array)
            {
                candidateElements = suggestionsElement.EnumerateArray();

                if (root.TryGetProperty("cheese", out var cheeseElement) && cheeseElement.ValueKind == JsonValueKind.String)
                {
                    var cheeseValue = cheeseElement.GetString();
                    if (!string.IsNullOrWhiteSpace(cheeseValue))
                    {
                        cheeseSuggestion = cheeseValue.Trim();
                    }
                }
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                candidateElements = root.EnumerateArray();
            }
            else
            {
                return false;
            }

            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var element in candidateElements)
            {
                if (element.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = element.GetString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                var trimmed = value.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (seen.Add(trimmed))
                {
                    list.Add(trimmed);
                    if (list.Count == 3)
                    {
                        break;
                    }
                }
            }

            if (list.Count == 0)
            {
                return false;
            }

            suggestions = list;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }



   

   




private static IReadOnlyList<WineSurferSipSessionBottle> CreateBottleSummaries(
    IEnumerable<SipSessionBottle>? sessionBottles,
    Guid? currentUserId = null,
    IReadOnlyDictionary<Guid, decimal>? sisterhoodAverageScores = null)
{
    return CreateBottleSummariesInternal(
        sessionBottles,
        link => link?.Bottle,
        link => link?.IsRevealed ?? false,
        currentUserId,
        sisterhoodAverageScores);
}

private static IReadOnlyList<WineSurferSipSessionBottle> CreateBottleSummaries(
    IEnumerable<Bottle>? bottles,
    Guid? currentUserId = null,
    IReadOnlyDictionary<Guid, decimal>? sisterhoodAverageScores = null)
{
    return CreateBottleSummariesInternal(
        bottles,
        bottle => bottle,
        _ => true,
        currentUserId,
        sisterhoodAverageScores);
}

private static IReadOnlyList<WineSurferSipSessionBottle> CreateBottleSummariesInternal<T>(
    IEnumerable<T>? source,
    Func<T, Bottle?> bottleSelector,
    Func<T, bool> isRevealedSelector,
    Guid? currentUserId,
    IReadOnlyDictionary<Guid, decimal>? sisterhoodAverageScores)
{
    if (source is null)
    {
        return Array.Empty<WineSurferSipSessionBottle>();
    }

    var summaries = source
        .Where(entry => entry is not null)
        .Select(entry =>
        {
            var bottle = bottleSelector(entry!);
            if (bottle is null)
            {
                return null;
            }

            var actualIsRevealed = isRevealedSelector(entry!);
            var isOwnedByCurrentUser = currentUserId.HasValue &&
                bottle.UserId.HasValue &&
                bottle.UserId.Value == currentUserId.Value;

            var labelBase = CreateBottleLabel(bottle);
            var rawWineName = bottle.WineVintage?.Wine?.Name;
            var wineName = string.IsNullOrWhiteSpace(rawWineName)
                ? "Bottle"
                : rawWineName!.Trim();

            var vintageValue = bottle.WineVintage?.Vintage;

            if (!actualIsRevealed && !isOwnedByCurrentUser)
            {
                wineName = "Mystery bottle";
                labelBase = "Mystery bottle";
                vintageValue = null;
            }

            TastingNote? currentUserNote = null;

            if (currentUserId.HasValue)
            {
                currentUserNote = bottle.TastingNotes?
                    .FirstOrDefault(note => note.UserId == currentUserId.Value);
            }

            var scoreValues = bottle.TastingNotes?
                .Where(note => note.Score.HasValue)
                .Select(note => note.Score!.Value)
                .ToList();

            decimal? averageScore = null;
            if (scoreValues is { Count: > 0 })
            {
                averageScore = scoreValues.Average();
            }

            decimal? sisterhoodAverageScore = null;
            var wineVintageId = bottle.WineVintageId;
            if (wineVintageId != Guid.Empty && sisterhoodAverageScores is not null &&
                sisterhoodAverageScores.TryGetValue(wineVintageId, out var average))
            {
                sisterhoodAverageScore = average;
            }

            if (!actualIsRevealed && !isOwnedByCurrentUser)
            {
                averageScore = null;
                sisterhoodAverageScore = null;
            }

            return new WineSurferSipSessionBottle(
                bottle.Id,
                wineName,
                vintageValue,
                labelBase,
                isOwnedByCurrentUser,
                bottle.IsDrunk,
                bottle.DrunkAt,
                currentUserNote?.Id,
                currentUserNote?.Note,
                currentUserNote?.Score,
                averageScore,
                sisterhoodAverageScore,
                actualIsRevealed);
        })
        .Where(summary => summary is not null)
        .OrderBy(summary => summary!.Label, StringComparer.OrdinalIgnoreCase)
        .Select(summary => summary!)
        .ToList();

    if (summaries.Count == 0)
    {
        return Array.Empty<WineSurferSipSessionBottle>();
    }

    return summaries;
}

    private static MapHighlightPoint? CreateHighlightPoint(
        Region region,
        RegionInventoryMetrics metrics)
    {
        var countryName = region.Country?.Name;
        if (!string.IsNullOrWhiteSpace(region.Name) && GeoCoordinatesService.RegionCoordinates.TryGetValue(region.Name, out var regionCoord))
        {
            return new MapHighlightPoint(
                Label: region.Name,
                Country: countryName ?? string.Empty,
                Latitude: regionCoord.Latitude,
                Longitude: regionCoord.Longitude,
                BottlesCellared: metrics.BottlesCellared,
                BottlesConsumed: metrics.BottlesConsumed,
                AverageScore: metrics.AverageScore,
                UserAverageScores: metrics.UserAverageScores,
                RegionName: region.Name);
        }

        if (!string.IsNullOrWhiteSpace(countryName) && GeoCoordinatesService.CountryCoordinates.TryGetValue(countryName, out var countryCoord))
        {
            var label = string.IsNullOrWhiteSpace(region.Name)
                ? countryName
                : $"{region.Name}, {countryName}";
            return new MapHighlightPoint(
                Label: label,
                Country: countryName,
                Latitude: countryCoord.Latitude,
                Longitude: countryCoord.Longitude,
                BottlesCellared: metrics.BottlesCellared,
                BottlesConsumed: metrics.BottlesConsumed,
                AverageScore: metrics.AverageScore,
                UserAverageScores: metrics.UserAverageScores,
                RegionName: region.Name);
        }

        return null;
    }

    private static MapHighlightPoint? CreateSuggestedHighlightPoint(WineSurferSuggestedAppellation suggestion)
    {
        if (suggestion is null)
        {
            return null;
        }

        var label = !string.IsNullOrWhiteSpace(suggestion.SubAppellationName)
            ? suggestion.SubAppellationName.Trim()
            : suggestion.AppellationName?.Trim();

        if (string.IsNullOrWhiteSpace(label))
        {
            label = !string.IsNullOrWhiteSpace(suggestion.RegionName)
                ? suggestion.RegionName.Trim()
                : suggestion.CountryName?.Trim();
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        var regionName = string.IsNullOrWhiteSpace(suggestion.RegionName)
            ? null
            : suggestion.RegionName.Trim();
        var countryName = string.IsNullOrWhiteSpace(suggestion.CountryName)
            ? null
            : suggestion.CountryName.Trim();
        var suggestionReason = string.IsNullOrWhiteSpace(suggestion.Reason)
            ? null
            : suggestion.Reason.Trim();

        static string BuildSubtitle(string? regionValue, string? countryValue)
        {
            var parts = new List<string>(2);
            if (!string.IsNullOrWhiteSpace(regionValue))
            {
                parts.Add(regionValue.Trim());
            }

            if (!string.IsNullOrWhiteSpace(countryValue))
            {
                parts.Add(countryValue.Trim());
            }

            return parts.Count > 0 ? string.Join(", ", parts) : string.Empty;
        }

        var subtitle = BuildSubtitle(regionName, countryName);

        if (!string.IsNullOrWhiteSpace(regionName) && GeoCoordinatesService.RegionCoordinates.TryGetValue(regionName, out var regionCoord))
        {
            return new MapHighlightPoint(
                Label: label,
                Country: subtitle,
                Latitude: regionCoord.Latitude,
                Longitude: regionCoord.Longitude,
                BottlesCellared: 0,
                BottlesConsumed: 0,
                AverageScore: null,
                UserAverageScores: Array.Empty<RegionUserAverageScore>(),
                IsSuggested: true,
                RegionName: regionName,
                SuggestionReason: suggestionReason);
        }

        if (!string.IsNullOrWhiteSpace(countryName) && GeoCoordinatesService.CountryCoordinates.TryGetValue(countryName, out var countryCoord))
        {
            return new MapHighlightPoint(
                Label: label,
                Country: subtitle,
                Latitude: countryCoord.Latitude,
                Longitude: countryCoord.Longitude,
                BottlesCellared: 0,
                BottlesConsumed: 0,
                AverageScore: null,
                UserAverageScores: Array.Empty<RegionUserAverageScore>(),
                IsSuggested: true,
                RegionName: regionName,
                SuggestionReason: suggestionReason);
        }

        return null;
    }
}

public record SurfEyeAnalysisResponse(string Summary, IReadOnlyList<SurfEyeWineMatch> Wines);

public record SurfEyeWineMatch(
    string Name,
    string? Producer,
    string? Country,
    string? Region,
    string? Appellation,
    string? SubAppellation,
    string? Variety,
    string? Color,
    string? Vintage,
    double AlignmentScore,
    string AlignmentSummary,
    double Confidence,
    string? Notes)
{
    public Guid? WineId { get; init; }
}

public record SurfEyeAnalysisError(string Error);

public class SurfEyeAnalysisRequest
{
    [Required]
    public IFormFile? Photo { get; set; }
}

public record WineSurferSurfEyeViewModel(
    string? DisplayName,
    string TasteProfileSummary,
    string TasteProfile,
    bool HasTasteProfile);

public record GenerateTasteProfileResponse(
    string Summary,
    string Profile,
    IReadOnlyList<GenerateTasteProfileSuggestion> Suggestions);

public record GenerateTasteProfileSuggestion(
    string Country,
    string Region,
    string Appellation,
    string? SubAppellation,
    string Reason,
    IReadOnlyList<GenerateTasteProfileWine> Wines);

public record GenerateTasteProfileWine(
    Guid WineId,
    string Name,
    string? Color,
    string? Variety,
    string? Vintage,
    string? SubAppellation);

public record GenerateTasteProfileError(string Error);

public record WineSurferTasteProfileViewModel(
    WineSurferCurrentUser? CurrentUser,
    IReadOnlyList<WineSurferIncomingSisterhoodInvitation> IncomingInvitations,
    IReadOnlyList<WineSurferSentInvitationNotification> SentInvitationNotifications,
    string TasteProfileSummary,
    int SummaryMaxLength,
    string TasteProfile,
    int MaxLength,
    IReadOnlyList<WineSurferSuggestedAppellation> SuggestedAppellations,
    string? StatusMessage,
    string? ErrorMessage,
    Guid? ActiveTasteProfileId,
    IReadOnlyList<WineSurferTasteProfileHistoryEntry> History);

public record WineSurferTasteProfileHistoryEntry(
    Guid Id,
    string Summary,
    string Profile,
    DateTime CreatedAtUtc,
    bool InUse);

public record WineSurferSuggestedAppellation(
    Guid SubAppellationId,
    string? SubAppellationName,
    Guid AppellationId,
    string AppellationName,
    string RegionName,
    string CountryName,
    string? Reason,
    IReadOnlyList<WineSurferSuggestedWine> Wines);

public record WineSurferSuggestedWine(
    Guid WineId,
    string Name,
    string? Color,
    string? Variety,
    string? Vintage,
    string? SubAppellationName);



public record WineSurferLandingViewModel(
    IReadOnlyList<MapHighlightPoint> HighlightPoints,
    WineSurferCurrentUser? CurrentUser,
    IReadOnlyList<WineSurferIncomingSisterhoodInvitation> IncomingInvitations,
    IReadOnlyList<WineSurferUpcomingSipSession> UpcomingSipSessions,
    IReadOnlyList<WineSurferSentInvitationNotification> SentInvitationNotifications,
    IReadOnlyList<WineSurferSisterhoodOption> ManageableSisterhoods,
    IReadOnlyList<WineSurferSipSessionBottle> FavoriteBottles,
    IReadOnlyList<WineSurferSuggestedAppellation> SuggestedAppellations,
    IReadOnlyCollection<Guid> InventoryWineIds);

public record WineSurferUpcomingSipSession(
    Guid SisterhoodId,
    string SisterhoodName,
    string? SisterhoodDescription,
    WineSurferSipSessionSummary Session);

public record WineSurferSipSessionCardModel(
    WineSurferSipSessionSummary Session,
    string ScheduleLabel,
    string LocationLabel,
    string SessionUrl,
    string AriaLabel,
    string? HostLabel);

public record WineSurferSipSessionDetailViewModel(
    WineSurferSipSessionSummary Session,
    Guid SisterhoodId,
    string SisterhoodName,
    string? SisterhoodDescription,
    bool CanManageSession,
    WineSurferCurrentUser? CurrentUser,
    IReadOnlyList<WineSurferIncomingSisterhoodInvitation> IncomingInvitations,
    IReadOnlyList<WineSurferSentInvitationNotification> SentInvitationNotifications,
    bool IsCreateMode,
    IReadOnlyList<WineSurferSisterhoodOption> ManageableSisterhoods,
    IReadOnlyList<WineSurferSipSessionBottle> AvailableBottles,
    IReadOnlyList<string>? FoodSuggestions = null,
    string? FoodSuggestionError = null,
    string? CheeseSuggestion = null);

public record WineSurferSisterhoodOption(Guid Id, string Name, string? Description);

public record WineSurferTopBarModel(
    string CurrentPath,
    string PanelHeading,
    string PanelAriaLabel,
    IReadOnlyList<WineSurferTopBarNotificationSection> Sections,
    IReadOnlyDictionary<string, IReadOnlyCollection<string>> DismissedNotificationStamps,
    IReadOnlyList<WineSurferTopBarLink> FooterLinks,
    string? DisplayName = null,
    bool IsAdmin = false)
{
    public const string SisterhoodNotificationsUrl = "/wine-surfer/sisterhoods";
    public const string DefaultPanelHeading = "Notifications";
    public const string DefaultPanelAriaLabel = "Wine Surfer notifications";

    private static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> EmptyDismissedMap =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, IReadOnlyCollection<string>> EmptyDismissedNotificationSet => EmptyDismissedMap;

    public static WineSurferTopBarModel Empty(string currentPath, bool isAdmin = false)
    {
        return new WineSurferTopBarModel(
            currentPath,
            DefaultPanelHeading,
            DefaultPanelAriaLabel,
            Array.Empty<WineSurferTopBarNotificationSection>(),
            EmptyDismissedMap,
            Array.Empty<WineSurferTopBarLink>(),
            null,
            isAdmin);
    }

    public static IReadOnlyList<WineSurferTopBarNotificationSection> BuildSections(
        IEnumerable<WineSurferIncomingSisterhoodInvitation> incomingInvitations,
        IEnumerable<WineSurferSentInvitationNotification> sentInvitationNotifications,
        string sisterhoodUrl)
    {
        var sections = new List<WineSurferTopBarNotificationSection>();

        if (incomingInvitations is not null)
        {
            var pendingNotifications = incomingInvitations
                .Where(invitation => invitation is not null && invitation.Status == SisterhoodInvitationStatus.Pending)
                .OrderBy(invitation => invitation.SisterhoodName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(invitation => invitation.CreatedAtUtc)
                .Select(invitation =>
                {
                    var notificationTitle = string.IsNullOrWhiteSpace(invitation.SisterhoodName)
                        ? invitation.InviteeEmail
                        : invitation.SisterhoodName;

                    var dismissLabel = string.IsNullOrWhiteSpace(invitation.SisterhoodName)
                        ? "Dismiss invitation"
                        : $"Dismiss invitation for {invitation.SisterhoodName}";

                    var bodySegments = new List<WineSurferTopBarTextSegment>
                    {
                        new("Invited as "),
                        new(invitation.InviteeEmail, true),
                    };

                    var tagLabel = invitation.MatchesUserId
                        ? "Linked to your account"
                        : invitation.MatchesEmail
                            ? "Matches your email"
                            : null;

                    return new WineSurferTopBarNotification(
                        "sisterhood.pending",
                        $"{invitation.Id:D}|{invitation.UpdatedAtUtc:O}",
                        notificationTitle ?? "Invitation",
                        bodySegments,
                        Array.Empty<WineSurferTopBarTextSegment>(),
                        tagLabel,
                        sisterhoodUrl,
                        invitation.UpdatedAtUtc,
                        dismissLabel);
                })
                .ToList();

            sections.Add(new WineSurferTopBarNotificationSection(
                "sisterhood.pending",
                "Pending invitations",
                "Pending invitations for you",
                "No pending invitations",
                pendingNotifications));
        }

        if (sentInvitationNotifications is not null)
        {
            var acceptedNotifications = sentInvitationNotifications
                .Where(notification => notification is not null)
                .OrderByDescending(notification => notification.UpdatedAtUtc)
                .ThenBy(notification => notification.InviteeName ?? notification.InviteeEmail, StringComparer.OrdinalIgnoreCase)
                .Select(notification =>
                {
                    var inviteeLabel = string.IsNullOrWhiteSpace(notification.InviteeName)
                        ? notification.InviteeEmail
                        : notification.InviteeName!;

                    var notificationTitle = string.IsNullOrWhiteSpace(inviteeLabel)
                        ? string.IsNullOrWhiteSpace(notification.InviteeEmail)
                            ? notification.SisterhoodName
                            : notification.InviteeEmail
                        : inviteeLabel;

                    var bodySegments = new List<WineSurferTopBarTextSegment>
                    {
                        new("Accepted your invitation to "),
                        new(notification.SisterhoodName, true),
                    };

                    if (!string.IsNullOrWhiteSpace(notification.InviteeEmail))
                    {
                        bodySegments.Add(new($" ({notification.InviteeEmail})"));
                    }

                    var dismissLabel = string.IsNullOrWhiteSpace(inviteeLabel)
                        ? "Dismiss notification"
                        : $"Dismiss notification for {inviteeLabel}";

                    return new WineSurferTopBarNotification(
                        "sisterhood.accepted",
                        $"{notification.InvitationId:D}|{notification.UpdatedAtUtc:O}",
                        notificationTitle ?? "Sisterhood invitation",
                        bodySegments,
                        Array.Empty<WineSurferTopBarTextSegment>(),
                        null,
                        sisterhoodUrl,
                        notification.UpdatedAtUtc,
                        dismissLabel);
                })
                .ToList();

            sections.Add(new WineSurferTopBarNotificationSection(
                "sisterhood.accepted",
                "Accepted invitations",
                "Accepted invitations you sent",
                "No accepted invitations",
                acceptedNotifications));
        }

        return sections;
    }

    public static WineSurferTopBarModel CreateFromSisterhoodData(
        string currentPath,
        IEnumerable<WineSurferIncomingSisterhoodInvitation> incomingInvitations,
        IEnumerable<WineSurferSentInvitationNotification> sentInvitationNotifications,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? dismissed = null,
        IReadOnlyList<WineSurferTopBarNotificationSection>? precomputedSections = null,
        string? displayName = null,
        bool isAdmin = false)
    {
        var sections = precomputedSections ?? BuildSections(
            incomingInvitations,
            sentInvitationNotifications,
            SisterhoodNotificationsUrl);

        var hasSisterhoodNotifications = sections.Any(section =>
            section.Key.StartsWith("sisterhood.", StringComparison.OrdinalIgnoreCase));

        var footerLinks = hasSisterhoodNotifications
            ? new[] { new WineSurferTopBarLink(SisterhoodNotificationsUrl, "View Sisterhoods") }
            : Array.Empty<WineSurferTopBarLink>();

        IReadOnlyDictionary<string, IReadOnlyCollection<string>> dismissedMap;
        if (dismissed is null || dismissed.Count == 0)
        {
            dismissedMap = EmptyDismissedMap;
        }
        else if (ReferenceEquals(dismissed, EmptyDismissedMap))
        {
            dismissedMap = EmptyDismissedMap;
        }
        else
        {
            dismissedMap = new Dictionary<string, IReadOnlyCollection<string>>(dismissed, StringComparer.OrdinalIgnoreCase);
        }

        var trimmedDisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();

        return new WineSurferTopBarModel(
            currentPath,
            DefaultPanelHeading,
            DefaultPanelAriaLabel,
            sections,
            dismissedMap,
            footerLinks,
            trimmedDisplayName,
            isAdmin);
    }
}

public record WineSurferTopBarNotificationSection(
    string Key,
    string Heading,
    string? AriaLabel,
    string EmptyMessage,
    IReadOnlyList<WineSurferTopBarNotification> Notifications);

public record WineSurferTopBarNotification(
    string Category,
    string Stamp,
    string Title,
    IReadOnlyList<WineSurferTopBarTextSegment> Body,
    IReadOnlyList<WineSurferTopBarTextSegment> Meta,
    string? Tag,
    string? Url,
    DateTime? OccurredAtUtc,
    string? DismissLabel);

public record WineSurferTopBarTextSegment(string Text, bool Emphasize = false);

public record WineSurferTopBarLink(string Href, string Label);

public record WineSurferPageHeaderModel(string Title);

public record WineSurferSisterhoodsViewModel(
    bool IsAuthenticated,
    string? DisplayName,
    bool IsAdmin,
    IReadOnlyList<WineSurferSisterhoodSummary> Sisterhoods,
    Guid? CurrentUserId,
    string? StatusMessage,
    string? ErrorMessage,
    IReadOnlyList<WineSurferIncomingSisterhoodInvitation> IncomingInvitations,
    IReadOnlyList<WineSurferSentInvitationNotification> SentInvitationNotifications,
    IReadOnlyList<WineSurferSipSessionBottle> AvailableBottles);

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
    DateTime UpdatedAtUtc,
    IReadOnlyList<WineSurferSipSessionBottle> Bottles);

public record WineSurferSipSessionBottle(
    Guid Id,
    string WineName,
    int? Vintage,
    string Label,
    bool IsOwnedByCurrentUser,
    bool IsDrunk,
    DateTime? DrunkAtUtc,
    Guid? CurrentUserNoteId,
    string? CurrentUserNote,
    decimal? CurrentUserScore,
    decimal? AverageScore,
    decimal? SisterhoodAverageScore,
    bool IsRevealed);

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
    IReadOnlyList<RegionUserAverageScore> UserAverageScores,
    bool IsSuggested = false,
    string? RegionName = null,
    string? SuggestionReason = null);

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

public record WineSurferSentInvitationNotification(
    Guid InvitationId,
    Guid SisterhoodId,
    string SisterhoodName,
    string InviteeEmail,
    string? InviteeName,
    DateTime UpdatedAtUtc);

public record RegionInventoryMetrics(
    int BottlesCellared,
    int BottlesConsumed,
    decimal? AverageScore,
    IReadOnlyList<RegionUserAverageScore> UserAverageScores);

public record RegionUserAverageScore(Guid UserId, decimal AverageScore);

public record WineSurferUserSummary(Guid Id, string Name, string Email, string TasteProfileSummary, string TasteProfile);
public record WineSurferCurrentUser(Guid? DomainUserId, string DisplayName, string? Email, string? TasteProfileSummary, string? TasteProfile, bool IsAdmin);
