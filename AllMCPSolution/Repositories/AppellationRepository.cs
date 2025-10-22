using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Data;
using AllMCPSolution.Models;
using AllMCPSolution.Utilities;

namespace AllMCPSolution.Repositories;

public interface IAppellationRepository
{
    Task<IReadOnlyList<Appellation>> GetAllAsync(CancellationToken ct = default);
    Task<Appellation?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Appellation?> FindByNameAndRegionAsync(string name, Guid regionId, CancellationToken ct = default);
    Task<IReadOnlyList<Appellation>> SearchByApproximateNameAsync(string name, Guid regionId, int maxResults = 5, CancellationToken ct = default);
    Task<bool> AnyForRegionAsync(Guid regionId, CancellationToken ct = default);
    Task<Appellation> GetOrCreateAsync(string name, Guid regionId, CancellationToken ct = default);
    Task AddAsync(Appellation appellation, CancellationToken ct = default);
    Task UpdateAsync(Appellation appellation, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class AppellationRepository : IAppellationRepository
{
    private readonly ApplicationDbContext _db;

    public AppellationRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Appellation>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.Appellations
            .AsNoTracking()
            .Include(a => a.Region)
                .ThenInclude(r => r.Country)
            .Include(a => a.SubAppellations)
            .OrderBy(a => a.Region.Name)
            .ThenBy(a => a.Name)
            .ToListAsync(ct);
    }

    public async Task<Appellation?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Appellations
            .AsNoTracking()
            .Include(a => a.Region)
                .ThenInclude(r => r.Country)
            .Include(a => a.SubAppellations)
                .ThenInclude(sa => sa.Wines)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<Appellation?> FindByNameAndRegionAsync(string name, Guid regionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim();
        var localMatch = _db.Appellations.Local.FirstOrDefault(
            a => a.RegionId == regionId && string.Equals(a.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (localMatch is not null)
        {
            return localMatch;
        }

        var normalized = trimmed.ToLowerInvariant();
        return await _db.Appellations
            .Include(a => a.Region)
                .ThenInclude(r => r.Country)
            .FirstOrDefaultAsync(a => a.RegionId == regionId && a.Name.ToLower() == normalized, ct);
    }

    public async Task<IReadOnlyList<Appellation>> SearchByApproximateNameAsync(string name, Guid regionId, int maxResults = 5, CancellationToken ct = default)
    {
        var appellations = await _db.Appellations
            .AsNoTracking()
            .Where(a => a.RegionId == regionId)
            .Include(a => a.Region)
                .ThenInclude(r => r.Country)
            .ToListAsync(ct);

        return FuzzyMatchUtilities.FindClosestMatches(appellations, name, a => a.Name, maxResults);
    }

    public async Task<Appellation> GetOrCreateAsync(string name, Guid regionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Appellation name cannot be empty.", nameof(name));
        }

        var trimmed = name.Trim();
        var existing = await FindByNameAndRegionAsync(trimmed, regionId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var region = await _db.Regions
            .Include(r => r.Country)
            .FirstOrDefaultAsync(r => r.Id == regionId, ct)
            ?? throw new InvalidOperationException($"Region {regionId} could not be found when creating appellation '{name}'.");

        var entity = new Appellation
        {
            Id = Guid.NewGuid(),
            Name = trimmed,
            RegionId = regionId,
            Region = region
        };

        _db.Appellations.Add(entity);
        await _db.SaveChangesAsync(ct);

        return entity;
    }

    public async Task<bool> AnyForRegionAsync(Guid regionId, CancellationToken ct = default)
    {
        return await _db.Appellations.AnyAsync(a => a.RegionId == regionId, ct);
    }

    public async Task AddAsync(Appellation appellation, CancellationToken ct = default)
    {
        _db.Appellations.Add(appellation);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Appellation appellation, CancellationToken ct = default)
    {
        _db.Appellations.Update(appellation);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Appellations.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (entity is null)
        {
            return;
        }

        _db.Appellations.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }
}
