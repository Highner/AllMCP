using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Data;
using AllMCPSolution.Models;

namespace AllMCPSolution.Repositories;

public interface ISisterhoodRepository
{
    Task<List<Sisterhood>> GetAllAsync(CancellationToken ct = default);
    Task<Sisterhood?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Sisterhood?> FindByNameAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<Sisterhood>> GetForUserAsync(Guid userId, CancellationToken ct = default);
    Task<Sisterhood> CreateWithAdminAsync(string name, string? description, Guid adminUserId, CancellationToken ct = default);
    Task UpdateAsync(Sisterhood sisterhood, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<bool> AddUserToSisterhoodAsync(Guid sisterhoodId, Guid userId, CancellationToken ct = default);
    Task<bool> RemoveUserFromSisterhoodAsync(Guid sisterhoodId, Guid userId, CancellationToken ct = default);
    Task<bool> IsAdminAsync(Guid sisterhoodId, Guid userId, CancellationToken ct = default);
    Task<SisterhoodMembership?> GetMembershipAsync(Guid sisterhoodId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<SisterhoodMembership>> GetMembershipsAsync(Guid sisterhoodId, CancellationToken ct = default);
    Task<bool> SetAdminStatusAsync(Guid sisterhoodId, Guid userId, bool isAdmin, CancellationToken ct = default);
}

public class SisterhoodRepository : ISisterhoodRepository
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public SisterhoodRepository(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task<List<Sisterhood>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.Sisterhoods
            .AsNoTracking()
            .Include(s => s.SipSessions)
            .Include(s => s.Memberships)
                .ThenInclude(membership => membership.User)
            .Include(s => s.Invitations)
                .ThenInclude(invitation => invitation.InviteeUser)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);
    }

    public async Task<Sisterhood?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Sisterhoods
            .AsNoTracking()
            .Include(s => s.SipSessions)
            .Include(s => s.Memberships)
                .ThenInclude(membership => membership.User)
            .Include(s => s.Invitations)
                .ThenInclude(invitation => invitation.InviteeUser)
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
            .Include(s => s.SipSessions)
            .Include(s => s.Memberships)
                .ThenInclude(membership => membership.User)
            .Include(s => s.Invitations)
                .ThenInclude(invitation => invitation.InviteeUser)
            .FirstOrDefaultAsync(s => s.Name == trimmedName, ct);
    }

    public async Task<IReadOnlyList<Sisterhood>> GetForUserAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            return Array.Empty<Sisterhood>();
        }

        return await _db.Sisterhoods
            .AsNoTracking()
            .Include(s => s.SipSessions)
            .Include(s => s.Memberships)
                .ThenInclude(membership => membership.User)
                    .ThenInclude(user => user.TastingNotes)
                        .ThenInclude(note => note.Bottle)
                            .ThenInclude(bottle => bottle.WineVintage)
                                .ThenInclude(vintage => vintage.Wine)
                                    .ThenInclude(wine => wine.SubAppellation)
                                        .ThenInclude(subAppellation => subAppellation.Appellation)
                                            .ThenInclude(appellation => appellation.Region)
                                                .ThenInclude(region => region.Country)
            .Include(s => s.Invitations)
                .ThenInclude(invitation => invitation.InviteeUser)
            .Where(s => s.Memberships.Any(membership => membership.UserId == userId))
            .OrderBy(s => s.Name)
            .ToListAsync(ct);
    }

    public async Task<Sisterhood> CreateWithAdminAsync(string name, string? description, Guid adminUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Sisterhood name cannot be empty.", nameof(name));
        }

        ct.ThrowIfCancellationRequested();

        var trimmedName = name.Trim();
        var trimmedDescription = string.IsNullOrWhiteSpace(description)
            ? null
            : description.Trim();

        var existing = await _db.Sisterhoods.AnyAsync(s => s.Name == trimmedName, ct);
        if (existing)
        {
            throw new InvalidOperationException($"A sisterhood named '{trimmedName}' already exists.");
        }

        var adminUser = await _userManager.FindByIdAsync(adminUserId.ToString());
        if (adminUser is null)
        {
            throw new InvalidOperationException("The specified admin user could not be found.");
        }

        var sisterhood = new Sisterhood
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            Description = trimmedDescription,
        };

        sisterhood.Memberships.Add(new SisterhoodMembership
        {
            SisterhoodId = sisterhood.Id,
            UserId = adminUser.Id,
            User = adminUser,
            IsAdmin = true,
            JoinedAt = DateTime.UtcNow,
        });

        _db.Sisterhoods.Add(sisterhood);
        await _db.SaveChangesAsync(ct);

        return sisterhood;
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

    public async Task<bool> AddUserToSisterhoodAsync(Guid sisterhoodId, Guid userId, CancellationToken ct = default)
    {
        var sisterhood = await _db.Sisterhoods
            .Include(s => s.Memberships)
            .FirstOrDefaultAsync(s => s.Id == sisterhoodId, ct);

        if (sisterhood is null)
        {
            return false;
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return false;
        }

        if (sisterhood.Memberships.Any(membership => membership.UserId == userId))
        {
            return false;
        }

        sisterhood.Memberships.Add(new SisterhoodMembership
        {
            SisterhoodId = sisterhood.Id,
            UserId = user.Id,
            User = user,
            JoinedAt = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RemoveUserFromSisterhoodAsync(Guid sisterhoodId, Guid userId, CancellationToken ct = default)
    {
        var membership = await _db.SisterhoodMemberships
            .FirstOrDefaultAsync(m => m.SisterhoodId == sisterhoodId && m.UserId == userId, ct);

        if (membership is null)
        {
            return false;
        }

        _db.SisterhoodMemberships.Remove(membership);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> IsAdminAsync(Guid sisterhoodId, Guid userId, CancellationToken ct = default)
    {
        return await _db.SisterhoodMemberships
            .AsNoTracking()
            .AnyAsync(m => m.SisterhoodId == sisterhoodId && m.UserId == userId && m.IsAdmin, ct);
    }

    public async Task<SisterhoodMembership?> GetMembershipAsync(Guid sisterhoodId, Guid userId, CancellationToken ct = default)
    {
        return await _db.SisterhoodMemberships
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.SisterhoodId == sisterhoodId && m.UserId == userId, ct);
    }

    public async Task<IReadOnlyList<SisterhoodMembership>> GetMembershipsAsync(Guid sisterhoodId, CancellationToken ct = default)
    {
        return await _db.SisterhoodMemberships
            .AsNoTracking()
            .Include(m => m.User)
            .Where(m => m.SisterhoodId == sisterhoodId)
            .OrderBy(m => m.User != null ? m.User.Name : string.Empty)
            .ToListAsync(ct);
    }

    public async Task<bool> SetAdminStatusAsync(Guid sisterhoodId, Guid userId, bool isAdmin, CancellationToken ct = default)
    {
        var membership = await _db.SisterhoodMemberships
            .FirstOrDefaultAsync(m => m.SisterhoodId == sisterhoodId && m.UserId == userId, ct);

        if (membership is null)
        {
            return false;
        }

        if (membership.IsAdmin == isAdmin)
        {
            return true;
        }

        membership.IsAdmin = isAdmin;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
