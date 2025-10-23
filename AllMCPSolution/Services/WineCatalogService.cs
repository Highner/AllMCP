using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using AllMCPSolution.Utilities;

namespace AllMCPSolution.Services;

public interface IWineCatalogService
{
    Task<WineCatalogResult> EnsureWineAsync(WineCatalogRequest request, CancellationToken cancellationToken);
}

public sealed class WineCatalogService : IWineCatalogService
{
    private const double LocationMatchThreshold = 0.3d;
    private const double WineNameMatchThreshold = 0.2d;

    private readonly IWineRepository _wineRepository;
    private readonly ICountryRepository _countryRepository;
    private readonly IRegionRepository _regionRepository;
    private readonly IAppellationRepository _appellationRepository;
    private readonly ISubAppellationRepository _subAppellationRepository;

    public WineCatalogService(
        IWineRepository wineRepository,
        ICountryRepository countryRepository,
        IRegionRepository regionRepository,
        IAppellationRepository appellationRepository,
        ISubAppellationRepository subAppellationRepository)
    {
        _wineRepository = wineRepository;
        _countryRepository = countryRepository;
        _regionRepository = regionRepository;
        _appellationRepository = appellationRepository;
        _subAppellationRepository = subAppellationRepository;
    }

    public async Task<WineCatalogResult> EnsureWineAsync(WineCatalogRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var name = Normalize(request.Name);
        if (string.IsNullOrEmpty(name))
        {
            AddError(errors, nameof(request.Name), "Wine name is required.");
        }

        var regionName = Normalize(request.Region);
        if (string.IsNullOrEmpty(regionName))
        {
            AddError(errors, nameof(request.Region), "Region is required to add this wine.");
        }

        var appellationName = Normalize(request.Appellation);
        if (string.IsNullOrEmpty(appellationName))
        {
            AddError(errors, nameof(request.Appellation), "Appellation is required to add this wine.");
        }

        WineColor? color = null;
        var colorCandidate = Normalize(request.Color);
        if (!string.IsNullOrEmpty(colorCandidate))
        {
            if (WineColorUtilities.TryParse(colorCandidate, out var parsedColor))
            {
                color = parsedColor;
            }
            else
            {
                AddError(errors, nameof(request.Color), "Color must be Red, White, or Rose.");
            }
        }
        else
        {
            AddError(errors, nameof(request.Color), "Color must be Red, White, or Rose.");
        }

        if (errors.Count > 0)
        {
            return WineCatalogResult.Failure(ToReadOnly(errors));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var countryName = Normalize(request.Country);
        var subAppellationName = Normalize(request.SubAppellation);
        var grapeVariety = Normalize(request.GrapeVariety) ?? string.Empty;

        Country? country = null;
        if (!string.IsNullOrEmpty(countryName))
        {
            country = await ResolveCountryAsync(countryName, cancellationToken)
                ?? await _countryRepository.GetOrCreateAsync(countryName, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        Region? region = null;
        if (!string.IsNullOrEmpty(regionName))
        {
            if (country is not null)
            {
                region = await ResolveRegionAsync(regionName, country.Id, cancellationToken)
                    ?? await _regionRepository.GetOrCreateAsync(regionName, country, cancellationToken);
            }
            else
            {
                region = await ResolveRegionAsync(regionName, null, cancellationToken);
                if (region is null)
                {
                    AddError(errors, nameof(request.Country), "Country is required when creating a new region.");
                    return WineCatalogResult.Failure(ToReadOnly(errors));
                }

                country = region.Country;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        Appellation? appellation = null;
        if (region is not null && !string.IsNullOrEmpty(appellationName))
        {
            appellation = await ResolveAppellationAsync(appellationName, region.Id, cancellationToken)
                ?? await _appellationRepository.GetOrCreateAsync(appellationName, region.Id, cancellationToken);
        }

        if (appellation is null)
        {
            AddError(errors, nameof(request.Appellation), "Appellation is required to add this wine.");
            return WineCatalogResult.Failure(ToReadOnly(errors));
        }

        cancellationToken.ThrowIfCancellationRequested();

        SubAppellation? subAppellation;
        if (string.IsNullOrEmpty(subAppellationName))
        {
            subAppellation = await _subAppellationRepository.GetOrCreateBlankAsync(appellation.Id, cancellationToken);
        }
        else
        {
            subAppellation = await ResolveSubAppellationAsync(subAppellationName, appellation.Id, cancellationToken)
                ?? await _subAppellationRepository.GetOrCreateAsync(subAppellationName, appellation.Id, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var existingWine = await ResolveWineAsync(name!, appellation, subAppellation, cancellationToken);
        if (existingWine is not null)
        {
            var updated = false;
            if (string.IsNullOrWhiteSpace(existingWine.GrapeVariety) && !string.IsNullOrWhiteSpace(grapeVariety))
            {
                existingWine.GrapeVariety = grapeVariety;
                updated = true;
            }

            if (existingWine.SubAppellationId != subAppellation.Id
                && string.IsNullOrWhiteSpace(existingWine.SubAppellation?.Name)
                && !string.IsNullOrWhiteSpace(subAppellation.Name)
                && existingWine.SubAppellation?.AppellationId == subAppellation.AppellationId)
            {
                existingWine.SubAppellationId = subAppellation.Id;
                existingWine.SubAppellation = subAppellation;
                updated = true;
            }

            if (updated)
            {
                await _wineRepository.UpdateAsync(existingWine, cancellationToken);
                existingWine = await _wineRepository.GetByIdAsync(existingWine.Id, cancellationToken) ?? existingWine;
            }

            return WineCatalogResult.Success(existingWine, false);
        }

        var wine = new Wine
        {
            Id = Guid.NewGuid(),
            Name = name!,
            Color = color!.Value,
            GrapeVariety = grapeVariety,
            SubAppellationId = subAppellation.Id,
            SubAppellation = subAppellation
        };

        await _wineRepository.AddAsync(wine, cancellationToken);
        var created = await _wineRepository.GetByIdAsync(wine.Id, cancellationToken) ?? wine;
        return WineCatalogResult.Success(created, true);
    }

    private async Task<Country?> ResolveCountryAsync(string name, CancellationToken cancellationToken)
    {
        var existing = await _countryRepository.FindByNameAsync(name, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var candidates = await _countryRepository.SearchByApproximateNameAsync(name, 5, cancellationToken);
        foreach (var candidate in candidates)
        {
            var distance = FuzzyMatchUtilities.CalculateNormalizedDistance(name, candidate.Name);
            if (distance <= LocationMatchThreshold)
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task<Region?> ResolveRegionAsync(string name, Guid? countryId, CancellationToken cancellationToken)
    {
        if (countryId.HasValue)
        {
            var scoped = await _regionRepository.FindByNameAndCountryAsync(name, countryId.Value, cancellationToken);
            if (scoped is not null)
            {
                return scoped;
            }
        }

        var generic = await _regionRepository.FindByNameAsync(name, cancellationToken);
        if (generic is not null && (!countryId.HasValue || generic.CountryId == countryId.Value))
        {
            return generic;
        }

        var candidates = await _regionRepository.SearchByApproximateNameAsync(name, 5, cancellationToken);
        foreach (var candidate in candidates)
        {
            if (countryId.HasValue && candidate.CountryId != countryId.Value)
            {
                continue;
            }

            var distance = FuzzyMatchUtilities.CalculateNormalizedDistance(name, candidate.Name);
            if (distance <= LocationMatchThreshold)
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task<Appellation?> ResolveAppellationAsync(string name, Guid regionId, CancellationToken cancellationToken)
    {
        var existing = await _appellationRepository.FindByNameAndRegionAsync(name, regionId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var candidates = await _appellationRepository.SearchByApproximateNameAsync(name, regionId, 5, cancellationToken);
        foreach (var candidate in candidates)
        {
            var distance = FuzzyMatchUtilities.CalculateNormalizedDistance(name, candidate.Name);
            if (distance <= LocationMatchThreshold)
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task<SubAppellation?> ResolveSubAppellationAsync(string name, Guid appellationId, CancellationToken cancellationToken)
    {
        var existing = await _subAppellationRepository.FindByNameAndAppellationAsync(name, appellationId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var candidates = await _subAppellationRepository.SearchByApproximateNameAsync(name, appellationId, 5, cancellationToken);
        foreach (var candidate in candidates)
        {
            var distance = FuzzyMatchUtilities.CalculateNormalizedDistance(name, candidate.Name);
            if (distance <= LocationMatchThreshold)
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task<Wine?> ResolveWineAsync(string name, Appellation appellation, SubAppellation subAppellation, CancellationToken cancellationToken)
    {
        var subAppellationName = subAppellation.Name;
        var appellationName = appellation.Name;

        var existing = await _wineRepository.FindByNameAsync(name, subAppellationName, appellationName, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var candidates = await _wineRepository.FindClosestMatchesAsync(name, 5, cancellationToken);
        foreach (var candidate in candidates)
        {
            var distance = FuzzyMatchUtilities.CalculateNormalizedDistance(name, candidate.Name);
            if (distance > WineNameMatchThreshold)
            {
                continue;
            }

            var candidateSub = candidate.SubAppellation;
            if (candidateSub is null)
            {
                continue;
            }

            if (candidateSub.Id == subAppellation.Id)
            {
                return candidate;
            }

            var candidateAppellation = candidateSub.Appellation;
            if (candidateAppellation is null || candidateAppellation.Id != appellation.Id)
            {
                continue;
            }

            var candidateSubName = candidateSub.Name?.Trim();
            var targetSubName = subAppellation.Name?.Trim();

            if (string.IsNullOrEmpty(candidateSubName) && string.IsNullOrEmpty(targetSubName))
            {
                return candidate;
            }

            if (!string.IsNullOrEmpty(candidateSubName)
                && !string.IsNullOrEmpty(targetSubName)
                && FuzzyMatchUtilities.CalculateNormalizedDistance(candidateSubName, targetSubName) <= LocationMatchThreshold)
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void AddError(IDictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var list))
        {
            list = new List<string>();
            errors[key] = list;
        }

        if (list.Any(existing => string.Equals(existing, message, StringComparison.Ordinal)))
        {
            return;
        }

        list.Add(message);
    }

    private static IReadOnlyDictionary<string, string[]> ToReadOnly(Dictionary<string, List<string>> errors)
    {
        return errors.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record WineCatalogRequest(
    string? Name,
    string? Color,
    string? Country,
    string? Region,
    string? Appellation,
    string? SubAppellation,
    string? GrapeVariety);

public sealed class WineCatalogResult
{
    private WineCatalogResult(bool isSuccess, bool created, Wine? wine, IReadOnlyDictionary<string, string[]> errors)
    {
        IsSuccess = isSuccess;
        Created = created;
        Wine = wine;
        Errors = errors;
    }

    public bool IsSuccess { get; }
    public bool Created { get; }
    public Wine? Wine { get; }
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public static WineCatalogResult Success(Wine wine, bool created)
    {
        return new WineCatalogResult(true, created, wine, new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));
    }

    public static WineCatalogResult Failure(IReadOnlyDictionary<string, string[]> errors)
    {
        return new WineCatalogResult(false, false, null, errors);
    }
}
