using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AllMCPSolution.Controllers;

[Route("wine-waves")]
public class WineWavesController : Controller
{
    private static readonly IReadOnlyDictionary<string, (double X, double Y)> RegionCoordinates =
        new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Bordeaux"] = (0.415, 0.348),
            ["Burgundy"] = (0.43, 0.34),
            ["Champagne"] = (0.42, 0.33),
            ["Rhône"] = (0.435, 0.37),
            ["Rhone"] = (0.435, 0.37),
            ["Loire"] = (0.405, 0.34),
            ["Provence"] = (0.445, 0.39),
            ["Tuscany"] = (0.455, 0.37),
            ["Piedmont"] = (0.455, 0.35),
            ["Veneto"] = (0.47, 0.35),
            ["Ribera del Duero"] = (0.39, 0.37),
            ["Ribera Del Duero"] = (0.39, 0.37),
            ["Rioja"] = (0.385, 0.36),
            ["Douro"] = (0.37, 0.38),
            ["Douro Valley"] = (0.37, 0.38),
            ["Mosel"] = (0.44, 0.32),
            ["Rheingau"] = (0.445, 0.32),
            ["Nahe"] = (0.44, 0.33),
            ["Finger Lakes"] = (0.28, 0.33),
            ["Napa Valley"] = (0.22, 0.34),
            ["Sonoma"] = (0.21, 0.34),
            ["Willamette Valley"] = (0.19, 0.32),
            ["Columbia Valley"] = (0.18, 0.31),
            ["Marlborough"] = (0.83, 0.63),
            ["Central Otago"] = (0.84, 0.7),
            ["Barossa"] = (0.78, 0.6),
            ["McLaren Vale"] = (0.78, 0.63),
            ["Mc Laren Vale"] = (0.78, 0.63),
            ["Yarra Valley"] = (0.82, 0.62),
            ["Coonawarra"] = (0.79, 0.64),
            ["Maipo"] = (0.26, 0.54),
            ["Maipo Valley"] = (0.26, 0.54),
            ["Mendoza"] = (0.27, 0.6),
            ["Mendoza Valley"] = (0.27, 0.6),
            ["Stellenbosch"] = (0.5, 0.67)
        };

    private static readonly IReadOnlyDictionary<string, (double X, double Y)> CountryCoordinates =
        new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase)
        {
            ["France"] = (0.415, 0.35),
            ["Italy"] = (0.455, 0.36),
            ["Spain"] = (0.39, 0.38),
            ["Portugal"] = (0.365, 0.39),
            ["Germany"] = (0.44, 0.33),
            ["Austria"] = (0.455, 0.33),
            ["Switzerland"] = (0.44, 0.345),
            ["United States"] = (0.24, 0.34),
            ["United States of America"] = (0.24, 0.34),
            ["USA"] = (0.24, 0.34),
            ["U.S.A."] = (0.24, 0.34),
            ["US"] = (0.24, 0.34),
            ["Canada"] = (0.23, 0.27),
            ["Chile"] = (0.26, 0.56),
            ["Argentina"] = (0.27, 0.62),
            ["Australia"] = (0.79, 0.6),
            ["New Zealand"] = (0.84, 0.66),
            ["South Africa"] = (0.5, 0.64),
            ["England"] = (0.41, 0.31),
            ["United Kingdom"] = (0.41, 0.31),
            ["UK"] = (0.41, 0.31),
            ["Scotland"] = (0.41, 0.28),
            ["Ireland"] = (0.39, 0.32),
            ["Japan"] = (0.74, 0.36),
            ["China"] = (0.68, 0.36),
            ["Georgia"] = (0.51, 0.35),
            ["Greece"] = (0.49, 0.39),
            ["Hungary"] = (0.47, 0.34),
            ["Slovenia"] = (0.47, 0.35),
            ["Croatia"] = (0.475, 0.36),
            ["Uruguay"] = (0.3, 0.66)
        };

    private readonly IWineRepository _wineRepository;

    public WineWavesController(IWineRepository wineRepository)
    {
        _wineRepository = wineRepository;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var wines = await _wineRepository.GetAllAsync(cancellationToken);

        var highlightPoints = wines
            .Where(w => w.SubAppellation?.Appellation?.Region is not null)
            .Select(w => w.SubAppellation!.Appellation!.Region)
            .Where(region => region is not null)
            .Select(region => CreateHighlightPoint(region))
            .Where(point => point is not null)
            .Cast<MapHighlightPoint>()
            .GroupBy(point => new { point.Label, point.X, point.Y })
            .Select(group => group.First())
            .OrderBy(point => point.Label)
            .ToList();

        var model = new WineWavesLandingViewModel(highlightPoints);
        Response.ContentType = "text/html; charset=utf-8";
        return View("Index", model);
    }

    private static MapHighlightPoint? CreateHighlightPoint(Region region)
    {
        var countryName = region.Country?.Name;
        if (!string.IsNullOrWhiteSpace(region.Name) && RegionCoordinates.TryGetValue(region.Name, out var regionCoord))
        {
            return new MapHighlightPoint(region.Name, countryName ?? string.Empty, regionCoord.X, regionCoord.Y);
        }

        if (!string.IsNullOrWhiteSpace(countryName) && CountryCoordinates.TryGetValue(countryName, out var countryCoord))
        {
            var label = string.IsNullOrWhiteSpace(region.Name)
                ? countryName
                : $"{region.Name}, {countryName}";
            return new MapHighlightPoint(label, countryName, countryCoord.X, countryCoord.Y);
        }

        return null;
    }
}

public record WineWavesLandingViewModel(IReadOnlyList<MapHighlightPoint> HighlightPoints);

public record MapHighlightPoint(string Label, string? Country, double X, double Y);
