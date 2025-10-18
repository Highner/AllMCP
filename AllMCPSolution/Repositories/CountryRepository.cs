using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Data;
using AllMCPSolution.Models;
using AllMCPSolution.Utilities;

namespace AllMCPSolution.Repositories;

public interface ICountryRepository
{
    Task<List<Country>> GetAllAsync(CancellationToken ct = default);
    Task<Country?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Country?> FindByNameAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<Country>> SearchByApproximateNameAsync(string name, int maxResults = 5, CancellationToken ct = default);
    Task<Country> GetOrCreateAsync(string name, CancellationToken ct = default);
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

    public async Task<Country?> FindByNameAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var normalized = name.Trim().ToLowerInvariant();
        return await _db.Countries
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Name.ToLower() == normalized, ct);
    }

    public async Task<IReadOnlyList<Country>> SearchByApproximateNameAsync(string name, int maxResults = 5, CancellationToken ct = default)
    {
        var countries = await _db.Countries
            .AsNoTracking()
            .ToListAsync(ct);

        return FuzzyMatchUtilities.FindClosestMatches(countries, name, c => c.Name, maxResults);
    }

    public async Task<Country> GetOrCreateAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Country name cannot be empty.", nameof(name));
        }

        var trimmed = name.Trim();
        var existing = await FindByNameAsync(trimmed, ct);
        if (existing is not null)
        {
            return existing;
        }

        var entity = new Country
        {
            Id = Guid.NewGuid(),
            Name = trimmed
        };

        _db.Countries.Add(entity);
        await _db.SaveChangesAsync(ct);

        return entity;
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
