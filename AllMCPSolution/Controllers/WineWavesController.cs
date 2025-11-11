using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    private readonly string _wineWavesModel;

    public WineWavesController(
        IWineVintageEvolutionScoreRepository evolutionScoreRepository,
        IBottleRepository bottleRepository,
        IUserRepository userRepository,
        IWineSurferTopBarService topBarService,
        IChatGptService chatGptService,
        IChatGptPromptService chatGptPromptService,
        IThemeService themeService,
        IOptions<ChatGptOptions> chatGptOptions,
        UserManager<ApplicationUser> userManager) : base(userManager, userRepository)
    {
        _evolutionScoreRepository = evolutionScoreRepository;
        _bottleRepository = bottleRepository;
        _topBarService = topBarService;
        _chatGptService = chatGptService;
        _chatGptPromptService = chatGptPromptService;
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        if (chatGptOptions is null)
        {
            throw new ArgumentNullException(nameof(chatGptOptions));
        }

        var options = chatGptOptions.Value;
        var fallbackModel = !string.IsNullOrWhiteSpace(options?.DefaultModel)
            ? options!.DefaultModel!
            : ChatGptOptions.FallbackModel;

        _wineWavesModel = string.IsNullOrWhiteSpace(options?.WebSearchModel)
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

        var scores = await _evolutionScoreRepository.GetForUserAsync(currentUserId.Value, cancellationToken);

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

                return new WineWavesDataset(
                    first.WineVintageId,
                    label,
                    detailText,
                    points,
                    color);
            })
            .ToList();

        var inventory = await BuildInventoryAsync(currentUserId.Value, cancellationToken);

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

        var promptItems = bottleLookup
            .Select(pair => BuildPromptItem(userId, pair.Key, pair.Value))
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderBy(item => item.Label)
            .ToList();

        if (promptItems.Count == 0)
        {
            return BadRequest(new WineWavesMakeResponse(false, "We couldn't gather enough information about the selected wines."));
        }

        var currentUser = await _userRepository.GetByIdAsync(userId, cancellationToken);
        var (tasteProfileSummary, tasteProfile) = TasteProfileUtilities.GetActiveTasteProfileTexts(currentUser);

        var prompt = _chatGptPromptService.BuildWineWavesPrompt(promptItems, tasteProfileSummary, tasteProfile);
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
        if (!TryParseWineWavesScores(completionText, userId, promptItems.Select(item => item.WineVintageId).ToList(), out var scores))
        {
            return StatusCode(StatusCodes.Status502BadGateway, new WineWavesMakeResponse(false, "We couldn't understand the Wine Waves response. Please try again."));
        }

        await _evolutionScoreRepository.UpsertRangeAsync(userId, scores, cancellationToken);

        var affectedVintageCount = scores
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

                return new WineWavesInventoryItem(wineVintage.Id, label, details);
            })
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderBy(item => item.Label)
            .ToList();

        return items;
    }

    private static WineWavesPromptItem? BuildPromptItem(Guid userId, Guid wineVintageId, IReadOnlyCollection<Bottle> bottles)
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

        return new WineWavesPromptItem(
            wineVintageId,
            label,
            wineVintage.Vintage,
            origin,
            attributes,
            tastingNotes,
            existingScores);
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
    string ColorHex);

public sealed record WineWavesPoint(int Year, decimal Score);

public sealed record WineWavesInventoryItem(Guid WineVintageId, string Label, string? Details);

public sealed class WineWavesMakeRequest
{
    public List<Guid> WineVintageIds { get; init; } = new();
}

public sealed record WineWavesMakeResponse(bool Success, string Message);
public sealed record WineWavesDeleteResponse(bool Success, string Message);
