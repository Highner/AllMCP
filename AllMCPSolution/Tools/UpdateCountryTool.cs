using System.Text.Json.Nodes;
using AllMCPSolution.Attributes;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;

namespace AllMCPSolution.Tools;

[McpTool("update_country", "Updates the name of an existing country.")]
public sealed class UpdateCountryTool : CountryToolBase
{
    public UpdateCountryTool(ICountryRepository countryRepository)
        : base(countryRepository)
    {
    }

    public override string Name => "update_country";
    public override string Description => "Updates an existing country.";
    public override string Title => "Update Country";
    protected override string InvokingMessage => "Updating country…";
    protected override string InvokedMessage => "Country updated.";

    protected override async Task<CrudOperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var id = ParameterHelpers.GetGuidParameter(parameters, "id", "id");
        var name = ParameterHelpers.GetStringParameter(parameters, "name", "name")?.Trim();
        var newName = ParameterHelpers.GetStringParameter(parameters, "newName", "new_name")?.Trim();

        var errors = new List<string>();
        if (id is null && string.IsNullOrWhiteSpace(name))
        {
            errors.Add("Either 'id' or 'name' must be provided to locate the country.");
        }

        if (string.IsNullOrWhiteSpace(newName))
        {
            errors.Add("'newName' is required.");
        }

        if (errors.Count > 0)
        {
            return Failure("update", "Validation failed.", errors);
        }

        Country? country = null;
        if (id is not null)
        {
            country = await CountryRepository.GetByIdAsync(id.Value, ct);
        }

        if (country is null && !string.IsNullOrWhiteSpace(name))
        {
            country = await CountryRepository.FindByNameAsync(name!, ct);
        }

        if (country is null)
        {
            var suggestions = string.IsNullOrWhiteSpace(name)
                ? Array.Empty<object>()
                : (await CountryRepository.SearchByApproximateNameAsync(name!, 5, ct))
                    .Select(BottleResponseMapper.MapCountry)
                    .ToArray();

            return Failure("update",
                "Country not found.",
                new[]
                {
                    id is not null
                        ? $"Country with id '{id}' was not found."
                        : $"Country '{name}' was not found."
                },
                new
                {
                    type = "country_search",
                    query = name ?? id?.ToString(),
                    suggestions
                });
        }

        if (!string.Equals(country.Name, newName!, StringComparison.OrdinalIgnoreCase))
        {
            var duplicate = await CountryRepository.FindByNameAsync(newName!, ct);
            if (duplicate is not null && duplicate.Id != country.Id)
            {
                return Failure("update",
                    $"Country '{newName}' already exists.",
                    new[] { $"Country '{newName}' already exists." },
                    new
                    {
                        type = "country_exists",
                        country = BottleResponseMapper.MapCountry(duplicate)
                    });
            }
        }

        country.Name = newName!;
        await CountryRepository.UpdateAsync(country, ct);
        var updated = await CountryRepository.GetByIdAsync(country.Id, ct) ?? country;
        return Success("update", "Country updated.", BottleResponseMapper.MapCountry(updated));
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
                    ["description"] = "Identifier of the country to update."
                },
                ["name"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Name of the country to update when id is not provided."
                },
                ["newName"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "New name for the country."
                }
            },
            ["required"] = new JsonArray("newName")
        };
    }
}
