using System.Linq;
using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Data;
using AllMCPSolution.Models;
using AllMCPSolution.Utilities;

namespace AllMCPSolution.Repositories;

public interface IWineRepository
{
    Task<List<Wine>> GetAllAsync(CancellationToken ct = default);
    Task<Wine?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Wine?> FindByNameAsync(string name, string? subAppellation = null, string? appellation = null, CancellationToken ct = default);
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
            .Include(w => w.SubAppellation)
                .ThenInclude(sa => sa.Appellation)
                    .ThenInclude(a => a.Region)
                        .ThenInclude(r => r.Country)
            .Include(w => w.WineVintages)
                .ThenInclude(wv => wv.Bottles)
                    .ThenInclude(b => b.TastingNotes)
            .OrderBy(w => w.Name)
            .ThenBy(w => w.SubAppellation.Appellation.Name)
            .ThenBy(w => w.SubAppellation.Name)
            .ToListAsync(ct);
    }

    public async Task<Wine?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Wines
            .AsNoTracking()
            .Include(w => w.SubAppellation)
                .ThenInclude(sa => sa.Appellation)
                    .ThenInclude(a => a.Region)
                        .ThenInclude(r => r.Country)
            .Include(w => w.WineVintages)
                .ThenInclude(wv => wv.Bottles)
                    .ThenInclude(b => b.TastingNotes)
            .FirstOrDefaultAsync(w => w.Id == id, ct);
    }

    public async Task<Wine?> FindByNameAsync(string name, string? subAppellation = null, string? appellation = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var normalized = name.Trim().ToLowerInvariant();
        var query = _db.Wines
            .AsNoTracking()
            .Include(w => w.SubAppellation)
                .ThenInclude(sa => sa.Appellation)
                    .ThenInclude(a => a.Region)
                        .ThenInclude(r => r.Country);

        if (!string.IsNullOrWhiteSpace(subAppellation))
        {
            var normalizedSubAppellation = subAppellation.Trim().ToLowerInvariant();
            return await query.FirstOrDefaultAsync(
                w => w.Name.ToLower() == normalized
                    && w.SubAppellation != null
                    && w.SubAppellation.Name.ToLower() == normalizedSubAppellation,
                ct);
        }

        if (!string.IsNullOrWhiteSpace(appellation))
        {
            var normalizedAppellation = appellation.Trim().ToLowerInvariant();
            return await query.FirstOrDefaultAsync(
                w => w.Name.ToLower() == normalized
                    && w.SubAppellation != null
                    && w.SubAppellation.Appellation != null
                    && w.SubAppellation.Appellation.Name.ToLower() == normalizedAppellation,
                ct);
        }

        return await query
            .Where(w => w.Name.ToLower() == normalized)
            .OrderBy(w => w.SubAppellation.Appellation.Name)
            .ThenBy(w => w.SubAppellation.Name)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<Wine>> FindClosestMatchesAsync(string name, int maxResults = 5, CancellationToken ct = default)
    {
        var wines = await _db.Wines
            .AsNoTracking()
            .Include(w => w.SubAppellation)
                .ThenInclude(sa => sa.Appellation)
                    .ThenInclude(a => a.Region)
                        .ThenInclude(r => r.Country)
            .ToListAsync(ct);

        return FuzzyMatchUtilities.FindClosestMatches(
            wines,
            name,
            w => w.SubAppellation is null
                ? w.Name
                : string.IsNullOrWhiteSpace(w.SubAppellation.Appellation?.Name)
                    ? $"{w.Name} ({w.SubAppellation.Name})"
                    : $"{w.Name} ({w.SubAppellation.Name}, {w.SubAppellation.Appellation.Name})",
            maxResults);
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
