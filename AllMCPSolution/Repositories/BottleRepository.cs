using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Data;
using AllMCPSolution.Models;

namespace AllMCPSolution.Repositories;

public interface IBottleRepository
{
    Task<List<Bottle>> GetAllAsync(CancellationToken ct = default);
    Task<Bottle?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Bottle bottle, CancellationToken ct = default);
    Task UpdateAsync(Bottle bottle, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public class BottleRepository : IBottleRepository
{
    private readonly ApplicationDbContext _db;
    public BottleRepository(ApplicationDbContext db) => _db = db;

    public async Task<List<Bottle>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.Bottles
            .AsNoTracking()
            .Include(b => b.Wine)
                .ThenInclude(w => w.Region)
                    .ThenInclude(r => r.Country)
            .OrderBy(b => b.Wine.Name)
            .ThenBy(b => b.Vintage)
            .ToListAsync(ct);
    }

    public async Task<Bottle?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Bottles
            .AsNoTracking()
            .Include(b => b.Wine)
                .ThenInclude(w => w.Region)
                    .ThenInclude(r => r.Country)
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
}
