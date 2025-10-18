using System.Text.Json.Nodes;
using AllMCPSolution.Attributes;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;

namespace AllMCPSolution.Tools;

[McpTool("create_country", "Creates a new country for use in geography-driven tools.")]
public sealed class CreateCountryTool : CountryToolBase
{
    public CreateCountryTool(ICountryRepository countryRepository)
        : base(countryRepository)
    {
    }

    public override string Name => "create_country";
    public override string Description => "Creates a new country entry.";
    public override string Title => "Create Country";
    protected override string InvokingMessage => "Creating country…";
    protected override string InvokedMessage => "Country created.";

    protected override async Task<CrudOperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var name = ParameterHelpers.GetStringParameter(parameters, "name", "name")?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return Failure("create", "'name' is required.", new[] { "'name' is required." });
        }

        var existing = await CountryRepository.FindByNameAsync(name, ct);
        if (existing is not null)
        {
            return Failure("create",
                $"Country '{existing.Name}' already exists.",
                new[] { $"Country '{existing.Name}' already exists." },
                new
                {
                    type = "country_exists",
                    country = BottleResponseMapper.MapCountry(existing)
                });
        }

        var country = new Country
        {
            Id = Guid.NewGuid(),
            Name = name
        };

        await CountryRepository.AddAsync(country, ct);
        var persisted = await CountryRepository.GetByIdAsync(country.Id, ct) ?? country;
        return Success("create", "Country created.", BottleResponseMapper.MapCountry(persisted));
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
                    ["description"] = "Name of the country to create."
                }
            },
            ["required"] = new JsonArray("name")
        };
    }
}
