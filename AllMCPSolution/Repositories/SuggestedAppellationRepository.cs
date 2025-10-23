using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Data;
using AllMCPSolution.Models;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Repositories;

public interface ISuggestedAppellationRepository
{
    Task<IReadOnlyList<SuggestedAppellation>> GetForUserAsync(Guid userId, CancellationToken ct = default);
    Task ReplaceSuggestionsAsync(Guid userId, IReadOnlyList<Guid>? subAppellationIds, CancellationToken ct = default);
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
            .OrderBy(suggestion => suggestion.SubAppellation.Appellation.Region.Country.Name)
            .ThenBy(suggestion => suggestion.SubAppellation.Appellation.Region.Name)
            .ThenBy(suggestion => suggestion.SubAppellation.Appellation.Name)
            .ThenBy(suggestion => suggestion.SubAppellation.Name)
            .ToListAsync(ct);
    }

    public async Task ReplaceSuggestionsAsync(Guid userId, IReadOnlyList<Guid>? subAppellationIds, CancellationToken ct = default)
    {
        var existing = await _db.SuggestedAppellations
            .Where(suggestion => suggestion.UserId == userId)
            .ToListAsync(ct);

        if (existing.Count > 0)
        {
            _db.SuggestedAppellations.RemoveRange(existing);
        }

        if (subAppellationIds is not null && subAppellationIds.Count > 0)
        {
            var pending = subAppellationIds
                .Where(id => id != Guid.Empty)
                .Distinct()
                .Select(id => new SuggestedAppellation
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    SubAppellationId = id
                })
                .ToList();

            if (pending.Count > 0)
            {
                _db.SuggestedAppellations.AddRange(pending);
            }
        }

        await _db.SaveChangesAsync(ct);
    }
}
