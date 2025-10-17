using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Attributes;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;

namespace AllMCPSolution.Tools;

[McpTool("update_bottle", "Updates bottle information and validates any referenced wine metadata.")]
public sealed class UpdateBottleTool : BottleToolBase
{
    private readonly string[] _colorOptions = Enum.GetNames(typeof(WineColor));

    public UpdateBottleTool(
        IBottleRepository bottleRepository,
        IWineRepository wineRepository,
        ICountryRepository countryRepository,
        IRegionRepository regionRepository)
        : base(bottleRepository, wineRepository, countryRepository, regionRepository)
    {
    }

    public override string Name => "update_bottle";
    public override string Description => "Updates bottle information and validates any referenced wine metadata.";
    public override string Title => "Update Bottle";
    protected override string InvokingMessage => "Updating bottle…";
    protected override string InvokedMessage => "Bottle updated.";

    protected override async Task<BottleOperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var id = ParameterHelpers.GetGuidParameter(parameters, "id", "id");
        if (id is null)
        {
            return Failure("update", "'id' is required.", new[] { "'id' is required." });
        }

        var bottle = await BottleRepository.GetByIdAsync(id.Value, ct);
        if (bottle is null)
        {
            return Failure("update", $"Bottle with id {id} was not found.", new[] { $"Bottle with id {id} was not found." });
        }

        var vintage = ParameterHelpers.GetIntParameter(parameters, "vintage", "vintage");
        var price = ParameterHelpers.GetDecimalParameter(parameters, "price", "price");
        var score = ParameterHelpers.GetDecimalParameter(parameters, "score", "score");
        var tastingNote = ParameterHelpers.GetStringParameter(parameters, "tastingNote", "tasting_note")
            ?? ParameterHelpers.GetStringParameter(parameters, "tastingNotes", "tasting_notes");
        var name = ParameterHelpers.GetStringParameter(parameters, "name", "name");
        var colorInput = ParameterHelpers.GetStringParameter(parameters, "color", "color");
        var countryName = ParameterHelpers.GetStringParameter(parameters, "country", "country");
        var regionName = ParameterHelpers.GetStringParameter(parameters, "region", "region");

        WineColor? color = null;
        if (!string.IsNullOrWhiteSpace(colorInput))
        {
            if (Enum.TryParse<WineColor>(colorInput, true, out var parsedColor))
            {
                color = parsedColor;
            }
            else
            {
                return Failure("update", $"Color '{colorInput}' is not recognised.",
                    new[] { $"Color '{colorInput}' is not recognised." },
                    new
                    {
                        type = "color",
                        query = colorInput,
                        suggestions = _colorOptions
                    });
            }
        }

        Country? country = null;
        if (!string.IsNullOrWhiteSpace(countryName))
        {
            country = await CountryRepository.FindByNameAsync(countryName!, ct);
            if (country is null)
            {
                var suggestions = await CountryRepository.SearchByApproximateNameAsync(countryName!, 5, ct);
                return Failure("update", $"Country '{countryName}' was not found.",
                    new[] { $"Country '{countryName}' was not found." },
                    new
                    {
                        type = "country",
                        query = countryName,
                        suggestions = suggestions.Select(BottleResponseMapper.MapCountry).ToList()
                    });
            }
        }

        Region? region = null;
        if (!string.IsNullOrWhiteSpace(regionName))
        {
            region = await RegionRepository.FindByNameAsync(regionName!, ct);
            if (region is null)
            {
                var suggestions = await RegionRepository.SearchByApproximateNameAsync(regionName!, 5, ct);
                return Failure("update", $"Region '{regionName}' was not found.",
                    new[] { $"Region '{regionName}' was not found." },
                    new
                    {
                        type = "region",
                        query = regionName,
                        suggestions = suggestions.Select(BottleResponseMapper.MapRegion).ToList()
                    });
            }
        }

