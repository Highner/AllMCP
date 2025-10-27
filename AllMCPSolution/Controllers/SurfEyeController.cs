using System.ClientModel;
using System.Globalization;
using System.Security.Claims;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;
using AllMCPSolution.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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
        UserManager<ApplicationUser> userManager) : base(userManager, userRepository)
    {
        _bottleLocationRepository = bottleLocationRepository;
        _wineRepository = wineRepository;
        _chatGptPromptService = chatGptPromptService;
        _topBarService = topBarService;
        _chatGptService = chatGptService;
        _ocrService = ocrService;
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

        var (activeSummary, activeProfile) = TasteProfileUtilities.GetActiveTasteProfileTexts(user);
        var hasTasteProfile = !string.IsNullOrWhiteSpace(activeProfile);
        var normalizedTasteProfile = hasTasteProfile ? activeProfile!.Trim() : null;
        var tasteProfileSummary = hasTasteProfile && !string.IsNullOrWhiteSpace(activeSummary)
            ? activeSummary!.Trim()
            : null;

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

        var prompt = hasTasteProfile
            ? _chatGptPromptService.BuildSurfEyePrompt(tasteProfileSummary, normalizedTasteProfile!)
            : _chatGptPromptService.BuildSurfEyePromptWithoutTasteProfile();

        // Run OCR on the uploaded image using Azure Vision OCR service
        string? ocrText = null;
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
        catch(Exception ex)
        {
            // If OCR fails, proceed without it.
            ocrText = null;
        }

        // Compose the chat messages: system prompt, then user prompt with image and extracted text if available
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
        try
        {
            completion = await _chatGptService.GetChatCompletionAsync(
                new ChatMessage[]
                {
                    new SystemChatMessage(_chatGptPromptService.SurfEyeSystemPrompt),
                    new UserChatMessage(parts)
                },
                ct: cancellationToken);
        }
        catch (ChatGptServiceNotConfiguredException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new SurfEyeAnalysisError("Surf Eye is not configured."));
        }
        catch (ClientResultException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new SurfEyeAnalysisError("We couldn't reach the Surf Eye analyst. Please try again."));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new SurfEyeAnalysisError("We couldn't analyze that photo right now. Please try again."));
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
        var response = new SurfEyeAnalysisResponse(summary, resolvedMatches);
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
                var existing = await _wineRepository.FindByNameAsync(
                    match.Name!,
                    match.SubAppellation,
                    match.Appellation,
                    cancellationToken);

                if (existing is null && !string.IsNullOrWhiteSpace(match.Appellation))
                {
                    existing = await _wineRepository.FindByNameAsync(
                        match.Name!,
                        null,
                        match.Appellation,
                        cancellationToken);
                }

                if (existing is null)
                {
                    persisted.Add(match);
                    continue;
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
                        : match.Variety
                };

                persisted.Add(updated);
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