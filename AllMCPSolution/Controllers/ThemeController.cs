using System;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Services.Theming;
using Microsoft.AspNetCore.Mvc;

namespace AllMCPSolution.Controllers;

[Route("theme")]
public sealed class ThemeController : Controller
{
    private readonly IThemeService _themeService;

    public ThemeController(IThemeService themeService)
    {
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
    }

    [HttpPost("select")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Select([FromForm] string? palette, [FromForm] string? returnUrl, CancellationToken cancellationToken)
    {
        var normalizedPalette = string.IsNullOrWhiteSpace(palette) ? null : palette.Trim();
        if (!string.IsNullOrEmpty(normalizedPalette))
        {
            await _themeService.SetActivePaletteAsync(normalizedPalette, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToAction("Index", "WineSurfer");
    }
}
