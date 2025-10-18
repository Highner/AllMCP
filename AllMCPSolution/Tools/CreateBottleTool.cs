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

[McpTool("create_bottle", "Creates a bottle for an existing wine after validating the wine metadata.")]
public sealed class CreateBottleTool : BottleToolBase
{
    private readonly string[] _colorOptions = Enum.GetNames(typeof(WineColor));

    public CreateBottleTool(
        IBottleRepository bottleRepository,
        IWineRepository wineRepository,
        ICountryRepository countryRepository,
        IRegionRepository regionRepository)
        : base(bottleRepository, wineRepository, countryRepository, regionRepository)
    {
    }

    public override string Name => "create_bottle";
    public override string Description => "Creates a bottle for an existing wine after validating the wine metadata.";
    public override string Title => "Create Bottle";
    protected override string InvokingMessage => "Creating bottle…";
    protected override string InvokedMessage => "Bottle created.";

    protected override async Task<BottleOperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        try
        {
            var errors = new List<string>();

            var name = ParameterHelpers.GetStringParameter(parameters, "name", "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add("'name' is required.");
            }

            var vintage = ParameterHelpers.GetIntParameter(parameters, "vintage", "vintage");
            if (vintage is null)
            {
                errors.Add("'vintage' is required and must be a valid year.");
            }

            var price = ParameterHelpers.GetDecimalParameter(parameters, "price", "price");
            var score = ParameterHelpers.GetDecimalParameter(parameters, "score", "score");
            var tastingNote = ParameterHelpers.GetStringParameter(parameters, "tastingNote", "tasting_note")
                ?? ParameterHelpers.GetStringParameter(parameters, "tastingNotes", "tasting_notes");
            var isDrunk = ParameterHelpers.GetBoolParameter(parameters, "isDrunk", "is_drunk");
            var drunkAt = ParameterHelpers.GetDateTimeParameter(parameters, "drunkAt", "drunk_at");

            if (drunkAt.HasValue && isDrunk != true)
            {
                errors.Add("'drunkAt' can only be provided when 'isDrunk' is true.");
            }

            var colorInput = ParameterHelpers.GetStringParameter(parameters, "color", "color");
            WineColor? color = null;
            if (!string.IsNullOrWhiteSpace(colorInput))
            {
                if (Enum.TryParse<WineColor>(colorInput, true, out var parsedColor))
                {
                    color = parsedColor;
                }
                else
                {
                    return Failure("create", $"Color '{colorInput}' is not recognised.",
                        new[] { $"Color '{colorInput}' is not recognised." },
                        new
                        {
                            type = "color",
                            query = colorInput,
                            suggestions = _colorOptions
                        });
                }
            }

            var countryName = ParameterHelpers.GetStringParameter(parameters, "country", "country");
            var regionName = ParameterHelpers.GetStringParameter(parameters, "region", "region");

            if (errors.Count > 0)
            {
                return Failure("create", "Validation failed.", errors);
            }

            Country? country = null;
            if (!string.IsNullOrWhiteSpace(countryName))
            {
                country = await CountryRepository.FindByNameAsync(countryName!, ct);
                if (country is null)
                {
                    var suggestions = await CountryRepository.SearchByApproximateNameAsync(countryName!, 5, ct);
                    return Failure("create", $"Country '{countryName}' was not found.",
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
                    return Failure("create", $"Region '{regionName}' was not found.",
                        new[] { $"Region '{regionName}' was not found." },
                        new
                        {
                            type = "region",
                            query = regionName,
                            suggestions = suggestions.Select(BottleResponseMapper.MapRegion).ToList()
                        });
                }
            }

            var wine = await WineRepository.FindByNameAsync(name!, ct);
            if (wine is null)
            {
                var suggestions = await WineRepository.FindClosestMatchesAsync(name!, 5, ct);
                if (suggestions.Count > 0)
                {
                    return Failure("create", $"Wine '{name}' does not have an exact match. Please confirm the correct wine before creating the bottle.",
                        new[] { $"Wine '{name}' does not have an exact match." },
                        new
                        {
                            type = "wine_confirmation_required",
                            query = name,
                            suggestions = suggestions.Select(BottleResponseMapper.MapWineSummary).ToList()
                        });
                }

                if (!color.HasValue)
                {
                    return Failure("create", $"Wine '{name}' does not exist. Provide a color so it can be created automatically.",
                        new[] { "Color is required to create a new wine." },
                        new
                        {
                            type = "wine_creation_missing_color",
                            query = name,
                            suggestions = _colorOptions
                        });
                }

                if (country is null)
                {
                    return Failure("create", $"Wine '{name}' does not exist. Provide a country so it can be created automatically.",
                        new[] { "Country is required to create a new wine." },
                        new
                        {
                            type = "wine_creation_missing_country",
                            query = name
                        });
                }

                if (region is null)
                {
                    return Failure("create", $"Wine '{name}' does not exist. Provide a region so it can be created automatically.",
                        new[] { "Region is required to create a new wine." },
                        new
                        {
                            type = "wine_creation_missing_region",
                            query = name
                        });
                }

                var newWine = new Wine
                {
                    Id = Guid.NewGuid(),
                    Name = name!.Trim(),
                    GrapeVariety = string.Empty,
                    Color = color.Value,
                    CountryId = country.Id,
                    RegionId = region.Id,
                    Country = country,
                    Region = region
                };

                await WineRepository.AddAsync(newWine, ct);
                wine = await WineRepository.GetByIdAsync(newWine.Id, ct) ?? newWine;
            }

            if (color.HasValue && wine.Color != color)
            {
                return Failure("create", $"Wine '{wine.Name}' exists with color '{wine.Color}'.",
                    new[] { $"Wine '{wine.Name}' exists with color '{wine.Color}'." },
                    new
                    {
                        type = "wine_color_mismatch",
                        requested = color.Value.ToString(),
                        actual = wine.Color.ToString()
                    });
            }

            if (country is not null && wine.CountryId != country.Id)
            {
                return Failure("create", $"Wine '{wine.Name}' is recorded for country '{wine.Country?.Name ?? "unknown"}'.",
                    new[] { $"Wine '{wine.Name}' is recorded for country '{wine.Country?.Name ?? "unknown"}'." },
                    new
                    {
                        type = "wine_country_mismatch",
                        requested = new { name = country.Name, id = country.Id },
                        actual = wine.Country is null ? null : new { name = wine.Country.Name, id = wine.Country.Id }
                    });
            }

            if (region is not null && wine.RegionId != region.Id)
            {
                return Failure("create", $"Wine '{wine.Name}' is recorded for region '{wine.Region?.Name ?? "unknown"}'.",
                    new[] { $"Wine '{wine.Name}' is recorded for region '{wine.Region?.Name ?? "unknown"}'." },
                    new
                    {
                        type = "wine_region_mismatch",
                        requested = new { name = region.Name, id = region.Id },
                        actual = wine.Region is null ? null : new { name = wine.Region.Name, id = wine.Region.Id }
                    });
            }

            var hasBeenDrunk = isDrunk ?? false;
            DateTime? resolvedDrunkAt = null;
            if (hasBeenDrunk)
            {
                if (drunkAt.HasValue)
                {
                    resolvedDrunkAt = drunkAt.Value.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(drunkAt.Value, DateTimeKind.Utc)
                        : drunkAt.Value.ToUniversalTime();
                }
                else
                {
                    resolvedDrunkAt = DateTime.UtcNow;
                }
            }

            var bottle = new Bottle
            {
                Id = Guid.NewGuid(),
                WineId = wine.Id,
                Vintage = vintage!.Value,
                Price = price,
                Score = score,
                TastingNote = tastingNote?.Trim() ?? string.Empty,
                IsDrunk = hasBeenDrunk,
                DrunkAt = resolvedDrunkAt
            };

            await BottleRepository.AddAsync(bottle, ct);

            var created = await BottleRepository.GetByIdAsync(bottle.Id, ct) ?? new Bottle
            {
                Id = bottle.Id,
                WineId = bottle.WineId,
                Vintage = bottle.Vintage,
                Price = bottle.Price,
                Score = bottle.Score,
                TastingNote = bottle.TastingNote,
                IsDrunk = bottle.IsDrunk,
                DrunkAt = bottle.DrunkAt,
                Wine = wine
            };

            return Success("create", "Bottle created successfully.", new
            {
                bottle = BottleResponseMapper.MapBottle(created)
            });
        }
        catch (Exception ex)
        {
            return Failure(
                "create",
                ex.Message,
                new[] { ex.Message },
                new
                {
                    type = "exception",
                    stackTrace = ex.StackTrace
                },
                ex);
        }
    }

