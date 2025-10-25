using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;

namespace AllMCPSolution.Controllers;

[Authorize]
[Route("wine-surfer")]
public sealed class WineWavesController : WineSurferControllerBase
{
    private static readonly string[] DatasetPalette =
    {
        "#E4572E",
        "#4E79A7",
        "#F28E2B",
        "#76B7B2",
        "#59A14F",
        "#EDC948",
        "#B07AA1",
        "#FF9DA7",
        "#9C755F",
        "#BAB0AC"
    };

    private readonly IWineVintageEvolutionScoreRepository _evolutionScoreRepository;
    private readonly IWineSurferTopBarService _topBarService;

    public WineWavesController(
        IWineVintageEvolutionScoreRepository evolutionScoreRepository,
        IUserRepository userRepository,
        IWineSurferTopBarService topBarService,
        UserManager<ApplicationUser> userManager) : base(userManager, userRepository)
    {
        _evolutionScoreRepository = evolutionScoreRepository;
        _topBarService = topBarService;
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

        var scores = await _evolutionScoreRepository.GetForUserAsync(currentUserId.Value, cancellationToken);

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

                var subAppellation = wine.SubAppellation;
                var appellation = subAppellation?.Appellation;
                var region = appellation?.Region;
                var country = region?.Country;

                var nameParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(wine.Name))
                {
                    nameParts.Add(wine.Name);
                }
                if (vintage > 0)
                {
                    nameParts.Add(vintage.ToString());
                }

                var label = nameParts.Count > 0
                    ? string.Join(" ", nameParts)
                    : $"Vintage {vintage}";

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

                var detailText = details.Count > 0
                    ? string.Join(" • ", details)
                    : null;

                var color = DatasetPalette[index % DatasetPalette.Length];

                return new WineWavesDataset(
                    first.WineVintageId,
                    label,
                    detailText,
                    points,
                    color);
            })
            .ToList();

        ViewData["WineSurferPageTitle"] = "Wine Waves";
        var viewModel = new WineWavesViewModel(datasets);
        return View("~/Views/WineWaves/Index.cshtml", viewModel);
    }
}

public sealed record WineWavesViewModel(IReadOnlyList<WineWavesDataset> Datasets);

public sealed record WineWavesDataset(
    Guid WineVintageId,
    string Label,
    string? Details,
    IReadOnlyList<WineWavesPoint> Points,
    string ColorHex);

public sealed record WineWavesPoint(int Year, decimal Score);
