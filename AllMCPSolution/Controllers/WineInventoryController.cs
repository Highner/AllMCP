using System.ClientModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using AllMCPSolution.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenAI.Chat;

namespace AllMCPSolution.Controllers;

[Authorize]
[Route("wine-manager")]
public class WineInventoryController : Controller
{
    private readonly IBottleRepository _bottleRepository;
    private readonly IBottleLocationRepository _bottleLocationRepository;
    private readonly IWineRepository _wineRepository;
    private readonly IWineVintageRepository _wineVintageRepository;
    private readonly ISubAppellationRepository _subAppellationRepository;
    private readonly IAppellationRepository _appellationRepository;
    private readonly IRegionRepository _regionRepository;
    private readonly ICountryRepository _countryRepository;
    private readonly IUserRepository _userRepository;
    private readonly ITastingNoteRepository _tastingNoteRepository;
    private readonly IWineCatalogService _wineCatalogService;
    private readonly IWineSurferTopBarService _topBarService;
    private readonly IWineImportService _wineImportService;
    private readonly IChatGptService _chatGptService;
    private readonly UserManager<ApplicationUser> _userManager;
    private const string WineSurferSystemPrompt = "You are Wine Surfer, an expert wine research assistant. Respond ONLY with minified JSON matching {\"wines\":[{\"name\":\"...\",\"country\":\"...\",\"region\":\"...\",\"appellation\":\"...\",\"subAppellation\":\"...\",\"color\":\"Red\"}]}. List up to six likely matches, order them by relevance, limit color to Red, White, or Rose, and use null for any field you cannot determine. In case of Burgundy wines, include the producer, the climat and the classification (1er Cru or Grand Cru) where applicable. Stay factual and do not make up any wines you did not find.";
    private const string WineSurferStreamMediaType = "application/x-ndjson";
    private const string WineSurferDiscoveryErrorMessage = "Wine Surfer could not reach the discovery service. Please try again.";
    private const string WineSurferGenericErrorMessage = "Wine Surfer is unavailable right now. Please try again.";
    private const string WineSurferUnexpectedResponseMessage = "Wine Surfer returned an unexpected response. Please try again.";
    private const string WineSurferConfigurationErrorMessage = "Wine Surfer is not configured. Please contact your administrator.";
    private static readonly JsonSerializerOptions WineSurferStreamSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public WineInventoryController(
        IBottleRepository bottleRepository,
        IBottleLocationRepository bottleLocationRepository,
        IWineRepository wineRepository,
        IWineVintageRepository wineVintageRepository,
        ISubAppellationRepository subAppellationRepository,
        IAppellationRepository appellationRepository,
        IRegionRepository regionRepository,
        ICountryRepository countryRepository,
        IUserRepository userRepository,
        ITastingNoteRepository tastingNoteRepository,
        IWineCatalogService wineCatalogService,
        IWineSurferTopBarService topBarService,
        IWineImportService wineImportService,
        IChatGptService chatGptService,
        UserManager<ApplicationUser> userManager)
    {
        _bottleRepository = bottleRepository;
        _bottleLocationRepository = bottleLocationRepository;
        _wineRepository = wineRepository;
        _wineVintageRepository = wineVintageRepository;
        _subAppellationRepository = subAppellationRepository;
        _appellationRepository = appellationRepository;
        _regionRepository = regionRepository;
        _countryRepository = countryRepository;
        _userRepository = userRepository;
        _tastingNoteRepository = tastingNoteRepository;
        _wineCatalogService = wineCatalogService;
        _topBarService = topBarService;
        _wineImportService = wineImportService;
        _chatGptService = chatGptService;
        _userManager = userManager;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery] string? status,
        [FromQuery] string? color,
        [FromQuery] string? search,
        [FromQuery] string? sortField,
        [FromQuery] string? sortDir,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        var currentPath = HttpContext?.Request?.Path.Value ?? string.Empty;
        ViewData["WineSurferTopBarModel"] = await _topBarService.BuildAsync(User, currentPath, cancellationToken);

        var allBottles = await _bottleRepository.GetAllAsync(cancellationToken);

        var bottles = allBottles
            .Where(bottle => bottle.UserId == currentUserId)
            .ToList();

        var bottleGroups = bottles
            .GroupBy(b => b.WineVintageId)
            .ToList();

