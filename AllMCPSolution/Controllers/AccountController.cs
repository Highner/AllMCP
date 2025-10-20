using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AllMCPSolution.Controllers;

[Route("account")]
public class AccountController : Controller
{
    [AllowAnonymous]
    [HttpGet("signin/google")]
    public IActionResult SignInWithGoogle([FromQuery] string? returnUrl = null)
    {
        var redirectUrl = ResolveReturnUrl(returnUrl);
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
        return Challenge(properties, "Google");
    }

    [AllowAnonymous]
    [HttpGet("signin/microsoft")]
    public IActionResult SignInWithMicrosoft([FromQuery] string? returnUrl = null)
    {
        var redirectUrl = ResolveReturnUrl(returnUrl);
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
        return Challenge(properties, "Microsoft");
    }

    [Authorize]
    [HttpPost("signout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignOutCurrentUser([FromForm] string? returnUrl = null)
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var redirectUrl = ResolveReturnUrl(returnUrl);
        return LocalRedirect(redirectUrl);
    }

    private string ResolveReturnUrl(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return returnUrl;
        }

        return Url.Content("~/wine-surfer");
    }
}
