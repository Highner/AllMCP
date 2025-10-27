using System;
using System.ClientModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using AllMCPSolution.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenAI.Chat;

namespace AllMCPSolution.Controllers;

[Authorize]
[Route("wine-surfer")]
public class TasteProfileController: WineSurferControllerBase
{
    public sealed class UpdateTasteProfileRequest
    {
        public Guid? TasteProfileId { get; set; }

        [StringLength(TasteProfileSummaryMaxLength)]
        public string? TasteProfileSummary { get; set; }

        [StringLength(TasteProfileMaxLength)]
        public string? TasteProfile { get; set; }

        public decimal? SuggestionBudget { get; set; }
    }
    
    private readonly ISuggestedAppellationService _suggestedAppellationService;
    private readonly ISisterhoodInvitationRepository _sisterhoodInvitationRepository;

    private readonly ICountryRepository _countryRepository;
    private readonly IRegionRepository _regionRepository;
    private readonly IAppellationRepository _appellationRepository;
    private readonly ISubAppellationRepository _subAppellationRepository;
    private readonly IBottleRepository _bottleRepository;
    private readonly IChatGptPromptService _chatGptPromptService;
    private readonly IChatGptService _chatGptService;
    private readonly IWineCatalogService _wineCatalogService;
    private readonly ITasteProfileRepository _tasteProfileRepository;
    private static readonly TimeSpan SentInvitationNotificationWindow = TimeSpan.FromDays(7);


