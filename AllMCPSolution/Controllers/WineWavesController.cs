using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenAI.Chat;
using System.ClientModel;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;
using AllMCPSolution.Services.Theming;
using AllMCPSolution.Utilities;
using AllMCPSolution.Utilities.Theming;
using Microsoft.Extensions.Options;

namespace AllMCPSolution.Controllers;

[Authorize]
[Route("wine-surfer")]
public sealed class WineWavesController : WineSurferControllerBase
{
    private readonly IWineVintageEvolutionScoreRepository _evolutionScoreRepository;
    private readonly IBottleRepository _bottleRepository;
    private readonly IWineSurferTopBarService _topBarService;
    private readonly IChatGptService _chatGptService;
    private readonly IChatGptPromptService _chatGptPromptService;
    private readonly IThemeService _themeService;
    private readonly IWineVintageUserDrinkingWindowRepository _drinkingWindowRepository;
    private readonly IUserDrinkingWindowService _userDrinkingWindowService;
    private readonly string _wineWavesModel;

    private static readonly JsonDocumentOptions DrinkingWindowJsonDocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static readonly string[] DrinkingWindowStartPropertyCandidates =
    {
        "startYear",
        "start",
        "start_year",
        "from",
        "begin"
    };

    private static readonly string[] DrinkingWindowEndPropertyCandidates =
    {
        "endYear",
        "end",
        "end_year",
        "to",
        "finish"
    };

    private static readonly string[] DrinkingWindowAlignmentPropertyCandidates =
    {
        "alignmentScore",
        "alignment",
        "alignment_score",
        "score"
    };

    private const decimal AlignmentScoreMinimum = 0m;
    private const decimal AlignmentScoreMaximum = 10m;

    public WineWavesController(
        IWineVintageEvolutionScoreRepository evolutionScoreRepository,
        IBottleRepository bottleRepository,
        IWineVintageUserDrinkingWindowRepository drinkingWindowRepository,
        IUserRepository userRepository,
        IWineSurferTopBarService topBarService,
        IChatGptService chatGptService,
        IChatGptPromptService chatGptPromptService,
        IThemeService themeService,
        IOptions<ChatGptOptions> chatGptOptions,
        IUserDrinkingWindowService userDrinkingWindowService,
        UserManager<ApplicationUser> userManager) : base(userManager, userRepository)
    {
        _evolutionScoreRepository = evolutionScoreRepository;
        _bottleRepository = bottleRepository;
        _drinkingWindowRepository = drinkingWindowRepository;
        _topBarService = topBarService;
        _chatGptService = chatGptService;
        _chatGptPromptService = chatGptPromptService;
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _userDrinkingWindowService = userDrinkingWindowService ?? throw new ArgumentNullException(nameof(userDrinkingWindowService));
        if (chatGptOptions is null)
        {
            throw new ArgumentNullException(nameof(chatGptOptions));
        }

        var options = chatGptOptions.Value;
        var fallbackModel = !string.IsNullOrWhiteSpace(options?.DefaultModel)
            ? options!.DefaultModel!
            : ChatGptOptions.FallbackModel;

        _wineWavesModel = string.IsNullOrWhiteSpace(options?.WineWavesModel)
            ? fallbackModel
            : options!.WebSearchModel!;
    }

