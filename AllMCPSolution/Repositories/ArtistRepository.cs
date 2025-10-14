using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Data;
using AllMCPSolution.Models;

namespace AllMCPSolution.Repositories;

public interface IArtistRepository
{
    Task<List<Artist>> GetAllAsync(CancellationToken ct = default);
    Task<Artist?> FindByNameAsync(string firstName, string lastName, CancellationToken ct = default);
    Task AddAsync(Artist artist, CancellationToken ct = default);
}

public class ArtistRepository : IArtistRepository
{
    private readonly ApplicationDbContext _db;
    public ArtistRepository(ApplicationDbContext db) => _db = db;

    public Task<List<Artist>> GetAllAsync(CancellationToken ct = default)
        => _db.Artists.AsNoTracking().OrderBy(a => a.LastName).ThenBy(a => a.FirstName).ToListAsync(ct);

    public Task<Artist?> FindByNameAsync(string firstName, string lastName, CancellationToken ct = default)
        => _db.Artists.FirstOrDefaultAsync(a => a.FirstName.ToLower() == firstName.ToLower() && a.LastName.ToLower() == lastName.ToLower(), ct);

    public async Task AddAsync(Artist artist, CancellationToken ct = default)
    {
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync(ct);
    }
}