        Wine targetWine;
        if (!string.IsNullOrWhiteSpace(name))
        {
            var found = await WineRepository.FindByNameAsync(name!, ct);
            if (found is null)
            {
                var suggestions = await WineRepository.FindClosestMatchesAsync(name!, 5, ct);
                return Failure("update", $"Wine '{name}' does not exist.",
                    new[] { $"Wine '{name}' does not exist." },
                    new
                    {
                        type = "wine",
                        query = name,
                        suggestions = suggestions.Select(BottleResponseMapper.MapWineSummary).ToList()
                    });
            }

            targetWine = found;
        }
        else
        {
            targetWine = bottle.Wine ?? await WineRepository.GetByIdAsync(bottle.WineId, ct)
                ?? throw new InvalidOperationException($"Wine {bottle.WineId} referenced by bottle {bottle.Id} could not be resolved.");
        }

        if (color.HasValue && targetWine.Color != color)
        {
            return Failure("update", $"Wine '{targetWine.Name}' exists with color '{targetWine.Color}'.",
                new[] { $"Wine '{targetWine.Name}' exists with color '{targetWine.Color}'." },
                new
                {
                    type = "wine_color_mismatch",
                    requested = color.Value.ToString(),
                    actual = targetWine.Color.ToString()
                });
        }

        if (country is not null && targetWine.CountryId != country.Id)
        {
            return Failure("update", $"Wine '{targetWine.Name}' is recorded for country '{targetWine.Country?.Name ?? "unknown"}'.",
                new[] { $"Wine '{targetWine.Name}' is recorded for country '{targetWine.Country?.Name ?? "unknown"}'." },
                new
                {
                    type = "wine_country_mismatch",
                    requested = new { name = country.Name, id = country.Id },
                    actual = targetWine.Country is null ? null : new { name = targetWine.Country.Name, id = targetWine.Country.Id }
                });
        }

        if (region is not null && targetWine.RegionId != region.Id)
        {
            return Failure("update", $"Wine '{targetWine.Name}' is recorded for region '{targetWine.Region?.Name ?? "unknown"}'.",
                new[] { $"Wine '{targetWine.Name}' is recorded for region '{targetWine.Region?.Name ?? "unknown"}'." },
                new
                {
                    type = "wine_region_mismatch",
                    requested = new { name = region.Name, id = region.Id },
                    actual = targetWine.Region is null ? null : new { name = targetWine.Region.Name, id = targetWine.Region.Id }
                });
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            bottle.WineId = targetWine.Id;
        }

        if (vintage.HasValue)
        {
            bottle.Vintage = vintage.Value;
        }

        if (price.HasValue)
        {
            bottle.Price = price;
        }

        if (score.HasValue)
        {
            bottle.Score = score;
        }

        if (tastingNote is not null)
        {
            bottle.TastingNote = tastingNote.Trim();
        }

        await BottleRepository.UpdateAsync(bottle, ct);

        var updated = await BottleRepository.GetByIdAsync(bottle.Id, ct) ?? new Bottle
        {
            Id = bottle.Id,
            WineId = bottle.WineId,
            Vintage = bottle.Vintage,
            Price = bottle.Price,
            Score = bottle.Score,
            TastingNote = bottle.TastingNote,
            Wine = targetWine
        };

        return Success("update", "Bottle updated successfully.", new
        {
            bottle = BottleResponseMapper.MapBottle(updated)
        });
    }

    protected override JsonObject BuildInputSchema()
    {
        var properties = new JsonObject
        {
            ["id"] = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "uuid",
                ["description"] = "Identifier of the bottle to update."
            },
            ["name"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Optional wine name to reassign this bottle to."
            },
            ["vintage"] = new JsonObject
            {
                ["type"] = "integer",
                ["description"] = "Updated vintage year."
            },
            ["price"] = new JsonObject
            {
                ["type"] = "number",
                ["description"] = "Updated price."
            },
            ["score"] = new JsonObject
            {
                ["type"] = "number",
                ["description"] = "Updated score."
            },
            ["tastingNote"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Updated tasting notes."
            },
            ["tasting_note"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Snake_case alias for tastingNote."
            },
            ["color"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Optional wine color verification. Valid options: " + string.Join(", ", _colorOptions)
            },
            ["country"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Optional country verification for the target wine."
            },
            ["region"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Optional region verification for the target wine."
            }
        };

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray("id")
        };
    }
}
