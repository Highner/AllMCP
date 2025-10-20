using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Data;
using AllMCPSolution.Models;

namespace AllMCPSolution.Repositories;

public interface IWineVintageRepository
{
    Task<WineVintage?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<WineVintage?> FindByWineAndVintageAsync(Guid wineId, int vintage, CancellationToken ct = default);
    Task<WineVintage> GetOrCreateAsync(Guid wineId, int vintage, CancellationToken ct = default);
}

public sealed class WineVintageRepository : IWineVintageRepository
{
    private readonly ApplicationDbContext _db;

    public WineVintageRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<WineVintage?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.WineVintages
            .AsNoTracking()
            .Include(wv => wv.Wine)
                .ThenInclude(w => w.SubAppellation)
                    .ThenInclude(sa => sa.Appellation)
                        .ThenInclude(a => a.Region)
                            .ThenInclude(r => r.Country)
            .FirstOrDefaultAsync(wv => wv.Id == id, ct);
    }

    public async Task<WineVintage?> FindByWineAndVintageAsync(Guid wineId, int vintage, CancellationToken ct = default)
    {
        var localMatch = _db.WineVintages.Local.FirstOrDefault(
            wv => wv.WineId == wineId && wv.Vintage == vintage);
        if (localMatch is not null)
        {
            return localMatch;
        }

        return await _db.WineVintages
            .Include(wv => wv.Wine)
                .ThenInclude(w => w.SubAppellation)
                    .ThenInclude(sa => sa.Appellation)
                        .ThenInclude(a => a.Region)
                            .ThenInclude(r => r.Country)
            .FirstOrDefaultAsync(wv => wv.WineId == wineId && wv.Vintage == vintage, ct);
    }

    public async Task<WineVintage> GetOrCreateAsync(Guid wineId, int vintage, CancellationToken ct = default)
    {
        var existing = await FindByWineAndVintageAsync(wineId, vintage, ct);
        if (existing is not null)
        {
            return existing;
        }

        var wine = await _db.Wines
            .Include(w => w.SubAppellation)
                .ThenInclude(sa => sa.Appellation)
                    .ThenInclude(a => a.Region)
                        .ThenInclude(r => r.Country)
            .FirstOrDefaultAsync(w => w.Id == wineId, ct)
            ?? throw new InvalidOperationException($"Wine {wineId} could not be found when creating vintage {vintage}.");

        var entity = new WineVintage
        {
            Id = Guid.NewGuid(),
            WineId = wineId,
            Wine = wine,
            Vintage = vintage
        };

        _db.WineVintages.Add(entity);
        await _db.SaveChangesAsync(ct);

        return entity;
    }
}
