using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Data;
using AllMCPSolution.Models;
using AllMCPSolution.Utilities;

namespace AllMCPSolution.Repositories;

public interface IUserRepository
{
    Task<List<User>> GetAllAsync(CancellationToken ct = default);
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> FindByNameAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<User>> SearchByApproximateNameAsync(string name, int maxResults = 5, CancellationToken ct = default);
    Task<User> GetOrCreateAsync(string name, string tasteProfile, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    Task UpdateAsync(User user, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _db;

    public UserRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<User>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.DomainUsers
            .AsNoTracking()
            .OrderBy(u => u.Name)
            .ToListAsync(ct);
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.DomainUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<User?> FindByNameAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var normalized = name.Trim().ToLowerInvariant();
        return await _db.DomainUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Name.ToLower() == normalized, ct);
    }

    public async Task<IReadOnlyList<User>> SearchByApproximateNameAsync(string name, int maxResults = 5, CancellationToken ct = default)
    {
        var users = await _db.DomainUsers
            .AsNoTracking()
            .ToListAsync(ct);

        return FuzzyMatchUtilities.FindClosestMatches(users, name, u => u.Name, maxResults);
    }

    public async Task<User> GetOrCreateAsync(string name, string tasteProfile, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("User name cannot be empty.", nameof(name));
        }

        var trimmedName = name.Trim();
        var existing = await FindByNameAsync(trimmedName, ct);
        if (existing is not null)
        {
            return existing;
        }

        var entity = new User
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            TasteProfile = tasteProfile?.Trim() ?? string.Empty
        };

        _db.DomainUsers.Add(entity);
        await _db.SaveChangesAsync(ct);

        return entity;
    }

    public async Task AddAsync(User user, CancellationToken ct = default)
    {
        _db.DomainUsers.Add(user);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(User user, CancellationToken ct = default)
    {
        _db.DomainUsers.Update(user);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.DomainUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (entity is null)
        {
            return;
        }

        _db.DomainUsers.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }
}
