using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Data;
using AllMCPSolution.Models;
using AllMCPSolution.Utilities;

namespace AllMCPSolution.Repositories;

public interface IWineRepository
{
    Task<List<Wine>> GetAllAsync(CancellationToken ct = default);
    Task<Wine?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Wine?> FindByNameAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<Wine>> FindClosestMatchesAsync(string name, int maxResults = 5, CancellationToken ct = default);
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
            .Include(w => w.Bottles)
            .OrderBy(w => w.Name)
            .ToListAsync(ct);
    }

    public async Task<Wine?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Wines
            .AsNoTracking()
            .Include(w => w.Country)
            .Include(w => w.Region)
            .Include(w => w.Bottles)
            .FirstOrDefaultAsync(w => w.Id == id, ct);
    }

    public async Task<Wine?> FindByNameAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var normalized = name.Trim().ToLowerInvariant();
        return await _db.Wines
            .AsNoTracking()
            .Include(w => w.Country)
            .Include(w => w.Region)
            .FirstOrDefaultAsync(w => w.Name.ToLower() == normalized, ct);
    }

    public async Task<IReadOnlyList<Wine>> FindClosestMatchesAsync(string name, int maxResults = 5, CancellationToken ct = default)
    {
        var wines = await _db.Wines
            .AsNoTracking()
            .Include(w => w.Country)
            .Include(w => w.Region)
            .ToListAsync(ct);

        return FuzzyMatchUtilities.FindClosestMatches(wines, name, w => w.Name, maxResults);
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
