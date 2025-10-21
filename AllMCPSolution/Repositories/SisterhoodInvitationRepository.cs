using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Data;
using AllMCPSolution.Models;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Repositories;

public interface ISisterhoodInvitationRepository
{
    Task<SisterhoodInvitation?> GetAsync(Guid sisterhoodId, string email, CancellationToken ct = default);
    Task<IReadOnlyList<SisterhoodInvitation>> GetPendingForSisterhoodAsync(Guid sisterhoodId, CancellationToken ct = default);
    Task<SisterhoodInvitation> CreateOrUpdatePendingAsync(Guid sisterhoodId, string email, Guid? inviteeUserId, CancellationToken ct = default);
    Task<SisterhoodInvitation?> UpdateStatusAsync(Guid sisterhoodId, string email, SisterhoodInvitationStatus status, Guid? inviteeUserId = null, CancellationToken ct = default);
    Task<IReadOnlyList<SisterhoodInvitation>> GetForInviteeAsync(Guid? inviteeUserId, string? inviteeEmail, CancellationToken ct = default);
    Task<IReadOnlyList<SisterhoodInvitation>> GetAcceptedForAdminAsync(Guid adminUserId, DateTime sinceUtc, CancellationToken ct = default);
    Task<SisterhoodInvitation?> GetByIdAsync(Guid invitationId, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid invitationId, CancellationToken ct = default);
}

public class SisterhoodInvitationRepository : ISisterhoodInvitationRepository
{
    private readonly ApplicationDbContext _db;

    public SisterhoodInvitationRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<SisterhoodInvitation?> GetAsync(Guid sisterhoodId, string email, CancellationToken ct = default)
    {
        if (sisterhoodId == Guid.Empty)
        {
            throw new ArgumentException("Sisterhood id cannot be empty.", nameof(sisterhoodId));
        }

        var normalizedEmail = NormalizeEmail(email);

        return await _db.SisterhoodInvitations
            .AsNoTracking()
            .Include(invite => invite.InviteeUser)
            .FirstOrDefaultAsync(invite => invite.SisterhoodId == sisterhoodId && invite.InviteeEmail == normalizedEmail, ct);
    }

    public async Task<IReadOnlyList<SisterhoodInvitation>> GetPendingForSisterhoodAsync(Guid sisterhoodId, CancellationToken ct = default)
    {
        if (sisterhoodId == Guid.Empty)
        {
            return Array.Empty<SisterhoodInvitation>();
        }

        return await _db.SisterhoodInvitations
            .AsNoTracking()
            .Include(invite => invite.InviteeUser)
            .Where(invite => invite.SisterhoodId == sisterhoodId && invite.Status == SisterhoodInvitationStatus.Pending)
            .OrderBy(invite => invite.InviteeEmail)
            .ToListAsync(ct);
    }

    public async Task<SisterhoodInvitation> CreateOrUpdatePendingAsync(Guid sisterhoodId, string email, Guid? inviteeUserId, CancellationToken ct = default)
    {
        if (sisterhoodId == Guid.Empty)
        {
            throw new ArgumentException("Sisterhood id cannot be empty.", nameof(sisterhoodId));
        }

        var normalizedEmail = NormalizeEmail(email);
        var now = DateTime.UtcNow;

        var invitation = await _db.SisterhoodInvitations
            .FirstOrDefaultAsync(invite => invite.SisterhoodId == sisterhoodId && invite.InviteeEmail == normalizedEmail, ct);

        if (invitation is null)
        {
            invitation = new SisterhoodInvitation
            {
                Id = Guid.NewGuid(),
                SisterhoodId = sisterhoodId,
                InviteeEmail = normalizedEmail,
                InviteeUserId = inviteeUserId,
                Status = SisterhoodInvitationStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now,
            };

            await _db.SisterhoodInvitations.AddAsync(invitation, ct);
        }
        else
        {
            invitation.InviteeEmail = normalizedEmail;
            if (inviteeUserId.HasValue)
            {
                invitation.InviteeUserId = inviteeUserId;
            }

            invitation.Status = SisterhoodInvitationStatus.Pending;
            invitation.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);

        return invitation;
    }

