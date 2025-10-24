using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using AllMCPSolution.Models;
using AllMCPSolution.Utilities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;

namespace AllMCPSolution.Controllers;

[Route("sip-session")]
public class SipSessionController : WineSurferControllerBase
{
    private readonly ISipSessionRepository _sipSessionRepository;
    private readonly IBottleRepository _bottleRepository;
    private readonly ITastingNoteRepository _tastingNoteRepository;
    private readonly ISisterhoodInvitationRepository _sisterhoodInvitationRepository;
    private readonly IWineSurferTopBarService _topBarService;
    private readonly IChatGptService _chatGptService;
    private readonly IChatGptPromptService _chatGptPromptService;
    private readonly Services.IBottleSummaryService _bottleSummaryService;

    private static readonly JsonDocumentOptions FoodSuggestionJsonDocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    public SipSessionController(
        ISipSessionRepository sipSessionRepository,
        IBottleRepository bottleRepository,
        ITastingNoteRepository tastingNoteRepository,
        ISisterhoodInvitationRepository sisterhoodInvitationRepository,
        IWineSurferTopBarService topBarService,
        IChatGptService chatGptService,
        IChatGptPromptService chatGptPromptService,
        Services.IBottleSummaryService bottleSummaryService,
        UserManager<ApplicationUser> userManager,
        IUserRepository userRepository) : base(userManager, userRepository)
    {
        _sipSessionRepository = sipSessionRepository;
        _bottleRepository = bottleRepository;
        _tastingNoteRepository = tastingNoteRepository;
        _sisterhoodInvitationRepository = sisterhoodInvitationRepository;
        _topBarService = topBarService;
        _chatGptService = chatGptService;
        _chatGptPromptService = chatGptPromptService;
        _bottleSummaryService = bottleSummaryService;
    }

