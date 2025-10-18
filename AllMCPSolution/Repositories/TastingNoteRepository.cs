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
                        .ThenInclude(w => w.Appellation)
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
                        .ThenInclude(w => w.Appellation)
                            .ThenInclude(a => a.Region)
                                .ThenInclude(r => r.Country)
            .OrderBy(tn => tn.Id)
            .ToListAsync(ct);
    }

    public async Task AddAsync(TastingNote tastingNote, CancellationToken ct = default)
    {
        _db.TastingNotes.Add(tastingNote);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(TastingNote tastingNote, CancellationToken ct = default)
    {
        _db.TastingNotes.Update(tastingNote);
        await _db.SaveChangesAsync(ct);
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
}