    public async Task<SisterhoodInvitation?> UpdateStatusAsync(Guid sisterhoodId, string email, SisterhoodInvitationStatus status, Guid? inviteeUserId = null, CancellationToken ct = default)
    {
        if (sisterhoodId == Guid.Empty)
        {
            throw new ArgumentException("Sisterhood id cannot be empty.", nameof(sisterhoodId));
        }

        var normalizedEmail = NormalizeEmail(email);

        var invitation = await _db.SisterhoodInvitations
            .FirstOrDefaultAsync(invite => invite.SisterhoodId == sisterhoodId && invite.InviteeEmail == normalizedEmail, ct);

        if (invitation is null)
        {
            return null;
        }

        invitation.Status = status;
        if (inviteeUserId.HasValue)
        {
            invitation.InviteeUserId = inviteeUserId;
        }

        invitation.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return invitation;
    }

    public async Task<IReadOnlyList<SisterhoodInvitation>> GetForInviteeAsync(Guid? inviteeUserId, string? inviteeEmail, CancellationToken ct = default)
    {
        var hasUserId = inviteeUserId.HasValue && inviteeUserId.Value != Guid.Empty;
        var normalizedEmail = string.IsNullOrWhiteSpace(inviteeEmail) ? null : NormalizeEmail(inviteeEmail);

        if (!hasUserId && string.IsNullOrEmpty(normalizedEmail))
        {
            return Array.Empty<SisterhoodInvitation>();
        }

        var query = _db.SisterhoodInvitations
            .AsNoTracking()
            .Include(invite => invite.Sisterhood)
            .Include(invite => invite.InviteeUser)
            .Where(invite => invite.Status != SisterhoodInvitationStatus.Revoked);

        if (hasUserId && !string.IsNullOrEmpty(normalizedEmail))
        {
            query = query.Where(invite => invite.InviteeUserId == inviteeUserId || invite.InviteeEmail == normalizedEmail);
        }
        else if (hasUserId)
        {
            query = query.Where(invite => invite.InviteeUserId == inviteeUserId);
        }
        else if (!string.IsNullOrEmpty(normalizedEmail))
        {
            query = query.Where(invite => invite.InviteeEmail == normalizedEmail);
        }

        return await query
            .OrderBy(invite => invite.Status)
            .ThenBy(invite => invite.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SisterhoodInvitation>> GetAcceptedForAdminAsync(Guid adminUserId, DateTime sinceUtc, CancellationToken ct = default)
    {
        if (adminUserId == Guid.Empty)
        {
            return Array.Empty<SisterhoodInvitation>();
        }

        var normalizedSinceUtc = sinceUtc.Kind switch
        {
            DateTimeKind.Utc => sinceUtc,
            DateTimeKind.Local => sinceUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc),
        };

        return await _db.SisterhoodInvitations
            .AsNoTracking()
            .Include(invite => invite.Sisterhood)
                .ThenInclude(sisterhood => sisterhood.Memberships)
            .Include(invite => invite.InviteeUser)
            .Where(invite => invite.Status == SisterhoodInvitationStatus.Accepted
                && invite.UpdatedAt >= normalizedSinceUtc
                && invite.Sisterhood != null
                && invite.Sisterhood.Memberships.Any(membership => membership.UserId == adminUserId && membership.IsAdmin))
            .OrderByDescending(invite => invite.UpdatedAt)
            .ToListAsync(ct);
    }

    public async Task<SisterhoodInvitation?> GetByIdAsync(Guid invitationId, CancellationToken ct = default)
    {
        if (invitationId == Guid.Empty)
        {
            return null;
        }

        return await _db.SisterhoodInvitations
            .AsNoTracking()
            .Include(invite => invite.Sisterhood)
            .Include(invite => invite.InviteeUser)
            .FirstOrDefaultAsync(invite => invite.Id == invitationId, ct);
    }

    public async Task<bool> DeleteAsync(Guid invitationId, CancellationToken ct = default)
    {
        if (invitationId == Guid.Empty)
        {
            return false;
        }

        var invitation = await _db.SisterhoodInvitations
            .FirstOrDefaultAsync(invite => invite.Id == invitationId, ct);

        if (invitation is null)
        {
            return false;
        }

        _db.SisterhoodInvitations.Remove(invitation);
        await _db.SaveChangesAsync(ct);

        return true;
    }

    private static string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email cannot be empty.", nameof(email));
        }

        return email.Trim().ToLowerInvariant();
    }
}
