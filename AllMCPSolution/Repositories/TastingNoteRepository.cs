using System;
using System.Collections.Generic;
using System.Linq;
using AllMCPSolution.Data;
using AllMCPSolution.Models;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Repositories;

public interface ITastingNoteRepository
{
    Task<TastingNote?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<TastingNote>> GetByBottleIdAsync(Guid bottleId, CancellationToken ct = default);
    Task AddAsync(TastingNote tastingNote, CancellationToken ct = default);
    Task UpdateAsync(TastingNote tastingNote, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, decimal>> GetAverageScoresForWineVintagesByUsersAsync(
        IEnumerable<Guid> wineVintageIds,
        IEnumerable<Guid> userIds,
        CancellationToken ct = default);
}

public class TastingNoteRepository : ITastingNoteRepository
{
    private readonly ApplicationDbContext _db;

    public TastingNoteRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<TastingNote?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.TastingNotes
            .AsNoTracking()
            .Include(tn => tn.User)
            .Include(tn => tn.Bottle)
                .ThenInclude(b => b.WineVintage)
                    .ThenInclude(wv => wv.Wine)
                        .ThenInclude(w => w.SubAppellation)
                            .ThenInclude(sa => sa.Appellation)
                                .ThenInclude(a => a.Region)
                                    .ThenInclude(r => r.Country)
            .FirstOrDefaultAsync(tn => tn.Id == id, ct);
    }

    public async Task<List<TastingNote>> GetByBottleIdAsync(Guid bottleId, CancellationToken ct = default)
    {
        return await _db.TastingNotes
            .AsNoTracking()
            .Where(tn => tn.BottleId == bottleId)
            .Include(tn => tn.User)
            .Include(tn => tn.Bottle)
                .ThenInclude(b => b.WineVintage)
                    .ThenInclude(wv => wv.Wine)
                        .ThenInclude(w => w.SubAppellation)
                            .ThenInclude(sa => sa.Appellation)
                                .ThenInclude(a => a.Region)
                                    .ThenInclude(r => r.Country)
            .OrderBy(tn => tn.Id)
            .ToListAsync(ct);
    }

    public async Task AddAsync(TastingNote tastingNote, CancellationToken ct = default)
    {
        if (tastingNote is null) throw new ArgumentNullException(nameof(tastingNote));
        // Normalize when not tasted: clear note and score
        if (tastingNote.NotTasted)
        {
            tastingNote.Note = string.Empty;
            tastingNote.Score = null;
        }
        _db.TastingNotes.Add(tastingNote);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(TastingNote tastingNote, CancellationToken ct = default)
    {
        if (tastingNote is null)
        {
            throw new ArgumentNullException(nameof(tastingNote));
        }

        if (tastingNote.Id == Guid.Empty)
        {
            throw new ArgumentException("Tasting note ID must be provided.", nameof(tastingNote));
        }

        var isNotTasted = tastingNote.NotTasted;
        var trimmedNote = isNotTasted ? string.Empty : (tastingNote.Note?.Trim() ?? string.Empty);
        var scoreValue = isNotTasted ? null : tastingNote.Score;

        var affected = await _db.TastingNotes
            .Where(tn => tn.Id == tastingNote.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(tn => tn.Note, trimmedNote)
                    .SetProperty(tn => tn.Score, scoreValue),
                ct);

        if (affected == 0)
        {
            throw new InvalidOperationException($"Tasting note '{tastingNote.Id}' was not found.");
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await _db.TastingNotes.FirstOrDefaultAsync(tn => tn.Id == id, ct);
        if (existing is null)
        {
            return;
        }

        _db.TastingNotes.Remove(existing);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetAverageScoresForWineVintagesByUsersAsync(
        IEnumerable<Guid> wineVintageIds,
        IEnumerable<Guid> userIds,
        CancellationToken ct = default)
    {
        if (wineVintageIds is null || userIds is null)
        {
            return new Dictionary<Guid, decimal>();
        }

        var vintageIds = wineVintageIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var memberUserIds = userIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (vintageIds.Count == 0 || memberUserIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var averages = await _db.TastingNotes
            .AsNoTracking()
            .Where(note =>
                note.Score.HasValue &&
                memberUserIds.Contains(note.UserId) &&
                vintageIds.Contains(note.Bottle.WineVintageId))
            .GroupBy(note => note.Bottle.WineVintageId)
            .Select(group => new
            {
                WineVintageId = group.Key,
                AverageScore = group.Average(note => note.Score!.Value)
            })
            .ToListAsync(ct);

        if (averages.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        return averages.ToDictionary(
            entry => entry.WineVintageId,
            entry => Math.Round(entry.AverageScore, 1, MidpointRounding.AwayFromZero));
    }
}