    protected override JsonObject BuildInputSchema()
    {
        var properties = new JsonObject
        {
            ["name"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Name of the wine the bottle belongs to."
            },
            ["vintage"] = new JsonObject
            {
                ["type"] = "integer",
                ["description"] = "Vintage year for the bottle."
            },
            ["country"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Country of the wine. Required when the wine must be created."
            },
            ["region"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Region of the wine. Required when the wine must be created."
            },
            ["color"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Wine color. Required when the wine must be created. Valid options: " + string.Join(", ", _colorOptions)
            },
            ["price"] = new JsonObject
            {
                ["type"] = "number",
                ["description"] = "Optional bottle price."
            },
            ["score"] = new JsonObject
            {
                ["type"] = "number",
                ["description"] = "Optional rating or score."
            },
            ["tastingNote"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Optional tasting notes."
            },
            ["tasting_note"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Snake_case alias for tastingNote."
            },
            ["isDrunk"] = new JsonObject
            {
                ["type"] = "boolean",
                ["description"] = "Indicates whether the bottle has been consumed."
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
                ["description"] = "Timestamp of when the bottle was consumed (requires isDrunk=true)."
            },
            ["drunk_at"] = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "date-time",
                ["description"] = "Snake_case alias for drunkAt."
            }
        };

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray("name", "vintage")
        };
    }
}
