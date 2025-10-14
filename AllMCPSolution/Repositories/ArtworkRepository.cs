using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Data;
using AllMCPSolution.Models;

namespace AllMCPSolution.Repositories;

public interface IArtworkRepository
{
    Task AddAsync(Artwork artwork, CancellationToken ct = default);
}

public class ArtworkRepository : IArtworkRepository
{
    private readonly ApplicationDbContext _db;
    public ArtworkRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(Artwork artwork, CancellationToken ct = default)
    {
        _db.Artworks.Add(artwork);
        await _db.SaveChangesAsync(ct);
    }
}
