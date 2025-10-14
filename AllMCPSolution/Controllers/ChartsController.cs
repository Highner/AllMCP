using Microsoft.AspNetCore.Mvc;
using System.Linq;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;

namespace AllMCPSolution.Charts;

[ApiController]
[Route("api")] // Preserve exact routes
public class ChartsController : ControllerBase
{
    private readonly IArtworkSaleRepository _sales;

    public ChartsController(IArtworkSaleRepository sales)
    {
        _sales = sales;
    }

    // GET /api/categories
    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories(CancellationToken ct)
    {
        var categories = await _sales.GetCategoriesAsync(ct);
        return Ok(categories);
    }

    // GET /api/chart-data
    [HttpGet("chart-data")]
    public async Task<IActionResult> GetChartData(CancellationToken ct)
    {
        var query = HttpContext.Request.Query;

        if (!query.TryGetValue("artistId", out var artistIdStr) ||
            !Guid.TryParse(artistIdStr.ToString(), out var artistId) ||
            artistId == Guid.Empty)
        {
            return BadRequest(new { message = "Valid Artist ID is required" });
        }

        var dateFrom = query.TryGetValue("dateFrom", out var dateFromStr) ? dateFromStr.ToString() : null;
        var dateTo = query.TryGetValue("dateTo", out var dateToStr) ? dateToStr.ToString() : null;
        var categories = query.TryGetValue("categories", out var categoriesStr)
            ? categoriesStr.ToList()
            : new List<string>();

        DateTime? from = null;
        DateTime? to = null;
        if (!string.IsNullOrEmpty(dateFrom) && DateTime.TryParse(dateFrom, out var f)) from = f;
        if (!string.IsNullOrEmpty(dateTo) && DateTime.TryParse(dateTo, out var t)) to = t.AddDays(1).AddSeconds(-1);

        var sales = await _sales.GetSalesAsync(artistId, from, to, categories, ct);
        var projected = sales.Select(a => new
        {
            a.Id,
            a.Name,
            a.Category,
            a.SaleDate,
            a.LowEstimate,
            a.HighEstimate,
            a.HammerPrice,
            a.Currency,
            a.Height,
            a.Width
        }).ToList();

        return Ok(new { sales = projected });
    }

    // GET /api/performance-data
    [HttpGet("performance-data")]
    public async Task<IActionResult> GetPerformanceData(CancellationToken ct)
    {
        var query = HttpContext.Request.Query;

        if (!query.TryGetValue("artistId", out var artistIdStr) ||
            !Guid.TryParse(artistIdStr.ToString(), out var artistId) ||
            artistId == Guid.Empty)
        {
            return BadRequest(new { message = "Valid Artist ID is required" });
        }

        var dateFrom = query.TryGetValue("dateFrom", out var dateFromStr) ? dateFromStr.ToString() : null;
        var dateTo = query.TryGetValue("dateTo", out var dateToStr) ? dateToStr.ToString() : null;
        var categories = query.TryGetValue("categories", out var categoriesStr)
            ? categoriesStr.ToList()
            : new List<string>();

        DateTime? from = null;
        DateTime? to = null;
        if (!string.IsNullOrEmpty(dateFrom) && DateTime.TryParse(dateFrom, out var f)) from = f;
        if (!string.IsNullOrEmpty(dateTo) && DateTime.TryParse(dateTo, out var t)) to = t.AddDays(1).AddSeconds(-1);

        var perfSales = await _sales.GetPerformanceSalesAsync(artistId, from, to, categories, ct);

        var timeSeries = perfSales.Select(sale => new
        {
            Time = sale.SaleDate,
            PerformanceFactor = PerformanceCalculator.CalculatePerformanceFactor(
                sale.HammerPrice,
                sale.LowEstimate,
                sale.HighEstimate)
        }).ToList();

        return Ok(new { timeSeries });
    }
}
