using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Repositories;

public interface IWineVintageUserDrinkingWindowRepository
{
    Task<WineVintageUserDrinkingWindow?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<WineVintageUserDrinkingWindow?> FindAsync(Guid userId, Guid wineVintageId, CancellationToken ct = default);
    Task<IReadOnlyList<WineVintageUserDrinkingWindow>> GetForUserAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(WineVintageUserDrinkingWindow drinkingWindow, CancellationToken ct = default);
    Task UpdateAsync(WineVintageUserDrinkingWindow drinkingWindow, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class WineVintageUserDrinkingWindowRepository : IWineVintageUserDrinkingWindowRepository
{
    private readonly ApplicationDbContext _dbContext;

    public WineVintageUserDrinkingWindowRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<WineVintageUserDrinkingWindow?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        return await BuildQuery()
            .FirstOrDefaultAsync(window => window.Id == id, ct);
    }

    public async Task<WineVintageUserDrinkingWindow?> FindAsync(Guid userId, Guid wineVintageId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty || wineVintageId == Guid.Empty)
        {
            return null;
        }

        return await BuildQuery()
            .FirstOrDefaultAsync(window => window.UserId == userId && window.WineVintageId == wineVintageId, ct);
    }

    public async Task<IReadOnlyList<WineVintageUserDrinkingWindow>> GetForUserAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            return Array.Empty<WineVintageUserDrinkingWindow>();
        }

        return await BuildQuery()
            .Where(window => window.UserId == userId)
            .OrderBy(window => window.WineVintage.Wine.Name)
            .ThenBy(window => window.WineVintage.Vintage)
            .ToListAsync(ct);
    }

    public async Task AddAsync(WineVintageUserDrinkingWindow drinkingWindow, CancellationToken ct = default)
    {
        _dbContext.WineVintageUserDrinkingWindows.Add(drinkingWindow);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(WineVintageUserDrinkingWindow drinkingWindow, CancellationToken ct = default)
    {
        _dbContext.WineVintageUserDrinkingWindows.Update(drinkingWindow);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty)
        {
            return;
        }

        var entity = await _dbContext.WineVintageUserDrinkingWindows
            .FirstOrDefaultAsync(window => window.Id == id, ct);

        if (entity is null)
        {
            return;
        }

        _dbContext.WineVintageUserDrinkingWindows.Remove(entity);
        await _dbContext.SaveChangesAsync(ct);
    }

    private IQueryable<WineVintageUserDrinkingWindow> BuildQuery()
    {
        return _dbContext.WineVintageUserDrinkingWindows
            .AsNoTracking()
            .Include(window => window.User)
            .Include(window => window.WineVintage)
                .ThenInclude(wineVintage => wineVintage.Wine)
                    .ThenInclude(wine => wine.SubAppellation)
                        .ThenInclude(subAppellation => subAppellation.Appellation)
                            .ThenInclude(appellation => appellation.Region)
                                .ThenInclude(region => region.Country)
            .Include(window => window.WineVintage)
                .ThenInclude(wineVintage => wineVintage.EvolutionScores);
    }
}