        var averageScores = bottleGroups
            .Select(group => new
            {
                group.Key,
                Score = CalculateAverageScore(group)
            })
            .Where(entry => entry.Score.HasValue)
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Score!.Value);

        decimal? GetAverageScore(Guid wineVintageId) =>
            averageScores.TryGetValue(wineVintageId, out var avg)
                ? avg
                : null;

        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim().ToLowerInvariant();
        WineColor? filterColor = null;
        if (!string.IsNullOrWhiteSpace(color) && Enum.TryParse<WineColor>(color, true, out var parsedColor))
        {
            filterColor = parsedColor;
        }

        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var normalizedSortField = string.IsNullOrWhiteSpace(sortField) ? "wine" : sortField.Trim().ToLowerInvariant();
        var normalizedSortDir = string.IsNullOrWhiteSpace(sortDir) ? "asc" : sortDir.Trim().ToLowerInvariant();
        var hasActiveFilters = !string.Equals(normalizedStatus, "all", StringComparison.Ordinal)
            || filterColor.HasValue
            || !string.IsNullOrWhiteSpace(normalizedSearch);

        IEnumerable<Bottle> query = bottles;

        query = normalizedStatus switch
        {
            "drunk" => query.Where(b => b.IsDrunk),
            "cellared" or "undrunk" => query.Where(b => !b.IsDrunk),
            _ => query
        };

        if (filterColor.HasValue)
        {
            query = query.Where(b => b.WineVintage.Wine.Color == filterColor.Value);
        }

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(b =>
                (!string.IsNullOrEmpty(b.WineVintage.Wine.Name) && b.WineVintage.Wine.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)) ||
                (b.WineVintage.Wine.SubAppellation?.Name?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (b.WineVintage.Wine.SubAppellation?.Appellation?.Name?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
                b.WineVintage.Vintage.ToString(CultureInfo.InvariantCulture).Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase));
        }

        var descending = string.Equals(normalizedSortDir, "desc", StringComparison.OrdinalIgnoreCase);
        var filteredBottles = query.ToList();
        var sortSource = filteredBottles.AsEnumerable();

        IOrderedEnumerable<Bottle> ordered = normalizedSortField switch
        {
            "appellation" => descending
                ? sortSource.OrderByDescending(b => b.WineVintage.Wine.SubAppellation?.Appellation?.Name)
                    .ThenByDescending(b => b.WineVintage.Wine.SubAppellation?.Name)
                : sortSource.OrderBy(b => b.WineVintage.Wine.SubAppellation?.Appellation?.Name)
                    .ThenBy(b => b.WineVintage.Wine.SubAppellation?.Name),
            "vintage" => descending
                ? sortSource.OrderByDescending(b => b.WineVintage.Vintage)
                : sortSource.OrderBy(b => b.WineVintage.Vintage),
            "color" => descending
                ? sortSource.OrderByDescending(b => b.WineVintage.Wine.Color)
                : sortSource.OrderBy(b => b.WineVintage.Wine.Color),
            "status" => descending
                ? sortSource.OrderByDescending(b => b.IsDrunk)
                : sortSource.OrderBy(b => b.IsDrunk),
            "score" => descending
                ? sortSource.OrderByDescending(b => GetAverageScore(b.WineVintageId))
                : sortSource.OrderBy(b => GetAverageScore(b.WineVintageId)),
            _ => descending
                ? sortSource.OrderByDescending(b => b.WineVintage.Wine.Name)
                : sortSource.OrderBy(b => b.WineVintage.Wine.Name)
        };

        var orderedWithTies = ordered
            .ThenBy(b => b.WineVintage.Wine.Name)
            .ThenBy(b => b.WineVintage.Vintage)
            .ThenBy(b => b.Id);

        var groupedBottles = orderedWithTies
            .GroupBy(b => b.WineVintageId)
            .Select(group =>
            {
                var bottlesInGroup = group.ToList();
                return CreateBottleGroupViewModel(group.Key, bottlesInGroup, GetAverageScore(group.Key));
            })
            .ToList();

        IOrderedEnumerable<WineInventoryBottleViewModel> orderedGroups = normalizedSortField switch
        {
            "appellation" => descending
                ? groupedBottles.OrderByDescending(b => b.Appellation).ThenByDescending(b => b.SubAppellation)
                : groupedBottles.OrderBy(b => b.Appellation).ThenBy(b => b.SubAppellation),
            "vintage" => descending
                ? groupedBottles.OrderByDescending(b => b.Vintage)
                : groupedBottles.OrderBy(b => b.Vintage),
            "color" => descending
                ? groupedBottles.OrderByDescending(b => b.Color)
                : groupedBottles.OrderBy(b => b.Color),
            "status" => descending
                ? groupedBottles.OrderByDescending(b => b.StatusLabel)
                : groupedBottles.OrderBy(b => b.StatusLabel),
            "score" => descending
                ? groupedBottles.OrderByDescending(b => b.AverageScore)
                : groupedBottles.OrderBy(b => b.AverageScore),
            _ => descending
                ? groupedBottles.OrderByDescending(b => b.WineName)
                : groupedBottles.OrderBy(b => b.WineName)
        };

        var items = orderedGroups
            .ThenBy(b => b.WineName)
            .ThenBy(b => b.Vintage)
            .ToList();

        var userLocations = await GetUserLocationsAsync(currentUserId, cancellationToken);
        var userLocationIds = userLocations
            .Select(location => location.Id)
            .ToHashSet();

        var locationSummaries = BuildLocationSummaries(bottles, userLocations);

        var highlightedLocationIds = hasActiveFilters
            ? filteredBottles
                .Where(bottle => bottle.BottleLocationId.HasValue
                    && userLocationIds.Contains(bottle.BottleLocationId.Value))
                .Select(bottle => bottle.BottleLocationId!.Value)
                .ToHashSet()
            : new HashSet<Guid>();

        var viewModel = new WineInventoryViewModel
        {
            Status = normalizedStatus,
            Color = filterColor?.ToString(),
            Search = normalizedSearch,
            SortField = normalizedSortField,
            SortDirection = descending ? "desc" : "asc",
            Bottles = items,
            CurrentUserId = currentUserId,
            Locations = locationSummaries,
            InventoryAddModal = new InventoryAddModalViewModel
            {
                Locations = userLocations
                    .Select(location => new BottleLocationOption
                    {
                        Id = location.Id,
                        Name = location.Name,
                        Capacity = location.Capacity
                    })
                    .ToList()
            },
            HasActiveFilters = hasActiveFilters,
            HighlightedLocationIds = highlightedLocationIds,
            StatusOptions = new List<FilterOption>
            {
                new("all", "All Bottles"),
                new("cellared", "Cellared"),
                new("drunk", "Drunk")
            },
            ColorOptions = new List<FilterOption>
            {
                new(string.Empty, "All Colors"),
                new(WineColor.Red.ToString(), "Red"),
                new(WineColor.White.ToString(), "White"),
                new(WineColor.Rose.ToString(), "Rosé")
            }
        };

        Response.ContentType = "text/html; charset=utf-8";
        return View("Index", viewModel);
    }

    [HttpGet("import")]
    public async Task<IActionResult> Import(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out _))
        {
            return Challenge();
        }

        var currentPath = HttpContext?.Request?.Path.Value ?? string.Empty;
        ViewData["WineSurferTopBarModel"] = await _topBarService.BuildAsync(User, currentPath, cancellationToken);

        var viewModel = new WineImportPageViewModel();
        return View("Import", viewModel);
    }

    [HttpPost("import")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(IFormFile? file, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out _))
        {
            return Challenge();
        }

        var currentPath = HttpContext?.Request?.Path.Value ?? string.Empty;
        ViewData["WineSurferTopBarModel"] = await _topBarService.BuildAsync(User, currentPath, cancellationToken);

        var viewModel = new WineImportPageViewModel();

        if (file is null || file.Length == 0)
        {
            viewModel.Errors = new[] { "Please select an Excel file to upload." };
            return View("Import", viewModel);
        }

        var extension = Path.GetExtension(file.FileName);
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".xls", StringComparison.OrdinalIgnoreCase))
        {
            viewModel.Errors = new[] { "Unsupported file type. Please upload an .xlsx or .xls file." };
            return View("Import", viewModel);
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _wineImportService.ImportAsync(stream, cancellationToken);
            viewModel.Result = result;
            viewModel.UploadedFileName = file.FileName;
        }
        catch (InvalidDataException ex)
        {
            viewModel.Errors = new[] { ex.Message };
        }
        catch (Exception ex)
        {
            viewModel.Errors = new[] { $"An unexpected error occurred while importing wines: {ex.Message}" };
        }

        return View("Import", viewModel);
    }

    [HttpGet("bottles/{wineVintageId:guid}")]
    public async Task<IActionResult> GetBottleGroupDetails(Guid wineVintageId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        var response = await BuildBottleGroupResponseAsync(wineVintageId, currentUserId, cancellationToken);
        if (response.Group is null)
        {
            return NotFound();
        }

        return Json(response);
    }

    [HttpGet("options")]
    public async Task<IActionResult> GetReferenceData(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        var subAppellations = await _subAppellationRepository.GetAllAsync(cancellationToken);
        var bottleLocations = await _bottleLocationRepository.GetAllAsync(cancellationToken);
        var userLocations = bottleLocations
            .Where(location => location.UserId == currentUserId)
            .OrderBy(location => location.Name)
            .ToList();
        var users = await _userRepository.GetAllAsync(cancellationToken);

        var response = new InventoryReferenceDataResponse
        {
            SubAppellations = subAppellations
                .Select(sa => new SubAppellationOption
                {
                    Id = sa.Id,
                    Label = BuildSubAppellationLabel(sa)
                })
                .ToList(),
            BottleLocations = userLocations
                .Select(bl => new BottleLocationOption
                {
                    Id = bl.Id,
                    Name = bl.Name,
                    Capacity = bl.Capacity
                })
                .ToList(),
            Users = users
                .Select(u => new UserOption
                {
                    Id = u.Id,
                    Name = u.Name
                })
                .ToList()
        };

        return Json(response);
    }

    [HttpGet("wines")]
    public async Task<IActionResult> GetWineOptions([FromQuery(Name = "search")] string? search, CancellationToken cancellationToken)
    {
        var trimmedSearch = search?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedSearch) || trimmedSearch.Length < 3)
        {
            return Json(Array.Empty<WineInventoryWineOption>());
        }

        var wineOptions = await _wineRepository.SearchInventoryOptionsAsync(trimmedSearch, 20, cancellationToken);

        var response = wineOptions
            .Select(option => new WineInventoryWineOption
            {
                Id = option.Id,
                Name = option.Name,
                Color = option.Color.ToString(),
                SubAppellation = option.SubAppellation,
                Appellation = option.Appellation,
                Region = option.Region,
                Country = option.Country,
                Vintages = option.Vintages.ToList()
            })
            .ToList();

        return Json(response);
    }

    [HttpGet("wine-surfer")]
    public async Task<IActionResult> GetWineSurferMatches([FromQuery(Name = "query")] string? query, CancellationToken cancellationToken)
    {
        var trimmedQuery = query?.Trim();
        var wantsStream = AcceptsWineSurferStream(Request);

        if (string.IsNullOrWhiteSpace(trimmedQuery) || trimmedQuery.Length < 3)
        {
            if (wantsStream)
            {
                await SendImmediateWineSurferResponseAsync(Array.Empty<WineSurferLookupResult>(), cancellationToken);
                return new EmptyResult();
            }

            return Json(new WineSurferLookupResponse(Array.Empty<WineSurferLookupResult>()));
        }

        if (wantsStream)
        {
            await StreamWineSurferMatchesAsync(trimmedQuery, cancellationToken);
            return new EmptyResult();
        }

        ChatCompletion completion;
        try
        {
            completion = await _chatGptService.GetChatCompletionAsync(
                new ChatMessage[]
                {
                    new SystemChatMessage(WineSurferSystemPrompt),
                    new UserChatMessage(BuildWineSurferUserPrompt(trimmedQuery))
                },
                temperature: 0.2,
                ct: cancellationToken);
        }
        catch (ChatGptServiceNotConfiguredException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new WineSurferLookupError(WineSurferConfigurationErrorMessage));
        }
        catch (ClientResultException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new WineSurferLookupError(WineSurferDiscoveryErrorMessage));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new WineSurferLookupError(WineSurferGenericErrorMessage));
        }

        var completionText = ExtractCompletionText(completion);
        if (!WineSurferResultParser.TryParse(completionText, out var matches))
        {
            return StatusCode(StatusCodes.Status502BadGateway, new WineSurferLookupError(WineSurferUnexpectedResponseMessage));
        }

        return Json(new WineSurferLookupResponse(matches));
    }

    [HttpGet("catalog/countries")]
    public async Task<IActionResult> SearchCountries([FromQuery(Name = "search")] string? search, CancellationToken cancellationToken)
    {
        var trimmedSearch = search?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedSearch) || trimmedSearch.Length < 1)
        {
            return Json(Array.Empty<WineCountrySuggestion>());
        }

        var matches = await _countryRepository.SearchByApproximateNameAsync(trimmedSearch, 10, cancellationToken);
        var response = matches
            .Where(country => !string.IsNullOrWhiteSpace(country.Name))
            .Select(country => new WineCountrySuggestion
            {
                Id = country.Id,
                Name = country.Name ?? string.Empty
            })
            .ToList();

        return Json(response);
    }

    [HttpGet("catalog/regions")]
    public async Task<IActionResult> SearchRegions(
        [FromQuery(Name = "search")] string? search,
        [FromQuery(Name = "countryId")] Guid? countryId,
        CancellationToken cancellationToken)
    {
        var trimmedSearch = search?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedSearch) || trimmedSearch.Length < 1)
        {
            return Json(Array.Empty<WineRegionSuggestion>());
        }

        var matches = await _regionRepository.SearchByApproximateNameAsync(trimmedSearch, 20, cancellationToken);
        var filtered = countryId.HasValue
            ? matches.Where(region => region.CountryId == countryId.Value)
            : matches;

        var response = filtered
            .Where(region => !string.IsNullOrWhiteSpace(region.Name))
            .Take(10)
            .Select(region => new WineRegionSuggestion
            {
                Id = region.Id,
                Name = region.Name ?? string.Empty,
                CountryId = region.CountryId,
                CountryName = region.Country?.Name ?? string.Empty
            })
            .ToList();

        return Json(response);
    }

    [HttpGet("catalog/appellations")]
    public async Task<IActionResult> SearchAppellations(
        [FromQuery(Name = "search")] string? search,
        [FromQuery(Name = "regionId")] Guid? regionId,
        CancellationToken cancellationToken)
    {
        if (!regionId.HasValue)
        {
            return Json(Array.Empty<WineAppellationSuggestion>());
        }

        var trimmedSearch = search?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedSearch) || trimmedSearch.Length < 1)
        {
            return Json(Array.Empty<WineAppellationSuggestion>());
        }

        var matches = await _appellationRepository.SearchByApproximateNameAsync(trimmedSearch, regionId.Value, 10, cancellationToken);
        var response = matches
            .Where(appellation => !string.IsNullOrWhiteSpace(appellation.Name))
            .Select(appellation => new WineAppellationSuggestion
            {
                Id = appellation.Id,
                Name = appellation.Name ?? string.Empty,
                RegionId = appellation.RegionId,
                RegionName = appellation.Region?.Name ?? string.Empty,
                CountryId = appellation.Region?.CountryId ?? Guid.Empty,
                CountryName = appellation.Region?.Country?.Name ?? string.Empty
            })
            .ToList();

        return Json(response);
    }

    [HttpGet("catalog/sub-appellations")]
    public async Task<IActionResult> SearchSubAppellations(
        [FromQuery(Name = "search")] string? search,
        [FromQuery(Name = "appellationId")] Guid? appellationId,
        CancellationToken cancellationToken)
    {
        if (!appellationId.HasValue)
        {
            return Json(Array.Empty<WineSubAppellationSuggestion>());
        }

        var trimmedSearch = search?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedSearch) || trimmedSearch.Length < 1)
        {
            return Json(Array.Empty<WineSubAppellationSuggestion>());
        }

        var matches = await _subAppellationRepository.SearchByApproximateNameAsync(trimmedSearch, appellationId.Value, 10, cancellationToken);
        var response = matches
            .Where(sub => !string.IsNullOrWhiteSpace(sub.Name))
            .Select(sub => new WineSubAppellationSuggestion
            {
                Id = sub.Id,
                Name = sub.Name ?? string.Empty,
                AppellationId = sub.AppellationId,
                AppellationName = sub.Appellation?.Name ?? string.Empty,
                RegionId = sub.Appellation?.RegionId ?? Guid.Empty,
                RegionName = sub.Appellation?.Region?.Name ?? string.Empty,
                CountryId = sub.Appellation?.Region?.CountryId ?? Guid.Empty,
                CountryName = sub.Appellation?.Region?.Country?.Name ?? string.Empty
            })
            .ToList();

        return Json(response);
    }

    [HttpPost("catalog/wines")]
    public async Task<IActionResult> CreateWine([FromBody] CreateWineCatalogRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (!TryGetCurrentUserId(out _))
        {
            return Challenge();
        }

        var result = await _wineCatalogService.EnsureWineAsync(
            new WineCatalogRequest(
                request.Name,
                request.Color,
                request.Country,
                request.Region,
                request.Appellation,
                request.SubAppellation,
                request.GrapeVariety),
            cancellationToken);

        if (!result.IsSuccess || result.Wine is null)
        {
            if (result.Errors.Count > 0)
            {
                foreach (var entry in result.Errors)
                {
                    var key = string.IsNullOrWhiteSpace(entry.Key) ? string.Empty : entry.Key;
                    foreach (var message in entry.Value)
                    {
                        ModelState.AddModelError(key, message);
                    }
                }
            }
            else
            {
                ModelState.AddModelError(string.Empty, "Unable to add wine to the catalog.");
            }

            return ValidationProblem(ModelState);
        }

        var option = CreateWineOption(result.Wine);
        return Json(option);
    }

    [HttpPost("wine-surfer/wines")]
    public async Task<IActionResult> CreateWineFromWineSurfer([FromBody] WineSurferAddWineRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (!TryGetCurrentUserId(out _))
        {
            return Challenge();
        }

        var result = await _wineCatalogService.EnsureWineAsync(
            new WineCatalogRequest(
                request.Name,
                request.Color,
                request.Country,
                request.Region,
                request.Appellation,
                request.SubAppellation,
                null),
            cancellationToken);

        if (!result.IsSuccess || result.Wine is null)
        {
            if (result.Errors.Count > 0)
            {
                foreach (var entry in result.Errors)
                {
                    var key = string.IsNullOrWhiteSpace(entry.Key) ? string.Empty : entry.Key;
                    foreach (var message in entry.Value)
                    {
                        ModelState.AddModelError(key, message);
                    }
                }
            }
            else
            {
                ModelState.AddModelError(string.Empty, "Unable to add wine to the catalog.");
            }

            return ValidationProblem(ModelState);
        }

        var option = CreateWineOption(result.Wine);
        return Json(option);
    }

    [HttpGet("bottles/{bottleId:guid}/notes")]
    public async Task<IActionResult> GetBottleNotes(Guid bottleId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        var response = await BuildBottleNotesResponseAsync(bottleId, currentUserId, cancellationToken);
        if (response is null)
        {
            return NotFound();
        }

        return Json(response);
    }

    [HttpPost("groups")]
    public async Task<IActionResult> CreateGroup([FromBody] WineGroupCreateRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        var trimmedName = request.WineName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            ModelState.AddModelError(nameof(request.WineName), "Wine name is required.");
            return ValidationProblem(ModelState);
        }

        var subAppellation = await _subAppellationRepository.GetByIdAsync(request.SubAppellationId, cancellationToken);
        if (subAppellation is null)
        {
            ModelState.AddModelError(nameof(request.SubAppellationId), "Sub-appellation was not found.");
            return ValidationProblem(ModelState);
        }

        var duplicate = await _wineRepository.FindByNameAsync(trimmedName, subAppellation.Name, subAppellation.Appellation?.Name, cancellationToken);
        if (duplicate is not null)
        {
            ModelState.AddModelError(nameof(request.WineName), "A wine with the same name already exists for the selected sub-appellation.");
            return ValidationProblem(ModelState);
        }

        BottleLocation? bottleLocation = null;
        if (request.BottleLocationId.HasValue)
        {
            bottleLocation = await _bottleLocationRepository.GetByIdAsync(request.BottleLocationId.Value, cancellationToken);
            if (bottleLocation is null)
            {
                ModelState.AddModelError(nameof(request.BottleLocationId), "Bottle location was not found.");
                return ValidationProblem(ModelState);
            }
        }

        var wine = new Wine
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            Color = request.Color,
            SubAppellationId = subAppellation.Id
        };

        await _wineRepository.AddAsync(wine, cancellationToken);

        var wineVintage = await _wineVintageRepository.GetOrCreateAsync(wine.Id, request.Vintage, cancellationToken);

        for (var i = 0; i < request.InitialBottleCount; i++)
        {
            var bottle = new Bottle
            {
                Id = Guid.NewGuid(),
                WineVintageId = wineVintage.Id,
                IsDrunk = false,
                DrunkAt = null,
                Price = null,
                BottleLocationId = bottleLocation?.Id,
                UserId = currentUserId
            };

            await _bottleRepository.AddAsync(bottle, cancellationToken);
        }

        var response = await BuildBottleGroupResponseAsync(wineVintage.Id, currentUserId, cancellationToken);
        if (response.Group is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "Unable to load wine group after creation.");
        }

        return Json(response);
    }

    [HttpPost("inventory")]
    public async Task<IActionResult> AddWineToInventory([FromBody] AddWineToInventoryRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (request.WineId == Guid.Empty)
        {
            ModelState.AddModelError(nameof(request.WineId), "Wine must be selected.");
            return ValidationProblem(ModelState);
        }

        var wine = await _wineRepository.GetByIdAsync(request.WineId, cancellationToken);
        if (wine is null)
        {
            ModelState.AddModelError(nameof(request.WineId), "The selected wine could not be found.");
            return ValidationProblem(ModelState);
        }

        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Unauthorized("You must be signed in to add wine to your inventory.");
        }

        var user = await _userRepository.GetByIdAsync(currentUserId.Value, cancellationToken);
        if (user is null)
        {
            return Unauthorized("You must be signed in to add wine to your inventory.");
        }

        BottleLocation? bottleLocation = null;
        if (request.BottleLocationId.HasValue)
        {
            bottleLocation = await _bottleLocationRepository.GetByIdAsync(request.BottleLocationId.Value, cancellationToken);
            if (bottleLocation is null)
            {
                ModelState.AddModelError(nameof(request.BottleLocationId), "Bottle location was not found.");
                return ValidationProblem(ModelState);
            }
        }

        var wineVintage = await _wineVintageRepository.GetOrCreateAsync(wine.Id, request.Vintage, cancellationToken);

        var quantity = request.Quantity;
        if (quantity < 1)
        {
            quantity = 1;
        }
        else if (quantity > 12)
        {
            quantity = 12;
        }

        for (var i = 0; i < quantity; i++)
        {
            var bottle = new Bottle
            {
                Id = Guid.NewGuid(),
                WineVintageId = wineVintage.Id,
                Price = null,
                IsDrunk = false,
                DrunkAt = null,
                BottleLocationId = bottleLocation?.Id,
                UserId = user.Id
            };

            await _bottleRepository.AddAsync(bottle, cancellationToken);
        }

        var response = await BuildBottleGroupResponseAsync(wineVintage.Id, currentUserId.Value, cancellationToken);
        if (response.Group is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "Unable to load wine group after creation.");
        }

        return Json(response);
    }

    [HttpPut("groups/{wineVintageId:guid}")]
    public async Task<IActionResult> UpdateGroup(Guid wineVintageId, [FromBody] WineGroupUpdateRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        var existingVintage = await _wineVintageRepository.GetByIdAsync(wineVintageId, cancellationToken);
        if (existingVintage is null)
        {
            return NotFound();
        }

        var existingBottles = await _bottleRepository.GetByWineVintageIdAsync(wineVintageId, cancellationToken);
        if (!existingBottles.Any(bottle => bottle.UserId == currentUserId))
        {
            return NotFound();
        }

        var trimmedName = request.WineName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            ModelState.AddModelError(nameof(request.WineName), "Wine name is required.");
            return ValidationProblem(ModelState);
        }

        var subAppellation = await _subAppellationRepository.GetByIdAsync(request.SubAppellationId, cancellationToken);
        if (subAppellation is null)
        {
            ModelState.AddModelError(nameof(request.SubAppellationId), "Sub-appellation was not found.");
            return ValidationProblem(ModelState);
        }

        var duplicate = await _wineRepository.FindByNameAsync(trimmedName, subAppellation.Name, subAppellation.Appellation?.Name, cancellationToken);
        if (duplicate is not null && duplicate.Id != existingVintage.WineId)
        {
            ModelState.AddModelError(nameof(request.WineName), "A wine with the same name already exists for the selected sub-appellation.");
            return ValidationProblem(ModelState);
        }

        var wine = await _wineRepository.GetByIdAsync(existingVintage.WineId, cancellationToken);
        if (wine is null)
        {
            return NotFound();
        }

        wine.Name = trimmedName;
        wine.Color = request.Color;
        wine.SubAppellationId = subAppellation.Id;

        await _wineRepository.UpdateAsync(wine, cancellationToken);

        var updatedVintage = new WineVintage
        {
            Id = existingVintage.Id,
            WineId = wine.Id,
            Vintage = request.Vintage
        };

        await _wineVintageRepository.UpdateAsync(updatedVintage, cancellationToken);

        var response = await BuildBottleGroupResponseAsync(updatedVintage.Id, currentUserId, cancellationToken);
        if (response.Group is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "Unable to load wine group after update.");
        }

        return Json(response);
    }

    [HttpDelete("groups/{wineVintageId:guid}")]
    public async Task<IActionResult> DeleteGroup(Guid wineVintageId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        var existingVintage = await _wineVintageRepository.GetByIdAsync(wineVintageId, cancellationToken);
        if (existingVintage is null)
        {
            return NotFound();
        }

        var bottles = await _bottleRepository.GetByWineVintageIdAsync(wineVintageId, cancellationToken);
        var ownedBottles = bottles
            .Where(bottle => bottle.UserId == currentUserId)
            .ToList();

        if (ownedBottles.Count == 0)
        {
            return NotFound();
        }

        foreach (var bottle in ownedBottles)
        {
            await _bottleRepository.DeleteAsync(bottle.Id, cancellationToken);
        }

        var remainingBottles = await _bottleRepository.GetByWineVintageIdAsync(wineVintageId, cancellationToken);
        if (!remainingBottles.Any())
        {
            await _wineVintageRepository.DeleteAsync(wineVintageId, cancellationToken);

            var wine = await _wineRepository.GetByIdAsync(existingVintage.WineId, cancellationToken);
            if (wine is not null && (wine.WineVintages == null || wine.WineVintages.Count == 0))
            {
                await _wineRepository.DeleteAsync(wine.Id, cancellationToken);
            }
        }

        return NoContent();
    }

    [HttpPost("bottles")]
    public async Task<IActionResult> CreateBottle([FromBody] BottleMutationRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        var existingBottles = await _bottleRepository.GetByWineVintageIdAsync(request.WineVintageId, cancellationToken);
        if (!existingBottles.Any(bottle => bottle.UserId == currentUserId))
        {
            return NotFound();
        }

        BottleLocation? bottleLocation = null;
        if (request.BottleLocationId.HasValue)
        {
            bottleLocation = await _bottleLocationRepository.GetByIdAsync(request.BottleLocationId.Value, cancellationToken);
            if (bottleLocation is null)
            {
                ModelState.AddModelError(nameof(request.BottleLocationId), "Bottle location was not found.");
                return ValidationProblem(ModelState);
            }
        }

        Guid targetUserId = currentUserId;
        if (request.UserId.HasValue && request.UserId.Value != currentUserId)
        {
            ModelState.AddModelError(nameof(request.UserId), "You can only assign bottles to your account.");
            return ValidationProblem(ModelState);
        }

        var quantity = request.Quantity <= 0 ? 1 : request.Quantity;
        var isDrunk = request.IsDrunk;
        var drunkAt = NormalizeDrunkAt(isDrunk, request.DrunkAt);

        for (var i = 0; i < quantity; i++)
        {
            var bottle = new Bottle
            {
                Id = Guid.NewGuid(),
                WineVintageId = request.WineVintageId,
                Price = request.Price,
                IsDrunk = isDrunk,
                DrunkAt = drunkAt,
                BottleLocationId = bottleLocation?.Id,
                UserId = targetUserId
            };

            await _bottleRepository.AddAsync(bottle, cancellationToken);
        }

        var response = await BuildBottleGroupResponseAsync(request.WineVintageId, currentUserId, cancellationToken);
        return response is null
            ? StatusCode(StatusCodes.Status500InternalServerError, "Unable to load bottle group after creation.")
            : Json(response);
    }

    [HttpPut("bottles/{id:guid}")]
    public async Task<IActionResult> UpdateBottle(Guid id, [FromBody] BottleMutationRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        var bottle = await _bottleRepository.GetByIdAsync(id, cancellationToken);
        if (bottle is null || bottle.UserId != currentUserId)
        {
            return NotFound();
        }

        BottleLocation? bottleLocation = null;
        if (request.BottleLocationId.HasValue)
        {
            bottleLocation = await _bottleLocationRepository.GetByIdAsync(request.BottleLocationId.Value, cancellationToken);
            if (bottleLocation is null)
            {
                ModelState.AddModelError(nameof(request.BottleLocationId), "Bottle location was not found.");
                return ValidationProblem(ModelState);
            }
        }

        if (request.UserId.HasValue && request.UserId.Value != currentUserId)
        {
            ModelState.AddModelError(nameof(request.UserId), "You can only assign bottles to your account.");
            return ValidationProblem(ModelState);
        }

        bottle.UserId = currentUserId;
        bottle.Price = request.Price;
        bottle.IsDrunk = request.IsDrunk;
        bottle.DrunkAt = NormalizeDrunkAt(request.IsDrunk, request.DrunkAt);
        bottle.BottleLocationId = bottleLocation?.Id;

        await _bottleRepository.UpdateAsync(bottle, cancellationToken);

        var response = await BuildBottleGroupResponseAsync(bottle.WineVintageId, currentUserId, cancellationToken);
        return Json(response);
    }

    [HttpPost("notes")]
    public async Task<IActionResult> CreateNote([FromBody] TastingNoteCreateRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        var trimmedNote = request.Note?.Trim();
        var hasNote = !string.IsNullOrWhiteSpace(trimmedNote);
        var hasScore = request.Score.HasValue;

        if (!hasNote && !hasScore)
        {
            ModelState.AddModelError(nameof(request.Note), "Provide a tasting note or score.");
            return ValidationProblem(ModelState);
        }

        var noteToSave = hasNote ? trimmedNote! : string.Empty;

        var bottle = await _bottleRepository.GetByIdAsync(request.BottleId, cancellationToken);
        if (bottle is null)
        {
            ModelState.AddModelError(nameof(request.BottleId), "Bottle was not found.");
            return ValidationProblem(ModelState);
        }

        if (bottle.UserId != currentUserId)
        {
            return NotFound();
        }

        var currentUser = await _userRepository.GetByIdAsync(currentUserId, cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized("You must be signed in to add tasting notes.");
        }

        var existingNote = bottle.TastingNotes
            .Where(note => note.UserId == currentUserId)
            .OrderBy(note => note.Id)
            .LastOrDefault();

        if (existingNote is not null)
        {
            var updatedNote = new TastingNote
            {
                Id = existingNote.Id,
                BottleId = existingNote.BottleId,
                Note = noteToSave,
                Score = request.Score,
                UserId = existingNote.UserId
            };

            await _tastingNoteRepository.UpdateAsync(updatedNote, cancellationToken);
        }
        else
        {
            var entity = new TastingNote
            {
                Id = Guid.NewGuid(),
                BottleId = bottle.Id,
                Note = noteToSave,
                Score = request.Score,
                UserId = currentUser.Id
            };

            await _tastingNoteRepository.AddAsync(entity, cancellationToken);
        }

        var response = await BuildBottleNotesResponseAsync(bottle.Id, currentUserId, cancellationToken);
        if (response is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "Unable to load tasting notes after creation.");
        }

        return Json(response);
    }

    [HttpPut("notes/{noteId:guid}")]
    public async Task<IActionResult> UpdateNote(Guid noteId, [FromBody] TastingNoteUpdateRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        var trimmedNote = request.Note?.Trim();
        var hasNote = !string.IsNullOrWhiteSpace(trimmedNote);
        var hasScore = request.Score.HasValue;

        if (!hasNote && !hasScore)
        {
            ModelState.AddModelError(nameof(request.Note), "Provide a tasting note or score.");
            return ValidationProblem(ModelState);
        }

        var existing = await _tastingNoteRepository.GetByIdAsync(noteId, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        if (existing.Bottle?.UserId != currentUserId)
        {
            return NotFound();
        }

        if (existing.UserId != currentUserId)
        {
            return NotFound();
        }

        existing.Note = hasNote ? trimmedNote! : string.Empty;
        existing.Score = request.Score;

        await _tastingNoteRepository.UpdateAsync(existing, cancellationToken);

        var response = await BuildBottleNotesResponseAsync(existing.BottleId, currentUserId, cancellationToken);
        if (response is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "Unable to load tasting notes after update.");
        }

        return Json(response);
    }

    [HttpDelete("notes/{noteId:guid}")]
    public async Task<IActionResult> DeleteNote(Guid noteId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        var existing = await _tastingNoteRepository.GetByIdAsync(noteId, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        if (existing.Bottle?.UserId != currentUserId)
        {
            return NotFound();
        }

        if (existing.UserId != currentUserId)
        {
            return NotFound();
        }

        await _tastingNoteRepository.DeleteAsync(noteId, cancellationToken);

        var response = await BuildBottleNotesResponseAsync(existing.BottleId, currentUserId, cancellationToken);
        if (response is null)
        {
            return Json(new BottleNotesResponse
            {
                Bottle = null,
                Notes = Array.Empty<TastingNoteViewModel>()
            });
        }

        return Json(response);
    }

    [HttpDelete("bottles/{id:guid}")]
    public async Task<IActionResult> DeleteBottle(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        var bottle = await _bottleRepository.GetByIdAsync(id, cancellationToken);
        if (bottle is null || bottle.UserId != currentUserId)
        {
            return NotFound();
        }

        await _bottleRepository.DeleteAsync(id, cancellationToken);

        var response = await BuildBottleGroupResponseAsync(bottle.WineVintageId, currentUserId, cancellationToken);
        return Json(response);
    }

    private async Task<BottleGroupDetailsResponse> BuildBottleGroupResponseAsync(Guid wineVintageId, Guid userId, CancellationToken cancellationToken)
    {
        var userBottles = await _bottleRepository.GetForUserAsync(userId, cancellationToken);
        var userLocations = await GetUserLocationsAsync(userId, cancellationToken);
        var locationSummaries = BuildLocationSummaries(userBottles, userLocations);

        var ownedBottles = userBottles
            .Where(bottle => bottle.WineVintageId == wineVintageId)
            .ToList();

        if (!ownedBottles.Any())
        {
            return new BottleGroupDetailsResponse
            {
                Group = null,
                Details = Array.Empty<WineInventoryBottleDetailViewModel>(),
                Locations = locationSummaries
            };
        }

        var averageScore = CalculateAverageScore(ownedBottles);
        var summary = CreateBottleGroupViewModel(wineVintageId, ownedBottles, averageScore);
        var details = ownedBottles
            .OrderBy(b => b.IsDrunk)
            .ThenBy(b => b.DrunkAt ?? DateTime.MaxValue)
            .Select(b =>
            {
                var userNote = b.TastingNotes
                    .Where(note => note.UserId == userId)
                    .OrderBy(note => note.Id)
                    .LastOrDefault();

                return new WineInventoryBottleDetailViewModel
                {
                    BottleId = b.Id,
                    Price = b.Price,
                    IsDrunk = b.IsDrunk,
                    DrunkAt = b.DrunkAt,
                    BottleLocationId = b.BottleLocationId,
                    BottleLocation = b.BottleLocation?.Name ?? "—",
                    UserId = b.UserId,
                    UserName = b.User?.Name ?? string.Empty,
                    Vintage = b.WineVintage.Vintage,
                    WineName = b.WineVintage.Wine.Name,
                    AverageScore = CalculateAverageScore(b),
                    CurrentUserNoteId = userNote?.Id,
                    CurrentUserNote = userNote?.Note,
                    CurrentUserScore = userNote?.Score
                };
            })
            .ToList();

        return new BottleGroupDetailsResponse
        {
            Group = summary,
            Details = details,
            Locations = locationSummaries
        };
    }

    private async Task<List<BottleLocation>> GetUserLocationsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var bottleLocations = await _bottleLocationRepository.GetAllAsync(cancellationToken);
        return bottleLocations
            .Where(location => location.UserId == userId)
            .OrderBy(location => location.Name)
            .ToList();
    }

    private static List<WineInventoryLocationViewModel> BuildLocationSummaries(
        IEnumerable<Bottle> bottles,
        IEnumerable<BottleLocation> userLocations)
    {
        var bottlesByLocation = bottles
            .Where(bottle => bottle.BottleLocationId.HasValue)
            .GroupBy(bottle => bottle.BottleLocationId!.Value)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    BottleCount = group.Count(),
                    UniqueWineCount = group
                        .Select(bottle => bottle.WineVintageId)
                        .Distinct()
                        .Count(),
                    DrunkBottleCount = group.Count(bottle => bottle.IsDrunk)
                });

        return userLocations
            .Select(location =>
            {
                if (!bottlesByLocation.TryGetValue(location.Id, out var summary))
                {
                    return new WineInventoryLocationViewModel
                    {
                        Id = location.Id,
                        Name = location.Name,
                        Capacity = location.Capacity,
                        BottleCount = 0,
                        UniqueWineCount = 0,
                        CellaredBottleCount = 0,
                        DrunkBottleCount = 0
                    };
                }

                var cellaredCount = summary.BottleCount - summary.DrunkBottleCount;

                return new WineInventoryLocationViewModel
                {
                    Id = location.Id,
                    Name = location.Name,
                    Capacity = location.Capacity,
                    BottleCount = summary.BottleCount,
                    UniqueWineCount = summary.UniqueWineCount,
                    CellaredBottleCount = Math.Max(cellaredCount, 0),
                    DrunkBottleCount = summary.DrunkBottleCount
                };
            })
            .ToList();
    }

    private async Task<BottleNotesResponse?> BuildBottleNotesResponseAsync(Guid bottleId, Guid userId, CancellationToken cancellationToken)
    {
        var bottle = await _bottleRepository.GetByIdAsync(bottleId, cancellationToken);
        if (bottle is null || bottle.UserId != userId)
        {
            return null;
        }

        var notes = await _tastingNoteRepository.GetByBottleIdAsync(bottleId, cancellationToken);

        var noteViewModels = notes
            .Select(note => new TastingNoteViewModel
            {
                Id = note.Id,
                Note = note.Note,
                Score = note.Score,
                UserId = note.UserId,
                UserName = note.User?.Name ?? string.Empty
            })
            .ToList();

        var ownedGroupBottles = await _bottleRepository
            .GetByWineVintageIdAsync(bottle.WineVintageId, cancellationToken);

        var filteredGroupBottles = ownedGroupBottles
            .Where(groupBottle => groupBottle.UserId == userId)
            .ToList();

        var groupAverageScore = filteredGroupBottles.Count > 0
            ? CalculateAverageScore(filteredGroupBottles)
            : null;

        var bottleSummary = new BottleNotesBottleSummary
        {
            BottleId = bottle.Id,
            WineVintageId = bottle.WineVintageId,
            WineName = bottle.WineVintage.Wine.Name,
            Vintage = bottle.WineVintage.Vintage,
            BottleLocation = bottle.BottleLocation?.Name,
            UserId = bottle.UserId,
            UserName = bottle.User?.Name ?? string.Empty,
            IsDrunk = bottle.IsDrunk,
            DrunkAt = bottle.DrunkAt,
            BottleAverageScore = CalculateAverageScore(bottle),
            GroupAverageScore = groupAverageScore
        };

        return new BottleNotesResponse
        {
            Bottle = bottleSummary,
            Notes = noteViewModels
        };
    }

    private static WineInventoryBottleViewModel CreateBottleGroupViewModel(Guid groupId, IReadOnlyCollection<Bottle> bottles, decimal? averageScore)
    {
        var firstBottle = bottles.First();
        var totalCount = bottles.Count;
        var drunkCount = bottles.Count(b => b.IsDrunk);

        var (statusLabel, statusClass) = drunkCount switch
        {
            var d when d == 0 => ("Cellared", "cellared"),
            var d when d == totalCount => ("Drunk", "drunk"),
            _ => ("Mixed", "mixed")
        };

        return new WineInventoryBottleViewModel
        {
            WineVintageId = groupId,
            WineId = firstBottle.WineVintage.Wine.Id,
            WineName = firstBottle.WineVintage.Wine.Name,
            SubAppellation = firstBottle.WineVintage.Wine.SubAppellation?.Name,
            Appellation = firstBottle.WineVintage.Wine.SubAppellation?.Appellation?.Name,
            SubAppellationId = firstBottle.WineVintage.Wine.SubAppellation?.Id,
            AppellationId = firstBottle.WineVintage.Wine.SubAppellation?.Appellation?.Id,
            Vintage = firstBottle.WineVintage.Vintage,
            Color = firstBottle.WineVintage.Wine.Color.ToString(),
            BottleCount = totalCount,
            StatusLabel = statusLabel,
            StatusCssClass = statusClass,
            AverageScore = averageScore
        };
    }

    private static string BuildSubAppellationLabel(SubAppellation subAppellation)
    {
        var segments = new List<string>();

        if (!string.IsNullOrWhiteSpace(subAppellation.Name))
        {
            segments.Add(subAppellation.Name);
        }

        if (!string.IsNullOrWhiteSpace(subAppellation.Appellation?.Name))
        {
            segments.Add(subAppellation.Appellation!.Name);
        }

        if (!string.IsNullOrWhiteSpace(subAppellation.Appellation?.Region?.Name))
        {
            segments.Add(subAppellation.Appellation!.Region!.Name);
        }

        return segments.Count > 0 ? string.Join(" • ", segments) : "Unspecified";
    }

    private static decimal? CalculateAverageScore(IEnumerable<Bottle> bottles)
    {
        var scores = bottles
            .SelectMany(bottle => bottle.TastingNotes)
            .Select(note => note.Score)
            .Where(score => score.HasValue && score.Value > 0)
            .Select(score => score!.Value)
            .ToList();

        if (scores.Count == 0)
        {
            return null;
        }

        return decimal.Round((decimal)scores.Average(), 1, MidpointRounding.AwayFromZero);
    }

    private static decimal? CalculateAverageScore(Bottle bottle)
    {
        var scores = bottle.TastingNotes
            .Select(note => note.Score)
            .Where(score => score.HasValue && score.Value > 0)
            .Select(score => score!.Value)
            .ToList();

        if (scores.Count == 0)
        {
            return null;
        }

        return decimal.Round((decimal)scores.Average(), 1, MidpointRounding.AwayFromZero);
    }

    private Guid? GetCurrentUserId()
    {
        var idValue = _userManager.GetUserId(User);
        return Guid.TryParse(idValue, out var parsedId) ? parsedId : null;
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId.HasValue)
        {
            userId = currentUserId.Value;
            return true;
        }

        userId = Guid.Empty;
        return false;
    }

    private static DateTime? NormalizeDrunkAt(bool isDrunk, DateTime? drunkAt)
    {
        if (!isDrunk)
        {
            return null;
        }

        if (!drunkAt.HasValue)
        {
            return DateTime.UtcNow;
        }

        var value = drunkAt.Value;
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value;
    }

    private static string BuildWineSurferUserPrompt(string query)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Find wines that match the user's search text.");
        builder.Append("Search text: ");
        builder.AppendLine(query);
        builder.AppendLine("Return up to six likely matches sorted by relevance.");
        builder.AppendLine("Include the full wine name, country, region, appellation, and sub-appellation when available.");
        builder.AppendLine("Return the wine's primary color using Red, White, or Rose.");
        builder.AppendLine("Use null for any field you cannot determine.");
        return builder.ToString();
    }

    private static WineInventoryWineOption CreateWineOption(Wine wine, SubAppellation? fallbackSubAppellation = null)
    {
        var subAppellation = wine.SubAppellation ?? fallbackSubAppellation;
        var appellation = subAppellation?.Appellation;
        var region = appellation?.Region;
        var country = region?.Country;

        var vintages = wine.WineVintages?.Select(v => v.Vintage).Distinct().OrderByDescending(v => v).ToList()
            ?? new List<int>();

        return new WineInventoryWineOption
        {
            Id = wine.Id,
            Name = wine.Name,
            Color = wine.Color.ToString(),
            SubAppellation = NormalizeDisplayName(subAppellation?.Name),
            Appellation = NormalizeDisplayName(appellation?.Name),
            Region = NormalizeDisplayName(region?.Name),
            Country = NormalizeDisplayName(country?.Name),
            Vintages = vintages
        };
    }

    private static string? NormalizeDisplayName(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool AcceptsWineSurferStream(HttpRequest request)
    {
        if (request is null)
        {
            return false;
        }

        foreach (var header in request.Headers.Accept)
        {
            if (!string.IsNullOrWhiteSpace(header) && header.Contains(WineSurferStreamMediaType, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task StreamWineSurferMatchesAsync(string query, CancellationToken cancellationToken)
    {
        var response = Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = WineSurferStreamMediaType;
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";

        await response.StartAsync(cancellationToken);

        var builder = new StringBuilder();
        var hasPublishedMatches = false;
        IReadOnlyList<WineSurferLookupResult> lastPublishedMatches = Array.Empty<WineSurferLookupResult>();

        try
        {
            await foreach (var chunk in _chatGptService
                .StreamChatCompletionAsync(
                    new ChatMessage[]
                    {
                        new SystemChatMessage(WineSurferSystemPrompt),
                        new UserChatMessage(BuildWineSurferUserPrompt(query))
                    },
                    temperature: 0.2,
                    ct: cancellationToken)
                .WithCancellation(cancellationToken))
            {
                if (string.IsNullOrEmpty(chunk))
                {
                    continue;
                }

                builder.Append(chunk);
                await WriteWineSurferEventAsync(response, new { type = "delta", content = chunk }, cancellationToken);

                var snapshot = builder.ToString();
                if (WineSurferResultParser.TryParse(snapshot, out var matches)
                    && (!hasPublishedMatches || !MatchesEqual(matches, lastPublishedMatches)))
                {
                    await WriteWineSurferEventAsync(response, new { type = "matches", wines = matches }, cancellationToken);
                    lastPublishedMatches = matches;
                    hasPublishedMatches = true;
                }
            }

            if (!hasPublishedMatches)
            {
                var completionText = builder.ToString();
                if (WineSurferResultParser.TryParse(completionText, out var matches))
                {
                    await WriteWineSurferEventAsync(response, new { type = "matches", wines = matches }, cancellationToken);
                    lastPublishedMatches = matches;
                    hasPublishedMatches = true;
                }
                else
                {
                    await WriteWineSurferEventAsync(
                        response,
                        new { type = "error", message = WineSurferUnexpectedResponseMessage },
                        cancellationToken);
                    return;
                }
            }

            await WriteWineSurferEventAsync(
                response,
                new { type = "complete", wines = lastPublishedMatches },
                cancellationToken);
        }
        catch (ChatGptServiceNotConfiguredException)
        {
            await WriteWineSurferEventAsync(
                response,
                new { type = "error", message = WineSurferConfigurationErrorMessage },
                cancellationToken);
        }
        catch (ClientResultException)
        {
            await WriteWineSurferEventAsync(
                response,
                new { type = "error", message = WineSurferDiscoveryErrorMessage },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Request was aborted by the caller; do not emit an error payload.
        }
        catch (Exception)
        {
            await WriteWineSurferEventAsync(
                response,
                new { type = "error", message = WineSurferGenericErrorMessage },
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
                // Ignore flush failures caused by disconnected clients.
            }
        }
    }

    private async Task SendImmediateWineSurferResponseAsync(
        IReadOnlyList<WineSurferLookupResult> matches,
        CancellationToken cancellationToken)
    {
        var response = Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = WineSurferStreamMediaType;
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";

        await response.StartAsync(cancellationToken);
        await WriteWineSurferEventAsync(response, new { type = "matches", wines = matches }, cancellationToken);
        await WriteWineSurferEventAsync(response, new { type = "complete", wines = matches }, cancellationToken);

        try
        {
            await response.BodyWriter.FlushAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Ignore flush failures triggered by client disconnects.
        }
    }

    private static async Task WriteWineSurferEventAsync(HttpResponse response, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, WineSurferStreamSerializerOptions);
        var encoded = Encoding.UTF8.GetBytes(json + "\n");
        await response.BodyWriter.WriteAsync(encoded, cancellationToken);
        await response.BodyWriter.FlushAsync(cancellationToken);
    }

    private static bool MatchesEqual(
        IReadOnlyList<WineSurferLookupResult> left,
        IReadOnlyList<WineSurferLookupResult> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
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

}

public class WineInventoryViewModel
{
    public string Status { get; set; } = "all";
    public string? Color { get; set; }
    public string? Search { get; set; }
    public string SortField { get; set; } = "wine";
    public string SortDirection { get; set; } = "asc";
    public IReadOnlyList<WineInventoryBottleViewModel> Bottles { get; set; } = Array.Empty<WineInventoryBottleViewModel>();
    public Guid CurrentUserId { get; set; }
    public IReadOnlyList<WineInventoryLocationViewModel> Locations { get; set; } = Array.Empty<WineInventoryLocationViewModel>();
    public InventoryAddModalViewModel InventoryAddModal { get; set; } = new();
    public bool HasActiveFilters { get; set; }
    public IReadOnlySet<Guid> HighlightedLocationIds { get; set; } = new HashSet<Guid>();
    public IReadOnlyList<FilterOption> StatusOptions { get; set; } = Array.Empty<FilterOption>();
    public IReadOnlyList<FilterOption> ColorOptions { get; set; } = Array.Empty<FilterOption>();
}

public class WineInventoryBottleViewModel
{
    public Guid WineVintageId { get; set; }
    public Guid WineId { get; set; }
    public string WineName { get; set; } = string.Empty;
    public string? SubAppellation { get; set; }
    public string? Appellation { get; set; }
    public Guid? SubAppellationId { get; set; }
    public Guid? AppellationId { get; set; }
    public int Vintage { get; set; }
    public string Color { get; set; } = string.Empty;
    public int BottleCount { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public string StatusCssClass { get; set; } = string.Empty;
    public decimal? AverageScore { get; set; }
}

public class WineInventoryBottleDetailViewModel
{
    public Guid BottleId { get; set; }
    public decimal? Price { get; set; }
    public bool IsDrunk { get; set; }
    public DateTime? DrunkAt { get; set; }
    public Guid? BottleLocationId { get; set; }
    public string BottleLocation { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public int Vintage { get; set; }
    public string WineName { get; set; } = string.Empty;
    public decimal? AverageScore { get; set; }
    public Guid? CurrentUserNoteId { get; set; }
    public string? CurrentUserNote { get; set; }
    public decimal? CurrentUserScore { get; set; }
}

public class WineInventoryLocationViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? Capacity { get; set; }
    public int BottleCount { get; set; }
    public int UniqueWineCount { get; set; }
    public int CellaredBottleCount { get; set; }
    public int DrunkBottleCount { get; set; }
}

public class BottleGroupDetailsResponse
{
    public WineInventoryBottleViewModel? Group { get; set; }
    public IReadOnlyList<WineInventoryBottleDetailViewModel> Details { get; set; } = Array.Empty<WineInventoryBottleDetailViewModel>();
    public IReadOnlyList<WineInventoryLocationViewModel> Locations { get; set; } = Array.Empty<WineInventoryLocationViewModel>();
}

public class BottleMutationRequest
{
    [Required]
    public Guid WineVintageId { get; set; }

    public decimal? Price { get; set; }

    public bool IsDrunk { get; set; }

    public DateTime? DrunkAt { get; set; }

    public Guid? BottleLocationId { get; set; }

    public Guid? UserId { get; set; }

    [Range(1, 12)]
    public int Quantity { get; set; } = 1;
}

public record FilterOption(string Value, string Label);

public class WineGroupUpdateRequest
{
    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string WineName { get; set; } = string.Empty;

    [Range(1900, 2100)]
    public int Vintage { get; set; }

    [Required]
    public WineColor Color { get; set; }

    [Required]
    public Guid SubAppellationId { get; set; }
}

public class WineGroupCreateRequest : WineGroupUpdateRequest
{
    [Range(1, 5000)]
    public int InitialBottleCount { get; set; } = 1;

    public Guid? BottleLocationId { get; set; }
}

public class AddWineToInventoryRequest
{
    [Required]
    public Guid WineId { get; set; }

    [Range(1900, 2100)]
    public int Vintage { get; set; }

    [Range(1, 12)]
    public int Quantity { get; set; } = 1;

    public Guid? BottleLocationId { get; set; }
}

public class WineSurferAddWineRequest
{
    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(128)]
    public string? Country { get; set; }

    [StringLength(128)]
    public string? Region { get; set; }

    [StringLength(256)]
    public string? Appellation { get; set; }

    [StringLength(256)]
    public string? SubAppellation { get; set; }

    [StringLength(32)]
    public string? Color { get; set; }
}

public class CreateWineCatalogRequest
{
    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(32, MinimumLength = 1)]
    public string Color { get; set; } = string.Empty;

    [StringLength(128)]
    public string? Country { get; set; }

    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string Region { get; set; } = string.Empty;

    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string Appellation { get; set; } = string.Empty;

    [StringLength(256)]
    public string? SubAppellation { get; set; }

    [StringLength(256)]
    public string? GrapeVariety { get; set; }
}

public class WineCountrySuggestion
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class WineRegionSuggestion
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid CountryId { get; set; }
    public string CountryName { get; set; } = string.Empty;
}

public class WineAppellationSuggestion
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public Guid CountryId { get; set; }
    public string CountryName { get; set; } = string.Empty;
}

public class WineSubAppellationSuggestion
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid AppellationId { get; set; }
    public string AppellationName { get; set; } = string.Empty;
    public Guid RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public Guid CountryId { get; set; }
    public string CountryName { get; set; } = string.Empty;
}

public class InventoryReferenceDataResponse
{
    public IReadOnlyList<SubAppellationOption> SubAppellations { get; set; } = Array.Empty<SubAppellationOption>();
    public IReadOnlyList<BottleLocationOption> BottleLocations { get; set; } = Array.Empty<BottleLocationOption>();
    public IReadOnlyList<UserOption> Users { get; set; } = Array.Empty<UserOption>();
}

public class WineInventoryWineOption
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? SubAppellation { get; set; }
    public string? Appellation { get; set; }
    public string? Region { get; set; }
    public string? Country { get; set; }
    public string Color { get; set; } = string.Empty;
    public IReadOnlyList<int> Vintages { get; set; } = Array.Empty<int>();
}

