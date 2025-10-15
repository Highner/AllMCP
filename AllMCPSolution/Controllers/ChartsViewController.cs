using System.Text.Json;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;
using Microsoft.AspNetCore.Mvc;

namespace AllMCPSolution.Controllers;

[Route("charts")] // Base route for chart views
public class ChartsController : Controller  // Changed from ChartsViewController to ChartsController
{
    private readonly IArtworkSaleRepository _sales;
    private readonly IHammerPerAreaAnalyticsService _hammerPerArea;

    public ChartsController(IArtworkSaleRepository sales, IHammerPerAreaAnalyticsService hammerPerArea)  // Update constructor name too
    {
        _sales = sales;
        _hammerPerArea = hammerPerArea;
    }

    [HttpGet("ssr")]
    public async Task<IActionResult> Ssr([FromQuery] Guid artistId, [FromQuery] DateTime? dateFrom, [FromQuery] DateTime? dateTo, [FromQuery] string? categories, [FromQuery(Name = "categories")] string[]? categoriesMulti, [FromQuery] string? y1Scale, [FromQuery] string? y2Scale, CancellationToken ct)
    {
        if (artistId == Guid.Empty)
        {
            return BadRequest("artistId is required");
        }

        var categoryList = new List<string>();
        if (!string.IsNullOrWhiteSpace(categories))
        {
            categoryList.AddRange(categories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        if (categoriesMulti != null && categoriesMulti.Length > 0)
        {
            foreach (var c in categoriesMulti)
            {
                if (!string.IsNullOrWhiteSpace(c)) categoryList.Add(c.Trim());
            }
        }
        categoryList = categoryList.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var sales = await _sales.GetSalesAsync(artistId, dateFrom, dateTo, categoryList, ct);
        var perf = await _sales.GetPerformanceSalesAsync(artistId, dateFrom, dateTo, categoryList, ct);

        var hpa = await _hammerPerArea.GetHammerPerAreaAsync(new HammerPerAreaFilter
        {
            ArtistId = artistId,
            SaleDateFrom = dateFrom,
            SaleDateTo = dateTo,
            Category = categoryList.Count == 1 ? categoryList[0] : null,
            Sold = true,
            Page = 1
        }, ct);

        // normalize scales
        string NormalizeScale(string? s, string fallback)
        {
            if (string.Equals(s, "linear", StringComparison.OrdinalIgnoreCase)) return "linear";
            if (string.Equals(s, "logarithmic", StringComparison.OrdinalIgnoreCase)) return "logarithmic";
            return fallback;
        }

        var vm = new ChartSsrViewModel
        {
            ArtistId = artistId,
            DateFrom = dateFrom,
            DateTo = dateTo,
            Categories = categoryList,
            Y1Scale = NormalizeScale(y1Scale, "logarithmic"),
            Y2Scale = NormalizeScale(y2Scale, "linear"),
            Sales = sales.Select(a => new ChartSsrViewModel.SaleItem
            {
                Id = a.Id,
                Name = a.Name,
                Category = a.Category,
                SaleDate = a.SaleDate,
                LowEstimate = a.LowEstimate,
                HighEstimate = a.HighEstimate,
                HammerPrice = a.HammerPrice,
                Currency = a.Currency,
                Height = a.Height,
                Width = a.Width
            }).ToList(),
            Performance = perf.Select(a => new ChartSsrViewModel.PerformanceItem
            {
                Name = a.Name,
                Category = a.Category,
                Technique = a.Technique,
                Height = a.Height,
                Width = a.Width,
                HammerPrice = a.HammerPrice,
                SaleDate = a.SaleDate,
                LowEstimate = a.LowEstimate,
                HighEstimate = a.HighEstimate
            }).ToList(),
            HammerPerArea = hpa
        };

        return View("Ssr", vm);
    }
}

public class ChartSsrViewModel
{
    public Guid ArtistId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public List<string> Categories { get; set; } = new();

    public string Y1Scale { get; set; } = "logarithmic";
    public string Y2Scale { get; set; } = "linear";

    public List<SaleItem> Sales { get; set; } = new();
    public List<PerformanceItem> Performance { get; set; } = new();
    public object? HammerPerArea { get; set; }

    public class SaleItem
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public string? Category { get; set; }
        public DateTime SaleDate { get; set; }
        public decimal LowEstimate { get; set; }
        public decimal HighEstimate { get; set; }
        public decimal HammerPrice { get; set; }
        public string? Currency { get; set; }
        public decimal Height { get; set; }
        public decimal Width { get; set; }
    }

    public class PerformanceItem
    {
        public string? Name { get; set; }
        public string? Category { get; set; }
        public string? Technique { get; set; }
        public decimal Height { get; set; }
        public decimal Width { get; set; }
        public decimal HammerPrice { get; set; }
        public DateTime SaleDate { get; set; }
        public decimal LowEstimate { get; set; }
        public decimal HighEstimate { get; set; }
    }
}