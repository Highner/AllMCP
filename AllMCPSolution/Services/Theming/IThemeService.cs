using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Utilities.Theming;

namespace AllMCPSolution.Services.Theming;

public interface IThemeService
{
    IReadOnlyList<AppColorPalette> GetPalettes();

    AppColorPalette GetActivePalette();

    Task SetActivePaletteAsync(string paletteName, CancellationToken cancellationToken = default);

    IReadOnlyDictionary<string, string> BuildCssVariables();
}
