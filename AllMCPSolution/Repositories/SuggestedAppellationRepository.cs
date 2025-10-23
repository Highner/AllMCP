using AllMCPSolution.Data;
using AllMCPSolution.Models;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Repositories;

public sealed record SuggestedWineReplacement(Guid WineId, string? Vintage);

public sealed record SuggestedAppellationReplacement(
    Guid SubAppellationId,
    string? Reason,
    IReadOnlyList<SuggestedWineReplacement> Wines);

public interface ISuggestedAppellationRepository
{
    Task<IReadOnlyList<SuggestedAppellation>> GetForUserAsync(Guid userId, CancellationToken ct = default);
    Task ReplaceSuggestionsAsync(
        Guid userId,
        IReadOnlyList<SuggestedAppellationReplacement>? suggestions,
        CancellationToken ct = default);
}

public sealed class SuggestedAppellationRepository : ISuggestedAppellationRepository
{
    private readonly ApplicationDbContext _db;

    public SuggestedAppellationRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<SuggestedAppellation>> GetForUserAsync(Guid userId, CancellationToken ct = default)
    {
        return await _db.SuggestedAppellations
            .AsNoTracking()
            .Where(suggestion => suggestion.UserId == userId)
            .Include(suggestion => suggestion.SubAppellation)
                .ThenInclude(sub => sub.Appellation)
                    .ThenInclude(app => app.Region)
                        .ThenInclude(region => region.Country)
            .Include(suggestion => suggestion.SuggestedWines)
                .ThenInclude(suggestedWine => suggestedWine.Wine)
                    .ThenInclude(wine => wine.SubAppellation)
                        .ThenInclude(sub => sub.Appellation)
                            .ThenInclude(app => app.Region)
                                .ThenInclude(region => region.Country)
            .OrderBy(suggestion => suggestion.SubAppellation.Appellation.Region.Country.Name)
            .ThenBy(suggestion => suggestion.SubAppellation.Appellation.Region.Name)
            .ThenBy(suggestion => suggestion.SubAppellation.Appellation.Name)
            .ThenBy(suggestion => suggestion.SubAppellation.Name)
            .ToListAsync(ct);
    }

    public async Task ReplaceSuggestionsAsync(
        Guid userId,
        IReadOnlyList<SuggestedAppellationReplacement>? suggestions,
        CancellationToken ct = default)
    {
        var existing = await _db.SuggestedAppellations
            .Where(suggestion => suggestion.UserId == userId)
            .ToListAsync(ct);

        if (existing.Count > 0)
        {
            _db.SuggestedAppellations.RemoveRange(existing);
        }

        if (suggestions is not null && suggestions.Count > 0)
        {
            var pending = suggestions
                .Where(entry => entry is not null && entry.SubAppellationId != Guid.Empty)
                .GroupBy(entry => entry.SubAppellationId)
                .Select(group => new SuggestedAppellation
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    SubAppellationId = group.Key,
                    Reason = NormalizeReason(group.First().Reason),
                    SuggestedWines = NormalizeWines(group.SelectMany(entry => entry.Wines ?? Array.Empty<SuggestedWineReplacement>()))
                })
                .ToList();

            if (pending.Count > 0)
            {
                _db.SuggestedAppellations.AddRange(pending);
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private static string? NormalizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        var normalized = reason.ReplaceLineEndings(" ").Trim();
        while (normalized.Contains("  ", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        }

        if (normalized.Length <= 512)
        {
            return normalized;
        }

        return normalized[..512].TrimEnd();
    }

    private static List<SuggestedWine> NormalizeWines(IEnumerable<SuggestedWineReplacement> wines)
    {
        if (wines is null)
        {
            return [];
        }

        var normalized = new List<SuggestedWine>();
        var seen = new HashSet<Guid>();

        foreach (var wine in wines)
        {
            if (wine is null || wine.WineId == Guid.Empty)
            {
                continue;
            }

            if (!seen.Add(wine.WineId))
            {
                continue;
            }

            normalized.Add(new SuggestedWine
            {
                Id = Guid.NewGuid(),
                WineId = wine.WineId,
                Vintage = NormalizeVintage(wine.Vintage)
            });

            if (normalized.Count == 3)
            {
                break;
            }
        }

        return normalized;
    }

    private static string? NormalizeVintage(string? vintage)
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
}
