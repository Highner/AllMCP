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
    Task<ApplicationUser> GetOrCreateAsync(string name, string tasteProfile, CancellationToken ct = default);
    Task AddAsync(ApplicationUser user, CancellationToken ct = default);
    Task UpdateAsync(ApplicationUser user, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public class UserRepository : IUserRepository
{
    private readonly UserManager<ApplicationUser> _userManager;

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

    public async Task<ApplicationUser> GetOrCreateAsync(string name, string tasteProfile, CancellationToken ct = default)
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

        var entity = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            UserName = trimmedName,
            TasteProfile = tasteProfile?.Trim() ?? string.Empty
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
}
