using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Artworks;

[McpTool("list_artwork_sales_simple", "Lists artwork sales and returns simplified results")]
public class ListArtworkSalesTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;
    private const int PageSize = 40; // Fixed page size

    public ListArtworkSalesTool(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "list_artwork_sales_simple";
    public string Description => "Lists artwork sales using optional filters and pagination. Returns only sale date, category, low estimate, high estimate, and hammer price. Returns 20 results per page ordered by sale date.";
    public string? SafetyLevel => "non_critical";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        parameters ??= new Dictionary<string, object>();

        // Extract parameters using helper methods that support both naming conventions
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
        var page = ParameterHelpers.GetIntParameter(parameters, "page", "page") ?? 1;

        // Build query
        var query = _dbContext.ArtworkSales
            .Include(a => a.Artist)
            .AsQueryable();

        // Apply filters - same logic as SearchArtworkSalesTool
        if (artistId.HasValue)
        {
            query = query.Where(a => a.ArtistId == artistId.Value);
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            query = query.Where(a => a.Name.Contains(name));
        }

        if (minHeight.HasValue)
        {
            query = query.Where(a => a.Height >= minHeight.Value);
        }

        if (maxHeight.HasValue)
        {
            query = query.Where(a => a.Height <= maxHeight.Value);
        }

        if (minWidth.HasValue)
        {
            query = query.Where(a => a.Width >= minWidth.Value);
        }

        if (maxWidth.HasValue)
        {
            query = query.Where(a => a.Width <= maxWidth.Value);
        }

        if (yearCreatedFrom.HasValue)
        {
            query = query.Where(a => a.YearCreated >= yearCreatedFrom.Value);
        }

        if (yearCreatedTo.HasValue)
        {
            query = query.Where(a => a.YearCreated <= yearCreatedTo.Value);
        }

        if (saleDateFrom.HasValue)
        {
            query = query.Where(a => a.SaleDate >= saleDateFrom.Value);
        }

        if (saleDateTo.HasValue)
        {
            query = query.Where(a => a.SaleDate <= saleDateTo.Value);
        }

        if (!string.IsNullOrWhiteSpace(technique))
        {
            query = query.Where(a => a.Technique.Contains(technique));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(a => a.Category.Contains(category));
        }

        if (!string.IsNullOrWhiteSpace(currency))
        {
            query = query.Where(a => a.Currency == currency);
        }

        if (minLowEstimate.HasValue)
        {
            query = query.Where(a => a.LowEstimate >= minLowEstimate.Value);
        }

        if (maxLowEstimate.HasValue)
        {
            query = query.Where(a => a.LowEstimate <= maxLowEstimate.Value);
        }

        if (minHighEstimate.HasValue)
        {
            query = query.Where(a => a.HighEstimate >= minHighEstimate.Value);
        }

        if (maxHighEstimate.HasValue)
        {
            query = query.Where(a => a.HighEstimate <= maxHighEstimate.Value);
        }

        if (minHammerPrice.HasValue)
        {
            query = query.Where(a => a.HammerPrice >= minHammerPrice.Value);
        }

        if (maxHammerPrice.HasValue)
        {
            query = query.Where(a => a.HammerPrice <= maxHammerPrice.Value);
        }

        if (sold.HasValue)
        {
            query = query.Where(a => a.Sold == sold.Value);
        }

        // Get total count
        var totalCount = await query.CountAsync();

        // Apply ordering and pagination
        var results = await query
            .OrderByDescending(a => a.SaleDate)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(a => new
            {
                SaleDate = a.SaleDate,
                Title = a.Name,
                a.Category,
                a.Technique,
                a.YearCreated,
                LowEstimate = a.LowEstimate,
                HighEstimate = a.HighEstimate,
                HammerPrice = a.HammerPrice
            })
            .ToListAsync();

        return new
        {
            data = results,
            page,
            pageSize = PageSize,
            totalCount,
            totalPages = (int)Math.Ceiling((double)totalCount / PageSize)
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
                properties = new
                {
                    artistId = new
                    {
                        type = "string",
                        description = "Filter by artist ID (exact match)"
                    },
                    name = new
                    {
                        type = "string",
                        description = "Filter by artwork name (partial match)"
                    },
                    minHeight = new
                    {
                        type = "number",
                        description = "Minimum height in cm"
                    },
                    maxHeight = new
                    {
                        type = "number",
                        description = "Maximum height in cm"
                    },
                    minWidth = new
                    {
                        type = "number",
                        description = "Minimum width in cm"
                    },
                    maxWidth = new
                    {
                        type = "number",
                        description = "Maximum width in cm"
                    },
                    yearCreatedFrom = new
                    {
                        type = "integer",
                        description = "Start year for creation year filter"
                    },
                    yearCreatedTo = new
                    {
                        type = "integer",
                        description = "End year for creation year filter"
                    },
                    saleDateFrom = new
                    {
                        type = "string",
                        format = "date-time",
                        description = "Start date for sale date filter (ISO 8601 format)"
                    },
                    saleDateTo = new
                    {
                        type = "string",
                        format = "date-time",
                        description = "End date for sale date filter (ISO 8601 format)"
                    },
                    technique = new
                    {
                        type = "string",
                        description = "Filter by technique (partial match)"
                    },
                    category = new
                    {
                        type = "string",
                        description = "Filter by category (partial match)"
                    },
                    currency = new
                    {
                        type = "string",
                        description = "Filter by currency (exact match)"
                    },
                    minLowEstimate = new
                    {
                        type = "number",
                        description = "Minimum low estimate"
                    },
                    maxLowEstimate = new
                    {
                        type = "number",
                        description = "Maximum low estimate"
                    },
                    minHighEstimate = new
                    {
                        type = "number",
                        description = "Minimum high estimate"
                    },
                    maxHighEstimate = new
                    {
                        type = "number",
                        description = "Maximum high estimate"
                    },
                    minHammerPrice = new
                    {
                        type = "number",
                        description = "Minimum hammer price"
                    },
                    maxHammerPrice = new
                    {
                        type = "number",
                        description = "Maximum hammer price"
                    },
                    sold = new
                    {
                        type = "boolean",
                        description = "Filter by sold status"
                    },
                    page = new
                    {
                        type = "integer",
                        description = "Page number (default: 1)"
                    }
                }
            }
        };
    }

        public object GetOpenApiSchema()
    {
        return new
        {
            summary = Description,
            operationId = Name,
            requestBody = new
            {
                required = false,
                content = new Dictionary<string, object>
                {
                    ["application/json"] = new
                    {
                        schema = new
                        {
                            type = "object",
                            properties = ParameterHelpers.CreateOpenApiProperties()
                        }
                    }
                }
            },
            responses = new
            {
                _200 = new
                {
                    description = "Success",
                    content = new Dictionary<string, object>
                    {
                        ["application/json"] = new
                        {
                            schema = new
                            {
                                type = "object",
                                properties = new
                                {
                                    result = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            data = new
                                            {
                                                type = "array",
                                                items = new
                                                {
                                                    type = "object",
                                                    properties = new
                                                    {
                                                        saleDate = new { type = "string", format = "date-time" },
                                                        title = new { type = "string" },
                                                        category = new { type = "string" },
                                                        technique = new { type = "string" },
                                                        lowEstimate = new { type = "number" },
                                                        highEstimate = new { type = "number" },
                                                        hammerPrice = new { type = "number" }
                                                    }
                                                }
                                            },
                                            page = new { type = "integer" },
                                            pageSize = new { type = "integer" },
                                            totalCount = new { type = "integer" },
                                            totalPages = new { type = "integer" }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
    }
}