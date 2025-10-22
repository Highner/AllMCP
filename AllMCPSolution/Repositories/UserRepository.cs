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
    Task<ApplicationUser?> UpdateTasteProfileAsync(Guid id, string tasteProfile, string? tasteProfileSummary, CancellationToken ct = default);
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

        return await _userManager.Users
            .AsNoTracking()
            .Where(u =>
                (!string.IsNullOrEmpty(u.Name) && EF.Functions.Like(u.Name!, likeName, "\\")) ||
                (!string.IsNullOrEmpty(u.NormalizedUserName) && EF.Functions.Like(u.NormalizedUserName!, likeNormalizedName, "\\")) ||
                (!string.IsNullOrEmpty(u.NormalizedEmail) && EF.Functions.Like(u.NormalizedEmail!, likeNormalizedEmail, "\\")))
            .OrderBy(u => u.Name)
            .ThenBy(u => u.UserName)
            .Take(limit)
            .ToListAsync(ct);
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
    }

    public async Task UpdateAsync(ApplicationUser user, CancellationToken ct = default)
    {
        if (user is null)
        {
            throw new ArgumentNullException(nameof(user));
        }

        ct.ThrowIfCancellationRequested();

        NormalizeUser(user);

        var result = await _userManager.UpdateAsync(user);
        EnsureSucceeded(result, $"Failed to update user '{user.Name}'.");
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

    public async Task<ApplicationUser?> UpdateTasteProfileAsync(Guid id, string tasteProfile, string? tasteProfileSummary, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var user = await _userManager.Users
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user is null)
        {
            return null;
        }

        var trimmedTasteProfile = string.IsNullOrWhiteSpace(tasteProfile)
            ? string.Empty
            : tasteProfile.Trim();

        if (trimmedTasteProfile.Length > TasteProfileMaxLength)
        {
            trimmedTasteProfile = trimmedTasteProfile[..TasteProfileMaxLength];
        }

        user.TasteProfile = trimmedTasteProfile;

        var trimmedSummary = string.IsNullOrWhiteSpace(tasteProfileSummary)
            ? string.Empty
            : tasteProfileSummary.Trim();

        if (trimmedSummary.Length > TasteProfileSummaryMaxLength)
        {
            trimmedSummary = trimmedSummary[..TasteProfileSummaryMaxLength];
        }

        user.TasteProfileSummary = trimmedSummary;

        if (!string.IsNullOrWhiteSpace(user.Name))
        {
            user.Name = user.Name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(user.UserName))
        {
            user.UserName = user.UserName.Trim();
        }

        var result = await _userManager.UpdateAsync(user);
        EnsureSucceeded(result, $"Failed to update user '{user.Id}'.");

        return await _userManager.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, ct);
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
