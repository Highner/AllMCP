using System;
using System.Collections.Generic;
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

        public IReadOnlyList<SisterhoodConnectionUser> Connections { get; init; } = Array.Empty<SisterhoodConnectionUser>();

        public int ConnectionCount => Connections.Count;
    }

    private readonly ISisterhoodRepository _sisterhoodRepository;
    private readonly IWineSurferTopBarService _topBarService;
    private readonly ISisterhoodConnectionService _connectionService;

    public FellowSurfersController(
        ISisterhoodRepository sisterhoodRepository,
        IUserRepository userRepository,
        IWineSurferTopBarService topBarService,
        ISisterhoodConnectionService connectionService,
        UserManager<ApplicationUser> userManager) : base(userManager, userRepository)
    {
        _sisterhoodRepository = sisterhoodRepository;
        _topBarService = topBarService;
        _connectionService = connectionService;
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

        var isAdmin = await IsCurrentUserAdminAsync(cancellationToken);
        if (!isAdmin)
        {
            return Forbid();
        }

        var identityName = User?.Identity?.Name;
        var email = User?.FindFirstValue(ClaimTypes.Email) ?? User?.FindFirstValue("email");
        var domainUser = await _userRepository.GetByIdAsync(currentUserId.Value, cancellationToken);
        var displayName = StringUtilities.ResolveDisplayName(domainUser?.Name, identityName, email);

        var sisterhoods = await _sisterhoodRepository.GetForUserAsync(currentUserId.Value, cancellationToken);
        var connections = await _connectionService.GetConnectionsAsync(currentUserId.Value, cancellationToken);

        var viewModel = new FellowSurfersViewModel
        {
            CurrentUserDisplayName = displayName,
            SisterhoodCount = sisterhoods.Count,
            Connections = connections
        };

        return View("Index", viewModel);
    }
}
