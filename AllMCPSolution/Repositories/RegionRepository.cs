using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Data;
using AllMCPSolution.Models;
using AllMCPSolution.Utilities;

namespace AllMCPSolution.Repositories;

public interface IRegionRepository
{
    Task<List<Region>> GetAllAsync(CancellationToken ct = default);
    Task<Region?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Region?> FindByNameAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<Region>> SearchByApproximateNameAsync(string name, int maxResults = 5, CancellationToken ct = default);
    Task AddAsync(Region region, CancellationToken ct = default);
    Task UpdateAsync(Region region, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public class RegionRepository : IRegionRepository
{
    private readonly ApplicationDbContext _db;
    public RegionRepository(ApplicationDbContext db) => _db = db;

    public async Task<List<Region>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.Regions
            .AsNoTracking()
            .Include(r => r.Country)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);
    }

    public async Task<Region?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Regions
            .AsNoTracking()
            .Include(r => r.Country)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<Region?> FindByNameAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var normalized = name.Trim().ToLowerInvariant();
        return await _db.Regions
            .AsNoTracking()
            .Include(r => r.Country)
            .FirstOrDefaultAsync(r => r.Name.ToLower() == normalized, ct);
    }

    public async Task<IReadOnlyList<Region>> SearchByApproximateNameAsync(string name, int maxResults = 5, CancellationToken ct = default)
    {
        var regions = await _db.Regions
            .AsNoTracking()
            .Include(r => r.Country)
            .ToListAsync(ct);

        return FuzzyMatchUtilities.FindClosestMatches(regions, name, r => r.Name, maxResults);
    }

    public async Task AddAsync(Region region, CancellationToken ct = default)
    {
        _db.Regions.Add(region);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Region region, CancellationToken ct = default)
    {
        _db.Regions.Update(region);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Regions.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (entity is null)
        {
            return;
        }

        _db.Regions.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }
}
