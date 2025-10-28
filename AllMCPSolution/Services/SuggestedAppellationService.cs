using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Controllers; // Reuse view model records
using AllMCPSolution.Repositories;

namespace AllMCPSolution.Services;

public class SuggestedAppellationService : ISuggestedAppellationService
{
    private readonly ISuggestedAppellationRepository _suggestedAppellationRepository;

    public SuggestedAppellationService(ISuggestedAppellationRepository suggestedAppellationRepository)
    {
        _suggestedAppellationRepository = suggestedAppellationRepository;
    }

    public async Task<IReadOnlyList<WineSurferSuggestedAppellation>> GetForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var suggestions = await _suggestedAppellationRepository.GetForUserAsync(userId, cancellationToken);
        if (suggestions.Count == 0)
        {
            return Array.Empty<WineSurferSuggestedAppellation>();
        }

        var results = new List<WineSurferSuggestedAppellation>(suggestions.Count);
        foreach (var suggestion in suggestions)
        {
            var subAppellation = suggestion.SubAppellation;
            if (subAppellation is null)
            {
                continue;
            }

            var appellation = subAppellation.Appellation;
            var region = appellation?.Region;
            var country = region?.Country;

            if (appellation is null || region is null || country is null)
            {
                continue;
            }

            var subName = string.IsNullOrWhiteSpace(subAppellation.Name)
                ? null
                : subAppellation.Name.Trim();

            var wines = new List<WineSurferSuggestedWine>();
            if (suggestion.SuggestedWines is not null && suggestion.SuggestedWines.Count > 0)
            {
                foreach (var stored in suggestion.SuggestedWines)
                {
                    if (stored?.Wine is null)
                    {
                        continue;
                    }

                    var displayName = NormalizeSuggestedWineName(stored.Wine.Name);
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        continue;
                    }

                    var variety = NormalizeSuggestedWineVariety(stored.Wine.GrapeVariety);
                    var vintage = NormalizeSuggestedWineVintage(stored.Vintage);
                    var storedSubName = stored.Wine.SubAppellation?.Name?.Trim();

                    wines.Add(new WineSurferSuggestedWine(
                        stored.WineId,
                        displayName!,
                        stored.Wine.Color.ToString(),
                        variety,
                        vintage,
                        string.IsNullOrWhiteSpace(storedSubName) ? null : storedSubName,
                        null));

                    if (wines.Count == 3)
                    {
                        break;
                    }
                }
            }

            var wineResults = wines.Count == 0 ? Array.Empty<WineSurferSuggestedWine>() : wines.ToArray();

            var reason = NormalizeSuggestedReason(suggestion.Reason);

            results.Add(new WineSurferSuggestedAppellation(
                subAppellation.Id,
                subName,
                appellation.Id,
                appellation.Name?.Trim() ?? string.Empty,
                region.Name?.Trim() ?? string.Empty,
                country.Name?.Trim() ?? string.Empty,
                reason,
                wineResults));
        }

        return results.Count == 0 ? Array.Empty<WineSurferSuggestedAppellation>() : results.ToArray();
    }

    private static string? NormalizeSuggestedWineName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim();
        if (trimmed.Length <= 256)
        {
            return trimmed;
        }

        return trimmed[..256].TrimEnd();
    }

    private static string? NormalizeSuggestedWineVariety(string? variety)
    {
        if (string.IsNullOrWhiteSpace(variety))
        {
            return null;
        }

        var trimmed = variety.Trim();
        if (trimmed.Length <= 128)
        {
            return trimmed;
        }

        return trimmed[..128].TrimEnd();
    }

    private static string? NormalizeSuggestedWineVintage(string? vintage)
    {
        if (string.IsNullOrWhiteSpace(vintage))
        {
            return null;
        }

        var trimmed = vintage.Trim();
        if (trimmed.Length <= 32)
        {
            return trimmed;
        }

        return trimmed[..32].TrimEnd();
    }

    private static string? NormalizeSuggestedReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        // Cap at 400 chars to be safe for UI display
        var trimmed = reason.Trim();
        if (trimmed.Length <= 400)
        {
            return trimmed;
        }

        return trimmed[..400].TrimEnd();
    }
}