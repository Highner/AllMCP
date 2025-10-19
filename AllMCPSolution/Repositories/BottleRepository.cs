using System.Linq;
using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Data;
using AllMCPSolution.Models;

namespace AllMCPSolution.Repositories;

public interface IBottleRepository
{
    Task<List<Bottle>> GetAllAsync(CancellationToken ct = default);
    Task<List<Bottle>> GetAllWithTastingNotesAsync(CancellationToken ct = default);
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

    public Task<List<Bottle>> GetAllWithTastingNotesAsync(CancellationToken ct = default)
    {
        return GetAllAsync(ct);
    }

    private IQueryable<Bottle> BuildBottleQuery()
    {
        return _db.Bottles
            .AsNoTracking()
            .Include(b => b.TastingNotes)
                .ThenInclude(tn => tn.User)
            .Include(b => b.WineVintage)
                .ThenInclude(wv => wv.Wine)
                    .ThenInclude(w => w.Appellation)
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
            .Include(b => b.TastingNotes)
                .ThenInclude(tn => tn.User)
            .Include(b => b.WineVintage)
                .ThenInclude(wv => wv.Wine)
                    .ThenInclude(w => w.Appellation)
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
            .Select(b => new ActiveBottleLocation(
                b.WineVintage.Wine.Appellation != null && b.WineVintage.Wine.Appellation.Region != null
                    ? b.WineVintage.Wine.Appellation.Region.Id
                    : (Guid?)null,
                b.WineVintage.Wine.Appellation != null && b.WineVintage.Wine.Appellation.Region != null
                    ? b.WineVintage.Wine.Appellation.Region.Name
                    : null,
                b.WineVintage.Wine.Appellation != null
                    ? b.WineVintage.Wine.Appellation.Id
                    : (Guid?)null,
                b.WineVintage.Wine.Appellation != null
                    ? b.WineVintage.Wine.Appellation.Name
                    : null,
                (int?)b.WineVintage.Vintage))
            .ToListAsync(ct);
    }
}
