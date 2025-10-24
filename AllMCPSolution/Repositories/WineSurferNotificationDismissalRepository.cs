using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Data;
using AllMCPSolution.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Repositories;

public interface IWineSurferNotificationDismissalRepository
{
    Task<IReadOnlyDictionary<string, IReadOnlyCollection<string>>> GetDismissedStampsAsync(
        Guid userId,
        IEnumerable<string> categories,
        CancellationToken ct = default);

    Task<WineSurferNotificationDismissal> UpsertAsync(
        Guid userId,
        string category,
        string stamp,
        DateTime? dismissedAtUtc = null,
        CancellationToken ct = default);
}

public class WineSurferNotificationDismissalRepository : IWineSurferNotificationDismissalRepository
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> EmptyResult =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);

    private readonly ApplicationDbContext _db;

    public WineSurferNotificationDismissalRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyCollection<string>>> GetDismissedStampsAsync(
        Guid userId,
        IEnumerable<string> categories,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            return EmptyResult;
        }

        var normalizedCategories = (categories ?? Array.Empty<string>())
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Select(NormalizeCategory)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedCategories.Length == 0)
        {
            return EmptyResult;
        }

        try
        {
            var dismissals = await _db.WineSurferNotificationDismissals
                .AsNoTracking()
                .Where(dismissal => dismissal.UserId == userId && normalizedCategories.Contains(dismissal.Category))
                .ToListAsync(ct);

            if (dismissals.Count == 0)
            {
                return EmptyResult;
            }

            return dismissals
                .GroupBy(dismissal => dismissal.Category, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyCollection<string>)group
                        .Select(dismissal => dismissal.Stamp)
                        .Distinct(StringComparer.Ordinal)
                        .ToList(),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (SqlException ex) when (IsMissingNotificationDismissalsTable(ex))
        {
            return EmptyResult;
        }
    }

    public async Task<WineSurferNotificationDismissal> UpsertAsync(
        Guid userId,
        string category,
        string stamp,
        DateTime? dismissedAtUtc = null,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id cannot be empty.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("Category is required.", nameof(category));
        }

        if (string.IsNullOrWhiteSpace(stamp))
        {
            throw new ArgumentException("Stamp is required.", nameof(stamp));
        }

        var normalizedCategory = NormalizeCategory(category);
        var normalizedStamp = stamp.Trim();
        var timestamp = dismissedAtUtc?.ToUniversalTime() ?? DateTime.UtcNow;

        try
        {
            var dismissal = await _db.WineSurferNotificationDismissals
                .FirstOrDefaultAsync(
                    entry => entry.UserId == userId
                        && entry.Category == normalizedCategory
                        && entry.Stamp == normalizedStamp,
                    ct);

            if (dismissal is null)
            {
                dismissal = new WineSurferNotificationDismissal
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Category = normalizedCategory,
                    Stamp = normalizedStamp,
                    DismissedAtUtc = timestamp,
                };

                await _db.WineSurferNotificationDismissals.AddAsync(dismissal, ct);
            }
            else
            {
                dismissal.DismissedAtUtc = timestamp;
            }

            await _db.SaveChangesAsync(ct);

            return dismissal;
        }
        catch (SqlException ex) when (IsMissingNotificationDismissalsTable(ex))
        {
            return new WineSurferNotificationDismissal
            {
                Id = Guid.Empty,
                UserId = userId,
                Category = normalizedCategory,
                Stamp = normalizedStamp,
                DismissedAtUtc = timestamp,
            };
        }
    }

    private static string NormalizeCategory(string category)
    {
        return category.Trim().ToLowerInvariant();
    }

    private static bool IsMissingNotificationDismissalsTable(SqlException exception)
    {
        if (exception is null)
        {
            return false;
        }

        foreach (SqlError error in exception.Errors)
        {
            if (error.Number == 208 && error.Message.Contains("WineSurferNotificationDismissals", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
