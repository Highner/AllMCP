using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Models;
using AllMCPSolution.Utilities;

namespace AllMCPSolution.Repositories;

public interface IUserRepository
{
    Task<List<ApplicationUser>> GetAllAsync(CancellationToken ct = default);
    Task<ApplicationUser?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ApplicationUser?> FindByNameAsync(string name, CancellationToken ct = default);
    Task<ApplicationUser?> FindByEmailAsync(string email, CancellationToken ct = default);
    Task<IReadOnlyList<ApplicationUser>> SearchByApproximateNameAsync(string name, int maxResults = 5, CancellationToken ct = default);
    Task<IReadOnlyList<ApplicationUser>> SearchByNameOrEmailAsync(string query, int maxResults = 10, CancellationToken ct = default);
    Task<ApplicationUser> GetOrCreateAsync(string name, string tasteProfile, string? tasteProfileSummary = null, CancellationToken ct = default);
    Task AddAsync(ApplicationUser user, CancellationToken ct = default);
    Task UpdateAsync(ApplicationUser user, CancellationToken ct = default);
    Task<ApplicationUser?> UpdateDisplayNameAsync(Guid id, string displayName, CancellationToken ct = default);
    Task<ApplicationUser?> UpdateTasteProfileAsync(Guid id, Guid? tasteProfileId, string tasteProfile, string? tasteProfileSummary, CancellationToken ct = default);
    Task<TasteProfile?> AddGeneratedTasteProfileAsync(Guid id, string tasteProfile, string? tasteProfileSummary, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public class UserRepository : IUserRepository
{
    private readonly UserManager<ApplicationUser> _userManager;
    private const int TasteProfileMaxLength = 4096;
    private const int TasteProfileSummaryMaxLength = 512;

    public UserRepository(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<List<ApplicationUser>> GetAllAsync(CancellationToken ct = default)
    {
        return await _userManager.Users
            .AsNoTracking()
            .OrderBy(u => u.Name)
            .ThenBy(u => u.UserName)
            .ToListAsync(ct);
    }

    public async Task<ApplicationUser?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _userManager.Users
            .AsNoTracking()
            .Include(u => u.TasteProfiles)
            .FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<ApplicationUser?> FindByNameAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim();
        var normalized = _userManager.NormalizeName(trimmed);

        return await _userManager.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.NormalizedUserName == normalized, ct);
    }

    public async Task<ApplicationUser?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var trimmed = email.Trim();
        var normalized = _userManager.NormalizeEmail(trimmed);

        return await _userManager.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
    }

    public async Task<IReadOnlyList<ApplicationUser>> SearchByApproximateNameAsync(string name, int maxResults = 5, CancellationToken ct = default)
    {
        var users = await _userManager.Users
            .AsNoTracking()
            .ToListAsync(ct);

        return FuzzyMatchUtilities.FindClosestMatches(users, name, u => u.Name, maxResults);
    }

    public async Task<IReadOnlyList<ApplicationUser>> SearchByNameOrEmailAsync(string query, int maxResults = 10, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<ApplicationUser>();
        }

        var trimmed = query.Trim();
        var normalizedName = _userManager.NormalizeName(trimmed) ?? trimmed.ToUpperInvariant();
        var normalizedEmail = _userManager.NormalizeEmail(trimmed) ?? trimmed.ToUpperInvariant();
        var likeName = $"%{EscapeLikePattern(trimmed)}%";
        var likeNormalizedName = $"%{EscapeLikePattern(normalizedName)}%";
        var likeNormalizedEmail = $"%{EscapeLikePattern(normalizedEmail)}%";
        var limit = Math.Max(1, maxResults);

