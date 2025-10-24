using System.Net;
using System.Threading;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AllMCPSolution.Controllers;

[Route("account")]
public class AccountController : Controller
{
    private const string StatusMessageTempDataKey = "Account.StatusMessage";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<AccountController> _logger;
    private readonly IWineSurferTopBarService _topBarService;
    private readonly IUserRepository _userRepository;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<AccountController> logger,
        IWineSurferTopBarService topBarService,
        IUserRepository userRepository)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
        _topBarService = topBarService;
        _userRepository = userRepository;
    }

    [AllowAnonymous]
    [HttpGet("login")]
    public async Task<IActionResult> Login([FromQuery] string? returnUrl = null, [FromQuery] string? error = null)
    {
        if (User?.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(ResolveReturnUrl(returnUrl));
        }

        var model = new LoginViewModel
        {
            ReturnUrl = ResolveReturnUrl(returnUrl),
            ExternalLogins = await GetExternalLoginOptionsAsync()
        };

        if (!string.IsNullOrWhiteSpace(error))
        {
            ViewData["ErrorMessage"] = WebUtility.UrlDecode(error);
        }

        await SetTopBarModelAsync(HttpContext.RequestAborted);
        return View("Login", model);
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        var resolvedReturnUrl = ResolveReturnUrl(model.ReturnUrl);

        if (!ModelState.IsValid)
        {
            model.ReturnUrl = resolvedReturnUrl;
            model.ExternalLogins = await GetExternalLoginOptionsAsync();
            await SetTopBarModelAsync(HttpContext.RequestAborted);
            return View("Login", model);
        }

        var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            return LocalRedirect(resolvedReturnUrl);
        }

        if (result.RequiresTwoFactor)
        {
            ModelState.AddModelError(string.Empty, "Two-factor authentication is not enabled for this application.");
        }
        else if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "This account has been temporarily locked due to too many failed attempts. Try again later.");
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
        }

        model.ReturnUrl = resolvedReturnUrl;
        model.ExternalLogins = await GetExternalLoginOptionsAsync();
        await SetTopBarModelAsync(HttpContext.RequestAborted);
        return View("Login", model);
    }

    [AllowAnonymous]
    [HttpGet("register")]
    public async Task<IActionResult> Register([FromQuery] string? returnUrl = null, [FromQuery] string? email = null)
    {
        if (User?.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(ResolveReturnUrl(returnUrl));
        }

        var prefilledEmail = string.IsNullOrWhiteSpace(email)
            ? string.Empty
            : email.Trim();

        var model = new RegisterViewModel
        {
            ReturnUrl = ResolveReturnUrl(returnUrl),
            Email = prefilledEmail,
            ExternalLogins = await GetExternalLoginOptionsAsync()
        };

        await SetTopBarModelAsync(HttpContext.RequestAborted);
        return View("Register", model);
    }

    [AllowAnonymous]
    [HttpPost("register")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        var resolvedReturnUrl = ResolveReturnUrl(model.ReturnUrl);

        if (!ModelState.IsValid)
        {
            model.ReturnUrl = resolvedReturnUrl;
            model.ExternalLogins = await GetExternalLoginOptionsAsync();
            await SetTopBarModelAsync(HttpContext.RequestAborted);
            return View("Register", model);
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = model.Email,
            Email = model.Email,
            Name = string.IsNullOrWhiteSpace(model.Name) ? model.Email : model.Name.Trim()
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (result.Succeeded)
        {
            await _signInManager.SignInAsync(user, isPersistent: false);
            _logger.LogInformation("User {Email} registered a new account.", model.Email);
            return LocalRedirect(resolvedReturnUrl);
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        model.ReturnUrl = resolvedReturnUrl;
        model.ExternalLogins = await GetExternalLoginOptionsAsync();
        await SetTopBarModelAsync(HttpContext.RequestAborted);
        return View("Register", model);
    }

    [AllowAnonymous]
    [HttpGet("forgot-password")]
    public async Task<IActionResult> ForgotPassword()
    {
        await SetTopBarModelAsync(HttpContext.RequestAborted);
        return View("ForgotPassword", new ForgotPasswordViewModel());
    }

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await SetTopBarModelAsync(HttpContext.RequestAborted);
            return View("ForgotPassword", model);
        }

        string? resetLink = null;
        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user != null)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            resetLink = Url.Action(nameof(ResetPassword), "Account", new { token, email = model.Email }, Request.Scheme, Request.Host.Value);
        }

        var confirmationModel = new PasswordResetNotificationViewModel
        {
            Email = model.Email,
            ResetLink = resetLink
        };

        await SetTopBarModelAsync(HttpContext.RequestAborted);
        return View("ForgotPasswordConfirmation", confirmationModel);
    }

    [AllowAnonymous]
    [HttpGet("reset-password")]
    public async Task<IActionResult> ResetPassword([FromQuery] string? token = null, [FromQuery] string? email = null)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(email))
        {
            return RedirectToAction(nameof(Login));
        }

        var model = new ResetPasswordViewModel
        {
            Token = token,
            Email = email
        };

        await SetTopBarModelAsync(HttpContext.RequestAborted);
        return View("ResetPassword", model);
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await SetTopBarModelAsync(HttpContext.RequestAborted);
            return View("ResetPassword", model);
        }

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user == null)
        {
            return RedirectToAction(nameof(ResetPasswordConfirmation));
        }

        var result = await _userManager.ResetPasswordAsync(user, model.Token, model.Password);
        if (result.Succeeded)
        {
            return RedirectToAction(nameof(ResetPasswordConfirmation));
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        await SetTopBarModelAsync(HttpContext.RequestAborted);
        return View("ResetPassword", model);
    }

    [AllowAnonymous]
    [HttpGet("reset-password-confirmation")]
    public async Task<IActionResult> ResetPasswordConfirmation()
    {
        await SetTopBarModelAsync(HttpContext.RequestAborted);
        return View("ResetPasswordConfirmation");
    }

    [Authorize]
    [HttpGet("change-password")]
    public async Task<IActionResult> ChangePassword()
    {
        if (TempData.TryGetValue(StatusMessageTempDataKey, out var statusMessage))
        {
            ViewData["StatusMessage"] = statusMessage;
        }

        await SetTopBarModelAsync(HttpContext.RequestAborted);
        return View("ChangePassword", new ChangePasswordViewModel());
    }

    [Authorize]
    [HttpPost("change-password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await SetTopBarModelAsync(HttpContext.RequestAborted);
            return View("ChangePassword", model);
        }

        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return RedirectToAction(nameof(Login));
        }

        var result = await _userManager.ChangePasswordAsync(user, model.OldPassword, model.NewPassword);
        if (result.Succeeded)
        {
            await _signInManager.RefreshSignInAsync(user);
            TempData[StatusMessageTempDataKey] = "Your password has been changed.";
            return RedirectToAction(nameof(ChangePassword));
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        await SetTopBarModelAsync(HttpContext.RequestAborted);
        return View("ChangePassword", model);
    }

    [Authorize]
    [HttpPost("profile/display-name")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDisplayName(UpdateAccountDisplayNameViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var trimmedName = model.DisplayName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            ModelState.AddModelError(nameof(model.DisplayName), "Display name cannot be empty.");
            return ValidationProblem(ModelState);
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        try
        {
            var updatedUser = await _userRepository.UpdateDisplayNameAsync(user.Id, trimmedName, HttpContext.RequestAborted);
            if (updatedUser is null)
            {
                return NotFound();
            }

            await _signInManager.RefreshSignInAsync(updatedUser);

            var avatarLetter = string.IsNullOrWhiteSpace(trimmedName)
                ? "?"
                : trimmedName[..1].ToUpperInvariant();
            var accountLabel = string.IsNullOrWhiteSpace(trimmedName)
                ? "Guest account"
                : $"{trimmedName}'s account";
            var menuName = string.IsNullOrWhiteSpace(trimmedName) ? "Account" : trimmedName;

            return Ok(new
            {
                displayName = trimmedName,
                menuName,
                avatarLetter,
                accountLabel
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Failed to update display name for user {UserId}.", user.Id);
            return BadRequest(new { error = "We couldn't update your display name. Please try again." });
        }
    }

    [AllowAnonymous]
    [HttpGet("signin/external")]
    public async Task<IActionResult> ExternalLogin([FromQuery] string scheme, [FromQuery] string? returnUrl = null)
    {
        if (string.IsNullOrWhiteSpace(scheme))
        {
            return BadRequest();
        }

        if (IsUnsupportedExternalScheme(scheme))
        {
            return NotFound();
        }

        var externalOptions = await _signInManager.GetExternalAuthenticationSchemesAsync();
        var provider = externalOptions.FirstOrDefault(option =>
            string.Equals(option.Name, scheme, StringComparison.Ordinal));

        if (provider == null)
        {
            return NotFound();
        }

        var redirectUrl = ResolveReturnUrl(returnUrl);
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider.Name, redirectUrl);
        return Challenge(properties, provider.Name);
    }

    [AllowAnonymous]
    [HttpGet("logout")]
    public async Task<IActionResult> LogoutGet([FromQuery] string? returnUrl = null)
    {
        var redirectUrl = Url.Content("~/wine-surfer?signedOut=1");

        // Sign out of Identity (application and external) explicitly
        await _signInManager.SignOutAsync();
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        // Best-effort explicit cookie cleanup to ensure no stale auth remains (ensure Path=/)
        var cookiesToDelete = new[]
        {
            ".AspNetCore.Identity.Application",
            ".AspNetCore.Identity.External",
            ".AspNetCore.Cookies",
            "Identity.TwoFactorUserId"
        };
        foreach (var cookie in cookiesToDelete)
        {
            if (Request.Cookies.ContainsKey(cookie))
            {
                Response.Cookies.Delete(cookie, new CookieOptions
                {
                    Path = "/",
                    Expires = DateTimeOffset.UnixEpoch,
                    HttpOnly = true,
                    Secure = HttpContext.Request.IsHttps
                });
            }
        }

        Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0";
        Response.Headers["Pragma"] = "no-cache";

        return LocalRedirect(redirectUrl);
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Logout([FromForm] string? returnUrl = null)
    {
        var redirectUrl = Url.Content("~/wine-surfer?signedOut=1");

        // Sign out of Identity (application and external) explicitly
        await _signInManager.SignOutAsync();
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        // Best-effort explicit cookie cleanup to ensure no stale auth remains (ensure Path=/)
        var cookiesToDelete = new[]
        {
            ".AspNetCore.Identity.Application",
            ".AspNetCore.Identity.External",
            ".AspNetCore.Cookies",
            "Identity.TwoFactorUserId"
        };
        foreach (var cookie in cookiesToDelete)
        {
            if (Request.Cookies.ContainsKey(cookie))
            {
                Response.Cookies.Delete(cookie, new CookieOptions
                {
                    Path = "/",
                    Expires = DateTimeOffset.UnixEpoch,
                    HttpOnly = true,
                    Secure = HttpContext.Request.IsHttps
                });
            }
        }

        // For safety, instruct clients/proxies not to cache the previous authenticated response
        Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0";
        Response.Headers["Pragma"] = "no-cache";

        // If this is an AJAX request, return a structured payload instead of a redirect
        var isAjax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        if (isAjax)
        {
            return Ok(new { redirectUrl });
        }

        return LocalRedirect(redirectUrl);
    }

    private async Task<IReadOnlyList<ExternalLoginOption>> GetExternalLoginOptionsAsync()
    {
        var schemes = await _signInManager.GetExternalAuthenticationSchemesAsync();
        return schemes
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .Where(s => !IsUnsupportedExternalScheme(s.Name))
            .Select(s => new ExternalLoginOption
            {
                AuthenticationScheme = s.Name,
                DisplayName = string.IsNullOrWhiteSpace(s.DisplayName) ? s.Name : s.DisplayName
            })
            .OrderBy(s => s.DisplayName)
            .ToList();
    }

    private static bool IsUnsupportedExternalScheme(string? scheme) =>
        !string.IsNullOrWhiteSpace(scheme) &&
        (string.Equals(scheme, "Google", StringComparison.OrdinalIgnoreCase)
         || string.Equals(scheme, "OpenIdConnect", StringComparison.OrdinalIgnoreCase));

    private string ResolveReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : Url.Content("~/wine-surfer");

    private async Task SetTopBarModelAsync(CancellationToken cancellationToken)
    {
        var currentPath = HttpContext?.Request?.Path.Value ?? string.Empty;
        ViewData["WineSurferTopBarModel"] = await _topBarService.BuildAsync(User, currentPath, cancellationToken);
    }
}
