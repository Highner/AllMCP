using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using AllMCPSolution.Data;
using AllMCPSolution.Models;

namespace AllMCPSolution.Repositories;

public interface IBottleRepository
{
    Task<List<Bottle>> GetAllAsync(CancellationToken ct = default);
    Task<List<Bottle>> GetByWineVintageIdAsync(Guid wineVintageId, CancellationToken ct = default);
    Task<Bottle?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Bottle bottle, CancellationToken ct = default);
    Task UpdateAsync(Bottle bottle, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<List<ActiveBottleLocation>> GetActiveBottleLocationsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Bottle>> GetAvailableForUserAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<Bottle>> GetForUserAsync(Guid userId, CancellationToken ct = default);
    Task<bool> MarkAsDrunkAsync(Guid bottleId, Guid ownerUserId, DateTime? drunkAt, CancellationToken ct = default);
    Task<IReadOnlyList<Bottle>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task UpdateManyAsync(IEnumerable<Bottle> bottles, CancellationToken ct = default);
}

public class BottleRepository : IBottleRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<BottleRepository> _logger;

    public BottleRepository(ApplicationDbContext db, ILogger<BottleRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<Bottle>> GetAllAsync(CancellationToken ct = default)
    {
        return await BuildBottleQuery()
            .ToListAsync(ct);
    }

    public async Task<List<Bottle>> GetByWineVintageIdAsync(Guid wineVintageId, CancellationToken ct = default)
    {
        return await BuildBottleQuery()
            .Where(b => b.WineVintageId == wineVintageId)
            .ToListAsync(ct);
    }


    public async Task<IReadOnlyList<Bottle>> GetAvailableForUserAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            return Array.Empty<Bottle>();
        }

        return await BuildBottleQuery()
            .Where(b => b.UserId == userId && !b.IsDrunk && !b.PendingDelivery)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Bottle>> GetForUserAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            return Array.Empty<Bottle>();
        }

        return await BuildBottleQuery()
            .Where(b => b.UserId == userId)
            .ToListAsync(ct);
    }


    public async Task<bool> MarkAsDrunkAsync(Guid bottleId, Guid ownerUserId, DateTime? drunkAt, CancellationToken ct = default)
    {
        if (bottleId == Guid.Empty || ownerUserId == Guid.Empty)
        {
            return false;
        }

        var entity = await _db.Bottles
            .FirstOrDefaultAsync(b => b.Id == bottleId && b.UserId == ownerUserId, ct);

        if (entity is null)
        {
            return false;
        }

        entity.IsDrunk = true;
        entity.PendingDelivery = false;
        entity.DrunkAt = drunkAt ?? DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<Bottle>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        if (ids is null)
        {
            return Array.Empty<Bottle>();
        }

        var idList = ids
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (idList.Count == 0)
        {
            return Array.Empty<Bottle>();
        }

        return await _db.Bottles
            .AsNoTracking()
            .Where(b => idList.Contains(b.Id))
            .ToListAsync(ct);
    }

    public async Task UpdateManyAsync(IEnumerable<Bottle> bottles, CancellationToken ct = default)
    {
        var entities = bottles?.ToList();
        if (entities is null || entities.Count == 0)
        {
            return;
        }

        _db.Bottles.UpdateRange(entities);
        await _db.SaveChangesAsync(ct);
    }


    private IQueryable<Bottle> BuildBottleQuery()
    {
        return _db.Bottles
            .AsNoTracking()
            .Include(b => b.User)
            .Include(b => b.BottleLocation)
            .Include(b => b.TastingNotes)
                .ThenInclude(tn => tn.User)
            .Include(b => b.WineVintage)
                .ThenInclude(wv => wv.Wine)
                    .ThenInclude(w => w.SubAppellation)
                        .ThenInclude(sa => sa.Appellation)
                            .ThenInclude(a => a.Region)
                                .ThenInclude(r => r.Country)
            .Include(b => b.WineVintage)
                .ThenInclude(wv => wv.EvolutionScores)
            .OrderBy(b => b.WineVintage.Wine.Name)
            .ThenBy(b => b.WineVintage.Vintage);
    }

    public async Task<Bottle?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Bottles
            .AsNoTracking()
            .Include(b => b.User)
            .Include(b => b.BottleLocation)
            .Include(b => b.TastingNotes)
                .ThenInclude(tn => tn.User)
            .Include(b => b.WineVintage)
                .ThenInclude(wv => wv.Wine)
                    .ThenInclude(w => w.SubAppellation)
                        .ThenInclude(sa => sa.Appellation)
                            .ThenInclude(a => a.Region)
                                .ThenInclude(r => r.Country)
            .Include(b => b.WineVintage)
                .ThenInclude(wv => wv.EvolutionScores)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
    }

    public async Task AddAsync(Bottle bottle, CancellationToken ct = default)
    {
        _db.Bottles.Add(bottle);
        _logger.LogInformation("Adding bottle {BottleId} for user {UserId} with location {BottleLocationId}.", bottle.Id, bottle.UserId, bottle.BottleLocationId);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Bottle bottle, CancellationToken ct = default)
    {
        _db.Bottles.Update(bottle);
        _logger.LogInformation("Updating bottle {BottleId} for user {UserId} with location {BottleLocationId}.", bottle.Id, bottle.UserId, bottle.BottleLocationId);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Bottles.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (entity is null)
        {
            return;
        }

        _db.Bottles.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<ActiveBottleLocation>> GetActiveBottleLocationsAsync(CancellationToken ct = default)
    {
        return await _db.Bottles
            .AsNoTracking()
            .Where(b => !b.IsDrunk && !b.PendingDelivery)
            .Select(b => new
            {
                SubAppellation = b.WineVintage.Wine.SubAppellation,
                Appellation = b.WineVintage.Wine.SubAppellation != null
                    ? b.WineVintage.Wine.SubAppellation.Appellation
                    : null,
                Region = b.WineVintage.Wine.SubAppellation != null
                    && b.WineVintage.Wine.SubAppellation.Appellation != null
                        ? b.WineVintage.Wine.SubAppellation.Appellation.Region
                        : null,
                Vintage = b.WineVintage.Vintage
            })
            .Select(x => new ActiveBottleLocation(
                x.Region != null ? (Guid?)x.Region.Id : null,
                x.Region != null ? x.Region.Name : null,
                x.Appellation != null ? (Guid?)x.Appellation.Id : null,
                x.Appellation != null ? x.Appellation.Name : null,
                x.SubAppellation != null ? (Guid?)x.SubAppellation.Id : null,
                x.SubAppellation != null ? x.SubAppellation.Name : null,
                (int?)x.Vintage))
            .ToListAsync(ct);
    }
}
