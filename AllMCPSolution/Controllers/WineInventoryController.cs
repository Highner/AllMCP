using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;
using AllMCPSolution.Utilities;
using AllMCPSolution.Services;
using OpenAI.Chat;

namespace AllMCPSolution.Controllers;

[Authorize]
[Route("wine-manager")]
public partial class WineInventoryController : Controller
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
    private readonly IWineVintageUserDrinkingWindowRepository _drinkingWindowRepository;
    private readonly IWineCatalogService _wineCatalogService;
    private readonly IWineSurferTopBarService _topBarService;
    private readonly IWineImportService _wineImportService;
    private readonly IStarWineListImportService _starWineListImportService;
    private readonly ICellarTrackerImportService _cellarTrackerImportService;
    private readonly IChatGptService _chatGptService;
    private readonly IChatGptPromptService _chatGptPromptService;
    private readonly IUserDrinkingWindowService _userDrinkingWindowService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<WineInventoryController> _logger;



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
        IWineVintageUserDrinkingWindowRepository drinkingWindowRepository,
        ITastingNoteRepository tastingNoteRepository,
        IWineCatalogService wineCatalogService,
        IWineSurferTopBarService topBarService,
        IWineImportService wineImportService,
        IStarWineListImportService starWineListImportService,
        ICellarTrackerImportService cellarTrackerImportService,
        IChatGptService chatGptService,
        IChatGptPromptService chatGptPromptService,
        IUserDrinkingWindowService userDrinkingWindowService,
        UserManager<ApplicationUser> userManager,
        ILogger<WineInventoryController> logger)
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
        _drinkingWindowRepository = drinkingWindowRepository;
        _tastingNoteRepository = tastingNoteRepository;
        _wineCatalogService = wineCatalogService;
        _topBarService = topBarService;
        _wineImportService = wineImportService;
        _starWineListImportService = starWineListImportService;
        _cellarTrackerImportService = cellarTrackerImportService;
        _chatGptService = chatGptService;
        _chatGptPromptService = chatGptPromptService;
        _userDrinkingWindowService = userDrinkingWindowService ?? throw new ArgumentNullException(nameof(userDrinkingWindowService));
        _userManager = userManager;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] string? sortField,
        [FromQuery] string? sortDir,
        [FromQuery] string? locationId,
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

        var userLocations = await GetUserLocationsAsync(currentUserId, cancellationToken);
        var userLocationIds = userLocations
            .Select(location => location.Id)
            .ToHashSet();

        var userDrinkingWindows = await _drinkingWindowRepository
            .GetForUserAsync(currentUserId, cancellationToken);

        var drinkingWindowsByVintageId = userDrinkingWindows
            .GroupBy(window => window.WineVintageId)
            .ToDictionary(group => group.Key, group => group.First());

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

        int? GetDrinkingWindowStart(Bottle bottle) =>
            drinkingWindowsByVintageId.TryGetValue(bottle.WineVintageId, out var window)
                ? window.StartingYear
                : (int?)null;

        int? GetDrinkingWindowEnd(Bottle bottle) =>
            drinkingWindowsByVintageId.TryGetValue(bottle.WineVintageId, out var window)
                ? window.EndingYear
                : (int?)null;

        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim().ToLowerInvariant();
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        Guid? normalizedLocationId = null;
        if (!string.IsNullOrWhiteSpace(locationId)
            && Guid.TryParse(locationId, out var parsedLocationId)
            && userLocationIds.Contains(parsedLocationId))
        {
            normalizedLocationId = parsedLocationId;
        }
        var normalizedSortField = string.IsNullOrWhiteSpace(sortField) ? "wine" : sortField.Trim().ToLowerInvariant();
        var normalizedSortDir = string.IsNullOrWhiteSpace(sortDir) ? "asc" : sortDir.Trim().ToLowerInvariant();
        var hasActiveFilters = !string.Equals(normalizedStatus, "all", StringComparison.Ordinal)
                               || !string.IsNullOrWhiteSpace(normalizedSearch)
                               || normalizedLocationId.HasValue;

        IEnumerable<Bottle> query = bottles;

        query = normalizedStatus switch
        {
            "drunk" => query.Where(b => b.IsDrunk),
            "pending" => query.Where(b => b.PendingDelivery),
            "cellared" or "undrunk" => query.Where(b => !b.IsDrunk && !b.PendingDelivery),
            _ => query
        };

        if (normalizedLocationId.HasValue)
        {
            query = query.Where(b => b.BottleLocationId == normalizedLocationId);
        }

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(b =>
                (!string.IsNullOrEmpty(b.WineVintage.Wine.Name) &&
                 b.WineVintage.Wine.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)) ||
                (b.WineVintage.Wine.SubAppellation?.Name?.Contains(normalizedSearch,
                    StringComparison.OrdinalIgnoreCase) ?? false) ||
                (b.WineVintage.Wine.SubAppellation?.Appellation?.Name?.Contains(normalizedSearch,
                    StringComparison.OrdinalIgnoreCase) ?? false) ||
                (b.WineVintage.Wine.SubAppellation?.Appellation?.Region?.Name?.Contains(normalizedSearch,
                    StringComparison.OrdinalIgnoreCase) ?? false) ||
                b.WineVintage.Vintage.ToString(CultureInfo.InvariantCulture)
                    .Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase));
        }

        var descending = string.Equals(normalizedSortDir, "desc", StringComparison.OrdinalIgnoreCase);
        var filteredBottles = query.ToList();
        var sortSource = filteredBottles.AsEnumerable();

        static int GetStatusRank(Bottle bottle)
        {
            if (bottle.PendingDelivery)
            {
                return 0;
            }

            if (bottle.IsDrunk)
            {
                return 2;
            }

            return 1;
        }

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
            "region" => descending
                ? sortSource.OrderByDescending(b => b.WineVintage.Wine.SubAppellation?.Appellation?.Region?.Name)
                    .ThenByDescending(b => b.WineVintage.Wine.SubAppellation?.Appellation?.Name)
                    .ThenByDescending(b => b.WineVintage.Wine.SubAppellation?.Name)
                : sortSource.OrderBy(b => b.WineVintage.Wine.SubAppellation?.Appellation?.Region?.Name)
                    .ThenBy(b => b.WineVintage.Wine.SubAppellation?.Appellation?.Name)
                    .ThenBy(b => b.WineVintage.Wine.SubAppellation?.Name),
            "status" => descending
                ? sortSource.OrderByDescending(GetStatusRank)
                : sortSource.OrderBy(GetStatusRank),
            "score" => descending
                ? sortSource.OrderByDescending(b => GetAverageScore(b.WineVintageId))
                : sortSource.OrderBy(b => GetAverageScore(b.WineVintageId)),
            "drinking-window-start" => descending
                ? sortSource.OrderByDescending(GetDrinkingWindowStart)
                : sortSource.OrderBy(GetDrinkingWindowStart),
            "drinking-window-end" => descending
                ? sortSource.OrderByDescending(GetDrinkingWindowEnd)
                : sortSource.OrderBy(GetDrinkingWindowEnd),
            _ => descending
                ? sortSource.OrderByDescending(b => b.WineVintage.Wine.Name)
                : sortSource.OrderBy(b => b.WineVintage.Wine.Name)
        };

        var orderedWithTies = ordered
            .ThenBy(b => b.WineVintage.Wine.Name)
            .ThenBy(b => b.WineVintage.Vintage)
            .ThenBy(b => b.Id);

        // Aggregate by Wine (not by WineVintage) to produce a single row per wine
        var groupedBottles = orderedWithTies
            .GroupBy(b => b.WineVintage.Wine.Id)
            .Select(group =>
            {
                var bottlesInWine = group.ToList();
                var firstBottle = bottlesInWine.First();
                var totalCount = bottlesInWine.Count;
                var pendingCount = bottlesInWine.Count(b => b.PendingDelivery);
                var drunkCount = bottlesInWine.Count(b => b.IsDrunk);
                var cellaredCount = Math.Max(totalCount - pendingCount - drunkCount, 0);

                var (statusLabel, statusClass) = (pendingCount, cellaredCount, drunkCount) switch
                {
                    var (pending, _, drunk) when pending > 0 && drunk == 0 && cellaredCount == 0 => ("Pending", "pending"),
                    var (_, cellared, drunk) when cellared > 0 && drunk == 0 && pendingCount == 0 => ("Cellared", "cellared"),
                    var (_, _, drunk) when drunk == totalCount && totalCount > 0 => ("Drunk", "drunk"),
                    _ => ("Mixed", "mixed")
                };

                var windowStartYears = new List<int>();
                var windowEndYears = new List<int>();
                var windowAlignmentScores = new List<decimal>();
                var windowGeneratedDates = new List<DateTime>();

                var undrunkVintageIds = bottlesInWine
                    .Where(bottle => !bottle.IsDrunk && !bottle.PendingDelivery)
                    .Select(bottle => bottle.WineVintageId)
                    .Distinct();

                foreach (var vintageId in undrunkVintageIds)
                {
                    if (drinkingWindowsByVintageId.TryGetValue(vintageId, out var window))
                    {
                        windowStartYears.Add(window.StartingYear);
                        windowEndYears.Add(window.EndingYear);
                        windowAlignmentScores.Add(window.AlignmentScore);
                        if (window.GeneratedAtUtc.HasValue)
                        {
                            windowGeneratedDates.Add(window.GeneratedAtUtc.Value);
                        }
                    }
                }

                int? aggregatedWindowStart = windowStartYears.Count > 0
                    ? windowStartYears.Min()
                    : null;

                int? aggregatedWindowEnd = windowEndYears.Count > 0
                    ? windowEndYears.Min()
                    : null;

                decimal? aggregatedAlignmentScore = windowAlignmentScores.Count > 0
                    ? NormalizeAlignmentScore(windowAlignmentScores.Average())
                    : null;

                DateTime? aggregatedGeneratedAtUtc = windowGeneratedDates.Count > 0
                    ? windowGeneratedDates.Max()
                    : null;

                return new WineInventoryBottleViewModel
                {
                    WineVintageId = Guid.Empty, // no single vintage represents the wine-level row
                    WineId = firstBottle.WineVintage.Wine.Id,
                    WineName = firstBottle.WineVintage.Wine.Name,
                    Region = firstBottle.WineVintage.Wine.SubAppellation?.Appellation?.Region?.Name,
                    SubAppellation = firstBottle.WineVintage.Wine.SubAppellation?.Name,
                    Appellation = firstBottle.WineVintage.Wine.SubAppellation?.Appellation?.Name,
                    SubAppellationId = firstBottle.WineVintage.Wine.SubAppellation?.Id,
                    AppellationId = firstBottle.WineVintage.Wine.SubAppellation?.Appellation?.Id,
                    Vintage = 0, // displayed as — in the view
                    Color = firstBottle.WineVintage.Wine.Color.ToString(),
                    BottleCount = totalCount,
                    PendingBottleCount = pendingCount,
                    CellaredBottleCount = cellaredCount,
                    DrunkBottleCount = drunkCount,
                    StatusLabel = statusLabel,
                    StatusCssClass = statusClass,
                    AverageScore = CalculateAverageScore(bottlesInWine),
                    UserDrinkingWindowStartYear = aggregatedWindowStart,
                    UserDrinkingWindowEndYear = aggregatedWindowEnd,
                    UserDrinkingWindowAlignmentScore = aggregatedAlignmentScore,
                    DrinkingWindowGeneratedAtUtc = aggregatedGeneratedAtUtc
                };
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
            "region" => descending
                ? groupedBottles.OrderByDescending(b => b.Region)
                    .ThenByDescending(b => b.Appellation)
                    .ThenByDescending(b => b.SubAppellation)
                : groupedBottles.OrderBy(b => b.Region)
                    .ThenBy(b => b.Appellation)
                    .ThenBy(b => b.SubAppellation),
            "status" => descending
                ? groupedBottles.OrderByDescending(b => b.StatusLabel)
                : groupedBottles.OrderBy(b => b.StatusLabel),
            "score" => descending
                ? groupedBottles.OrderByDescending(b => b.AverageScore)
                : groupedBottles.OrderBy(b => b.AverageScore),
            "drinking-window-start" => descending
                ? groupedBottles.OrderByDescending(b => b.UserDrinkingWindowStartYear)
                : groupedBottles.OrderBy(b => b.UserDrinkingWindowStartYear),
            "drinking-window-end" => descending
                ? groupedBottles.OrderByDescending(b => b.UserDrinkingWindowEndYear)
                : groupedBottles.OrderBy(b => b.UserDrinkingWindowEndYear),
            _ => descending
                ? groupedBottles.OrderByDescending(b => b.WineName)
                : groupedBottles.OrderBy(b => b.WineName)
        };

        var items = orderedGroups
            .ThenBy(b => b.WineName)
            .ThenBy(b => b.Vintage)
            .ToList();

        var locationSummaries = BuildLocationSummaries(bottles, userLocations);

        var locationOptions = userLocations
            .Select(location => new BottleLocationOption
            {
                Id = location.Id,
                Name = location.Name,
                Capacity = location.Capacity
            })
            .ToList();

        var pendingBottleOptions = bottles
            .Where(bottle => bottle.PendingDelivery)
            .OrderBy(bottle => bottle.WineVintage?.Wine?.Name ?? string.Empty)
            .ThenBy(bottle => bottle.WineVintage?.Vintage ?? 0)
            .Select(bottle => new PendingBottleOption
            {
                BottleId = bottle.Id,
                WineVintageId = bottle.WineVintageId,
                WineName = bottle.WineVintage?.Wine?.Name ?? "Unknown wine",
                Vintage = bottle.WineVintage?.Vintage ?? 0,
                SubAppellation = bottle.WineVintage?.Wine?.SubAppellation?.Name,
                Appellation = bottle.WineVintage?.Wine?.SubAppellation?.Appellation?.Name,
                Region = bottle.WineVintage?.Wine?.SubAppellation?.Appellation?.Region?.Name,
                LocationName = bottle.BottleLocation?.Name
            })
            .ToList();

        var pendingBottleCount = pendingBottleOptions.Count;

        var acceptDeliveriesModal = new AcceptDeliveriesModalViewModel
        {
            PendingBottles = pendingBottleOptions,
            Locations = locationOptions
        };

        var highlightedLocationIds = hasActiveFilters
            ? filteredBottles
                .Where(bottle => bottle.BottleLocationId.HasValue
                                 && userLocationIds.Contains(bottle.BottleLocationId.Value))
                .Select(bottle => bottle.BottleLocationId!.Value)
                .ToHashSet()
            : new HashSet<Guid>();

        if (normalizedLocationId.HasValue
            && userLocationIds.Contains(normalizedLocationId.Value))
        {
            highlightedLocationIds.Add(normalizedLocationId.Value);
        }

        var viewModel = new WineInventoryViewModel
        {
            Status = normalizedStatus,
            Search = normalizedSearch,
            SortField = normalizedSortField,
            SortDirection = descending ? "desc" : "asc",
            Bottles = items,
            CurrentUserId = currentUserId,
            Locations = locationSummaries,
            InventoryAddModal = new InventoryAddModalViewModel
            {
                Locations = locationOptions
            },
            AcceptDeliveriesModal = acceptDeliveriesModal,
            PendingBottleCount = pendingBottleCount,
            HasActiveFilters = hasActiveFilters,
            HighlightedLocationIds = highlightedLocationIds,
            LocationFilterId = normalizedLocationId,
            StatusOptions = new List<FilterOption>
            {
                new("all", "All Bottles"),
                new("pending", "Pending delivery"),
                new("cellared", "Cellared"),
                new("drunk", "Drunk")
            }
        };

        Response.ContentType = "text/html; charset=utf-8";
        return View("Index", viewModel);
    }

    [HttpGet("import")]
    public async Task<IActionResult> Import(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        var currentPath = HttpContext?.Request?.Path.Value ?? string.Empty;
        ViewData["WineSurferTopBarModel"] = await _topBarService.BuildAsync(User, currentPath, cancellationToken);
        await SetInventoryAddModalViewDataAsync(currentUserId, cancellationToken);

        var viewModel = new WineImportPageViewModel();
        return View("Import", viewModel);
    }

    [HttpPost("import")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(
        string importType = "wines",
        IFormFile? wineFile = null,
        IFormFile? bottleFile = null,
        IFormFile? starListFile = null,
        IFormFile? cellarTrackerFile = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        var currentPath = HttpContext?.Request?.Path.Value ?? string.Empty;
        ViewData["WineSurferTopBarModel"] = await _topBarService.BuildAsync(User, currentPath, cancellationToken);
        await SetInventoryAddModalViewDataAsync(currentUserId, cancellationToken);

        var viewModel = new WineImportPageViewModel();

        var normalizedType = importType?.Trim().ToLowerInvariant();
        var isBottleImport = string.Equals(normalizedType, "bottles", StringComparison.Ordinal);
        var isStarWineListImport = string.Equals(normalizedType, "starwinelist", StringComparison.Ordinal);
        var isCellarTrackerImport = string.Equals(normalizedType, "cellartracker", StringComparison.Ordinal);

        if (isStarWineListImport)
        {
            await HandleStarWineListImportAsync(
                starListFile,
                viewModel,
                cancellationToken);
        }
        else if (isCellarTrackerImport)
        {
            await HandleCellarTrackerImportAsync(
                cellarTrackerFile,
                viewModel,
                cancellationToken);
        }
        else if (isBottleImport)
        {
            await HandleBottleImportAsync(bottleFile, currentUserId, viewModel, cancellationToken);
        }
        else
        {
            await HandleWineImportAsync(wineFile, viewModel, cancellationToken);
        }

        return View("Import", viewModel);
    }

    private async Task HandleWineImportAsync(
        IFormFile? file,
        WineImportPageViewModel viewModel,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            viewModel.WineUpload.Errors = new[] { "Please select an Excel file to upload." };
            return;
        }

        var extension = Path.GetExtension(file.FileName);
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".xls", StringComparison.OrdinalIgnoreCase))
        {
            viewModel.WineUpload.Errors = new[] { "Unsupported file type. Please upload an .xlsx or .xls file." };
            return;
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _wineImportService.ImportAsync(stream, cancellationToken);
            viewModel.WineUpload.Result = result;
            viewModel.WineUpload.UploadedFileName = file.FileName;
        }
        catch (InvalidDataException ex)
        {
            viewModel.WineUpload.Errors = new[] { ex.Message };
        }
        catch (Exception ex)
        {
            viewModel.WineUpload.Errors = new[] { $"An unexpected error occurred while importing wines: {ex.Message}" };
        }
    }

    private async Task HandleBottleImportAsync(
        IFormFile? file,
        Guid? currentUserId,
        WineImportPageViewModel viewModel,
        CancellationToken cancellationToken)
    {
        viewModel.BottleUpload.SelectedCountry = null;
        viewModel.BottleUpload.SelectedRegion = null;
        viewModel.BottleUpload.SelectedColor = null;

        if (file is null || file.Length == 0)
        {
            viewModel.BottleUpload.Errors = new[] { "Please select an Excel file to upload." };
            return;
        }

        var extension = Path.GetExtension(file.FileName);
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".xls", StringComparison.OrdinalIgnoreCase))
        {
            viewModel.BottleUpload.Errors = new[] { "Unsupported file type. Please upload an .xlsx or .xls file." };
            return;
        }

        if (!currentUserId.HasValue || currentUserId.Value == Guid.Empty)
        {
            viewModel.BottleUpload.Errors = new[] { "You must be signed in to import bottles." };
            return;
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var preview = await _wineImportService.PreviewBottleImportAsync(stream, cancellationToken);
            viewModel.BottleUpload.UploadedFileName = file.FileName;

            if (preview.TotalRows > 0 || preview.RowErrors.Count > 0)
            {
                var result = new WineImportResult
                {
                    TotalRows = preview.TotalRows
                };

                foreach (var error in preview.RowErrors)
                {
                    result.RowErrors.Add(error);
                }

                viewModel.BottleUpload.Result = result;
            }

            viewModel.BottleUpload.PreviewRows = preview.Rows
                .Select(row => new WineImportPreviewRowViewModel
                {
                    RowId = $"row-{row.RowNumber.ToString(CultureInfo.InvariantCulture)}",
                    RowNumber = row.RowNumber,
                    Name = row.Name,
                    Country = row.Country,
                    Region = row.Region,
                    Appellation = row.Appellation,
                    SubAppellation = row.SubAppellation,
                    GrapeVariety = row.GrapeVariety ?? string.Empty,
                    Color = row.Color,
                    Amount = row.Amount,
                    WineExists = row.WineExists,
                    CountryExists = row.CountryExists,
                    RegionExists = row.RegionExists,
                    AppellationExists = row.AppellationExists,
                    HasBottleDetails = row.Amount > 0,
                    IsConsumed = row.IsConsumed,
                    ConsumptionDate = row.ConsumptionDate,
                    ConsumptionScore = row.ConsumptionScore,
                    ConsumptionNote = row.ConsumptionNote ?? string.Empty
                })
                .ToList();
        }
        catch (InvalidDataException ex)
        {
            viewModel.BottleUpload.Errors = new[] { ex.Message };
        }
        catch (Exception ex)
        {
            viewModel.BottleUpload.Errors = new[]
                { $"An unexpected error occurred while importing bottles: {ex.Message}" };
        }
    }

    [HttpPost("import/wines/bulk")]
    public async Task<IActionResult> ImportReadyWines(
        [FromBody] ImportReadyWinesRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out _))
        {
            return Challenge();
        }

        if (request?.Rows is null || request.Rows.Count == 0)
        {
            return BadRequest(new { message = "No wines were provided for import." });
        }

        var response = new ImportReadyWinesResponse
        {
            TotalRequested = request.Rows.Count
        };

        var ensuredCountries = new Dictionary<string, Country>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in request.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resultEntry = new ImportReadyWineRowResult
            {
                RowId = row?.RowId ?? string.Empty,
                RowNumber = row?.RowNumber ?? 0
            };

            if (row is null)
            {
                resultEntry.Error = "Row data is missing.";
                response.Rows.Add(resultEntry);
                response.Errors.Add("Encountered an empty row while importing wines.");
                response.Failed++;
                continue;
            }

            var missingFields = new List<string>();

            var name = row.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                missingFields.Add("Name");
            }

            var color = row.Color?.Trim();
            if (string.IsNullOrWhiteSpace(color))
            {
                missingFields.Add("Color");
            }

            var region = row.Region?.Trim();
            if (string.IsNullOrWhiteSpace(region))
            {
                missingFields.Add("Region");
            }

            var appellation = row.Appellation?.Trim();
            if (string.IsNullOrWhiteSpace(appellation))
            {
                missingFields.Add("Appellation");
            }

            if (missingFields.Count > 0)
            {
                resultEntry.Error = $"Missing required fields: {string.Join(", ", missingFields)}.";
                response.Rows.Add(resultEntry);
                response.Failed++;
                continue;
            }

            var isConsumed = row.IsConsumed;
            var consumptionDate = row.ConsumptionDate;
            var consumptionScore = row.ConsumptionScore;
            var consumptionNote = string.IsNullOrWhiteSpace(row.ConsumptionNote)
                ? null
                : row.ConsumptionNote!.Trim();

            try
            {
                Country? ensuredCountry = null;
                var country = row.Country?.Trim();
                if (!string.IsNullOrWhiteSpace(country))
                {
                    if (!ensuredCountries.TryGetValue(country, out ensuredCountry))
                    {
                        try
                        {
                            var existingCountry = await _countryRepository.FindByNameAsync(country, cancellationToken);
                            if (existingCountry is not null)
                            {
                                ensuredCountry = existingCountry;
                            }
                            else
                            {
                                ensuredCountry = await _countryRepository.GetOrCreateAsync(country, cancellationToken);
                                response.CreatedCountries++;
                                resultEntry.CountryCreated = true;
                            }

                            if (ensuredCountry is not null)
                            {
                                ensuredCountries[country] = ensuredCountry;
                            }
                        }
                        catch (Exception ex)
                        {
                            resultEntry.Error = $"Unable to ensure country '{country}': {ex.Message}";
                            response.Rows.Add(resultEntry);
                            response.Failed++;
                            continue;
                        }
                    }
                }

                var catalogRequest = new WineCatalogRequest(
                    name!,
                    color!,
                    ensuredCountry?.Name ?? row.Country,
                    region!,
                    appellation!,
                    row.SubAppellation,
                    row.GrapeVariety);

                var catalogResult = await _wineCatalogService.EnsureWineAsync(catalogRequest, cancellationToken);

                if (!catalogResult.IsSuccess || catalogResult.Wine is null)
                {
                    var formattedError = FormatCatalogErrors(catalogResult.Errors)
                                        ?? "Unable to add wine to the catalog.";
                    resultEntry.Error = formattedError;
                    response.Rows.Add(resultEntry);
                    response.Failed++;
                    continue;
                }

                if (catalogResult.Created)
                {
                    resultEntry.Created = true;
                    response.Created++;
                }
                else
                {
                    resultEntry.AlreadyExists = true;
                    response.AlreadyExists++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                resultEntry.Error = $"Unable to import wine: {ex.Message}";
                response.Rows.Add(resultEntry);
                response.Failed++;
                continue;
            }

            response.Rows.Add(resultEntry);
        }

        return Json(response);
    }

    [HttpPost("import/cellartracker/inventory")]
    public async Task<IActionResult> ImportCellarTrackerInventory(
        [FromBody] ImportCellarTrackerInventoryRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        if (currentUserId == Guid.Empty)
        {
            return Challenge();
        }

        if (request?.Rows is null || request.Rows.Count == 0)
        {
            return BadRequest(new { message = "No rows were provided for inventory import." });
        }

        var response = new ImportCellarTrackerInventoryResponse
        {
            TotalRequested = request.Rows.Count
        };

        var ensuredCountries = new Dictionary<string, Country>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in request.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resultEntry = new ImportCellarTrackerInventoryRowResult
            {
                RowId = row?.RowId ?? string.Empty,
                RowNumber = row?.RowNumber ?? 0
            };

            if (row is null)
            {
                resultEntry.Error = "Row data is missing.";
                response.Rows.Add(resultEntry);
                response.Errors.Add("Encountered an empty row while importing inventory.");
                response.Failed++;
                continue;
            }

            var missingFields = new List<string>();

            var name = row.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                missingFields.Add("Name");
            }

            var color = row.Color?.Trim();
            if (string.IsNullOrWhiteSpace(color))
            {
                missingFields.Add("Color");
            }

            var region = row.Region?.Trim();
            if (string.IsNullOrWhiteSpace(region))
            {
                missingFields.Add("Region");
            }

            var appellation = row.Appellation?.Trim();
            if (string.IsNullOrWhiteSpace(appellation))
            {
                missingFields.Add("Appellation");
            }

            var vintage = row.Vintage;
            if (!vintage.HasValue)
            {
                missingFields.Add("Vintage");
            }

            var quantity = row.Quantity;
            if (quantity <= 0)
            {
                missingFields.Add("Quantity");
            }

            if (missingFields.Count > 0)
            {
                resultEntry.Error = $"Missing required fields: {string.Join(", ", missingFields)}.";
                response.Rows.Add(resultEntry);
                response.Failed++;
                continue;
            }

            var isConsumed = row.IsConsumed;
            var consumptionDate = row.ConsumptionDate;
            var consumptionScore = row.ConsumptionScore;
            var consumptionNote = string.IsNullOrWhiteSpace(row.ConsumptionNote)
                ? null
                : row.ConsumptionNote!.Trim();

            try
            {
                Country? ensuredCountry = null;
                var country = row.Country?.Trim();
                if (!string.IsNullOrWhiteSpace(country))
                {
                    if (!ensuredCountries.TryGetValue(country, out ensuredCountry))
                    {
                        try
                        {
                            var existingCountry = await _countryRepository.FindByNameAsync(country, cancellationToken);
                            if (existingCountry is not null)
                            {
                                ensuredCountry = existingCountry;
                            }
                            else
                            {
                                ensuredCountry = await _countryRepository.GetOrCreateAsync(country, cancellationToken);
                                response.CreatedCountries++;
                                resultEntry.CountryCreated = true;
                            }

                            if (ensuredCountry is not null)
                            {
                                ensuredCountries[country] = ensuredCountry;
                            }
                        }
                        catch (Exception ex)
                        {
                            resultEntry.Error = $"Unable to ensure country '{country}': {ex.Message}";
                            response.Rows.Add(resultEntry);
                            response.Failed++;
                            continue;
                        }
                    }
                }

                var catalogRequest = new WineCatalogRequest(
                    name!,
                    color!,
                    ensuredCountry?.Name ?? row.Country,
                    region!,
                    appellation!,
                    row.SubAppellation,
                    row.GrapeVariety);

                var catalogResult = await _wineCatalogService.EnsureWineAsync(catalogRequest, cancellationToken);

                if (!catalogResult.IsSuccess || catalogResult.Wine is null)
                {
                    var formattedError = FormatCatalogErrors(catalogResult.Errors)
                                        ?? "Unable to add wine to the catalog.";
                    resultEntry.Error = formattedError;
                    response.Rows.Add(resultEntry);
                    response.Failed++;
                    continue;
                }

                var wine = catalogResult.Wine;
                if (catalogResult.Created)
                {
                    resultEntry.WineCreated = true;
                    response.WinesCreated++;
                }
                else
                {
                    resultEntry.WineAlreadyExisted = true;
                    response.WinesAlreadyExisting++;
                }

                var vintageValue = vintage!.Value;
                var wineVintage = await _wineVintageRepository.GetOrCreateAsync(wine.Id, vintageValue, cancellationToken);
                resultEntry.WineId = wine.Id;
                resultEntry.WineVintageId = wineVintage.Id;

                for (var i = 0; i < quantity; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var bottle = new Bottle
                    {
                        Id = Guid.NewGuid(),
                        WineVintageId = wineVintage.Id,
                        IsDrunk = isConsumed,
                        DrunkAt = isConsumed ? consumptionDate : null,
                        Price = null,
                        PendingDelivery = false,
                        BottleLocationId = null,
                        UserId = currentUserId
                    };

                    await _bottleRepository.AddAsync(bottle, cancellationToken);

                    if (isConsumed && (consumptionScore.HasValue || !string.IsNullOrWhiteSpace(consumptionNote)))
                    {
                        var noteText = string.IsNullOrWhiteSpace(consumptionNote) ? string.Empty : consumptionNote!;
                        var tastingNote = new TastingNote
                        {
                            Id = Guid.NewGuid(),
                            BottleId = bottle.Id,
                            UserId = currentUserId,
                            Score = consumptionScore,
                            Note = noteText
                        };

                        await _tastingNoteRepository.AddAsync(tastingNote, cancellationToken);
                    }
                }

                resultEntry.BottlesAdded = quantity;
                response.BottlesAdded += quantity;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                resultEntry.Error = $"Unable to add bottles: {ex.Message}";
                response.Rows.Add(resultEntry);
                response.Failed++;
                continue;
            }

            response.Rows.Add(resultEntry);
        }

        return Json(response);
    }

    private async Task HandleStarWineListImportAsync(
        IFormFile? file,
        WineImportPageViewModel viewModel,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        viewModel.BottleUpload.SelectedCountry = null;
        viewModel.BottleUpload.SelectedRegion = null;
        viewModel.BottleUpload.SelectedColor = null;
        viewModel.BottleUpload.Result = null;

        if (file is null || file.Length == 0)
        {
            errors.Add("Please select an HTML file to upload.");
        }
        else
        {
            var extension = Path.GetExtension(file.FileName);
            if (!string.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".htm", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Unsupported file type. Please upload an .html file.");
            }
        }

        if (errors.Count > 0)
        {
            viewModel.BottleUpload.Errors = errors;
            viewModel.BottleUpload.PreviewRows = Array.Empty<WineImportPreviewRowViewModel>();
            viewModel.BottleUpload.UploadedFileName = file?.FileName;
            return;
        }

        try
        {
            await using var stream = file!.OpenReadStream();
            var parseResult = await _starWineListImportService
                .ParseAsync(stream, cancellationToken);

            var trimmedCountry = string.IsNullOrWhiteSpace(parseResult.Country)
                ? null
                : parseResult.Country.Trim();
            var trimmedRegion = string.IsNullOrWhiteSpace(parseResult.Region)
                ? null
                : parseResult.Region.Trim();

            viewModel.BottleUpload.SelectedCountry = trimmedCountry;
            viewModel.BottleUpload.SelectedRegion = trimmedRegion;

            if (trimmedCountry is null)
            {
                errors.Add("We could not determine the country from the uploaded Star Wine List file.");
            }

            if (trimmedRegion is null)
            {
                errors.Add("We could not determine the region from the uploaded Star Wine List file.");
            }

            if (errors.Count > 0)
            {
                viewModel.BottleUpload.Errors = errors;
                viewModel.BottleUpload.PreviewRows = Array.Empty<WineImportPreviewRowViewModel>();
                viewModel.BottleUpload.UploadedFileName = file.FileName;
                return;
            }

            if (parseResult.Producers.Count == 0)
            {
                viewModel.BottleUpload.Errors = new[]
                    { "No producers were found in the uploaded Star Wine List file." };
                viewModel.BottleUpload.PreviewRows = Array.Empty<WineImportPreviewRowViewModel>();
                viewModel.BottleUpload.UploadedFileName = file.FileName;
                return;
            }

            var existingCountry = await _countryRepository
                .FindByNameAsync(trimmedCountry!, cancellationToken);
            var countryEntity = existingCountry ?? await _countryRepository
                .GetOrCreateAsync(trimmedCountry!, cancellationToken);

            var existingRegion = await _regionRepository
                .FindByNameAndCountryAsync(trimmedRegion!, countryEntity.Id, cancellationToken);
            var regionEntity = existingRegion ?? await _regionRepository
                .GetOrCreateAsync(trimmedRegion!, countryEntity, cancellationToken);

            // Define fuzzy matching thresholds similar to WineImportService
            const double maxNameDistance = 0.2d;
            const double maxHierarchyDistance = 0.15d;

            bool MatchesImportRow(string sourceName, string? sourceAppellation, Wine candidate)
            {
                if (string.IsNullOrWhiteSpace(sourceName))
                {
                    return false;
                }

                var nameDistance = FuzzyMatchUtilities.CalculateNormalizedDistance(sourceName, candidate.Name);
                if (nameDistance > maxNameDistance)
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(sourceAppellation))
                {
                    var candidateAppellation = candidate.SubAppellation?.Appellation?.Name;
                    if (string.IsNullOrWhiteSpace(candidateAppellation))
                    {
                        return false;
                    }

                    var distance = FuzzyMatchUtilities.CalculateNormalizedDistance(sourceAppellation!, candidateAppellation);
                    return distance <= maxHierarchyDistance;
                }

                return true;
            }

            var rows = new List<WineImportPreviewRowViewModel>();
            for (var i = 0; i < parseResult.Producers.Count; i++)
            {
                var producer = parseResult.Producers[i];

                // Check for existing appellation under the detected region
                var existingAppellation = string.IsNullOrWhiteSpace(producer.Appellation)
                    ? null
                    : await _appellationRepository.FindByNameAndRegionAsync(producer.Appellation.Trim(), regionEntity.Id, cancellationToken);

                // Find closest wine name matches and evaluate
                var matches = await _wineRepository.FindClosestMatchesAsync(producer.Name, 5, cancellationToken);
                var wineExists = matches.Any(m => MatchesImportRow(producer.Name, producer.Appellation, m));

                rows.Add(new WineImportPreviewRowViewModel
                {
                    RowId = $"star-{i + 1}",
                    RowNumber = i + 1,
                    Name = producer.Name,
                    Country = trimmedCountry!,
                    Region = trimmedRegion!,
                    Appellation = producer.Appellation,
                    SubAppellation = string.Empty,
                    GrapeVariety = string.Empty,
                    Color = string.Empty,
                    Amount = 1,
                    WineExists = wineExists,
                    CountryExists = existingCountry is not null,
                    RegionExists = existingRegion is not null,
                    AppellationExists = existingAppellation is not null
                });
            }

            viewModel.BottleUpload.Errors = Array.Empty<string>();
            viewModel.BottleUpload.PreviewRows = rows;
            viewModel.BottleUpload.UploadedFileName = file.FileName;
        }
        catch (InvalidDataException ex)
        {
            viewModel.BottleUpload.Errors = new[] { ex.Message };
            viewModel.BottleUpload.PreviewRows = Array.Empty<WineImportPreviewRowViewModel>();
            viewModel.BottleUpload.UploadedFileName = file?.FileName;
        }
        catch (Exception ex)
        {
            viewModel.BottleUpload.Errors = new[]
            {
                $"An unexpected error occurred while processing the Star Wine List file: {ex.Message}"
            };
            viewModel.BottleUpload.PreviewRows = Array.Empty<WineImportPreviewRowViewModel>();
            viewModel.BottleUpload.UploadedFileName = file?.FileName;
        }
    }

    private async Task HandleCellarTrackerImportAsync(
        IFormFile? file,
        WineImportPageViewModel viewModel,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        viewModel.BottleUpload.SelectedCountry = null;
        viewModel.BottleUpload.SelectedRegion = null;
        viewModel.BottleUpload.SelectedColor = null;
        viewModel.BottleUpload.Result = null;

        if (file is null || file.Length == 0)
        {
            errors.Add("Please select an HTML file to upload.");
        }
        else
        {
            var extension = Path.GetExtension(file.FileName);
            if (!string.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".htm", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Unsupported file type. Please upload an .html file.");
            }
        }

        if (errors.Count > 0)
        {
            viewModel.BottleUpload.Errors = errors;
            viewModel.BottleUpload.PreviewRows = Array.Empty<WineImportPreviewRowViewModel>();
            viewModel.BottleUpload.UploadedFileName = file?.FileName;
            return;
        }

        try
        {
            await using var stream = file!.OpenReadStream();
            var parseResult = await _cellarTrackerImportService
                .ParseAsync(stream, cancellationToken);

            var trimmedCountry = string.IsNullOrWhiteSpace(parseResult.Country)
                ? null
                : parseResult.Country.Trim();
            var trimmedRegion = string.IsNullOrWhiteSpace(parseResult.Region)
                ? null
                : parseResult.Region.Trim();
            var trimmedColor = string.IsNullOrWhiteSpace(parseResult.Color)
                ? null
                : parseResult.Color.Trim();

            viewModel.BottleUpload.SelectedCountry = trimmedCountry;
            viewModel.BottleUpload.SelectedRegion = trimmedRegion;
            viewModel.BottleUpload.SelectedColor = trimmedColor;

            Region? existingRegion = null;
            Country? existingCountry = null;
            Region? regionEntity = null;
            Country? countryEntity = null;

            if (trimmedRegion is not null)
            {
                existingRegion = await _regionRepository.FindByNameAsync(trimmedRegion, cancellationToken);
                existingCountry = existingRegion?.Country;
                if (viewModel.BottleUpload.SelectedCountry is null
                    && !string.IsNullOrWhiteSpace(existingCountry?.Name))
                {
                    viewModel.BottleUpload.SelectedCountry = existingCountry!.Name;
                }
            }

            if (trimmedCountry is null
                && existingCountry is null)
            {
                errors.Add("We could not determine the country from the uploaded CellarTracker file.");
            }

            if (trimmedRegion is null)
            {
                errors.Add("We could not determine the region from the uploaded CellarTracker file.");
            }

            if (trimmedColor is null)
            {
                errors.Add("We could not determine the color from the uploaded CellarTracker file.");
            }

            if (errors.Count > 0)
            {
                viewModel.BottleUpload.Errors = errors;
                viewModel.BottleUpload.PreviewRows = Array.Empty<WineImportPreviewRowViewModel>();
                viewModel.BottleUpload.UploadedFileName = file.FileName;
                return;
            }

            if (trimmedCountry is not null)
            {
                countryEntity = existingCountry
                    ?? await _countryRepository.FindByNameAsync(trimmedCountry, cancellationToken)
                    ?? await _countryRepository.GetOrCreateAsync(trimmedCountry, cancellationToken);
            }
            else if (existingCountry is not null)
            {
                countryEntity = existingCountry;
            }

            if (trimmedRegion is not null)
            {
                if (existingRegion is not null)
                {
                    regionEntity = existingRegion;
                }
                else if (countryEntity is not null)
                {
                    regionEntity = await _regionRepository
                        .FindByNameAndCountryAsync(trimmedRegion, countryEntity.Id, cancellationToken)
                        ?? await _regionRepository.GetOrCreateAsync(trimmedRegion, countryEntity, cancellationToken);
                }
                else
                {
                    regionEntity = await _regionRepository.FindByNameAsync(trimmedRegion, cancellationToken);
                }
            }

            countryEntity ??= existingCountry;
            regionEntity ??= existingRegion;

            if (viewModel.BottleUpload.SelectedCountry is null
                && !string.IsNullOrWhiteSpace(countryEntity?.Name))
            {
                viewModel.BottleUpload.SelectedCountry = countryEntity!.Name;
            }

            if (parseResult.Wines.Count == 0)
            {
                viewModel.BottleUpload.Errors = new[]
                    { "No wines were found in the uploaded CellarTracker file." };
                viewModel.BottleUpload.PreviewRows = Array.Empty<WineImportPreviewRowViewModel>();
                viewModel.BottleUpload.UploadedFileName = file.FileName;
                return;
            }

            WineColor? parsedColor = null;
            if (trimmedColor is not null
                && Enum.TryParse<WineColor>(trimmedColor, true, out var wineColor))
            {
                parsedColor = wineColor;
            }

            const double maxNameDistance = 0.2d;
            const double maxHierarchyDistance = 0.15d;

            bool MatchesImportRow(CellarTrackerWine wine, Wine candidate)
            {
                if (string.IsNullOrWhiteSpace(wine.Name))
                {
                    return false;
                }

                var nameDistance = FuzzyMatchUtilities.CalculateNormalizedDistance(wine.Name, candidate.Name);
                if (nameDistance > maxNameDistance)
                {
                    return false;
                }

                if (parsedColor.HasValue && candidate.Color != parsedColor.Value)
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(wine.Appellation))
                {
                    var candidateAppellation = candidate.SubAppellation?.Appellation?.Name;
                    if (string.IsNullOrWhiteSpace(candidateAppellation))
                    {
                        return false;
                    }

                    var distance = FuzzyMatchUtilities.CalculateNormalizedDistance(wine.Appellation!, candidateAppellation);
                    return distance <= maxHierarchyDistance;
                }

                return true;
            }

            var rows = new List<WineImportPreviewRowViewModel>();

            for (var i = 0; i < parseResult.Wines.Count; i++)
            {
                var wine = parseResult.Wines[i];
                var trimmedName = wine.Name?.Trim();
                if (string.IsNullOrWhiteSpace(trimmedName))
                {
                    continue;
                }

                Appellation? existingAppellation = null;
                if (regionEntity is not null && !string.IsNullOrWhiteSpace(wine.Appellation))
                {
                    existingAppellation = await _appellationRepository
                        .FindByNameAndRegionAsync(wine.Appellation.Trim(), regionEntity.Id, cancellationToken);
                }

                var matches = await _wineRepository.FindClosestMatchesAsync(trimmedName, 5, cancellationToken);
                var wineExists = matches.Any(candidate => MatchesImportRow(wine, candidate));

                var regionText = trimmedRegion ?? string.Empty;
                var colorText = trimmedColor ?? string.Empty;
                var countryText = viewModel.BottleUpload.SelectedCountry ?? string.Empty;

                var rowNumber = rows.Count + 1;

                var row = new WineImportPreviewRowViewModel
                {
                    RowId = $"cellar-{rowNumber}",
                    RowNumber = rowNumber,
                    Name = trimmedName,
                    Country = countryText,
                    Region = regionText,
                    Appellation = wine.Appellation,
                    SubAppellation = string.Empty,
                    GrapeVariety = wine.Variety ?? string.Empty,
                    Color = colorText,
                    Amount = wine.Quantity > 0 ? wine.Quantity : 1,
                    WineExists = wineExists,
                    CountryExists = existingCountry is not null,
                    RegionExists = existingRegion is not null,
                    AppellationExists = existingAppellation is not null,
                    Vintage = wine.Vintage,
                    HasBottleDetails = wine.HasBottleDetails,
                    IsConsumed = wine.IsConsumed,
                    ConsumptionDate = wine.ConsumptionDate,
                    ConsumptionScore = wine.ConsumptionScore,
                    ConsumptionNote = wine.ConsumptionNote ?? string.Empty
                };

                rows.Add(row);
            }

            viewModel.BottleUpload.Errors = Array.Empty<string>();
            viewModel.BottleUpload.PreviewRows = rows;
            viewModel.BottleUpload.UploadedFileName = file.FileName;
        }
        catch (InvalidDataException ex)
        {
            viewModel.BottleUpload.Errors = new[] { ex.Message };
            viewModel.BottleUpload.PreviewRows = Array.Empty<WineImportPreviewRowViewModel>();
            viewModel.BottleUpload.UploadedFileName = file?.FileName;
        }
        catch (Exception ex)
        {
            viewModel.BottleUpload.Errors = new[]
            {
                $"An unexpected error occurred while processing the CellarTracker file: {ex.Message}"
            };
            viewModel.BottleUpload.PreviewRows = Array.Empty<WineImportPreviewRowViewModel>();
            viewModel.BottleUpload.UploadedFileName = file?.FileName;
        }
    }

    [HttpGet("bottles/{wineVintageId:guid}")]
    public async Task<IActionResult> GetBottleGroupDetails(Guid wineVintageId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        var response = await BuildBottleGroupResponseAsync(wineVintageId, currentUserId, cancellationToken);
        return Json(response);
    }

    // New endpoint: fetch details for an entire wine (all vintages)
    [HttpGet("wine/{wineId:guid}/details")]
    public async Task<IActionResult> GetWineDetails(
        Guid wineId,
        [FromQuery] Guid? locationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        var response = await BuildWineDetailsResponseAsync(
            wineId,
            currentUserId,
            cancellationToken,
            locationId);
        if (response is null)
        {
            return NotFound();
        }

        return Json(response);
    }

    [HttpPost("wines/{wineId:guid}/drinking-windows")]
    public async Task<IActionResult> GenerateDrinkingWindows(Guid wineId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        var user = await _userRepository.GetByIdAsync(currentUserId, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        var wine = await _wineRepository.GetByIdAsync(wineId, cancellationToken);
        if (wine is null)
        {
            return NotFound();
        }

        var ownedVintages = wine.WineVintages?
            .Where(vintage => vintage is not null && vintage.Bottles.Any(bottle => bottle.UserId == currentUserId))
            .ToList() ?? new List<WineVintage>();

        if (ownedVintages.Count == 0)
        {
            return NotFound();
        }

        var (tasteProfileSummary, tasteProfile) = TasteProfileUtilities.GetActiveTasteProfileTexts(user);
        var tasteProfileText = BuildTasteProfileText(tasteProfileSummary, tasteProfile);

        try
        {
            foreach (var vintage in ownedVintages)
            {
                if (vintage is null)
                {
                    continue;
                }

                var generatedAtUtc = DateTime.UtcNow;
                var wineDescription = BuildWineDescription(wine, vintage);
                var prompt = _chatGptPromptService.BuildDrinkingWindowPrompt(tasteProfileText, wineDescription);
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    continue;
                }

                var builder = new StringBuilder();
                builder.AppendLine(_chatGptPromptService.DrinkingWindowSystemPrompt);
                builder.AppendLine(prompt);
                
                var completion = await _chatGptService.GetChatResponseAsync(
                    builder.ToString(),
                    model: "gpt-4.1",
                    useWebSearch: true,
                    ct: cancellationToken);

                var content = completion.GetOutputText();// StringUtilities.ExtractCompletionText(completion);
                if (!TryParseDrinkingWindowYears(content, out var startYear, out var endYear,
                        out var alignmentScore))
                {
                    throw new JsonException("Unable to parse the drinking window response.");
                }

                var normalizedAlignmentScore = NormalizeAlignmentScore(alignmentScore);

                await _userDrinkingWindowService.SaveGeneratedWindowAsync(
                    currentUserId,
                    vintage.Id,
                    startYear,
                    endYear,
                    normalizedAlignmentScore,
                    generatedAtUtc,
                    cancellationToken);
            }
        }
        catch (ChatGptServiceNotConfiguredException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                "Drinking window generation is not configured.");
        }
        catch (System.ClientModel.ClientResultException)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                "We couldn't reach the drinking window assistant. Please try again.");
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                "We couldn't reach the drinking window assistant. Please try again.");
        }
        catch (JsonException)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                "The drinking window assistant returned an unexpected response.");
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }

        var response = await BuildWineDetailsResponseAsync(wineId, currentUserId, cancellationToken);
        if (response is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                "We updated your drinking windows, but could not refresh the inventory view.");
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
    public async Task<IActionResult> GetWineOptions([FromQuery(Name = "search")] string? search,
        CancellationToken cancellationToken)
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

    [HttpGet("catalog/countries")]
    public async Task<IActionResult> SearchCountries([FromQuery(Name = "search")] string? search,
        CancellationToken cancellationToken)
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

        var matches =
            await _appellationRepository.SearchByApproximateNameAsync(trimmedSearch, regionId.Value, 10,
                cancellationToken);
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

        var matches =
            await _subAppellationRepository.SearchByApproximateNameAsync(trimmedSearch, appellationId.Value, 10,
                cancellationToken);
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
    [HttpPost("wine-surfer/wines")]
    public async Task<IActionResult> CreateWine([FromBody] CreateWineRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        Wine? wine = null;

        if (request.WineId.HasValue && request.WineId.Value != Guid.Empty)
        {
            wine = await _wineRepository.GetByIdAsync(request.WineId.Value, cancellationToken);
            if (wine is null)
            {
                ModelState.AddModelError(nameof(request.WineId), "The selected wine could not be found.");
                return ValidationProblem(ModelState);
            }
        }
        else
        {
            // For creating/ensuring a wine, require basic catalog fields
            var name = request.Name?.Trim();
            var color = request.Color?.Trim();
            var region = request.Region?.Trim();
            var appellation = request.Appellation?.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                ModelState.AddModelError(nameof(request.Name), "Wine name is required.");
            }

            if (string.IsNullOrWhiteSpace(color))
            {
                ModelState.AddModelError(nameof(request.Color), "Color is required.");
            }

            if (string.IsNullOrWhiteSpace(region))
            {
                ModelState.AddModelError(nameof(request.Region), "Region is required.");
            }

            if (string.IsNullOrWhiteSpace(appellation))
            {
                ModelState.AddModelError(nameof(request.Appellation), "Appellation is required.");
            }

            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var result = await _wineCatalogService.EnsureWineAsync(
                new WineCatalogRequest(
                    name!,
                    color!,
                    request.Country,
                    region!,
                    appellation!,
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

            wine = result.Wine;
        }

        // If no inventory addition requested, return the wine option (backwards compatible)
        var wantsInventoryAdd = request.Quantity.HasValue && request.Quantity.Value > 0 && request.Vintage.HasValue;
        if (!wantsInventoryAdd)
        {
            var optionOnly = CreateWineOption(wine!);
            return Json(optionOnly);
        }

        // Add bottles for the current user
        var quantity = Math.Clamp(request.Quantity!.Value, 1, 12);

        BottleLocation? bottleLocation = null;
        if (request.BottleLocationId.HasValue)
        {
            bottleLocation =
                await _bottleLocationRepository.GetByIdAsync(request.BottleLocationId.Value, cancellationToken);
            if (bottleLocation is null)
            {
                ModelState.AddModelError(nameof(request.BottleLocationId), "Bottle location was not found.");
                return ValidationProblem(ModelState);
            }
        }

        var wineVintage =
            await _wineVintageRepository.GetOrCreateAsync(wine!.Id, request.Vintage!.Value, cancellationToken);

        for (var i = 0; i < quantity; i++)
        {
            var bottle = new Bottle
            {
                Id = Guid.NewGuid(),
                WineVintageId = wineVintage.Id,
                IsDrunk = false,
                DrunkAt = null,
                Price = null,
                PendingDelivery = false,
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
    public async Task<IActionResult> CreateGroup([FromBody] WineGroupCreateRequest request,
        CancellationToken cancellationToken)
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

        var duplicate = await _wineRepository.FindByNameAsync(trimmedName, subAppellation.Name,
            subAppellation.Appellation?.Name, cancellationToken);
        if (duplicate is not null)
        {
            ModelState.AddModelError(nameof(request.WineName),
                "A wine with the same name already exists for the selected sub-appellation.");
            return ValidationProblem(ModelState);
        }

        BottleLocation? bottleLocation = null;
        if (request.BottleLocationId.HasValue)
        {
            bottleLocation =
                await _bottleLocationRepository.GetByIdAsync(request.BottleLocationId.Value, cancellationToken);
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
                PendingDelivery = false,
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
    public async Task<IActionResult> AddWineToInventory([FromBody] AddWineToInventoryRequest request,
        CancellationToken cancellationToken)
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
            bottleLocation =
                await _bottleLocationRepository.GetByIdAsync(request.BottleLocationId.Value, cancellationToken);
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
                PendingDelivery = request.PendingDelivery,
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
    public async Task<IActionResult> UpdateGroup(Guid wineVintageId, [FromBody] WineGroupUpdateRequest request,
        CancellationToken cancellationToken)
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

        var duplicate = await _wineRepository.FindByNameAsync(trimmedName, subAppellation.Name,
            subAppellation.Appellation?.Name, cancellationToken);
        if (duplicate is not null && duplicate.Id != existingVintage.WineId)
        {
            ModelState.AddModelError(nameof(request.WineName),
                "A wine with the same name already exists for the selected sub-appellation.");
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
    public async Task<IActionResult> CreateBottle([FromBody] BottleMutationRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            _logger.LogWarning("Invalid create bottle request for wine vintage {WineVintageId}: {Errors}", request?.WineVintageId, FormatModelStateErrors(ModelState));
            return ValidationProblem(ModelState);
        }

        _logger.LogInformation("CreateBottle request received for wine vintage {WineVintageId}. Requested location: {BottleLocationId}. Quantity: {Quantity}. Pending delivery: {PendingDelivery}", request.WineVintageId, request.BottleLocationId, request.Quantity, request.PendingDelivery);

        if (!TryGetCurrentUserId(out var currentUserId))
        {
            _logger.LogWarning("CreateBottle request for wine vintage {WineVintageId} rejected because the user is not authenticated.", request.WineVintageId);
            return Challenge();
        }

        var existingBottles = await _bottleRepository.GetByWineVintageIdAsync(request.WineVintageId, cancellationToken);
        if (!existingBottles.Any(bottle => bottle.UserId == currentUserId))
        {
            _logger.LogWarning("CreateBottle request for wine vintage {WineVintageId} denied because user {UserId} does not own a bottle in this group.", request.WineVintageId, currentUserId);
            return NotFound();
        }

        BottleLocation? bottleLocation = null;
        if (request.BottleLocationId.HasValue)
        {
            _logger.LogInformation("CreateBottle verifying requested location {BottleLocationId} for wine vintage {WineVintageId}.", request.BottleLocationId, request.WineVintageId);
            bottleLocation =
                await _bottleLocationRepository.GetByIdAsync(request.BottleLocationId.Value, cancellationToken);
            if (bottleLocation is null)
            {
                _logger.LogWarning("CreateBottle request for wine vintage {WineVintageId} failed because bottle location {BottleLocationId} was not found.", request.WineVintageId, request.BottleLocationId);
                ModelState.AddModelError(nameof(request.BottleLocationId), "Bottle location was not found.");
                return ValidationProblem(ModelState);
            }

            _logger.LogInformation("CreateBottle request resolved bottle location {BottleLocationId} named {BottleLocationName}.", bottleLocation.Id, bottleLocation.Name);
        }
        else
        {
            _logger.LogInformation("CreateBottle request for wine vintage {WineVintageId} will assign no location.", request.WineVintageId);
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
                    PendingDelivery = isDrunk ? false : request.PendingDelivery,
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

    [HttpPost("bottles/{wineVintageId:guid}/drinking-window")]
    public async Task<IActionResult> SaveDrinkingWindow(Guid wineVintageId,
        [FromBody] DrinkingWindowRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var bottles = await _bottleRepository.GetByWineVintageIdAsync(wineVintageId, cancellationToken);
        var ownedBottles = bottles
            .Where(bottle => bottle.UserId == currentUserId)
            .ToList();

        if (!ownedBottles.Any())
        {
            return NotFound();
        }

        var startYear = request.StartYear;
        var endYear = request.EndYear;
        var hasAlignmentScore = request.AlignmentScore.HasValue;

        var hasStartOnly = startYear.HasValue && !endYear.HasValue;
        var hasEndOnly = endYear.HasValue && !startYear.HasValue;

        if (hasStartOnly || hasEndOnly)
        {
            const string message = "Please provide both a start and end year.";
            ModelState.AddModelError(nameof(request.StartYear), message);
            ModelState.AddModelError(nameof(request.EndYear), message);
            return ValidationProblem(ModelState);
        }

        if (startYear.HasValue && endYear.HasValue && endYear.Value < startYear.Value)
        {
            ModelState.AddModelError(nameof(request.EndYear),
                "The drinking window end year must be on or after the start year.");
            return ValidationProblem(ModelState);
        }

        var existingWindow = await _drinkingWindowRepository.FindAsync(currentUserId, wineVintageId, cancellationToken);
        var normalizedAlignmentScore = hasAlignmentScore
            ? NormalizeAlignmentScore(request.AlignmentScore!.Value)
            : existingWindow?.AlignmentScore ?? AlignmentScoreMinimum;

        if (!startYear.HasValue && !endYear.HasValue)
        {
            if (existingWindow is not null)
            {
                await _drinkingWindowRepository.DeleteAsync(existingWindow.Id, cancellationToken);
            }
        }
        else if (startYear.HasValue && endYear.HasValue)
        {
            var generatedAtUtc = DateTime.UtcNow;
            if (existingWindow is null)
            {
                var newWindow = new WineVintageUserDrinkingWindow
                {
                    Id = Guid.NewGuid(),
                    UserId = currentUserId,
                    WineVintageId = wineVintageId,
                    StartingYear = startYear.Value,
                    EndingYear = endYear.Value,
                    AlignmentScore = normalizedAlignmentScore,
                    GeneratedAtUtc = generatedAtUtc
                };

                await _drinkingWindowRepository.AddAsync(newWindow, cancellationToken);
            }
            else
            {
                existingWindow.StartingYear = startYear.Value;
                existingWindow.EndingYear = endYear.Value;
                if (hasAlignmentScore)
                {
                    existingWindow.AlignmentScore = normalizedAlignmentScore;
                }
                existingWindow.GeneratedAtUtc = generatedAtUtc;
                await _drinkingWindowRepository.UpdateAsync(existingWindow, cancellationToken);
            }
        }

        var response = await BuildBottleGroupResponseAsync(wineVintageId, currentUserId, cancellationToken);
        return Json(response);
    }

    [HttpPut("bottles/{id:guid}")]
    public async Task<IActionResult> UpdateBottle(Guid id, [FromBody] BottleMutationRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            _logger.LogWarning("Invalid update request for bottle {BottleId}: {Errors}", id, FormatModelStateErrors(ModelState));
            return ValidationProblem(ModelState);
        }

        _logger.LogInformation("UpdateBottle request received for bottle {BottleId} (wine vintage {WineVintageId}). Requested location: {BottleLocationId}. Pending delivery: {PendingDelivery}. Price: {Price}", id, request.WineVintageId, request.BottleLocationId, request.PendingDelivery, request.Price);

        if (!TryGetCurrentUserId(out var currentUserId))
        {
            _logger.LogWarning("UpdateBottle request for bottle {BottleId} rejected because the user is not authenticated.", id);
            return Challenge();
        }

        var bottle = await _bottleRepository.GetByIdAsync(id, cancellationToken);
        if (bottle is null || bottle.UserId != currentUserId)
        {
            _logger.LogWarning("UpdateBottle request for bottle {BottleId} failed because the bottle could not be found for user {UserId}.", id, currentUserId);
            return NotFound();
        }

        _logger.LogInformation("UpdateBottle current bottle location is {BottleLocationId}.", bottle.BottleLocationId);

        BottleLocation? bottleLocation = null;
        if (request.BottleLocationId.HasValue)
        {
            _logger.LogInformation("UpdateBottle request will verify bottle location {BottleLocationId} for bottle {BottleId}.", request.BottleLocationId, id);
            bottleLocation =
                await _bottleLocationRepository.GetByIdAsync(request.BottleLocationId.Value, cancellationToken);
            if (bottleLocation is null)
            {
                _logger.LogWarning("UpdateBottle request for bottle {BottleId} failed because bottle location {BottleLocationId} was not found.", id, request.BottleLocationId);
                ModelState.AddModelError(nameof(request.BottleLocationId), "Bottle location was not found.");
                return ValidationProblem(ModelState);
            }

            _logger.LogInformation("UpdateBottle request resolved bottle location {BottleLocationId} named {BottleLocationName}.", bottleLocation.Id, bottleLocation.Name);
        }
        else
        {
            _logger.LogInformation("UpdateBottle request for bottle {BottleId} will clear the bottle location.", id);
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
        bottle.PendingDelivery = request.IsDrunk ? false : request.PendingDelivery;
        bottle.BottleLocationId = bottleLocation?.Id;
        bottle.BottleLocation = null; // ensure EF does not reapply a stale navigation when updating

        _logger.LogInformation("UpdateBottle persisting bottle {BottleId} with location {BottleLocationId}, price {Price}, pending delivery {PendingDelivery}, is drunk {IsDrunk}.", bottle.Id, bottle.BottleLocationId, bottle.Price, bottle.PendingDelivery, bottle.IsDrunk);

        await _bottleRepository.UpdateAsync(bottle, cancellationToken);

        var response = await BuildBottleGroupResponseAsync(bottle.WineVintageId, currentUserId, cancellationToken);
        return Json(response);
    }

    [HttpPost("deliveries/accept")]
    public async Task<IActionResult> AcceptDeliveries([FromBody] AcceptDeliveriesRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        if (request.LocationId == Guid.Empty)
        {
            ModelState.AddModelError(nameof(request.LocationId), "Choose a storage location for the delivered bottles.");
            return ValidationProblem(ModelState);
        }

        var requestedBottleIds = request.BottleIds?
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList() ?? new List<Guid>();

        if (requestedBottleIds.Count == 0)
        {
            ModelState.AddModelError(nameof(request.BottleIds), "Select at least one pending bottle to accept.");
            return ValidationProblem(ModelState);
        }

        var location = await _bottleLocationRepository.GetByIdAsync(request.LocationId, cancellationToken);
        if (location is null || location.UserId != currentUserId)
        {
            ModelState.AddModelError(nameof(request.LocationId), "Storage location was not found.");
            return ValidationProblem(ModelState);
        }

        var bottles = await _bottleRepository.GetByIdsAsync(requestedBottleIds, cancellationToken);
        var bottleList = bottles.ToList();

        if (bottleList.Count != requestedBottleIds.Count)
        {
            return NotFound();
        }

        var unauthorizedBottle = bottleList.FirstOrDefault(bottle => bottle.UserId != currentUserId);
        if (unauthorizedBottle is not null)
        {
            return NotFound();
        }

        var alreadyAccepted = bottleList.FirstOrDefault(bottle => !bottle.PendingDelivery);
        if (alreadyAccepted is not null)
        {
            ModelState.AddModelError(nameof(request.BottleIds),
                "One or more bottles are no longer pending delivery. Refresh and try again.");
            return ValidationProblem(ModelState);
        }

        foreach (var bottle in bottleList)
        {
            bottle.PendingDelivery = false;
            bottle.BottleLocationId = location.Id;
            bottle.BottleLocation = null;
            bottle.UserId = currentUserId;
        }

        _logger.LogInformation(
            "AcceptDeliveries updating {BottleCount} bottles to location {LocationId} for user {UserId}.",
            bottleList.Count,
            location.Id,
            currentUserId);

        await _bottleRepository.UpdateManyAsync(bottleList, cancellationToken);

        return NoContent();
    }

    // Fallback endpoint to support environments that disallow HTTP PUT from the client
    [HttpPost("bottles/{id:guid}/drink")]
    public async Task<IActionResult> DrinkBottle(Guid id, [FromBody] BottleMutationRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            _logger.LogWarning("Invalid drink bottle request for bottle {BottleId}: {Errors}", id, FormatModelStateErrors(ModelState));
            return ValidationProblem(ModelState);
        }

        _logger.LogInformation("DrinkBottle request received for bottle {BottleId} (wine vintage {WineVintageId}). Requested location: {BottleLocationId}. Pending delivery: {PendingDelivery}. Price: {Price}.", id, request.WineVintageId, request.BottleLocationId, request.PendingDelivery, request.Price);

        if (!TryGetCurrentUserId(out var currentUserId))
        {
            _logger.LogWarning("DrinkBottle request for bottle {BottleId} rejected because the user is not authenticated.", id);
            return Challenge();
        }

        var bottle = await _bottleRepository.GetByIdAsync(id, cancellationToken);
        if (bottle is null || bottle.UserId != currentUserId)
        {
            _logger.LogWarning("DrinkBottle request for bottle {BottleId} failed because the bottle could not be found for user {UserId}.", id, currentUserId);
            return NotFound();
        }

        _logger.LogInformation("DrinkBottle current bottle location is {BottleLocationId}.", bottle.BottleLocationId);

        BottleLocation? bottleLocation = null;
        if (request.BottleLocationId.HasValue)
        {
            _logger.LogInformation("DrinkBottle request will verify bottle location {BottleLocationId} for bottle {BottleId}.", request.BottleLocationId, id);
            bottleLocation =
                await _bottleLocationRepository.GetByIdAsync(request.BottleLocationId.Value, cancellationToken);
            if (bottleLocation is null)
            {
                _logger.LogWarning("DrinkBottle request for bottle {BottleId} failed because bottle location {BottleLocationId} was not found.", id, request.BottleLocationId);
                ModelState.AddModelError(nameof(request.BottleLocationId), "Bottle location was not found.");
                return ValidationProblem(ModelState);
            }

            _logger.LogInformation("DrinkBottle request resolved bottle location {BottleLocationId} named {BottleLocationName}.", bottleLocation.Id, bottleLocation.Name);
        }
        else
        {
            _logger.LogInformation("DrinkBottle request for bottle {BottleId} will clear the bottle location.", id);
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
        bottle.PendingDelivery = request.IsDrunk ? false : request.PendingDelivery;
        bottle.BottleLocationId = bottleLocation?.Id;
        bottle.BottleLocation = null; // ensure EF does not reapply a stale navigation when updating

        _logger.LogInformation("DrinkBottle persisting bottle {BottleId} with location {BottleLocationId}, price {Price}, pending delivery {PendingDelivery}, is drunk {IsDrunk}.", bottle.Id, bottle.BottleLocationId, bottle.Price, bottle.PendingDelivery, bottle.IsDrunk);

        await _bottleRepository.UpdateAsync(bottle, cancellationToken);

        var response = await BuildBottleGroupResponseAsync(bottle.WineVintageId, currentUserId, cancellationToken);
        return Json(response);
    }

    [HttpPost("notes")]
    public async Task<IActionResult> CreateNote([FromBody] TastingNoteCreateRequest request,
        CancellationToken cancellationToken)
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
        var notTasted = (request as TastingNoteCreateRequest)?.NotTasted ?? (request is TastingNoteUpdateRequest ut ? ut.NotTasted : false);

        if (!notTasted && !hasNote && !hasScore)
        {
            ModelState.AddModelError(nameof(request.Note), "Provide a tasting note or score.");
            return ValidationProblem(ModelState);
        }

        var noteToSave = notTasted ? string.Empty : (hasNote ? trimmedNote! : string.Empty);

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
                Score = notTasted ? null : request.Score,
                NotTasted = notTasted,
                UserId = existingNote.UserId
            };

            await _tastingNoteRepository.UpdateAsync(updatedNote, cancellationToken);
        }
        else if (!notTasted)
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
    public async Task<IActionResult> UpdateNote(Guid noteId, [FromBody] TastingNoteUpdateRequest request,
        CancellationToken cancellationToken)
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

        var notTasted = request.NotTasted;
        if (!notTasted && !hasNote && !hasScore)
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

        existing.NotTasted = notTasted;
        existing.Note = notTasted ? string.Empty : (hasNote ? trimmedNote! : string.Empty);
        existing.Score = notTasted ? null : request.Score;

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

    private async Task<BottleGroupDetailsResponse?> BuildWineDetailsResponseAsync(
        Guid wineId,
        Guid userId,
        CancellationToken cancellationToken,
        Guid? locationFilterId = null)
    {
        var userBottles = await _bottleRepository.GetForUserAsync(userId, cancellationToken);
        var userDrinkingWindows = await _drinkingWindowRepository.GetForUserAsync(userId, cancellationToken);
        var drinkingWindowsByVintageId = userDrinkingWindows
            .GroupBy(window => window.WineVintageId)
            .ToDictionary(group => group.Key, group => group.First());
        var userLocations = await GetUserLocationsAsync(userId, cancellationToken);
        var locationSummaries = BuildLocationSummaries(userBottles, userLocations);

        var normalizedLocationId = locationFilterId.HasValue
            && userLocations.Any(location => location.Id == locationFilterId.Value)
                ? locationFilterId
                : null;

        var allOwnedForWine = userBottles
            .Where(b => b.WineVintage.Wine.Id == wineId)
            .ToList();

        if (allOwnedForWine.Count == 0)
        {
            return null;
        }

        var ownedBottles = (normalizedLocationId.HasValue
                ? allOwnedForWine.Where(b => b.BottleLocationId == normalizedLocationId.Value)
                : allOwnedForWine)
            .ToList();

        var totalCount = ownedBottles.Count;
        var pendingCount = ownedBottles.Count(b => b.PendingDelivery);
        var drunkCount = ownedBottles.Count(b => b.IsDrunk);
        var cellaredCount = Math.Max(totalCount - pendingCount - drunkCount, 0);
        var (statusLabel, statusClass) = (pendingCount, cellaredCount, drunkCount) switch
        {
            var (pending, _, drunk) when pending > 0 && drunk == 0 && cellaredCount == 0 => ("Pending", "pending"),
            var (_, cellared, drunk) when cellared > 0 && drunk == 0 && pendingCount == 0 => ("Cellared", "cellared"),
            var (_, _, drunk) when drunk == totalCount && totalCount > 0 => ("Drunk", "drunk"),
            _ => ("Mixed", "mixed")
        };

        var metadataBottle = allOwnedForWine.First();

        var summaryWindowStartYears = new List<int>();
        var summaryWindowEndYears = new List<int>();
        var summaryAlignmentScores = new List<decimal>();
        var summaryGeneratedDates = new List<DateTime>();

        foreach (var bottle in ownedBottles)
        {
            if (!bottle.PendingDelivery && drinkingWindowsByVintageId.TryGetValue(bottle.WineVintageId, out var window))
            {
                summaryWindowStartYears.Add(window.StartingYear);
                summaryWindowEndYears.Add(window.EndingYear);
                summaryAlignmentScores.Add(window.AlignmentScore);
                if (window.GeneratedAtUtc.HasValue)
                {
                    summaryGeneratedDates.Add(window.GeneratedAtUtc.Value);
                }
            }
        }

        int? aggregatedWindowStart = summaryWindowStartYears.Count > 0
            ? summaryWindowStartYears.Min()
            : null;

        int? aggregatedWindowEnd = summaryWindowEndYears.Count > 0
            ? summaryWindowEndYears.Min()
            : null;

        decimal? aggregatedAlignmentScore = summaryAlignmentScores.Count > 0
            ? NormalizeAlignmentScore(summaryAlignmentScores.Average())
            : null;

        DateTime? aggregatedGeneratedAtUtc = summaryGeneratedDates.Count > 0
            ? summaryGeneratedDates.Max()
            : null;

        var summary = new WineInventoryBottleViewModel
        {
            WineVintageId = Guid.Empty,
            WineId = wineId,
            WineName = metadataBottle.WineVintage.Wine.Name,
            Region = metadataBottle.WineVintage.Wine.SubAppellation?.Appellation?.Region?.Name,
            SubAppellation = metadataBottle.WineVintage.Wine.SubAppellation?.Name,
            Appellation = metadataBottle.WineVintage.Wine.SubAppellation?.Appellation?.Name,
            SubAppellationId = metadataBottle.WineVintage.Wine.SubAppellation?.Id,
            AppellationId = metadataBottle.WineVintage.Wine.SubAppellation?.Appellation?.Id,
            Vintage = 0,
            Color = metadataBottle.WineVintage.Wine.Color.ToString(),
            BottleCount = totalCount,
            PendingBottleCount = pendingCount,
            CellaredBottleCount = cellaredCount,
            DrunkBottleCount = drunkCount,
            StatusLabel = statusLabel,
            StatusCssClass = statusClass,
            AverageScore = CalculateAverageScore(ownedBottles),
            UserDrinkingWindowStartYear = aggregatedWindowStart,
            UserDrinkingWindowEndYear = aggregatedWindowEnd,
            UserDrinkingWindowAlignmentScore = aggregatedAlignmentScore,
            DrinkingWindowGeneratedAtUtc = aggregatedGeneratedAtUtc
        };

        var details = ownedBottles
            .OrderByDescending(b => b.WineVintage.Vintage)
            .ThenBy(b => b.PendingDelivery ? 0 : 1)
            .ThenBy(b => b.IsDrunk)
            .ThenBy(b => b.DrunkAt ?? DateTime.MaxValue)
            .Select(b =>
            {
                var userNote = b.TastingNotes
                    .Where(note => note.UserId == userId)
                    .OrderBy(note => note.Id)
                    .LastOrDefault();

                var hasWindow = drinkingWindowsByVintageId.TryGetValue(b.WineVintageId, out var window);

                return new WineInventoryBottleDetailViewModel
                {
                    BottleId = b.Id,
                    Price = b.Price,
                    IsDrunk = b.IsDrunk,
                    DrunkAt = b.DrunkAt,
                    PendingDelivery = b.PendingDelivery,
                    BottleLocationId = b.BottleLocationId,
                    BottleLocation = b.BottleLocation?.Name ?? "—",
                    UserId = b.UserId,
                    UserName = b.User?.Name ?? string.Empty,
                    Vintage = b.WineVintage.Vintage,
                    WineName = b.WineVintage.Wine.Name,
                    AverageScore = CalculateAverageScore(b),
                    CurrentUserNoteId = userNote?.Id,
                    CurrentUserNote = userNote?.Note,
                    CurrentUserScore = userNote?.Score,
                    WineVintageId = b.WineVintageId,
                    UserDrinkingWindowStartYear = hasWindow ? window.StartingYear : null,
                    UserDrinkingWindowEndYear = hasWindow ? window.EndingYear : null,
                    UserDrinkingWindowAlignmentScore = hasWindow
                        ? NormalizeAlignmentScore(window.AlignmentScore)
                        : null
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

    private async Task<BottleGroupDetailsResponse> BuildBottleGroupResponseAsync(Guid wineVintageId, Guid userId,
        CancellationToken cancellationToken)
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
        var drinkingWindow = await _drinkingWindowRepository.FindAsync(userId, wineVintageId, cancellationToken);
        var summary = CreateBottleGroupViewModel(wineVintageId, ownedBottles, averageScore, drinkingWindow);
        var details = ownedBottles
            .OrderBy(b => b.PendingDelivery ? 0 : 1)
            .ThenBy(b => b.IsDrunk)
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
                    PendingDelivery = b.PendingDelivery,
                    BottleLocationId = b.BottleLocationId,
                    BottleLocation = b.BottleLocation?.Name ?? "—",
                    UserId = b.UserId,
                    UserName = b.User?.Name ?? string.Empty,
                    Vintage = b.WineVintage.Vintage,
                    WineName = b.WineVintage.Wine.Name,
                    AverageScore = CalculateAverageScore(b),
                    CurrentUserNoteId = userNote?.Id,
                    CurrentUserNote = userNote?.Note,
                    CurrentUserScore = userNote?.Score,
                    WineVintageId = b.WineVintageId,
                    UserDrinkingWindowStartYear = drinkingWindow?.StartingYear,
                    UserDrinkingWindowEndYear = drinkingWindow?.EndingYear,
                    UserDrinkingWindowAlignmentScore = drinkingWindow is not null
                        ? NormalizeAlignmentScore(drinkingWindow.AlignmentScore)
                        : null
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

    private static string BuildTasteProfileText(string summary, string profile)
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

    private static bool TryParseDrinkingWindowYears(string? content, out int startYear, out int endYear,
        out decimal alignmentScore)
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

    private async Task SetInventoryAddModalViewDataAsync(Guid currentUserId, CancellationToken cancellationToken)
    {
        var userLocations = await GetUserLocationsAsync(currentUserId, cancellationToken);
        var modalViewModel = new InventoryAddModalViewModel
        {
            Locations = userLocations
                .Select(location => new BottleLocationOption
                {
                    Id = location.Id,
                    Name = location.Name,
                    Capacity = location.Capacity
                })
                .ToList()
        };

        ViewData["InventoryAddModal"] = modalViewModel;
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
                    DrunkBottleCount = group.Count(bottle => bottle.IsDrunk),
                    PendingBottleCount = group.Count(bottle => bottle.PendingDelivery)
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
                        PendingBottleCount = 0,
                        CellaredBottleCount = 0,
                        DrunkBottleCount = 0
                    };
                }

                var cellaredCount = summary.BottleCount - summary.DrunkBottleCount - summary.PendingBottleCount;

                return new WineInventoryLocationViewModel
                {
                    Id = location.Id,
                    Name = location.Name,
                    Capacity = location.Capacity,
                    BottleCount = summary.BottleCount,
                    UniqueWineCount = summary.UniqueWineCount,
                    PendingBottleCount = Math.Max(summary.PendingBottleCount, 0),
                    CellaredBottleCount = Math.Max(cellaredCount, 0),
                    DrunkBottleCount = summary.DrunkBottleCount
                };
            })
            .ToList();
    }

    private async Task<BottleNotesResponse?> BuildBottleNotesResponseAsync(Guid bottleId, Guid userId,
        CancellationToken cancellationToken)
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
            PendingDelivery = bottle.PendingDelivery,
            BottleAverageScore = CalculateAverageScore(bottle),
            GroupAverageScore = groupAverageScore
        };

        return new BottleNotesResponse
        {
            Bottle = bottleSummary,
            Notes = noteViewModels
        };
    }

    private static WineInventoryBottleViewModel CreateBottleGroupViewModel(Guid groupId,
        IReadOnlyCollection<Bottle> bottles, decimal? averageScore,
        WineVintageUserDrinkingWindow? drinkingWindow)
    {
        var firstBottle = bottles.First();
        var totalCount = bottles.Count;
        var pendingCount = bottles.Count(b => b.PendingDelivery);
        var drunkCount = bottles.Count(b => b.IsDrunk);
        var cellaredCount = Math.Max(totalCount - pendingCount - drunkCount, 0);

        var (statusLabel, statusClass) = (pendingCount, cellaredCount, drunkCount) switch
        {
            var (pending, _, drunk) when pending > 0 && drunk == 0 && cellaredCount == 0 => ("Pending", "pending"),
            var (_, cellared, drunk) when cellared > 0 && drunk == 0 && pendingCount == 0 => ("Cellared", "cellared"),
            var (_, _, drunk) when drunk == totalCount && totalCount > 0 => ("Drunk", "drunk"),
            _ => ("Mixed", "mixed")
        };

        return new WineInventoryBottleViewModel
        {
            WineVintageId = groupId,
            WineId = firstBottle.WineVintage.Wine.Id,
            WineName = firstBottle.WineVintage.Wine.Name,
            Region = firstBottle.WineVintage.Wine.SubAppellation?.Appellation?.Region?.Name,
            SubAppellation = firstBottle.WineVintage.Wine.SubAppellation?.Name,
            Appellation = firstBottle.WineVintage.Wine.SubAppellation?.Appellation?.Name,
            SubAppellationId = firstBottle.WineVintage.Wine.SubAppellation?.Id,
            AppellationId = firstBottle.WineVintage.Wine.SubAppellation?.Appellation?.Id,
            Vintage = firstBottle.WineVintage.Vintage,
            Color = firstBottle.WineVintage.Wine.Color.ToString(),
            BottleCount = totalCount,
            PendingBottleCount = pendingCount,
            CellaredBottleCount = cellaredCount,
            DrunkBottleCount = drunkCount,
            StatusLabel = statusLabel,
            StatusCssClass = statusClass,
            AverageScore = averageScore,
            UserDrinkingWindowStartYear = drinkingWindow?.StartingYear,
            UserDrinkingWindowEndYear = drinkingWindow?.EndingYear,
            UserDrinkingWindowAlignmentScore = drinkingWindow is not null
                ? NormalizeAlignmentScore(drinkingWindow.AlignmentScore)
                : null,
            DrinkingWindowGeneratedAtUtc = drinkingWindow?.GeneratedAtUtc
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

    [HttpGet("available-bottles")]
    public async Task<IActionResult> GetAvailableBottles(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Challenge();
        }

        var bottles = await _bottleRepository.GetAvailableForUserAsync(userId, cancellationToken);

        var results = bottles
            .Select(b => new AvailableBottleOption
            {
                Id = b.Id,
                Wine = b.WineVintage?.Wine?.Name ?? string.Empty,
                Vintage = b.WineVintage?.Vintage,
                Appellation = BuildSubAppellationLabel(b.WineVintage?.Wine?.SubAppellation!),
                Location = b.BottleLocation?.Name
            })
            .OrderBy(b => b.Wine)
            .ThenByDescending(b => b.Vintage)
            .ToList();

        return Json(results);
    }
}

public class AvailableBottleOption
{
    public Guid Id { get; set; }
    public string Wine { get; set; } = string.Empty;
    public int? Vintage { get; set; }
    public string? Appellation { get; set; }
    public string? Location { get; set; }
}

public class WineInventoryViewModel
{
    public string Status { get; set; } = "all";
    public string? Search { get; set; }
    public string SortField { get; set; } = "wine";
    public string SortDirection { get; set; } = "asc";

    public IReadOnlyList<WineInventoryBottleViewModel> Bottles { get; set; } =
        Array.Empty<WineInventoryBottleViewModel>();

    public Guid CurrentUserId { get; set; }

    public IReadOnlyList<WineInventoryLocationViewModel> Locations { get; set; } =
        Array.Empty<WineInventoryLocationViewModel>();

    public InventoryAddModalViewModel InventoryAddModal { get; set; } = new();
    public AcceptDeliveriesModalViewModel AcceptDeliveriesModal { get; set; } =
        AcceptDeliveriesModalViewModel.Empty;
    public int PendingBottleCount { get; set; }
    public bool HasActiveFilters { get; set; }
    public IReadOnlySet<Guid> HighlightedLocationIds { get; set; } = new HashSet<Guid>();
    public Guid? LocationFilterId { get; set; }
    public IReadOnlyList<FilterOption> StatusOptions { get; set; } = Array.Empty<FilterOption>();
}

    public class WineInventoryBottleViewModel
    {
        public Guid WineVintageId { get; set; }
        public Guid WineId { get; set; }
        public string WineName { get; set; } = string.Empty;
        public string? Region { get; set; }
        public string? SubAppellation { get; set; }
        public string? Appellation { get; set; }
        public Guid? SubAppellationId { get; set; }
        public Guid? AppellationId { get; set; }
        public int Vintage { get; set; }
        public string Color { get; set; } = string.Empty;
        public int BottleCount { get; set; }
        public int PendingBottleCount { get; set; }
        public int CellaredBottleCount { get; set; }
        public int DrunkBottleCount { get; set; }
        public string StatusLabel { get; set; } = string.Empty;
        public string StatusCssClass { get; set; } = string.Empty;
        public decimal? AverageScore { get; set; }
        public int? UserDrinkingWindowStartYear { get; set; }
        public int? UserDrinkingWindowEndYear { get; set; }
        public decimal? UserDrinkingWindowAlignmentScore { get; set; }
        public DateTime? DrinkingWindowGeneratedAtUtc { get; set; }
    }


    public class WineInventoryBottleDetailViewModel
    {
        public Guid BottleId { get; set; }
        public decimal? Price { get; set; }
        public bool IsDrunk { get; set; }
        public DateTime? DrunkAt { get; set; }
        public bool PendingDelivery { get; set; }
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

        // Added to allow client to open a specific vintage group from wine-level view
        public Guid WineVintageId { get; set; }
        public int? UserDrinkingWindowStartYear { get; set; }
        public int? UserDrinkingWindowEndYear { get; set; }
        public decimal? UserDrinkingWindowAlignmentScore { get; set; }
    }

    public class WineInventoryLocationViewModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? Capacity { get; set; }
        public int BottleCount { get; set; }
        public int UniqueWineCount { get; set; }
        public int PendingBottleCount { get; set; }
        public int CellaredBottleCount { get; set; }
        public int DrunkBottleCount { get; set; }
    }

    public class BottleGroupDetailsResponse
    {
        public WineInventoryBottleViewModel? Group { get; set; }

        public IReadOnlyList<WineInventoryBottleDetailViewModel> Details { get; set; } =
            Array.Empty<WineInventoryBottleDetailViewModel>();

        public IReadOnlyList<WineInventoryLocationViewModel> Locations { get; set; } =
            Array.Empty<WineInventoryLocationViewModel>();
    }

    public class BottleMutationRequest
    {
        [Required] public Guid WineVintageId { get; set; }

        public decimal? Price { get; set; }

        public bool IsDrunk { get; set; }

        public DateTime? DrunkAt { get; set; }

        public Guid? BottleLocationId { get; set; }

        public Guid? UserId { get; set; }

        public bool PendingDelivery { get; set; }

        [Range(1, 12)] public int Quantity { get; set; } = 1;
    }

    public class AcceptDeliveriesRequest
    {
        [Required]
        public Guid LocationId { get; set; }

        [MinLength(1)]
        public IReadOnlyList<Guid> BottleIds { get; set; } = Array.Empty<Guid>();
    }

    public class DrinkingWindowRequest
    {
        [Range(1900, 2200)]
        public int? StartYear { get; set; }

        [Range(1900, 2200)]
        public int? EndYear { get; set; }

        [Range((double)0, (double)10)]
        public decimal? AlignmentScore { get; set; }
    }

    public record FilterOption(string Value, string Label);

    public class WineGroupUpdateRequest
    {
        [Required]
        [StringLength(256, MinimumLength = 1)]
        public string WineName { get; set; } = string.Empty;

        [Range(1900, 2100)] public int Vintage { get; set; }

        [Required] public WineColor Color { get; set; }

        [Required] public Guid SubAppellationId { get; set; }
    }

    public class WineGroupCreateRequest : WineGroupUpdateRequest
    {
        [Range(1, 5000)] public int InitialBottleCount { get; set; } = 1;

        public Guid? BottleLocationId { get; set; }
    }

    public class AddWineToInventoryRequest
    {
        [Required] public Guid WineId { get; set; }

        [Range(1900, 2100)] public int Vintage { get; set; }

        [Range(1, 12)] public int Quantity { get; set; } = 1;

        public Guid? BottleLocationId { get; set; }

        public bool PendingDelivery { get; set; }
    }

    public class CreateWineRequest
    {
        // When adding bottles for an existing wine, allow WineId
        public Guid? WineId { get; set; }

        [StringLength(256, MinimumLength = 1)] public string Name { get; set; } = string.Empty;

        [StringLength(32, MinimumLength = 1)] public string Color { get; set; } = string.Empty;

        [StringLength(128)] public string? Country { get; set; }

        [StringLength(128, MinimumLength = 0)] public string? Region { get; set; }

        [StringLength(256, MinimumLength = 0)] public string? Appellation { get; set; }

        [StringLength(256)] public string? SubAppellation { get; set; }

        [StringLength(256)] public string? GrapeVariety { get; set; }

        // Inventory parameters (optional). If Quantity > 0, will add bottles for current user
        [Range(1900, 2100)] public int? Vintage { get; set; }

        [Range(1, 12)] public int? Quantity { get; set; }

        public Guid? BottleLocationId { get; set; }
    }

    public class ImportReadyWinesRequest
    {
        public IReadOnlyList<ImportReadyWineRowRequest> Rows { get; set; } = Array.Empty<ImportReadyWineRowRequest>();
    }

    public class ImportReadyWineRowRequest
    {
        public string RowId { get; set; } = string.Empty;
        public int RowNumber { get; set; }
        public string? Name { get; set; }
        public string? Country { get; set; }
        public string? Region { get; set; }
        public string? Appellation { get; set; }
        public string? SubAppellation { get; set; }
        public string? Color { get; set; }
        public string? GrapeVariety { get; set; }
        public bool IsConsumed { get; set; }
        public DateTime? ConsumptionDate { get; set; }
        public decimal? ConsumptionScore { get; set; }
        public string? ConsumptionNote { get; set; }
    }

    public class ImportReadyWinesResponse
    {
        public int TotalRequested { get; set; }
        public int Created { get; set; }
        public int AlreadyExists { get; set; }
        public int Failed { get; set; }
        public int CreatedCountries { get; set; }
        public List<ImportReadyWineRowResult> Rows { get; } = [];
        public List<string> Errors { get; } = [];
    }

    public class ImportReadyWineRowResult
    {
        public string RowId { get; set; } = string.Empty;
        public int RowNumber { get; set; }
        public bool Created { get; set; }
        public bool AlreadyExists { get; set; }
        public bool CountryCreated { get; set; }
        public string? Error { get; set; }
    }

    public class ImportCellarTrackerInventoryRequest
    {
        public IReadOnlyList<ImportCellarTrackerInventoryRowRequest> Rows { get; set; } =
            Array.Empty<ImportCellarTrackerInventoryRowRequest>();
    }

    public class ImportCellarTrackerInventoryRowRequest
    {
        public string RowId { get; set; } = string.Empty;
        public int RowNumber { get; set; }
        public string? Name { get; set; }
        public string? Country { get; set; }
        public string? Region { get; set; }
        public string? Appellation { get; set; }
        public string? SubAppellation { get; set; }
        public string? Color { get; set; }
        public string? GrapeVariety { get; set; }
        public int? Vintage { get; set; }
        public int Quantity { get; set; }
        public bool IsConsumed { get; set; }
        public DateTime? ConsumptionDate { get; set; }
        public decimal? ConsumptionScore { get; set; }
        public string? ConsumptionNote { get; set; }
    }

    public class ImportCellarTrackerInventoryResponse
    {
        public int TotalRequested { get; set; }
        public int WinesCreated { get; set; }
        public int WinesAlreadyExisting { get; set; }
        public int BottlesAdded { get; set; }
        public int Failed { get; set; }
        public int CreatedCountries { get; set; }
        public List<ImportCellarTrackerInventoryRowResult> Rows { get; } = [];
        public List<string> Errors { get; } = [];
    }

    public class ImportCellarTrackerInventoryRowResult
    {
        public string RowId { get; set; } = string.Empty;
        public int RowNumber { get; set; }
        public bool WineCreated { get; set; }
        public bool WineAlreadyExisted { get; set; }
        public bool CountryCreated { get; set; }
        public int BottlesAdded { get; set; }
        public Guid? WineId { get; set; }
        public Guid? WineVintageId { get; set; }
        public string? Error { get; set; }
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

    // Helper wrapped in partial class to keep controller organized.
    public partial class WineInventoryController
    {
        private static string FormatModelStateErrors(ModelStateDictionary modelState)
        {
            if (modelState is null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();

            foreach (var entry in modelState)
            {
                if (entry.Value?.Errors is null || entry.Value.Errors.Count == 0)
                {
                    continue;
                }

                foreach (var error in entry.Value.Errors)
                {
                    if (builder.Length > 0)
                    {
                        builder.Append(" | ");
                    }

                    builder.Append(entry.Key);

                    if (!string.IsNullOrWhiteSpace(error.ErrorMessage))
                    {
                        builder.Append(": ").Append(error.ErrorMessage);
                    }
                    else if (error.Exception is not null)
                    {
                        builder.Append(": ").Append(error.Exception.Message);
                    }
                }
            }

            return builder.Length == 0 ? "No model state errors recorded." : builder.ToString();
        }

        private static string? FormatCatalogErrors(IReadOnlyDictionary<string, string[]>? errors)
        {
            if (errors is null || errors.Count == 0)
            {
                return null;
            }

            var messages = new List<string>();
            foreach (var entry in errors)
            {
                if (entry.Value is null)
                {
                    continue;
                }

                foreach (var message in entry.Value)
                {
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        messages.Add(message.Trim());
                    }
                }
            }

            if (messages.Count == 0)
            {
                return null;
            }

            return string.Join(' ', messages);
        }
    }

    public class WineImportPageViewModel
    {
        public WineImportPanelViewModel WineUpload { get; set; } = new();
        public WineImportPanelViewModel BottleUpload { get; set; } = new();
    }

    public class WineImportPanelViewModel
    {
        public string? UploadedFileName { get; set; }
        public WineImportResult? Result { get; set; }
        public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
        public IReadOnlyList<WineImportPreviewRowViewModel> PreviewRows { get; set; } =
            Array.Empty<WineImportPreviewRowViewModel>();
        public string? SelectedCountry { get; set; }
        public string? SelectedRegion { get; set; }
        public string? SelectedColor { get; set; }
        public bool HasErrors => Errors.Count > 0;
        public bool HasResult => Result is not null;
        public bool HasPreview => PreviewRows.Count > 0;
    }

    public class WineImportPreviewRowViewModel
    {
        public string RowId { get; set; } = string.Empty;
        public int RowNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string Appellation { get; set; } = string.Empty;
        public string SubAppellation { get; set; } = string.Empty;
        public string GrapeVariety { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public int Amount { get; set; }
        public bool WineExists { get; set; }
        public bool CountryExists { get; set; }
        public bool RegionExists { get; set; }
        public bool AppellationExists { get; set; }
        public int? Vintage { get; set; }
        public bool HasBottleDetails { get; set; }
        public bool IsConsumed { get; set; }
        public DateTime? ConsumptionDate { get; set; }
        public decimal? ConsumptionScore { get; set; }
        public string ConsumptionNote { get; set; } = string.Empty;
        public bool CanCreateInventory => HasBottleDetails && Amount > 0;
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
        public bool PendingDelivery { get; set; }
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
        [StringLength(2048)] public string? Note { get; set; }

        [Range(0, 10)] public decimal? Score { get; set; }

        public bool NotTasted { get; set; }
    }

    public class TastingNoteCreateRequest : TastingNoteUpdateRequest
    {
        [Required] public Guid BottleId { get; set; }
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
