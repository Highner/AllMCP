using System.Text.Json.Nodes;
using AllMCPSolution.Attributes;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;

namespace AllMCPSolution.Tools;

[McpTool("create_region", "Creates a new region and links it to a country.")]
public sealed class CreateRegionTool : RegionToolBase
{
    public CreateRegionTool(IRegionRepository regionRepository, ICountryRepository countryRepository)
        : base(regionRepository, countryRepository)
    {
    }

    public override string Name => "create_region";
    public override string Description => "Creates a new region.";
    public override string Title => "Create Region";
    protected override string InvokingMessage => "Creating region…";
    protected override string InvokedMessage => "Region created.";

    protected override async Task<CrudOperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var name = ParameterHelpers.GetStringParameter(parameters, "name", "name")?.Trim();
        var countryId = ParameterHelpers.GetGuidParameter(parameters, "countryId", "country_id");
        var countryName = ParameterHelpers.GetStringParameter(parameters, "country", "country")?.Trim();

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("'name' is required.");
        }

        if (countryId is null && string.IsNullOrWhiteSpace(countryName))
        {
            errors.Add("Either 'countryId' or 'country' must be provided.");
        }

        if (errors.Count > 0)
        {
            return Failure("create", "Validation failed.", errors);
        }

        Country? country = null;
        if (countryId is not null)
        {
            country = await CountryRepository.GetByIdAsync(countryId.Value, ct);
            if (country is null)
            {
                errors.Add($"Country with id '{countryId}' was not found.");
            }
        }

        if (country is null && !string.IsNullOrWhiteSpace(countryName))
        {
            country = await CountryRepository.FindByNameAsync(countryName!, ct);
            if (country is null)
            {
                var suggestions = await CountryRepository.SearchByApproximateNameAsync(countryName!, 5, ct);
                return Failure("create",
                    $"Country '{countryName}' was not found.",
                    new[] { $"Country '{countryName}' was not found." },
                    new
                    {
                        type = "country",
                        query = countryName,
                        suggestions = suggestions.Select(BottleResponseMapper.MapCountry).ToArray()
                    });
            }
        }

        if (country is null)
        {
            return Failure("create", "Country could not be resolved.", errors);
        }

        var existing = await RegionRepository.FindByNameAndCountryAsync(name!, country.Id, ct);
        if (existing is not null)
        {
            return Failure("create",
                $"Region '{name}' already exists in country '{country.Name}'.",
                new[] { $"Region '{name}' already exists in country '{country.Name}'." },
                new
                {
                    type = "region_exists",
                    region = BottleResponseMapper.MapRegion(existing)
                });
        }

        var region = new Region
        {
            Id = Guid.NewGuid(),
            Name = name!,
            CountryId = country.Id
        };

        await RegionRepository.AddAsync(region, ct);
        var persisted = await RegionRepository.GetByIdAsync(region.Id, ct) ?? region;
        return Success("create", "Region created.", BottleResponseMapper.MapRegion(persisted));
    }

    protected override JsonObject BuildInputSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["name"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Name of the region."
                },
                ["countryId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["format"] = "uuid",
                    ["description"] = "Identifier of the country the region belongs to."
                },
                ["country"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Name of the country the region belongs to (used when countryId is not provided)."
                }
            },
            ["required"] = new JsonArray("name")
        };
    }
}
