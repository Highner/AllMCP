using System.Data.Common;
using System.Text.Json;
using AllMCPSolution.Data;
using AllMCPSolution.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

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
    private static readonly Lazy<IReadOnlyList<ArtistSeed>> _fallbackArtists = new(LoadFallbackArtists);

    public ArtistRepository(ApplicationDbContext db) => _db = db;

    public Task<List<Artist>> GetAllAsync(CancellationToken ct = default)
        => ExecuteWithFallbackAsync(
            () => _db.Artists
                .AsNoTracking()
                .OrderBy(a => a.LastName)
                .ThenBy(a => a.FirstName)
                .ToListAsync(ct),
            () => CloneFallbackArtists().ToList());

    public Task<Artist?> FindByNameAsync(string firstName, string lastName, CancellationToken ct = default)
        => ExecuteWithFallbackAsync(
            () => _db.Artists.FirstOrDefaultAsync(
                a => a.FirstName.ToLower() == firstName.ToLower() && a.LastName.ToLower() == lastName.ToLower(), ct),
            () => CloneFallbackArtists()
                .FirstOrDefault(a =>
                    string.Equals(a.FirstName, firstName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(a.LastName, lastName, StringComparison.OrdinalIgnoreCase)));

    public async Task AddAsync(Artist artist, CancellationToken ct = default)
    {
        try
        {
            _db.Artists.Add(artist);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (IsDatabaseUnavailable(ex))
        {
            throw new InvalidOperationException("The database is not available; adding artists is disabled in offline mode.", ex);
        }
    }

    private static async Task<T> ExecuteWithFallbackAsync<T>(Func<Task<T>> operation, Func<T> fallback)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex) when (IsDatabaseUnavailable(ex))
        {
            return fallback();
        }
    }

    private static bool IsDatabaseUnavailable(Exception ex)
    {
        var seen = new HashSet<Exception>();
        var stack = new Stack<Exception>();
        if (ex != null) stack.Push(ex);

        const int maxNodes = 128;
        int visited = 0;

        while (stack.Count > 0 && visited < maxNodes)
        {
            var current = stack.Pop();
            visited++;

            if (!seen.Add(current))
                continue; // avoid cycles

            switch (current)
            {
                case SqlException:
                case DbException:
                case TimeoutException:
                case TaskCanceledException:
                case OperationCanceledException:
                    return true;
                case AggregateException agg:
                    foreach (var inner in agg.Flatten().InnerExceptions)
                    {
                        if (inner is not null) stack.Push(inner);
                    }
                    break;
            }

            if (current.InnerException is not null)
            {
                stack.Push(current.InnerException);
            }
        }

        return false;
    }

    private static IReadOnlyList<ArtistSeed> LoadFallbackArtists()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var path = Path.Combine(baseDir, "Data", "Seed", "artists.json");
            if (!File.Exists(path))
            {
                return Array.Empty<ArtistSeed>();
            }

            using var stream = File.OpenRead(path);
            var seeds = JsonSerializer.Deserialize<List<ArtistSeed>>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return seeds?.Where(s => s is not null).ToArray() ?? Array.Empty<ArtistSeed>();
        }
        catch
        {
            return Array.Empty<ArtistSeed>();
        }
    }

    private static IEnumerable<Artist> CloneFallbackArtists()
        => _fallbackArtists.Value.Select(seed => new Artist
        {
            Id = seed.Id,
            FirstName = seed.FirstName,
            LastName = seed.LastName,
            Artworks = new List<Artwork>()
        });

    private sealed record ArtistSeed(Guid Id, string FirstName, string LastName);
}
