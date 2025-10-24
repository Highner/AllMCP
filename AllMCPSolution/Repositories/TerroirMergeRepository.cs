using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Data;
using AllMCPSolution.Models;

namespace AllMCPSolution.Repositories;

public interface ITerroirMergeRepository
{
    Task<TerroirMergeResult> MergeCountriesAsync(Guid leaderId, IReadOnlyCollection<Guid> followerIds, CancellationToken ct = default);
    Task<TerroirMergeResult> MergeRegionsAsync(Guid leaderId, IReadOnlyCollection<Guid> followerIds, CancellationToken ct = default);
    Task<TerroirMergeResult> MergeAppellationsAsync(Guid leaderId, IReadOnlyCollection<Guid> followerIds, CancellationToken ct = default);
    Task<TerroirMergeResult> MergeSubAppellationsAsync(Guid leaderId, IReadOnlyCollection<Guid> followerIds, CancellationToken ct = default);
    Task<TerroirMergeResult> MergeWinesAsync(Guid leaderId, IReadOnlyCollection<Guid> followerIds, CancellationToken ct = default);
}

public sealed class TerroirMergeRepository : ITerroirMergeRepository
{
    private readonly ApplicationDbContext _db;
    private const string NullNameKey = "\u0001";

    public TerroirMergeRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<TerroirMergeResult> MergeCountriesAsync(Guid leaderId, IReadOnlyCollection<Guid> followerIds, CancellationToken ct = default)
    {
        var normalizedFollowers = NormalizeFollowerIds(leaderId, followerIds);
        if (normalizedFollowers.Count == 0)
        {
            throw new InvalidOperationException("Select at least two countries to merge.");
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var leader = await _db.Countries
                .Include(c => c.Regions)
                .FirstOrDefaultAsync(c => c.Id == leaderId, ct)
                ?? throw new InvalidOperationException("Selected leading country could not be found.");

            var followers = await _db.Countries
                .Where(c => normalizedFollowers.Contains(c.Id))
                .Include(c => c.Regions)
                .ToListAsync(ct);

            if (followers.Count != normalizedFollowers.Count)
            {
                throw new InvalidOperationException("One or more countries selected for merge no longer exist.");
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                await _db.Entry(leader).Collection(c => c.Regions).LoadAsync(ct);
                var regionLookup = new Dictionary<string, Region>(StringComparer.OrdinalIgnoreCase);
                foreach (var region in leader.Regions)
                {
                    var key = GetNameKey(region.Name);
                    if (!regionLookup.ContainsKey(key))
                    {
                        regionLookup[key] = region;
                    }
                }

                foreach (var follower in followers)
                {
                    await _db.Entry(follower).Collection(c => c.Regions).LoadAsync(ct);

                    foreach (var region in follower.Regions.ToList())
                    {
                        var key = GetNameKey(region.Name);
                        if (regionLookup.TryGetValue(key, out var existingRegion))
                        {
                            await MergeRegionEntitiesAsync(existingRegion, region, ct);
                        }
                        else
                        {
                            region.CountryId = leader.Id;
                            region.Country = leader;
                            if (!leader.Regions.Contains(region))
                            {
                                leader.Regions.Add(region);
                            }
                            regionLookup[key] = region;
                        }
                    }

                    _db.Countries.Remove(follower);
                }

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return new TerroirMergeResult(leader.Id, leader.Name, followers.Count);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    public async Task<TerroirMergeResult> MergeRegionsAsync(Guid leaderId, IReadOnlyCollection<Guid> followerIds, CancellationToken ct = default)
    {
        var normalizedFollowers = NormalizeFollowerIds(leaderId, followerIds);
        if (normalizedFollowers.Count == 0)
        {
            throw new InvalidOperationException("Select at least two regions to merge.");
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var leader = await _db.Regions
                .Include(r => r.Appellations)
                .FirstOrDefaultAsync(r => r.Id == leaderId, ct)
                ?? throw new InvalidOperationException("Selected leading region could not be found.");

            var followers = await _db.Regions
                .Where(r => normalizedFollowers.Contains(r.Id))
                .Include(r => r.Appellations)
                .ToListAsync(ct);

            if (followers.Count != normalizedFollowers.Count)
            {
                throw new InvalidOperationException("One or more regions selected for merge no longer exist.");
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                await _db.Entry(leader).Collection(r => r.Appellations).LoadAsync(ct);
                var lookup = BuildAppellationLookup(leader.Appellations);

                foreach (var follower in followers)
                {
                    await _db.Entry(follower).Collection(r => r.Appellations).LoadAsync(ct);

                    foreach (var appellation in follower.Appellations.ToList())
                    {
                        var key = GetNameKey(appellation.Name);
                        if (lookup.TryGetValue(key, out var existing))
                        {
                            await MergeAppellationEntitiesAsync(existing, appellation, ct);
                        }
                        else
                        {
                            appellation.RegionId = leader.Id;
                            appellation.Region = leader;
                            if (!leader.Appellations.Contains(appellation))
                            {
                                leader.Appellations.Add(appellation);
                            }
                            lookup[key] = appellation;
                        }
                    }

                    _db.Regions.Remove(follower);
                }

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return new TerroirMergeResult(leader.Id, leader.Name, followers.Count);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    public async Task<TerroirMergeResult> MergeAppellationsAsync(Guid leaderId, IReadOnlyCollection<Guid> followerIds, CancellationToken ct = default)
    {
        var normalizedFollowers = NormalizeFollowerIds(leaderId, followerIds);
        if (normalizedFollowers.Count == 0)
        {
            throw new InvalidOperationException("Select at least two appellations to merge.");
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var leader = await _db.Appellations
                .Include(a => a.SubAppellations)
                .FirstOrDefaultAsync(a => a.Id == leaderId, ct)
                ?? throw new InvalidOperationException("Selected leading appellation could not be found.");

            var followers = await _db.Appellations
                .Where(a => normalizedFollowers.Contains(a.Id))
                .Include(a => a.SubAppellations)
                .ToListAsync(ct);

            if (followers.Count != normalizedFollowers.Count)
            {
                throw new InvalidOperationException("One or more appellations selected for merge no longer exist.");
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                await _db.Entry(leader).Collection(a => a.SubAppellations).LoadAsync(ct);
                var lookup = BuildSubAppellationLookup(leader.SubAppellations);

                foreach (var follower in followers)
                {
                    await _db.Entry(follower).Collection(a => a.SubAppellations).LoadAsync(ct);

                    foreach (var subApp in follower.SubAppellations.ToList())
                    {
                        var key = GetNameKey(subApp.Name);
                        if (lookup.TryGetValue(key, out var existing))
                        {
                            await MergeSubAppellationEntitiesAsync(existing, subApp, ct);
                        }
                        else
                        {
                            subApp.AppellationId = leader.Id;
                            subApp.Appellation = leader;
                            if (!leader.SubAppellations.Contains(subApp))
                            {
                                leader.SubAppellations.Add(subApp);
                            }
                            lookup[key] = subApp;
                        }
                    }

                    _db.Appellations.Remove(follower);
                }

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return new TerroirMergeResult(leader.Id, leader.Name, followers.Count);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    public async Task<TerroirMergeResult> MergeSubAppellationsAsync(Guid leaderId, IReadOnlyCollection<Guid> followerIds, CancellationToken ct = default)
    {
        var normalizedFollowers = NormalizeFollowerIds(leaderId, followerIds);
        if (normalizedFollowers.Count == 0)
        {
            throw new InvalidOperationException("Select at least two sub-appellations to merge.");
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var leader = await _db.SubAppellations
                .Include(sa => sa.Wines)
                .FirstOrDefaultAsync(sa => sa.Id == leaderId, ct)
                ?? throw new InvalidOperationException("Selected leading sub-appellation could not be found.");

            var followers = await _db.SubAppellations
                .Where(sa => normalizedFollowers.Contains(sa.Id))
                .Include(sa => sa.Wines)
                .ToListAsync(ct);

            if (followers.Count != normalizedFollowers.Count)
            {
                throw new InvalidOperationException("One or more sub-appellations selected for merge no longer exist.");
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                await _db.Entry(leader).Collection(sa => sa.Wines).LoadAsync(ct);
                await _db.Entry(leader).Collection(sa => sa.SuggestedAppellations).Query()
                    .Include(s => s.SuggestedWines)
                    .LoadAsync(ct);

                var wineLookup = BuildWineLookup(leader.Wines);
                var suggestionLookup = leader.SuggestedAppellations
                    .Where(s => s?.TasteProfileId != Guid.Empty)
                    .GroupBy(s => s!.TasteProfileId)
                    .ToDictionary(group => group.Key, group => group.First());

                foreach (var follower in followers)
                {
                    await _db.Entry(follower).Collection(sa => sa.Wines).LoadAsync(ct);
                    await _db.Entry(follower).Collection(sa => sa.SuggestedAppellations).Query()
                        .Include(s => s.SuggestedWines)
                        .LoadAsync(ct);

                    foreach (var wine in follower.Wines.ToList())
                    {
                        var key = GetNameKey(wine.Name);
                        if (wineLookup.TryGetValue(key, out var existingWine))
                        {
                            await MergeWineEntitiesAsync(existingWine, wine, ct);
                        }
                        else
                        {
                            wine.SubAppellationId = leader.Id;
                            wine.SubAppellation = leader;
                            leader.Wines.Add(wine);
                            wineLookup[key] = wine;
                        }
                    }

                    foreach (var suggestion in follower.SuggestedAppellations.ToList())
                    {
                        if (suggestionLookup.TryGetValue(suggestion.TasteProfileId, out var existingSuggestion))
                        {
                            await MergeSuggestedAppellationAsync(existingSuggestion, suggestion, ct);
                        }
                        else
                        {
                            suggestion.SubAppellationId = leader.Id;
                            suggestion.SubAppellation = leader;
                            leader.SuggestedAppellations.Add(suggestion);
                            suggestionLookup[suggestion.TasteProfileId] = suggestion;
                        }
                    }

                    _db.SubAppellations.Remove(follower);
                }

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return new TerroirMergeResult(leader.Id, leader.Name ?? "Unnamed sub-appellation", followers.Count);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    public async Task<TerroirMergeResult> MergeWinesAsync(Guid leaderId, IReadOnlyCollection<Guid> followerIds, CancellationToken ct = default)
    {
        var normalizedFollowers = NormalizeFollowerIds(leaderId, followerIds);
        if (normalizedFollowers.Count == 0)
        {
            throw new InvalidOperationException("Select at least two wines to merge.");
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var leader = await _db.Wines
                .Include(w => w.WineVintages)
                .FirstOrDefaultAsync(w => w.Id == leaderId, ct)
                ?? throw new InvalidOperationException("Selected leading wine could not be found.");

            var followers = await _db.Wines
                .Where(w => normalizedFollowers.Contains(w.Id))
                .Include(w => w.WineVintages)
                .ToListAsync(ct);

            if (followers.Count != normalizedFollowers.Count)
            {
                throw new InvalidOperationException("One or more wines selected for merge no longer exist.");
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                await MergeWineCollectionAsync(leader, followers, ct);
                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return new TerroirMergeResult(leader.Id, leader.Name, followers.Count);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    private async Task MergeWineCollectionAsync(Wine leader, IReadOnlyList<Wine> followers, CancellationToken ct)
    {
        await _db.Entry(leader).Collection(w => w.WineVintages).LoadAsync(ct);
        await _db.Entry(leader).Collection(w => w.SuggestedWines).LoadAsync(ct);

        var vintageLookup = leader.WineVintages.ToDictionary(v => v.Vintage);
        var suggestionLookup = leader.SuggestedWines.ToDictionary(sw => sw.SuggestedAppellationId);

        foreach (var follower in followers)
        {
            await _db.Entry(follower).Collection(w => w.WineVintages).LoadAsync(ct);
            await _db.Entry(follower).Collection(w => w.SuggestedWines).LoadAsync(ct);

            foreach (var vintage in follower.WineVintages.ToList())
            {
                await _db.Entry(vintage).Collection(v => v.Bottles).LoadAsync(ct);
                await _db.Entry(vintage).Collection(v => v.EvolutionScores).LoadAsync(ct);

                if (vintageLookup.TryGetValue(vintage.Vintage, out var existingVintage))
                {
                    await _db.Entry(existingVintage).Collection(v => v.Bottles).LoadAsync(ct);
                    await _db.Entry(existingVintage).Collection(v => v.EvolutionScores).LoadAsync(ct);

                    foreach (var bottle in vintage.Bottles.ToList())
                    {
                        bottle.WineVintageId = existingVintage.Id;
                        bottle.WineVintage = existingVintage;
                        existingVintage.Bottles.Add(bottle);
                    }

                    foreach (var score in vintage.EvolutionScores.ToList())
                    {
                        score.WineVintageId = existingVintage.Id;
                        score.WineVintage = existingVintage;
                        existingVintage.EvolutionScores.Add(score);
                    }

                    _db.WineVintages.Remove(vintage);
                }
                else
                {
                    vintage.WineId = leader.Id;
                    vintage.Wine = leader;
                    leader.WineVintages.Add(vintage);
                    vintageLookup[vintage.Vintage] = vintage;
                }
            }

            foreach (var suggestedWine in follower.SuggestedWines.ToList())
            {
                if (suggestionLookup.TryGetValue(suggestedWine.SuggestedAppellationId, out var existingSuggestedWine))
                {
                    if (string.IsNullOrWhiteSpace(existingSuggestedWine.Vintage) && !string.IsNullOrWhiteSpace(suggestedWine.Vintage))
                    {
                        existingSuggestedWine.Vintage = suggestedWine.Vintage;
                    }

                    _db.SuggestedWines.Remove(suggestedWine);
                }
                else
                {
                    suggestedWine.WineId = leader.Id;
                    suggestedWine.Wine = leader;
                    leader.SuggestedWines.Add(suggestedWine);
                    suggestionLookup[suggestedWine.SuggestedAppellationId] = suggestedWine;
                }
            }

            _db.Wines.Remove(follower);
        }
    }

    private async Task MergeWineEntitiesAsync(Wine leader, Wine follower, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(leader.GrapeVariety) && !string.IsNullOrWhiteSpace(follower.GrapeVariety))
        {
            leader.GrapeVariety = follower.GrapeVariety;
        }

        await MergeWineCollectionAsync(leader, new[] { follower }, ct);
    }

    private async Task MergeRegionEntitiesAsync(Region leader, Region follower, CancellationToken ct)
    {
        await _db.Entry(leader).Collection(r => r.Appellations).LoadAsync(ct);
        await _db.Entry(follower).Collection(r => r.Appellations).LoadAsync(ct);

        var lookup = BuildAppellationLookup(leader.Appellations);

        foreach (var appellation in follower.Appellations.ToList())
        {
            var key = GetNameKey(appellation.Name);
            if (lookup.TryGetValue(key, out var existing))
            {
                await MergeAppellationEntitiesAsync(existing, appellation, ct);
            }
            else
            {
                appellation.RegionId = leader.Id;
                appellation.Region = leader;
                leader.Appellations.Add(appellation);
                lookup[key] = appellation;
            }
        }

        _db.Regions.Remove(follower);
    }

    private async Task MergeAppellationEntitiesAsync(Appellation leader, Appellation follower, CancellationToken ct)
    {
        await _db.Entry(leader).Collection(a => a.SubAppellations).LoadAsync(ct);
        await _db.Entry(follower).Collection(a => a.SubAppellations).LoadAsync(ct);

        var lookup = BuildSubAppellationLookup(leader.SubAppellations);

        foreach (var subApp in follower.SubAppellations.ToList())
        {
            var key = GetNameKey(subApp.Name);
            if (lookup.TryGetValue(key, out var existing))
            {
                await MergeSubAppellationEntitiesAsync(existing, subApp, ct);
            }
            else
            {
                subApp.AppellationId = leader.Id;
                subApp.Appellation = leader;
                leader.SubAppellations.Add(subApp);
                lookup[key] = subApp;
            }
        }

        _db.Appellations.Remove(follower);
    }

    private async Task MergeSubAppellationEntitiesAsync(SubAppellation leader, SubAppellation follower, CancellationToken ct)
    {
        await _db.Entry(leader).Collection(sa => sa.Wines).LoadAsync(ct);
        await _db.Entry(leader).Collection(sa => sa.SuggestedAppellations).Query()
            .Include(s => s.SuggestedWines)
            .LoadAsync(ct);

        await _db.Entry(follower).Collection(sa => sa.Wines).LoadAsync(ct);
        await _db.Entry(follower).Collection(sa => sa.SuggestedAppellations).Query()
            .Include(s => s.SuggestedWines)
            .LoadAsync(ct);

        var wineLookup = BuildWineLookup(leader.Wines);
        var suggestionLookup = leader.SuggestedAppellations
            .Where(s => s?.TasteProfileId != Guid.Empty)
            .GroupBy(s => s!.TasteProfileId)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var wine in follower.Wines.ToList())
        {
            var key = GetNameKey(wine.Name);
            if (wineLookup.TryGetValue(key, out var existingWine))
            {
                await MergeWineEntitiesAsync(existingWine, wine, ct);
            }
            else
            {
                wine.SubAppellationId = leader.Id;
                wine.SubAppellation = leader;
                leader.Wines.Add(wine);
                wineLookup[key] = wine;
            }
        }

        foreach (var suggestion in follower.SuggestedAppellations.ToList())
        {
            if (suggestionLookup.TryGetValue(suggestion.TasteProfileId, out var existingSuggestion))
            {
                await MergeSuggestedAppellationAsync(existingSuggestion, suggestion, ct);
            }
            else
            {
                suggestion.SubAppellationId = leader.Id;
                suggestion.SubAppellation = leader;
                leader.SuggestedAppellations.Add(suggestion);
                suggestionLookup[suggestion.TasteProfileId] = suggestion;
            }
        }

        _db.SubAppellations.Remove(follower);
    }

    private async Task MergeSuggestedAppellationAsync(SuggestedAppellation leader, SuggestedAppellation follower, CancellationToken ct)
    {
        await _db.Entry(leader).Collection(s => s.SuggestedWines).LoadAsync(ct);
        await _db.Entry(follower).Collection(s => s.SuggestedWines).LoadAsync(ct);

        if (string.IsNullOrWhiteSpace(leader.Reason) && !string.IsNullOrWhiteSpace(follower.Reason))
        {
            leader.Reason = follower.Reason;
        }

        var wineLookup = leader.SuggestedWines.ToDictionary(sw => sw.WineId);

        foreach (var suggestedWine in follower.SuggestedWines.ToList())
        {
            if (wineLookup.TryGetValue(suggestedWine.WineId, out var existing))
            {
                if (string.IsNullOrWhiteSpace(existing.Vintage) && !string.IsNullOrWhiteSpace(suggestedWine.Vintage))
                {
                    existing.Vintage = suggestedWine.Vintage;
                }

                _db.SuggestedWines.Remove(suggestedWine);
            }
            else
            {
                suggestedWine.SuggestedAppellationId = leader.Id;
                suggestedWine.SuggestedAppellation = leader;
                leader.SuggestedWines.Add(suggestedWine);
                wineLookup[suggestedWine.WineId] = suggestedWine;
            }
        }

        _db.SuggestedAppellations.Remove(follower);
    }

    private static Dictionary<string, Appellation> BuildAppellationLookup(IEnumerable<Appellation> source)
    {
        var lookup = new Dictionary<string, Appellation>(StringComparer.OrdinalIgnoreCase);
        foreach (var appellation in source)
        {
            var key = GetNameKey(appellation.Name);
            if (!lookup.ContainsKey(key))
            {
                lookup[key] = appellation;
            }
        }

        return lookup;
    }

    private static Dictionary<string, SubAppellation> BuildSubAppellationLookup(IEnumerable<SubAppellation> source)
    {
        var lookup = new Dictionary<string, SubAppellation>(StringComparer.OrdinalIgnoreCase);
        foreach (var subAppellation in source)
        {
            var key = GetNameKey(subAppellation.Name);
            if (!lookup.ContainsKey(key))
            {
                lookup[key] = subAppellation;
            }
        }

        return lookup;
    }

    private static Dictionary<string, Wine> BuildWineLookup(IEnumerable<Wine> source)
    {
        var lookup = new Dictionary<string, Wine>(StringComparer.OrdinalIgnoreCase);
        foreach (var wine in source)
        {
            var key = GetNameKey(wine.Name);
            if (!lookup.ContainsKey(key))
            {
                lookup[key] = wine;
            }
        }

        return lookup;
    }

    private static string GetNameKey(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? NullNameKey : value.Trim();
    }

    private static List<Guid> NormalizeFollowerIds(Guid leaderId, IReadOnlyCollection<Guid> followerIds)
    {
        if (followerIds is null || followerIds.Count == 0)
        {
            return new List<Guid>();
        }

        return followerIds
            .Where(id => id != Guid.Empty && id != leaderId)
            .Distinct()
            .ToList();
    }
}

public readonly record struct TerroirMergeResult(Guid LeaderId, string LeaderName, int FollowersMerged);
