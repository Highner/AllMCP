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
using OpenAI.Chat;

namespace AllMCPSolution.Controllers;

[Route("wine-surfer")]
public class WineSurferController : Controller
{
    [Route("debug/config")]
    public IActionResult ConfigTest([FromServices] IConfiguration config)
    {
        var apiKey = config["OpenAI:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
            return Ok("OpenAI API key not found 😕");
        return Ok("OpenAI API key is loaded ✅");
    }

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
    private readonly IBottleRepository _bottleRepository;
    private readonly IBottleLocationRepository _bottleLocationRepository;
    private readonly ITastingNoteRepository _tastingNoteRepository;
    private readonly ICountryRepository _countryRepository;
    private readonly IRegionRepository _regionRepository;
    private readonly IAppellationRepository _appellationRepository;
    private readonly ISubAppellationRepository _subAppellationRepository;
    private readonly ISuggestedAppellationRepository _suggestedAppellationRepository;
    private readonly IWineCatalogService _wineCatalogService;
    private readonly IChatGptService _chatGptService;
    private static readonly TimeSpan SentInvitationNotificationWindow = TimeSpan.FromDays(7);
    private const int TasteProfileMaxLength = 4096;
    private const int TasteProfileSummaryMaxLength = 512;
    private const string TasteProfileStatusTempDataKey = "WineSurfer.TasteProfile.Status";
    private const string TasteProfileErrorTempDataKey = "WineSurfer.TasteProfile.Error";
    private const string TasteProfileStreamMediaType = "application/x-ndjson";
    private const string TasteProfileStreamingStartMessage = "Contacting the taste profile assistant…";
    private const string TasteProfileStreamingFinalizeMessage = "Finalizing your taste profile…";
    private const string TasteProfileStreamingSuggestionsMessage = "Matching appellations to your palate…";
    private const string TasteProfileStreamingSuccessMessage = "We generated a new taste profile. Review and save it when you’re ready.";
    private const string TasteProfileAssistantUnavailableErrorMessage = "We couldn't reach the taste profile assistant. Please try again.";
    private const string TasteProfileGenerationGenericErrorMessage = "We couldn't generate a taste profile right now. Please try again.";
    private const string TasteProfileAssistantUnexpectedResponseMessage = "We couldn't understand the taste profile assistant's response. Please try again.";
    private const string TasteProfileInsufficientDataErrorMessage = "Add scores to a few bottles before generating a taste profile.";
    private const string TasteProfileGenerationSystemPrompt =
        "You are an expert sommelier assistant. Respond ONLY with valid minified JSON like {\"summary\":\"...\",\"profile\":\"...\",\"suggestedAppellations\":[{\"country\":\"...\",\"region\":\"...\",\"appellation\":\"...\",\"subAppellation\":null,\"reason\":\"...\",\"wines\":[{\"name\":\"...\",\"color\":\"Red\",\"variety\":\"...\",\"subAppellation\":null,\"vintage\":\"2019\"},{\"name\":\"...\",\"color\":\"White\",\"variety\":null,\"subAppellation\":null,\"vintage\":\"NV\"}]}]}. " +
        "The summary must be 200 characters or fewer and offer a concise descriptor of the user's palate. " +
        "The profile must be 3-5 sentences, 1500 characters or fewer, written in the second person, and describe styles, structure, and flavor preferences without recommending specific new wines. " +
        "The suggestedAppellations array must contain exactly two entries describing appellations or sub-appellations that fit the profile, each with country, region, appellation strings, subAppellation set to a string or null, and a single-sentence reason of 200 characters or fewer explaining the match. " +
        "For each suggested appellation, include a wines array with two or three entries highlighting wines from that location. Each wine must provide the full label name (producer and cuvée), a color of Red, White, or Rose, an optional variety string or null, an optional subAppellation or null, and a vintage string that is either a 4-digit year or \"NV\". " +
        "Do not include markdown, bullet lists, code fences, or any explanatory text outside the JSON object.";
    private static readonly JsonDocumentOptions TasteProfileJsonDocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };
    private static readonly JsonSerializerOptions TasteProfileStreamSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private const double SuggestedAppellationFuzzyThreshold = 0.35;
    private const long SurfEyeMaxUploadBytes = 8 * 1024 * 1024;
    private const string SurfEyeSystemPrompt = """
You are Surf Eye, an expert sommelier and computer vision guide. Use the user's taste profile to rank wines from best to worst alignment.
Respond ONLY with minified JSON matching {"analysisSummary":"...","wines":[{"name":"...","producer":"...","country":"...","region":"...","appellation":"...","subAppellation":"...","variety":"...","color":"Red","vintage":"...","alignmentScore":0,"alignmentSummary":"...","confidence":0.0,"notes":"..."}]}.
The wines array must be sorted by descending alignmentScore. Include at most five wines. Provide concise notes that justify the ranking with respect to the taste profile. Report each wine's color using Red, White, or Rose and set any unknown fields to null. The alignmentScore must be an integer from 0 to 100. Confidence must be a decimal between 0 and 1. If no wine is recognized, return an empty wines array and set analysisSummary to a short explanation. Do not use markdown, newlines, or any commentary outside of the JSON object.
""";
    private static readonly JsonDocumentOptions SurfEyeJsonDocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };
    private const string SipSessionFoodSuggestionSystemPrompt = """
You are an expert sommelier assistant. Recommend three distinct food pairings that can be served together with the wines provided by the user.
Ensure at least one suggestion is vegetarian and begin that entry with "(Vegetarian)".
Respond ONLY with minified JSON matching {"suggestions":["Suggestion 1","Suggestion 2","Suggestion 3"],"cheese":"Cheese course"}.
Each suggestion must be a short dish description followed by a concise reason, and the cheese field must describe a dedicated cheese course pairing. Do not include numbering, markdown, or any other fields.
""";
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

    public WineSurferController(
        IWineRepository wineRepository,
        IUserRepository userRepository,
        ISisterhoodRepository sisterhoodRepository,
        ISisterhoodInvitationRepository sisterhoodInvitationRepository,
        ISipSessionRepository sipSessionRepository,
        IBottleRepository bottleRepository,
        IBottleLocationRepository bottleLocationRepository,
        ITastingNoteRepository tastingNoteRepository,
        ICountryRepository countryRepository,
        IRegionRepository regionRepository,
        IAppellationRepository appellationRepository,
        ISubAppellationRepository subAppellationRepository,
        ISuggestedAppellationRepository suggestedAppellationRepository,
        IWineCatalogService wineCatalogService,
        IChatGptService chatGptService,
        IWineSurferTopBarService topBarService)
    {
        _wineRepository = wineRepository;
        _userRepository = userRepository;
        _sisterhoodRepository = sisterhoodRepository;
        _sisterhoodInvitationRepository = sisterhoodInvitationRepository;
        _sipSessionRepository = sipSessionRepository;
        _bottleRepository = bottleRepository;
        _bottleLocationRepository = bottleLocationRepository;
        _tastingNoteRepository = tastingNoteRepository;
        _countryRepository = countryRepository;
        _regionRepository = regionRepository;
        _appellationRepository = appellationRepository;
        _subAppellationRepository = subAppellationRepository;
        _suggestedAppellationRepository = suggestedAppellationRepository;
        _wineCatalogService = wineCatalogService;
        _chatGptService = chatGptService;
        _topBarService = topBarService;
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
            var normalizedEmail = NormalizeEmailCandidate(email);
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

            var displayName = ResolveDisplayName(domainUser?.Name, identityName, email);

            if (domainUser is not null)
            {
                currentUserId = domainUser.Id;
                normalizedEmail ??= NormalizeEmailCandidate(domainUser.Email);
            }

            if (string.IsNullOrWhiteSpace(normalizedEmail) && LooksLikeEmail(displayName))
            {
                normalizedEmail = NormalizeEmailCandidate(displayName);
            }

            if (!string.IsNullOrWhiteSpace(displayName) || !string.IsNullOrWhiteSpace(email) || domainUser is not null)
            {
                currentUser = new WineSurferCurrentUser(
                    domainUser?.Id,
                    displayName ?? email ?? string.Empty,
                    email,
                    domainUser?.TasteProfileSummary,
                    domainUser?.TasteProfile,
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

            sentInvitationNotifications = CreateSentInvitationNotifications(acceptedInvitations);
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

            suggestedAppellations = await GetSuggestedAppellationsForUserAsync(currentUserId.Value, cancellationToken);
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

    [Authorize]
    [HttpGet("taste-profile")]
    public async Task<IActionResult> TasteProfile(CancellationToken cancellationToken)
    {
        var currentPath = HttpContext?.Request?.Path.Value ?? string.Empty;
        ViewData["WineSurferTopBarModel"] = await _topBarService.BuildAsync(User, currentPath, cancellationToken);
        Response.ContentType = "text/html; charset=utf-8";

        var now = DateTime.UtcNow;
        WineSurferCurrentUser? currentUser = null;
        IReadOnlyList<WineSurferIncomingSisterhoodInvitation> incomingInvitations = Array.Empty<WineSurferIncomingSisterhoodInvitation>();
        IReadOnlyList<WineSurferSentInvitationNotification> sentInvitationNotifications = Array.Empty<WineSurferSentInvitationNotification>();
        Guid? currentUserId = null;
        string? normalizedEmail = null;
        ApplicationUser? domainUser = null;
        var isAdmin = false;

        if (User?.Identity?.IsAuthenticated == true)
        {
            var identityName = User.Identity?.Name;
            var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
            normalizedEmail = NormalizeEmailCandidate(email);
            currentUserId = GetCurrentUserId();

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

            if (!string.IsNullOrWhiteSpace(displayName) || !string.IsNullOrWhiteSpace(email) || domainUser is not null)
            {
                currentUser = new WineSurferCurrentUser(
                    domainUser?.Id,
                    displayName ?? email ?? string.Empty,
                    email,
                    domainUser?.TasteProfileSummary,
                    domainUser?.TasteProfile,
                    isAdmin);
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

        if (currentUserId.HasValue && isAdmin)
        {
            var acceptedInvitations = await _sisterhoodInvitationRepository.GetAcceptedForAdminAsync(
                currentUserId.Value,
                now - SentInvitationNotificationWindow,
                cancellationToken);

            sentInvitationNotifications = CreateSentInvitationNotifications(acceptedInvitations);
        }

        var statusMessage = TempData.ContainsKey(TasteProfileStatusTempDataKey)
            ? TempData[TasteProfileStatusTempDataKey] as string
            : null;
        var errorMessage = TempData.ContainsKey(TasteProfileErrorTempDataKey)
            ? TempData[TasteProfileErrorTempDataKey] as string
            : null;

        var tasteProfileSummary = domainUser?.TasteProfileSummary ?? currentUser?.TasteProfileSummary ?? string.Empty;
        var tasteProfile = domainUser?.TasteProfile ?? currentUser?.TasteProfile ?? string.Empty;
        IReadOnlyList<WineSurferSuggestedAppellation> suggestedAppellations = Array.Empty<WineSurferSuggestedAppellation>();

        if (currentUserId.HasValue)
        {
            suggestedAppellations = await GetSuggestedAppellationsForUserAsync(currentUserId.Value, cancellationToken);
        }

        var viewModel = new WineSurferTasteProfileViewModel(
            currentUser,
            incomingInvitations,
            sentInvitationNotifications,
            tasteProfileSummary,
            TasteProfileSummaryMaxLength,
            tasteProfile,
            TasteProfileMaxLength,
            suggestedAppellations,
            statusMessage,
            errorMessage);

        return View("TasteProfile", viewModel);
    }

    [Authorize]
    [HttpPost("taste-profile")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTasteProfile([FromForm] UpdateTasteProfileRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var summaryErrors = ModelState.TryGetValue(nameof(UpdateTasteProfileRequest.TasteProfileSummary), out var summaryEntry)
                && summaryEntry.Errors.Count > 0;
            var profileErrors = ModelState.TryGetValue(nameof(UpdateTasteProfileRequest.TasteProfile), out var profileEntry)
                && profileEntry.Errors.Count > 0;

            string errorMessage = profileErrors && summaryErrors
                ? $"Taste profile must be {TasteProfileMaxLength} characters or fewer and summary must be {TasteProfileSummaryMaxLength} characters or fewer."
                : summaryErrors
                    ? $"Taste profile summary must be {TasteProfileSummaryMaxLength} characters or fewer."
                    : $"Taste profile must be {TasteProfileMaxLength} characters or fewer.";

            TempData[TasteProfileErrorTempDataKey] = errorMessage;
            return RedirectToAction(nameof(TasteProfile));
        }

        var userId = GetCurrentUserId();
        if (!userId.HasValue)
        {
            return Unauthorized();
        }

        var trimmedTasteProfile = string.IsNullOrWhiteSpace(request.TasteProfile)
            ? string.Empty
            : request.TasteProfile.Trim();

        var trimmedSummary = string.IsNullOrWhiteSpace(request.TasteProfileSummary)
            ? string.Empty
            : request.TasteProfileSummary.Trim();

        if (trimmedTasteProfile.Length > TasteProfileMaxLength)
        {
            TempData[TasteProfileErrorTempDataKey] = $"Taste profile must be {TasteProfileMaxLength} characters or fewer.";
            return RedirectToAction(nameof(TasteProfile));
        }

        if (trimmedSummary.Length > TasteProfileSummaryMaxLength)
        {
            TempData[TasteProfileErrorTempDataKey] = $"Taste profile summary must be {TasteProfileSummaryMaxLength} characters or fewer.";
            return RedirectToAction(nameof(TasteProfile));
        }

        try
        {
            var updatedUser = await _userRepository.UpdateTasteProfileAsync(
                userId.Value,
                trimmedTasteProfile,
                trimmedSummary,
                cancellationToken);
            if (updatedUser is null)
            {
                TempData[TasteProfileErrorTempDataKey] = "We couldn't update your taste profile. Please try again.";
                return RedirectToAction(nameof(TasteProfile));
            }

            TempData.Remove(TasteProfileErrorTempDataKey);
            TempData[TasteProfileStatusTempDataKey] = "Your taste profile was updated.";
        }
        catch (Exception)
        {
            TempData[TasteProfileErrorTempDataKey] = "We couldn't update your taste profile. Please try again.";
        }

        return RedirectToAction(nameof(TasteProfile));
    }

    [Authorize]
    [HttpPost("taste-profile/generate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateTasteProfile(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
        {
            return Unauthorized();
        }

        var prefersStreaming = RequestPrefersTasteProfileStreaming();

        var bottles = await _bottleRepository.GetForUserAsync(userId.Value, cancellationToken);
        var scoredBottles = bottles
            .Select(bottle =>
            {
                var note = bottle.TastingNotes?
                    .FirstOrDefault(tn => tn.UserId == userId.Value && tn.Score.HasValue);
                return (Bottle: bottle, Note: note);
            })
            .Where(entry => entry.Note is not null)
            .Select(entry => (Bottle: entry.Bottle, Note: entry.Note!))
            .OrderByDescending(entry => entry.Note.Score!.Value)
            .Take(25)
            .ToList();

        if (scoredBottles.Count == 0)
        {
            return BadRequest(new GenerateTasteProfileError(TasteProfileInsufficientDataErrorMessage));
        }

        var prompt = BuildTasteProfilePrompt(scoredBottles);
        if (prefersStreaming)
        {
            return await StreamTasteProfileGenerationAsync(userId.Value, prompt, cancellationToken);
        }

        return await GenerateTasteProfileJsonAsync(userId.Value, prompt, cancellationToken);
    }

    private bool RequestPrefersTasteProfileStreaming()
    {
        if (Request?.Headers is null)
        {
            return false;
        }

        if (!Request.Headers.TryGetValue("Accept", out var acceptValues))
        {
            return false;
        }

        foreach (var value in acceptValues)
        {
            if (!string.IsNullOrWhiteSpace(value)
                && value.IndexOf(TasteProfileStreamMediaType, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<IActionResult> GenerateTasteProfileJsonAsync(
        int userId,
        string prompt,
        CancellationToken cancellationToken)
    {
        ChatCompletion completion;
        try
        {
            completion = await _chatGptService.GetChatCompletionAsync(
                new ChatMessage[]
                {
                    new SystemChatMessage(TasteProfileGenerationSystemPrompt),
                    new UserChatMessage(prompt)
                },
                ct: cancellationToken);
        }
        catch (ClientResultException)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new GenerateTasteProfileError(TasteProfileAssistantUnavailableErrorMessage));
        }
        catch (Exception)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new GenerateTasteProfileError(TasteProfileGenerationGenericErrorMessage));
        }

        var content = ExtractChatCompletionContent(completion);
        if (!TryParseGeneratedTasteProfile(content, out var generatedProfile))
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new GenerateTasteProfileError(TasteProfileAssistantUnexpectedResponseMessage));
        }

        if (!TryNormalizeGeneratedTasteProfile(generatedProfile, out var summary, out var profile, out var normalizationError))
        {
            var errorMessage = normalizationError ?? TasteProfileGenerationGenericErrorMessage;
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new GenerateTasteProfileError(errorMessage));
        }

        var resolvedSuggestions = await ResolveSuggestedAppellationsAsync(
            userId,
            generatedProfile.Suggestions,
            cancellationToken);

        var response = new GenerateTasteProfileResponse(
            summary,
            profile,
            BuildTasteProfileSuggestions(resolvedSuggestions));

        return Json(response);
    }

    private async Task<IActionResult> StreamTasteProfileGenerationAsync(
        int userId,
        string prompt,
        CancellationToken cancellationToken)
    {
        var response = Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = TasteProfileStreamMediaType;
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";

        await response.StartAsync(cancellationToken);

        await WriteTasteProfileEventAsync(
            response,
            new { type = "status", stage = "starting", message = TasteProfileStreamingStartMessage },
            cancellationToken);

        var accumulator = new TasteProfileStreamAccumulator();
        string? lastSummary = null;
        string? lastProfile = null;

        try
        {
            await foreach (var chunk in _chatGptService
                .StreamChatCompletionAsync(
                    new ChatMessage[]
                    {
                        new SystemChatMessage(TasteProfileGenerationSystemPrompt),
                        new UserChatMessage(prompt)
                    },
                    ct: cancellationToken)
                .WithCancellation(cancellationToken))
            {
                if (string.IsNullOrEmpty(chunk))
                {
                    continue;
                }

                var update = accumulator.Append(chunk);

                if (update.HasSummaryUpdate && !string.IsNullOrWhiteSpace(update.Summary))
                {
                    lastSummary = update.Summary;
                    await WriteTasteProfileEventAsync(
                        response,
                        new { type = "summary", text = update.Summary, complete = false },
                        cancellationToken);
                }

                if (update.HasProfileUpdate && !string.IsNullOrWhiteSpace(update.Profile))
                {
                    lastProfile = update.Profile;
                    await WriteTasteProfileEventAsync(
                        response,
                        new { type = "profile", text = update.Profile, complete = false },
                        cancellationToken);
                }
            }

            var finalUpdate = accumulator.Complete();

            if (finalUpdate.HasSummaryUpdate && !string.IsNullOrWhiteSpace(finalUpdate.Summary))
            {
                lastSummary = finalUpdate.Summary;
                await WriteTasteProfileEventAsync(
                    response,
                    new { type = "summary", text = finalUpdate.Summary, complete = true },
                    cancellationToken);
            }

            if (finalUpdate.HasProfileUpdate && !string.IsNullOrWhiteSpace(finalUpdate.Profile))
            {
                lastProfile = finalUpdate.Profile;
                await WriteTasteProfileEventAsync(
                    response,
                    new { type = "profile", text = finalUpdate.Profile, complete = true },
                    cancellationToken);
            }

            if (!finalUpdate.IsFinalPayloadReady
                || !TryParseGeneratedTasteProfile(finalUpdate.FinalContent, out var generatedProfile))
            {
                await WriteTasteProfileEventAsync(
                    response,
                    new { type = "error", message = TasteProfileAssistantUnexpectedResponseMessage },
                    cancellationToken);
                return new EmptyResult();
            }

            await WriteTasteProfileEventAsync(
                response,
                new { type = "status", stage = "finalizing", message = TasteProfileStreamingFinalizeMessage },
                cancellationToken);

            if (!TryNormalizeGeneratedTasteProfile(generatedProfile, out var summary, out var profile, out var normalizationError))
            {
                await WriteTasteProfileEventAsync(
                    response,
                    new { type = "error", message = normalizationError ?? TasteProfileGenerationGenericErrorMessage },
                    cancellationToken);
                return new EmptyResult();
            }

            if (!string.IsNullOrWhiteSpace(summary)
                && !string.Equals(lastSummary, summary, StringComparison.Ordinal))
            {
                await WriteTasteProfileEventAsync(
                    response,
                    new { type = "summary", text = summary, complete = true },
                    cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(profile)
                && !string.Equals(lastProfile, profile, StringComparison.Ordinal))
            {
                await WriteTasteProfileEventAsync(
                    response,
                    new { type = "profile", text = profile, complete = true },
                    cancellationToken);
            }

            await WriteTasteProfileEventAsync(
                response,
                new { type = "status", stage = "resolving", message = TasteProfileStreamingSuggestionsMessage },
                cancellationToken);

            var resolvedSuggestions = await ResolveSuggestedAppellationsAsync(
                userId,
                generatedProfile.Suggestions,
                cancellationToken);

            var payload = new GenerateTasteProfileResponse(
                summary,
                profile,
                BuildTasteProfileSuggestions(resolvedSuggestions));

            await WriteTasteProfileEventAsync(
                response,
                new
                {
                    type = "complete",
                    message = TasteProfileStreamingSuccessMessage,
                    payload
                },
                cancellationToken);
        }
        catch (ClientResultException)
        {
            await WriteTasteProfileEventAsync(
                response,
                new { type = "error", message = TasteProfileAssistantUnavailableErrorMessage },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Client disconnected; no further payloads are required.
        }
        catch (Exception)
        {
            await WriteTasteProfileEventAsync(
                response,
                new { type = "error", message = TasteProfileGenerationGenericErrorMessage },
                cancellationToken);
        }
        finally
        {
            try
            {
                await response.BodyWriter.FlushAsync(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // Ignore flush failures triggered by disconnected clients.
            }
        }

        return new EmptyResult();
    }

    private static async Task WriteTasteProfileEventAsync(
        HttpResponse response,
        object payload,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, TasteProfileStreamSerializerOptions);
        var encoded = Encoding.UTF8.GetBytes(json + "\n");
        await response.BodyWriter.WriteAsync(encoded, cancellationToken);
        await response.BodyWriter.FlushAsync(cancellationToken);
    }

    private static string? ExtractChatCompletionContent(ChatCompletion completion)
    {
        if (completion?.Content is not { Count: > 0 })
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var part in completion.Content)
        {
            if (part.Kind == ChatMessageContentPartKind.Text && !string.IsNullOrWhiteSpace(part.Text))
            {
                builder.Append(part.Text);
            }
        }

        return builder.Length > 0 ? builder.ToString() : null;
    }

    private bool TryNormalizeGeneratedTasteProfile(
        GeneratedTasteProfile generatedProfile,
        out string summary,
        out string profile,
        out string? errorMessage)
    {
        profile = NormalizeGeneratedText(generatedProfile.Profile, TasteProfileMaxLength);
        if (string.IsNullOrWhiteSpace(profile))
        {
            summary = string.Empty;
            errorMessage = TasteProfileGenerationGenericErrorMessage;
            return false;
        }

        summary = NormalizeGeneratedText(generatedProfile.Summary, TasteProfileSummaryMaxLength);
        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = BuildSummaryFallback(profile, TasteProfileSummaryMaxLength);
        }

        errorMessage = null;
        return true;
    }

    private static List<GenerateTasteProfileSuggestion> BuildTasteProfileSuggestions(
        IReadOnlyList<WineSurferSuggestedAppellation> resolvedSuggestions)
    {
        if (resolvedSuggestions is null || resolvedSuggestions.Count == 0)
        {
            return new List<GenerateTasteProfileSuggestion>();
        }

        var suggestions = new List<GenerateTasteProfileSuggestion>(resolvedSuggestions.Count);
        foreach (var suggestion in resolvedSuggestions)
        {
            if (suggestion is null)
            {
                continue;
            }

            var wines = suggestion.Wines?.Select(wine => new GenerateTasteProfileWine(
                    wine.WineId,
                    wine.Name,
                    wine.Color,
                    wine.Variety,
                    wine.Vintage,
                    wine.SubAppellationName))
                .ToList() ?? new List<GenerateTasteProfileWine>();

            suggestions.Add(new GenerateTasteProfileSuggestion(
                suggestion.CountryName,
                suggestion.RegionName,
                suggestion.AppellationName,
                suggestion.SubAppellationName,
                suggestion.Reason ?? string.Empty,
                wines));
        }

        return suggestions;
    }

    private sealed class TasteProfileStreamAccumulator
    {
        private readonly StringBuilder _builder = new();
        private readonly JsonReaderOptions _readerOptions = new()
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };
        private string? _lastSummary;
        private string? _lastProfile;

        public TasteProfileStreamUpdate Append(string chunk)
        {
            if (!string.IsNullOrEmpty(chunk))
            {
                _builder.Append(chunk);
            }

            return ParseSnapshot(isFinal: false);
        }

        public TasteProfileStreamUpdate Complete()
        {
            return ParseSnapshot(isFinal: true);
        }

        private TasteProfileStreamUpdate ParseSnapshot(bool isFinal)
        {
            var update = new TasteProfileStreamUpdate();

            if (_builder.Length == 0)
            {
                return update;
            }

            var snapshot = _builder.ToString();
            var bytes = Encoding.UTF8.GetBytes(snapshot);
            var reader = new Utf8JsonReader(bytes, isFinal, new JsonReaderState(_readerOptions));
            string? currentProperty = null;

            try
            {
                while (reader.Read())
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.PropertyName:
                            currentProperty = reader.GetString();
                            break;
                        case JsonTokenType.String:
                            if (string.Equals(currentProperty, "summary", StringComparison.OrdinalIgnoreCase))
                            {
                                var value = reader.GetString();
                                if (!string.IsNullOrWhiteSpace(value)
                                    && !string.Equals(value, _lastSummary, StringComparison.Ordinal))
                                {
                                    _lastSummary = value.Trim();
                                    update.Summary = _lastSummary;
                                    update.HasSummaryUpdate = true;
                                }
                            }
                            else if (string.Equals(currentProperty, "profile", StringComparison.OrdinalIgnoreCase))
                            {
                                var value = reader.GetString();
                                if (!string.IsNullOrWhiteSpace(value)
                                    && !string.Equals(value, _lastProfile, StringComparison.Ordinal))
                                {
                                    _lastProfile = value.Trim();
                                    update.Profile = _lastProfile;
                                    update.HasProfileUpdate = true;
                                }
                            }
                            break;
                    }
                }

                if (isFinal)
                {
                    update.IsFinalPayloadReady = true;
                    update.FinalContent = snapshot;
                }
            }
            catch (JsonException)
            {
                if (isFinal)
                {
                    update.IsFinalPayloadReady = true;
                    update.FinalContent = snapshot;
                }
            }

            return update;
        }
    }

    private sealed class TasteProfileStreamUpdate
    {
        public bool HasSummaryUpdate { get; set; }
        public string? Summary { get; set; }
        public bool HasProfileUpdate { get; set; }
        public string? Profile { get; set; }
        public bool IsFinalPayloadReady { get; set; }
        public string? FinalContent { get; set; }
    }

    [Authorize]
    [HttpGet("surf-eye")]
    public async Task<IActionResult> SurfEye(CancellationToken cancellationToken)
    {
        var currentPath = HttpContext?.Request?.Path.Value ?? string.Empty;
        ViewData["WineSurferTopBarModel"] = await _topBarService.BuildAsync(User, currentPath, cancellationToken);
        Response.ContentType = "text/html; charset=utf-8";

        var identityName = User?.Identity?.Name;
        var email = User?.FindFirstValue(ClaimTypes.Email) ?? User?.FindFirstValue("email");
        var currentUserId = GetCurrentUserId();

        ApplicationUser? domainUser = null;
        if (currentUserId.HasValue)
        {
            domainUser = await _userRepository.GetByIdAsync(currentUserId.Value, cancellationToken);
        }

        var displayName = ResolveDisplayName(domainUser?.Name, identityName, email);
        var tasteProfileSummary = domainUser?.TasteProfileSummary?.Trim() ?? string.Empty;
        var tasteProfile = domainUser?.TasteProfile?.Trim() ?? string.Empty;

        ViewData["SurfEyeMaxUploadBytes"] = SurfEyeMaxUploadBytes;

        await SetInventoryAddModalViewDataAsync(currentUserId, cancellationToken);

        var viewModel = new WineSurferSurfEyeViewModel(
            displayName,
            tasteProfileSummary,
            tasteProfile,
            !string.IsNullOrWhiteSpace(tasteProfile));

        return View("SurfEye", viewModel);
    }

    [Authorize]
    [HttpPost("surf-eye/analyze")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(SurfEyeMaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = SurfEyeMaxUploadBytes)]
    public async Task<IActionResult> AnalyzeSurfEye([FromForm] SurfEyeAnalysisRequest request, CancellationToken cancellationToken)
    {
        if (request?.Photo is null || request.Photo.Length == 0)
        {
            return BadRequest(new SurfEyeAnalysisError("Capture a photo before asking Surf Eye to analyze it."));
        }

        if (request.Photo.Length > SurfEyeMaxUploadBytes)
        {
            return BadRequest(new SurfEyeAnalysisError("Images must be 8 MB or smaller."));
        }

        var contentType = request.Photo.ContentType;
        if (!string.IsNullOrWhiteSpace(contentType) && !SurfEyeSupportedContentTypes.Contains(contentType))
        {
            return BadRequest(new SurfEyeAnalysisError("Please use a JPEG, PNG, or WEBP photo."));
        }

        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        var user = await _userRepository.GetByIdAsync(currentUserId.Value, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var tasteProfile = user.TasteProfile?.Trim();
        if (string.IsNullOrWhiteSpace(tasteProfile))
        {
            return BadRequest(new SurfEyeAnalysisError("Add a taste profile before using Surf Eye."));
        }

        var normalizedTasteProfile = tasteProfile!;
        var tasteProfileSummary = user.TasteProfileSummary?.Trim();

        byte[] imageBytes;
        await using (var stream = new MemoryStream())
        {
            await request.Photo.CopyToAsync(stream, cancellationToken);
            if (stream.Length == 0)
            {
                return BadRequest(new SurfEyeAnalysisError("We couldn't read that photo. Please try again."));
            }

            imageBytes = stream.ToArray();
        }

        var normalizedContentType = string.IsNullOrWhiteSpace(contentType)
            ? "image/jpeg"
            : contentType!;

        var prompt = BuildSurfEyePrompt(tasteProfileSummary, normalizedTasteProfile);

        ChatCompletion completion;
        try
        {
            completion = await _chatGptService.GetChatCompletionAsync(
                new ChatMessage[]
                {
                    new SystemChatMessage(SurfEyeSystemPrompt),
                    new UserChatMessage(new[]
                    {
                        ChatMessageContentPart.CreateTextPart(prompt),
                        ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(imageBytes), normalizedContentType, ChatImageDetailLevel.High)
                    })
                },
                ct: cancellationToken);
        }
        catch (ClientResultException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new SurfEyeAnalysisError("We couldn't reach the Surf Eye analyst. Please try again."));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new SurfEyeAnalysisError("We couldn't analyze that photo right now. Please try again."));
        }

        var content = ExtractCompletionText(completion);
        if (!TryParseSurfEyeAnalysis(content, out var parsedResult))
        {
            return StatusCode(StatusCodes.Status502BadGateway, new SurfEyeAnalysisError("We couldn't understand Surf Eye's response. Please try again."));
        }

        var orderedMatches = parsedResult!.Wines
            .OrderByDescending(match => match.AlignmentScore)
            .ThenByDescending(match => match.Confidence)
            .ThenBy(match => match.Name, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        var summary = string.IsNullOrWhiteSpace(parsedResult.Summary)
            ? (orderedMatches.Count > 0
                ? "Surf Eye spotted the following wines."
                : "Surf Eye couldn't recognize any wines in this photo.")
            : parsedResult.Summary.Trim();

        var persistedMatches = await PersistSurfEyeMatchesAsync(orderedMatches, cancellationToken);
        var response = new SurfEyeAnalysisResponse(summary, persistedMatches);
        return Json(response);
    }

    [Authorize]
    [HttpGet("terroir")]
    public async Task<IActionResult> ManageTerroir(CancellationToken cancellationToken)
    {
        var currentPath = HttpContext?.Request?.Path.Value ?? string.Empty;
        ViewData["WineSurferTopBarModel"] = await _topBarService.BuildAsync(User, currentPath, cancellationToken);
        Response.ContentType = "text/html; charset=utf-8";

        var now = DateTime.UtcNow;
        WineSurferCurrentUser? currentUser = null;
        IReadOnlyList<WineSurferIncomingSisterhoodInvitation> incomingInvitations = Array.Empty<WineSurferIncomingSisterhoodInvitation>();
        IReadOnlyList<WineSurferSentInvitationNotification> sentInvitationNotifications = Array.Empty<WineSurferSentInvitationNotification>();
        Guid? currentUserId = null;
        string? normalizedEmail = null;
        ApplicationUser? domainUser = null;
        var isAdmin = false;

        if (User?.Identity?.IsAuthenticated == true)
        {
            var identityName = User.Identity?.Name;
            var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
            normalizedEmail = NormalizeEmailCandidate(email);
            currentUserId = GetCurrentUserId();

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

            if (!string.IsNullOrWhiteSpace(displayName) || !string.IsNullOrWhiteSpace(email) || domainUser is not null)
            {
                currentUser = new WineSurferCurrentUser(
                    domainUser?.Id,
                    displayName ?? email ?? string.Empty,
                    email,
                    domainUser?.TasteProfileSummary,
                    domainUser?.TasteProfile,
                    isAdmin);
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

        if (!isAdmin)
        {
            return Forbid();
        }

        if (currentUserId.HasValue)
        {
            var acceptedInvitations = await _sisterhoodInvitationRepository.GetAcceptedForAdminAsync(
                currentUserId.Value,
                now - SentInvitationNotificationWindow,
                cancellationToken);

            sentInvitationNotifications = CreateSentInvitationNotifications(acceptedInvitations);
        }

        var statusMessage = TempData.ContainsKey("StatusMessage")
            ? TempData["StatusMessage"] as string
            : null;
        var errorMessage = TempData.ContainsKey("ErrorMessage")
            ? TempData["ErrorMessage"] as string
            : null;

        var viewModel = await BuildTerroirManagementViewModel(
            currentUser,
            incomingInvitations,
            sentInvitationNotifications,
            statusMessage,
            errorMessage,
            cancellationToken);

        return View("ManageTerroir", viewModel);
    }

    [Authorize]
    [HttpPost("countries")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCountry([FromForm] CreateCountryRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            TempData["ErrorMessage"] = "Country name is required.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var trimmedName = request.Name.Trim();
        var duplicate = await _countryRepository.FindByNameAsync(trimmedName, cancellationToken);
        if (duplicate is not null)
        {
            TempData["ErrorMessage"] = $"Country '{trimmedName}' already exists.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var country = new Country
        {
            Id = Guid.NewGuid(),
            Name = trimmedName
        };

        await _countryRepository.AddAsync(country, cancellationToken);

        TempData["StatusMessage"] = $"Country '{country.Name}' was created.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("countries/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCountry(Guid id, [FromForm] UpdateCountryRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var existing = await _countryRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            TempData["ErrorMessage"] = "Country could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            TempData["ErrorMessage"] = "Country name is required.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var trimmedName = request.Name.Trim();
        var duplicate = await _countryRepository.FindByNameAsync(trimmedName, cancellationToken);
        if (duplicate is not null && duplicate.Id != id)
        {
            TempData["ErrorMessage"] = $"Country '{trimmedName}' already exists.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        existing.Name = trimmedName;

        await _countryRepository.UpdateAsync(existing, cancellationToken);

        TempData["StatusMessage"] = $"Country '{existing.Name}' was updated.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("countries/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCountry(Guid id, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var existing = await _countryRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            TempData["ErrorMessage"] = "Country could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (await _regionRepository.AnyForCountryAsync(id, cancellationToken))
        {
            TempData["ErrorMessage"] = $"Remove regions for '{existing.Name}' before deleting the country.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        await _countryRepository.DeleteAsync(id, cancellationToken);

        TempData["StatusMessage"] = $"Country '{existing.Name}' was deleted.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("regions")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRegion([FromForm] CreateRegionRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            TempData["ErrorMessage"] = "Region name is required.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (request.CountryId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Please select a country.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var trimmedName = request.Name.Trim();
        var country = await _countryRepository.GetByIdAsync(request.CountryId, cancellationToken);
        if (country is null)
        {
            TempData["ErrorMessage"] = "Selected country could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var duplicate = await _regionRepository.FindByNameAndCountryAsync(trimmedName, country.Id, cancellationToken);
        if (duplicate is not null)
        {
            TempData["ErrorMessage"] = $"Region '{trimmedName}' already exists for {country.Name}.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var region = new Region
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            CountryId = country.Id
        };

        await _regionRepository.AddAsync(region, cancellationToken);

        TempData["StatusMessage"] = $"Region '{region.Name}' was created.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("regions/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRegion(Guid id, [FromForm] UpdateRegionRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var existing = await _regionRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            TempData["ErrorMessage"] = "Region could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            TempData["ErrorMessage"] = "Region name is required.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (request.CountryId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Please select a country.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var trimmedName = request.Name.Trim();
        var country = await _countryRepository.GetByIdAsync(request.CountryId, cancellationToken);
        if (country is null)
        {
            TempData["ErrorMessage"] = "Selected country could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var duplicate = await _regionRepository.FindByNameAndCountryAsync(trimmedName, country.Id, cancellationToken);
        if (duplicate is not null && duplicate.Id != id)
        {
            TempData["ErrorMessage"] = $"Region '{trimmedName}' already exists for {country.Name}.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        existing.Name = trimmedName;
        existing.CountryId = country.Id;

        await _regionRepository.UpdateAsync(existing, cancellationToken);

        TempData["StatusMessage"] = $"Region '{existing.Name}' was updated.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("regions/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRegion(Guid id, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var existing = await _regionRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            TempData["ErrorMessage"] = "Region could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (await _appellationRepository.AnyForRegionAsync(id, cancellationToken))
        {
            TempData["ErrorMessage"] = $"Remove appellations for '{existing.Name}' before deleting the region.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        await _regionRepository.DeleteAsync(id, cancellationToken);

        TempData["StatusMessage"] = $"Region '{existing.Name}' was deleted.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("appellations")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAppellation([FromForm] CreateAppellationRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            TempData["ErrorMessage"] = "Appellation name is required.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (request.RegionId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Please select a region.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var region = await _regionRepository.GetByIdAsync(request.RegionId, cancellationToken);
        if (region is null)
        {
            TempData["ErrorMessage"] = "Selected region could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var trimmedName = request.Name.Trim();
        var duplicate = await _appellationRepository.FindByNameAndRegionAsync(trimmedName, region.Id, cancellationToken);
        if (duplicate is not null)
        {
            TempData["ErrorMessage"] = $"Appellation '{trimmedName}' already exists in {region.Name}.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var appellation = new Appellation
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            RegionId = region.Id
        };

        await _appellationRepository.AddAsync(appellation, cancellationToken);

        TempData["StatusMessage"] = $"Appellation '{appellation.Name}' was created.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("appellations/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateAppellation(Guid id, [FromForm] UpdateAppellationRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var existing = await _appellationRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            TempData["ErrorMessage"] = "Appellation could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            TempData["ErrorMessage"] = "Appellation name is required.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (request.RegionId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Please select a region.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var region = await _regionRepository.GetByIdAsync(request.RegionId, cancellationToken);
        if (region is null)
        {
            TempData["ErrorMessage"] = "Selected region could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var trimmedName = request.Name.Trim();
        var duplicate = await _appellationRepository.FindByNameAndRegionAsync(trimmedName, region.Id, cancellationToken);
        if (duplicate is not null && duplicate.Id != id)
        {
            TempData["ErrorMessage"] = $"Appellation '{trimmedName}' already exists in {region.Name}.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        existing.Name = trimmedName;
        existing.RegionId = region.Id;

        await _appellationRepository.UpdateAsync(existing, cancellationToken);

        TempData["StatusMessage"] = $"Appellation '{existing.Name}' was updated.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("appellations/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAppellation(Guid id, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var existing = await _appellationRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            TempData["ErrorMessage"] = "Appellation could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (await _subAppellationRepository.AnyForAppellationAsync(id, cancellationToken))
        {
            TempData["ErrorMessage"] = $"Remove sub-appellations for '{existing.Name}' before deleting the appellation.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        await _appellationRepository.DeleteAsync(id, cancellationToken);

        TempData["StatusMessage"] = $"Appellation '{existing.Name}' was deleted.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("sub-appellations")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSubAppellation([FromForm] CreateSubAppellationRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        if (request.AppellationId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Please select an appellation.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var appellation = await _appellationRepository.GetByIdAsync(request.AppellationId, cancellationToken);
        if (appellation is null)
        {
            TempData["ErrorMessage"] = "Selected appellation could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var appellationName = string.IsNullOrWhiteSpace(appellation.Name) ? "the selected appellation" : appellation.Name.Trim();
        var trimmedName = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
        var lookupName = trimmedName ?? string.Empty;
        var duplicate = await _subAppellationRepository.FindByNameAndAppellationAsync(lookupName, appellation.Id, cancellationToken);
        if (duplicate is not null)
        {
            var duplicateLabel = string.IsNullOrWhiteSpace(trimmedName) ? "Unknown sub-appellation" : trimmedName;
            TempData["ErrorMessage"] = $"Sub-appellation '{duplicateLabel}' already exists in {appellationName}.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var subAppellation = new SubAppellation
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            AppellationId = appellation.Id
        };

        await _subAppellationRepository.AddAsync(subAppellation, cancellationToken);

        var displayName = string.IsNullOrWhiteSpace(trimmedName) ? "Unknown sub-appellation" : trimmedName;
        TempData["StatusMessage"] = $"Sub-appellation '{displayName}' was created.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("sub-appellations/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSubAppellation(Guid id, [FromForm] UpdateSubAppellationRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var existing = await _subAppellationRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            TempData["ErrorMessage"] = "Sub-appellation could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (request.AppellationId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Please select an appellation.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var appellation = await _appellationRepository.GetByIdAsync(request.AppellationId, cancellationToken);
        if (appellation is null)
        {
            TempData["ErrorMessage"] = "Selected appellation could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var appellationName = string.IsNullOrWhiteSpace(appellation.Name) ? "the selected appellation" : appellation.Name.Trim();
        var trimmedName = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
        var lookupName = trimmedName ?? string.Empty;
        var duplicate = await _subAppellationRepository.FindByNameAndAppellationAsync(lookupName, appellation.Id, cancellationToken);
        if (duplicate is not null && duplicate.Id != id)
        {
            var duplicateLabel = string.IsNullOrWhiteSpace(trimmedName) ? "Unknown sub-appellation" : trimmedName;
            TempData["ErrorMessage"] = $"Sub-appellation '{duplicateLabel}' already exists in {appellationName}.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        existing.Name = trimmedName;
        existing.AppellationId = appellation.Id;

        await _subAppellationRepository.UpdateAsync(existing, cancellationToken);

        var displayName = string.IsNullOrWhiteSpace(trimmedName) ? "Unknown sub-appellation" : trimmedName;
        TempData["StatusMessage"] = $"Sub-appellation '{displayName}' was updated.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("sub-appellations/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSubAppellation(Guid id, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var existing = await _subAppellationRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            TempData["ErrorMessage"] = "Sub-appellation could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (await _wineRepository.AnyBySubAppellationAsync(id, cancellationToken))
        {
            var displayName = string.IsNullOrWhiteSpace(existing.Name) ? "Unknown sub-appellation" : existing.Name.Trim();
            TempData["ErrorMessage"] = $"Remove wines for '{displayName}' before deleting the sub-appellation.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        await _subAppellationRepository.DeleteAsync(id, cancellationToken);

        var deletedName = string.IsNullOrWhiteSpace(existing.Name) ? "Unknown sub-appellation" : existing.Name.Trim();
        TempData["StatusMessage"] = $"Sub-appellation '{deletedName}' was deleted.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("wines")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateWine([FromForm] CreateWineRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            TempData["ErrorMessage"] = "Wine name is required.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (request.SubAppellationId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Please select a sub-appellation.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var subAppellation = await _subAppellationRepository.GetByIdAsync(request.SubAppellationId, cancellationToken);
        if (subAppellation is null)
        {
            TempData["ErrorMessage"] = "Selected sub-appellation could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var trimmedName = request.Name.Trim();
        var duplicate = await _wineRepository.FindByNameAsync(trimmedName, subAppellation.Name, subAppellation.Appellation?.Name, cancellationToken);
        if (duplicate is not null)
        {
            TempData["ErrorMessage"] = $"Wine '{trimmedName}' already exists for the selected sub-appellation.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var grapeVariety = string.IsNullOrWhiteSpace(request.GrapeVariety) ? string.Empty : request.GrapeVariety.Trim();

        var wine = new Wine
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            GrapeVariety = grapeVariety,
            Color = request.Color,
            SubAppellationId = subAppellation.Id
        };

        await _wineRepository.AddAsync(wine, cancellationToken);

        TempData["StatusMessage"] = $"Wine '{wine.Name}' was created.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("wines/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateWine(Guid id, [FromForm] UpdateWineRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var existing = await _wineRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            TempData["ErrorMessage"] = "Wine could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            TempData["ErrorMessage"] = "Wine name is required.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (request.SubAppellationId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Please select a sub-appellation.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var subAppellation = await _subAppellationRepository.GetByIdAsync(request.SubAppellationId, cancellationToken);
        if (subAppellation is null)
        {
            TempData["ErrorMessage"] = "Selected sub-appellation could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var trimmedName = request.Name.Trim();
        var duplicate = await _wineRepository.FindByNameAsync(trimmedName, subAppellation.Name, subAppellation.Appellation?.Name, cancellationToken);
        if (duplicate is not null && duplicate.Id != id)
        {
            TempData["ErrorMessage"] = $"Wine '{trimmedName}' already exists for the selected sub-appellation.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var grapeVariety = string.IsNullOrWhiteSpace(request.GrapeVariety) ? string.Empty : request.GrapeVariety.Trim();

        existing.Name = trimmedName;
        existing.GrapeVariety = grapeVariety;
        existing.Color = request.Color;
        existing.SubAppellationId = subAppellation.Id;

        await _wineRepository.UpdateAsync(existing, cancellationToken);

        TempData["StatusMessage"] = $"Wine '{existing.Name}' was updated.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("wines/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteWine(Guid id, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var existing = await _wineRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            TempData["ErrorMessage"] = "Wine could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        await _wineRepository.DeleteAsync(id, cancellationToken);

        TempData["StatusMessage"] = $"Wine '{existing.Name}' was deleted.";
        return RedirectToAction(nameof(ManageTerroir));
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

        var prompt = BuildSipSessionFoodSuggestionPrompt(session);
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
                    new SystemChatMessage(SipSessionFoodSuggestionSystemPrompt),
                    new UserChatMessage(prompt)
                },

                ct: cancellationToken);
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

        var completionText = ExtractCompletionText(completion);
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

        if (User?.Identity?.IsAuthenticated == true)
        {
            var identityName = User.Identity?.Name;
            var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
            var normalizedEmail = NormalizeEmailCandidate(email);
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

            var displayName = ResolveDisplayName(domainUser?.Name, identityName, email);

            if (domainUser is not null)
            {
                currentUserId = domainUser.Id;
                normalizedEmail ??= NormalizeEmailCandidate(domainUser.Email);
            }

            if (string.IsNullOrWhiteSpace(normalizedEmail) && LooksLikeEmail(displayName))
            {
                normalizedEmail = NormalizeEmailCandidate(displayName);
            }

            if (!string.IsNullOrWhiteSpace(displayName) || !string.IsNullOrWhiteSpace(email) || domainUser is not null)
            {
                currentUser = new WineSurferCurrentUser(
                    domainUser?.Id,
                    displayName ?? email ?? string.Empty,
                    email,
                    domainUser?.TasteProfileSummary,
                    domainUser?.TasteProfile,
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

            sentInvitationNotifications = CreateSentInvitationNotifications(acceptedInvitations);
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
        var normalizedEmail = NormalizeEmailCandidate(email);
        var currentUserId = GetCurrentUserId();
        ApplicationUser? domainUser = null;

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
        }

        if (string.IsNullOrWhiteSpace(normalizedEmail) && LooksLikeEmail(displayName))
        {
            normalizedEmail = NormalizeEmailCandidate(displayName);
        }

        if (!string.IsNullOrWhiteSpace(displayName) || !string.IsNullOrWhiteSpace(email) || domainUser is not null)
        {
            currentUser = new WineSurferCurrentUser(
                domainUser?.Id,
                displayName ?? email ?? string.Empty,
                email,
                domainUser?.TasteProfileSummary,
                domainUser?.TasteProfile,
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

            sentInvitationNotifications = CreateSentInvitationNotifications(acceptedInvitations);
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
            var normalizedEmail = NormalizeEmailCandidate(email);

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

            displayName = ResolveDisplayName(domainUser?.Name, identityName, email);

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

            if (currentUserId.HasValue)
            {
                var membership = await _sisterhoodRepository.GetForUserAsync(currentUserId.Value, cancellationToken);
                var availableBottleEntities = await _bottleRepository.GetAvailableForUserAsync(currentUserId.Value, cancellationToken);
                availableBottles = CreateBottleSummaries(availableBottleEntities, currentUserId);
                var acceptedInvitations = await _sisterhoodInvitationRepository.GetAcceptedForAdminAsync(
                    currentUserId.Value,
                    now - SentInvitationNotificationWindow,
                    cancellationToken);

                sentInvitationNotifications = CreateSentInvitationNotifications(acceptedInvitations);
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
            .Select(u => new WineSurferUserSummary(
                u.Id,
                u.Name,
                u.Email ?? string.Empty,
                u.TasteProfileSummary ?? string.Empty,
                u.TasteProfile ?? string.Empty))
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
            var registerRouteValues = new { email = inviteeEmail };
            var signupLink = Url.ActionLink("Register", "Account", registerRouteValues)
                ?? Url.Action("Register", "Account", registerRouteValues)
                ?? "/account/register";
            signupLink = EnsureEmailQueryParameter(signupLink, inviteeEmail);
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

    private async Task<bool> IsCurrentUserAdminAsync(CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return false;
        }

        var user = await _userRepository.GetByIdAsync(currentUserId.Value, cancellationToken);
        return user?.IsAdmin == true;
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

    private async Task<WineSurferTerroirManagementViewModel> BuildTerroirManagementViewModel(
        WineSurferCurrentUser? currentUser,
        IReadOnlyList<WineSurferIncomingSisterhoodInvitation> incomingInvitations,
        IReadOnlyList<WineSurferSentInvitationNotification> sentInvitationNotifications,
        string? statusMessage,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var countries = await _countryRepository.GetAllAsync(cancellationToken);
        var regions = await _regionRepository.GetAllAsync(cancellationToken);
        var appellations = await _appellationRepository.GetAllAsync(cancellationToken);
        var subAppellations = await _subAppellationRepository.GetAllAsync(cancellationToken);
        var wines = await _wineRepository.GetAllAsync(cancellationToken);

        var regionCountByCountry = regions
            .GroupBy(region => region.CountryId)
            .ToDictionary(group => group.Key, group => group.Count());

        var appellationCountByCountry = appellations
            .Where(appellation => appellation.Region is not null)
            .GroupBy(appellation => appellation.Region!.CountryId)
            .ToDictionary(group => group.Key, group => group.Count());

        var subAppellationCountByCountry = subAppellations
            .Where(sub => sub.Appellation?.Region is not null)
            .GroupBy(sub => sub.Appellation!.Region!.CountryId)
            .ToDictionary(group => group.Key, group => group.Count());

        var wineCountByCountry = wines
            .Where(wine => wine.SubAppellation?.Appellation?.Region is not null)
            .GroupBy(wine => wine.SubAppellation!.Appellation!.Region!.CountryId)
            .ToDictionary(group => group.Key, group => group.Count());

        var countryModels = countries
            .Select(country =>
            {
                regionCountByCountry.TryGetValue(country.Id, out var regionCount);
                appellationCountByCountry.TryGetValue(country.Id, out var appellationCount);
                subAppellationCountByCountry.TryGetValue(country.Id, out var subAppellationCount);
                wineCountByCountry.TryGetValue(country.Id, out var wineCount);

                return new WineSurferTerroirCountry(
                    country.Id,
                    country.Name,
                    regionCount,
                    appellationCount,
                    subAppellationCount,
                    wineCount);
            })
            .OrderBy(country => country.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var appellationCountByRegion = appellations
            .GroupBy(appellation => appellation.RegionId)
            .ToDictionary(group => group.Key, group => group.Count());

        var subAppellationCountByRegion = subAppellations
            .Where(sub => sub.Appellation?.Region is not null)
            .GroupBy(sub => sub.Appellation!.RegionId)
            .ToDictionary(group => group.Key, group => group.Count());

        var wineCountByRegion = wines
            .Where(wine => wine.SubAppellation?.Appellation?.Region is not null)
            .GroupBy(wine => wine.SubAppellation!.Appellation!.RegionId)
            .ToDictionary(group => group.Key, group => group.Count());

        var subAppellationCountByAppellation = subAppellations
            .GroupBy(sub => sub.AppellationId)
            .ToDictionary(group => group.Key, group => group.Count());

        var wineCountByAppellation = wines
            .Where(wine => wine.SubAppellation?.Appellation is not null)
            .GroupBy(wine => wine.SubAppellation!.AppellationId)
            .ToDictionary(group => group.Key, group => group.Count());

        var wineCountBySubAppellation = wines
            .GroupBy(wine => wine.SubAppellationId)
            .ToDictionary(group => group.Key, group => group.Count());

        var regionModels = regions
            .Select(region =>
            {
                appellationCountByRegion.TryGetValue(region.Id, out var appellationCount);
                subAppellationCountByRegion.TryGetValue(region.Id, out var subAppellationCount);
                wineCountByRegion.TryGetValue(region.Id, out var wineCount);
                var countryName = region.Country?.Name;
                return new WineSurferTerroirRegion(
                    region.Id,
                    region.Name,
                    region.CountryId,
                    countryName,
                    appellationCount,
                    subAppellationCount,
                    wineCount);
            })
            .OrderBy(region => region.CountryName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(region => region.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var appellationModels = appellations
            .Select(appellation =>
            {
                subAppellationCountByAppellation.TryGetValue(appellation.Id, out var subCount);
                wineCountByAppellation.TryGetValue(appellation.Id, out var wineCount);
                var regionName = appellation.Region?.Name ?? "Unknown region";
                var countryName = appellation.Region?.Country?.Name;
                return new WineSurferTerroirAppellation(
                    appellation.Id,
                    appellation.Name,
                    appellation.RegionId,
                    regionName,
                    countryName,
                    subCount,
                    wineCount);
            })
            .OrderBy(appellation => appellation.RegionName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(appellation => appellation.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var subAppellationModels = subAppellations
            .Select(sub =>
            {
                wineCountBySubAppellation.TryGetValue(sub.Id, out var wineCount);
                var appellationName = sub.Appellation?.Name ?? "Unknown appellation";
                var regionName = sub.Appellation?.Region?.Name ?? "Unknown region";
                var countryName = sub.Appellation?.Region?.Country?.Name;
                return new WineSurferTerroirSubAppellation(
                    sub.Id,
                    sub.Name,
                    sub.AppellationId,
                    appellationName,
                    regionName,
                    countryName,
                    wineCount);
            })
            .OrderBy(sub => sub.RegionName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(sub => sub.AppellationName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(sub => sub.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var wineModels = wines
            .Select(wine =>
            {
                var subName = wine.SubAppellation?.Name ?? "Unknown sub-appellation";
                var appellationName = wine.SubAppellation?.Appellation?.Name ?? "Unknown appellation";
                var regionName = wine.SubAppellation?.Appellation?.Region?.Name ?? "Unknown region";
                var countryName = wine.SubAppellation?.Appellation?.Region?.Country?.Name;
                return new WineSurferTerroirWine(
                    wine.Id,
                    wine.Name,
                    wine.GrapeVariety,
                    wine.Color,
                    wine.SubAppellationId,
                    subName,
                    appellationName,
                    regionName,
                    countryName);
            })
            .OrderBy(wine => wine.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(wine => wine.SubAppellationName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new WineSurferTerroirManagementViewModel(
            currentUser,
            incomingInvitations,
            sentInvitationNotifications,
            countryModels,
            regionModels,
            appellationModels,
            subAppellationModels,
            wineModels,
            statusMessage,
            errorMessage);
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

    public sealed class UpdateTasteProfileRequest
    {
        [StringLength(TasteProfileSummaryMaxLength)]
        public string? TasteProfileSummary { get; set; }

        [StringLength(TasteProfileMaxLength)]
        public string? TasteProfile { get; set; }
    }

    public sealed class CreateCountryRequest
    {
        [Required]
        [StringLength(256, MinimumLength = 2)]
        public string? Name { get; set; }
    }

    public sealed class UpdateCountryRequest
    {
        [Required]
        [StringLength(256, MinimumLength = 2)]
        public string? Name { get; set; }
    }

    public sealed class CreateRegionRequest
    {
        [Required]
        [StringLength(256, MinimumLength = 2)]
        public string? Name { get; set; }

        [Required]
        public Guid CountryId { get; set; }
    }

    public sealed class UpdateRegionRequest
    {
        [Required]
        [StringLength(256, MinimumLength = 2)]
        public string? Name { get; set; }

        [Required]
        public Guid CountryId { get; set; }
    }

    public sealed class CreateAppellationRequest
    {
        [Required]
        [StringLength(256, MinimumLength = 2)]
        public string? Name { get; set; }

        [Required]
        public Guid RegionId { get; set; }
    }

    public sealed class UpdateAppellationRequest
    {
        [Required]
        [StringLength(256, MinimumLength = 2)]
        public string? Name { get; set; }

        [Required]
        public Guid RegionId { get; set; }
    }

    public sealed class CreateSubAppellationRequest
    {
        [StringLength(256)]
        public string? Name { get; set; }

        [Required]
        public Guid AppellationId { get; set; }
    }

    public sealed class UpdateSubAppellationRequest
    {
        [StringLength(256)]
        public string? Name { get; set; }

        [Required]
        public Guid AppellationId { get; set; }
    }

    public sealed class CreateWineRequest
    {
        [Required]
        [StringLength(256, MinimumLength = 2)]
        public string? Name { get; set; }

        [StringLength(256)]
        public string? GrapeVariety { get; set; }

        [Required]
        public Guid SubAppellationId { get; set; }

        [Required]
        public WineColor Color { get; set; }
    }

    public sealed class UpdateWineRequest
    {
        [Required]
        [StringLength(256, MinimumLength = 2)]
        public string? Name { get; set; }

        [StringLength(256)]
        public string? GrapeVariety { get; set; }

        [Required]
        public Guid SubAppellationId { get; set; }

        [Required]
        public WineColor Color { get; set; }
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

    private static string EnsureEmailQueryParameter(string link, string email)
    {
        if (string.IsNullOrWhiteSpace(link) || string.IsNullOrWhiteSpace(email))
        {
            return link;
        }

        var queryIndex = link.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex >= 0)
        {
            var query = link.Substring(queryIndex);
            var parsed = QueryHelpers.ParseQuery(query);
            if (parsed.Any(pair => string.Equals(pair.Key, "email", StringComparison.OrdinalIgnoreCase)))
            {
                return link;
            }
        }

        return QueryHelpers.AddQueryString(link, "email", email);
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

    private static string BuildTasteProfilePrompt(IReadOnlyList<(Bottle Bottle, TastingNote Note)> scoredBottles)
    {
        if (scoredBottles is null || scoredBottles.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Create a cohesive wine taste profile for the user based on the scored bottles listed below.");
        builder.AppendLine("Each entry follows: Name (Vintage) — Origin | Attributes | Score | Notes.");
        builder.AppendLine();

        for (var index = 0; index < scoredBottles.Count; index++)
        {
            var (bottle, note) = scoredBottles[index];
            var wineVintage = bottle?.WineVintage;
            var wine = wineVintage?.Wine;

            var displayName = BuildWineDisplayName(wineVintage, wine);
            var origin = BuildWineOrigin(wine);
            var attributes = BuildWineAttributes(wine);
            var score = note.Score!.Value.ToString("0.##", CultureInfo.InvariantCulture);
            var noteText = PrepareNoteText(note.Note);

            builder.Append(index + 1);
            builder.Append(". ");
            builder.Append(displayName);

            if (!string.IsNullOrEmpty(origin))
            {
                builder.Append(" — ");
                builder.Append(origin);
            }

            if (!string.IsNullOrEmpty(attributes))
            {
                builder.Append(" | ");
                builder.Append(attributes);
            }

            builder.Append(" | Score: ");
            builder.Append(score);

            if (!string.IsNullOrEmpty(noteText))
            {
                builder.Append(" | Notes: ");
                builder.Append(noteText);
            }

            builder.AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("Identify consistent stylistic preferences, texture, structure, and favored regions or grapes.");
        builder.AppendLine("Use only the provided information and avoid recommending specific new bottles.");
        builder.AppendLine("Write the taste profile in the second person.");
        builder.AppendLine("Also include exactly two suggested appellations or sub-appellations that match the palate, providing country, region, appellation, an optional subAppellation (use null when unknown), and a single-sentence reason under 200 characters explaining the fit. Suggest only appellations that are not already in use.");
        builder.AppendLine("For each suggested appellation list two or three representative wines from that location, giving the full label name, color (Red, White, or Rose), an optional variety, an optional subAppellation, and a vintage string that is either a four-digit year or \"NV\".");
        builder.AppendLine("Respond only with JSON: {\"summary\":\"...\",\"profile\":\"...\",\"suggestedAppellations\":[{\"country\":\"...\",\"region\":\"...\",\"appellation\":\"...\",\"subAppellation\":null,\"reason\":\"...\",\"wines\":[{\"name\":\"...\",\"color\":\"Red\",\"variety\":\"...\",\"subAppellation\":null,\"vintage\":\"2019\"}]}]}. No markdown or commentary.");

        return builder.ToString();
    }

    private static string BuildSipSessionFoodSuggestionPrompt(SipSession session)
    {
        if (session is null)
        {
            return string.Empty;
        }

        var bottles = session.Bottles ?? Array.Empty<SipSessionBottle>();
        if (bottles.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var sessionName = string.IsNullOrWhiteSpace(session.Name)
            ? "this sip session"
            : $"the \"{session.Name.Trim()}\" sip session";

        builder.Append("The following wines will be tasted during ");
        builder.Append(sessionName);
        builder.AppendLine(". Recommend three complementary food pairings that guests can enjoy together.");
        builder.AppendLine("Ensure at least one pairing is vegetarian and start that suggestion with \"(Vegetarian)\".");
        builder.AppendLine("Also provide a dedicated cheese course recommendation suited to the lineup.");

        if (!string.IsNullOrWhiteSpace(session.Description))
        {
            builder.Append("Session context: ");
            builder.AppendLine(session.Description.Trim());
            builder.AppendLine();
        }

        var index = 1;
        foreach (var link in bottles)
        {
            var bottle = link?.Bottle;
            var wineVintage = bottle?.WineVintage;
            var wine = wineVintage?.Wine;

            var displayName = BuildWineDisplayName(wineVintage, wine);
            var origin = BuildWineOrigin(wine);
            var attributes = BuildWineAttributes(wine);

            builder.Append(index);
            builder.Append(". ");
            builder.Append(displayName);

            var details = new List<string>();
            if (!string.IsNullOrEmpty(origin))
            {
                details.Add(origin);
            }

            if (!string.IsNullOrEmpty(attributes))
            {
                details.Add(attributes);
            }

            if (bottle?.TastingNotes is { Count: > 0 })
            {
                var highlights = bottle.TastingNotes
                    .Where(note => note is not null)
                    .Select(note => PrepareNoteText(note!.Note))
                    .Where(note => !string.IsNullOrWhiteSpace(note))
                    .Take(2)
                    .ToList();

                if (highlights.Count > 0)
                {
                    details.Add($"Notes: {string.Join(" / ", highlights)}");
                }
            }

            if (details.Count > 0)
            {
                builder.Append(" — ");
                builder.Append(string.Join(" | ", details));
            }

            builder.AppendLine();
            index++;
        }

        builder.AppendLine();
        builder.AppendLine("Suggest dishes that harmonize with the overall lineup and briefly explain why each works.");
        builder.AppendLine("Respond ONLY with JSON shaped as {\"suggestions\":[\"Suggestion 1\",\"Suggestion 2\",\"Suggestion 3\"],\"cheese\":\"Cheese course\"}.");

        return builder.ToString();
    }

    private static string BuildWineDisplayName(WineVintage? wineVintage, Wine? wine)
    {
        var name = wine?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Unknown wine";
        }
        else
        {
            name = name.Trim();
        }

        if (wineVintage is not null && wineVintage.Vintage > 0)
        {
            return $"{name} {wineVintage.Vintage}";
        }

        return name;
    }

    private static string BuildWineOrigin(Wine? wine)
    {
        if (wine?.SubAppellation is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        AddDistinctPart(parts, wine.SubAppellation.Name);
        var appellation = wine.SubAppellation.Appellation;
        if (appellation is not null)
        {
            AddDistinctPart(parts, appellation.Name);
            var region = appellation.Region;
            if (region is not null)
            {
                AddDistinctPart(parts, region.Name);
                AddDistinctPart(parts, region.Country?.Name);
            }
        }

        return parts.Count == 0 ? string.Empty : string.Join(", ", parts);
    }

    private static string BuildWineAttributes(Wine? wine)
    {
        if (wine is null)
        {
            return string.Empty;
        }

        var attributes = new List<string>();
        if (!string.IsNullOrWhiteSpace(wine.GrapeVariety))
        {
            attributes.Add(wine.GrapeVariety.Trim());
        }

        var color = wine.Color switch
        {
            WineColor.Rose => "Rosé",
            WineColor.White => "White",
            WineColor.Red => "Red",
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(color))
        {
            attributes.Add(color);
        }

        return attributes.Count == 0 ? string.Empty : string.Join(" • ", attributes);
    }

    private static string PrepareNoteText(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return string.Empty;
        }

        var normalized = note.ReplaceLineEndings(" ").Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        if (normalized.Length > 240)
        {
            normalized = normalized[..Math.Min(240, normalized.Length)].TrimEnd();
            if (!normalized.EndsWith("…", StringComparison.Ordinal))
            {
                normalized = $"{normalized}…";
            }
        }

        return normalized;
    }

    private static void AddDistinctPart(List<string> parts, string? value)
    {
        if (parts is null || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        if (parts.Exists(existing => string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        parts.Add(trimmed);
    }

    private async Task<IReadOnlyList<WineSurferSuggestedAppellation>> ResolveSuggestedAppellationsAsync(
        Guid userId,
        IReadOnlyList<GeneratedAppellationSuggestion> suggestions,
        CancellationToken cancellationToken)
    {
        if (suggestions is null || suggestions.Count == 0)
        {
            await _suggestedAppellationRepository.ReplaceSuggestionsAsync(
                userId,
                Array.Empty<SuggestedAppellationReplacement>(),
                cancellationToken);
            return Array.Empty<WineSurferSuggestedAppellation>();
        }

        var resolved = new List<WineSurferSuggestedAppellation>(Math.Min(suggestions.Count, 2));
        var replacements = new List<SuggestedAppellationReplacement>(Math.Min(suggestions.Count, 2));
        var seen = new HashSet<Guid>();

        foreach (var suggestion in suggestions.Take(2))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (suggestion is null)
            {
                continue;
            }

            var subAppellation = await ResolveSuggestedSubAppellationAsync(suggestion, cancellationToken);
            if (subAppellation is null)
            {
                continue;
            }

            var appellation = subAppellation.Appellation;
            var region = appellation?.Region;
            var country = region?.Country;

            if (appellation is null || region is null || country is null)
            {
                continue;
            }

            if (!seen.Add(subAppellation.Id))
            {
                continue;
            }

            var normalizedReason = NormalizeSuggestedReason(suggestion.Reason);
            if (string.IsNullOrEmpty(normalizedReason))
            {
                continue;
            }

            var (resolvedWines, wineReplacements) = await ResolveSuggestedWinesAsync(
                suggestion,
                country,
                region,
                appellation,
                subAppellation,
                cancellationToken);

            replacements.Add(new SuggestedAppellationReplacement(
                subAppellation.Id,
                normalizedReason,
                wineReplacements));

            var subName = string.IsNullOrWhiteSpace(subAppellation.Name)
                ? null
                : subAppellation.Name.Trim();

            resolved.Add(new WineSurferSuggestedAppellation(
                subAppellation.Id,
                subName,
                appellation.Id,
                appellation.Name?.Trim() ?? string.Empty,
                region.Name?.Trim() ?? string.Empty,
                country.Name?.Trim() ?? string.Empty,
                normalizedReason,
                resolvedWines));
        }

        await _suggestedAppellationRepository.ReplaceSuggestionsAsync(userId, replacements, cancellationToken);

        return resolved.Count == 0 ? Array.Empty<WineSurferSuggestedAppellation>() : resolved;
    }

    private async Task<SubAppellation?> ResolveSuggestedSubAppellationAsync(
        GeneratedAppellationSuggestion suggestion,
        CancellationToken cancellationToken)
    {
        var countryName = suggestion.Country;
        var regionName = suggestion.Region;
        var appellationName = suggestion.Appellation;

        if (string.IsNullOrWhiteSpace(countryName)
            || string.IsNullOrWhiteSpace(regionName)
            || string.IsNullOrWhiteSpace(appellationName))
        {
            return null;
        }

        var country = await ResolveCountryAsync(countryName.Trim(), cancellationToken);
        if (country is null)
        {
            return null;
        }

        var region = await ResolveRegionAsync(regionName.Trim(), country, cancellationToken);
        if (region is null)
        {
            return null;
        }

        var appellation = await ResolveAppellationAsync(appellationName.Trim(), region, cancellationToken);
        if (appellation is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(suggestion.SubAppellation))
        {
            return await _subAppellationRepository.GetOrCreateBlankAsync(appellation.Id, cancellationToken);
        }

        return await ResolveSubAppellationAsync(suggestion.SubAppellation.Trim(), appellation, cancellationToken);
    }

    private async Task<(IReadOnlyList<WineSurferSuggestedWine> Wines, IReadOnlyList<SuggestedWineReplacement> Replacements)>
        ResolveSuggestedWinesAsync(
            GeneratedAppellationSuggestion suggestion,
            Country country,
            Region region,
            Appellation appellation,
            SubAppellation subAppellation,
            CancellationToken cancellationToken)
    {
        if (suggestion.Wines is null || suggestion.Wines.Count == 0)
        {
            return (Array.Empty<WineSurferSuggestedWine>(), Array.Empty<SuggestedWineReplacement>());
        }

        var resolved = new List<WineSurferSuggestedWine>(Math.Min(suggestion.Wines.Count, 3));
        var replacements = new List<SuggestedWineReplacement>(Math.Min(suggestion.Wines.Count, 3));
        var seen = new HashSet<Guid>();

        var countryName = country.Name?.Trim();
        var regionName = region.Name?.Trim();
        var appellationName = appellation.Name?.Trim();

        foreach (var generatedWine in suggestion.Wines.Take(3))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (generatedWine is null)
            {
                continue;
            }

            var ensured = await EnsureSuggestedWineAsync(
                generatedWine,
                countryName,
                regionName,
                appellationName,
                subAppellation,
                cancellationToken);

            if (ensured.Wine is null)
            {
                continue;
            }

            if (!seen.Add(ensured.Wine.Id))
            {
                continue;
            }

            var displayName = string.IsNullOrWhiteSpace(ensured.Wine.Name)
                ? NormalizeSuggestedWineName(generatedWine.Name)
                : ensured.Wine.Name.Trim();

            if (string.IsNullOrWhiteSpace(displayName))
            {
                continue;
            }

            var normalizedVariety = string.IsNullOrWhiteSpace(ensured.Wine.GrapeVariety)
                ? NormalizeSuggestedWineVariety(generatedWine.Variety)
                : NormalizeSuggestedWineVariety(ensured.Wine.GrapeVariety);

            var subAppellationName = ensured.Wine.SubAppellation?.Name?.Trim();
            if (string.IsNullOrWhiteSpace(subAppellationName))
            {
                subAppellationName = NormalizeSuggestedWineSubAppellation(generatedWine.SubAppellation);
            }

            var normalizedVintage = NormalizeSuggestedWineVintage(ensured.Vintage ?? generatedWine.Vintage);

            replacements.Add(new SuggestedWineReplacement(ensured.Wine.Id, normalizedVintage));

            resolved.Add(new WineSurferSuggestedWine(
                ensured.Wine.Id,
                displayName!,
                ensured.Wine.Color.ToString(),
                normalizedVariety,
                normalizedVintage,
                subAppellationName));
        }

        return (
            resolved.Count == 0 ? Array.Empty<WineSurferSuggestedWine>() : resolved,
            replacements.Count == 0 ? Array.Empty<SuggestedWineReplacement>() : replacements);
    }

    private async Task<(Wine? Wine, string? Vintage)> EnsureSuggestedWineAsync(
        GeneratedSuggestedWine generatedWine,
        string? countryName,
        string? regionName,
        string? appellationName,
        SubAppellation resolvedSubAppellation,
        CancellationToken cancellationToken)
    {
        var name = NormalizeSuggestedWineName(generatedWine.Name);
        var color = NormalizeSuggestedWineColor(generatedWine.Color);

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(color))
        {
            return default;
        }

        var subAppellationName = NormalizeSuggestedWineSubAppellation(
            string.IsNullOrWhiteSpace(generatedWine.SubAppellation)
                ? resolvedSubAppellation.Name
                : generatedWine.SubAppellation);

        var variety = NormalizeSuggestedWineVariety(generatedWine.Variety);
        var vintage = NormalizeSuggestedWineVintage(generatedWine.Vintage);

        try
        {
            var request = new WineCatalogRequest(
                name,
                color,
                countryName,
                regionName,
                appellationName,
                subAppellationName,
                variety);

            var result = await _wineCatalogService.EnsureWineAsync(request, cancellationToken);
            if (result.IsSuccess && result.Wine is not null)
            {
                return (result.Wine, vintage);
            }
        }
        catch
        {
            // Ignore catalog failures and continue with any other candidates.
        }

        return default;
    }

    private static string? NormalizeSuggestedWineName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim();
        if (trimmed.Length <= 256)
        {
            return trimmed;
        }

        return trimmed[..256].TrimEnd();
    }

    private static string? NormalizeSuggestedWineColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return null;
        }

        var trimmed = color.Trim();
        if (trimmed.Length <= 32)
        {
            return trimmed;
        }

        return trimmed[..32].TrimEnd();
    }

    private static string? NormalizeSuggestedWineVariety(string? variety)
    {
        if (string.IsNullOrWhiteSpace(variety))
        {
            return null;
        }

        var trimmed = variety.Trim();
        if (trimmed.Length <= 128)
        {
            return trimmed;
        }

        return trimmed[..128].TrimEnd();
    }

    private static string? NormalizeSuggestedWineSubAppellation(string? subAppellation)
    {
        if (string.IsNullOrWhiteSpace(subAppellation))
        {
            return null;
        }

        var trimmed = subAppellation.Trim();
        if (trimmed.Length <= 256)
        {
            return trimmed;
        }

        return trimmed[..256].TrimEnd();
    }

    private static string? NormalizeSuggestedWineVintage(string? vintage)
    {
        if (string.IsNullOrWhiteSpace(vintage))
        {
            return null;
        }

        var trimmed = vintage.Trim();
        if (trimmed.Length <= 32)
        {
            return trimmed;
        }

        return trimmed[..32].TrimEnd();
    }

    private async Task<Country?> ResolveCountryAsync(string countryName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(countryName))
        {
            return null;
        }

        var trimmed = countryName.Trim();
        var exact = await _countryRepository.FindByNameAsync(trimmed, cancellationToken);
        if (exact is not null)
        {
            return exact;
        }

        var candidates = await _countryRepository.SearchByApproximateNameAsync(trimmed, 5, cancellationToken);
        var best = SelectBestFuzzyMatch(candidates, trimmed, country => country.Name, SuggestedAppellationFuzzyThreshold);
        if (best is not null)
        {
            return best;
        }

        return await _countryRepository.GetOrCreateAsync(trimmed, cancellationToken);
    }

    private async Task<Region?> ResolveRegionAsync(string regionName, Country country, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(regionName))
        {
            return null;
        }

        var trimmed = regionName.Trim();
        var exact = await _regionRepository.FindByNameAndCountryAsync(trimmed, country.Id, cancellationToken);
        if (exact is not null)
        {
            return exact;
        }

        var candidates = await _regionRepository.SearchByApproximateNameAsync(trimmed, 5, cancellationToken);
        var sameCountry = candidates.Where(region => region.CountryId == country.Id).ToList();
        var best = SelectBestFuzzyMatch(sameCountry, trimmed, region => region.Name, SuggestedAppellationFuzzyThreshold);

        if (best is null)
        {
            var fallback = SelectBestFuzzyMatch(candidates, trimmed, region => region.Name, SuggestedAppellationFuzzyThreshold);
            if (fallback is not null && fallback.CountryId == country.Id)
            {
                best = fallback;
            }
        }

        if (best is not null)
        {
            return best;
        }

        return await _regionRepository.GetOrCreateAsync(trimmed, country, cancellationToken);
    }

    private async Task<Appellation?> ResolveAppellationAsync(string appellationName, Region region, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(appellationName))
        {
            return null;
        }

        var trimmed = appellationName.Trim();
        var exact = await _appellationRepository.FindByNameAndRegionAsync(trimmed, region.Id, cancellationToken);
        if (exact is not null)
        {
            return exact;
        }

        var candidates = await _appellationRepository.SearchByApproximateNameAsync(trimmed, region.Id, 5, cancellationToken);
        var best = SelectBestFuzzyMatch(candidates, trimmed, appellation => appellation.Name, SuggestedAppellationFuzzyThreshold);
        if (best is not null)
        {
            return best;
        }

        return await _appellationRepository.GetOrCreateAsync(trimmed, region.Id, cancellationToken);
    }

    private async Task<SubAppellation> ResolveSubAppellationAsync(string subAppellationName, Appellation appellation, CancellationToken cancellationToken)
    {
        var trimmed = subAppellationName.Trim();
        var exact = await _subAppellationRepository.FindByNameAndAppellationAsync(trimmed, appellation.Id, cancellationToken);
        if (exact is not null)
        {
            return exact;
        }

        var candidates = await _subAppellationRepository.SearchByApproximateNameAsync(trimmed, appellation.Id, 5, cancellationToken);
        var best = SelectBestFuzzyMatch(candidates, trimmed, sub => sub.Name ?? string.Empty, SuggestedAppellationFuzzyThreshold);
        if (best is not null)
        {
            return best;
        }

        return await _subAppellationRepository.GetOrCreateAsync(trimmed, appellation.Id, cancellationToken);
    }

    private static T? SelectBestFuzzyMatch<T>(
        IEnumerable<T> candidates,
        string target,
        Func<T, string?> selector,
        double threshold)
    {
        if (candidates is null)
        {
            return default;
        }

        var normalizedTarget = (target ?? string.Empty).Trim();
        if (normalizedTarget.Length == 0)
        {
            return default;
        }

        var bestDistance = double.MaxValue;
        T? bestCandidate = default;

        foreach (var candidate in candidates)
        {
            var value = selector(candidate) ?? string.Empty;
            var distance = FuzzyMatchUtilities.CalculateNormalizedDistance(value, normalizedTarget);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestCandidate = candidate;
            }

            if (distance <= 0)
            {
                break;
            }
        }

        return bestDistance <= threshold ? bestCandidate : default;
    }

    private async Task<IReadOnlyList<WineSurferSuggestedAppellation>> GetSuggestedAppellationsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var suggestions = await _suggestedAppellationRepository.GetForUserAsync(userId, cancellationToken);
        if (suggestions.Count == 0)
        {
            return Array.Empty<WineSurferSuggestedAppellation>();
        }

        var results = new List<WineSurferSuggestedAppellation>(suggestions.Count);
        foreach (var suggestion in suggestions)
        {
            var subAppellation = suggestion.SubAppellation;
            if (subAppellation is null)
            {
                continue;
            }

            var appellation = subAppellation.Appellation;
            var region = appellation?.Region;
            var country = region?.Country;

            if (appellation is null || region is null || country is null)
            {
                continue;
            }

            var subName = string.IsNullOrWhiteSpace(subAppellation.Name)
                ? null
                : subAppellation.Name.Trim();

            var wines = new List<WineSurferSuggestedWine>();
            if (suggestion.SuggestedWines is not null && suggestion.SuggestedWines.Count > 0)
            {
                foreach (var stored in suggestion.SuggestedWines)
                {
                    if (stored?.Wine is null)
                    {
                        continue;
                    }

                    var displayName = NormalizeSuggestedWineName(stored.Wine.Name);
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        continue;
                    }

                    var variety = NormalizeSuggestedWineVariety(stored.Wine.GrapeVariety);
                    var vintage = NormalizeSuggestedWineVintage(stored.Vintage);
                    var storedSubName = stored.Wine.SubAppellation?.Name?.Trim();

                    wines.Add(new WineSurferSuggestedWine(
                        stored.WineId,
                        displayName!,
                        stored.Wine.Color.ToString(),
                        variety,
                        vintage,
                        string.IsNullOrWhiteSpace(storedSubName) ? null : storedSubName));

                    if (wines.Count == 3)
                    {
                        break;
                    }
                }
            }

            var wineResults = wines.Count == 0 ? Array.Empty<WineSurferSuggestedWine>() : wines.ToArray();

            var reason = NormalizeSuggestedReason(suggestion.Reason);

            results.Add(new WineSurferSuggestedAppellation(
                subAppellation.Id,
                subName,
                appellation.Id,
                appellation.Name?.Trim() ?? string.Empty,
                region.Name?.Trim() ?? string.Empty,
                country.Name?.Trim() ?? string.Empty,
                reason,
                wineResults));
        }

        return results.Count == 0 ? Array.Empty<WineSurferSuggestedAppellation>() : results;
    }

    private static string NormalizeGeneratedText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        var truncated = trimmed[..maxLength].TrimEnd();
        return truncated;
    }

    private static string? NormalizeSuggestedReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        var normalized = reason.ReplaceLineEndings(" ").Trim();
        while (normalized.Contains("  ", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        }

        if (normalized.Length > 512)
        {
            normalized = normalized[..512].TrimEnd();
        }

        return normalized;
    }

    private static string BuildSummaryFallback(string profile, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(profile))
        {
            return string.Empty;
        }

        var trimmed = profile.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        var limit = Math.Min(maxLength, trimmed.Length);
        var sentenceEnd = -1;
        for (var index = 0; index < limit; index++)
        {
            var character = trimmed[index];
            if (character == '.' || character == '!' || character == '?')
            {
                sentenceEnd = index;
                break;
            }
        }

        if (sentenceEnd >= 0)
        {
            return trimmed[..(sentenceEnd + 1)].Trim();
        }

        var lastSpace = trimmed.LastIndexOf(' ', limit - 1);
        if (lastSpace > 0)
        {
            return trimmed[..lastSpace].TrimEnd();
        }

        return trimmed[..limit].TrimEnd();
    }

    private static bool TryParseGeneratedTasteProfile(string? content, out GeneratedTasteProfile result)
    {
        result = default!;
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var segment = ExtractJsonSegment(content);

        try
        {
            using var document = JsonDocument.Parse(segment, TasteProfileJsonDocumentOptions);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            string? summary = null;
            if (root.TryGetProperty("summary", out var summaryElement) && summaryElement.ValueKind == JsonValueKind.String)
            {
                summary = summaryElement.GetString();
            }

            if (!root.TryGetProperty("profile", out var profileElement) || profileElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var profile = profileElement.GetString();
            if (string.IsNullOrWhiteSpace(profile))
            {
                return false;
            }

            var suggestions = Array.Empty<GeneratedAppellationSuggestion>();
            if (root.TryGetProperty("suggestedAppellations", out var suggestionsElement)
                && suggestionsElement.ValueKind == JsonValueKind.Array)
            {
                var parsed = new List<GeneratedAppellationSuggestion>();
                foreach (var suggestionElement in suggestionsElement.EnumerateArray())
                {
                    if (suggestionElement.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var country = GetTrimmedJsonString(suggestionElement, "country");
                    var region = GetTrimmedJsonString(suggestionElement, "region");
                    var appellation = GetTrimmedJsonString(suggestionElement, "appellation");
                    var subAppellation = GetTrimmedJsonString(suggestionElement, "subAppellation");
                    var reason = GetTrimmedJsonString(suggestionElement, "reason");

                    if (string.IsNullOrWhiteSpace(country)
                        || string.IsNullOrWhiteSpace(region)
                        || string.IsNullOrWhiteSpace(appellation)
                        || string.IsNullOrWhiteSpace(reason))
                    {
                        continue;
                    }

                    IReadOnlyList<GeneratedSuggestedWine> wines = Array.Empty<GeneratedSuggestedWine>();
                    if (suggestionElement.TryGetProperty("wines", out var winesElement)
                        && winesElement.ValueKind == JsonValueKind.Array)
                    {
                        var parsedWines = new List<GeneratedSuggestedWine>();
                        foreach (var wineElement in winesElement.EnumerateArray())
                        {
                            if (wineElement.ValueKind != JsonValueKind.Object)
                            {
                                continue;
                            }

                            var wineName = GetTrimmedJsonString(wineElement, "name");
                            var wineColor = GetTrimmedJsonString(wineElement, "color");

                            if (string.IsNullOrWhiteSpace(wineName) || string.IsNullOrWhiteSpace(wineColor))
                            {
                                continue;
                            }

                            var wineVariety = GetTrimmedJsonString(wineElement, "variety");
                            var wineSubAppellation = GetTrimmedJsonString(wineElement, "subAppellation");
                            var wineVintage = GetTrimmedJsonString(wineElement, "vintage");

                            parsedWines.Add(new GeneratedSuggestedWine(
                                wineName!,
                                wineColor!,
                                wineVariety,
                                wineSubAppellation,
                                wineVintage));

                            if (parsedWines.Count == 3)
                            {
                                break;
                            }
                        }

                        if (parsedWines.Count > 0)
                        {
                            wines = parsedWines.ToArray();
                        }
                    }

                    parsed.Add(new GeneratedAppellationSuggestion(
                        country!,
                        region!,
                        appellation!,
                        subAppellation,
                        reason!,
                        wines));

                    if (parsed.Count == 2)
                    {
                        break;
                    }
                }

                if (parsed.Count > 0)
                {
                    suggestions = parsed.ToArray();
                }
            }

            result = new GeneratedTasteProfile(summary, profile, suggestions);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? GetTrimmedJsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String =>
                string.IsNullOrWhiteSpace(property.GetString())
                    ? null
                    : property.GetString()!.Trim(),
            JsonValueKind.Null => null,
            _ => null
        };
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

        var segment = ExtractJsonSegment(content);

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

    private static string? ExtractCompletionText(ChatCompletion completion)
    {
        if (completion?.Content is not { Count: > 0 })
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var part in completion.Content)
        {
            if (part.Kind == ChatMessageContentPartKind.Text && !string.IsNullOrWhiteSpace(part.Text))
            {
                builder.Append(part.Text);
            }
        }

        return builder.Length > 0 ? builder.ToString() : null;
    }

    private static string BuildSurfEyePrompt(string? tasteProfileSummary, string tasteProfile)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Analyze the attached photo of wine bottles.");

        if (!string.IsNullOrWhiteSpace(tasteProfileSummary))
        {
            builder.Append("User taste profile summary: ");
            builder.AppendLine(tasteProfileSummary.Trim());
        }

        builder.Append("User taste profile details: ");
        builder.AppendLine(tasteProfile.Trim());
        builder.AppendLine("Identify each distinct wine label that appears in the photo and return at most five wines.");
        builder.AppendLine("Prioritize wines that match the user's taste preferences and explain the ranking succinctly.");

        return builder.ToString();
    }

    private static bool TryParseSurfEyeAnalysis(string? content, out SurfEyeAnalysisIntermediate? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var segment = ExtractJsonSegment(content);

        try
        {
            using var document = JsonDocument.Parse(segment, SurfEyeJsonDocumentOptions);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var summary = TryGetTrimmedString(root, "analysisSummary");
            var wines = new List<SurfEyeWineMatch>();

            if (root.TryGetProperty("wines", out var winesElement) && winesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var wineElement in winesElement.EnumerateArray())
                {
                    if (wineElement.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var name = TryGetTrimmedString(wineElement, "name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var alignmentScore = TryGetDouble(wineElement, "alignmentScore");
                    if (double.IsNaN(alignmentScore))
                    {
                        alignmentScore = 0d;
                    }

                    var confidence = TryGetDouble(wineElement, "confidence");
                    if (double.IsNaN(confidence))
                    {
                        confidence = 0d;
                    }

                    alignmentScore = Math.Clamp(alignmentScore, 0d, 100d);
                    confidence = Math.Clamp(confidence, 0d, 1d);

                    var match = new SurfEyeWineMatch(
                        name!,
                        TryGetTrimmedString(wineElement, "producer"),
                        TryGetTrimmedString(wineElement, "country"),
                        TryGetTrimmedString(wineElement, "region"),
                        TryGetTrimmedString(wineElement, "appellation"),
                        TryGetTrimmedString(wineElement, "subAppellation") ?? TryGetTrimmedString(wineElement, "sub_appellation"),
                        TryGetTrimmedString(wineElement, "variety"),
                        TryGetTrimmedString(wineElement, "color"),
                        TryGetTrimmedString(wineElement, "vintage")
                            ?? TryGetTrimmedString(wineElement, "vintageYear")
                            ?? TryGetTrimmedString(wineElement, "vintage_year")
                            ?? TryGetTrimmedString(wineElement, "year"),
                        alignmentScore,
                        TryGetTrimmedString(wineElement, "alignmentSummary") ?? "",
                        confidence,
                        TryGetTrimmedString(wineElement, "notes"));

                    wines.Add(match);
                }
            }

            result = new SurfEyeAnalysisIntermediate(summary, wines);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<IReadOnlyList<SurfEyeWineMatch>> PersistSurfEyeMatchesAsync(
        IReadOnlyList<SurfEyeWineMatch> matches,
        CancellationToken cancellationToken)
    {
        if (matches is null || matches.Count == 0)
        {
            return matches ?? Array.Empty<SurfEyeWineMatch>();
        }

        var persisted = new List<SurfEyeWineMatch>(matches.Count);

        foreach (var match in matches)
        {
            if (match is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(match.Name)
                || string.IsNullOrWhiteSpace(match.Region)
                || string.IsNullOrWhiteSpace(match.Appellation)
                || string.IsNullOrWhiteSpace(match.Color))
            {
                persisted.Add(match);
                continue;
            }

            try
            {
                var request = new WineCatalogRequest(
                    match.Name,
                    match.Color,
                    match.Country,
                    match.Region,
                    match.Appellation,
                    match.SubAppellation,
                    match.Variety);

                var result = await _wineCatalogService.EnsureWineAsync(request, cancellationToken);
                if (result.IsSuccess && result.Wine is not null)
                {
                    var wine = result.Wine;
                    var subAppellation = wine.SubAppellation;
                    var appellation = subAppellation?.Appellation;
                    var region = appellation?.Region;
                    var country = region?.Country;

                    var updated = match with
                    {
                        WineId = wine.Id,
                        Country = country?.Name ?? match.Country,
                        Region = region?.Name ?? match.Region,
                        Appellation = appellation?.Name ?? match.Appellation,
                        SubAppellation = subAppellation?.Name ?? match.SubAppellation,
                        Color = wine.Color.ToString(),
                        Variety = string.IsNullOrWhiteSpace(match.Variety) && !string.IsNullOrWhiteSpace(wine.GrapeVariety)
                            ? wine.GrapeVariety
                            : match.Variety
                    };

                    persisted.Add(updated);
                }
                else
                {
                    persisted.Add(match);
                }
            }
            catch
            {
                persisted.Add(match);
            }
        }

        return persisted;
    }

    private static string? TryGetTrimmedString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            var value = property.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase)
                ? null
                : trimmed;
        }

        if (property.ValueKind == JsonValueKind.Number)
        {
            if (property.TryGetInt64(out var integer))
            {
                return integer.ToString(CultureInfo.InvariantCulture);
            }

            var raw = property.GetRawText();
            var trimmedRaw = raw.Trim();
            return trimmedRaw.Length == 0 ? null : trimmedRaw;
        }

        return null;
    }

    private static double TryGetDouble(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property))
        {
            if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var number))
            {
                return number;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                var text = property.GetString();
                if (!string.IsNullOrWhiteSpace(text) &&
                    double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }
        }

        return double.NaN;
    }

    private sealed record SurfEyeAnalysisIntermediate(string? Summary, IReadOnlyList<SurfEyeWineMatch> Wines);

    private static string ExtractJsonSegment(string content)
    {
        var trimmed = content.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf('\n');
            var closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && closingFence > firstNewLine)
            {
                trimmed = trimmed.Substring(firstNewLine + 1, closingFence - firstNewLine - 1);
            }
        }

        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace >= firstBrace)
        {
            trimmed = trimmed.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        return trimmed.Trim();
    }

    private sealed record GeneratedTasteProfile(
        string? Summary,
        string Profile,
        IReadOnlyList<GeneratedAppellationSuggestion> Suggestions);

    private sealed record GeneratedAppellationSuggestion(
        string? Country,
        string? Region,
        string? Appellation,
        string? SubAppellation,
        string? Reason,
        IReadOnlyList<GeneratedSuggestedWine> Wines);

    private sealed record GeneratedSuggestedWine(
        string Name,
        string Color,
        string? Variety,
        string? SubAppellation,
        string? Vintage);

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
        if (!string.IsNullOrWhiteSpace(region.Name) && RegionCoordinates.TryGetValue(region.Name, out var regionCoord))
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

        if (!string.IsNullOrWhiteSpace(countryName) && CountryCoordinates.TryGetValue(countryName, out var countryCoord))
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

        if (!string.IsNullOrWhiteSpace(regionName) && RegionCoordinates.TryGetValue(regionName, out var regionCoord))
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

        if (!string.IsNullOrWhiteSpace(countryName) && CountryCoordinates.TryGetValue(countryName, out var countryCoord))
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
    string? ErrorMessage);

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

public record WineSurferTerroirManagementViewModel(
    WineSurferCurrentUser? CurrentUser,
    IReadOnlyList<WineSurferIncomingSisterhoodInvitation> IncomingInvitations,
    IReadOnlyList<WineSurferSentInvitationNotification> SentInvitationNotifications,
    IReadOnlyList<WineSurferTerroirCountry> Countries,
    IReadOnlyList<WineSurferTerroirRegion> Regions,
    IReadOnlyList<WineSurferTerroirAppellation> Appellations,
    IReadOnlyList<WineSurferTerroirSubAppellation> SubAppellations,
    IReadOnlyList<WineSurferTerroirWine> Wines,
    string? StatusMessage,
    string? ErrorMessage);

public record WineSurferTerroirCountry(
    Guid Id,
    string Name,
    int RegionCount,
    int AppellationCount,
    int SubAppellationCount,
    int WineCount);

public record WineSurferTerroirRegion(
    Guid Id,
    string Name,
    Guid CountryId,
    string? CountryName,
    int AppellationCount,
    int SubAppellationCount,
    int WineCount);

public record WineSurferTerroirAppellation(
    Guid Id,
    string Name,
    Guid RegionId,
    string RegionName,
    string? CountryName,
    int SubAppellationCount,
    int WineCount);

public record WineSurferTerroirSubAppellation(
    Guid Id,
    string? Name,
    Guid AppellationId,
    string AppellationName,
    string RegionName,
    string? CountryName,
    int WineCount);

public record WineSurferTerroirWine(
    Guid Id,
    string Name,
    string GrapeVariety,
    WineColor Color,
    Guid SubAppellationId,
    string SubAppellationName,
    string AppellationName,
    string RegionName,
    string? CountryName);

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
