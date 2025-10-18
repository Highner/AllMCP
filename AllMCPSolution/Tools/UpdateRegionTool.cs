using System.Text.Json.Nodes;
using AllMCPSolution.Attributes;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;

namespace AllMCPSolution.Tools;

[McpTool("update_region", "Updates an existing region's details.")]
public sealed class UpdateRegionTool : RegionToolBase
{
    public UpdateRegionTool(IRegionRepository regionRepository, ICountryRepository countryRepository)
        : base(regionRepository, countryRepository)
    {
    }

    public override string Name => "update_region";
    public override string Description => "Updates an existing region.";
    public override string Title => "Update Region";
    protected override string InvokingMessage => "Updating region…";
    protected override string InvokedMessage => "Region updated.";

    protected override async Task<CrudOperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var id = ParameterHelpers.GetGuidParameter(parameters, "id", "id");
        var name = ParameterHelpers.GetStringParameter(parameters, "name", "name")?.Trim();
        var countryId = ParameterHelpers.GetGuidParameter(parameters, "countryId", "country_id");
        var countryName = ParameterHelpers.GetStringParameter(parameters, "country", "country")?.Trim();

        var newName = ParameterHelpers.GetStringParameter(parameters, "newName", "new_name")?.Trim();
        var newCountryId = ParameterHelpers.GetGuidParameter(parameters, "newCountryId", "new_country_id");
        var newCountryName = ParameterHelpers.GetStringParameter(parameters, "newCountry", "new_country")?.Trim();

        var errors = new List<string>();
        if (id is null && string.IsNullOrWhiteSpace(name))
        {
            errors.Add("Either 'id' or 'name' must be provided to locate the region.");
        }

        if (string.IsNullOrWhiteSpace(newName) && newCountryId is null && string.IsNullOrWhiteSpace(newCountryName))
        {
            errors.Add("At least one of 'newName', 'newCountryId', or 'newCountry' must be provided.");
        }

        if (errors.Count > 0)
        {
            return Failure("update", "Validation failed.", errors);
        }

        Country? currentCountry = null;
        if (countryId is not null)
        {
            currentCountry = await CountryRepository.GetByIdAsync(countryId.Value, ct);
        }

        if (currentCountry is null && !string.IsNullOrWhiteSpace(countryName))
        {
            currentCountry = await CountryRepository.FindByNameAsync(countryName!, ct);
        }

        Region? region = null;
        if (id is not null)
        {
            region = await RegionRepository.GetByIdAsync(id.Value, ct);
        }

        if (region is null && !string.IsNullOrWhiteSpace(name))
        {
            if (currentCountry is not null)
            {
                region = await RegionRepository.FindByNameAndCountryAsync(name!, currentCountry.Id, ct);
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

            return Failure("update",
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

        Country? targetCountry = region.Country;
        if (newCountryId is not null)
        {
            targetCountry = await CountryRepository.GetByIdAsync(newCountryId.Value, ct);
            if (targetCountry is null)
            {
                errors.Add($"Country with id '{newCountryId}' was not found.");
            }
        }

        if (targetCountry is null && !string.IsNullOrWhiteSpace(newCountryName))
        {
            targetCountry = await CountryRepository.FindByNameAsync(newCountryName!, ct);
            if (targetCountry is null)
            {
                var suggestions = await CountryRepository.SearchByApproximateNameAsync(newCountryName!, 5, ct);
                return Failure("update",
                    $"Country '{newCountryName}' was not found.",
                    new[] { $"Country '{newCountryName}' was not found." },
                    new
                    {
                        type = "country",
                        query = newCountryName,
                        suggestions = suggestions.Select(BottleResponseMapper.MapCountry).ToArray()
                    });
            }
        }

        if (errors.Count > 0)
        {
            return Failure("update", "Validation failed.", errors);
        }

        var finalName = string.IsNullOrWhiteSpace(newName) ? region.Name : newName!;
        var finalCountryId = targetCountry?.Id ?? region.CountryId;

        var duplicate = await RegionRepository.FindByNameAndCountryAsync(finalName, finalCountryId, ct);
        if (duplicate is not null && duplicate.Id != region.Id)
        {
            return Failure("update",
                $"Region '{finalName}' already exists in country '{duplicate.Country?.Name ?? "unknown"}'.",
                new[] { $"Region '{finalName}' already exists in country '{duplicate.Country?.Name ?? "unknown"}'." },
                new
                {
                    type = "region_exists",
                    region = BottleResponseMapper.MapRegion(duplicate)
                });
        }

        region.Name = finalName;
        region.CountryId = finalCountryId;
        await RegionRepository.UpdateAsync(region, ct);
        var updated = await RegionRepository.GetByIdAsync(region.Id, ct) ?? region;
        return Success("update", "Region updated.", BottleResponseMapper.MapRegion(updated));
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
                    ["description"] = "Identifier of the region to update."
                },
                ["name"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Name of the region to update when id is not provided."
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
                },
                ["newName"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "New name for the region."
                },
                ["newCountryId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["format"] = "uuid",
                    ["description"] = "Identifier of the new country for the region."
                },
                ["newCountry"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Name of the new country for the region."
                }
            },
            ["required"] = new JsonArray()
        };
    }
}
