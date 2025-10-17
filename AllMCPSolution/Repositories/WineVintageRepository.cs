using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Data;
using AllMCPSolution.Models;

namespace AllMCPSolution.Repositories;

public interface IWineVintageRepository
{
    Task<List<WineVintage>> GetAllAsync(CancellationToken ct = default);
    Task<List<WineVintage>> GetByWineIdAsync(Guid wineId, CancellationToken ct = default);
    Task<WineVintage?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(WineVintage wineVintage, CancellationToken ct = default);
    Task UpdateAsync(WineVintage wineVintage, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public class WineVintageRepository : IWineVintageRepository
{
    private readonly ApplicationDbContext _db;
    public WineVintageRepository(ApplicationDbContext db) => _db = db;

    public async Task<List<WineVintage>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.WineVintages
            .AsNoTracking()
            .Include(wv => wv.Wine)
                .ThenInclude(w => w.Country)
            .Include(wv => wv.Wine)
                .ThenInclude(w => w.Region)
            .OrderBy(wv => wv.Wine.Name)
            .ThenBy(wv => wv.Vintage)
            .ToListAsync(ct);
    }

    public async Task<List<WineVintage>> GetByWineIdAsync(Guid wineId, CancellationToken ct = default)
    {
        return await _db.WineVintages
            .AsNoTracking()
            .Where(wv => wv.WineId == wineId)
            .Include(wv => wv.Wine)
                .ThenInclude(w => w.Country)
            .Include(wv => wv.Wine)
                .ThenInclude(w => w.Region)
            .OrderBy(wv => wv.Vintage)
            .ToListAsync(ct);
    }

    public async Task<WineVintage?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.WineVintages
            .AsNoTracking()
            .Include(wv => wv.Wine)
                .ThenInclude(w => w.Country)
            .Include(wv => wv.Wine)
                .ThenInclude(w => w.Region)
            .FirstOrDefaultAsync(wv => wv.Id == id, ct);
    }

    public async Task AddAsync(WineVintage wineVintage, CancellationToken ct = default)
    {
        _db.WineVintages.Add(wineVintage);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(WineVintage wineVintage, CancellationToken ct = default)
    {
        _db.WineVintages.Update(wineVintage);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.WineVintages.FirstOrDefaultAsync(wv => wv.Id == id, ct);
        if (entity is null)
        {
            return;
        }

        _db.WineVintages.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }
}
