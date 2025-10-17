using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Data;
using AllMCPSolution.Models;

namespace AllMCPSolution.Repositories;

public interface ICountryRepository
{
    Task<List<Country>> GetAllAsync(CancellationToken ct = default);
    Task<Country?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Country country, CancellationToken ct = default);
    Task UpdateAsync(Country country, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public class CountryRepository : ICountryRepository
{
    private readonly ApplicationDbContext _db;
    public CountryRepository(ApplicationDbContext db) => _db = db;

    public async Task<List<Country>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.Countries
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
    }

    public async Task<Country?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Countries
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task AddAsync(Country country, CancellationToken ct = default)
    {
        _db.Countries.Add(country);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Country country, CancellationToken ct = default)
    {
        _db.Countries.Update(country);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Countries.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null)
        {
            return;
        }

        _db.Countries.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }
}
