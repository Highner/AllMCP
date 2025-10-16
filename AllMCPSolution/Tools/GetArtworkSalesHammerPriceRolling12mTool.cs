using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Services;

namespace AllMCPSolution.Artworks;

[McpTool("get_artwork_sales_hammer_price_rolling_12m", "Returns 12-month rolling averages of hammer prices, including inflation-adjusted values, one data point per month.")]
public class GetArtworkSalesHammerPriceRolling12mTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IInflationService _inflationService;

    public GetArtworkSalesHammerPriceRolling12mTool(ApplicationDbContext dbContext, IInflationService inflationService)
    {
        _dbContext = dbContext;
        _inflationService = inflationService;
    }

    public string Name => "get_artwork_sales_hammer_price_rolling_12m";
    public string Description => "Returns a monthly series where each point is the 12-month rolling average of hammer prices and inflation-adjusted hammer prices.";
    public string? SafetyLevel => "non_critical";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        parameters ??= new Dictionary<string, object>();

        var artistId = ParameterHelpers.GetGuidParameter(parameters, "artistId", "artist_id");
        var name = ParameterHelpers.GetStringParameter(parameters, "name", "name");
        var minHeight = ParameterHelpers.GetDecimalParameter(parameters, "minHeight", "min_height");
        var maxHeight = ParameterHelpers.GetDecimalParameter(parameters, "maxHeight", "max_height");
        var minWidth = ParameterHelpers.GetDecimalParameter(parameters, "minWidth", "min_width");
        var maxWidth = ParameterHelpers.GetDecimalParameter(parameters, "maxWidth", "max_width");
        var yearCreatedFrom = ParameterHelpers.GetIntParameter(parameters, "yearCreatedFrom", "year_created_from");
        var yearCreatedTo = ParameterHelpers.GetIntParameter(parameters, "yearCreatedTo", "year_created_to");
        var saleDateFrom = ParameterHelpers.GetDateTimeParameter(parameters, "saleDateFrom", "sale_date_from");
        var saleDateTo = ParameterHelpers.GetDateTimeParameter(parameters, "saleDateTo", "sale_date_to");
        var technique = ParameterHelpers.GetStringParameter(parameters, "technique", "technique");
        var category = ParameterHelpers.GetStringParameter(parameters, "category", "category");
        var currency = ParameterHelpers.GetStringParameter(parameters, "currency", "currency");
        var minLowEstimate = ParameterHelpers.GetDecimalParameter(parameters, "minLowEstimate", "min_low_estimate");
        var maxLowEstimate = ParameterHelpers.GetDecimalParameter(parameters, "maxLowEstimate", "max_low_estimate");
        var minHighEstimate = ParameterHelpers.GetDecimalParameter(parameters, "minHighEstimate", "min_high_estimate");
        var maxHighEstimate = ParameterHelpers.GetDecimalParameter(parameters, "maxHighEstimate", "max_high_estimate");
        var minHammerPrice = ParameterHelpers.GetDecimalParameter(parameters, "minHammerPrice", "min_hammer_price");
        var maxHammerPrice = ParameterHelpers.GetDecimalParameter(parameters, "maxHammerPrice", "max_hammer_price");
        var sold = ParameterHelpers.GetBoolParameter(parameters, "sold", "sold");

        var query = _dbContext.ArtworkSales.Include(a => a.Artist).AsQueryable();

        if (artistId.HasValue) query = query.Where(a => a.ArtistId == artistId.Value);
        if (!string.IsNullOrWhiteSpace(name)) query = query.Where(a => a.Name.Contains(name));
        if (minHeight.HasValue) query = query.Where(a => a.Height >= minHeight.Value);
        if (maxHeight.HasValue) query = query.Where(a => a.Height <= maxHeight.Value);
        if (minWidth.HasValue) query = query.Where(a => a.Width >= minWidth.Value);
        if (maxWidth.HasValue) query = query.Where(a => a.Width <= maxWidth.Value);
        if (yearCreatedFrom.HasValue) query = query.Where(a => a.YearCreated >= yearCreatedFrom.Value);
        if (yearCreatedTo.HasValue) query = query.Where(a => a.YearCreated <= yearCreatedTo.Value);
        if (saleDateFrom.HasValue) query = query.Where(a => a.SaleDate >= saleDateFrom.Value);
        if (saleDateTo.HasValue) query = query.Where(a => a.SaleDate <= saleDateTo.Value);
        if (!string.IsNullOrWhiteSpace(technique)) query = query.Where(a => a.Technique.Contains(technique));
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(a => a.Category.Contains(category));
        if (!string.IsNullOrWhiteSpace(currency)) query = query.Where(a => a.Currency == currency);
        if (minLowEstimate.HasValue) query = query.Where(a => a.LowEstimate >= minLowEstimate.Value);
        if (maxLowEstimate.HasValue) query = query.Where(a => a.LowEstimate <= maxLowEstimate.Value);
        if (minHighEstimate.HasValue) query = query.Where(a => a.HighEstimate >= minHighEstimate.Value);
        if (maxHighEstimate.HasValue) query = query.Where(a => a.HighEstimate <= maxHighEstimate.Value);
        if (minHammerPrice.HasValue) query = query.Where(a => a.HammerPrice >= minHammerPrice.Value);
        if (maxHammerPrice.HasValue) query = query.Where(a => a.HammerPrice <= maxHammerPrice.Value);
        if (sold.HasValue) query = query.Where(a => a.Sold == sold.Value);

        var sales = await query
            .OrderBy(a => a.SaleDate)
            .Select(a => new { a.SaleDate, a.HammerPrice })
            .ToListAsync();

        if (sales.Count == 0)
        {
            return new { timeSeries = Array.Empty<object>(), count = 0, description = "No data found for the specified filters." };
        }

        // Build monthly buckets
        var firstMonth = new DateTime(sales.First().SaleDate.Year, sales.First().SaleDate.Month, 1);
        var lastMonth = new DateTime(sales.Last().SaleDate.Year, sales.Last().SaleDate.Month, 1);

        var monthly = new List<(DateTime Month, decimal AvgNominal, decimal AvgAdj, int Count)>();
        var indexByMonth = new Dictionary<DateTime, (decimal sumNominal, decimal sumAdj, int count)>();

        foreach (var s in sales)
        {
            var m = new DateTime(s.SaleDate.Year, s.SaleDate.Month, 1);
            if (!indexByMonth.TryGetValue(m, out var agg)) agg = (0m, 0m, 0);
            var adj = await _inflationService.AdjustAmountAsync(s.HammerPrice, s.SaleDate);
            agg.sumNominal += s.HammerPrice;
            agg.sumAdj += adj;
            agg.count += 1;
            indexByMonth[m] = agg;
        }

        // Fill full month range and compute monthly averages
        var months = new List<DateTime>();
        for (var dt = firstMonth; dt <= lastMonth; dt = dt.AddMonths(1)) months.Add(dt);

        foreach (var m in months)
        {
            if (indexByMonth.TryGetValue(m, out var agg))
            {
                monthly.Add((m, agg.sumNominal / agg.count, agg.sumAdj / agg.count, agg.count));
            }
            else
            {
                monthly.Add((m, 0m, 0m, 0));
            }
        }

        // Compute rolling 12-month averages using helper (weighted by monthly counts)
        var rollingNom = RollingAverageHelper.RollingAverage(
            monthly.ConvertAll(x => (x.Month, x.AvgNominal, x.Count)),
            windowMonths: 12,
            weightByCount: true);

        var rollingAdj = RollingAverageHelper.RollingAverage(
            monthly.ConvertAll(x => (x.Month, x.AvgAdj, x.Count)),
            windowMonths: 12,
            weightByCount: true);

        var series = new List<object>(monthly.Count);
        for (int i = 0; i < monthly.Count; i++)
        {
            var rpNom = rollingNom[i];
            var rpAdj = rollingAdj[i];
            series.Add(new
            {
                Time = monthly[i].Month,
                CountInWindow = rpNom.CountInWindow,
                Rolling12mHammerPrice = rpNom.Value,
                Rolling12mHammerPriceInflationAdjusted = rpAdj.Value
            });
        }

        return new
        {
            timeSeries = series,
            count = series.Count,
            description = "Each monthly point represents the 12-month rolling average of (a) average monthly hammer price and (b) average monthly inflation-adjusted hammer price. Months without sales are included with null rolling values until sufficient data accumulates."
        };
    }

    public object GetToolDefinition()
    {
        return new
        {
            name = Name,
            description = Description,
            inputSchema = new
            {
                type = "object",
                properties = ParameterHelpers.CreateOpenApiProperties(_dbContext),
                required = Array.Empty<string>()
            }
        };
    }

    public object GetOpenApiSchema()
    {
        return new
        {
            operationId = Name,
            summary = Description,
            description = Description,
            requestBody = new
            {
                required = false,
                content = new
                {
                    application__json = new
                    {
                        schema = new
                        {
                            type = "object",
                            properties = ParameterHelpers.CreateOpenApiProperties(_dbContext)
                        }
                    }
                }
            },
            responses = new
            {
                _200 = new
                {
                    description = "Successful response",
                    content = new
                    {
                        application__json = new
                        {
                            schema = new { type = "object" }
                        }
                    }
                }
            }
        };
    }
}