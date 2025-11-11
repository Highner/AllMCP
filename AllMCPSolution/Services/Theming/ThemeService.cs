using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Utilities.Theming;
using Microsoft.AspNetCore.Http;

namespace AllMCPSolution.Services.Theming;

public sealed class ThemeService : IThemeService
{
    private const string ThemeCookieName = "wine-surfer-theme";
    private const string ActivePaletteContextKey = "__WineSurfer.ActivePalette";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IReadOnlyList<AppColorPalette> _palettes;
    private readonly Dictionary<string, AppColorPalette> _paletteLookup;
    private readonly AppColorPalette _defaultPalette;

    public ThemeService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _palettes = AppColorPalettes.DefaultPalettes;
        _paletteLookup = _palettes
            .ToDictionary(palette => palette.Name, StringComparer.OrdinalIgnoreCase);
        _defaultPalette = _palettes.FirstOrDefault(palette => palette.IsDefault)
            ?? throw new InvalidOperationException("At least one color palette must be marked as the default.");
    }

    public IReadOnlyList<AppColorPalette> GetPalettes() => _palettes;

    public AppColorPalette GetActivePalette()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return _defaultPalette;
        }

        if (httpContext.Items.TryGetValue(ActivePaletteContextKey, out var existing) &&
            existing is AppColorPalette cached)
        {
            return cached;
        }

        var cookieValue = httpContext.Request.Cookies.TryGetValue(ThemeCookieName, out var candidate)
            ? candidate
            : null;

        var palette = ResolvePaletteOrDefault(cookieValue);
        httpContext.Items[ActivePaletteContextKey] = palette;
        return palette;
    }

    public Task SetActivePaletteAsync(string paletteName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("Cannot change the active palette outside the scope of an HTTP request.");

        var palette = ResolvePaletteOrDefault(paletteName);

        var options = new CookieOptions
        {
            HttpOnly = false,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = httpContext.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddYears(1)
        };

        httpContext.Response.Cookies.Append(ThemeCookieName, palette.Name, options);
        httpContext.Items[ActivePaletteContextKey] = palette;
        return Task.CompletedTask;
    }

    public IReadOnlyDictionary<string, string> BuildCssVariables()
    {
        var palette = GetActivePalette();
        return palette.CssVariables;
    }

    private AppColorPalette ResolvePaletteOrDefault(string? paletteName)
    {
        if (!string.IsNullOrWhiteSpace(paletteName) &&
            _paletteLookup.TryGetValue(paletteName, out var palette))
        {
            return palette;
        }

        return _defaultPalette;
    }
}
