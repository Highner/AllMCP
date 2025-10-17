using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Data;
using AllMCPSolution.Models;

namespace AllMCPSolution.Repositories;

public interface IWineRepository
{
    Task<List<Wine>> GetAllAsync(CancellationToken ct = default);
    Task<Wine?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Wine wine, CancellationToken ct = default);
    Task UpdateAsync(Wine wine, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public class WineRepository : IWineRepository
{
    private readonly ApplicationDbContext _db;
    public WineRepository(ApplicationDbContext db) => _db = db;

    public async Task<List<Wine>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.Wines
            .AsNoTracking()
            .Include(w => w.Country)
            .Include(w => w.Region)
            .Include(w => w.WineVintages)
            .OrderBy(w => w.Name)
            .ToListAsync(ct);
    }

    public async Task<Wine?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Wines
            .AsNoTracking()
            .Include(w => w.Country)
            .Include(w => w.Region)
            .Include(w => w.WineVintages)
            .FirstOrDefaultAsync(w => w.Id == id, ct);
    }

    public async Task AddAsync(Wine wine, CancellationToken ct = default)
    {
        _db.Wines.Add(wine);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Wine wine, CancellationToken ct = default)
    {
        _db.Wines.Update(wine);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Wines.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (entity is null)
        {
            return;
        }

        _db.Wines.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }
}
