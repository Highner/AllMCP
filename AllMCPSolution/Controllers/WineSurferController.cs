using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AllMCPSolution.Controllers;

[Route("wine-surfer")]
public class WineSurferController : Controller
{
    private static readonly IReadOnlyDictionary<string, (double Longitude, double Latitude)> RegionCoordinates =
        new Dictionary<string, (double Longitude, double Latitude)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Bordeaux"] = (-0.58, 44.84),
            ["Burgundy"] = (4.75, 47.0),
            ["Champagne"] = (4.05, 49.05),
            ["Rhône"] = (4.8, 45.0),
            ["Rhone"] = (4.8, 45.0),
            ["Loire"] = (-0.5, 47.5),
            ["Provence"] = (6.2, 43.5),
            ["Tuscany"] = (11.0, 43.4),
            ["Piedmont"] = (8.0, 44.7),
            ["Veneto"] = (11.5, 45.5),
            ["Ribera del Duero"] = (-3.75, 41.7),
            ["Ribera Del Duero"] = (-3.75, 41.7),
            ["Rioja"] = (-2.43, 42.4),
            ["Douro"] = (-7.8, 41.1),
            ["Douro Valley"] = (-7.8, 41.1),
            ["Mosel"] = (6.7, 49.8),
            ["Rheingau"] = (8.0, 50.0),
            ["Nahe"] = (7.75, 49.8),
            ["Finger Lakes"] = (-76.9, 42.7),
            ["Napa Valley"] = (-122.3, 38.5),
            ["Sonoma"] = (-122.5, 38.3),
            ["Willamette Valley"] = (-123.0, 45.2),
            ["Columbia Valley"] = (-119.5, 46.2),
            ["Marlborough"] = (173.9, -41.5),
            ["Central Otago"] = (169.2, -45.0),
            ["Barossa"] = (138.95, -34.5),
            ["McLaren Vale"] = (138.5, -35.2),
            ["Mc Laren Vale"] = (138.5, -35.2),
            ["Yarra Valley"] = (145.5, -37.7),
            ["Coonawarra"] = (140.8, -37.3),
            ["Maipo"] = (-70.55, -33.6),
            ["Maipo Valley"] = (-70.55, -33.6),
            ["Mendoza"] = (-68.85, -32.9),
            ["Mendoza Valley"] = (-68.85, -32.9),
            ["Stellenbosch"] = (18.86, -33.9)
        };

    private static readonly IReadOnlyDictionary<string, (double Longitude, double Latitude)> CountryCoordinates =
        new Dictionary<string, (double Longitude, double Latitude)>(StringComparer.OrdinalIgnoreCase)
        {
            ["France"] = (2.21, 46.23),
            ["Italy"] = (12.57, 41.87),
            ["Spain"] = (-3.75, 40.46),
            ["Portugal"] = (-8.0, 39.69),
            ["Germany"] = (10.45, 51.17),
            ["Austria"] = (14.55, 47.52),
            ["Switzerland"] = (8.23, 46.82),
            ["United States"] = (-98.58, 39.83),
            ["United States of America"] = (-98.58, 39.83),
            ["USA"] = (-98.58, 39.83),
            ["U.S.A."] = (-98.58, 39.83),
            ["US"] = (-98.58, 39.83),
            ["Canada"] = (-106.35, 56.13),
            ["Chile"] = (-70.67, -33.45),
            ["Argentina"] = (-63.62, -38.42),
            ["Australia"] = (133.78, -25.27),
            ["New Zealand"] = (174.78, -41.28),
            ["South Africa"] = (22.94, -30.56),
            ["England"] = (-1.17, 52.36),
            ["United Kingdom"] = (-3.44, 55.38),
            ["UK"] = (-3.44, 55.38),
            ["Scotland"] = (-4.2, 56.82),
            ["Ireland"] = (-8.0, 53.41),
            ["Japan"] = (138.25, 36.2),
            ["China"] = (104.2, 35.86),
            ["Georgia"] = (43.36, 42.32),
            ["Greece"] = (22.0, 39.07),
            ["Hungary"] = (19.5, 47.16),
            ["Slovenia"] = (14.82, 46.15),
            ["Croatia"] = (15.2, 45.1),
            ["Uruguay"] = (-55.77, -32.52)
        };

    private readonly IWineRepository _wineRepository;
    private readonly IUserRepository _userRepository;

    public WineSurferController(IWineRepository wineRepository, IUserRepository userRepository)
    {
        _wineRepository = wineRepository;
        _userRepository = userRepository;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var wines = await _wineRepository.GetAllAsync(cancellationToken);

        var highlightPoints = wines
            .Where(w => w.SubAppellation?.Appellation?.Region is not null)
            .Select(w => new
            {
                Wine = w,
                Region = w.SubAppellation!.Appellation!.Region!
            })
            .GroupBy(entry => entry.Region.Id)
            .Select(group =>
            {
                var region = group.First().Region;
                var metrics = CalculateRegionInventoryMetrics(group.Select(entry => entry.Wine));
                return CreateHighlightPoint(region, metrics);
            })
            .Where(point => point is not null)
            .Cast<MapHighlightPoint>()
            .OrderBy(point => point.Label)
            .ToList();

        var model = new WineSurferLandingViewModel(highlightPoints);
        Response.ContentType = "text/html; charset=utf-8";
        return View("Index", model);
    }

    [HttpGet("sisterhoods")]
    public IActionResult Sisterhoods()
    {
        Response.ContentType = "text/html; charset=utf-8";
        return View("Sisterhoods");
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        var users = await _userRepository.GetAllAsync(cancellationToken);

        var response = users
            .Where(u => !string.IsNullOrWhiteSpace(u.Name))
            .OrderBy(u => u.Name)
            .Select(u => new WineSurferUserSummary(u.Id, u.Name, u.TasteProfile ?? string.Empty))
            .ToList();

        return Json(response);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] WineSurferLoginRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var trimmedName = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            ModelState.AddModelError(nameof(request.Name), "Name is required.");
            return ValidationProblem(ModelState);
        }

        var trimmedTasteProfile = request.TasteProfile?.Trim() ?? string.Empty;

        User? user;
        if (request.CreateIfMissing)
        {
            user = await _userRepository.GetOrCreateAsync(trimmedName, trimmedTasteProfile, cancellationToken);
        }
        else
        {
            user = await _userRepository.FindByNameAsync(trimmedName, cancellationToken);
            if (user is null)
            {
                ModelState.AddModelError(nameof(request.Name), "We couldn't find a matching profile.");
                return ValidationProblem(ModelState);
            }

            if (!string.IsNullOrWhiteSpace(trimmedTasteProfile) && !string.Equals(user.TasteProfile, trimmedTasteProfile, StringComparison.Ordinal))
            {
                user.TasteProfile = trimmedTasteProfile;
                await _userRepository.UpdateAsync(user, cancellationToken);
            }
        }

        var response = new WineSurferLoginResponse(user.Id, user.Name, user.TasteProfile ?? string.Empty);
        return Ok(response);
    }

    private static RegionInventoryMetrics CalculateRegionInventoryMetrics(IEnumerable<Wine> wines)
    {
        var bottleList = wines
            .SelectMany(wine => wine.WineVintages ?? Enumerable.Empty<WineVintage>())
            .SelectMany(vintage => vintage.Bottles ?? Enumerable.Empty<Bottle>())
            .ToList();

        var cellared = bottleList.Count(bottle => !bottle.IsDrunk);
        var consumed = bottleList.Count(bottle => bottle.IsDrunk);

        var tastingNotes = bottleList
            .SelectMany(bottle => bottle.TastingNotes ?? Enumerable.Empty<TastingNote>())
            .Where(note => note.Score.HasValue)
            .ToList();

        var scoreValues = tastingNotes
            .Select(note => note.Score!.Value)
            .ToList();

        decimal? averageScore = scoreValues.Count > 0
            ? Math.Round(scoreValues.Average(), 1, MidpointRounding.AwayFromZero)
            : null;

        var userAverageScores = tastingNotes
            .GroupBy(note => note.UserId)
            .Select(group => new RegionUserAverageScore(
                group.Key,
                Math.Round(group.Average(note => note.Score!.Value), 1, MidpointRounding.AwayFromZero)))
            .OrderBy(entry => entry.UserId)
            .ToList();

        return new RegionInventoryMetrics(cellared, consumed, averageScore, userAverageScores);
    }

    private static MapHighlightPoint? CreateHighlightPoint(
        Region region,
        RegionInventoryMetrics metrics)
    {
        var countryName = region.Country?.Name;
        if (!string.IsNullOrWhiteSpace(region.Name) && RegionCoordinates.TryGetValue(region.Name, out var regionCoord))
        {
            return new MapHighlightPoint(
                region.Name,
                countryName ?? string.Empty,
                regionCoord.Latitude,
                regionCoord.Longitude,
                metrics.BottlesCellared,
                metrics.BottlesConsumed,
                metrics.AverageScore,
                metrics.UserAverageScores);
        }

        if (!string.IsNullOrWhiteSpace(countryName) && CountryCoordinates.TryGetValue(countryName, out var countryCoord))
        {
            var label = string.IsNullOrWhiteSpace(region.Name)
                ? countryName
                : $"{region.Name}, {countryName}";
            return new MapHighlightPoint(
                label,
                countryName,
                countryCoord.Latitude,
                countryCoord.Longitude,
                metrics.BottlesCellared,
                metrics.BottlesConsumed,
                metrics.AverageScore,
                metrics.UserAverageScores);
        }

        return null;
    }
}

public record WineSurferLandingViewModel(IReadOnlyList<MapHighlightPoint> HighlightPoints);

public record MapHighlightPoint(
    string Label,
    string? Country,
    double Latitude,
    double Longitude,
    int BottlesCellared,
    int BottlesConsumed,
    decimal? AverageScore,
    IReadOnlyList<RegionUserAverageScore> UserAverageScores);

public record RegionInventoryMetrics(
    int BottlesCellared,
    int BottlesConsumed,
    decimal? AverageScore,
    IReadOnlyList<RegionUserAverageScore> UserAverageScores);

public record RegionUserAverageScore(Guid UserId, decimal AverageScore);

public record WineSurferUserSummary(Guid Id, string Name, string TasteProfile);

public class WineSurferLoginRequest
{
    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(512)]
    public string? TasteProfile { get; set; }

    public bool CreateIfMissing { get; set; }
}

public record WineSurferLoginResponse(Guid UserId, string Name, string TasteProfile);
