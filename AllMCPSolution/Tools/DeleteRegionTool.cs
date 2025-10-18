using System.Text.Json.Nodes;
using AllMCPSolution.Attributes;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Tools;

[McpTool("delete_region", "Deletes a region when no wines depend on it.")]
public sealed class DeleteRegionTool : RegionToolBase
{
    public DeleteRegionTool(IRegionRepository regionRepository, ICountryRepository countryRepository)
        : base(regionRepository, countryRepository)
    {
    }

    public override string Name => "delete_region";
    public override string Description => "Deletes an existing region.";
    public override string Title => "Delete Region";
    protected override string InvokingMessage => "Deleting region…";
    protected override string InvokedMessage => "Region deleted.";

    protected override async Task<CrudOperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var id = ParameterHelpers.GetGuidParameter(parameters, "id", "id");
        var name = ParameterHelpers.GetStringParameter(parameters, "name", "name")?.Trim();
        var countryId = ParameterHelpers.GetGuidParameter(parameters, "countryId", "country_id");
        var countryName = ParameterHelpers.GetStringParameter(parameters, "country", "country")?.Trim();

        if (id is null && string.IsNullOrWhiteSpace(name))
        {
            return Failure("delete",
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

            return Failure("delete",
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

        try
        {
            await RegionRepository.DeleteAsync(region.Id, ct);
        }
        catch (DbUpdateException ex)
        {
            return Failure("delete",
                $"Region '{region.Name}' could not be deleted because dependent wines exist.",
                new[]
                {
                    $"Region '{region.Name}' still has dependent wines. Remove or reassign them first."
                },
                new
                {
                    type = "region_delete_restricted",
                    region = BottleResponseMapper.MapRegion(region)
                },
                ex);
        }

        return Success("delete",
            "Region deleted.",
            new
            {
                id = region.Id,
                name = region.Name,
                country = region.Country is null
                    ? null
                    : new { id = region.Country.Id, name = region.Country.Name }
            });
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
                    ["description"] = "Identifier of the region to delete."
                },
                ["name"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Name of the region to delete when id is not provided."
                },
                ["countryId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["format"] = "uuid",
                    ["description"] = "Country identifier used to disambiguate the region when searching by name."
                },
                ["country"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Country name used to disambiguate the region when searching by name."
                }
            },
            ["required"] = new JsonArray()
        };
    }
}
