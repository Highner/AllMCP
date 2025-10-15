using AllMCPSolution.Data;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Services;

public class HammerPerAreaFilter
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
    public int Page { get; set; } = 1;
}

public interface IHammerPerAreaAnalyticsService
{
    Task<object> GetHammerPerAreaAsync(HammerPerAreaFilter filter, CancellationToken ct = default);
}

public class HammerPerAreaAnalyticsService : IHammerPerAreaAnalyticsService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IInflationService _inflationService;
    private const int MaxResults = 1000;

    public HammerPerAreaAnalyticsService(ApplicationDbContext dbContext, IInflationService inflationService)
    {
        _dbContext = dbContext;
        _inflationService = inflationService;
    }

    public async Task<object> GetHammerPerAreaAsync(HammerPerAreaFilter filter, CancellationToken ct = default)
    {
        var query = _dbContext.ArtworkSales.Include(a => a.Artist).AsQueryable();

        if (filter.ArtistId.HasValue) query = query.Where(a => a.ArtistId == filter.ArtistId.Value);
        if (!string.IsNullOrWhiteSpace(filter.Name)) query = query.Where(a => a.Name.Contains(filter.Name));
        if (filter.MinHeight.HasValue) query = query.Where(a => a.Height >= filter.MinHeight.Value);
        if (filter.MaxHeight.HasValue) query = query.Where(a => a.Height <= filter.MaxHeight.Value);
        if (filter.MinWidth.HasValue) query = query.Where(a => a.Width >= filter.MinWidth.Value);
        if (filter.MaxWidth.HasValue) query = query.Where(a => a.Width <= filter.MaxWidth.Value);
        if (filter.YearCreatedFrom.HasValue) query = query.Where(a => a.YearCreated >= filter.YearCreatedFrom.Value);
        if (filter.YearCreatedTo.HasValue) query = query.Where(a => a.YearCreated <= filter.YearCreatedTo.Value);
        if (filter.SaleDateFrom.HasValue) query = query.Where(a => a.SaleDate >= filter.SaleDateFrom.Value);
        if (filter.SaleDateTo.HasValue) query = query.Where(a => a.SaleDate <= filter.SaleDateTo.Value);
        if (!string.IsNullOrWhiteSpace(filter.Technique)) query = query.Where(a => a.Technique.Contains(filter.Technique));
        if (!string.IsNullOrWhiteSpace(filter.Category)) query = query.Where(a => a.Category.Contains(filter.Category));
        if (!string.IsNullOrWhiteSpace(filter.Currency)) query = query.Where(a => a.Currency == filter.Currency);
        if (filter.MinLowEstimate.HasValue) query = query.Where(a => a.LowEstimate >= filter.MinLowEstimate.Value);
        if (filter.MaxLowEstimate.HasValue) query = query.Where(a => a.LowEstimate <= filter.MaxLowEstimate.Value);
        if (filter.MinHighEstimate.HasValue) query = query.Where(a => a.HighEstimate >= filter.MinHighEstimate.Value);
        if (filter.MaxHighEstimate.HasValue) query = query.Where(a => a.HighEstimate <= filter.MaxHighEstimate.Value);
        if (filter.MinHammerPrice.HasValue) query = query.Where(a => a.HammerPrice >= filter.MinHammerPrice.Value);
        if (filter.MaxHammerPrice.HasValue) query = query.Where(a => a.HammerPrice <= filter.MaxHammerPrice.Value);
        if (filter.Sold.HasValue) query = query.Where(a => a.Sold == filter.Sold.Value);

        var totalCount = await query.CountAsync(cancellationToken: ct);
        var page = filter.Page <= 0 ? 1 : filter.Page;
        var skip = (page - 1) * MaxResults;

        var sales = await query
            .OrderByDescending(a => a.SaleDate)
            .Skip(skip)
            .Take(MaxResults)
            .Select(a => new { a.Name, a.Category, a.Technique, a.YearCreated, a.SaleDate, a.HammerPrice, a.Height, a.Width, a.Sold })
            .ToListAsync(ct);

        var list = new List<object>(sales.Count);
        foreach (var s in sales)
        {
            var area = (s.Height > 0 && s.Width > 0) ? (s.Height * s.Width) : 0m;
            decimal? perArea = area > 0 ? s.HammerPrice / area : null;
            var adjPrice = await _inflationService.AdjustAmountAsync(s.HammerPrice, s.SaleDate, null, ct);
            decimal? perAreaAdj = area > 0 ? adjPrice / area : null;

            list.Add(new
            {
                //Title = s.Name,
                Category = s.Category,
                Technique = s.Technique,
                YearCreated = s.YearCreated,
                Time = s.SaleDate,
                //Height = s.Height,
                //Width = s.Width,
                Area = area,
                Sold = s.Sold,
                //HammerPrice = s.HammerPrice,
                //HammerPricePerArea = perArea,
                HammerPricePerAreaInflationAdjusted = perAreaAdj
            });
        }

        var totalPages = (int)Math.Ceiling((double)totalCount / MaxResults);
        var hasMoreResults = page < totalPages;

        var result = new
        {
            timeSeries = list,
            count = list.Count,
            totalCount,
            totalPages,
            currentPage = page,
            hasMoreResults,
            description = "HammerPricePerArea = HammerPrice/(height*width). Inflation-adjusted uses ECB HICP to convert price to today's value before dividing by area."
        };

        if (hasMoreResults)
        {
            return new
            {
                result.timeSeries,
                result.count,
                result.totalCount,
                result.totalPages,
                result.currentPage,
                result.hasMoreResults,
                nextPageInstructions = $"To get the next page of results, call this tool again with page={page + 1}.",
                result.description
            };
        }

        if (totalPages > 1)
        {
            return new
            {
                result.timeSeries,
                result.count,
                result.totalCount,
                result.totalPages,
                result.currentPage,
                result.hasMoreResults,
                mergeInstructions = $"This is the final page (page {page} of {totalPages}). If you retrieved multiple pages, merge all timeSeries arrays from all pages into one dataset.",
                result.description
            };
        }

        return result;
    }
}