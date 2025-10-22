using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Repositories;

public interface IWineVintageEvolutionScoreRepository
{
    Task<IReadOnlyList<WineVintageEvolutionScore>> GetByWineIdAsync(Guid userId, Guid wineId, CancellationToken ct = default);
    Task<IReadOnlyList<WineVintageEvolutionScore>> GetByWineVintageIdAsync(Guid userId, Guid wineVintageId, CancellationToken ct = default);
    Task<WineVintageEvolutionScore?> FindAsync(Guid userId, Guid wineVintageId, int year, CancellationToken ct = default);
    Task UpsertRangeAsync(Guid userId, IReadOnlyCollection<WineVintageEvolutionScore> scores, CancellationToken ct = default);
    Task RemoveByIdsAsync(Guid userId, IEnumerable<Guid> ids, CancellationToken ct = default);
    Task RemoveByPairsAsync(Guid userId, IEnumerable<(Guid WineVintageId, int Year)> pairs, CancellationToken ct = default);
    Task ReplaceForWineAsync(Guid userId, Guid wineId, IReadOnlyCollection<WineVintageEvolutionScore> desiredScores, CancellationToken ct = default);
}

public sealed class WineVintageEvolutionScoreRepository : IWineVintageEvolutionScoreRepository
{
    private readonly ApplicationDbContext _db;

    public WineVintageEvolutionScoreRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<WineVintageEvolutionScore>> GetByWineIdAsync(Guid userId, Guid wineId, CancellationToken ct = default)
    {
        return await _db.WineVintageEvolutionScores
            .AsNoTracking()
            .Include(ev => ev.WineVintage)
                .ThenInclude(wv => wv.Wine)
                    .ThenInclude(w => w.SubAppellation)
                        .ThenInclude(sa => sa.Appellation)
                            .ThenInclude(a => a.Region)
                                .ThenInclude(r => r.Country)
            .Where(ev => ev.WineVintage.WineId == wineId && ev.UserId == userId)
            .OrderBy(ev => ev.WineVintage.Vintage)
            .ThenBy(ev => ev.Year)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<WineVintageEvolutionScore>> GetByWineVintageIdAsync(Guid userId, Guid wineVintageId, CancellationToken ct = default)
    {
        return await _db.WineVintageEvolutionScores
            .AsNoTracking()
            .Include(ev => ev.WineVintage)
                .ThenInclude(wv => wv.Wine)
                    .ThenInclude(w => w.SubAppellation)
                        .ThenInclude(sa => sa.Appellation)
                            .ThenInclude(a => a.Region)
                                .ThenInclude(r => r.Country)
            .Where(ev => ev.WineVintageId == wineVintageId && ev.UserId == userId)
            .OrderBy(ev => ev.Year)
            .ToListAsync(ct);
    }

    public async Task<WineVintageEvolutionScore?> FindAsync(Guid userId, Guid wineVintageId, int year, CancellationToken ct = default)
    {
        return await _db.WineVintageEvolutionScores
            .Include(ev => ev.WineVintage)
            .FirstOrDefaultAsync(ev => ev.WineVintageId == wineVintageId && ev.Year == year && ev.UserId == userId, ct);
    }

    public async Task UpsertRangeAsync(Guid userId, IReadOnlyCollection<WineVintageEvolutionScore> scores, CancellationToken ct = default)
    {
        if (scores.Count == 0)
        {
            return;
        }

        var sanitizedScores = scores
            .Select(score =>
            {
                if (score.UserId != userId)
                {
                    score.UserId = userId;
                }

                return score;
            })
            .ToList();

        var pairs = sanitizedScores.Select(s => (s.WineVintageId, s.Year)).ToHashSet();
        var wineVintageIds = pairs.Select(p => p.WineVintageId).Distinct().ToList();

        var existing = await _db.WineVintageEvolutionScores
            .Where(ev => wineVintageIds.Contains(ev.WineVintageId) && ev.UserId == userId)
            .ToListAsync(ct);

        var pending = sanitizedScores.ToDictionary(s => (s.WineVintageId, s.Year));

        foreach (var entity in existing)
        {
            if (pending.TryGetValue((entity.WineVintageId, entity.Year), out var updated))
            {
                entity.Score = updated.Score;
                entity.UserId = userId;
                pending.Remove((entity.WineVintageId, entity.Year));
            }
        }

        foreach (var entry in pending.Values)
        {
            if (entry.Id == Guid.Empty)
            {
                entry.Id = Guid.NewGuid();
            }

            entry.UserId = userId;
            _db.WineVintageEvolutionScores.Add(entry);
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveByIdsAsync(Guid userId, IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
        {
            return;
        }

        var entities = await _db.WineVintageEvolutionScores
            .Where(ev => idList.Contains(ev.Id) && ev.UserId == userId)
            .ToListAsync(ct);

        if (entities.Count == 0)
        {
            return;
        }

        _db.WineVintageEvolutionScores.RemoveRange(entities);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveByPairsAsync(Guid userId, IEnumerable<(Guid WineVintageId, int Year)> pairs, CancellationToken ct = default)
    {
        var pairSet = pairs.ToHashSet();
        if (pairSet.Count == 0)
        {
            return;
        }

        var wineVintageIds = pairSet.Select(p => p.WineVintageId).Distinct().ToList();

        var entities = await _db.WineVintageEvolutionScores
            .Where(ev => wineVintageIds.Contains(ev.WineVintageId) && ev.UserId == userId)
            .ToListAsync(ct);

        var toRemove = entities
            .Where(ev => pairSet.Contains((ev.WineVintageId, ev.Year)))
            .ToList();

        if (toRemove.Count == 0)
        {
            return;
        }

        _db.WineVintageEvolutionScores.RemoveRange(toRemove);
        await _db.SaveChangesAsync(ct);
    }

    public async Task ReplaceForWineAsync(Guid userId, Guid wineId, IReadOnlyCollection<WineVintageEvolutionScore> desiredScores, CancellationToken ct = default)
    {
        var sanitizedScores = desiredScores
            .Select(score =>
            {
                if (score.UserId != userId)
                {
                    score.UserId = userId;
                }

                return score;
            })
            .ToList();

        var desired = sanitizedScores.ToDictionary(s => (s.WineVintageId, s.Year));

        var existing = await _db.WineVintageEvolutionScores
            .Where(ev => ev.WineVintage.WineId == wineId && ev.UserId == userId)
            .ToListAsync(ct);

        foreach (var entity in existing)
        {
            if (desired.TryGetValue((entity.WineVintageId, entity.Year), out var replacement))
            {
                entity.Score = replacement.Score;
                entity.UserId = userId;
                desired.Remove((entity.WineVintageId, entity.Year));
            }
            else
            {
                _db.WineVintageEvolutionScores.Remove(entity);
            }
        }

        foreach (var entry in desired.Values)
        {
            if (entry.Id == Guid.Empty)
            {
                entry.Id = Guid.NewGuid();
            }

            entry.UserId = userId;
            _db.WineVintageEvolutionScores.Add(entry);
        }

        await _db.SaveChangesAsync(ct);
    }
}
