using AllMCPSolution.Services;
using Microsoft.AspNetCore.Mvc;

namespace AllMCPSolution.Controllers;

[ApiController]
[Route("api/hammer-per-area")] // GET /api/hammer-per-area
public class HammerPerAreaController : ControllerBase
{
    private readonly IHammerPerAreaAnalyticsService _service;

    public HammerPerAreaController(IHammerPerAreaAnalyticsService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] HammerPerAreaQuery query, CancellationToken ct)
    {
        var filter = new HammerPerAreaFilter
        {
            ArtistId = query.ArtistId,
            Name = query.Name,
            MinHeight = query.MinHeight,
            MaxHeight = query.MaxHeight,
            MinWidth = query.MinWidth,
            MaxWidth = query.MaxWidth,
            YearCreatedFrom = query.YearCreatedFrom,
            YearCreatedTo = query.YearCreatedTo,
            SaleDateFrom = query.SaleDateFrom,
            SaleDateTo = query.SaleDateTo,
            Technique = query.Technique,
            Category = query.Category,
            Currency = query.Currency,
            MinLowEstimate = query.MinLowEstimate,
            MaxLowEstimate = query.MaxLowEstimate,
            MinHighEstimate = query.MinHighEstimate,
            MaxHighEstimate = query.MaxHighEstimate,
            MinHammerPrice = query.MinHammerPrice,
            MaxHammerPrice = query.MaxHammerPrice,
            Sold = query.Sold,
            Page = query.Page ?? 1
        };

        var result = await _service.GetHammerPerAreaAsync(filter, ct);
        return Ok(result);
    }
}

public class HammerPerAreaQuery
{
    public Guid? ArtistId { get; set; }
    public string? Name { get; set; }
    public decimal? MinHeight { get; set; }
    public decimal? MaxHeight { get; set; }
    public decimal? MinWidth { get; set; }
    public decimal? MaxWidth { get; set; }
    public int? YearCreatedFrom { get; set; }
    public int? YearCreatedTo { get; set; }
    public DateTime? SaleDateFrom { get; set; }
    public DateTime? SaleDateTo { get; set; }
    public string? Technique { get; set; }
    public string? Category { get; set; }
    public string? Currency { get; set; }
    public decimal? MinLowEstimate { get; set; }
    public decimal? MaxLowEstimate { get; set; }
    public decimal? MinHighEstimate { get; set; }
    public decimal? MaxHighEstimate { get; set; }
    public decimal? MinHammerPrice { get; set; }
    public decimal? MaxHammerPrice { get; set; }
    public bool? Sold { get; set; }
    public int? Page { get; set; }
}