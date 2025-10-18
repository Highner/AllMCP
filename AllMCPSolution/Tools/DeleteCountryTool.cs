using System.Text.Json.Nodes;
using AllMCPSolution.Attributes;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Tools;

[McpTool("delete_country", "Deletes a country when it has no dependent regions.")]
public sealed class DeleteCountryTool : CountryToolBase
{
    public DeleteCountryTool(ICountryRepository countryRepository)
        : base(countryRepository)
    {
    }

    public override string Name => "delete_country";
    public override string Description => "Deletes an existing country.";
    public override string Title => "Delete Country";
    protected override string InvokingMessage => "Deleting country…";
    protected override string InvokedMessage => "Country deleted.";

    protected override async Task<CrudOperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var id = ParameterHelpers.GetGuidParameter(parameters, "id", "id");
        var name = ParameterHelpers.GetStringParameter(parameters, "name", "name")?.Trim();

        if (id is null && string.IsNullOrWhiteSpace(name))
        {
            return Failure("delete",
                "Either 'id' or 'name' must be provided.",
                new[] { "Either 'id' or 'name' must be provided." });
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

            return Failure("delete",
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

        try
        {
            await CountryRepository.DeleteAsync(country.Id, ct);
        }
        catch (DbUpdateException ex)
        {
            return Failure("delete",
                $"Country '{country.Name}' could not be deleted because dependent regions exist.",
                new[]
                {
                    $"Country '{country.Name}' still has dependent regions. Remove or reassign them first."
                },
                new
                {
                    type = "country_delete_restricted",
                    country = BottleResponseMapper.MapCountry(country)
                },
                ex);
        }

        return Success("delete",
            "Country deleted.",
            new
            {
                id = country.Id,
                name = country.Name
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
                    ["description"] = "Identifier of the country to delete."
                },
                ["name"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Name of the country to delete when id is not provided."
                }
            },
            ["required"] = new JsonArray()
        };
    }
}
