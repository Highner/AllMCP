using System.Text.Json.Nodes;
using AllMCPSolution.Attributes;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;

namespace AllMCPSolution.Tools;

[McpTool("read_region", "Retrieves a region by id or name.")]
public sealed class ReadRegionTool : RegionToolBase
{
    public ReadRegionTool(IRegionRepository regionRepository, ICountryRepository countryRepository)
        : base(regionRepository, countryRepository)
    {
    }

    public override string Name => "read_region";
    public override string Description => "Retrieves an existing region.";
    public override string Title => "Read Region";
    protected override string InvokingMessage => "Fetching region…";
    protected override string InvokedMessage => "Region loaded.";

    protected override async Task<CrudOperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var id = ParameterHelpers.GetGuidParameter(parameters, "id", "id");
        var name = ParameterHelpers.GetStringParameter(parameters, "name", "name")?.Trim();
        var countryId = ParameterHelpers.GetGuidParameter(parameters, "countryId", "country_id");
        var countryName = ParameterHelpers.GetStringParameter(parameters, "country", "country")?.Trim();

        if (id is null && string.IsNullOrWhiteSpace(name))
        {
            return Failure("read",
                "Either 'id' or 'name' must be provided.",
                new[] { "Either 'id' or 'name' must be provided." });
        }

        Country? country = null;
        if (countryId is not null)
        {
            country = await CountryRepository.GetByIdAsync(countryId.Value, ct);
        }

        if (country is null && !string.IsNullOrWhiteSpace(countryName))
        {
            country = await CountryRepository.FindByNameAsync(countryName!, ct);
        }

        Region? region = null;
        if (id is not null)
        {
            region = await RegionRepository.GetByIdAsync(id.Value, ct);
        }

        if (region is null && !string.IsNullOrWhiteSpace(name))
        {
            if (country is not null)
            {
                region = await RegionRepository.FindByNameAndCountryAsync(name!, country.Id, ct);
            }

            region ??= await RegionRepository.FindByNameAsync(name!, ct);
        }

        if (region is null)
        {
            var suggestions = string.IsNullOrWhiteSpace(name)
                ? Array.Empty<object>()
                : (await RegionRepository.SearchByApproximateNameAsync(name!, 5, ct))
                    .Select(BottleResponseMapper.MapRegion)
                    .ToArray();

            return Failure("read",
                "Region not found.",
                new[]
                {
                    id is not null
                        ? $"Region with id '{id}' was not found."
                        : $"Region '{name}' was not found."
                },
                new
                {
                    type = "region_search",
                    query = name ?? id?.ToString(),
                    suggestions
                });
        }

        return Success("read", "Region retrieved.", BottleResponseMapper.MapRegion(region));
    }

    protected override JsonObject BuildInputSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["id"] = new JsonObject
                {
                    ["type"] = "string",
                    ["format"] = "uuid",
                    ["description"] = "Region identifier."
                },
                ["name"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Region name."
                },
                ["countryId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["format"] = "uuid",
                    ["description"] = "Country identifier used when searching by name."
                },
                ["country"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Country name used when searching by name."
                }
            },
            ["required"] = new JsonArray()
        };
    }
}
