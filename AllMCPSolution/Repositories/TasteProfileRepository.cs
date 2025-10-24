using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Repositories;

public interface ITasteProfileRepository
{
    Task<IReadOnlyList<TasteProfile>> GetForUserAsync(Guid userId, CancellationToken ct = default);
}

public sealed class TasteProfileRepository : ITasteProfileRepository
{
    private readonly ApplicationDbContext _dbContext;

    public TasteProfileRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<TasteProfile>> GetForUserAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            return Array.Empty<TasteProfile>();
        }

        return await _dbContext.TasteProfiles
            .AsNoTracking()
            .Where(profile => profile.UserId == userId)
            .OrderByDescending(profile => profile.CreatedAt)
            .ToListAsync(ct);
    }
}
