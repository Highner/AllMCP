using AllMCPSolution.Data;
using AllMCPSolution.Models;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Repositories;

public interface IInflationIndexRepository
{
    Task<InflationIndex?> GetLatestAsync(CancellationToken ct = default);
    Task<DateTime?> GetLatestFinishedMonthAsync(CancellationToken ct = default);
    Task<IReadOnlyList<InflationIndex>> GetAllAsync(CancellationToken ct = default);
    Task UpsertRangeAsync(IEnumerable<InflationIndex> items, CancellationToken ct = default);
}

public class InflationIndexRepository : IInflationIndexRepository
{
    private readonly ApplicationDbContext _db;
    public InflationIndexRepository(ApplicationDbContext db) => _db = db;

    public async Task<InflationIndex?> GetLatestAsync(CancellationToken ct = default)
    {
        return await _db.InflationIndices
            .AsNoTracking()
            .OrderByDescending(i => i.Year)
            .ThenByDescending(i => i.Month)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<DateTime?> GetLatestFinishedMonthAsync(CancellationToken ct = default)
    {
        var latest = await GetLatestAsync(ct);
        return latest == null ? null : new DateTime(latest.Year, latest.Month, 1);
    }

    public async Task<IReadOnlyList<InflationIndex>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.InflationIndices
            .AsNoTracking()
            .OrderBy(i => i.Year)
            .ThenBy(i => i.Month)
            .ToListAsync(ct);
    }

    public async Task UpsertRangeAsync(IEnumerable<InflationIndex> items, CancellationToken ct = default)
    {
        foreach (var item in items)
        {
            var existing = await _db.InflationIndices
                .FirstOrDefaultAsync(i => i.Year == item.Year && i.Month == item.Month, ct);
            if (existing == null)
            {
                _db.InflationIndices.Add(item);
            }
            else
            {
                existing.IndexValue = item.IndexValue;
                _db.InflationIndices.Update(existing);
            }
        }
        await _db.SaveChangesAsync(ct);
    }
}