using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;

namespace AllMCPSolution.Services;

public sealed class InventoryIntakeService
{
    private readonly IBottleRepository _bottleRepository;
    private readonly IWineRepository _wineRepository;
    private readonly ICountryRepository _countryRepository;
    private readonly IRegionRepository _regionRepository;
    private readonly IAppellationRepository _appellationRepository;
    private readonly ISubAppellationRepository _subAppellationRepository;
    private readonly IWineVintageRepository _wineVintageRepository;
    private readonly IUserRepository _userRepository;
    private readonly ITastingNoteRepository _tastingNoteRepository;
    private readonly string[] _colorOptions = Enum.GetNames(typeof(WineColor));

    public InventoryIntakeService(
        IBottleRepository bottleRepository,
        IWineRepository wineRepository,
        ICountryRepository countryRepository,
        IRegionRepository regionRepository,
        IAppellationRepository appellationRepository,
        ISubAppellationRepository subAppellationRepository,
        IWineVintageRepository wineVintageRepository,
        IUserRepository userRepository,
        ITastingNoteRepository tastingNoteRepository)
    {
        _bottleRepository = bottleRepository;
        _wineRepository = wineRepository;
        _countryRepository = countryRepository;
        _regionRepository = regionRepository;
        _appellationRepository = appellationRepository;
        _subAppellationRepository = subAppellationRepository;
        _wineVintageRepository = wineVintageRepository;
        _userRepository = userRepository;
        _tastingNoteRepository = tastingNoteRepository;
    }