    [HttpGet("wine-waves")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var currentPath = HttpContext?.Request?.Path.Value ?? string.Empty;
        ViewData["WineSurferTopBarModel"] = await _topBarService.BuildAsync(User, currentPath, cancellationToken);

        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        var isAdmin = await IsCurrentUserAdminAsync(cancellationToken);
        if (!isAdmin)
        {
            return Forbid();
        }

        var userId = currentUserId.Value;

        var scores = await _evolutionScoreRepository.GetForUserAsync(userId, cancellationToken);
        var drinkingWindows = await _drinkingWindowRepository.GetForUserAsync(userId, cancellationToken);
        var drinkingWindowLookup = drinkingWindows
            .GroupBy(window => window.WineVintageId)
            .Select(group => new
            {
                VintageId = group.Key,
                Window = group
                    .OrderByDescending(window => window.GeneratedAtUtc ?? DateTime.MinValue)
                    .FirstOrDefault()
            })
            .Where(entry => entry.Window is not null)
            .ToDictionary(entry => entry.VintageId, entry => entry.Window!);

        var activePalette = _themeService.GetActivePalette();
        var datasetColors = activePalette?.ChartColors ?? Array.Empty<string>();
        var datasetColorCount = datasetColors.Count;
        if (datasetColorCount == 0)
        {
            var fallbackPalette = AppColorPalettes.DefaultPalettes.FirstOrDefault();
            datasetColors = fallbackPalette?.ChartColors ?? Array.Empty<string>();
            datasetColorCount = datasetColors.Count;
        }

        var datasets = scores
            .GroupBy(score => score.WineVintageId)
            .OrderBy(group => group.First().WineVintage.Wine.Name)
            .ThenBy(group => group.First().WineVintage.Vintage)
            .Select((group, index) =>
            {
                var first = group.First();
                var wine = first.WineVintage.Wine;
                var vintage = first.WineVintage.Vintage;

                var points = group
                    .OrderBy(score => score.Year)
                    .Select(score => new WineWavesPoint(score.Year, score.Score))
                    .ToList();

                var label = BuildWineLabel(wine, vintage);
                var detailText = BuildWineOriginDetails(wine);
                var color = datasetColorCount > 0
                    ? datasetColors[index % datasetColorCount]
                    : (AppColorPalettes.DefaultPalettes.FirstOrDefault()?.ChartColors.FirstOrDefault() ?? string.Empty);

                drinkingWindowLookup.TryGetValue(first.WineVintageId, out var drinkingWindow);
                int? drinkingWindowStartYear = drinkingWindow?.StartingYear;
                int? drinkingWindowEndYear = drinkingWindow?.EndingYear;

                return new WineWavesDataset(
                    first.WineVintageId,
                    label,
                    detailText,
                    points,
                    color,
                    drinkingWindowStartYear,
                    drinkingWindowEndYear);
            })
            .ToList();

        var inventory = await BuildInventoryAsync(userId, cancellationToken);

        ViewData["WineSurferPageTitle"] = "Wine Waves";
        var viewModel = new WineWavesViewModel(datasets, inventory);
        return View("~/Views/WineWaves/Index.cshtml", viewModel);
    }

