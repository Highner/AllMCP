using System.ClientModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;
using AllMCPSolution.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace AllMCPSolution.Controllers;

[Authorize]
[Route("wine-surfer")]
public class SurfEyeController: WineSurferControllerBase
{
    private readonly IWineSurferTopBarService _topBarService;
    private readonly IBottleLocationRepository _bottleLocationRepository;
    private readonly IWineRepository _wineRepository;
    private readonly IChatGptPromptService _chatGptPromptService;
    private readonly IChatGptService _chatGptService;
    private readonly IOcrService _ocrService;
    private readonly string _surfEyeIdentifyModel;

    private const int SurfEyeMaxUploadBytes = 8 * 1024 * 1024;
    private static readonly string[] SurfEyeSupportedContentTypes = new[] { "image/jpeg", "image/png", "image/webp" };
    private static readonly JsonDocumentOptions SurfEyeJsonDocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };
    
    public SurfEyeController(
        IUserRepository userRepository,
        IBottleLocationRepository bottleLocationRepository,
        IWineRepository wineRepository,
        IWineSurferTopBarService topBarService,
        IChatGptPromptService chatGptPromptService,
        IChatGptService chatGptService,
        IOcrService ocrService,
        IOptions<ChatGptOptions> chatGptOptions,
        UserManager<ApplicationUser> userManager) : base(userManager, userRepository)
    {
        _bottleLocationRepository = bottleLocationRepository;
        _wineRepository = wineRepository;
        _chatGptPromptService = chatGptPromptService;
        _topBarService = topBarService;
        _chatGptService = chatGptService;
        _ocrService = ocrService;
        if (chatGptOptions is null)
        {
            throw new ArgumentNullException(nameof(chatGptOptions));
        }

        var options = chatGptOptions.Value;
        var fallbackModel = !string.IsNullOrWhiteSpace(options?.DefaultModel)
            ? options!.DefaultModel!
            : ChatGptOptions.FallbackModel;

        _surfEyeIdentifyModel = string.IsNullOrWhiteSpace(options?.SmallModel)
            ? fallbackModel
            : options!.SmallModel!;
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

        var displayName = StringUtilities.ResolveDisplayName(domainUser?.Name, identityName, email);
        var (tasteProfileSummary, tasteProfile) = TasteProfileUtilities.GetActiveTasteProfileTexts(domainUser);

        ViewData["SurfEyeMaxUploadBytes"] = SurfEyeMaxUploadBytes;

        await SetInventoryAddModalViewDataAsync(currentUserId, cancellationToken);

        var viewModel = new WineSurferSurfEyeViewModel(
            displayName,
            tasteProfileSummary,
            tasteProfile,
            !string.IsNullOrWhiteSpace(tasteProfile));

        return View("Index", viewModel);
    }

    [Authorize]
    [HttpPost("surf-eye/analyze")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(SurfEyeMaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = SurfEyeMaxUploadBytes)]
    public Task<IActionResult> AnalyzeSurfEye([FromForm] SurfEyeAnalysisRequest request, CancellationToken cancellationToken) =>
        AnalyzeSurfEyeInternalAsync(request, allowTasteProfile: true, modelOverride: null, cancellationToken);

    [Authorize]
    [HttpPost("surf-eye/identify")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(SurfEyeMaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = SurfEyeMaxUploadBytes)]
    public Task<IActionResult> IdentifySurfEye([FromForm] SurfEyeAnalysisRequest request, CancellationToken cancellationToken) =>
        AnalyzeSurfEyeInternalAsync(request, allowTasteProfile: false, modelOverride: _surfEyeIdentifyModel, cancellationToken);

    private async Task<IActionResult> AnalyzeSurfEyeInternalAsync(
        SurfEyeAnalysisRequest request,
        bool allowTasteProfile,
        string? modelOverride,
        CancellationToken cancellationToken)
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

        var (activeSummary, activeProfile) = TasteProfileUtilities.GetActiveTasteProfileTexts(user);
        var userHasTasteProfile = !string.IsNullOrWhiteSpace(activeProfile);

        string? normalizedTasteProfile = null;
        string? tasteProfileSummary = null;
        if (allowTasteProfile && userHasTasteProfile)
        {
            normalizedTasteProfile = activeProfile!.Trim();
            tasteProfileSummary = !string.IsNullOrWhiteSpace(activeSummary)
                ? activeSummary!.Trim()
                : null;
        }

        var hasTasteProfile = normalizedTasteProfile is not null;

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

        string prompt;
        if (hasTasteProfile)
        {
            prompt = _chatGptPromptService.BuildSurfEyePrompt(tasteProfileSummary, normalizedTasteProfile!);
        }
        else if (allowTasteProfile)
        {
            prompt = _chatGptPromptService.BuildSurfEyePromptWithoutTasteProfile();
        }
        else
        {
            prompt = _chatGptPromptService.BuildSurfEyeIdentificationPrompt();
        }

        string? ocrText = null;
        double ocrElapsedMilliseconds = 0d;
        var ocrStopwatch = Stopwatch.StartNew();
        try
        {
            await using var ocrStream = new MemoryStream(imageBytes);
            var ocrResult = await _ocrService.ExtractTextAsync(ocrStream, cancellationToken);
            if (ocrResult?.Lines is { Count: > 0 })
            {
                ocrText = string.Join("\n", ocrResult.Lines.Select(l => l.Text).Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
                if (string.IsNullOrWhiteSpace(ocrText))
                {
                    ocrText = null;
                }
            }
        }
        catch (Exception)
        {
            ocrText = null;
        }
        finally
        {
            ocrStopwatch.Stop();
            ocrElapsedMilliseconds = ocrStopwatch.Elapsed.TotalMilliseconds;
        }

        var parts = new List<ChatMessageContentPart>
        {
            ChatMessageContentPart.CreateTextPart(prompt),
            ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(imageBytes), normalizedContentType, ChatImageDetailLevel.High)
        };
        if (!string.IsNullOrWhiteSpace(ocrText))
        {
            parts.Add(ChatMessageContentPart.CreateTextPart($"OCR detected text from image (verbatim):\n{ocrText}"));
        }

        ChatCompletion completion;
        double llmElapsedMilliseconds = 0d;
        var llmStopwatch = Stopwatch.StartNew();
        try
        {
            completion = await _chatGptService.GetChatCompletionAsync(
                new ChatMessage[]
                {
                    new SystemChatMessage(_chatGptPromptService.SurfEyeSystemPrompt),
                    new UserChatMessage(parts)
                },
                model: string.IsNullOrWhiteSpace(modelOverride) ? null : modelOverride,
                ct: cancellationToken);
        }
        catch (ChatGptServiceNotConfiguredException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new SurfEyeAnalysisError("Surf Eye is not configured."));
        }
        catch (ClientResultException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new SurfEyeAnalysisError("We couldn't reach the Surf Eye analyst. Please try again."));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new SurfEyeAnalysisError("We couldn't analyze that photo right now. Please try again."));
        }
        finally
        {
            llmStopwatch.Stop();
            llmElapsedMilliseconds = llmStopwatch.Elapsed.TotalMilliseconds;
        }

        var content = StringUtilities.ExtractCompletionText(completion);
        if (!TryParseSurfEyeAnalysis(content, out var parsedResult))
        {
            return StatusCode(StatusCodes.Status502BadGateway, new SurfEyeAnalysisError("We couldn't understand Surf Eye's response. Please try again."));
        }

        IReadOnlyList<SurfEyeWineMatch> orderedMatches;
        if (hasTasteProfile)
        {
            orderedMatches = parsedResult!.Wines
                .OrderByDescending(match => match.AlignmentScore)
                .ThenByDescending(match => match.Confidence)
                .ThenBy(match => match.Name, StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();
        }
        else
        {
            orderedMatches = parsedResult!.Wines
                .Select(match => match with
                {
                    AlignmentScore = 0,
                    AlignmentSummary = string.Empty
                })
                .OrderByDescending(match => match.Confidence)
                .ThenBy(match => match.Name, StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();
        }

        var summary = string.IsNullOrWhiteSpace(parsedResult.Summary)
            ? (orderedMatches.Count > 0
                ? "Surf Eye spotted the following wines."
                : "Surf Eye couldn't recognize any wines in this photo.")
            : parsedResult.Summary.Trim();

        var resolvedMatches = await ResolveSurfEyeMatchesAsync(orderedMatches, cancellationToken);
        var response = new SurfEyeAnalysisResponse(summary, resolvedMatches)
        {
            Timings = new SurfEyeAnalysisTimings(ocrElapsedMilliseconds, llmElapsedMilliseconds)
        };
        return Json(response);
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
    
     private static bool TryParseSurfEyeAnalysis(string? content, out SurfEyeAnalysisIntermediate? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var segment = StringUtilities.ExtractJsonSegment(content);

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

    private static readonly Regex SurfEyeTokenSplitRegex = new("[\\s\\p{P}]+", RegexOptions.Compiled);

    private async Task<IReadOnlyList<SurfEyeWineMatch>> ResolveSurfEyeMatchesAsync(
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

            if (string.IsNullOrWhiteSpace(match.Name))
            {
                persisted.Add(match);
                continue;
            }

            var searchResults = new List<SurfEyeWineSearchResult>();

            try
            {
                var fuzzyCandidates = await FindFuzzyCandidatesAsync(match, cancellationToken);
                searchResults = BuildSurfEyeSearchResults(match, fuzzyCandidates, out var bestCandidate);

                Wine? existing = null;
                var normalizedName = match.Name?.Trim();

                if (!string.IsNullOrWhiteSpace(normalizedName))
                {
                    var closestMatches = await _wineRepository.FindClosestMatchesAsync(
                        normalizedName!,
                        5,
                        cancellationToken);

                    if (!string.IsNullOrWhiteSpace(match.SubAppellation))
                    {
                        existing = closestMatches
                            .FirstOrDefault(wine => string.Equals(
                                wine?.SubAppellation?.Name,
                                match.SubAppellation,
                                StringComparison.OrdinalIgnoreCase));
                    }

                    if (existing is null && !string.IsNullOrWhiteSpace(match.Appellation))
                    {
                        existing = closestMatches
                            .FirstOrDefault(wine => string.Equals(
                                wine?.SubAppellation?.Appellation?.Name,
                                match.Appellation,
                                StringComparison.OrdinalIgnoreCase));
                    }

                    existing ??= closestMatches.FirstOrDefault();
                }

                if (existing is null && bestCandidate is not null)
                {
                    existing = bestCandidate;
                }

                if (existing is null)
                {
                    persisted.Add(match with { SearchResults = searchResults });
                    continue;
                }

                if (!searchResults.Any(result => result.WineId == existing.Id))
                {
                    searchResults.Insert(0, CreateSearchResult(existing));
                }

                if (searchResults.Count > 3)
                {
                    searchResults = searchResults.Take(3).ToList();
                }

                var subAppellation = existing.SubAppellation;
                var appellation = subAppellation?.Appellation;
                var region = appellation?.Region;
                var country = region?.Country;

                var updated = match with
                {
                    WineId = existing.Id,
                    Country = country?.Name ?? match.Country,
                    Region = region?.Name ?? match.Region,
                    Appellation = appellation?.Name ?? match.Appellation,
                    SubAppellation = subAppellation?.Name ?? match.SubAppellation,
                    Color = existing.Color.ToString(),
                    Variety = string.IsNullOrWhiteSpace(match.Variety) && !string.IsNullOrWhiteSpace(existing.GrapeVariety)
                        ? existing.GrapeVariety
                        : match.Variety,
                    SearchResults = searchResults
                };

                persisted.Add(updated);
            }
            catch
            {
                persisted.Add(match with { SearchResults = searchResults });
            }
        }

        return persisted;
    }

    private async Task<IReadOnlyList<Wine>> FindFuzzyCandidatesAsync(
        SurfEyeWineMatch match,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(match?.Name))
        {
            return Array.Empty<Wine>();
        }

        var queries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedName = match.Name!.Trim();
        if (normalizedName.Length > 0)
        {
            queries.Add(normalizedName);
        }

        foreach (var token in ExtractSignificantTokens(match.Name!))
        {
            queries.Add(token);
        }

        if (queries.Count == 0)
        {
            return Array.Empty<Wine>();
        }

        var candidates = new Dictionary<Guid, Wine>();

        foreach (var query in queries)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                continue;
            }

            var matches = await _wineRepository.FindClosestMatchesAsync(query, 5, cancellationToken);
            foreach (var wine in matches)
            {
                if (wine is null)
                {
                    continue;
                }

                if (!IsMetadataCompatible(wine, match))
                {
                    continue;
                }

                if (!candidates.ContainsKey(wine.Id))
                {
                    candidates[wine.Id] = wine;
                }
            }
        }

        return candidates.Count == 0
            ? Array.Empty<Wine>()
            : candidates.Values.ToList();
    }

    private static List<SurfEyeWineSearchResult> BuildSurfEyeSearchResults(
        SurfEyeWineMatch match,
        IReadOnlyList<Wine> candidates,
        out Wine? bestCandidate)
    {
        bestCandidate = null;

        if (match is null || string.IsNullOrWhiteSpace(match.Name) || candidates is null || candidates.Count == 0)
        {
            return new List<SurfEyeWineSearchResult>();
        }

        var maxResults = Math.Min(3, candidates.Count);
        var ordered = FuzzyMatchUtilities.FindClosestMatches(
            candidates,
            match.Name!,
            BuildWineComparisonLabel,
            maxResults);

        if (ordered.Count == 0)
        {
            return new List<SurfEyeWineSearchResult>();
        }

        bestCandidate = ordered[0];

        var results = new List<SurfEyeWineSearchResult>(ordered.Count);
        var seen = new HashSet<Guid>();

        foreach (var wine in ordered)
        {
            if (wine is null)
            {
                continue;
            }

            if (!seen.Add(wine.Id))
            {
                continue;
            }

            results.Add(CreateSearchResult(wine));
        }

        return results;
    }

    private static SurfEyeWineSearchResult CreateSearchResult(Wine wine)
    {
        var variety = string.IsNullOrWhiteSpace(wine.GrapeVariety)
            ? null
            : wine.GrapeVariety.Trim();

        return new SurfEyeWineSearchResult(
            wine.Id,
            wine.Name,
            BuildSearchResultLocation(wine),
            variety,
            wine.Color.ToString());
    }

    private static string? BuildSearchResultLocation(Wine wine)
    {
        if (wine is null)
        {
            return null;
        }

        var parts = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddPart(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var trimmed = value.Trim();
            if (seen.Add(trimmed))
            {
                parts.Add(trimmed);
            }
        }

        var subAppellation = wine.SubAppellation;
        AddPart(subAppellation?.Name);

        var appellation = subAppellation?.Appellation;
        AddPart(appellation?.Name);

        var region = appellation?.Region;
        AddPart(region?.Name);

        var country = region?.Country;
        AddPart(country?.Name);

        return parts.Count == 0
            ? null
            : string.Join(" • ", parts);
    }

    private static IReadOnlyList<string> ExtractSignificantTokens(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return SurfEyeTokenSplitRegex
            .Split(value)
            .Select(token => token.Trim())
            .Where(token => token.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsMetadataCompatible(Wine wine, SurfEyeWineMatch match)
    {
        if (wine is null)
        {
            return false;
        }

        if (!MatchesColor(wine.Color, match.Color))
        {
            return false;
        }

        var subAppellation = wine.SubAppellation;
        var appellation = subAppellation?.Appellation;
        var region = appellation?.Region;
        var country = region?.Country;

        if (!MatchesMetadata(match.SubAppellation, subAppellation?.Name))
        {
            return false;
        }

        if (!MatchesMetadata(match.Appellation, appellation?.Name))
        {
            return false;
        }

        if (!MatchesMetadata(match.Region, region?.Name))
        {
            return false;
        }

        if (!MatchesMetadata(match.Country, country?.Name))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesMetadata(string? expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(actual))
        {
            return false;
        }

        var compare = CultureInfo.InvariantCulture.CompareInfo;
        var options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        var expectedValue = expected.Trim();
        var actualValue = actual.Trim();

        if (compare.Compare(expectedValue, actualValue, options) == 0)
        {
            return true;
        }

        return compare.IndexOf(actualValue, expectedValue, options) >= 0
            || compare.IndexOf(expectedValue, actualValue, options) >= 0;
    }

    private static bool MatchesColor(WineColor actual, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        var compare = CultureInfo.InvariantCulture.CompareInfo;
        var options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        var expectedValue = expected.Trim();
        var actualValue = actual.ToString();

        if (compare.Compare(expectedValue, actualValue, options) == 0)
        {
            return true;
        }

        if (compare.IndexOf(expectedValue, actualValue, options) >= 0)
        {
            return true;
        }

        if (compare.IndexOf(actualValue, expectedValue, options) >= 0)
        {
            return true;
        }

        if (actual == WineColor.Rose && compare.IndexOf(expectedValue, "rose", options) >= 0)
        {
            return true;
        }

        return false;
    }

    private static string BuildWineComparisonLabel(Wine wine)
    {
        if (wine.SubAppellation is null)
        {
            return wine.Name;
        }

        if (string.IsNullOrWhiteSpace(wine.SubAppellation.Appellation?.Name))
        {
            return $"{wine.Name} ({wine.SubAppellation.Name})";
        }

        return $"{wine.Name} ({wine.SubAppellation.Name}, {wine.SubAppellation.Appellation!.Name})";
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
                return integer.ToString(System.Globalization.CultureInfo.InvariantCulture);
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
                    double.TryParse(text, NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }
        }

        return double.NaN;
    }

    private sealed record SurfEyeAnalysisIntermediate(string? Summary, IReadOnlyList<SurfEyeWineMatch> Wines);

}