public class WineImportPageViewModel
{
    public string? UploadedFileName { get; set; }
    public WineImportResult? Result { get; set; }
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
    public bool HasErrors => Errors.Count > 0;
    public bool HasResult => Result is not null;
}

public class BottleNotesResponse
{
    public BottleNotesBottleSummary? Bottle { get; set; }
    public IReadOnlyList<TastingNoteViewModel> Notes { get; set; } = Array.Empty<TastingNoteViewModel>();
}

public class BottleNotesBottleSummary
{
    public Guid BottleId { get; set; }
    public Guid WineVintageId { get; set; }
    public string WineName { get; set; } = string.Empty;
    public int Vintage { get; set; }
    public string? BottleLocation { get; set; }
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public bool IsDrunk { get; set; }
    public DateTime? DrunkAt { get; set; }
    public decimal? BottleAverageScore { get; set; }
    public decimal? GroupAverageScore { get; set; }
}

public class TastingNoteViewModel
{
    public Guid Id { get; set; }
    public string Note { get; set; } = string.Empty;
    public decimal? Score { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
}

public class TastingNoteUpdateRequest
{
    [StringLength(2048)]
    public string? Note { get; set; }

    [Range(0, 10)]
    public decimal? Score { get; set; }
}

public class TastingNoteCreateRequest : TastingNoteUpdateRequest
{
    [Required]
    public Guid BottleId { get; set; }
}

public class SubAppellationOption
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
}

public class UserOption
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
