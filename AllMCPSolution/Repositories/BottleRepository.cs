using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
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
}

public class BottleRepository : IBottleRepository
{
    private readonly ApplicationDbContext _db;
    public BottleRepository(ApplicationDbContext db) => _db = db;

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
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Bottle bottle, CancellationToken ct = default)
    {
        _db.Bottles.Update(bottle);
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
            .Where(b => !b.IsDrunk)
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
