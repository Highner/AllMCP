using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Data;
using AllMCPSolution.Models;
using AllMCPSolution.Utilities;

namespace AllMCPSolution.Repositories;

public interface ISubAppellationRepository
{
    Task<IReadOnlyList<SubAppellation>> GetAllAsync(CancellationToken ct = default);
    Task<SubAppellation?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<SubAppellation?> FindByNameAndAppellationAsync(string name, Guid appellationId, CancellationToken ct = default);
    Task<IReadOnlyList<SubAppellation>> SearchByApproximateNameAsync(string name, Guid appellationId, int maxResults = 5, CancellationToken ct = default);
    Task<SubAppellation> GetOrCreateAsync(string name, Guid appellationId, CancellationToken ct = default);
    Task<SubAppellation> GetOrCreateBlankAsync(Guid appellationId, CancellationToken ct = default);
}

public sealed class SubAppellationRepository : ISubAppellationRepository
{
    private readonly ApplicationDbContext _db;

    public SubAppellationRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<SubAppellation>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.SubAppellations
            .AsNoTracking()
            .Include(sa => sa.Appellation)
                .ThenInclude(a => a.Region)
                    .ThenInclude(r => r.Country)
            .OrderBy(sa => sa.Appellation != null && sa.Appellation.Region != null ? sa.Appellation.Region.Name : string.Empty)
            .ThenBy(sa => sa.Appellation != null ? sa.Appellation.Name : string.Empty)
            .ThenBy(sa => sa.Name)
            .ToListAsync(ct);
    }

    public async Task<SubAppellation?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.SubAppellations
            .AsNoTracking()
            .Include(sa => sa.Appellation)
                .ThenInclude(a => a.Region)
                    .ThenInclude(r => r.Country)
            .FirstOrDefaultAsync(sa => sa.Id == id, ct);
    }

    public async Task<SubAppellation?> FindByNameAndAppellationAsync(string name, Guid appellationId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return await FindBlankAsync(appellationId, ct);
        }

        var trimmed = name.Trim();
        var localMatch = _db.SubAppellations.Local.FirstOrDefault(
            sa => sa.AppellationId == appellationId && string.Equals(sa.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (localMatch is not null)
        {
            return localMatch;
        }

        var normalized = trimmed.ToLowerInvariant();
        return await _db.SubAppellations
            .Include(sa => sa.Appellation)
                .ThenInclude(a => a.Region)
                    .ThenInclude(r => r.Country)
            .FirstOrDefaultAsync(
                sa => sa.AppellationId == appellationId && sa.Name.ToLower() == normalized,
                ct);
    }

    public async Task<IReadOnlyList<SubAppellation>> SearchByApproximateNameAsync(string name, Guid appellationId, int maxResults = 5, CancellationToken ct = default)
    {
        var subAppellations = await _db.SubAppellations
            .AsNoTracking()
            .Where(sa => sa.AppellationId == appellationId)
            .Include(sa => sa.Appellation)
                .ThenInclude(a => a.Region)
                    .ThenInclude(r => r.Country)
            .ToListAsync(ct);

        return FuzzyMatchUtilities.FindClosestMatches(subAppellations, name, sa => sa.Name, maxResults);
    }

    public async Task<SubAppellation> GetOrCreateAsync(string name, Guid appellationId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return await GetOrCreateBlankAsync(appellationId, ct);
        }

        var trimmed = name.Trim();
        var existing = await FindByNameAndAppellationAsync(trimmed, appellationId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var appellation = await _db.Appellations
            .Include(a => a.Region)
                .ThenInclude(r => r.Country)
            .FirstOrDefaultAsync(a => a.Id == appellationId, ct)
            ?? throw new InvalidOperationException($"Appellation {appellationId} could not be found when creating sub-appellation '{name}'.");

        var entity = new SubAppellation
        {
            Id = Guid.NewGuid(),
            Name = trimmed,
            AppellationId = appellationId,
            Appellation = appellation
        };

        _db.SubAppellations.Add(entity);
        await _db.SaveChangesAsync(ct);

        return entity;
    }

    public async Task<SubAppellation> GetOrCreateBlankAsync(Guid appellationId, CancellationToken ct = default)
    {
        var existing = await FindBlankAsync(appellationId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var appellation = await _db.Appellations
            .Include(a => a.Region)
                .ThenInclude(r => r.Country)
            .FirstOrDefaultAsync(a => a.Id == appellationId, ct)
            ?? throw new InvalidOperationException($"Appellation {appellationId} could not be found when creating a blank sub-appellation.");

        var entity = new SubAppellation
        {
            Id = Guid.NewGuid(),
            Name = null,
            AppellationId = appellationId,
            Appellation = appellation
        };

        _db.SubAppellations.Add(entity);
        await _db.SaveChangesAsync(ct);

        return entity;
    }

    private async Task<SubAppellation?> FindBlankAsync(Guid appellationId, CancellationToken ct)
    {
        var localMatch = _db.SubAppellations.Local.FirstOrDefault(
            sa => sa.AppellationId == appellationId && string.IsNullOrWhiteSpace(sa.Name));
        if (localMatch is not null)
        {
            return localMatch;
        }

        return await _db.SubAppellations
            .Include(sa => sa.Appellation)
                .ThenInclude(a => a.Region)
                    .ThenInclude(r => r.Country)
            .FirstOrDefaultAsync(
                sa => sa.AppellationId == appellationId && (sa.Name == null || sa.Name == string.Empty),
                ct);
    }
}