    // New route: /sip-session/{sipSessionId}
    [HttpGet("{sipSessionId:guid}")]
    // Back-compat old route: /wine-surfer/sessions/{sipSessionId}
    [HttpGet("/wine-surfer/sessions/{sipSessionId:guid}")]
    public async Task<IActionResult> Index(Guid sipSessionId, CancellationToken cancellationToken)
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
        return View("~/Views/SipSession/Index.cshtml", model);
    }

    // New route: /sip-session/{sipSessionId}/suggest-food
    [HttpPost("{sipSessionId:guid}/suggest-food")]
    // Back-compat POST: /wine-surfer/sessions/{sipSessionId}/suggest-food
    [HttpPost("/wine-surfer/sessions/{sipSessionId:guid}/suggest-food")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SuggestFood(Guid sipSessionId, CancellationToken cancellationToken)
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
            return View("~/Views/SipSession/Index.cshtml", emptyModel);
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
            return View("~/Views/SipSession/Index.cshtml", fallbackModel);
        }

        OpenAI.Chat.ChatCompletion completion;
        try
        {
            completion = await _chatGptService.GetChatCompletionAsync(
                new OpenAI.Chat.ChatMessage[]
                {
                    new OpenAI.Chat.SystemChatMessage(_chatGptPromptService.SipSessionFoodSuggestionSystemPrompt),
                    new OpenAI.Chat.UserChatMessage(prompt)
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
            return View("~/Views/SipSession/Index.cshtml", errorModel);
        }
        catch (System.ClientModel.ClientResultException)
        {
            var errorModel = await BuildSipSessionDetailViewModelAsync(
                session,
                cancellationToken,
                Array.Empty<string>(),
                "We couldn't reach the pairing assistant. Please try again.");
            Response.ContentType = "text/html; charset=utf-8";
            return View("~/Views/SipSession/Index.cshtml", errorModel);
        }
        catch (Exception)
        {
            var errorModel = await BuildSipSessionDetailViewModelAsync(
                session,
                cancellationToken,
                Array.Empty<string>(),
                "We couldn't request food pairings right now. Please try again.");
            Response.ContentType = "text/html; charset=utf-8";
            return View("~/Views/SipSession/Index.cshtml", errorModel);
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
            return View("~/Views/SipSession/Index.cshtml", errorModel);
        }

        if (suggestions.Count > 0 || !string.IsNullOrWhiteSpace(cheeseSuggestion))
        {
            var payload = new SipSessionFoodSuggestionPayload(suggestions, cheeseSuggestion);
            var serializedSuggestions = System.Text.Json.JsonSerializer.Serialize(payload);
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
        return View("~/Views/SipSession/Index.cshtml", model);
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

        WineSurferCurrentUser? currentUser = null;
        IReadOnlyList<WineSurferIncomingSisterhoodInvitation> incomingInvitations = Array.Empty<WineSurferIncomingSisterhoodInvitation>();
        IReadOnlyList<WineSurferSentInvitationNotification> sentInvitationNotifications = Array.Empty<WineSurferSentInvitationNotification>();
        IReadOnlyList<WineSurferSipSessionBottle> availableBottles = Array.Empty<WineSurferSipSessionBottle>();
        Guid? currentUserId = GetCurrentUserId();
        string domainUserSummary = string.Empty;
        string domainUserProfile = string.Empty;

        if (User?.Identity?.IsAuthenticated == true)
        {
            var identityName = User.Identity?.Name;
            var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
            var normalizedEmail = StringUtilities.NormalizeEmailCandidate(email);
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
                    .Select(invitation => new WineSurferIncomingSisterhoodInvitation(
                        invitation.Id,
                        invitation.SisterhoodId,
                        invitation.Sisterhood?.Name ?? "Sisterhood",
                        invitation.Sisterhood?.Description,
                        invitation.InviteeEmail,
                        invitation.Status,
                        invitation.CreatedAt,
                        invitation.UpdatedAt,
                        invitation.InviteeUserId,
                        currentUserId.HasValue && invitation.InviteeUserId == currentUserId.Value,
                        normalizedEmail is not null && string.Equals(invitation.InviteeEmail, normalizedEmail, StringComparison.Ordinal)))
                    .ToList();
            }
        }

        bool canManageSession = false;
        if (currentUserId.HasValue)
        {
            var memberships = session.Sisterhood?.Memberships ?? Array.Empty<SisterhoodMembership>();
            canManageSession = memberships.Any(membership => membership.UserId == currentUserId.Value && membership.IsAdmin);
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
            _bottleSummaryService.CreateFromSessionBottles(session.Bottles, currentUserId, sisterhoodAverageScores));

        IReadOnlyList<string>? suggestionCandidates = foodSuggestions;
        var cheeseCandidate = string.IsNullOrWhiteSpace(cheeseSuggestion)
            ? null
            : cheeseSuggestion!.Trim();

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

    private static IReadOnlyList<WineSurferSipSessionBottle> CreateBottleSummaries(
        IEnumerable<SipSessionBottle>? sessionBottles,
        Guid? currentUserId = null,
        IReadOnlyDictionary<Guid, decimal>? sisterhoodAverageScores = null)
    {
        if (sessionBottles is null)
        {
            return Array.Empty<WineSurferSipSessionBottle>();
        }

        var summaries = sessionBottles
            .Where(link => link is not null && link.Bottle is not null)
            .Select(link =>
            {
                var bottle = link.Bottle!;
                var actualIsRevealed = link.IsRevealed;
                var isOwnedByCurrentUser = currentUserId.HasValue &&
                                           bottle.UserId.HasValue &&
                                           bottle.UserId.Value == currentUserId.Value;

                var labelBase = CreateBottleLabel(bottle);
                var rawWineName = bottle.WineVintage?.Wine?.Name;
                var wineName = string.IsNullOrWhiteSpace(rawWineName) ? "Bottle" : rawWineName!.Trim();
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
                    currentUserNote = bottle.TastingNotes?.FirstOrDefault(note => note.UserId == currentUserId.Value);
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

        return summaries.Count == 0 ? Array.Empty<WineSurferSipSessionBottle>() : summaries;
    }

    private static string CreateBottleLabel(Bottle bottle)
    {
        var wineName = bottle.WineVintage?.Wine?.Name;
        var labelBase = string.IsNullOrWhiteSpace(wineName) ? "Bottle" : wineName!.Trim();
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
            using var document = JsonDocument.Parse(segment, FoodSuggestionJsonDocumentOptions);
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
}