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
        IRegionRepository regionRepository,
        IAppellationRepository appellationRepository,
        IWineVintageRepository wineVintageRepository,
        ITastingNoteRepository tastingNoteRepository)
        : base(bottleRepository, wineRepository, countryRepository, regionRepository, appellationRepository, wineVintageRepository, tastingNoteRepository)
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
        var userId = ParameterHelpers.GetGuidParameter(parameters, "userId", "user_id");
        var name = ParameterHelpers.GetStringParameter(parameters, "name", "name");
        var colorInput = ParameterHelpers.GetStringParameter(parameters, "color", "color");
        var countryName = ParameterHelpers.GetStringParameter(parameters, "country", "country");
        var regionName = ParameterHelpers.GetStringParameter(parameters, "region", "region");
        var appellationName = ParameterHelpers.GetStringParameter(parameters, "appellation", "appellation");
        var isDrunk = ParameterHelpers.GetBoolParameter(parameters, "isDrunk", "is_drunk");
        var drunkAt = ParameterHelpers.GetDateTimeParameter(parameters, "drunkAt", "drunk_at");

        var validationErrors = new List<string>();
        if (drunkAt.HasValue && isDrunk == false)
        {
            validationErrors.Add("'drunkAt' can only be provided when 'isDrunk' is true.");
        }

        if ((score.HasValue || !string.IsNullOrWhiteSpace(tastingNote)) && userId is null)
        {
            validationErrors.Add("'userId' is required when providing tasting notes or scores.");
        }

        if (validationErrors.Count > 0)
        {
            return Failure("update", "Validation failed.", validationErrors);
        }

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

        if (region is not null && country is not null && region.CountryId != country.Id)
        {
            return Failure("update", $"Region '{region.Name}' belongs to country '{region.Country?.Name ?? "unknown"}'.",
                new[] { $"Region '{region.Name}' belongs to country '{region.Country?.Name ?? "unknown"}'." },
                new
                {
                    type = "region_country_mismatch",
                    requestedCountry = new { name = country.Name, id = country.Id },
                    regionCountry = region.Country is null
                        ? null
                        : new { name = region.Country.Name, id = region.Country.Id }
                });
        }

        var normalizedAppellation = string.IsNullOrWhiteSpace(appellationName)
            ? null
            : appellationName.Trim();

        var currentWineVintage = bottle.WineVintage ?? await WineVintageRepository.GetByIdAsync(bottle.WineVintageId, ct)
            ?? throw new InvalidOperationException($"Wine vintage {bottle.WineVintageId} referenced by bottle {bottle.Id} could not be resolved.");

        Wine targetWine;
        if (!string.IsNullOrWhiteSpace(name))
        {
            var found = await WineRepository.FindByNameAsync(name!, normalizedAppellation, ct);
            if (found is null)
            {
                if (!string.IsNullOrWhiteSpace(normalizedAppellation))
                {
                    var existingByName = await WineRepository.FindByNameAsync(name!, null, ct);
                    if (existingByName is not null)
                    {
                        var existingAppellation = string.IsNullOrWhiteSpace(existingByName.Appellation?.Name)
                            ? "unknown"
                            : existingByName.Appellation!.Name;
                        return Failure("update",
                            $"Wine '{name}' is recorded for appellation '{existingAppellation}'.",
                            new[] { $"Wine '{name}' is recorded for appellation '{existingAppellation}'." },
                            new
                            {
                                type = "wine_appellation_mismatch",
                                requested = normalizedAppellation,
                                actual = string.IsNullOrWhiteSpace(existingByName.Appellation?.Name)
                                    ? null
                                    : existingByName.Appellation.Name
                            });
                    }
                }

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
            targetWine = currentWineVintage.Wine ?? await WineRepository.GetByIdAsync(currentWineVintage.WineId, ct)
                ?? throw new InvalidOperationException($"Wine {currentWineVintage.WineId} referenced by bottle {bottle.Id} could not be resolved.");
        }

        var wineCountry = targetWine.Appellation?.Region?.Country;

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

        if (country is not null && wineCountry?.Id != country.Id)
        {
            return Failure("update", $"Wine '{targetWine.Name}' is recorded for country '{wineCountry?.Name ?? "unknown"}'.",
                new[] { $"Wine '{targetWine.Name}' is recorded for country '{wineCountry?.Name ?? "unknown"}'." },
                new
                {
                    type = "wine_country_mismatch",
                    requested = new { name = country.Name, id = country.Id },
                    actual = wineCountry is null ? null : new { name = wineCountry.Name, id = wineCountry.Id }
                });
        }

        if (!string.IsNullOrWhiteSpace(normalizedAppellation)
            && !string.Equals(targetWine.Appellation?.Name, normalizedAppellation, StringComparison.OrdinalIgnoreCase))
        {
            var recordedAppellation = string.IsNullOrWhiteSpace(targetWine.Appellation?.Name) ? "unknown" : targetWine.Appellation!.Name;
            return Failure("update",
                $"Wine '{targetWine.Name}' is recorded for appellation '{recordedAppellation}'.",
                new[] { $"Wine '{targetWine.Name}' is recorded for appellation '{recordedAppellation}'." },
                new
                {
                    type = "wine_appellation_mismatch",
                    requested = normalizedAppellation,
                    actual = string.IsNullOrWhiteSpace(targetWine.Appellation?.Name) ? null : targetWine.Appellation.Name
                });
        }

        if (region is not null && targetWine.Appellation?.Region?.Id != region.Id)
        {
            var recordedRegion = targetWine.Appellation?.Region?.Name ?? "unknown";
            return Failure("update", $"Wine '{targetWine.Name}' is recorded for region '{recordedRegion}'.",
                new[] { $"Wine '{targetWine.Name}' is recorded for region '{recordedRegion}'." },
                new
                {
                    type = "wine_region_mismatch",
                    requested = new { name = region.Name, id = region.Id },
                    actual = targetWine.Appellation?.Region is null ? null : new { name = targetWine.Appellation.Region.Name, id = targetWine.Appellation.Region.Id }
                });
        }

        var desiredVintage = vintage ?? currentWineVintage.Vintage;

        WineVintage targetWineVintage;
        if (targetWine.Id == currentWineVintage.WineId && desiredVintage == currentWineVintage.Vintage)
        {
            targetWineVintage = currentWineVintage;
        }
        else
        {
            targetWineVintage = await WineVintageRepository.GetOrCreateAsync(targetWine.Id, desiredVintage, ct);
        }

        bottle.WineVintageId = targetWineVintage.Id;

        if (price.HasValue)
        {
            bottle.Price = price;
        }

        if (isDrunk.HasValue)
        {
            bottle.IsDrunk = isDrunk.Value;
            if (!isDrunk.Value)
            {
                bottle.DrunkAt = null;
            }
        }

        if (drunkAt.HasValue)
        {
            bottle.DrunkAt = drunkAt.Value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(drunkAt.Value, DateTimeKind.Utc)
                : drunkAt.Value.ToUniversalTime();
            bottle.IsDrunk = true;
        }
        else if (isDrunk == true && !bottle.DrunkAt.HasValue)
        {
            bottle.DrunkAt = DateTime.UtcNow;
        }

        await BottleRepository.UpdateAsync(bottle, ct);

        if (userId.HasValue && (score.HasValue || tastingNote is not null))
        {
            var tastingNoteEntity = new TastingNote
            {
                Id = Guid.NewGuid(),
                BottleId = bottle.Id,
                UserId = userId.Value,
                Score = score,
                Note = tastingNote?.Trim() ?? string.Empty
            };

            await TastingNoteRepository.AddAsync(tastingNoteEntity, ct);
        }

        var updated = await BottleRepository.GetByIdAsync(bottle.Id, ct) ?? new Bottle
        {
            Id = bottle.Id,
            WineVintageId = bottle.WineVintageId,
            Price = bottle.Price,
            IsDrunk = bottle.IsDrunk,
            DrunkAt = bottle.DrunkAt,
            WineVintage = targetWineVintage,
            TastingNotes = []
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
            ["userId"] = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "uuid",
                ["description"] = "User identifier required when providing tasting notes or scores."
            },
            ["user_id"] = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "uuid",
                ["description"] = "Snake_case alias for userId."
            },
            ["score"] = new JsonObject
            {
                ["type"] = "number",
                ["description"] = "Updated score (requires userId)."
            },
            ["tastingNote"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Updated tasting notes (requires userId)."
            },
            ["tasting_note"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Snake_case alias for tastingNote."
            },
            ["isDrunk"] = new JsonObject
            {
                ["type"] = "boolean",
                ["description"] = "Set to true when the bottle has been consumed."
            },
            ["is_drunk"] = new JsonObject
            {
                ["type"] = "boolean",
                ["description"] = "Snake_case alias for isDrunk."
            },
            ["drunkAt"] = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "date-time",
                ["description"] = "Timestamp of when the bottle was consumed."
            },
            ["drunk_at"] = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "date-time",
                ["description"] = "Snake_case alias for drunkAt."
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
