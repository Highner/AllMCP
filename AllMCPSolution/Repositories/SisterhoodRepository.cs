using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Data;
using AllMCPSolution.Models;

namespace AllMCPSolution.Repositories;

public interface ISisterhoodRepository
{
    Task<List<Sisterhood>> GetAllAsync(CancellationToken ct = default);
    Task<Sisterhood?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Sisterhood?> FindByNameAsync(string name, CancellationToken ct = default);
    Task AddAsync(Sisterhood sisterhood, CancellationToken ct = default);
    Task UpdateAsync(Sisterhood sisterhood, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task AddUserToSisterhoodAsync(Guid sisterhoodId, Guid userId, CancellationToken ct = default);
    Task RemoveUserFromSisterhoodAsync(Guid sisterhoodId, Guid userId, CancellationToken ct = default);
}

public class SisterhoodRepository : ISisterhoodRepository
{
    private readonly ApplicationDbContext _db;

    public SisterhoodRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<Sisterhood>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.Sisterhoods
            .AsNoTracking()
            .Include(s => s.Members)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);
    }

    public async Task<Sisterhood?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Sisterhoods
            .AsNoTracking()
            .Include(s => s.Members)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<Sisterhood?> FindByNameAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmedName = name.Trim();
        return await _db.Sisterhoods
            .AsNoTracking()
            .Include(s => s.Members)
            .FirstOrDefaultAsync(s => s.Name == trimmedName, ct);
    }

    public async Task AddAsync(Sisterhood sisterhood, CancellationToken ct = default)
    {
        if (sisterhood is null)
        {
            throw new ArgumentNullException(nameof(sisterhood));
        }

        sisterhood.Name = sisterhood.Name?.Trim() ?? string.Empty;
        sisterhood.Description = string.IsNullOrWhiteSpace(sisterhood.Description)
            ? null
            : sisterhood.Description.Trim();

        _db.Sisterhoods.Add(sisterhood);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Sisterhood sisterhood, CancellationToken ct = default)
    {
        if (sisterhood is null)
        {
            throw new ArgumentNullException(nameof(sisterhood));
        }

        sisterhood.Name = sisterhood.Name?.Trim() ?? string.Empty;
        sisterhood.Description = string.IsNullOrWhiteSpace(sisterhood.Description)
            ? null
            : sisterhood.Description.Trim();

        _db.Sisterhoods.Update(sisterhood);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Sisterhoods.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (entity is null)
        {
            return;
        }

        _db.Sisterhoods.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }

    public async Task AddUserToSisterhoodAsync(Guid sisterhoodId, Guid userId, CancellationToken ct = default)
    {
        var sisterhood = await _db.Sisterhoods
            .Include(s => s.Members)
            .FirstOrDefaultAsync(s => s.Id == sisterhoodId, ct);
        if (sisterhood is null)
        {
            return;
        }

        var user = await _db.DomainUsers.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return;
        }

        if (sisterhood.Members.All(u => u.Id != userId))
        {
            sisterhood.Members.Add(user);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task RemoveUserFromSisterhoodAsync(Guid sisterhoodId, Guid userId, CancellationToken ct = default)
    {
        var sisterhood = await _db.Sisterhoods
            .Include(s => s.Members)
            .FirstOrDefaultAsync(s => s.Id == sisterhoodId, ct);
        if (sisterhood is null)
        {
            return;
        }

        var member = sisterhood.Members.FirstOrDefault(u => u.Id == userId);
        if (member is null)
        {
            return;
        }

        sisterhood.Members.Remove(member);
        await _db.SaveChangesAsync(ct);
    }
}
