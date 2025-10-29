using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;
using AllMCPSolution.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AllMCPSolution.Controllers;

[Authorize]
[Route("wine-surfer")]
public sealed class FellowSurfersController : WineSurferControllerBase
{
    public sealed class FellowSurfersViewModel
    {
        public string? CurrentUserDisplayName { get; init; }

        public int SisterhoodCount { get; init; }

        public IReadOnlyList<FellowSurferConnection> Connections { get; init; } = Array.Empty<FellowSurferConnection>();

        public int ConnectionCount => Connections.Count;
    }

    public sealed record FellowSurferConnection(
        Guid UserId,
        string DisplayName,
        string AvatarLetter,
        string? Email,
        IReadOnlyList<FellowSurferSharedSisterhood> SharedSisterhoods)
    {
        public int SharedSisterhoodCount => SharedSisterhoods.Count;
    }

    public sealed record FellowSurferSharedSisterhood(Guid SisterhoodId, string Name, string? Description);

    private readonly ISisterhoodRepository _sisterhoodRepository;
    private readonly IWineSurferTopBarService _topBarService;

    public FellowSurfersController(
        ISisterhoodRepository sisterhoodRepository,
        IUserRepository userRepository,
        IWineSurferTopBarService topBarService,
        UserManager<ApplicationUser> userManager) : base(userManager, userRepository)
    {
        _sisterhoodRepository = sisterhoodRepository;
        _topBarService = topBarService;
    }

    [HttpGet("fellow-surfers")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        var currentPath = HttpContext?.Request?.Path.Value ?? string.Empty;
        ViewData["WineSurferTopBarModel"] = await _topBarService.BuildAsync(User, currentPath, cancellationToken);
        Response.ContentType = "text/html; charset=utf-8";

        var identityName = User?.Identity?.Name;
        var email = User?.FindFirstValue(ClaimTypes.Email) ?? User?.FindFirstValue("email");
        var domainUser = await _userRepository.GetByIdAsync(currentUserId.Value, cancellationToken);
        var displayName = StringUtilities.ResolveDisplayName(domainUser?.Name, identityName, email);

        var sisterhoods = await _sisterhoodRepository.GetForUserAsync(currentUserId.Value, cancellationToken);
        var connections = BuildConnections(sisterhoods, currentUserId.Value);

        var viewModel = new FellowSurfersViewModel
        {
            CurrentUserDisplayName = displayName,
            SisterhoodCount = sisterhoods.Count,
            Connections = connections
        };

        return View("Index", viewModel);
    }

    private static IReadOnlyList<FellowSurferConnection> BuildConnections(IReadOnlyList<Sisterhood> sisterhoods, Guid currentUserId)
    {
        if (sisterhoods is null || sisterhoods.Count == 0)
        {
            return Array.Empty<FellowSurferConnection>();
        }

        var map = new Dictionary<Guid, ConnectionAccumulator>();

        foreach (var sisterhood in sisterhoods)
        {
            if (sisterhood?.Memberships is not { Count: > 0 })
            {
                continue;
            }

            var sharedSisterhood = new FellowSurferSharedSisterhood(
                sisterhood.Id,
                NormalizeName(sisterhood.Name),
                NormalizeOptional(sisterhood.Description));

            foreach (var membership in sisterhood.Memberships)
            {
                if (membership.UserId == currentUserId)
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
        private readonly List<FellowSurferSharedSisterhood> _sisterhoods = new();

        public ConnectionAccumulator(Guid userId, string displayName, string? email)
        {
            UserId = userId;
            DisplayName = displayName;
            Email = email;
        }

        public Guid UserId { get; }

        public string DisplayName { get; }

        public string? Email { get; }

        public void AddSharedSisterhood(FellowSurferSharedSisterhood sisterhood)
        {
            if (_sisterhoodIds.Add(sisterhood.SisterhoodId))
            {
                _sisterhoods.Add(sisterhood);
            }
        }

        public FellowSurferConnection BuildConnection()
        {
            var ordered = _sisterhoods
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.SisterhoodId)
                .ToList();

            return new FellowSurferConnection(
                UserId,
                DisplayName,
                GetAvatarLetter(DisplayName),
                Email,
                ordered);
        }
    }
}