        var directMatches = await _userManager.Users
            .AsNoTracking()
            .Where(u =>
                (!string.IsNullOrEmpty(u.Name) && EF.Functions.Like(u.Name!, likeName, "\\")) ||
                (!string.IsNullOrEmpty(u.NormalizedUserName) && EF.Functions.Like(u.NormalizedUserName!, likeNormalizedName, "\\")) ||
                (!string.IsNullOrEmpty(u.NormalizedEmail) && EF.Functions.Like(u.NormalizedEmail!, likeNormalizedEmail, "\\")))
            .OrderBy(u => u.Name)
            .ThenBy(u => u.UserName)
            .Take(limit * 3)
            .ToListAsync(ct);

        var results = new List<ApplicationUser>(directMatches.Count);
        var seen = new HashSet<Guid>();

        foreach (var user in directMatches)
        {
            if (seen.Add(user.Id))
            {
                results.Add(user);

                if (results.Count >= limit)
                {
                    return results.Take(limit).ToList();
                }
            }
        }

        var shouldRunFuzzySearch = results.Count < limit && !trimmed.Contains('@');

        if (shouldRunFuzzySearch)
        {
            var fuzzyMatches = await SearchByApproximateNameAsync(trimmed, limit * 3, ct);

            foreach (var user in fuzzyMatches)
            {
                if (seen.Add(user.Id))
                {
                    results.Add(user);

                    if (results.Count >= limit)
                    {
                        break;
                    }
                }
            }
        }