    private readonly IWineSurferTopBarService _topBarService;
    private const double SuggestedAppellationFuzzyThreshold = 0.35;
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
    private const string SuggestionBudgetInvalidErrorMessage = "Suggestion budget must be a number zero or greater.";
    private static readonly Regex MultipleWhitespaceRegex = new(@"\s{2,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EmptyParenthesesRegex = new(@"\(\s*\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EmptyBracketsRegex = new(@"\[\s*\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EmptyBracesRegex = new(@"\{\s*\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SeparatorSpacingRegex = new(@"\s*([,;/\-–—|])\s*", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DuplicateSeparatorRegex = new(@"([,;/\-–—|])\s*([,;/\-–—|])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly char[] WineNameTrimCharacters = { ' ', ',', ';', ':', '/', '-', '–', '—', '|' };
    private static readonly JsonDocumentOptions TasteProfileJsonDocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };
    private static readonly JsonSerializerOptions TasteProfileStreamSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public TasteProfileController(
        IWineRepository wineRepository,
        IUserRepository userRepository,
        ISisterhoodInvitationRepository sisterhoodInvitationRepository,
        ICountryRepository countryRepository,
        IRegionRepository regionRepository,
        IAppellationRepository appellationRepository,
        ISubAppellationRepository subAppellationRepository,
        ITerroirMergeRepository terroirMergeRepository,
        IWineSurferTopBarService topBarService,
        ISuggestedAppellationService suggestedAppellationService,
        IChatGptPromptService chatGptPromptService,
        IBottleRepository bottleRepository,
        IChatGptService chatGptService,
        IWineCatalogService wineCatalogService,
        ITasteProfileRepository tasteProfileRepository,
        UserManager<ApplicationUser> userManager) : base(userManager, userRepository)
    {
        _sisterhoodInvitationRepository = sisterhoodInvitationRepository;
        _countryRepository = countryRepository;
        _regionRepository = regionRepository;
        _appellationRepository = appellationRepository;
        _subAppellationRepository = subAppellationRepository;
        _topBarService = topBarService;
        _suggestedAppellationService = suggestedAppellationService;
        _chatGptPromptService = chatGptPromptService;
        _bottleRepository = bottleRepository;
        _chatGptService = chatGptService;
        _wineCatalogService = wineCatalogService;
        _tasteProfileRepository = tasteProfileRepository;
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
        string domainUserSummary = string.Empty;
        string domainUserProfile = string.Empty;
        var tasteProfileEntities = new List<TasteProfile>();

        if (User?.Identity?.IsAuthenticated == true)
        {
            var identityName = User.Identity?.Name;
            var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
            normalizedEmail = StringUtilities.NormalizeEmailCandidate(email);
            currentUserId = GetCurrentUserId();

            if (currentUserId.HasValue)
            {
                domainUser = await _userRepository.GetByIdAsync(currentUserId.Value, cancellationToken);
            }

            if (domainUser is null && !string.IsNullOrWhiteSpace(identityName))
            {
                domainUser = await _userRepository.FindByNameAsync(identityName, cancellationToken);
            }

            var resolvedUserId = domainUser?.Id ?? currentUserId;

            if (resolvedUserId.HasValue)
            {
                var fetchedProfiles = await _tasteProfileRepository.GetForUserAsync(resolvedUserId.Value, cancellationToken);
                tasteProfileEntities = fetchedProfiles.ToList();

                if (domainUser is not null)
                {
                    domainUser.TasteProfiles = tasteProfileEntities;
                }
            }

            var (resolvedSummary, resolvedProfile) = TasteProfileUtilities.GetActiveTasteProfileTexts(
                domainUser ?? new ApplicationUser { TasteProfiles = tasteProfileEntities });
            domainUserSummary = resolvedSummary;
            domainUserProfile = resolvedProfile;

            var displayName = StringUtilities.ResolveDisplayName(domainUser?.Name, identityName, email);

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

            if (!string.IsNullOrWhiteSpace(displayName) || !string.IsNullOrWhiteSpace(email) || domainUser is not null)
            {
                currentUser = new WineSurferCurrentUser(
                    domainUser?.Id,
                    displayName ?? email ?? string.Empty,
                    email,
                    domainUserSummary,
                    domainUserProfile,
                    domainUser?.SuggestionBudget,
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

            sentInvitationNotifications = NotificationService.CreateSentInvitationNotifications(acceptedInvitations);
        }

        var statusMessage = TempData.ContainsKey(TasteProfileStatusTempDataKey)
            ? TempData[TasteProfileStatusTempDataKey] as string
            : null;
        var errorMessage = TempData.ContainsKey(TasteProfileErrorTempDataKey)
            ? TempData[TasteProfileErrorTempDataKey] as string
            : null;

        var tasteProfileHistory = tasteProfileEntities
            .OrderByDescending(profile => profile.CreatedAt)
            .Select(profile => new WineSurferTasteProfileHistoryEntry(
                profile.Id,
                profile.Summary ?? string.Empty,
                profile.Profile ?? string.Empty,
                DateTime.SpecifyKind(profile.CreatedAt, DateTimeKind.Utc),
                profile.InUse))
            .ToList();

        var activeHistoryEntry = tasteProfileHistory.FirstOrDefault(entry => entry.InUse)
            ?? tasteProfileHistory.FirstOrDefault();

        var tasteProfileSummary = activeHistoryEntry?.Summary
            ?? domainUserSummary
            ?? currentUser?.TasteProfileSummary
            ?? string.Empty;
        var tasteProfile = activeHistoryEntry?.Profile
            ?? domainUserProfile
            ?? currentUser?.TasteProfile
            ?? string.Empty;
        IReadOnlyList<WineSurferSuggestedAppellation> suggestedAppellations = Array.Empty<WineSurferSuggestedAppellation>();

        if (currentUserId.HasValue)
        {
            suggestedAppellations = await _suggestedAppellationService.GetForUserAsync(currentUserId.Value, cancellationToken);
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
            errorMessage,
            activeHistoryEntry?.Id,
            tasteProfileHistory);

        return View("Index", viewModel);
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
            var budgetErrors = ModelState.TryGetValue(nameof(UpdateTasteProfileRequest.SuggestionBudget), out var budgetEntry)
                && budgetEntry.Errors.Count > 0;

            if (budgetErrors)
            {
                TempData[TasteProfileErrorTempDataKey] = SuggestionBudgetInvalidErrorMessage;
                return RedirectToAction(nameof(TasteProfile));
            }

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

        decimal? normalizedBudget = null;

        if (request.SuggestionBudget.HasValue)
        {
            if (request.SuggestionBudget.Value < 0)
            {
                TempData[TasteProfileErrorTempDataKey] = SuggestionBudgetInvalidErrorMessage;
                return RedirectToAction(nameof(TasteProfile));
            }

            normalizedBudget = decimal.Round(Math.Max(request.SuggestionBudget.Value, 0m), 2, MidpointRounding.AwayFromZero);
        }

        try
        {
            var updatedUser = await _userRepository.UpdateTasteProfileAsync(
                userId.Value,
                request.TasteProfileId,
                trimmedTasteProfile,
                trimmedSummary,
                normalizedBudget,
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

        var prompt = _chatGptPromptService.BuildTasteProfilePrompt(scoredBottles);
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
        Guid userId,
        string prompt,
        CancellationToken cancellationToken)
    {
        ChatCompletion completion;
        try
        {
            completion = await _chatGptService.GetChatCompletionAsync(
                new ChatMessage[]
                {
                    new SystemChatMessage(_chatGptPromptService.TasteProfileGenerationSystemPrompt),
                    new UserChatMessage(prompt)
                },
                ct: cancellationToken);
        }
        catch (ChatGptServiceNotConfiguredException)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new GenerateTasteProfileError(TasteProfileAssistantUnavailableErrorMessage));
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
            generatedProfile.Suggestions,
            cancellationToken);

        var response = new GenerateTasteProfileResponse(
            summary,
            profile,
            BuildTasteProfileSuggestions(resolvedSuggestions.Suggestions));

        TasteProfile? savedProfile = null;

        try
        {
            savedProfile = await _tasteProfileRepository.SaveGeneratedProfileAsync(
                userId,
                profile,
                summary,
                resolvedSuggestions.Replacements,
                cancellationToken);

            if (savedProfile is null)
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new GenerateTasteProfileError(TasteProfileGenerationGenericErrorMessage));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new GenerateTasteProfileError(TasteProfileGenerationGenericErrorMessage));
        }

        return Json(response);
    }

    private async Task<IActionResult> StreamTasteProfileGenerationAsync(
        Guid userId,
        string prompt,
        CancellationToken cancellationToken)
    {
        var response = Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = $"{TasteProfileStreamMediaType}; charset=utf-8";
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";

        HttpContext.Features
            .Get<IHttpResponseBodyFeature>()?
            .DisableBuffering();


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
                        new SystemChatMessage(_chatGptPromptService.TasteProfileGenerationSystemPrompt),
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
                generatedProfile.Suggestions,
                cancellationToken);

            var payload = new GenerateTasteProfileResponse(
                summary,
                profile,
                BuildTasteProfileSuggestions(resolvedSuggestions.Suggestions));

            TasteProfile? savedProfile = null;

            try
            {
                savedProfile = await _tasteProfileRepository.SaveGeneratedProfileAsync(
                    userId,
                    profile,
                    summary,
                    resolvedSuggestions.Replacements,
                    cancellationToken);

                if (savedProfile is null)
                {
                    await WriteTasteProfileEventAsync(
                        response,
                        new { type = "error", message = TasteProfileGenerationGenericErrorMessage },
                        cancellationToken);
                    return new EmptyResult();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                await WriteTasteProfileEventAsync(
                    response,
                    new { type = "error", message = TasteProfileGenerationGenericErrorMessage },
                    cancellationToken);
                return new EmptyResult();
            }

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
        catch (ChatGptServiceNotConfiguredException)
        {
            await WriteTasteProfileEventAsync(
                response,
                new { type = "error", message = TasteProfileAssistantUnavailableErrorMessage },
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
                await response.Body.FlushAsync(CancellationToken.None);
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
        await response.WriteAsync(json, cancellationToken);
        await response.WriteAsync("\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
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
    
    private static bool TryParseGeneratedTasteProfile(string? content, out GeneratedTasteProfile result)
    {
        result = default!;
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var segment = StringUtilities.ExtractJsonSegment(content);

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

    private sealed record ResolvedSuggestedAppellations(
        IReadOnlyList<WineSurferSuggestedAppellation> Suggestions,
        IReadOnlyList<SuggestedAppellationReplacement> Replacements);

    private async Task<ResolvedSuggestedAppellations> ResolveSuggestedAppellationsAsync(
        IReadOnlyList<GeneratedAppellationSuggestion> suggestions,
        CancellationToken cancellationToken)
    {
        if (suggestions is null || suggestions.Count == 0)
        {
            return new ResolvedSuggestedAppellations(
                Array.Empty<WineSurferSuggestedAppellation>(),
                Array.Empty<SuggestedAppellationReplacement>());
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

        IReadOnlyList<WineSurferSuggestedAppellation> resolvedResults = resolved.Count == 0
            ? Array.Empty<WineSurferSuggestedAppellation>()
            : resolved;

        IReadOnlyList<SuggestedAppellationReplacement> replacementResults = replacements.Count == 0
            ? Array.Empty<SuggestedAppellationReplacement>()
            : replacements;

        return new ResolvedSuggestedAppellations(resolvedResults, replacementResults);
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

            var normalizedVariety = string.IsNullOrWhiteSpace(ensured.Wine.GrapeVariety)
                ? NormalizeSuggestedWineVariety(generatedWine.Variety)
                : NormalizeSuggestedWineVariety(ensured.Wine.GrapeVariety);

            var subAppellationName = ensured.Wine.SubAppellation?.Name?.Trim();
            if (string.IsNullOrWhiteSpace(subAppellationName))
            {
                subAppellationName = NormalizeSuggestedWineSubAppellation(generatedWine.SubAppellation);
            }

            var normalizedVintage = NormalizeSuggestedWineVintage(ensured.Vintage ?? generatedWine.Vintage);

            var fallbackName = NormalizeSuggestedWineName(
                generatedWine.Name,
                generatedWine.Variety,
                normalizedVariety,
                countryName,
                regionName,
                appellationName,
                subAppellationName,
                generatedWine.SubAppellation,
                subAppellation.Name);

            var displayName = string.IsNullOrWhiteSpace(ensured.Wine.Name)
                ? fallbackName
                : ensured.Wine.Name.Trim();

            if (string.IsNullOrWhiteSpace(displayName))
            {
                continue;
            }

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
        var variety = NormalizeSuggestedWineVariety(generatedWine.Variety);

        var subAppellationName = NormalizeSuggestedWineSubAppellation(
            string.IsNullOrWhiteSpace(generatedWine.SubAppellation)
                ? resolvedSubAppellation.Name
                : generatedWine.SubAppellation);

        var name = NormalizeSuggestedWineName(
            generatedWine.Name,
            generatedWine.Variety,
            variety,
            countryName,
            regionName,
            appellationName,
            subAppellationName,
            generatedWine.SubAppellation,
            resolvedSubAppellation.Name);

        var color = NormalizeSuggestedWineColor(generatedWine.Color);

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(color))
        {
            return default;
        }

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

    private static string? NormalizeSuggestedWineName(string? name, params string?[] descriptorsToStrip)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var sanitized = name.Trim();

        if (descriptorsToStrip is { Length: > 0 })
        {
            foreach (var descriptor in descriptorsToStrip)
            {
                sanitized = RemoveDescriptorFromWineName(sanitized, descriptor);
            }
        }

        sanitized = CleanWineNameSeparators(sanitized);

        if (sanitized.Length > 256)
        {
            sanitized = sanitized[..256].TrimEnd();
        }

        sanitized = sanitized.Trim(WineNameTrimCharacters);
        sanitized = CleanWineNameSeparators(sanitized);

        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    private static string RemoveDescriptorFromWineName(string value, string? descriptor)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(descriptor))
        {
            return value;
        }

        var trimmedDescriptor = descriptor.Trim();
        if (trimmedDescriptor.Length == 0)
        {
            return value;
        }

        var words = trimmedDescriptor.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return value;
        }

        var pattern = string.Join(@"[\s\-/–—]*", words.Select(Regex.Escape));
        return Regex.Replace(
            value,
            $@"(?<!\w){pattern}(?!\w)",
            string.Empty,
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private static string CleanWineNameSeparators(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = EmptyParenthesesRegex.Replace(value, string.Empty);
        cleaned = EmptyBracketsRegex.Replace(cleaned, string.Empty);
        cleaned = EmptyBracesRegex.Replace(cleaned, string.Empty);
        cleaned = SeparatorSpacingRegex.Replace(cleaned, " $1 ");
        cleaned = DuplicateSeparatorRegex.Replace(cleaned, "$1 ");
        cleaned = MultipleWhitespaceRegex.Replace(cleaned, " ");
        return cleaned.Trim(WineNameTrimCharacters).Trim();
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
    
    private sealed class TasteProfileStreamUpdate
    {
        public bool HasSummaryUpdate { get; set; }
        public string? Summary { get; set; }
        public bool HasProfileUpdate { get; set; }
        public string? Profile { get; set; }
        public bool IsFinalPayloadReady { get; set; }
        public string? FinalContent { get; set; }
    }
}