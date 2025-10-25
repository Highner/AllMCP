using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Repositories;

public interface IWishlistRepository
{
    Task<Wishlist?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Wishlist>> GetForUserAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(Wishlist wishlist, CancellationToken ct = default);
    Task UpdateAsync(Wishlist wishlist, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class WishlistRepository : IWishlistRepository
{
    private readonly ApplicationDbContext _dbContext;

    public WishlistRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Wishlist?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        return await BuildWishlistQuery()
            .FirstOrDefaultAsync(wishlist => wishlist.Id == id, ct);
    }

    public async Task<IReadOnlyList<Wishlist>> GetForUserAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            return Array.Empty<Wishlist>();
        }

        return await BuildWishlistQuery()
            .Where(wishlist => wishlist.UserId == userId)
            .OrderBy(wishlist => wishlist.Name)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Wishlist wishlist, CancellationToken ct = default)
    {
        _dbContext.Wishlists.Add(wishlist);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Wishlist wishlist, CancellationToken ct = default)
    {
        _dbContext.Wishlists.Update(wishlist);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty)
        {
            return;
        }

        var entity = await _dbContext.Wishlists
            .FirstOrDefaultAsync(wishlist => wishlist.Id == id, ct);

        if (entity is null)
        {
            return;
        }

        _dbContext.Wishlists.Remove(entity);
        await _dbContext.SaveChangesAsync(ct);
    }

    private IQueryable<Wishlist> BuildWishlistQuery()
    {
        return _dbContext.Wishlists
            .AsNoTracking()
            .Include(wishlist => wishlist.User)
            .Include(wishlist => wishlist.Wishes)
                .ThenInclude(wish => wish.WineVintage)
                    .ThenInclude(wineVintage => wineVintage.Wine)
                        .ThenInclude(wine => wine.SubAppellation)
                            .ThenInclude(subAppellation => subAppellation.Appellation)
                                .ThenInclude(appellation => appellation.Region)
                                    .ThenInclude(region => region.Country)
            .Include(wishlist => wishlist.Wishes)
                .ThenInclude(wish => wish.WineVintage)
                    .ThenInclude(wineVintage => wineVintage.EvolutionScores);
    }
}
