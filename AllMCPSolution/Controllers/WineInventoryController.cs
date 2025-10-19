using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AllMCPSolution.Controllers;

[Route("wine-manager")]
public class WineInventoryController : Controller
{
    private readonly IBottleRepository _bottleRepository;

    public WineInventoryController(IBottleRepository bottleRepository)
    {
        _bottleRepository = bottleRepository;
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
        var bottles = await _bottleRepository.GetAllAsync(cancellationToken);

        var averageScores = bottles
            .SelectMany(b => b.TastingNotes
                .Where(tn => tn.Score.HasValue)
                .Select(tn => new { b.WineVintageId, Score = tn.Score!.Value }))
            .GroupBy(x => x.WineVintageId)
            .ToDictionary(g => g.Key, g => (decimal?)g.Average(x => x.Score));

        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim().ToLowerInvariant();
        WineColor? filterColor = null;
        if (!string.IsNullOrWhiteSpace(color) && Enum.TryParse<WineColor>(color, true, out var parsedColor))
        {
            filterColor = parsedColor;
        }

        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var normalizedSortField = string.IsNullOrWhiteSpace(sortField) ? "wine" : sortField.Trim().ToLowerInvariant();
        var normalizedSortDir = string.IsNullOrWhiteSpace(sortDir) ? "asc" : sortDir.Trim().ToLowerInvariant();

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
                (b.WineVintage.Wine.Appellation?.Name?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
                b.WineVintage.Vintage.ToString(CultureInfo.InvariantCulture).Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase));
        }

        var descending = string.Equals(normalizedSortDir, "desc", StringComparison.OrdinalIgnoreCase);
        IOrderedEnumerable<Bottle> ordered = normalizedSortField switch
        {
            "appellation" => descending
                ? query.OrderByDescending(b => b.WineVintage.Wine.Appellation?.Name)
                : query.OrderBy(b => b.WineVintage.Wine.Appellation?.Name),
            "vintage" => descending
                ? query.OrderByDescending(b => b.WineVintage.Vintage)
                : query.OrderBy(b => b.WineVintage.Vintage),
            "color" => descending
                ? query.OrderByDescending(b => b.WineVintage.Wine.Color)
                : query.OrderBy(b => b.WineVintage.Wine.Color),
            "status" => descending
                ? query.OrderByDescending(b => b.IsDrunk)
                : query.OrderBy(b => b.IsDrunk),
            "score" => descending
                ? query.OrderByDescending(b => averageScores.TryGetValue(b.WineVintageId, out var avg) ? avg : null)
                : query.OrderBy(b => averageScores.TryGetValue(b.WineVintageId, out var avg) ? avg : null),
            _ => descending
                ? query.OrderByDescending(b => b.WineVintage.Wine.Name)
                : query.OrderBy(b => b.WineVintage.Wine.Name)
        };

        var orderedWithTies = ordered
            .ThenBy(b => b.WineVintage.Wine.Name)
            .ThenBy(b => b.WineVintage.Vintage)
            .ThenBy(b => b.Id);

        var groupedBottles = orderedWithTies
            .GroupBy(b => b.WineVintageId)
            .Select(group =>
            {
                var firstBottle = group.First();
                var totalCount = group.Count();
                var drunkCount = group.Count(b => b.IsDrunk);

                var (statusLabel, statusClass) = drunkCount switch
                {
                    var d when d == 0 => ("Cellared", "cellared"),
                    var d when d == totalCount => ("Drunk", "drunk"),
                    _ => ("Mixed", "mixed")
                };

                return new WineInventoryBottleViewModel
                {
                    WineVintageId = group.Key,
                    WineName = firstBottle.WineVintage.Wine.Name,
                    Appellation = firstBottle.WineVintage.Wine.Appellation?.Name,
                    Vintage = firstBottle.WineVintage.Vintage,
                    Color = firstBottle.WineVintage.Wine.Color.ToString(),
                    BottleCount = totalCount,
                    StatusLabel = statusLabel,
                    StatusCssClass = statusClass,
                    AverageScore = averageScores.TryGetValue(group.Key, out var avg) ? avg : null
                };
            })
            .ToList();

        IOrderedEnumerable<WineInventoryBottleViewModel> orderedGroups = normalizedSortField switch
        {
            "appellation" => descending
                ? groupedBottles.OrderByDescending(b => b.Appellation)
                : groupedBottles.OrderBy(b => b.Appellation),
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

        var viewModel = new WineInventoryViewModel
        {
            Status = normalizedStatus,
            Color = filterColor?.ToString(),
            Search = normalizedSearch,
            SortField = normalizedSortField,
            SortDirection = descending ? "desc" : "asc",
            Bottles = items,
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
}

public class WineInventoryViewModel
{
    public string Status { get; set; } = "all";
    public string? Color { get; set; }
    public string? Search { get; set; }
    public string SortField { get; set; } = "wine";
    public string SortDirection { get; set; } = "asc";
    public IReadOnlyList<WineInventoryBottleViewModel> Bottles { get; set; } = Array.Empty<WineInventoryBottleViewModel>();
    public IReadOnlyList<FilterOption> StatusOptions { get; set; } = Array.Empty<FilterOption>();
    public IReadOnlyList<FilterOption> ColorOptions { get; set; } = Array.Empty<FilterOption>();
}

public class WineInventoryBottleViewModel
{
    public Guid WineVintageId { get; set; }
    public string WineName { get; set; } = string.Empty;
    public string? Appellation { get; set; }
    public int Vintage { get; set; }
    public string Color { get; set; } = string.Empty;
    public int BottleCount { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public string StatusCssClass { get; set; } = string.Empty;
    public decimal? AverageScore { get; set; }
}

public record FilterOption(string Value, string Label);
