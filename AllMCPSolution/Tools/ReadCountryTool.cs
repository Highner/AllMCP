using System.Text.Json.Nodes;
using AllMCPSolution.Attributes;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;

namespace AllMCPSolution.Tools;

[McpTool("read_country", "Retrieves an existing country by id or name.")]
public sealed class ReadCountryTool : CountryToolBase
{
    public ReadCountryTool(ICountryRepository countryRepository)
        : base(countryRepository)
    {
    }

    public override string Name => "read_country";
    public override string Description => "Retrieves an existing country.";
    public override string Title => "Read Country";
    protected override string InvokingMessage => "Fetching country…";
    protected override string InvokedMessage => "Country loaded.";

    protected override async Task<CrudOperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var id = ParameterHelpers.GetGuidParameter(parameters, "id", "id");
        var name = ParameterHelpers.GetStringParameter(parameters, "name", "name")?.Trim();

        if (id is null && string.IsNullOrWhiteSpace(name))
        {
            return Failure("read",
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

            return Failure("read",
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

        return Success("read", "Country retrieved.", BottleResponseMapper.MapCountry(country));
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
                    ["description"] = "Country identifier."
                },
                ["name"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Country name."
                }
            },
            ["required"] = new JsonArray()
        };
    }
}
