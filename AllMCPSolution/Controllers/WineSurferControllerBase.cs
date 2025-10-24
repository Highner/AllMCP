using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AllMCPSolution.Controllers;

public class WineSurferControllerBase : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    protected readonly IUserRepository _userRepository;
    
    public WineSurferControllerBase(UserManager<ApplicationUser> userManager, IUserRepository userRepository)
    {
        _userManager = userManager;
        _userRepository = userRepository;
    }
    protected Guid? GetCurrentUserId()
    {
        var idValue = _userManager.GetUserId(User);
        return Guid.TryParse(idValue, out var parsedId) ? parsedId : null;
    }
    
    protected async Task<bool> IsCurrentUserAdminAsync(CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return false;
        }

        var user = await _userRepository.GetByIdAsync(currentUserId.Value, cancellationToken);
        return user?.IsAdmin == true;
    }
}