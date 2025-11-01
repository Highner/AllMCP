using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Repositories;

public interface IBottleShareRepository
{
    Task<BottleShare?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<BottleShare?> GetShareAsync(Guid bottleId, Guid sharedWithUserId, CancellationToken ct = default);
    Task<IReadOnlyList<BottleShare>> GetSharesForBottleAsync(Guid bottleId, CancellationToken ct = default);
    Task<IReadOnlyList<BottleShare>> GetSharesGrantedByUserAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<BottleShare>> GetSharesForRecipientAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(BottleShare share, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task DeleteAsync(Guid bottleId, Guid sharedWithUserId, CancellationToken ct = default);
    Task<int> DeleteForBottleOwnerAsync(Guid bottleId, Guid ownerUserId, CancellationToken ct = default);
}

public sealed class BottleShareRepository : IBottleShareRepository
{
    private readonly ApplicationDbContext _dbContext;

    public BottleShareRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<BottleShare?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        return await BuildShareQuery()
            .FirstOrDefaultAsync(share => share.Id == id, ct);
    }

    public async Task<BottleShare?> GetShareAsync(Guid bottleId, Guid sharedWithUserId, CancellationToken ct = default)
    {
        if (bottleId == Guid.Empty || sharedWithUserId == Guid.Empty)
        {
            return null;
        }

        return await BuildShareQuery()
            .FirstOrDefaultAsync(share => share.BottleId == bottleId && share.SharedWithUserId == sharedWithUserId, ct);
    }

    public async Task<IReadOnlyList<BottleShare>> GetSharesForBottleAsync(Guid bottleId, CancellationToken ct = default)
    {
        if (bottleId == Guid.Empty)
        {
            return Array.Empty<BottleShare>();
        }

        return await BuildShareQuery()
            .Where(share => share.BottleId == bottleId)
            .OrderBy(share => share.SharedWithUser.Name)
            .ThenBy(share => share.SharedWithUser.Email)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<BottleShare>> GetSharesGrantedByUserAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            return Array.Empty<BottleShare>();
        }

        return await BuildShareQuery()
            .Where(share => share.SharedByUserId == userId)
            .OrderBy(share => share.Bottle.WineVintage.Wine.Name)
            .ThenBy(share => share.Bottle.WineVintage.Vintage)
            .ThenBy(share => share.SharedWithUser.Name)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<BottleShare>> GetSharesForRecipientAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            return Array.Empty<BottleShare>();
        }

        return await BuildShareQuery()
            .Where(share => share.SharedWithUserId == userId)
            .OrderBy(share => share.Bottle.WineVintage.Wine.Name)
            .ThenBy(share => share.Bottle.WineVintage.Vintage)
            .ToListAsync(ct);
    }

    public async Task AddAsync(BottleShare share, CancellationToken ct = default)
    {
        _dbContext.BottleShares.Add(share);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty)
        {
            return;
        }

        var entity = await _dbContext.BottleShares
            .FirstOrDefaultAsync(share => share.Id == id, ct);

        if (entity is null)
        {
            return;
        }

        _dbContext.BottleShares.Remove(entity);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid bottleId, Guid sharedWithUserId, CancellationToken ct = default)
    {
        if (bottleId == Guid.Empty || sharedWithUserId == Guid.Empty)
        {
            return;
        }

        var entity = await _dbContext.BottleShares
            .FirstOrDefaultAsync(share => share.BottleId == bottleId && share.SharedWithUserId == sharedWithUserId, ct);

        if (entity is null)
        {
            return;
        }

        _dbContext.BottleShares.Remove(entity);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<int> DeleteForBottleOwnerAsync(Guid bottleId, Guid ownerUserId, CancellationToken ct = default)
    {
        if (bottleId == Guid.Empty || ownerUserId == Guid.Empty)
        {
            return 0;
        }

        var entities = await _dbContext.BottleShares
            .Where(share => share.BottleId == bottleId && share.SharedByUserId == ownerUserId)
            .ToListAsync(ct);

        if (entities.Count == 0)
        {
            return 0;
        }

        _dbContext.BottleShares.RemoveRange(entities);
        await _dbContext.SaveChangesAsync(ct);
        return entities.Count;
    }

    private IQueryable<BottleShare> BuildShareQuery()
    {
        return _dbContext.BottleShares
            .AsNoTracking()
            .Include(share => share.Bottle)
                .ThenInclude(bottle => bottle.WineVintage)
                    .ThenInclude(wineVintage => wineVintage.Wine)
            .Include(share => share.Bottle)
                .ThenInclude(bottle => bottle.WineVintage)
                    .ThenInclude(wineVintage => wineVintage.EvolutionScores)
            .Include(share => share.Bottle)
                .ThenInclude(bottle => bottle.BottleLocation)
            .Include(share => share.Bottle)
                .ThenInclude(bottle => bottle.User)
            .Include(share => share.Bottle)
                .ThenInclude(bottle => bottle.SipSessions)
                    .ThenInclude(sessionBottle => sessionBottle.SipSession)
            .Include(share => share.SharedByUser)
            .Include(share => share.SharedWithUser);
    }
}
