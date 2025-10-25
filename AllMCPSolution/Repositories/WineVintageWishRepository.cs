using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Repositories;

public interface IWineVintageWishRepository
{
    Task<WineVintageWish?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<WineVintageWish?> FindAsync(Guid wishlistId, Guid wineVintageId, CancellationToken ct = default);
    Task<IReadOnlyList<WineVintageWish>> GetForWishlistAsync(Guid wishlistId, CancellationToken ct = default);
    Task AddAsync(WineVintageWish wish, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class WineVintageWishRepository : IWineVintageWishRepository
{
    private readonly ApplicationDbContext _dbContext;

    public WineVintageWishRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<WineVintageWish?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        return await BuildWishQuery()
            .FirstOrDefaultAsync(wish => wish.Id == id, ct);
    }

    public async Task<WineVintageWish?> FindAsync(Guid wishlistId, Guid wineVintageId, CancellationToken ct = default)
    {
        if (wishlistId == Guid.Empty || wineVintageId == Guid.Empty)
        {
            return null;
        }

        return await BuildWishQuery()
            .FirstOrDefaultAsync(wish => wish.WishlistId == wishlistId && wish.WineVintageId == wineVintageId, ct);
    }

    public async Task<IReadOnlyList<WineVintageWish>> GetForWishlistAsync(Guid wishlistId, CancellationToken ct = default)
    {
        if (wishlistId == Guid.Empty)
        {
            return Array.Empty<WineVintageWish>();
        }

        return await BuildWishQuery()
            .Where(wish => wish.WishlistId == wishlistId)
            .OrderBy(wish => wish.WineVintage.Wine.Name)
            .ThenBy(wish => wish.WineVintage.Vintage)
            .ToListAsync(ct);
    }

    public async Task AddAsync(WineVintageWish wish, CancellationToken ct = default)
    {
        _dbContext.WineVintageWishes.Add(wish);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty)
        {
            return;
        }

        var entity = await _dbContext.WineVintageWishes
            .FirstOrDefaultAsync(entry => entry.Id == id, ct);

        if (entity is null)
        {
            return;
        }

        _dbContext.WineVintageWishes.Remove(entity);
        await _dbContext.SaveChangesAsync(ct);
    }

    private IQueryable<WineVintageWish> BuildWishQuery()
    {
        return _dbContext.WineVintageWishes
            .AsNoTracking()
            .Include(wish => wish.Wishlist)
                .ThenInclude(wishlist => wishlist.User)
            .Include(wish => wish.WineVintage)
                .ThenInclude(wineVintage => wineVintage.Wine)
                    .ThenInclude(wine => wine.SubAppellation)
                        .ThenInclude(subAppellation => subAppellation.Appellation)
                            .ThenInclude(appellation => appellation.Region)
                                .ThenInclude(region => region.Country)
            .Include(wish => wish.WineVintage)
                .ThenInclude(wineVintage => wineVintage.EvolutionScores);
    }
}
