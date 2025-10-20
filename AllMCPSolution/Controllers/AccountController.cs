using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AllMCPSolution.Controllers;

[Route("account")]
public class AccountController : Controller
{
    [AllowAnonymous]
    [HttpGet("signin/microsoft")]
    public IActionResult SignInWithMicrosoft([FromQuery] string? returnUrl = null)
    {
        var redirectUrl = ResolveReturnUrl(returnUrl);
        var props = new AuthenticationProperties { RedirectUri = redirectUrl };
        return Challenge(props, OpenIdConnectDefaults.AuthenticationScheme); // <-- key change
    }

    [Authorize]
    [HttpPost("signout")]
    [ValidateAntiForgeryToken]
    public IActionResult SignOutCurrentUser([FromForm] string? returnUrl = null)
    {
        var redirectUrl = ResolveReturnUrl(returnUrl);
        return SignOut(new AuthenticationProperties { RedirectUri = redirectUrl },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme); // sign out cookie + OIDC
    }

    private string ResolveReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : Url.Content("~/wine-surfer");
}