    public async Task<InventoryBottleResult> CreateBottleAsync(IDictionary<string, object?>? parameters, CancellationToken ct)
    {
        try
        {
            var normalized = NormalizeParameters(parameters);
            var errors = new List<string>();

            var name = ParameterHelpers.GetStringParameter(normalized, "name", "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add("'name' is required.");
            }

            var vintage = ParameterHelpers.GetIntParameter(normalized, "vintage", "vintage");
            if (vintage is null)
            {
                errors.Add("'vintage' is required and must be a valid year.");
            }

            var price = ParameterHelpers.GetDecimalParameter(normalized, "price", "price");
            var score = ParameterHelpers.GetDecimalParameter(normalized, "score", "score");
            var tastingNote = ParameterHelpers.GetStringParameter(normalized, "tastingNote", "tasting_note")
                ?? ParameterHelpers.GetStringParameter(normalized, "tastingNotes", "tasting_notes");
            var userId = ParameterHelpers.GetGuidParameter(normalized, "userId", "user_id");
            var userName = ParameterHelpers.GetStringParameter(normalized, "userName", "user_name");
            var isDrunk = ParameterHelpers.GetBoolParameter(normalized, "isDrunk", "is_drunk");
            var drunkAt = ParameterHelpers.GetDateTimeParameter(normalized, "drunkAt", "drunk_at");

            if (drunkAt.HasValue && isDrunk != true)
            {
                errors.Add("'drunkAt' can only be provided when 'isDrunk' is true.");
            }

            if ((score.HasValue || !string.IsNullOrWhiteSpace(tastingNote))
                && userId is null
                && string.IsNullOrWhiteSpace(userName))
            {
                errors.Add("'userId' or 'userName' is required when providing tasting notes or scores.");
            }

            var colorInput = ParameterHelpers.GetStringParameter(normalized, "color", "color");
            WineColor? color = null;
            if (!string.IsNullOrWhiteSpace(colorInput))
            {
                if (Enum.TryParse(colorInput, true, out WineColor parsedColor))
                {
                    color = parsedColor;
                }
                else
                {
                    return InventoryBottleResult.CreateFailure(
                        $"Color '{colorInput}' is not recognised.",
                        new[] { $"Color '{colorInput}' is not recognised." },
                        new
                        {
                            type = "color",
                            query = colorInput,
                            suggestions = _colorOptions
                        });
                }
            }

            var countryName = ParameterHelpers.GetStringParameter(normalized, "country", "country");
            var regionName = ParameterHelpers.GetStringParameter(normalized, "region", "region");
            var appellationName = ParameterHelpers.GetStringParameter(normalized, "appellation", "appellation");
            var subAppellationName = ParameterHelpers.GetStringParameter(normalized, "subAppellation", "sub_appellation");

            var trimmedCountryName = string.IsNullOrWhiteSpace(countryName) ? null : countryName.Trim();
            var trimmedRegionName = string.IsNullOrWhiteSpace(regionName) ? null : regionName.Trim();
            var normalizedAppellation = string.IsNullOrWhiteSpace(appellationName)
                ? null
                : appellationName.Trim();
            var normalizedSubAppellation = string.IsNullOrWhiteSpace(subAppellationName)
                ? null
                : subAppellationName.Trim();

            if (errors.Count > 0)
            {
                return InventoryBottleResult.CreateFailure("Validation failed.", errors);
            }

            Country? country = null;
            if (!string.IsNullOrWhiteSpace(trimmedCountryName))
            {
                country = await _countryRepository.GetOrCreateAsync(trimmedCountryName!, ct);
            }

            Region? region = null;
            if (!string.IsNullOrWhiteSpace(trimmedRegionName))
            {
                var existingRegion = await _regionRepository.FindByNameAsync(trimmedRegionName!, ct);
                if (existingRegion is not null)
                {
                    if (country is not null && existingRegion.CountryId != country.Id)
                    {
                        return InventoryBottleResult.CreateFailure(
                            $"Region '{existingRegion.Name}' belongs to country '{existingRegion.Country?.Name ?? "unknown"}'.",
                            new[] { $"Region '{existingRegion.Name}' belongs to country '{existingRegion.Country?.Name ?? "unknown"}'." },
                            new
                            {
                                type = "region_country_mismatch",
                                requestedCountry = new { name = country.Name, id = country.Id },
                                regionCountry = existingRegion.Country is null
                                    ? null
                                    : new { name = existingRegion.Country.Name, id = existingRegion.Country.Id }
                            });
                    }

                    region = existingRegion;
                }
                else if (country is null)
                {
                    return InventoryBottleResult.CreateFailure(
                        $"Region '{trimmedRegionName}' was not found. Provide a country so it can be created automatically.",
                        new[] { "Country is required to create a new region." },
                        new
                        {
                            type = "region_creation_missing_country",
                            query = trimmedRegionName
                        });
                }
                else
                {
                    region = await _regionRepository.GetOrCreateAsync(trimmedRegionName!, country, ct);
                }
            }

            if (region is not null)
            {
                country ??= region.Country;
            }

            var wine = await _wineRepository.FindByNameAsync(name!, normalizedSubAppellation, normalizedAppellation, ct);
            if (wine is null)
            {
                if (!color.HasValue)
                {
                    return InventoryBottleResult.CreateFailure(
                        $"Wine '{name}' does not exist. Provide a color so it can be created automatically.",
                        new[] { "Color is required to create a new wine." },
                        new
                        {
                            type = "wine_creation_missing_color",
                            query = name,
                            suggestions = _colorOptions
                        });
                }

                if (region is null)
                {
                    return InventoryBottleResult.CreateFailure(
                        $"Wine '{name}' does not exist. Provide a region so it can be created automatically.",
                        new[] { "Region is required to create a new wine." },
                        new
                        {
                            type = "wine_creation_missing_region",
                            query = name
                        });
                }

                country ??= region.Country;

                if (string.IsNullOrWhiteSpace(normalizedAppellation))
                {
                    return InventoryBottleResult.CreateFailure(
                        $"Wine '{name}' does not exist. Provide an appellation so it can be created automatically.",
                        new[] { "Appellation is required to create a new wine." },
                        new
                        {
                            type = "wine_creation_missing_appellation",
                            query = name
                        });
                }

                var appellation = await _appellationRepository.GetOrCreateAsync(normalizedAppellation!, region.Id, ct);
                var subAppellation = string.IsNullOrWhiteSpace(normalizedSubAppellation)
                    ? await _subAppellationRepository.GetOrCreateBlankAsync(appellation.Id, ct)
                    : await _subAppellationRepository.GetOrCreateAsync(normalizedSubAppellation!, appellation.Id, ct);

                var newWine = new Wine
                {
                    Id = Guid.NewGuid(),
                    Name = name!.Trim(),
                    GrapeVariety = string.Empty,
                    Color = color.Value,
                    SubAppellationId = subAppellation.Id,
                    SubAppellation = subAppellation
                };

                await _wineRepository.AddAsync(newWine, ct);
                wine = await _wineRepository.GetByIdAsync(newWine.Id, ct) ?? newWine;
            }

            var wineSubAppellation = wine.SubAppellation;
            var wineAppellation = wineSubAppellation?.Appellation;
            var wineRegion = wineAppellation?.Region;
            var wineCountry = wineRegion?.Country;

            if (!string.IsNullOrWhiteSpace(normalizedSubAppellation)
                && !string.Equals(wineSubAppellation?.Name, normalizedSubAppellation, StringComparison.OrdinalIgnoreCase))
            {
                var recordedSubAppellation = string.IsNullOrWhiteSpace(wineSubAppellation?.Name) ? "unknown" : wineSubAppellation!.Name;
                return InventoryBottleResult.CreateFailure(
                    $"Wine '{wine.Name}' is recorded for sub-appellation '{recordedSubAppellation}'.",
                    new[] { $"Wine '{wine.Name}' is recorded for sub-appellation '{recordedSubAppellation}'." },
                    new
                    {
                        type = "wine_sub_appellation_mismatch",
                        requested = normalizedSubAppellation,
                        actual = string.IsNullOrWhiteSpace(wineSubAppellation?.Name) ? null : wineSubAppellation.Name
                    });
            }

            if (!string.IsNullOrWhiteSpace(normalizedAppellation)
                && !string.Equals(wineAppellation?.Name, normalizedAppellation, StringComparison.OrdinalIgnoreCase))
            {
                var recordedAppellation = string.IsNullOrWhiteSpace(wineAppellation?.Name) ? "unknown" : wineAppellation!.Name;
                return InventoryBottleResult.CreateFailure(
                    $"Wine '{wine.Name}' is recorded for appellation '{recordedAppellation}'.",
                    new[] { $"Wine '{wine.Name}' is recorded for appellation '{recordedAppellation}'." },
                    new
                    {
                        type = "wine_appellation_mismatch",
                        requested = normalizedAppellation,
                        actual = string.IsNullOrWhiteSpace(wineAppellation?.Name) ? null : wineAppellation.Name
                    });
            }

            if (color.HasValue && wine.Color != color)
            {
                return InventoryBottleResult.CreateFailure(
                    $"Wine '{wine.Name}' exists with color '{wine.Color}'.",
                    new[] { $"Wine '{wine.Name}' exists with color '{wine.Color}'." },
                    new
                    {
                        type = "wine_color_mismatch",
                        requested = color.Value.ToString(),
                        actual = wine.Color.ToString()
                    });
            }

            if (country is not null && wineCountry?.Id != country.Id)
            {
                return InventoryBottleResult.CreateFailure(
                    $"Wine '{wine.Name}' is recorded for country '{wineCountry?.Name ?? "unknown"}'.",
                    new[] { $"Wine '{wine.Name}' is recorded for country '{wineCountry?.Name ?? "unknown"}'." },
                    new
                    {
                        type = "wine_country_mismatch",
                        requested = new { name = country.Name, id = country.Id },
                        actual = wineCountry is null ? null : new { name = wineCountry.Name, id = wineCountry.Id }
                    });
            }

            if (region is not null && wineRegion?.Id != region.Id)
            {
                var recordedRegion = wineRegion?.Name ?? "unknown";
                return InventoryBottleResult.CreateFailure(
                    $"Wine '{wine.Name}' is recorded for region '{recordedRegion}'.",
                    new[] { $"Wine '{wine.Name}' is recorded for region '{recordedRegion}'." },
                    new
                    {
                        type = "wine_region_mismatch",
                        requested = new { name = region.Name, id = region.Id },
                        actual = wineRegion is null ? null : new { name = wineRegion.Name, id = wineRegion.Id }
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

            var wineVintage = await _wineVintageRepository.GetOrCreateAsync(wine.Id, vintage!.Value, ct);

        ApplicationUser? resolvedUser = null;
            if (userId.HasValue || !string.IsNullOrWhiteSpace(userName))
            {
                var userResolution = await ResolveUserAsync(userId, userName, ct);
                if (!userResolution.Success)
                {
                    return InventoryBottleResult.CreateFailure(
                        userResolution.Message,
                        userResolution.Errors,
                        userResolution.Suggestions);
                }

                resolvedUser = userResolution.User;
            }

            var bottle = new Bottle
            {
                Id = Guid.NewGuid(),
                WineVintageId = wineVintage.Id,
                Price = price,
                IsDrunk = hasBeenDrunk,
                DrunkAt = resolvedDrunkAt,
                UserId = resolvedUser?.Id
            };

            await _bottleRepository.AddAsync(bottle, ct);

            TastingNote? persistedNote = null;
            if ((score.HasValue || !string.IsNullOrWhiteSpace(tastingNote)) && resolvedUser is not null)
            {
                var tastingNoteEntity = new TastingNote
                {
                    Id = Guid.NewGuid(),
                    BottleId = bottle.Id,
                    UserId = resolvedUser.Id,
                    Score = score,
                    Note = tastingNote?.Trim() ?? string.Empty,
                    Bottle = null,
                    User = null
                };

                await _tastingNoteRepository.AddAsync(tastingNoteEntity, ct);
                persistedNote = await _tastingNoteRepository.GetByIdAsync(tastingNoteEntity.Id, ct) ?? tastingNoteEntity;
                if (persistedNote.User is null)
                {
                    persistedNote.User = resolvedUser;
                }
            }

            if ((score.HasValue || !string.IsNullOrWhiteSpace(tastingNote)) && resolvedUser is null)
            {
                return InventoryBottleResult.CreateFailure(
                    "User information is required to record tasting notes.",
                    new[] { "'userId' or 'userName' is required when providing tasting notes or scores." });
            }

            var created = await _bottleRepository.GetByIdAsync(bottle.Id, ct) ?? new Bottle
            {
                Id = bottle.Id,
                WineVintageId = bottle.WineVintageId,
                Price = bottle.Price,
                IsDrunk = bottle.IsDrunk,
                DrunkAt = bottle.DrunkAt,
                UserId = bottle.UserId,
                User = resolvedUser,
                WineVintage = wineVintage,
                TastingNotes = []
            };

            if (persistedNote is not null)
            {
                persistedNote.Bottle ??= created;
            }

            return InventoryBottleResult.CreateSuccess("Bottle created successfully.", created, persistedNote);
        }
        catch (Exception ex)
        {
            return InventoryBottleResult.CreateFailure(
                "An unexpected error occurred while importing the bottle.",
                new[] { ex.Message },
                new { type = "exception" },
                ex);
        }
    }

    public async Task<InventoryTastingNoteResult> CreateTastingNoteAsync(IDictionary<string, object?>? parameters, CancellationToken ct)
    {
        try
        {
            var normalized = NormalizeParameters(parameters);
            var note = ParameterHelpers.GetStringParameter(normalized, "note", "note")?.Trim();
            var score = ParameterHelpers.GetDecimalParameter(normalized, "score", "score");
            var userId = ParameterHelpers.GetGuidParameter(normalized, "userId", "user_id");
            var userName = ParameterHelpers.GetStringParameter(normalized, "userName", "user_name")?.Trim();
            var bottleId = ParameterHelpers.GetGuidParameter(normalized, "bottleId", "bottle_id");

            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(note))
            {
                errors.Add("'note' is required.");
            }

            if (bottleId is null)
            {
                errors.Add("'bottleId' is required.");
            }

            if (userId is null && string.IsNullOrWhiteSpace(userName))
            {
                errors.Add("Either 'userId' or 'userName' must be provided.");
            }

            if (score is not null && (score < 0 || score > 100))
            {
                errors.Add("'score' must be between 0 and 10.");
            }

            if (errors.Count > 0)
            {
                return InventoryTastingNoteResult.CreateFailure("Validation failed.", errors);
            }

            var bottle = await _bottleRepository.GetByIdAsync(bottleId!.Value, ct);
            if (bottle is null)
            {
                return InventoryTastingNoteResult.CreateFailure(
                    "Bottle not found.",
                    new[] { $"Bottle with id '{bottleId}' was not found." },
                    new
                    {
                        type = "bottle_not_found",
                        bottleId
                    });
            }

            var userResolution = await ResolveUserAsync(userId, userName, ct);
            if (!userResolution.Success)
            {
                return InventoryTastingNoteResult.CreateFailure(
                    userResolution.Message,
                    userResolution.Errors,
                    userResolution.Suggestions);
            }

            var entity = new TastingNote
            {
                Id = Guid.NewGuid(),
                Note = note!,
                Score = score,
                BottleId = bottle.Id,
                UserId = userResolution.User!.Id,
                Bottle = null,
                User = null
            };

            await _tastingNoteRepository.AddAsync(entity, ct);
            var persisted = await _tastingNoteRepository.GetByIdAsync(entity.Id, ct) ?? entity;
            if (persisted.Bottle is null)
            {
                persisted.Bottle = bottle;
            }

            if (persisted.User is null)
            {
                persisted.User = userResolution.User;
            }

            return InventoryTastingNoteResult.CreateSuccess("Tasting note created.", persisted);
        }
        catch (Exception ex)
        {
            return InventoryTastingNoteResult.CreateFailure(
                "An unexpected error occurred while creating the tasting note.",
                new[] { ex.Message },
                new { type = "exception" },
                ex);
        }
    }

    private static Dictionary<string, object> NormalizeParameters(IDictionary<string, object?>? parameters)
    {
        if (parameters is Dictionary<string, object> dictionary && dictionary.Comparer.Equals(StringComparer.OrdinalIgnoreCase))
        {
            return dictionary;
        }

        var normalized = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (parameters is null)
        {
            return normalized;
        }

        foreach (var kvp in parameters)
        {
            normalized[kvp.Key] = kvp.Value!;
        }

        return normalized;
    }

    private async Task<UserResolutionResult> ResolveUserAsync(Guid? userId, string? userName, CancellationToken ct)
    {
        ApplicationUser? user = null;

        if (userId is not null)
        {
            user = await _userRepository.GetByIdAsync(userId.Value, ct);
        }

        if (user is null && !string.IsNullOrWhiteSpace(userName))
        {
            user = await _userRepository.FindByNameAsync(userName!, ct);
        }

        if (user is null)
        {
            var suggestions = string.IsNullOrWhiteSpace(userName)
                ? Array.Empty<object>()
                : (await _userRepository.SearchByApproximateNameAsync(userName!, 5, ct))
                    .Select(u => new
                    {
                        id = u.Id,
                        name = u.Name,
                        tasteProfileSummary = u.TasteProfileSummary,
                        tasteProfile = u.TasteProfile
                    })
                    .ToArray();

            var query = userName ?? userId?.ToString();

            return new UserResolutionResult(
                false,
                null,
                "User not found.",
                new[]
                {
                    userId is not null
                        ? $"User with id '{userId}' was not found."
                        : $"User '{userName}' was not found."
                },
                new
                {
                    type = "user_search",
                    query,
                    suggestions
                });
        }

        return new UserResolutionResult(true, user!, string.Empty, null, null);
    }

    public sealed record InventoryBottleResult
    {
        private InventoryBottleResult()
        {
        }

        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public Bottle? Bottle { get; init; }
        public TastingNote? TastingNote { get; init; }
        public IReadOnlyList<string>? Errors { get; init; }
        public object? Suggestions { get; init; }
        public Exception? Exception { get; init; }

        public static InventoryBottleResult CreateSuccess(string message, Bottle bottle, TastingNote? tastingNote)
            => new()
            {
                Success = true,
                Message = message,
                Bottle = bottle,
                TastingNote = tastingNote
            };

        public static InventoryBottleResult CreateFailure(
            string message,
            IReadOnlyList<string>? errors = null,
            object? suggestions = null,
            Exception? exception = null)
            => new()
            {
                Success = false,
                Message = message,
                Errors = errors,
                Suggestions = suggestions,
                Exception = exception
            };
    }

    public sealed record InventoryTastingNoteResult
    {
        private InventoryTastingNoteResult()
        {
        }

        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public TastingNote? TastingNote { get; init; }
        public IReadOnlyList<string>? Errors { get; init; }
        public object? Suggestions { get; init; }
        public Exception? Exception { get; init; }

        public static InventoryTastingNoteResult CreateSuccess(string message, TastingNote tastingNote)
            => new()
            {
                Success = true,
                Message = message,
                TastingNote = tastingNote
            };

        public static InventoryTastingNoteResult CreateFailure(
            string message,
            IReadOnlyList<string>? errors = null,
            object? suggestions = null,
            Exception? exception = null)
            => new()
            {
                Success = false,
                Message = message,
                Errors = errors,
                Suggestions = suggestions,
                Exception = exception
            };
    }

    private sealed record UserResolutionResult(
        bool Success,
        ApplicationUser? User,
        string Message,
        IReadOnlyList<string>? Errors,
        object? Suggestions);
}
