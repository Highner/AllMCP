using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Repositories;

public interface IBottleLocationRepository
{
    Task<List<BottleLocation>> GetAllAsync(CancellationToken ct = default);
    Task<BottleLocation?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<BottleLocation?> FindByNameAsync(string name, CancellationToken ct = default);
    Task AddAsync(BottleLocation location, CancellationToken ct = default);
    Task UpdateAsync(BottleLocation location, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public class BottleLocationRepository : IBottleLocationRepository
{
    private readonly ApplicationDbContext _db;

    public BottleLocationRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<BottleLocation>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.BottleLocations
            .AsNoTracking()
            .OrderBy(l => l.Name)
            .ToListAsync(ct);
    }

    public async Task<BottleLocation?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.BottleLocations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id, ct);
    }

    public async Task<BottleLocation?> FindByNameAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var normalized = name.Trim();
        return await _db.BottleLocations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Name == normalized, ct);
    }

    public async Task AddAsync(BottleLocation location, CancellationToken ct = default)
    {
        _db.BottleLocations.Add(location);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(BottleLocation location, CancellationToken ct = default)
    {
        _db.BottleLocations.Update(location);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.BottleLocations.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (entity is null)
        {
            return;
        }

        _db.BottleLocations.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }
}
