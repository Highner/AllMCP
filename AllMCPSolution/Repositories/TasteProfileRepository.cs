using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Repositories;

public interface ITasteProfileRepository
{
    Task<IReadOnlyList<TasteProfile>> GetForUserAsync(Guid userId, CancellationToken ct = default);
    Task<TasteProfile?> SaveGeneratedProfileAsync(
        Guid userId,
        string? profile,
        string? summary,
        IReadOnlyList<SuggestedAppellationReplacement>? suggestions,
        CancellationToken ct = default);
}

public sealed class TasteProfileRepository : ITasteProfileRepository
{
    private readonly ApplicationDbContext _dbContext;
    private const int TasteProfileMaxLength = 4096;
    private const int TasteProfileSummaryMaxLength = 512;

    public TasteProfileRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<TasteProfile>> GetForUserAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            return Array.Empty<TasteProfile>();
        }

        return await _dbContext.TasteProfiles
            .AsNoTracking()
            .Where(profile => profile.UserId == userId)
            .OrderByDescending(profile => profile.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<TasteProfile?> SaveGeneratedProfileAsync(
        Guid userId,
        string? profile,
        string? summary,
        IReadOnlyList<SuggestedAppellationReplacement>? suggestions,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            return null;
        }

        ct.ThrowIfCancellationRequested();

        var userExists = await _dbContext.Users
            .AnyAsync(user => user.Id == userId, ct);

        if (!userExists)
        {
            return null;
        }

        var normalizedProfile = NormalizeProfile(profile);
        var normalizedSummary = NormalizeSummary(summary);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);

        var existingProfiles = await _dbContext.TasteProfiles
            .Where(entry => entry.UserId == userId)
            .ToListAsync(ct);

        var updatedExisting = false;
        foreach (var entry in existingProfiles)
        {
            if (!entry.InUse)
            {
                continue;
            }

            entry.InUse = false;
            updatedExisting = true;
        }

        if (updatedExisting)
        {
            await _dbContext.SaveChangesAsync(ct);
        }

        var newProfile = new TasteProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Profile = normalizedProfile,
            Summary = normalizedSummary,
            CreatedAt = DateTime.UtcNow,
            InUse = true
        };

        var normalizedSuggestions = BuildSuggestedAppellations(newProfile.Id, suggestions);
        if (normalizedSuggestions.Count > 0)
        {
            newProfile.SuggestedAppellations = normalizedSuggestions;
        }

        _dbContext.TasteProfiles.Add(newProfile);

        await _dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return newProfile;
    }

    private static string NormalizeProfile(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
        {
            return string.Empty;
        }

        var normalized = profile.Trim();
        if (normalized.Length <= TasteProfileMaxLength)
        {
            return normalized;
        }

        return normalized[..TasteProfileMaxLength];
    }

    private static string NormalizeSummary(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return string.Empty;
        }

        var normalized = summary.Trim();
        if (normalized.Length <= TasteProfileSummaryMaxLength)
        {
            return normalized;
        }

        return normalized[..TasteProfileSummaryMaxLength];
    }

    private static List<SuggestedAppellation> BuildSuggestedAppellations(
        Guid tasteProfileId,
        IReadOnlyList<SuggestedAppellationReplacement>? suggestions)
    {
        if (suggestions is null || suggestions.Count == 0)
        {
            return [];
        }

        var normalized = new List<SuggestedAppellation>();

        var grouped = suggestions
            .Where(entry => entry is not null && entry.SubAppellationId != Guid.Empty)
            .GroupBy(entry => entry.SubAppellationId);

        foreach (var group in grouped)
        {
            var first = group.First();
            var suggestion = new SuggestedAppellation
            {
                Id = Guid.NewGuid(),
                TasteProfileId = tasteProfileId,
                SubAppellationId = group.Key,
                Reason = NormalizeReason(first.Reason)
            };

            var wines = NormalizeWines(group.SelectMany(entry => entry.Wines ?? Array.Empty<SuggestedWineReplacement>()));
            if (wines.Count > 0)
            {
                foreach (var wine in wines)
                {
                    wine.SuggestedAppellationId = suggestion.Id;
                    wine.SuggestedAppellation = suggestion;
                }

                suggestion.SuggestedWines = wines;
            }

            normalized.Add(suggestion);
        }

        return normalized;
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
