using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using AllMCPSolution.Utilities;

namespace AllMCPSolution.Services;

public interface ISisterhoodConnectionService
{
    Task<IReadOnlyList<SisterhoodConnectionUser>> GetConnectionsAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed class SisterhoodConnectionService : ISisterhoodConnectionService
{
    private readonly ISisterhoodRepository _sisterhoodRepository;

    public SisterhoodConnectionService(ISisterhoodRepository sisterhoodRepository)
    {
        _sisterhoodRepository = sisterhoodRepository;
    }

    public async Task<IReadOnlyList<SisterhoodConnectionUser>> GetConnectionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return Array.Empty<SisterhoodConnectionUser>();
        }

        var sisterhoods = await _sisterhoodRepository.GetForUserAsync(userId, cancellationToken);
        if (sisterhoods is null || sisterhoods.Count == 0)
        {
            return Array.Empty<SisterhoodConnectionUser>();
        }

        var map = new Dictionary<Guid, ConnectionAccumulator>();

        foreach (var sisterhood in sisterhoods)
        {
            if (sisterhood?.Memberships is not { Count: > 0 })
            {
                continue;
            }

            var sharedSisterhood = new SisterhoodConnectionSharedSisterhood(
                sisterhood.Id,
                NormalizeName(sisterhood.Name),
                NormalizeOptional(sisterhood.Description));

            foreach (var membership in sisterhood.Memberships)
            {
                if (membership.UserId == userId)
                {
                    continue;
                }

                var user = membership.User;
                if (user is null)
                {
                    continue;
                }

                if (!map.TryGetValue(user.Id, out var accumulator))
                {
                    var memberDisplayName = ResolveMemberDisplayName(user);
                    accumulator = new ConnectionAccumulator(user.Id, memberDisplayName, NormalizeOptional(user.Email));
                    map[user.Id] = accumulator;
                }

                accumulator.AddSharedSisterhood(sharedSisterhood);
            }
        }

        return map.Values
            .Select(accumulator => accumulator.BuildConnection())
            .OrderBy(connection => connection.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(connection => connection.UserId)
            .ToList();
    }

    private static string ResolveMemberDisplayName(ApplicationUser user)
    {
        var displayName = StringUtilities.ResolveDisplayName(user.Name, user.UserName, user.Email);
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName.Trim();
        }

        return "Fellow Surfer";
    }

    private static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Sisterhood";
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static string GetAvatarLetter(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "?";
        }

        var trimmed = displayName.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? "?"
            : trimmed.Substring(0, 1).ToUpperInvariant();
    }

    private sealed class ConnectionAccumulator
    {
        private readonly HashSet<Guid> _sisterhoodIds = new();
        private readonly List<SisterhoodConnectionSharedSisterhood> _sisterhoods = new();

        public ConnectionAccumulator(Guid userId, string displayName, string? email)
        {
            UserId = userId;
            DisplayName = displayName;
            Email = email;
        }

        public Guid UserId { get; }

        public string DisplayName { get; }

        public string? Email { get; }

        public void AddSharedSisterhood(SisterhoodConnectionSharedSisterhood sisterhood)
        {
            if (_sisterhoodIds.Add(sisterhood.SisterhoodId))
            {
                _sisterhoods.Add(sisterhood);
            }
        }

        public SisterhoodConnectionUser BuildConnection()
        {
            var orderedSisterhoods = _sisterhoods
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.SisterhoodId)
                .ToList();

            return new SisterhoodConnectionUser(
                UserId,
                DisplayName,
                GetAvatarLetter(DisplayName),
                Email,
                orderedSisterhoods);
        }
    }
}