        // Trim to the requested limit while preserving the ranking of direct and fuzzy matches.
        return results.Take(limit).ToList();
    }

    public async Task<ApplicationUser> GetOrCreateAsync(string name, string tasteProfile, string? tasteProfileSummary = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("User name cannot be empty.", nameof(name));
        }

        var trimmedName = name.Trim();
        var existing = await FindByNameAsync(trimmedName, ct);
        if (existing is not null)
        {
            return existing;
        }

        var normalizedTasteProfile = string.IsNullOrWhiteSpace(tasteProfile)
            ? string.Empty
            : tasteProfile.Trim();

        if (normalizedTasteProfile.Length > TasteProfileMaxLength)
        {
            normalizedTasteProfile = normalizedTasteProfile[..TasteProfileMaxLength];
        }

        var normalizedSummary = string.IsNullOrWhiteSpace(tasteProfileSummary)
            ? string.Empty
            : tasteProfileSummary.Trim();

        if (normalizedSummary.Length > TasteProfileSummaryMaxLength)
        {
            normalizedSummary = normalizedSummary[..TasteProfileSummaryMaxLength];
        }

        var entity = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            UserName = trimmedName,
            TasteProfile = normalizedTasteProfile,
            TasteProfileSummary = normalizedSummary
        };

        await AddAsync(entity, ct);
        return await GetByIdAsync(entity.Id, ct) ?? entity;
    }

    public async Task AddAsync(ApplicationUser user, CancellationToken ct = default)
    {
        if (user is null)
        {
            throw new ArgumentNullException(nameof(user));
        }

        ct.ThrowIfCancellationRequested();

        NormalizeUser(user);

        if (user.Id == Guid.Empty)
        {
            user.Id = Guid.NewGuid();
        }

        var result = await _userManager.CreateAsync(user);
        EnsureSucceeded(result, $"Failed to create user '{user.Name}'.");

        if (!string.IsNullOrWhiteSpace(user.TasteProfile))
        {
            await AddGeneratedTasteProfileAsync(user.Id, user.TasteProfile, user.TasteProfileSummary, ct);
        }
    }

    public async Task UpdateAsync(ApplicationUser user, CancellationToken ct = default)
    {
        if (user is null)
        {
            throw new ArgumentNullException(nameof(user));
        }

        ct.ThrowIfCancellationRequested();

        var existing = await _userManager.Users
            .Include(u => u.TasteProfiles)
            .FirstOrDefaultAsync(u => u.Id == user.Id, ct);

        if (existing is null)
        {
            throw new InvalidOperationException($"User '{user.Id}' was not found.");
        }

        if (!string.IsNullOrWhiteSpace(user.Name))
        {
            existing.Name = user.Name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(user.UserName))
        {
            existing.UserName = user.UserName.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(existing.Name))
        {
            existing.UserName = existing.Name;
        }

        var (normalizedProfile, normalizedSummary) = NormalizeTasteProfileValues(
            user.TasteProfile,
            user.TasteProfileSummary);

        existing.TasteProfile = normalizedProfile;
        existing.TasteProfileSummary = normalizedSummary;

        ApplyManualTasteProfileUpdate(existing, normalizedProfile, normalizedSummary, null);

        NormalizeUser(existing);

        var result = await _userManager.UpdateAsync(existing);
        EnsureSucceeded(result, $"Failed to update user '{existing.Name}'.");
    }

    public async Task<ApplicationUser?> UpdateDisplayNameAsync(Guid id, string displayName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var user = await _userManager.Users
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user is null)
        {
            return null;
        }

        var trimmed = string.IsNullOrWhiteSpace(displayName) ? string.Empty : displayName.Trim();

        user.Name = trimmed;

        if (!string.IsNullOrWhiteSpace(user.UserName))
        {
            user.UserName = user.UserName.Trim();
        }
        else
        {
            user.UserName = trimmed;
        }

        user.TasteProfile = user.TasteProfile?.Trim() ?? string.Empty;
        if (user.TasteProfile.Length > TasteProfileMaxLength)
        {
            user.TasteProfile = user.TasteProfile[..TasteProfileMaxLength];
        }

        user.TasteProfileSummary = user.TasteProfileSummary?.Trim() ?? string.Empty;
        if (user.TasteProfileSummary.Length > TasteProfileSummaryMaxLength)
        {
            user.TasteProfileSummary = user.TasteProfileSummary[..TasteProfileSummaryMaxLength];
        }

        var result = await _userManager.UpdateAsync(user);
        EnsureSucceeded(result, $"Failed to update user '{user.Id}'.");

        return await _userManager.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<ApplicationUser?> UpdateTasteProfileAsync(Guid id, Guid? tasteProfileId, string tasteProfile, string? tasteProfileSummary, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var user = await _userManager.Users
            .Include(u => u.TasteProfiles)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user is null)
        {
            return null;
        }

        var (normalizedProfile, normalizedSummary) = NormalizeTasteProfileValues(tasteProfile, tasteProfileSummary);

        user.TasteProfile = normalizedProfile;
        user.TasteProfileSummary = normalizedSummary;

        ApplyManualTasteProfileUpdate(user, normalizedProfile, normalizedSummary, tasteProfileId);

        NormalizeUser(user);

        var result = await _userManager.UpdateAsync(user);
        EnsureSucceeded(result, $"Failed to update user '{user.Id}'.");

        return await _userManager.Users
            .AsNoTracking()
            .Include(u => u.TasteProfiles)
            .FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<TasteProfile?> AddGeneratedTasteProfileAsync(
        Guid id,
        string tasteProfile,
        string? tasteProfileSummary,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var user = await _userManager.Users
            .Include(u => u.TasteProfiles)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user is null)
        {
            return null;
        }

        var (normalizedProfile, normalizedSummary) = NormalizeTasteProfileValues(tasteProfile, tasteProfileSummary);

        user.TasteProfile = normalizedProfile;
        user.TasteProfileSummary = normalizedSummary;

        var newEntry = ApplyGeneratedTasteProfileUpdate(user, normalizedProfile, normalizedSummary);

        NormalizeUser(user);

        var result = await _userManager.UpdateAsync(user);
        EnsureSucceeded(result, $"Failed to update user '{user.Id}'.");

        return newEntry;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return;
        }

        var result = await _userManager.DeleteAsync(user);
        EnsureSucceeded(result, $"Failed to delete user '{user.Name}'.");
    }

    private (string Profile, string Summary) NormalizeTasteProfileValues(string? tasteProfile, string? tasteProfileSummary)
    {
        var normalizedProfile = string.IsNullOrWhiteSpace(tasteProfile)
            ? string.Empty
            : tasteProfile.Trim();

        if (normalizedProfile.Length > TasteProfileMaxLength)
        {
            normalizedProfile = normalizedProfile[..TasteProfileMaxLength];
        }

        var normalizedSummary = string.IsNullOrWhiteSpace(tasteProfileSummary)
            ? string.Empty
            : tasteProfileSummary.Trim();

        if (normalizedSummary.Length > TasteProfileSummaryMaxLength)
        {
            normalizedSummary = normalizedSummary[..TasteProfileSummaryMaxLength];
        }

        return (normalizedProfile, normalizedSummary);
    }

    private static TasteProfile ApplyGeneratedTasteProfileUpdate(ApplicationUser user, string profile, string summary)
    {
        var collection = user.TasteProfiles ?? new List<TasteProfile>();
        user.TasteProfiles = collection;

        foreach (var entry in collection)
        {
            entry.InUse = false;
        }

        var newEntry = new TasteProfile
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            Profile = profile,
            Summary = summary,
            CreatedAt = DateTime.UtcNow,
            InUse = true
        };

        collection.Add(newEntry);

        return newEntry;
    }

    private static TasteProfile ApplyManualTasteProfileUpdate(ApplicationUser user, string profile, string summary, Guid? targetEntryId)
    {
        var collection = user.TasteProfiles ?? new List<TasteProfile>();
        user.TasteProfiles = collection;

        TasteProfile? current = null;

        if (targetEntryId.HasValue)
        {
            current = collection.FirstOrDefault(tp => tp.Id == targetEntryId.Value);
        }

        current ??= collection.FirstOrDefault(tp => tp.InUse);

        if (current is null)
        {
            current = collection
                .OrderByDescending(tp => tp.CreatedAt)
                .FirstOrDefault();

            if (current is null)
            {
                current = new TasteProfile
                {
                    Id = targetEntryId.HasValue && targetEntryId.Value != Guid.Empty
                        ? targetEntryId.Value
                        : Guid.NewGuid(),
                    UserId = user.Id,
                    User = user,
                    Profile = profile,
                    Summary = summary,
                    CreatedAt = DateTime.UtcNow,
                    InUse = true
                };

                collection.Add(current);
            }
            else
            {
                current.InUse = true;
            }
        }

        current.Profile = profile;
        current.Summary = summary;
        current.UserId = user.Id;
        current.User = user;

        if (current.CreatedAt == default)
        {
            current.CreatedAt = DateTime.UtcNow;
        }

        foreach (var entry in collection)
        {
            entry.InUse = ReferenceEquals(entry, current);
        }

        return current;
    }

    private void NormalizeUser(ApplicationUser user)
    {
        if (!string.IsNullOrWhiteSpace(user.Name))
        {
            var trimmed = user.Name.Trim();
            user.Name = trimmed;
            user.UserName = trimmed;
        }

        user.TasteProfile = user.TasteProfile?.Trim() ?? string.Empty;
        if (user.TasteProfile.Length > TasteProfileMaxLength)
        {
            user.TasteProfile = user.TasteProfile[..TasteProfileMaxLength];
        }

        user.TasteProfileSummary = user.TasteProfileSummary?.Trim() ?? string.Empty;
        if (user.TasteProfileSummary.Length > TasteProfileSummaryMaxLength)
        {
            user.TasteProfileSummary = user.TasteProfileSummary[..TasteProfileSummaryMaxLength];
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string message)
    {
        if (result.Succeeded)
        {
            return;
        }

        var details = string.Join(", ", result.Errors.Select(e => e.Description));
        throw new InvalidOperationException($"{message} {details}");
    }

    private static string EscapeLikePattern(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal);
    }
}
