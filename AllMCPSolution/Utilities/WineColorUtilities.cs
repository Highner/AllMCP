using AllMCPSolution.Models;

namespace AllMCPSolution.Utilities;

public static class WineColorUtilities
{
    public static bool TryParse(string? value, out WineColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (Enum.TryParse(trimmed, true, out color))
        {
            return true;
        }

        var normalized = trimmed
            .Replace('é', 'e')
            .Replace('É', 'E');

        return Enum.TryParse(normalized, true, out color);
    }
}
