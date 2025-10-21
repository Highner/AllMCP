using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Repositories;

public interface ISipSessionRepository
{
    Task<SipSession?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<SipSession>> GetBySisterhoodAsync(Guid sisterhoodId, CancellationToken ct = default);
    Task<List<SipSession>> GetUpcomingAsync(DateTime utcNow, int limit, CancellationToken ct = default);
    Task<SipSession> AddAsync(SipSession sipSession, CancellationToken ct = default);
    Task UpdateAsync(SipSession sipSession, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<int> AddBottlesToSessionAsync(Guid sessionId, Guid ownerUserId, IReadOnlyCollection<Guid> bottleIds, CancellationToken ct = default);
}

public class SipSessionRepository : ISipSessionRepository
{
    private readonly ApplicationDbContext _db;

    public SipSessionRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<SipSession?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.SipSessions
            .AsNoTracking()
            .Include(session => session.Sisterhood)
            .Include(session => session.Bottles)
                .ThenInclude(bottle => bottle.WineVintage)
                    .ThenInclude(vintage => vintage.Wine)
            .FirstOrDefaultAsync(session => session.Id == id, ct);
    }

    public async Task<List<SipSession>> GetBySisterhoodAsync(Guid sisterhoodId, CancellationToken ct = default)
    {
        if (sisterhoodId == Guid.Empty)
        {
            return new List<SipSession>();
        }

        return await _db.SipSessions
            .AsNoTracking()
            .Where(session => session.SisterhoodId == sisterhoodId)
            .Include(session => session.Sisterhood)
            .Include(session => session.Bottles)
                .ThenInclude(bottle => bottle.WineVintage)
                    .ThenInclude(vintage => vintage.Wine)
            .OrderBy(session => session.ScheduledAt ?? session.CreatedAt)
            .ThenBy(session => session.Name)
            .ToListAsync(ct);
    }

    public async Task<List<SipSession>> GetUpcomingAsync(DateTime utcNow, int limit, CancellationToken ct = default)
    {
        if (limit < 0)
        {
            limit = 0;
        }

        var utcDate = utcNow.Date;

        IQueryable<SipSession> query = _db.SipSessions
            .AsNoTracking()
            .Include(session => session.Sisterhood)
            .Include(session => session.Bottles)
                .ThenInclude(bottle => bottle.WineVintage)
                    .ThenInclude(vintage => vintage.Wine)
            .Where(session =>
                (session.ScheduledAt.HasValue && session.ScheduledAt.Value >= utcNow) ||
                (!session.ScheduledAt.HasValue && session.Date.HasValue && session.Date.Value.Date >= utcDate))
            .OrderBy(session => session.ScheduledAt ?? session.Date ?? session.CreatedAt)
            .ThenBy(session => session.Name);

        if (limit > 0)
        {
            query = query.Take(limit);
        }

        return await query.ToListAsync(ct);
    }

    public async Task<SipSession> AddAsync(SipSession sipSession, CancellationToken ct = default)
    {
        if (sipSession is null)
        {
            throw new ArgumentNullException(nameof(sipSession));
        }

        if (sipSession.SisterhoodId == Guid.Empty)
        {
            throw new ArgumentException("Sip session must be linked to a sisterhood.", nameof(sipSession));
        }

        if (string.IsNullOrWhiteSpace(sipSession.Name))
        {
            throw new ArgumentException("Sip session name cannot be empty.", nameof(sipSession));
        }

        sipSession.Name = sipSession.Name.Trim();
        sipSession.Description = string.IsNullOrWhiteSpace(sipSession.Description)
            ? null
            : sipSession.Description.Trim();

        sipSession.Location = string.IsNullOrWhiteSpace(sipSession.Location)
            ? string.Empty
            : sipSession.Location.Trim();

        if (sipSession.Id == Guid.Empty)
        {
            sipSession.Id = Guid.NewGuid();
        }

        if (sipSession.CreatedAt == default)
        {
            sipSession.CreatedAt = DateTime.UtcNow;
        }

        sipSession.UpdatedAt = DateTime.UtcNow;

        _db.SipSessions.Add(sipSession);
        await _db.SaveChangesAsync(ct);

        return sipSession;
    }

    public async Task UpdateAsync(SipSession sipSession, CancellationToken ct = default)
    {
        if (sipSession is null)
        {
            throw new ArgumentNullException(nameof(sipSession));
        }

        if (sipSession.SisterhoodId == Guid.Empty)
        {
            throw new ArgumentException("Sip session must be linked to a sisterhood.", nameof(sipSession));
        }

        if (string.IsNullOrWhiteSpace(sipSession.Name))
        {
            throw new ArgumentException("Sip session name cannot be empty.", nameof(sipSession));
        }

        sipSession.Name = sipSession.Name.Trim();
        sipSession.Description = string.IsNullOrWhiteSpace(sipSession.Description)
            ? null
            : sipSession.Description.Trim();

        sipSession.Location = string.IsNullOrWhiteSpace(sipSession.Location)
            ? string.Empty
            : sipSession.Location.Trim();

        sipSession.UpdatedAt = DateTime.UtcNow;

        _db.SipSessions.Update(sipSession);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await _db.SipSessions.FirstOrDefaultAsync(session => session.Id == id, ct);
        if (existing is null)
        {
            return;
        }

        _db.SipSessions.Remove(existing);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<int> AddBottlesToSessionAsync(Guid sessionId, Guid ownerUserId, IReadOnlyCollection<Guid> bottleIds, CancellationToken ct = default)
    {
        if (sessionId == Guid.Empty || ownerUserId == Guid.Empty)
        {
            return 0;
        }

        if (bottleIds is null || bottleIds.Count == 0)
        {
            return 0;
        }

        var normalizedBottleIds = bottleIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (normalizedBottleIds.Count == 0)
        {
            return 0;
        }

        var session = await _db.SipSessions
            .Include(s => s.Bottles)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session is null)
        {
            return 0;
        }

        var candidateBottles = await _db.Bottles
            .Where(b => normalizedBottleIds.Contains(b.Id) && b.UserId == ownerUserId && !b.IsDrunk)
            .ToListAsync(ct);

        if (candidateBottles.Count == 0)
        {
            return 0;
        }

        var existingBottleIds = session.Bottles
            .Select(b => b.Id)
            .ToHashSet();

        var addedCount = 0;

        foreach (var bottle in candidateBottles)
        {
            if (existingBottleIds.Add(bottle.Id))
            {
                session.Bottles.Add(bottle);
                addedCount++;
            }
        }

        if (addedCount == 0)
        {
            return 0;
        }

        session.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return addedCount;
    }
}