    [HttpPost("wine-waves/make")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MakeWaves(
        [FromBody] WineWavesMakeRequest? request,
        CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        if (request?.WineVintageIds is not { Count: > 0 })
        {
            return BadRequest(new WineWavesMakeResponse(false, "Select at least one wine vintage to generate evolution scores."));
        }

        var userId = currentUserId.Value;
        var requestedIds = request.WineVintageIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (requestedIds.Count == 0)
        {
            return BadRequest(new WineWavesMakeResponse(false, "Select at least one wine vintage to generate evolution scores."));
        }

        var allBottles = await _bottleRepository.GetForUserAsync(userId, cancellationToken);
        var bottleLookup = allBottles
            .Where(bottle => requestedIds.Contains(bottle.WineVintageId))
            .GroupBy(bottle => bottle.WineVintageId)
            .ToDictionary(group => group.Key, group => group.ToList());

        if (bottleLookup.Count == 0)
        {
            return BadRequest(new WineWavesMakeResponse(false, "We couldn't find those wines in your inventory."));
        }

        var promptContexts = bottleLookup
            .Select(pair => BuildPromptContext(userId, pair.Key, pair.Value))
            .Where(context => context is not null)
            .Select(context => context!)
            .OrderBy(context => context.PromptItem.Label)
            .ToList();

        if (promptContexts.Count == 0)
        {
            return BadRequest(new WineWavesMakeResponse(false, "We couldn't gather enough information about the selected wines."));
        }

        var currentUser = await _userRepository.GetByIdAsync(userId, cancellationToken);
        var (tasteProfileSummary, tasteProfile) = TasteProfileUtilities.GetActiveTasteProfileTexts(currentUser);
        var tasteProfileText = BuildTasteProfileText(tasteProfileSummary, tasteProfile);

        var promptItems = new List<WineWavesPromptItem>(promptContexts.Count);
        foreach (var context in promptContexts)
        {
            var promptItem = context.PromptItem;

            try
            {
                var drinkingWindow = await GenerateDrinkingWindowAsync(
                    userId,
                    context.Wine,
                    context.WineVintage,
                    tasteProfileText,
                    cancellationToken);

                if (drinkingWindow is not null)
                {
                    promptItem = promptItem with
                    {
                        DrinkingWindowStartYear = drinkingWindow.StartYear,
                        DrinkingWindowEndYear = drinkingWindow.EndYear
                    };
                }
            }
            catch (ChatGptServiceNotConfiguredException)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new WineWavesMakeResponse(false, "Drinking window generation is not configured."));
            }
            catch (ClientResultException)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new WineWavesMakeResponse(false, "We couldn't reach the drinking window assistant. Please try again."));
            }
            catch (HttpRequestException)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new WineWavesMakeResponse(false, "We couldn't reach the drinking window assistant. Please try again."));
            }
            catch (JsonException)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new WineWavesMakeResponse(false, "The drinking window assistant returned an unexpected response."));
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new WineWavesMakeResponse(false, ex.Message));
            }

            promptItems.Add(promptItem);
        }

        var aggregatedScores = new List<WineVintageEvolutionScore>();

        foreach (var promptItem in promptItems)
        {
            var prompt = _chatGptPromptService.BuildWineWavesPrompt(new[] { promptItem }, tasteProfileSummary, tasteProfile);
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new WineWavesMakeResponse(false, "We couldn't prepare the request. Please try again."));
            }

            ChatCompletion completion;
            try
            {
                completion = await _chatGptService.GetChatCompletionAsync(
                    new ChatMessage[]
                    {
                        new SystemChatMessage(_chatGptPromptService.WineWavesSystemPrompt),
                        new UserChatMessage(prompt)
                    },
                    model: _wineWavesModel,
                    useWebSearch: true,
                    ct: cancellationToken);
            }
            catch (ChatGptServiceNotConfiguredException)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new WineWavesMakeResponse(false, "Wine Waves is not configured to request AI assistance."));
            }
            catch (ClientResultException)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new WineWavesMakeResponse(false, "We couldn't reach the Wine Waves assistant. Please try again."));
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new WineWavesMakeResponse(false, "Wine Waves couldn't generate scores right now. Please try again."));
            }

            var completionText = StringUtilities.ExtractCompletionText(completion);
            if (!TryParseWineWavesScores(completionText, userId, new[] { promptItem.WineVintageId }, out var vintageScores))
            {
                return StatusCode(StatusCodes.Status502BadGateway, new WineWavesMakeResponse(false, "We couldn't understand the Wine Waves response. Please try again."));
            }

            aggregatedScores.AddRange(vintageScores);
        }

        await _evolutionScoreRepository.UpsertRangeAsync(userId, aggregatedScores, cancellationToken);

        var affectedVintageCount = aggregatedScores
            .Select(score => score.WineVintageId)
            .Distinct()
            .Count();

        var successMessage = affectedVintageCount switch
        {
            0 => "Wine Waves did not provide any new scores.",
            1 => "Wine Waves generated evolution scores for 1 vintage.",
            _ => $"Wine Waves generated evolution scores for {affectedVintageCount} vintages."
        };

        return Ok(new WineWavesMakeResponse(true, successMessage));
    }

    [HttpPost("wine-waves/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAllWaves(CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        await _evolutionScoreRepository.RemoveAllForUserAsync(currentUserId.Value, cancellationToken);

        return Ok(new WineWavesDeleteResponse(true, "Deleted all Wine Waves evolution scores."));
    }

    [HttpPost("wine-waves/delete/vintage/{wineVintageId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteWavesForVintage(Guid wineVintageId, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        if (wineVintageId == Guid.Empty)
        {
            return BadRequest(new WineWavesDeleteResponse(false, "Select a valid vintage to delete."));
        }

        await _evolutionScoreRepository.RemoveForWineVintageAsync(currentUserId.Value, wineVintageId, cancellationToken);

        return Ok(new WineWavesDeleteResponse(true, "Deleted Wine Waves evolution scores for the selected vintage."));
    }

    private async Task<IReadOnlyList<WineWavesInventoryItem>> BuildInventoryAsync(Guid userId, CancellationToken cancellationToken)
    {
        var bottles = await _bottleRepository.GetForUserAsync(userId, cancellationToken);
        if (bottles.Count == 0)
        {
            return Array.Empty<WineWavesInventoryItem>();
        }

        var items = bottles
            .GroupBy(bottle => bottle.WineVintageId)
            .Select(group =>
            {
                var first = group.First();
                var wineVintage = first.WineVintage;
                if (wineVintage is null)
                {
                    return null;
                }

                var wine = wineVintage.Wine;
                var label = BuildWineLabel(wine, wineVintage.Vintage);
                var details = BuildWineOriginDetails(wine);
                var availableBottleCount = group.Count(bottle => !bottle.IsDrunk && !bottle.PendingDelivery);

                return new WineWavesInventoryItem(wineVintage.Id, label, details, availableBottleCount);
            })
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderBy(item => item.Label)
            .ToList();

        return items;
    }

    private async Task<DrinkingWindowResult?> GenerateDrinkingWindowAsync(
        Guid userId,
        Wine wine,
        WineVintage wineVintage,
        string tasteProfileText,
        CancellationToken cancellationToken)
    {
        if (wine is null || wineVintage is null)
        {
            return null;
        }

        var wineDescription = BuildWineDescription(wine, wineVintage);
        if (string.IsNullOrWhiteSpace(wineDescription))
        {
            return null;
        }

        var prompt = _chatGptPromptService.BuildDrinkingWindowPrompt(tasteProfileText, wineDescription);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.AppendLine(_chatGptPromptService.DrinkingWindowSystemPrompt);
        builder.AppendLine(prompt);

        var completion = await _chatGptService.GetChatResponseAsync(
            builder.ToString(),
            model: "gpt-4.1",
            useWebSearch: true,
            ct: cancellationToken);

        var content = completion.GetOutputText();
        if (!TryParseDrinkingWindowYears(content, out var startYear, out var endYear, out var alignmentScore))
        {
            throw new JsonException("Unable to parse the drinking window response.");
        }

        var normalizedAlignmentScore = NormalizeAlignmentScore(alignmentScore);
        var generatedAtUtc = DateTime.UtcNow;

        await _userDrinkingWindowService.SaveGeneratedWindowAsync(
            userId,
            wineVintage.Id,
            startYear,
            endYear,
            normalizedAlignmentScore,
            generatedAtUtc,
            cancellationToken);

        return new DrinkingWindowResult(startYear, endYear, normalizedAlignmentScore);
    }

    private static WineWavesPromptContext? BuildPromptContext(Guid userId, Guid wineVintageId, IReadOnlyCollection<Bottle> bottles)
    {
        if (bottles.Count == 0)
        {
            return null;
        }

        var firstBottle = bottles.First();
        var wineVintage = firstBottle.WineVintage;
        if (wineVintage is null)
        {
            return null;
        }

        var wine = wineVintage.Wine;
        if (wine is null)
        {
            return null;
        }

        var label = BuildWineLabel(wine, wineVintage.Vintage);
        var origin = BuildWineOriginDetails(wine);
        var attributes = BuildWineAttributes(wine);

        var tastingNotes = bottles
            .SelectMany(bottle => bottle.TastingNotes ?? Array.Empty<TastingNote>())
            .Where(note => note is not null && note.UserId == userId && !string.IsNullOrWhiteSpace(note.Note))
            .Select(note => note!.Note!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        var existingScores = wineVintage.EvolutionScores
            .Where(score => score.UserId == userId)
            .OrderBy(score => score.Year)
            .Select(score => new WineWavesPromptScore(score.Year, score.Score))
            .ToList();

        var promptItem = new WineWavesPromptItem(
            wineVintageId,
            label,
            wineVintage.Vintage,
            origin,
            attributes,
            tastingNotes,
            existingScores);

        return new WineWavesPromptContext(promptItem, wine, wineVintage);
    }

    private sealed record WineWavesPromptContext(WineWavesPromptItem PromptItem, Wine Wine, WineVintage WineVintage);
    private sealed record DrinkingWindowResult(int StartYear, int EndYear, decimal AlignmentScore);

    private static string BuildTasteProfileText(string? summary, string? profile)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(summary))
        {
            parts.Add(summary.Trim());
        }

        if (!string.IsNullOrWhiteSpace(profile))
        {
            parts.Add(profile.Trim());
        }

        if (parts.Count == 0)
        {
            return "No taste profile is available.";
        }

        return string.Join($"{Environment.NewLine}{Environment.NewLine}", parts);
    }

    private static string BuildWineDescription(Wine wine, WineVintage vintage)
    {
        if (wine is null || vintage is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var wineName = string.IsNullOrWhiteSpace(wine.Name) ? "Unknown wine" : wine.Name.Trim();
        builder.Append(wineName);

        if (vintage.Vintage > 0)
        {
            builder.Append(' ');
            builder.Append(vintage.Vintage.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            builder.Append(" NV");
        }

        var originParts = new List<string>();

        var region = wine.SubAppellation?.Appellation?.Region?.Name;
        if (!string.IsNullOrWhiteSpace(region))
        {
            originParts.Add(region.Trim());
        }

        if (originParts.Count > 0)
        {
            builder.Append(" from ");
            builder.Append(string.Join(", ", originParts));
        }

        if (!string.IsNullOrWhiteSpace(wine.GrapeVariety))
        {
            builder.Append(". Variety: ");
            builder.Append(wine.GrapeVariety.Trim());
        }

        builder.Append('.');
        return builder.ToString();
    }

    private static bool TryParseDrinkingWindowYears(string? content, out int startYear, out int endYear, out decimal alignmentScore)
    {
        startYear = 0;
        endYear = 0;
        alignmentScore = AlignmentScoreMinimum;

        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var segment = StringUtilities.ExtractJsonSegment(content);

        using var document = JsonDocument.Parse(segment, DrinkingWindowJsonDocumentOptions);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Object)
        {
            var startCandidate = TryGetYearFromObject(root, DrinkingWindowStartPropertyCandidates);
            var endCandidate = TryGetYearFromObject(root, DrinkingWindowEndPropertyCandidates);
            var alignmentCandidate = TryGetDecimalFromObject(root, DrinkingWindowAlignmentPropertyCandidates);

            if (startCandidate.HasValue && endCandidate.HasValue)
            {
                startYear = startCandidate.Value;
                endYear = endCandidate.Value;
                alignmentScore = NormalizeAlignmentScore(alignmentCandidate ?? AlignmentScoreMinimum);
                return NormalizeDrinkingWindowYears(ref startYear, ref endYear);
            }

            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                if (TryParseYearsFromArray(property.Value, out startYear, out endYear))
                {
                    alignmentScore = NormalizeAlignmentScore(alignmentCandidate ?? AlignmentScoreMinimum);
                    return NormalizeDrinkingWindowYears(ref startYear, ref endYear);
                }
            }

            return false;
        }

        if (TryParseYearsFromArray(root, out startYear, out endYear))
        {
            alignmentScore = AlignmentScoreMinimum;
            return NormalizeDrinkingWindowYears(ref startYear, ref endYear);
        }

        return false;
    }

    private static int? TryGetYearFromObject(JsonElement element, IReadOnlyList<string> propertyNames)
    {
        foreach (var property in element.EnumerateObject())
        {
            foreach (var candidate in propertyNames)
            {
                if (string.Equals(property.Name, candidate, StringComparison.OrdinalIgnoreCase)
                    && TryConvertToYear(property.Value, out var year))
                {
                    return year;
                }
            }
        }

        return null;
    }

    private static decimal? TryGetDecimalFromObject(JsonElement element, IReadOnlyList<string> propertyNames)
    {
        foreach (var property in element.EnumerateObject())
        {
            foreach (var candidate in propertyNames)
            {
                if (string.Equals(property.Name, candidate, StringComparison.OrdinalIgnoreCase)
                    && TryConvertToDecimal(property.Value, out var value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static bool TryParseYearsFromArray(JsonElement element, out int startYear, out int endYear)
    {
        startYear = 0;
        endYear = 0;

        if (element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        int? first = null;
        int? second = null;

        foreach (var item in element.EnumerateArray())
        {
            if (!first.HasValue && TryConvertToYear(item, out var firstYear))
            {
                first = firstYear;
            }
            else if (first.HasValue && !second.HasValue && TryConvertToYear(item, out var secondYear))
            {
                second = secondYear;
            }

            if (first.HasValue && second.HasValue)
            {
                break;
            }
        }

        if (!first.HasValue || !second.HasValue)
        {
            return false;
        }

        startYear = first.Value;
        endYear = second.Value;
        return true;
    }

    private static bool TryConvertToYear(JsonElement element, out int year)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number when element.TryGetInt32(out var numeric):
                year = numeric;
                return true;
            case JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text)
                    && int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    year = parsed;
                    return true;
                }

                break;
        }

        year = 0;
        return false;
    }

    private static bool TryConvertToDecimal(JsonElement element, out decimal value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number when element.TryGetDecimal(out var numeric):
                value = numeric;
                return true;
            case JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text)
                    && decimal.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    value = parsed;
                    return true;
                }

                break;
        }

        value = 0m;
        return false;
    }

    private static bool NormalizeDrinkingWindowYears(ref int startYear, ref int endYear)
    {
        if (startYear <= 0 || endYear <= 0)
        {
            return false;
        }

        if (startYear > endYear)
        {
            var temp = startYear;
            startYear = endYear;
            endYear = temp;
        }

        return true;
    }

    private static decimal NormalizeAlignmentScore(decimal score)
    {
        if (score < AlignmentScoreMinimum)
        {
            return AlignmentScoreMinimum;
        }

        if (score > AlignmentScoreMaximum)
        {
            return AlignmentScoreMaximum;
        }

        return Math.Round(score, 2, MidpointRounding.AwayFromZero);
    }

    private static bool TryParseWineWavesScores(
        string? completionText,
        Guid userId,
        IReadOnlyCollection<Guid> allowedWineVintageIds,
        out List<WineVintageEvolutionScore> scores)
    {
        scores = new List<WineVintageEvolutionScore>();
        if (string.IsNullOrWhiteSpace(completionText) || allowedWineVintageIds.Count == 0)
        {
            return false;
        }

        var trimmed = StringUtilities.ExtractJsonSegment(completionText);
        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!document.RootElement.TryGetProperty("vintages", out var vintagesElement) || vintagesElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var allowed = new HashSet<Guid>(allowedWineVintageIds);
            var seen = new HashSet<(Guid WineVintageId, int Year)>();

            foreach (var vintageElement in vintagesElement.EnumerateArray())
            {
                if (vintageElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!vintageElement.TryGetProperty("wineVintageId", out var idElement) || idElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var idValue = idElement.GetString();
                if (string.IsNullOrWhiteSpace(idValue) || !Guid.TryParse(idValue, out var wineVintageId))
                {
                    continue;
                }

                if (!allowed.Contains(wineVintageId))
                {
                    continue;
                }

                if (!vintageElement.TryGetProperty("scores", out var scoresElement) || scoresElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var scoreElement in scoresElement.EnumerateArray())
                {
                    if (scoreElement.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (!scoreElement.TryGetProperty("year", out var yearElement) || yearElement.ValueKind != JsonValueKind.Number)
                    {
                        continue;
                    }

                    if (!yearElement.TryGetInt32(out var year))
                    {
                        continue;
                    }

                    if (!scoreElement.TryGetProperty("score", out var scoreValue) || scoreValue.ValueKind != JsonValueKind.Number)
                    {
                        continue;
                    }

                    if (!scoreValue.TryGetDecimal(out var scoreDecimal))
                    {
                        continue;
                    }

                    if (year < 1900 || year > DateTime.UtcNow.Year + 50)
                    {
                        continue;
                    }

                    scoreDecimal = decimal.Max(0m, decimal.Min(10m, scoreDecimal));

                    if (!seen.Add((wineVintageId, year)))
                    {
                        continue;
                    }

                    scores.Add(new WineVintageEvolutionScore
                    {
                        Id = Guid.Empty,
                        UserId = userId,
                        WineVintageId = wineVintageId,
                        Year = year,
                        Score = decimal.Round(scoreDecimal, 2, MidpointRounding.AwayFromZero)
                    });
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return scores.Count > 0;
    }

    private static string BuildWineLabel(Wine? wine, int vintage)
    {
        var nameParts = new List<string>();
        var wineName = wine?.Name;
        if (!string.IsNullOrWhiteSpace(wineName))
        {
            nameParts.Add(wineName.Trim());
        }

        if (vintage > 0)
        {
            nameParts.Add(vintage.ToString(CultureInfo.InvariantCulture));
        }

        if (nameParts.Count > 0)
        {
            return string.Join(" ", nameParts);
        }

        return vintage > 0
            ? $"Vintage {vintage.ToString(CultureInfo.InvariantCulture)}"
            : "Unnamed Wine";
    }

    private static string? BuildWineOriginDetails(Wine? wine)
    {
        if (wine?.SubAppellation is null)
        {
            return null;
        }

        var subAppellation = wine.SubAppellation;
        var appellation = subAppellation.Appellation;
        var region = appellation?.Region;
        var country = region?.Country;

        var locationParts = new List<string?>
        {
            subAppellation?.Name,
            appellation?.Name,
            region?.Name,
            country?.Name
        };

        var details = locationParts
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return details.Count > 0
            ? string.Join(" • ", details)
            : null;
    }

    private static string? BuildWineAttributes(Wine? wine)
    {
        if (wine is null)
        {
            return null;
        }

        var attributes = new List<string>();
        var color = wine.Color switch
        {
            WineColor.Rose => "Rosé",
            WineColor.White => "White",
            WineColor.Red => "Red",
            _ => wine.Color.ToString()
        };

        attributes.Add(color);

        if (!string.IsNullOrWhiteSpace(wine.GrapeVariety))
        {
            attributes.Add(wine.GrapeVariety.Trim());
        }

        return attributes.Count > 0
            ? string.Join(" • ", attributes)
            : null;
    }
}

public sealed record WineWavesViewModel(
    IReadOnlyList<WineWavesDataset> Datasets,
    IReadOnlyList<WineWavesInventoryItem> Inventory);

public sealed record WineWavesDataset(
    Guid WineVintageId,
    string Label,
    string? Details,
    IReadOnlyList<WineWavesPoint> Points,
    string ColorHex,
    int? DrinkingWindowStartYear,
    int? DrinkingWindowEndYear);

public sealed record WineWavesPoint(int Year, decimal Score);

public sealed record WineWavesInventoryItem(Guid WineVintageId, string Label, string? Details, int AvailableBottleCount);

public sealed class WineWavesMakeRequest
{
    public List<Guid> WineVintageIds { get; init; } = new();
}

public sealed record WineWavesMakeResponse(bool Success, string Message);
public sealed record WineWavesDeleteResponse(bool Success, string Message);
